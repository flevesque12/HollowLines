using System;
using System.Collections.Generic;

namespace HollowLines.Core
{
    /// <summary>
    /// Drives block gravity, per the GDD's readability contract (pillar 4):
    /// unsupported chunk → Wobble (fixed telegraph) → falls one row per step → lands or crushes.
    /// Pure C#: Tick() is called by the Unity layer with deltaTime, and by tests with fixed steps.
    /// </summary>
    public sealed class GravitySystem
    {
        /// <summary>Telegraph duration before an unsupported chunk starts falling. GDD: 0.6 s (assist ramp may extend it).</summary>
        public float WobbleDuration { get; set; } = 0.6f;

        /// <summary>Seconds per one-cell fall step.</summary>
        public float FallStepInterval { get; set; } = 0.08f;

        /// <summary>A chunk just became unsupported and started its telegraph.</summary>
        public event Action<Chunk> WobbleStarted;

        /// <summary>A chunk moved down one row (view animates from this).</summary>
        public event Action<Chunk> ChunkMoved;

        /// <summary>A previously falling chunk found support.</summary>
        public event Action<Chunk> ChunkLanded;

        /// <summary>A falling chunk entered the avatar's cell. Consumer decides the consequence (hearts in M3).</summary>
        public event Action<GridPos> AvatarCrushed;

        /// <summary>
        /// v3 Chunk Burst: a chunk fell <see cref="BurstFallThreshold"/>+ rows and shattered on impact.
        /// Payload: the cells it occupied at landing (already cleared from the grid), the fall
        /// distance, and the chunk's color — the view needs the color to tint the debris, and the
        /// cells are Empty by the time this fires, so it cannot be read back off the grid.
        /// </summary>
        public event Action<List<GridPos>, int, CellType> ChunkBurst;

        /// <summary>A burst shockwave freed an air capsule. Consumer restores air (never destroys it).</summary>
        public event Action<GridPos> AirCapsuleLiberated;

        /// <summary>A burst shockwave freed a diamond (R4). Consumer counts it toward DiamondSystem — never destroys it.</summary>
        public event Action<GridPos> DiamondLiberated;

        /// <summary>A burst shockwave armed an adjacent bomb. Consumer starts its fuse.</summary>
        public event Action<GridPos> BombArmedByBurst;

        /// <summary>The avatar was inside a burst zone (chunk cells or shockwave ring).</summary>
        public event Action AvatarHitByBurst;

        /// <summary>Minimum fall distance (rows) that makes a landing chunk shatter. GDD v3: 2.</summary>
        public const int BurstFallThreshold = 2;

        private enum ChunkPhase { Wobbling, Falling }

        private sealed class ChunkState
        {
            public ChunkPhase Phase;
            public float Timer;

            /// <summary>Bottom edge (MaxY) at the moment the telegraph started — the fall-distance origin.</summary>
            public int StartRow;
        }

        private readonly GridModel _grid;
        private readonly Dictionary<long, ChunkState> _states = new Dictionary<long, ChunkState>();
        private readonly HashSet<GridPos> _wobblingCells = new HashSet<GridPos>();
        private readonly List<GridPos> _lastShockwave = new List<GridPos>();

        /// <summary>
        /// The shockwave ring of the burst currently being resolved — the cells around the chunk's
        /// footprint, excluding the footprint itself. Filled in BEFORE ChunkBurst fires, so a
        /// handler can read it during that event (that is its only supported use: EnemySystem needs
        /// footprint + ring as one kill zone, §6.5). Stale between bursts — never poll it.
        ///
        /// This exists instead of a 4th ChunkBurst parameter because ChunkBurst has five subscribers
        /// (GameBootstrap, VfxManager, AudioManager, the playtest harness, ChunkBurstTests) and only
        /// one of them wants the ring; widening the event would churn all five for one consumer.
        /// </summary>
        public IReadOnlyList<GridPos> LastShockwaveCells => _lastShockwave;

        public GravitySystem(GridModel grid)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        }

        /// <summary>Is this cell part of a wobbling chunk right now? (View queries this for the shake.)</summary>
        public bool IsCellWobbling(GridPos p) => _wobblingCells.Contains(p);

        /// <summary>True while any chunk is wobbling or falling. ChainTracker uses this to know when the dust settles.</summary>
        public bool IsBusy => _states.Count > 0;

