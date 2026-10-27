using System;

namespace HollowLines.Core
{
    /// <summary>
    /// The Cave-in (v3.3, ceiling-gas.md) — the well collapsing behind the player. Replaces the air clock.
    ///
    /// The front (CeilingRow) slides down at Speed rows/s. The gap between it and the avatar is the LEAD,
    /// and the lead is the new air: capped at MaxLead (the leash — a fast player can't bank a lead, exactly
    /// like air capped at 100 %), shown as a % on the HUD bar, ×2 Danger Zone under 15 %. When the front
    /// reaches the avatar's row it strikes (AvatarReached) and recoils HitRecoil rows, so one hit is never
    /// a guaranteed second one once the i-frames end (decision C).
    ///
    /// Rows the front passes are SWALLOWED for good: RowsSwallowed tells the caller which absolute rows to
    /// turn into rubble. The front itself may recoil back up into the rubble (bursts, bombs, a hit) — that
    /// buys time before the next row goes, never space (ceiling-gas.md §3.3).
    ///
    /// Rows are ABSOLUTE (Endless depth, not a board row), so the front crosses a segment seam untouched.
    /// Knows nothing about the grid or the hearts — GameBootstrap converts rows and takes the heart.
    /// </summary>
    public sealed class CeilingSystem
    {
        // ── Tunables ────────────────────────────────────────────────
        // Starting values are the air balance translated (ceiling-gas.md §2.2): time to be caught standing
        // still = MaxLead / speed = 100 % / drain, so speed = drain × MaxLead / 100. Provisional until the
        // harness measures them (R8.7).
        public const float DefaultMaxLead = 12f;   // rows (decision D) — ¾ of the 16-row view
        public const float DefaultSpeed   = 0.6f;  // rows/s (= endless start drain 5 %/s)
        public const float HitRecoil      = 4f;    // rows the front backs off after striking (decision C)

        // Recoils = the old air restores, same proportions (restore % × MaxLead / 100), rounded up to
        // something a player can notice. Drilling recoils nothing: drilling DOWN is already +1 row of lead.
        public const float BurstRecoil            = 0.1f;
        public const float BombChainRecoilPerBomb = 0.12f;
        public const float PerfectClearRecoil     = 0.75f; // also pays back the row ShiftDown costs (§5.2)
        public const float PowerDrillRecoil       = 0.5f;  // new: the ×6 pushes the well back

        /// <summary>Danger Zone below this share of MaxLead, in % — same threshold as the air's.</summary>
        public const float DangerZoneThreshold = 15f;

        // R6.2 start buffer, carried over unchanged: the front waits `grace` seconds, then ramps from
        // StartSpeedFactor to full speed. Opt-in via BeginStartBuffer(); never at an Endless seam.
        public const float DefaultStartGrace = 3f;
        public const float StartRampDuration = 12f;
        public const float StartSpeedFactor  = 0.5f;

        /// <summary>The front speed that gives the same standing-still time as an air drain of `drainPercent` %/s.</summary>
        public static float SpeedForDrainRate(float drainPercent, float maxLead = DefaultMaxLead) =>
            Math.Max(0f, drainPercent) * maxLead / 100f;

        /// <summary>Front speed in rows per second. Campaign / Endless assign it, like AirSystem.DrainRate.</summary>
        public float Speed { get; set; } = DefaultSpeed;

        /// <summary>The leash: the lead can never exceed this many rows.</summary>
        public float MaxLead { get; }

        // ── State ────────────────────────────────────────────────────
        /// <summary>The front, as an absolute row (float). The avatar is struck when this reaches its row.</summary>
        public float CeilingRow { get; private set; }

        /// <summary>Every absolute row below this value (exclusive) has been swallowed — it is rubble now.</summary>
        public int SwallowedRows { get; private set; }

        /// <summary>Rows between the front and the avatar, clamped to [0, MaxLead]. MaxLead before Reset.</summary>
        public float Lead => _hasAvatar ? Clamp(_avatarRow - CeilingRow, 0f, MaxLead) : MaxLead;

