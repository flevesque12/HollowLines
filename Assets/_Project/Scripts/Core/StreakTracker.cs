using System;

namespace HollowLines.Core
{
    /// <summary>
    /// v3 core loop: tracks consecutive same-color drills for the Color Streak scoring system
    /// (GDD §5, CLAUDE.md §6.1). Pure counting/reporting — ScoreSystem converts streak steps to points.
    ///
    /// AirCapsule and Hard/HardCracked are streak-neutral: the player is never punished for grabbing
    /// air or chipping through a Hard block mid-streak. Steel and Bomb never reach NotifyDrill —
    /// they are not drillable.
    ///
    /// v3.1 arcade pivot: the streak is vertical-only. A lateral or upward drill is completely
    /// ignored (no reset, no increment) — only a downward drill builds or breaks it. This makes the
    /// streak a passive bonus of natural descent instead of an active routing system, and removes
    /// the Streak × Burst conflict (bursts destroy blocks laterally; the streak never notices).
    /// </summary>
    public sealed class StreakTracker
    {
        /// <summary>The running streak count. 0 when idle (no drill yet, or just reset).</summary>
        public int CurrentStreak { get; private set; }

        /// <summary>The color the current streak is built on. Undefined (Empty) when CurrentStreak is 0.</summary>
        public CellType CurrentColor { get; private set; }

        /// <summary>Fired on every same-color drill that extends the streak, with the new count.</summary>
        public event Action<int> StreakGrew;

        /// <summary>Fired when a different color breaks the streak, with the count that just ended.</summary>
        public event Action<int> StreakBroken;

        /// <summary>Call after every successful drill with the CellType and direction that was drilled.</summary>
        public void NotifyDrill(CellType drilled, DrillDirection direction)
        {
            if (direction != DrillDirection.Down)
                return; // v3.1: lateral/upward drills are streak-neutral, no reset, no increment

            if (!drilled.CanFuse())
                return; // streak-neutral: Hard, HardCracked, AirCapsule, Diamond never touch the streak

            if (CurrentStreak > 0 && drilled == CurrentColor)
            {
                CurrentStreak++;
                StreakGrew?.Invoke(CurrentStreak);
                return;
            }

            if (CurrentStreak > 0)
                StreakBroken?.Invoke(CurrentStreak);

            CurrentColor  = drilled;
            CurrentStreak = 1;
            StreakGrew?.Invoke(CurrentStreak);
        }

        /// <summary>Zero the state for a new run/level.</summary>
        public void Reset()
        {
            CurrentStreak = 0;
            CurrentColor  = CellType.Empty;
        }
    }
}
