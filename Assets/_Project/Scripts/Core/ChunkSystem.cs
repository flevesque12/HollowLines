using System.Collections.Generic;

namespace HollowLines.Core
{
    /// <summary>
    /// A rigid group of orthogonally-connected same-color cells.
    /// Chunks are the unit of gravity: they wobble, fall, and land as one piece.
    /// </summary>
    public sealed class Chunk
    {
        public CellType Color { get; }
        public IReadOnlyList<GridPos> Cells => _cells;

        private readonly List<GridPos> _cells;

        /// <summary>Deepest row this chunk occupies (largest Y). Used to resolve falls bottom-up.</summary>
        public int MaxY { get; private set; }

        internal Chunk(CellType color, List<GridPos> cells)
        {
            Color = color;
            _cells = cells;
            RecomputeMaxY();
        }

        public bool Contains(GridPos p)
        {
            for (int i = 0; i < _cells.Count; i++)
            {
                if (_cells[i] == p)
                    return true;
            }
            return false;
        }

        /// <summary>Shift every cell down one row. Called by GravitySystem after it has mutated the grid.</summary>
        internal void ShiftDown()
        {
            for (int i = 0; i < _cells.Count; i++)
            {
                _cells[i] = _cells[i].Below;
            }
            MaxY++;
        }

        private void RecomputeMaxY()
        {
            MaxY = int.MinValue;
            for (int i = 0; i < _cells.Count; i++)
            {
                if (_cells[i].Y > MaxY)
                    MaxY = _cells[i].Y;
            }
        }
    }

    /// <summary>
    /// Computes chunk fusion with a union-find (disjoint set) over the grid.
    /// Deliberate design (GDD §4.3): fusion only — there is NO auto-clear at 4+ connected.
    /// Non-fusable solids (Hard blocks in M3) become single-cell chunks.
    /// </summary>
    public static class ChunkSystem
    {
        public static List<Chunk> ComputeChunks(GridModel grid)
        {
            int w = grid.Width;
            int h = grid.Height;
            var parent = new int[w * h];
            for (int i = 0; i < parent.Length; i++)
                parent[i] = i;

            // Union same-color fusable neighbors (right and down are enough to cover all pairs).
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var p = new GridPos(x, y);
                    CellType type = grid.Get(p);
                    if (!type.CanFuse())
                        continue;

                    var right = new GridPos(x + 1, y);
                    if (grid.InBounds(right) && grid.Get(right) == type)
                        Union(parent, Index(p, h), Index(right, h));

                    var below = p.Below;
                    if (grid.InBounds(below) && grid.Get(below) == type)
                        Union(parent, Index(p, h), Index(below, h));
                }
            }

            // Group solid cells by root.
            var groups = new Dictionary<int, List<GridPos>>();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var p = new GridPos(x, y);
                    if (!grid.Get(p).IsSolid())
                        continue;

                    int root = Find(parent, Index(p, h));
                    if (!groups.TryGetValue(root, out var list))
                    {
                        list = new List<GridPos>();
                        groups[root] = list;
                    }
                    list.Add(p);
                }
            }

            var chunks = new List<Chunk>(groups.Count);
            foreach (var pair in groups)
            {
                chunks.Add(new Chunk(grid.Get(pair.Value[0]), pair.Value));
            }
            return chunks;
        }

        private static int Index(GridPos p, int height) => p.X * height + p.Y;

        private static int Find(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]]; // path halving
                i = parent[i];
            }
            return i;
        }

        private static void Union(int[] parent, int a, int b)
        {
            int rootA = Find(parent, a);
            int rootB = Find(parent, b);
            if (rootA != rootB)
                parent[rootB] = rootA;
        }
    }
}