        /// <summary>Lead as a % of MaxLead — what the HUD bar shows in place of air.</summary>
        public float LeadPercent => Lead / MaxLead * 100f;

        /// <summary>Lead under DangerZoneThreshold % — ScoreSystem doubles base points (§M3.4).</summary>
        public bool IsDangerZone => LeadPercent < DangerZoneThreshold;

        /// <summary>True while the start buffer's still window is running.</summary>
        public bool InStartGrace => _bufferActive && _bufferTime < _startGrace;

        /// <summary>Seconds left in the still window (0 outside it).</summary>
        public float StartGraceRemaining => InStartGrace ? _startGrace - _bufferTime : 0f;

        /// <summary>Speed multiplier right now: 0 in the grace window, ramping to 1, then 1.</summary>
        public float StartBufferFactor
        {
            get
            {
                if (!_bufferActive) return 1f;
                float u = _bufferTime - _startGrace;
                if (u < 0f) return 0f;
                if (u >= StartRampDuration) return 1f;
                return StartSpeedFactor + (1f - StartSpeedFactor) * (u / StartRampDuration);
            }
        }

        // ── Events ───────────────────────────────────────────────────
        /// <summary>
        /// Absolute rows [fromRow, toRow) were just swallowed — turn them into rubble. Fired at most once
        /// per Tick / NotifyAvatarRow, with every row the front (or the leash) passed since the last one.
        /// </summary>
        public event Action<int, int> RowsSwallowed;

        /// <summary>
        /// The front reached the avatar's row. It has ALREADY recoiled HitRecoil rows when this fires. Fires
        /// again on every new contact — the caller's i-frames make repeats harmless (same as a Crawler).
        /// </summary>
        public event Action AvatarReached;

        /// <summary>Lead changed, in % of MaxLead — the HUD bar.</summary>
        public event Action<float> LeadChanged;

        /// <summary>Fired only when IsDangerZone flips (true = entered), after LeadChanged.</summary>
        public event Action<bool> DangerZoneChanged;

        private bool  _hasAvatar;
        private int   _avatarRow;
        private float _lastLead = -1f;
        private bool  _wasDangerZone;

        private bool  _bufferActive;
        private float _bufferTime;
        private float _startGrace = DefaultStartGrace;

        public CeilingSystem(float maxLead = DefaultMaxLead)
        {
            MaxLead = maxLead > 0f ? maxLead : DefaultMaxLead; // defensive: a zero leash would divide by 0
        }

        // ── Public API ───────────────────────────────────────────────

        /// <summary>
        /// New run: the front starts a full leash above the avatar, nothing swallowed yet. Rows above the
        /// starting front are treated as already gone (they are spawn air, not play space), silently.
        /// Also cancels any running start buffer.
        /// </summary>
        public void Reset(int avatarRow)
        {
            _hasAvatar     = true;
            _avatarRow     = avatarRow;
            CeilingRow     = avatarRow - MaxLead;
            SwallowedRows  = FloorToInt(CeilingRow);
            _bufferActive  = false;
            _bufferTime    = 0f;
            NotifyLeadChanged();
        }

        /// <summary>Open a board with the start buffer (R6.2): still for `grace` s, then a ramp to full speed.</summary>
        public void BeginStartBuffer(float grace = DefaultStartGrace)
        {
            _bufferActive = true;
            _bufferTime   = 0f;
            _startGrace   = Math.Max(0f, grace);
        }

        /// <summary>
        /// Call once per frame (skip it once the level is won). Order: advance → leash → contact → swallow,
        /// so the avatar's own row is never swallowed under it.
        /// </summary>
        public void Tick(float dt, int avatarRow)
        {
            if (dt > 0f)
            {
                float seconds = dt;
                if (_bufferActive)
                {
                    float t0 = _bufferTime;
                    _bufferTime += dt;
                    seconds = BufferedSeconds(_bufferTime) - BufferedSeconds(t0);
                    if (_bufferTime >= _startGrace + StartRampDuration)
                        _bufferActive = false;
                }
                CeilingRow += Math.Max(0f, Speed) * seconds;
            }

            Resolve(avatarRow);
        }

