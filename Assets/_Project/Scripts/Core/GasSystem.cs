using System;
using System.Collections.Generic;

namespace HollowLines.Core
{
    /// <summary>
    /// Gas pockets (v3.3, ceiling-gas.md §4) — the danger that replaces the air capsule.
    ///
    /// A pocket the avatar drills (or a burst / bomb frees) HISSES for HissDuration — the telegraph,
    /// same 0.6 s as the wobble (rule 4) — then vents a cloud at its cell. The cloud RISES: every
    /// StepInterval each puff moves up into an empty cell, or sideways when the way up is blocked, and
    /// the vent keeps emitting until the cloud holds CloudVolume puffs. After CloudLifetime it is gone.
    ///
    /// Touching a puff fires AvatarHitByGas — every overlapping Tick, like a Crawler; the caller's
    /// i-frames make repeats harmless. Because the gas rises, the only refuge is DOWN: keep drilling and
    /// you are a row or two below the vent when it opens; stop and it takes you (rule 6 / rule 10).
    ///
    /// Gas lives in EMPTY cells only and never touches the grid. A puff whose cell turns solid (a chunk
    /// fell through it, the Cave-in swallowed it) is simply gone — that is how the rubble absorbs gas.
    /// A pocket whose cell is solid again when its hiss ends never vents.
    /// </summary>
    public sealed class GasSystem
    {
        // ── Tunables (provisional — R8.7 measures them) ─────────────
        public const float HissDuration  = 0.6f;  // telegraph before the vent opens (= the wobble)
        public const float StepInterval  = 0.25f; // a puff moves one cell per step
        public const int   CloudVolume   = 4;     // puffs per cloud
        public const float CloudLifetime = 3f;    // seconds from the vent opening to the cloud fading

        // ── Events ───────────────────────────────────────────────────
        /// <summary>A pocket started hissing at this cell (the telegraph). Fired by Release.</summary>
        public event Action<GridPos> PocketHissing;

        /// <summary>A hiss ended and the vent opened here — the first puff is already in the cell.</summary>
        public event Action<GridPos> CloudReleased;

        /// <summary>A cloud reached its lifetime and faded. Argument = the vent it came from.</summary>
        public event Action<GridPos> CloudDissipated;

        /// <summary>The avatar's cell holds a puff. Fires on every Tick it overlaps.</summary>
        public event Action<GridPos> AvatarHitByGas;

        private sealed class Cloud
        {
            public GridPos Vent;
            public readonly List<GridPos> Puffs = new List<GridPos>();
            public float Age;
            public float StepClock;
            public int   Emitted;
            public int   Steps;
        }

        private readonly GridModel _grid;
        private readonly Dictionary<GridPos, float> _hissing = new Dictionary<GridPos, float>(); // vent → time left
        private readonly List<Cloud> _clouds = new List<Cloud>();
        private readonly HashSet<GridPos> _occupied = new HashSet<GridPos>(); // every puff, all clouds
        private readonly List<GridPos> _scratch = new List<GridPos>();

        public GasSystem(GridModel grid)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        }

        // ── Queries ──────────────────────────────────────────────────

        /// <summary>A puff sits in this cell right now.</summary>
        public bool IsGas(GridPos pos) => _occupied.Contains(pos);

        /// <summary>A pocket is hissing at this cell (vent not open yet).</summary>
        public bool IsHissing(GridPos pos) => _hissing.ContainsKey(pos);

        /// <summary>Anything still to resolve — a hiss or a cloud. The level-end settle can wait on it.</summary>
        public bool HasActiveGas => _hissing.Count > 0 || _clouds.Count > 0;

        /// <summary>Every puff, appended to <paramref name="into"/> (not cleared) — graze / view.</summary>
        public void CopyGasCells(List<GridPos> into) => into.AddRange(_occupied);

        /// <summary>Every hissing vent, appended to <paramref name="into"/> (not cleared).</summary>
        public void CopyHissingCells(List<GridPos> into) => into.AddRange(_hissing.Keys);

        // ── API ──────────────────────────────────────────────────────

        /// <summary>
        /// A pocket was drilled or freed at <paramref name="vent"/>: start its hiss. The caller has already
        /// emptied the cell (drill / blast / shockwave). Returns false — and does nothing — out of bounds
        /// or when that vent is already hissing.
        /// </summary>
        public bool Release(GridPos vent)
        {
            if (!_grid.InBounds(vent) || _hissing.ContainsKey(vent))
                return false;

            _hissing[vent] = HissDuration;
            PocketHissing?.Invoke(vent);
            return true;
        }

