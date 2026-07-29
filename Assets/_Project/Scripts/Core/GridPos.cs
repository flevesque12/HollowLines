using System;

namespace HollowLines.Core
{
    /// <summary>
    /// Grid coordinate. X = column (0 = left), Y = row (0 = TOP, grows downward).
    /// Pure C# — no UnityEngine dependency so the core stays unit-testable.
    /// </summary>
    public readonly struct GridPos : IEquatable<GridPos>
    {
        public readonly int X;
        public readonly int Y;

        public GridPos(int x, int y)
        {
            X = x;
            Y = y;
        }

        public GridPos Below => new GridPos(X, Y + 1);
        public GridPos Above => new GridPos(X, Y - 1);

        public GridPos Offset(int dx, int dy) => new GridPos(X + dx, Y + dy);

        public bool Equals(GridPos other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is GridPos other && Equals(other);
        public override int GetHashCode() => (Y << 16) ^ X;
        public override string ToString() => $"({X},{Y})";

        public static bool operator ==(GridPos a, GridPos b) => a.Equals(b);
        public static bool operator !=(GridPos a, GridPos b) => !a.Equals(b);
    }
}
