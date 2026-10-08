using System;
using System.Collections.Generic;

namespace HollowLines.Core
{
    /// <summary>
    /// v3.2 Tier 2 fissures (drill-momentum.md §M2, ⚡D5): drilling at Tier 2+ cracks the blocks around
    /// the drill. A block that takes its second fissure breaks — the cell is emptied and GravitySystem
    /// does the rest (unsupported chunks fall → burst). Passive destabilisation that feeds the chunks.
    ///
    /// Only COLOR blocks fissure (CanFuse): fissures feed chunks, and chunks are color. Capsules and
    /// diamonds would vanish without being collected, Hard/Steel exist to resist, and a bomb is only
    /// ever armed by a drill or a landing. Breaking pays nothing by itself — the fall and burst pay.
    ///
    /// Fissures are keyed by POSITION. When the block there is drilled, blasted or falls away, the entry
    /// is stale; it reads as 0 and is pruned on the next drill.
    /// </summary>
    public sealed class FissureTracker
    {
        /// <summary>Momentum tier from which a drill cracks its neighbours.</summary>
        public const int MinTier = 2;

        /// <summary>Fissures that break a block.</summary>
        public const int BreakAt = 2;

        private static readonly GridPos[] Neighbours =
        {
            new GridPos(0, 1), new GridPos(0, -1), new GridPos(1, 0), new GridPos(-1, 0)
        };

        private readonly GridModel _grid;
        private readonly Dictionary<GridPos, int> _fissures = new Dictionary<GridPos, int>();
        private readonly List<GridPos> _scratch = new List<GridPos>();

        // ── Events ───────────────────────────────────────────────────
        /// <summary>A block took a fissure that did NOT break it — (cell, fissure count). View overlay hook.</summary>
        public event Action<GridPos, int> FissureAdded;

        /// <summary>A block took its second fissure and broke; the cell is already Empty.</summary>
        public event Action<GridPos> FissureBroke;

        public FissureTracker(GridModel grid)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        }

        /// <summary>Live fissure count on a cell (0 if none, or if the block there is gone / not a color).</summary>
        public int GetFissures(GridPos pos)
        {
            if (!_fissures.TryGetValue(pos, out int count))
                return 0;
            return IsFissurable(pos) ? count : 0;
        }

        /// <summary>Number of cells currently carrying a live fissure.</summary>
        public int Count
        {
            get
            {
                int n = 0;
                foreach (GridPos p in _fissures.Keys)
                    if (IsFissurable(p)) n++;
                return n;
            }
        }

        /// <summary>
        /// Call after every successful drill with the momentum tier it was paid at. Below
        /// <see cref="MinTier"/> nothing happens; at Tier 2+ each cardinal color neighbour takes +1.
        /// </summary>
        public void NotifyDrill(GridPos drilled, int currentTier)
        {
            Prune();
            if (currentTier < MinTier)
                return;

            foreach (GridPos d in Neighbours)
            {
                GridPos p = drilled.Offset(d.X, d.Y);
                if (!IsFissurable(p))
                    continue;

                _fissures.TryGetValue(p, out int count);
                count++;

                if (count >= BreakAt)
                {
                    _fissures.Remove(p);
                    _grid.Set(p, CellType.Empty);
                    FissureBroke?.Invoke(p);
                }
                else
                {
                    _fissures[p] = count;
                    FissureAdded?.Invoke(p, count);
                }
            }
        }

        /// <summary>Forget every fissure (new board / segment).</summary>
        public void Clear() => _fissures.Clear();

        private bool IsFissurable(GridPos p) => _grid.InBounds(p) && _grid.Get(p).CanFuse();

        /// <summary>Drops entries whose block is gone or is no longer a color block.</summary>
        private void Prune()
        {
            if (_fissures.Count == 0)
                return;

            _scratch.Clear();
            foreach (GridPos p in _fissures.Keys)
                if (!IsFissurable(p))
                    _scratch.Add(p);
            foreach (GridPos p in _scratch)
                _fissures.Remove(p);
        }
    }
}