        public void Tick(float deltaTime, GridPos avatarCell)
        {
            List<Chunk> chunks = ChunkSystem.ComputeChunks(_grid);
            HashSet<Chunk> supported = ComputeSupport(chunks);

            _wobblingCells.Clear();
            var liveSignatures = new HashSet<long>();
            List<Chunk> bursting = null;
            List<int> burstDistances = null;

            // Supported chunks: clear any leftover state; a falling chunk that found support has landed.
            foreach (Chunk chunk in chunks)
            {
                if (!supported.Contains(chunk))
                    continue;

                long signature = Signature(chunk);
                if (_states.TryGetValue(signature, out ChunkState state))
                {
                    _states.Remove(signature);
                    if (state.Phase == ChunkPhase.Falling)
                    {
                        ChunkLanded?.Invoke(chunk);

                        // Fall distance is measured from where the telegraph began to where it settled.
                        int fallDistance = chunk.MaxY - state.StartRow;
                        if (fallDistance >= BurstFallThreshold)
                        {
                            bursting      ??= new List<Chunk>();
                            burstDistances ??= new List<int>();
                            bursting.Add(chunk);
                            burstDistances.Add(fallDistance);
                        }
                    }
                }
            }

            // A burst rewrites the board (chunk cells + shockwave), which invalidates the chunk/support
            // snapshot this tick is holding. Resolve the bursts and let the next tick recompute from the
            // new grid — that recompute IS the "rebuild" step, since ChunkSystem is stateless.
            // States are intentionally left unpruned here: stale signatures disappear on the next pass.
            if (bursting != null)
            {
                for (int i = 0; i < bursting.Count; i++)
                    ResolveBurst(bursting[i], burstDistances[i], avatarCell);
                return;
            }

            // Unsupported chunks, deepest first so lower chunks vacate space before upper ones move.
            var unsupported = new List<Chunk>();
            foreach (Chunk chunk in chunks)
            {
                if (!supported.Contains(chunk))
                    unsupported.Add(chunk);
            }
            unsupported.Sort((a, b) => b.MaxY.CompareTo(a.MaxY));

            foreach (Chunk chunk in unsupported)
            {
                long signature = Signature(chunk);

                if (!_states.TryGetValue(signature, out ChunkState state))
                {
                    // New instability (fresh dig, split, or the chunk under it moved away): telegraph first, always.
                    state = new ChunkState
                    {
                        Phase    = ChunkPhase.Wobbling,
                        Timer    = WobbleDuration,
                        StartRow = chunk.MaxY,
                    };
                    _states[signature] = state;
                    WobbleStarted?.Invoke(chunk);
                }

                if (state.Phase == ChunkPhase.Wobbling)
                {
                    state.Timer -= deltaTime;
                    if (state.Timer <= 0f)
                    {
                        state.Phase = ChunkPhase.Falling;
                        state.Timer = 0f;
                    }
                    else
                    {
                        MarkWobbling(chunk);
                        liveSignatures.Add(signature);
                        continue;
                    }
                }

                // Falling: consume accumulated time in whole one-row steps.
                state.Timer += deltaTime;
                while (state.Timer >= FallStepInterval)
                {
                    state.Timer -= FallStepInterval;
                    if (!TryMoveDown(chunk, avatarCell, ref signature, state))
                        break; // blocked this tick; support is re-evaluated next tick
                }

                liveSignatures.Add(signature);
            }

            PruneStaleStates(liveSignatures);
        }

        /// <summary>
        /// Resolve gravity to a resting state instantly — no telegraph, no burst, no shockwave.
        /// Called once at board load (GameBootstrap.LoadLevel) so a freshly generated board settles
        /// into a stable configuration BEFORE play. Without it, generated instability (chunks sitting
        /// over gaps) wobbles, falls and bursts on the first ticks — and the shockwave cascade
        /// demolishes the whole board before the player ever moves (R2.8d). After Settle() every chunk
        /// is supported, so the first Tick() finds nothing to drop. The avatar is never crushed here —
        /// this is board setup, not gameplay — and no events fire.
        /// </summary>
        public void Settle()
        {
            _states.Clear();
            _wobblingCells.Clear();

            // Each pass drops every currently-unsupported chunk by one row; repeat until a pass moves
            // nothing. The board strictly "descends" every productive pass, so this terminates; the
            // safety counter is a hard backstop against a pathological grid.
            int safety = _grid.Width * _grid.Height + 1;
            bool moved = true;
            while (moved && safety-- > 0)
            {
                moved = false;
                List<Chunk> chunks = ChunkSystem.ComputeChunks(_grid);
                HashSet<Chunk> supported = ComputeSupport(chunks);

                // Deepest first so a lower chunk vacates its cells before the chunk above drops in.
                var unsupported = new List<Chunk>();
                foreach (Chunk chunk in chunks)
                {
                    if (!supported.Contains(chunk))
                        unsupported.Add(chunk);
                }
                unsupported.Sort((a, b) => b.MaxY.CompareTo(a.MaxY));

                foreach (Chunk chunk in unsupported)
                {
                    if (SettleStepDown(chunk))
                        moved = true;
                }
            }
        }

