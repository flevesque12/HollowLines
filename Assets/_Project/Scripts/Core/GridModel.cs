using System;

namespace HollowLines.Core
{
    /// <summary>
    /// The board. Single source of truth for cell contents.
    /// Pure C#, zero UnityEngine — every rule here is unit-testable without the editor.
    /// Y = 0 is the TOP row; gravity pulls toward Y = Height - 1.
    /// </summary>
    public sealed class GridModel
    {
        public int Width { get; }
        public int Height { get; }

        private readonly CellType[,] _cells; // [x, y]

        /// <summary>Fired for every cell mutation (drill, chunk move, collapse).</summary>
        public event Action<GridPos, CellType> CellChanged;

        public GridModel(int width, int height)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Grid dimensions must be positive.");

            Width = width;
            Height = height;
            _cells = new CellType[width, height];
        }

        public bool InBounds(GridPos p) => p.X >= 0 && p.X < Width && p.Y >= 0 && p.Y < Height;

        public CellType Get(GridPos p)
        {
            if (!InBounds(p))
                throw new ArgumentOutOfRangeException(nameof(p), $"{p} is outside the {Width}x{Height} grid.");
            return _cells[p.X, p.Y];
        }

        public bool IsSolid(GridPos p) => InBounds(p) && _cells[p.X, p.Y].IsSolid();
        public bool IsEmpty(GridPos p) => InBounds(p) && !_cells[p.X, p.Y].IsSolid();

        public void Set(GridPos p, CellType type)
        {
            if (!InBounds(p))
                throw new ArgumentOutOfRangeException(nameof(p), $"{p} is outside the {Width}x{Height} grid.");
            if (_cells[p.X, p.Y] == type)
                return;

            _cells[p.X, p.Y] = type;
            CellChanged?.Invoke(p, type);
        }

        /// <summary>
        /// Apply one drill hit to a cell. Returns false if out of bounds or not drillable
        /// (e.g. Steel) — never throws, because "tapping nothing" is a normal player input.
        /// Hard → HardCracked on the first hit; everything else → Empty.
        /// </summary>
        public bool Drill(GridPos p)
        {
            if (!InBounds(p))
                return false;

            CellType current = _cells[p.X, p.Y];
            if (!current.IsDrillable())
                return false;

            Set(p, current.DrillResult());
            return true;
        }

        /// <summary>True when every cell of row <paramref name="y"/> is void (the collapse rule, wired in M2).</summary>
        public bool IsRowVoid(int y)
        {
            for (int x = 0; x < Width; x++)
            {
                if (_cells[x, y].IsSolid())
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Build a grid from a string map (rows[0] = top row).
        /// Characters: '.' empty · 'A'/'B'/'C' colors · 'H' Hard · 'S' Steel · 'P' AirCapsule · 'X' Bomb · 'D' Diamond
        /// </summary>
        public static GridModel FromStringMap(string[] rows)
        {
            if (rows == null || rows.Length == 0)
                throw new ArgumentException("Map needs at least one row.", nameof(rows));

            int height = rows.Length;
            int width = rows[0].Length;
            var grid = new GridModel(width, height);

            for (int y = 0; y < height; y++)
            {
                if (rows[y].Length != width)
                    throw new ArgumentException($"Row {y} has length {rows[y].Length}, expected {width}.", nameof(rows));

                for (int x = 0; x < width; x++)
                {
                    grid._cells[x, y] = ParseChar(rows[y][x]);
                }
            }

            return grid;
        }

        private static CellType ParseChar(char c)
        {
            switch (c)
            {
                case '.': return CellType.Empty;
                case 'A': return CellType.ColorA;
                case 'B': return CellType.ColorB;
                case 'C': return CellType.ColorC;
                case 'H': return CellType.Hard;
                case 'S': return CellType.Steel;
                case 'P': return CellType.AirCapsule; // P = air Pack / Pill
                case 'X': return CellType.Bomb;       // X = eXplosive
                case 'D': return CellType.Diamond;
                default:  throw new ArgumentException($"Unknown map character '{c}'.");
            }
        }
    }
}