        /// <summary>
        /// The avatar moved without the front advancing (freefall, a respawn, an Endless seam): apply the
        /// leash, contact and swallowing at the new row.
        /// </summary>
        public void NotifyAvatarRow(int avatarRow) => Resolve(avatarRow);

        /// <summary>
        /// Push the front back `rows`. It may climb back into the rubble (time, not space), but never past
        /// the leash — a lead beyond MaxLead would be yanked straight back on the next tick anyway.
        /// Non-positive amounts are ignored (defensive, like AirSystem.Restore).
        /// </summary>
        public void Recoil(float rows)
        {
            if (rows <= 0f)
                return;

            float floor = _hasAvatar ? _avatarRow - MaxLead : float.NegativeInfinity;
            CeilingRow = Math.Max(CeilingRow - rows, floor);
            NotifyLeadChanged();
        }

        public void RestoreBurst()        => Recoil(BurstRecoil);
        public void RestorePerfectClear() => Recoil(PerfectClearRecoil);
        public void RestorePowerDrill()   => Recoil(PowerDrillRecoil);

        /// <summary>Per bomb in a sympathetic chain. Non-positive counts recoil nothing.</summary>
        public void RestoreBombChain(int bombCount)
        {
            if (bombCount <= 0)
                return;
            Recoil(BombChainRecoilPerBomb * bombCount);
        }

        /// <summary>
        /// A Perfect Clear shifted every row above the cleared one down by `rows` — the rubble included,
        /// so the front and the swallowed boundary move with it (ceiling-gas.md §5.2). No RowsSwallowed:
        /// the grid already holds rubble there. Contact is checked on the next Tick.
        /// </summary>
        public void ShiftDown(int rows = 1)
        {
            if (rows <= 0)
                return;
            CeilingRow    += rows;
            SwallowedRows += rows;
            NotifyLeadChanged();
        }

        // ── Private ──────────────────────────────────────────────────

        private void Resolve(int avatarRow)
        {
            _hasAvatar = true;
            _avatarRow = avatarRow;

            // Leash: never more than MaxLead behind. A fast descent drags the front down with it.
            if (avatarRow - CeilingRow > MaxLead)
                CeilingRow = avatarRow - MaxLead;

            // Contact BEFORE swallowing: the strike recoils the front, then only the rows above the new
            // front go — the avatar's row is never turned to rubble under it.
            if (CeilingRow >= avatarRow)
            {
                CeilingRow = avatarRow - HitRecoil;
                AvatarReached?.Invoke();
            }

            int target = Math.Min(FloorToInt(CeilingRow), avatarRow);
            if (target > SwallowedRows)
            {
                int from = SwallowedRows;
                SwallowedRows = target;
                RowsSwallowed?.Invoke(from, target);
            }

            NotifyLeadChanged();
        }

        /// <summary>∫ StartBufferFactor dt from the buffer start to t — same integral as AirSystem's.</summary>
        private float BufferedSeconds(float t)
        {
            float u = t - _startGrace;
            if (u <= 0f) return 0f;

            float slope = (1f - StartSpeedFactor) / StartRampDuration;
            if (u < StartRampDuration)
                return StartSpeedFactor * u + 0.5f * slope * u * u;

            float rampArea = StartSpeedFactor * StartRampDuration + 0.5f * slope * StartRampDuration * StartRampDuration;
            return rampArea + (u - StartRampDuration);
        }

        private void NotifyLeadChanged()
        {
            float lead = Lead;
            if (lead != _lastLead)
            {
                _lastLead = lead;
                LeadChanged?.Invoke(LeadPercent);
            }

            bool danger = IsDangerZone;
            if (danger == _wasDangerZone)
                return;
            _wasDangerZone = danger;
            DangerZoneChanged?.Invoke(danger);
        }

        private static int FloorToInt(float v) => (int)Math.Floor(v);

        private static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    }
}
