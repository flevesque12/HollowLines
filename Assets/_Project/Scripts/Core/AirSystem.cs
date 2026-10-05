using System;

namespace HollowLines.Core
{
    /// <summary>
    /// The suffocation clock.
    ///
    /// Air drains constantly, and v3 pays it back for aggression (design rule 5): capsules,
    /// chunk bursts and bomb chains all restore air, so passive play suffocates. Perfect Clear
    /// still gives the biggest single top-up, but it is now a rare bonus rather than the loop.
    ///
    /// AirDepleted fires exactly once per run — the game loop handles the consequence (game over screen).
    /// Reset() clears the flag so the next run can deplete again.
    /// </summary>
    public sealed class AirSystem
    {
        // ── Tunables ────────────────────────────────────────────────
        public const float MaxAir           = 100f; // percentage
        public const float DefaultDrainRate = 5f;   // % per second (endless / debug boards)

        // Restore values (§15.1). The v3 draft numbers (+25 capsule / +5 burst / +3 per bomb /
        // +15 perfect clear) assumed these were RARE events. Measured on a real board they are not:
        // a single level-10 descent produced 78 bursts and 34 capsules, for +1240 % of air income
        // against 62 % of drain — a 20× oversupply that made the suffocation clock decorative.
        // Scaled so aggression offsets a meaningful share of the drain without erasing it.
        public const float PerfectClearRestoreAmount = 6f;   // % per Perfect Clear
        public const float CapsuleRestoreAmount      = 6f;   // % per air capsule (drilled or liberated)
        public const float BurstRestoreAmount        = 0.5f; // % per chunk burst
        public const float BombChainRestorePerBomb   = 1f;   // % per bomb in a sympathetic chain

        // v3.1 arcade pivot (§8, design rule 1/5): every drill pays a little air back — the core
        // survival loop is "drill to breathe", not a bonus for hitting a special cell type.
        public const float DrillRestoreAmount = 0.5f; // % per drill (any successful drill)

        // R6.2 (F02) start buffer: a new player spends the first seconds of a board reading it, not
        // drilling, and drilling is the main air source since v3.1 — so the clock was hitting hardest
        // exactly when the player was least able to answer it. A board now opens with a grace window
        // (no drain at all), then the drain ramps linearly from StartDrainFactor up to the full rate.
        // Opt-in via BeginStartBuffer(): without it the drain is the full rate from frame 0.
        public const float DefaultStartGrace = 3f;    // seconds of zero drain
        public const float StartRampDuration = 12f;   // seconds to go from StartDrainFactor to 1
        public const float StartDrainFactor  = 0.5f;  // drain multiplier when the ramp begins

        /// <summary>
        /// Drain in % per second. Campaign overrides this per level (CampaignManager.DrainRateForLevel);
        /// endless and debug boards keep the default.
        /// </summary>
        public float DrainRate { get; set; } = DefaultDrainRate;

        // ── State ────────────────────────────────────────────────────
        public float Air { get; private set; } = MaxAir;
        public bool IsEmpty => Air <= 0f;

        /// <summary>True while the start buffer's zero-drain window is running.</summary>
        public bool InStartGrace => _bufferActive && _bufferTime < _startGrace;

        /// <summary>Seconds left in the zero-drain window (0 outside it) — the HUD's grace countdown.</summary>
        public float StartGraceRemaining => InStartGrace ? _startGrace - _bufferTime : 0f;

        /// <summary>Length of the grace window last opened by BeginStartBuffer (for a 0-1 countdown).</summary>
        public float StartGraceDuration => _startGrace;

        /// <summary>Drain multiplier right now: 0 in the grace window, ramping to 1, then 1.</summary>
        public float StartBufferFactor
        {
            get
            {
                if (!_bufferActive) return 1f;
                float u = _bufferTime - _startGrace;
                if (u < 0f) return 0f;
                if (u >= StartRampDuration) return 1f;
                return StartDrainFactor + (1f - StartDrainFactor) * (u / StartRampDuration);
            }
        }

        /// <summary>The drain actually applied this instant, in % per second (DrainRate × buffer).</summary>
        public float EffectiveDrainRate => DrainRate * StartBufferFactor;

        // ── Events ───────────────────────────────────────────────────
        /// <summary>Fired once when Air first reaches 0. Subscribe here for game-over logic.</summary>
        public event Action AirDepleted;

        /// <summary>Fired whenever Air changes — view updates the HUD bar here.</summary>
        public event Action<float> AirChanged;

        private bool _depleted;

