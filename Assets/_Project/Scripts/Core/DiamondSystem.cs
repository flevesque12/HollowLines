using System;

namespace HollowLines.Core
{
    /// <summary>
    /// v3 core loop (R4): tracks diamond collection and gates the campaign win condition
    /// (GDD §5, CLAUDE.md §6.4). Pure counting/reporting — collection sources (drill, bomb
    /// liberation, burst shockwave liberation) all funnel into NotifyCollected from the View.
    ///
    /// Levels with zero diamonds are always complete (IsComplete is true as soon as Init(0)
    /// runs), so the win gate is a no-op on levels 1-3 (§9).
    /// </summary>
    public sealed class DiamondSystem
    {
        /// <summary>Diamonds collected so far on the current board.</summary>
        public int Collected { get; private set; }

        /// <summary>Total diamonds placed on the current board.</summary>
        public int Total { get; private set; }

        /// <summary>True once every diamond on the board has been collected (or the board has none).</summary>
        public bool IsComplete => Collected >= Total;

        /// <summary>Fired on every collection, with the running (collected, total) tally.</summary>
        public event Action<int, int> DiamondCollected;

        /// <summary>Fired once, the moment the last diamond on the board is collected.</summary>
        public event Action AllDiamondsCollected;

        /// <summary>Call when a new board loads, with how many diamonds it placed.</summary>
        public void Init(int totalDiamonds)
        {
            Total = totalDiamonds;
            Collected = 0;
        }

        /// <summary>Call when a diamond is collected — by drill, bomb liberation, or burst shockwave liberation.</summary>
        public void NotifyCollected(GridPos pos)
        {
            Collected++;
            DiamondCollected?.Invoke(Collected, Total);

            if (Collected == Total)
                AllDiamondsCollected?.Invoke();
        }

        /// <summary>Zero the state for a new run.</summary>
        public void Reset()
        {
            Collected = 0;
            Total = 0;
        }
    }
}
