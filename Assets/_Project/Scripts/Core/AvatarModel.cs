using System;

namespace HollowLines.Core
{
    /// <summary>
    /// The driller. Grid-locked movement rules, pure C#.
    /// Walks left/right, climbs single-block steps (Mr. Driller-standard),
    /// falls under gravity, and drills in the four cardinal directions — including up,
    /// which is legal and essential (the keystone play) but frees the block above you.
    /// </summary>
    public sealed class AvatarModel
    {
        public GridPos Position { get; private set; }

        /// <summary>Seconds per one-cell fall step. Slightly faster than blocks so you can outrun a collapse you started.</summary>
        public float FallStepInterval { get; set; } = 0.06f;

        /// <summary>
        /// Grace window (seconds) at the very start of a fall during which horizontal movement is
        /// still allowed — the "coyote time" that lets you step back onto a ledge you just walked off,
        /// or catch an adjacent one, WITHOUT unlocking full mid-air steering (long falls stay
        /// committed, which keeps crush dodges skillful). Set to 0 to restore the old fully-committed fall.
        /// </summary>
        public float CoyoteTime { get; set; } = 0.12f;

        public bool IsFalling => _grid.InBounds(Position.Below) && _grid.IsEmpty(Position.Below);

        /// <summary>True while falling and still inside the <see cref="CoyoteTime"/> grace window.</summary>
        public bool InCoyoteWindow => IsFalling && _fallElapsed < CoyoteTime;

        public event Action<GridPos> Moved;

        /// <summary>
        /// Fired after a successful drill. Second arg is the cell type BEFORE the hit
        /// (e.g. Hard, AirCapsule) so listeners can react to what was drilled. Third arg is the
        /// cardinal direction drilled — StreakTracker (§6.1) uses it to keep the streak vertical-only.
        /// </summary>
        public event Action<GridPos, CellType, DrillDirection> Drilled;

        private readonly GridModel _grid;
        private float _fallTimer;
        private float _fallElapsed; // continuous time spent in the current fall — drives the coyote window

        public AvatarModel(GridModel grid, GridPos spawn)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            if (!grid.InBounds(spawn))
                throw new ArgumentOutOfRangeException(nameof(spawn), $"Spawn {spawn} is outside the grid.");
            if (grid.IsSolid(spawn))
                throw new ArgumentException($"Spawn {spawn} is inside a solid cell.", nameof(spawn));

            Position = spawn;
        }

        /// <summary>
        /// Walk one cell left (-1) or right (+1).
        /// Into empty: walk. Into a solid with a free cell above it (and above you): step up.
        /// Mid-fall the fall is a commitment (keeps crush dodges skillful) — EXCEPT during the brief
        /// <see cref="CoyoteTime"/> grace window right after leaving the ground, where a sideways step
        /// into empty is still allowed so you can catch a ledge you just walked off. Step-up never
        /// works mid-air, even during the coyote window.
        /// </summary>
        public bool TryMove(int direction)
        {
            if (direction != -1 && direction != 1)
                return false;

            bool falling = IsFalling;
            if (falling && !InCoyoteWindow)
                return false; // committed fall: no horizontal control

            GridPos target = Position.Offset(direction, 0);
            if (!_grid.InBounds(target))
                return false;

            if (_grid.IsEmpty(target))
            {
                MoveTo(target);
                return true;
            }

            // Step-up: only when grounded. Climbing over a block mid-air (even in the coyote window)
            // would break the "falling is a commitment" contract.
            if (!falling)
            {
                GridPos stepUp = target.Above;
                GridPos aboveSelf = Position.Above;
                if (_grid.InBounds(stepUp) && _grid.IsEmpty(stepUp) &&
                    _grid.InBounds(aboveSelf) && _grid.IsEmpty(aboveSelf))
                {
                    MoveTo(stepUp);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Drill the adjacent cell in a cardinal direction (dx, dy), one axis at a time.
        /// Drilling up is allowed — GravitySystem takes it from there.
        /// </summary>
        public bool TryDrill(int dx, int dy)
        {
            bool cardinal = (dx == 0) != (dy == 0);
            bool unit = dx >= -1 && dx <= 1 && dy >= -1 && dy <= 1;
            if (!cardinal || !unit)
                return false;
            if (IsFalling)
                return false;

            GridPos target = Position.Offset(dx, dy);
            if (!_grid.InBounds(target))
                return false;

            CellType oldType = _grid.Get(target);
            if (!_grid.Drill(target))
                return false;

            Drilled?.Invoke(target, oldType, DirectionOf(dx, dy));
            return true;
        }

        private static DrillDirection DirectionOf(int dx, int dy)
        {
            if (dy < 0) return DrillDirection.Up;
            if (dy > 0) return DrillDirection.Down;
            return dx < 0 ? DrillDirection.Left : DrillDirection.Right;
        }

        /// <summary>Advance the avatar's own gravity. Call every frame before GravitySystem.Tick.</summary>
        public void Tick(float deltaTime)
        {
            if (!IsFalling)
            {
                _fallTimer = 0f;
                _fallElapsed = 0f; // back on solid ground: the coyote window recharges for the next fall
                return;
            }

            // Total time in this fall drives the coyote window. It is NOT reset by a sideways coyote
            // step (that still counts as the same fall), so the grace can never be extended by moving.
            _fallElapsed += deltaTime;

            _fallTimer += deltaTime;
            while (_fallTimer >= FallStepInterval && IsFalling)
            {
                _fallTimer -= FallStepInterval;
                MoveTo(Position.Below);
            }
        }

        /// <summary>Used by the run director to respawn after a crush (M1 toy behavior).</summary>
        public void Teleport(GridPos target)
        {
            if (!_grid.InBounds(target))
                throw new ArgumentOutOfRangeException(nameof(target));
            MoveTo(target);
        }

        private void MoveTo(GridPos target)
        {
            Position = target;
            Moved?.Invoke(target);
        }
    }
}