        /// <summary>Hisses, cloud movement and lifetime, then the avatar contact check. Call once per frame.</summary>
        public void Tick(float dt, GridPos avatarCell)
        {
            if (dt > 0f)
            {
                // Clouds first: a vent that opens this frame is advanced only by its own overshoot.
                TickClouds(dt);
                TickHisses(dt);
            }

            DropSqueezedPuffs();

            if (_occupied.Contains(avatarCell))
                AvatarHitByGas?.Invoke(avatarCell);
        }

        /// <summary>Forget every hiss and cloud (new board / segment).</summary>
        public void Clear()
        {
            _hissing.Clear();
            _clouds.Clear();
            _occupied.Clear();
        }

        // ── Private ──────────────────────────────────────────────────

        private void TickHisses(float dt)
        {
            if (_hissing.Count == 0)
                return;

            _scratch.Clear();
            _scratch.AddRange(_hissing.Keys);
            foreach (GridPos vent in _scratch)
            {
                float left = _hissing[vent] - dt;
                if (left > 0f)
                {
                    _hissing[vent] = left;
                    continue;
                }

                _hissing.Remove(vent);
                OpenVent(vent, overshoot: -left);
            }
        }

        private void OpenVent(GridPos vent, float overshoot)
        {
            // Filled again during the hiss (a chunk landed, the rubble took it): the pocket is smothered.
            if (_grid.IsSolid(vent))
                return;

            var cloud = new Cloud { Vent = vent };
            _clouds.Add(cloud);
            Emit(cloud);
            CloudReleased?.Invoke(vent);

            // The part of the frame after the vent opened still counts toward the cloud's life and steps.
            if (overshoot > 0f)
                AdvanceCloud(cloud, overshoot);
        }

        private void TickClouds(float dt)
        {
            // Backwards: AdvanceCloud may remove the cloud it is given.
            for (int i = _clouds.Count - 1; i >= 0; i--)
                AdvanceCloud(_clouds[i], dt);
        }

        private void AdvanceCloud(Cloud cloud, float dt)
        {
            cloud.Age       += dt;
            cloud.StepClock += dt;

            while (cloud.StepClock >= StepInterval && cloud.Age < CloudLifetime)
            {
                cloud.StepClock -= StepInterval;
                Step(cloud);
            }

            if (cloud.Age >= CloudLifetime)
            {
                foreach (GridPos p in cloud.Puffs)
                    _occupied.Remove(p);
                cloud.Puffs.Clear();
                _clouds.Remove(cloud);
                CloudDissipated?.Invoke(cloud.Vent);
            }
        }

        /// <summary>
        /// One step: every puff tries UP, else sideways; then the vent emits if the cloud isn't full.
        /// Topmost puffs move first so a column rises as one instead of blocking itself. The sideways
        /// preference alternates every step, so a cloud under a ceiling spreads both ways evenly.
        /// </summary>
        private void Step(Cloud cloud)
        {
            cloud.Puffs.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
            int side = (cloud.Steps++ % 2 == 0) ? -1 : 1;

            for (int i = 0; i < cloud.Puffs.Count; i++)
            {
                GridPos from = cloud.Puffs[i];
                GridPos to = from;

                if (IsFree(from.Above))                       to = from.Above;
                else if (IsFree(from.Offset(side, 0)))        to = from.Offset(side, 0);
                else if (IsFree(from.Offset(-side, 0)))       to = from.Offset(-side, 0);

                if (to == from)
                    continue;

                _occupied.Remove(from);
                _occupied.Add(to);
                cloud.Puffs[i] = to;
            }

            Emit(cloud);
        }

        private void Emit(Cloud cloud)
        {
            if (cloud.Emitted >= CloudVolume || !IsFree(cloud.Vent))
                return;

            cloud.Puffs.Add(cloud.Vent);
            _occupied.Add(cloud.Vent);
            cloud.Emitted++;
        }

        /// <summary>In bounds, empty, and no puff there yet (from any cloud).</summary>
        private bool IsFree(GridPos p) => _grid.InBounds(p) && !_grid.IsSolid(p) && !_occupied.Contains(p);

        /// <summary>A puff whose cell turned solid is gone — a chunk crushed it or the rubble absorbed it.</summary>
        private void DropSqueezedPuffs()
        {
            if (_occupied.Count == 0)
                return;

            foreach (Cloud cloud in _clouds)
            {
                for (int i = cloud.Puffs.Count - 1; i >= 0; i--)
                {
                    GridPos p = cloud.Puffs[i];
                    if (!_grid.IsSolid(p))
                        continue;
                    cloud.Puffs.RemoveAt(i);
                    _occupied.Remove(p);
                }
            }
        }
    }
}