        // Start buffer clock. Seconds since BeginStartBuffer; once it passes the end of the ramp the
        // buffer switches itself off, so a long run pays nothing for it.
        private bool  _bufferActive;
        private float _bufferTime;
        private float _startGrace = DefaultStartGrace;

        // ── Public API ───────────────────────────────────────────────

        /// <summary>
        /// Open a board with the start buffer (R6.2): `grace` seconds of zero drain, then a
        /// StartRampDuration ramp from StartDrainFactor to the full rate. Call at the start of every
        /// fresh board — NOT at an endless seam, which is the same run continuing. A negative grace
        /// clamps to 0 (ramp only).
        /// </summary>
        public void BeginStartBuffer(float grace = DefaultStartGrace)
        {
            _bufferActive = true;
            _bufferTime   = 0f;
            _startGrace   = Math.Max(0f, grace);
        }

        /// <summary>Call once per frame. Drains air; fires AirDepleted the first time Air hits 0.</summary>
        public void Tick(float dt)
        {
            if (Air <= 0f || dt <= 0f)
                return;

            // Integrate the buffer factor over [t, t+dt] instead of sampling it, so a long frame that
            // straddles the end of the grace window drains exactly what it should.
            float drainSeconds = dt;
            if (_bufferActive)
            {
                float t0 = _bufferTime;
                _bufferTime += dt;
                drainSeconds = BufferedSeconds(_bufferTime) - BufferedSeconds(t0);
                if (_bufferTime >= _startGrace + StartRampDuration)
                    _bufferActive = false;
            }

            if (drainSeconds <= 0f)
                return; // grace window: nothing drains, nothing to report

            Air = Math.Max(0f, Air - DrainRate * drainSeconds);
            AirChanged?.Invoke(Air);

            if (Air <= 0f && !_depleted)
            {
                _depleted = true;
                AirDepleted?.Invoke();
            }
        }

        /// <summary>+15% air for a full-void row. Wire to CollapseSystem.PerfectClear.</summary>
        public void RestorePerfectClear() => Restore(PerfectClearRestoreAmount);

        /// <summary>
        /// +25% air for a capsule, whether the avatar drilled it or a blast/shockwave freed it.
        /// Wire to AvatarModel.Drilled, BombSystem.AirCapsuleLiberated and GravitySystem.AirCapsuleLiberated.
        /// </summary>
        public void RestoreCapsule() => Restore(CapsuleRestoreAmount);

        /// <summary>+5% air for shattering a chunk on impact. Wire to GravitySystem.ChunkBurst.</summary>
        public void RestoreBurst() => Restore(BurstRestoreAmount);

        /// <summary>+0.5% air for every successful drill. Wire to AvatarModel.Drilled.</summary>
        public void RestoreDrill() => Restore(DrillRestoreAmount);

        /// <summary>
        /// +3% air per bomb in a sympathetic chain. Wire to BombSystem.BombScored when chainMult > 1.
        /// Non-positive counts restore nothing (defensive — no negative air from a bad caller).
        /// </summary>
        public void RestoreBombChain(int bombCount)
        {
            if (bombCount <= 0)
                return;
            Restore(BombChainRestorePerBomb * bombCount);
        }

        /// <summary>Reset to full air for a new run. Also cancels any running start buffer.</summary>
        public void Reset()
        {
            Air = MaxAir;
            _depleted = false;
            _bufferActive = false;
            _bufferTime   = 0f;
            AirChanged?.Invoke(Air);
        }

        // ── Private ──────────────────────────────────────────────────

        /// <summary>
        /// ∫ StartBufferFactor dt from the buffer start to t: "full-rate seconds" elapsed so far.
        /// 0 through the grace window, then the ramp's area (u/2 + u²/4R for factor 0.5→1), then 1:1.
        /// </summary>
        private float BufferedSeconds(float t)
        {
            float u = t - _startGrace;
            if (u <= 0f) return 0f;

            float slope = (1f - StartDrainFactor) / StartRampDuration;
            if (u < StartRampDuration)
                return StartDrainFactor * u + 0.5f * slope * u * u;

            float rampArea = StartDrainFactor * StartRampDuration + 0.5f * slope * StartRampDuration * StartRampDuration;
            return rampArea + (u - StartRampDuration);
        }

        private void Restore(float amount)
        {
            if (amount <= 0f || Air >= MaxAir)
                return;

            float before = Air;
            Air = Math.Min(MaxAir, Air + amount);
            if (Air != before)
                AirChanged?.Invoke(Air);
        }
    }
}
