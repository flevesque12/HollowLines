using System;
using System.Collections.Generic;

namespace HollowLines.Core
{
    /// <summary>
    /// M3 step 4: manages buried bombs and the player's pocket bomb.
    ///
    /// A buried bomb (CellType.Bomb in the grid) is armed when the player drills an adjacent cell
    /// or when a falling chunk lands next to it. Once armed, a 2.5 s fuse ticks; on expiry the bomb
    /// clears itself and blasts a cross of radius 2 in the four cardinal directions.
    ///
    /// Blast effects by type:
    ///   Color/Hard/HardCracked/Bomb → Empty (destroyed outright)
    ///   AirCapsule                  → Empty + AirCapsuleLiberated (v3: freed, not wasted)
    ///   Diamond                     → Empty + DiamondLiberated (R4: freed, not wasted)
    ///   Steel                       → Hard  (softened — still needs drill or another bomb)
    ///   Avatar in blast radius      → AvatarHitByBlast event (caller handles hearts)
    ///
    /// Sympathetic detonation: a bomb hit by a blast explodes immediately in the same Tick call
    /// via a BFS queue — chains resolve in a single frame with no extra delay. Each detonation
    /// reports its own BombScored(blocksDestroyed, chainMultiplier), the multiplier rising with
    /// its position in the chain.
    ///
    /// When a row collapses (CollapseSystem.PerfectClear), armed bomb positions are shifted down
    /// by one row to stay in sync with the grid's new layout.
    /// </summary>
    public sealed class BombSystem
    {
        public const float FuseDuration = 2.5f;
        public const int   BlastRadius  = 2;

        public bool HasPocketBomb { get; private set; }

        /// <summary>A buried bomb's fuse was just lit.</summary>
        public event Action<GridPos> BombArmed;

        /// <summary>
        /// Called each Tick for every armed bomb still ticking.
        /// Payload: (position, fraction 0→1 where 1 = detonation).
        /// Use this to drive a fuse animation or countdown bar in the view.
        /// </summary>
        public event Action<GridPos, float> FuseProgress;

        /// <summary>A bomb detonated. Fired before its blast is applied.</summary>
        public event Action<GridPos> BombExploded;

        /// <summary>
        /// The blast radius reached the avatar's cell.
        /// Caller (GameBootstrap) should call HealthSystem.TryTakeDamage().
        /// </summary>
        public event Action<GridPos> AvatarHitByBlast;

        /// <summary>
        /// v3: a blast freed an air capsule instead of wasting it (design rule 3 — bombs are friends).
        /// Caller wires this to AirSystem.RestoreCapsule().
        /// </summary>
        public event Action<GridPos> AirCapsuleLiberated;

        /// <summary>
        /// R4: a blast freed a diamond instead of wasting it (same principle as AirCapsuleLiberated).
        /// Caller wires this to DiamondSystem.NotifyCollected().
        /// </summary>
        public event Action<GridPos> DiamondLiberated;

        /// <summary>
        /// v3: fired once per individual detonation, after its blast resolves.
        /// Payload: (blocks destroyed by this blast, 1-based chain position within the BFS).
        /// Caller wires this to ScoreSystem.AwardBomb().
        /// </summary>
        public event Action<int, int> BombScored;

        private readonly GridModel _grid;

        // Keyed by bomb position; value = seconds remaining on the fuse.
        private readonly Dictionary<GridPos, float> _armed = new Dictionary<GridPos, float>();

        public BombSystem(GridModel grid, CollapseSystem collapse)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            if (collapse == null) throw new ArgumentNullException(nameof(collapse));
            collapse.PerfectClear += OnPerfectClear;
        }

        // ── Arm triggers ────────────────────────────────────────────────

        /// <summary>
        /// Call after a successful drill. Checks the four cardinal neighbors of the drilled cell
        /// for buried bombs and lights their fuse if not already armed.
        /// </summary>
        public void NotifyDrilled(GridPos drilledCell)
        {
            ArmAdjacent(drilledCell);
        }

        /// <summary>
        /// Call when GravitySystem.ChunkLanded fires. Checks all four neighbors of every cell in the
        /// landed chunk — a heavy slab landing next to a bomb is enough to shake it awake.
        /// </summary>
        public void NotifyChunkLanded(Chunk chunk)
        {
            foreach (GridPos cell in chunk.Cells)
                ArmAdjacent(cell);
        }

        /// <summary>
        /// Arm the bomb sitting at this exact cell (as opposed to its neighbors).
        /// Wire to GravitySystem.BombArmedByBurst: a shockwave lights the fuse of the bomb it hits.
        /// No-op if the cell holds no bomb or that bomb is already counting down.
        /// </summary>
        public void ArmBombAt(GridPos bombCell) => TryArm(bombCell);

        // ── Per-frame update ─────────────────────────────────────────────

        /// <summary>
        /// Advance all fuse timers. Detonate any that expired, then resolve sympathetic detonations
        /// via BFS. Must be called BEFORE CollapseSystem.Resolve so that cells cleared by blasts are
        /// detected as new void-lines in the same frame.
        /// </summary>
        public void Tick(float dt, GridPos avatarCell)
        {
            if (_armed.Count == 0)
                return;

            // Snapshot to safely mutate _armed while iterating.
            var snapshot = new List<KeyValuePair<GridPos, float>>(_armed);
            var toExplode = new List<GridPos>();

            foreach (var kvp in snapshot)
            {
                float remaining = kvp.Value - dt;
                if (remaining <= 0f)
                {
                    toExplode.Add(kvp.Key);
                }
                else
                {
                    _armed[kvp.Key] = remaining;
                    FuseProgress?.Invoke(kvp.Key, 1f - (remaining / FuseDuration));
                }
            }

            if (toExplode.Count == 0)
                return;

            ProcessExplosions(toExplode, avatarCell);
        }

