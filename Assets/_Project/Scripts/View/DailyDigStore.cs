using UnityEngine;

namespace HollowLines.View
{
    /// <summary>
    /// Local record of the player's Daily Dig results, in PlayerPrefs.
    ///
    /// Deliberately **local and best-of**, not "one attempt per day". The GDD floats a single-try
    /// rule, but that only means something once scores are posted somewhere (R3.5); enforcing it
    /// client-side today would just punish honest players while a PlayerPrefs edit walks around it.
    /// So: replay freely, the day keeps your best depth, and the run count is tracked for when the
    /// leaderboard needs to flag "first attempt" submissions.
    ///
    /// Keys are namespaced by the day label ("2026-07-26"), so history accumulates harmlessly.
    /// </summary>
    public static class DailyDigStore
    {
        private const string Prefix = "hollowlines.daily.";

        public readonly struct DailyRecord
        {
            public int BestDepth { get; }
            public int BestScore { get; }
            public int Runs      { get; }

            public DailyRecord(int bestDepth, int bestScore, int runs)
            {
                BestDepth = bestDepth;
                BestScore = bestScore;
                Runs      = runs;
            }

            public bool HasRun => Runs > 0;
        }

        public static DailyRecord Load(string dayLabel) =>
            new DailyRecord(
                PlayerPrefs.GetInt(Key(dayLabel, "depth"), 0),
                PlayerPrefs.GetInt(Key(dayLabel, "score"), 0),
                PlayerPrefs.GetInt(Key(dayLabel, "runs"),  0));

        /// <summary>
        /// Bank a finished run. Depth and score keep their own maxima — depth is the leaderboard
        /// metric (design rule 6) but a deep run is not automatically the highest-scoring one, and
        /// overwriting one with the other would lose a real record.
        /// </summary>
        public static DailyRecord Record(string dayLabel, int depth, int score)
        {
            DailyRecord old = Load(dayLabel);
            var updated = new DailyRecord(
                Mathf.Max(old.BestDepth, depth),
                Mathf.Max(old.BestScore, score),
                old.Runs + 1);

            PlayerPrefs.SetInt(Key(dayLabel, "depth"), updated.BestDepth);
            PlayerPrefs.SetInt(Key(dayLabel, "score"), updated.BestScore);
            PlayerPrefs.SetInt(Key(dayLabel, "runs"),  updated.Runs);
            PlayerPrefs.Save();

            return updated;
        }

        private static string Key(string dayLabel, string field) => Prefix + dayLabel + "." + field;
    }
}