        /// <summary>
        /// Move a chunk straight down one row if every target cell is free (empty or part of this same
        /// chunk). No events, no crush — Settle()'s silent counterpart to <see cref="TryMoveDown"/>.
        /// </summary>
        private bool SettleStepDown(Chunk chunk)
        {
            foreach (GridPos cell in chunk.Cells)
            {
                GridPos below = cell.Below;
                if (below.Y >= _grid.Height)
                    return false;
                if (_grid.IsSolid(below) && !chunk.Contains(below))
                    return false;
            }

            // Clear then re-stamp one row lower, in two passes so intra-chunk cells never erase themselves.
            foreach (GridPos cell in chunk.Cells)
                _grid.Set(cell, CellType.Empty);
            foreach (GridPos cell in chunk.Cells)
                _grid.Set(cell.Below, chunk.Color);

            return true;
        }

        /// <summary>
        /// Fixed-point support propagation: a chunk is supported if it touches the well floor,
        /// or rests on a cell of a supported chunk. Chains of stacked chunks resolve in a few passes.
        /// The avatar never supports a block (GDD §4.2 — blocks fall through you, that is the crush).
        /// </summary>
        private HashSet<Chunk> ComputeSupport(List<Chunk> chunks)
        {
            var cellOwner = new Dictionary<GridPos, Chunk>();
            foreach (Chunk chunk in chunks)
            {
                foreach (GridPos cell in chunk.Cells)
                    cellOwner[cell] = chunk;
            }

            var supported = new HashSet<Chunk>();

            // Seed: grounded on the floor.
            foreach (Chunk chunk in chunks)
            {
                foreach (GridPos cell in chunk.Cells)
                {
                    if (cell.Y == _grid.Height - 1)
                    {
                        supported.Add(chunk);
                        break;
                    }
                }
            }

            // Propagate upward until stable.
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (Chunk chunk in chunks)
                {
                    if (supported.Contains(chunk))
                        continue;

                    foreach (GridPos cell in chunk.Cells)
                    {
                        GridPos below = cell.Below;
                        if (cellOwner.TryGetValue(below, out Chunk owner) && owner != chunk && supported.Contains(owner))
                        {
                            supported.Add(chunk);
                            changed = true;
                            break;
                        }
                    }
                }
            }

