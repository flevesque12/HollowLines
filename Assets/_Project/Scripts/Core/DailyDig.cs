using System;

namespace HollowLines.Core
{
    /// <summary>
    /// Daily Dig (GDD §7): one well per calendar day, identical for every player.
    ///
    /// This is nothing more than a **deterministic date → seed** function — the run itself is an
    /// ordinary endless run (<see cref="EndlessManager"/>) started on that seed. Keeping it that
    /// small is the point: the daily mode inherits every future generator or balance change for
    /// free, and the leaderboard (R3.5) only needs the matching <see cref="LabelFor"/> key.
    ///
    /// Pure C# — no MonoBehaviour, no UnityEngine, no clock of its own (the caller passes the date,
    /// which is also what makes it testable).
    /// </summary>
    public static class DailyDig
    {
        /// <summary>
        /// The seed for a given day. Uses the date only — everyone digging on the same calendar day
        /// gets the same well, whatever the time.
        ///
        /// Callers should pass <c>DateTime.UtcNow</c>: the day must roll over at the same instant
        /// worldwide, or two players "on the same day" would be handed different boards and the
        /// leaderboard would compare runs on different terrain.
        /// </summary>
        public static int SeedFor(DateTime date)
        {
            // Day number since year 1 — a small, dense, strictly increasing integer.
            long ordinal = date.Date.Ticks / TimeSpan.TicksPerDay;

            // SplitMix64 finalizer: consecutive days must produce completely unrelated wells, and
            // raw consecutive integers fed to System.Random give visibly related boards.
            unchecked
            {
                ulong z = (ulong)ordinal + 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                z ^= z >> 31;

                int seed = (int)z;
                // int.MinValue has no positive counterpart, which some System.Random
                // implementations choke on. Nudge it; the day still maps to one stable seed.
                return seed == int.MinValue ? int.MaxValue : seed;
            }
        }

        /// <summary>
        /// Stable identifier for a day: "2026-07-26". Used as the local save key and, later, as the
        /// leaderboard's daily key. Invariant format on purpose — never localized.
        /// </summary>
        public static string LabelFor(DateTime date) =>
            date.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    }
}
