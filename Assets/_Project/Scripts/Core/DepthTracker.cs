using System;

namespace HollowLines.Core
{
    /// <summary>
    /// v3 core loop: tracks the deepest row the avatar has reached in the current run
    /// (GDD §5, CLAUDE.md §6.2). Pure counting/reporting — ScoreSystem awards points on
    /// NewDepthReached; the well only ever pulls the player forward, never backward.
    /// </summary>
    public sealed class DepthTracker
    {
        /// <summary>The deepest row reached so far this run. 0 until the avatar moves past the spawn row.</summary>
        public int MaxDepth { get; private set; }

        /// <summary>Fired the first time the avatar reaches a new deepest row, with that row index.</summary>
        public event Action<int> NewDepthReached;

        /// <summary>Call once per frame with the avatar's current position, after AvatarModel.Tick().</summary>
        public void NotifyPosition(GridPos pos)
        {
            if (pos.Y <= MaxDepth)
                return;

            MaxDepth = pos.Y;
            NewDepthReached?.Invoke(MaxDepth);
        }

        /// <summary>Zero the state for a new run.</summary>
        public void Reset()
        {
            MaxDepth = 0;
        }
    }
}