            return supported;
        }

        private bool TryMoveDown(Chunk chunk, GridPos avatarCell, ref long signature, ChunkState state)
        {
            // Every cell must be able to move into (x, y+1): in bounds, and empty or occupied by this same chunk.
            foreach (GridPos cell in chunk.Cells)
            {
                GridPos below = cell.Below;
                if (below.Y >= _grid.Height)
                    return false;
                if (_grid.IsSolid(below) && !chunk.Contains(below))
                    return false;
            }

            // Clear then re-stamp one row lower. Two passes so intra-chunk overlaps never erase themselves.
            foreach (GridPos cell in chunk.Cells)
                _grid.Set(cell, CellType.Empty);

            bool crushed = false;
            foreach (GridPos cell in chunk.Cells)
            {
                GridPos target = cell.Below;
                _grid.Set(target, chunk.Color);
                if (target == avatarCell)
                    crushed = true;
            }

            chunk.ShiftDown();

            // The chunk's identity moved with it: transfer its state to the new signature.
            _states.Remove(signature);
            signature = Signature(chunk);
            _states[signature] = state;

            ChunkMoved?.Invoke(chunk);
            if (crushed)
                AvatarCrushed?.Invoke(avatarCell);

            return true;
        }

        /// <summary>
        /// v3 Chunk Burst: shatter a chunk that landed hard, then blow a 1-cell cardinal shockwave
        /// through its neighbors. Fires ChunkBurst first (score/VFX read the intact cell list), then the
        /// per-cell shockwave consequences. Crush already fired during the fall, so ordering is:
        /// crush → land → burst → shockwave.
        ///
        /// §5.2 deviation 5 resolved: the shockwave ring no longer harms the avatar. Only the chunk's
        /// own footprint — where a crushed player actually stands — fires AvatarHitByBurst. A single
        /// sidestep during the wobble now clears the ring, so a correct dodge is never punished
        /// (design rule 4: "no unfair deaths"). The ring keeps every effect on blocks, capsules and bombs.
        /// </summary>
        private void ResolveBurst(Chunk chunk, int fallDistance, GridPos avatarCell)
        {
            var cells = new List<GridPos>(chunk.Cells);

            foreach (GridPos cell in cells)
                _grid.Set(cell, CellType.Empty);

            // Shockwave ring: every cardinal neighbor that is not itself part of the burst.
            // COMPUTED BEFORE ChunkBurst fires, so a handler reading LastShockwaveCells during the
            // event sees THIS burst's ring and not the previous one. Its effects still apply after
            // the event, so the documented crush → land → burst → shockwave ordering is unchanged.
            var ring = new HashSet<GridPos>();
            foreach (GridPos cell in cells)
            {
                AddRingCell(ring, cells, cell.Offset(0, -1));
                AddRingCell(ring, cells, cell.Offset(0,  1));
                AddRingCell(ring, cells, cell.Offset(-1, 0));
                AddRingCell(ring, cells, cell.Offset( 1, 0));
            }

            _lastShockwave.Clear();
            _lastShockwave.AddRange(ring);

            ChunkBurst?.Invoke(cells, fallDistance, chunk.Color);

            // The avatar is only hit by the chunk's own footprint — that is where a crushed player
            // stands. The shockwave ring below is deliberately harmless to the avatar (§5.2 dev 5).
            bool avatarHit = false;
            foreach (GridPos cell in cells)
            {
                if (cell == avatarCell)
                {
                    avatarHit = true;
                    break;
                }
            }

            foreach (GridPos p in ring)
            {
                switch (_grid.Get(p))
                {
                    case CellType.ColorA:
                    case CellType.ColorB:
                    case CellType.ColorC:
                    case CellType.Hard:
                    case CellType.HardCracked:
                        _grid.Set(p, CellType.Empty);
                        break;

                    case CellType.Steel:
                        _grid.Set(p, CellType.Hard); // softened, not destroyed
                        break;

                    case CellType.AirCapsule:
                        _grid.Set(p, CellType.Empty);
                        AirCapsuleLiberated?.Invoke(p);
                        break;

                    case CellType.Diamond:
                        _grid.Set(p, CellType.Empty);
                        DiamondLiberated?.Invoke(p);
                        break;

                    case CellType.Bomb:
                        BombArmedByBurst?.Invoke(p); // left on the grid; its fuse does the rest
                        break;
                }
            }

            if (avatarHit)
                AvatarHitByBurst?.Invoke();
        }

        private void AddRingCell(HashSet<GridPos> ring, List<GridPos> burstCells, GridPos p)
        {
            if (!_grid.InBounds(p))
                return;
            if (burstCells.Contains(p))
                return;
            ring.Add(p);
        }

        private void MarkWobbling(Chunk chunk)
        {
            foreach (GridPos cell in chunk.Cells)
                _wobblingCells.Add(cell);
        }

        private void PruneStaleStates(HashSet<long> liveSignatures)
        {
            List<long> stale = null;
            foreach (long key in _states.Keys)
            {
                if (!liveSignatures.Contains(key))
                {
                    stale ??= new List<long>();
                    stale.Add(key);
                }
            }

            if (stale == null)
                return;

            foreach (long key in stale)
                _states.Remove(key);
        }

        /// <summary>
        /// Stable identity for a chunk shape: same cells + color = same signature across ticks.
        /// If the shape changes (drill split/merge), the signature changes and the telegraph restarts —
        /// which is exactly the re-telegraph behavior the readability pillar asks for.
        /// </summary>
        private long Signature(Chunk chunk)
        {
            // Cells come out of union-find in deterministic scan order, so no sort is needed.
            long hash = 17;
            foreach (GridPos cell in chunk.Cells)
            {
                unchecked
                {
                    hash = hash * 31 + (cell.Y * _grid.Width + cell.X);
                }
            }
            unchecked
            {
                hash = hash * 31 + (int)chunk.Color;
            }
            return hash;
        }
    }
}
