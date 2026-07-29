using System;

namespace HollowLines.Core
{
    /// <summary>
    /// A fully-void row collapses everything above it by one row.
    ///
    /// v3 status: this was the core loop in v2.1; it is now the rare "Perfect Clear" jackpot
    /// (GDD v3 §8 — +500 × cascade). The mechanic is unchanged, only its role and its event
    /// name are: drilling, bursting and bombing carry the loop now.
    ///
    /// Unlike GravitySystem there is no wobble here: gravity is a *threat* so it telegraphs;
    /// the collapse is the *payoff* so it hits instantly.
    /// </summary>
    public sealed class CollapseSystem
    {
        /// <summary>
        /// A full-void row collapsed — the Perfect Clear bonus.
        /// Payload = the row index (view shakes the camera, ScoreSystem awards the jackpot).
        /// </summary>
        public event Action<int> PerfectClear;

        /// <summary>The shift dropped a solid onto the avatar's cell — your victory just crushed you (GDD §4.5).</summary>
        public event Action<GridPos> AvatarCrushed;

        private readonly GridModel _grid;

        public CollapseSystem(GridModel grid)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        }

        /// <summary>
        /// Resolve every collapsible row this frame. Returns how many rows collapsed (usually 0 or 1;
        /// more when a falling chunk vacates several rows at once).
        /// Call once per frame BEFORE GravitySystem.Tick, so chunks orphaned by the shift
        /// get their support re-evaluated (and wobble) on the same frame.
        /// </summary>
        public int Resolve(GridPos avatarCell)
        {
            int count = 0;
            while (TryCollapseOne(avatarCell))
                count++;
            return count;
        }

        private bool TryCollapseOne(GridPos avatarCell)
        {
            for (int y = 0; y < _grid.Height; y++)
            {
                if (!_grid.IsRowVoid(y))
                    continue;

                // Guard: a void row with nothing solid above it is just sky, not a line.
                // Without this, the empty spawn rows would "collapse" every single frame.
                if (!HasSolidAbove(y))
                    continue;

                CollapseRow(y, avatarCell);
                return true;
            }

            return false;
        }

        private bool HasSolidAbove(int row)
        {
            for (int y = 0; y < row; y++)
            {
                for (int x = 0; x < _grid.Width; x++)
                {
                    if (_grid.Get(new GridPos(x, y)).IsSolid())
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Shift every row above <paramref name="row"/> down by one, top-fill with void.
        /// Bottom-up copy so we never overwrite a row before reading it.
        /// Goes through GridModel.Set on purpose: every moved cell fires CellChanged,
        /// which is what keeps BoardView (and later the VFX) in sync for free.
        /// </summary>
        private void CollapseRow(int row, GridPos avatarCell)
        {
            bool crushedAvatar = false;

            for (int y = row; y > 0; y--)
            {
                for (int x = 0; x < _grid.Width; x++)
                {
                    CellType incoming = _grid.Get(new GridPos(x, y - 1));
                    // Steel cannot survive a structural collapse — the void sweeps it away.
                    // This is intentional (GDD §M3): strategic line clears can remove Steel
                    // that bombs cannot (they only soften Steel → Hard).
                    if (incoming == CellType.Steel)
                        incoming = CellType.Empty;

                    var target = new GridPos(x, y);
                    _grid.Set(target, incoming);

                    if (incoming.IsSolid() && target == avatarCell)
                        crushedAvatar = true;
                }
            }

            // The top row has nothing above it: it becomes the breathing room the collapse earned you.
            for (int x = 0; x < _grid.Width; x++)
                _grid.Set(new GridPos(x, 0), CellType.Empty);

            PerfectClear?.Invoke(row);
            if (crushedAvatar)
                AvatarCrushed?.Invoke(avatarCell);
        }
    }
}