        // ── Pocket bomb ──────────────────────────────────────────────────

        /// <summary>Grant the player a pocket bomb (called by level setup).</summary>
        public void GrantPocketBomb() => HasPocketBomb = true;

        /// <summary>
        /// Use the pocket bomb at the given center cell. Explodes immediately (no fuse).
        /// Returns false if no pocket bomb is available.
        /// </summary>
        public bool TryUsePocketBomb(GridPos center, GridPos avatarCell)
        {
            if (!HasPocketBomb)
                return false;
            HasPocketBomb = false;

            // If center is a buried bomb, consume it so it doesn't detonate twice.
            if (_grid.InBounds(center) && _grid.Get(center) == CellType.Bomb)
            {
                _armed.Remove(center);
                _grid.Set(center, CellType.Empty);
            }

            var seed = new List<GridPos> { center };
            ProcessExplosions(seed, avatarCell);
            return true;
        }

        // ── Internal ─────────────────────────────────────────────────────

        private void ProcessExplosions(List<GridPos> seeds, GridPos avatarCell)
        {
            var queue = new Queue<GridPos>(seeds);
            var exploded = new HashSet<GridPos>();

            // v3 chain reward: 1 for the bomb that started it, +1 for every further detonation
            // resolved in this same BFS. Bigger chains pay more (design rule 3).
            int chainMultiplier = 0;

            while (queue.Count > 0)
            {
                GridPos pos = queue.Dequeue();
                if (!exploded.Add(pos))
                    continue;

                chainMultiplier++;
                _armed.Remove(pos);

                // Clear the bomb cell itself from the grid before the outward blast.
                if (_grid.InBounds(pos) && _grid.Get(pos) == CellType.Bomb)
                    _grid.Set(pos, CellType.Empty);

                BombExploded?.Invoke(pos);
                int destroyed = Blast(pos, avatarCell, queue, exploded);
                BombScored?.Invoke(destroyed, chainMultiplier);
            }
        }

        /// <summary>Applies one bomb's cross blast. Returns how many blocks it removed outright.</summary>
        private int Blast(GridPos center, GridPos avatarCell, Queue<GridPos> sympatheticQueue, HashSet<GridPos> alreadyExploded)
        {
            // Cross pattern — 4 cardinal axes, up to BlastRadius steps each.
            int[] dx = {  0,  0, -1,  1 };
            int[] dy = { -1,  1,  0,  0 };

            int destroyed = 0;

            for (int axis = 0; axis < 4; axis++)
            {
                for (int r = 1; r <= BlastRadius; r++)
                {
                    GridPos target = center.Offset(dx[axis] * r, dy[axis] * r);
                    if (!_grid.InBounds(target))
                        break; // out of bounds stops the arm in this direction

                    CellType hit = _grid.Get(target);

                    if (target == avatarCell)
                        AvatarHitByBlast?.Invoke(target);

                    // Sympathetic detonation: queued for processing after current explosion.
                    if (hit == CellType.Bomb && !alreadyExploded.Contains(target))
                        sympatheticQueue.Enqueue(target);

                    CellType result = BlastResult(hit);
                    if (result != hit)
                    {
                        _grid.Set(target, result);

                        // Steel only softens to Hard — softening is not a destruction.
                        if (result == CellType.Empty)
                            destroyed++;

                        // The capsule is freed, not wasted: the caller banks the air.
                        if (hit == CellType.AirCapsule)
                            AirCapsuleLiberated?.Invoke(target);

                        // The diamond is freed, not wasted: the caller banks the collection.
                        if (hit == CellType.Diamond)
                            DiamondLiberated?.Invoke(target);
                    }
                }
            }

            return destroyed;
        }

        private static CellType BlastResult(CellType type)
        {
            switch (type)
            {
                case CellType.ColorA:
                case CellType.ColorB:
                case CellType.ColorC:
                case CellType.Hard:
                case CellType.HardCracked:
                case CellType.AirCapsule:
                case CellType.Diamond:
                case CellType.Bomb:
                    return CellType.Empty;
                case CellType.Steel:
                    return CellType.Hard; // softened — requires drill or second bomb to remove
                default:
                    return type; // Empty stays Empty
            }
        }

        private void ArmAdjacent(GridPos origin)
        {
            TryArm(origin.Above);
            TryArm(origin.Below);
            TryArm(origin.Offset(-1, 0));
            TryArm(origin.Offset( 1, 0));
        }

        private void TryArm(GridPos pos)
        {
            if (!_grid.InBounds(pos))
                return;
            if (_grid.Get(pos) != CellType.Bomb)
                return;
            if (_armed.ContainsKey(pos))
                return; // already counting down
            _armed[pos] = FuseDuration;
            BombArmed?.Invoke(pos);
        }

        // When a row collapses, every armed bomb that was ABOVE that row shifts down one row to
        // stay in sync with how CollapseSystem physically moved those grid cells.
        private void OnPerfectClear(int collapsedRow)
        {
            var toShift = new List<GridPos>();
            foreach (GridPos pos in _armed.Keys)
            {
                if (pos.Y < collapsedRow)
                    toShift.Add(pos);
            }
            foreach (GridPos pos in toShift)
            {
                float time = _armed[pos];
                _armed.Remove(pos);
                _armed[pos.Below] = time;
            }
        }
    }
}
