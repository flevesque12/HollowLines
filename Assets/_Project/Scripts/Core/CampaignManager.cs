using System;

namespace HollowLines.Core
{
    /// <summary>
    /// Tracks campaign progression across 10 levels.
    /// Win condition (v3): reach the bottom — the avatar gets within WinDepthFromFloor rows of the
    /// solid floor. The v2.1 void-line gate is gone: depth is always forward (design rule 6), and
    /// scoring now rewards drilling, bursting and bombing along the way rather than gating on them.
    ///
    /// v3.1 arcade pivot (§5.7, §9): the R4 diamond gate is replaced by a score gate. On levels 4+,
    /// reaching the bottom is necessary but not sufficient — the caller also passes the current
    /// ScoreSystem.Score into NotifyAvatarPosition, and it must meet ScoreMinimumForLevel(CurrentLevel).
    /// Levels 1-3 have a minimum of 0 (tutorial, no gate). Diamonds are no longer required to win —
    /// they're still placed (§5.8) and each is worth +150 pts toward the same score gate.
    /// Pure C# — no MonoBehaviour, no UnityEngine.
    /// </summary>
    public sealed class CampaignManager
    {
        public const int LevelCount        = 10;
        public const int WinDepthFromFloor = 2; // rows above solid floor row

        public int  CurrentLevel       { get; private set; }
        public bool IsCampaignComplete { get; private set; }

        // Fired with the completed level number; subscribe → show results, then call Advance().
        public event Action<int> LevelCompleted;
        // Fired once after Advance() is called past level 10.
        public event Action CampaignCompleted;

        private bool _levelWon; // guard: fire LevelCompleted only once per level

        public CampaignManager(int startLevel = 1)
        {
            Reset(startLevel);
        }

        /// <summary>
        /// Campaign air drain in % per second. Levels 1-3 breathe easier for onboarding.
        ///
        /// Raised for v3 (§15.1). Drain is time-based, so it only bites once a run is long enough
        /// for it to act: at the old 14-26 row depths a descent took ~2 s and NO drain rate worked
        /// (making 2 s costly would need ~20 %/s, which kills exploration outright). With depths
        /// tripled, a descent runs 5-9 s and 7 %/s consumes ~60 % of the tank — enough that
        /// capsules, bursts and bomb chains become things you actually need. Depth and drain are
        /// multiplicative; tune them together, never one alone.
        /// </summary>
        public static float DrainRateForLevel(int level) => level <= 3 ? 4f : 7f;

        /// <summary>
        /// Campaign wobble telegraph in seconds. The longer window on levels 1-3 slows crush
        /// pressure while players learn to read the telegraph.
        /// From level 4 on, the GDD's 0.6 s applies: dodging inside that window is the skill.
        /// </summary>
        public static float WobbleDurationForLevel(int level) => level <= 3 ? 0.8f : 0.6f;

        /// <summary>
        /// Returns the procedural board for the current level.
        /// Result is deterministic — same level always produces the same board.
        /// </summary>
        public string[] BuildCurrentBoard() =>
            StrateGenerator.CampaignBoard(CurrentLevel);

        /// <summary>
        /// Call once per Update after AvatarModel.Tick(). Detects the win condition: depth, gated by
        /// the level's score minimum (v3.1, §5.7).
        /// </summary>
        /// <param name="avatarPos">The avatar's current grid position.</param>
        /// <param name="boardHeight">Total row count of the current board (grid.Height).</param>
        /// <param name="currentScore">
        ///   ScoreSystem.Score for the current run. Defaults to int.MaxValue so callers that don't
        ///   care about the gate (the tutorial showcase, older tests) keep depth as the only
        ///   requirement — it always clears ScoreMinimumForLevel.
        /// </param>
        public void NotifyAvatarPosition(GridPos avatarPos, int boardHeight, int currentScore = int.MaxValue)
        {
            if (_levelWon || IsCampaignComplete) return;

            int threshold = boardHeight - WinDepthFromFloor;
            if (avatarPos.Y >= threshold && currentScore >= ScoreMinimumForLevel(CurrentLevel))
            {
                _levelWon = true;
                LevelCompleted?.Invoke(CurrentLevel);
            }
        }

        /// <summary>
        /// Minimum ScoreSystem.Score required to win a level, on top of reaching the bottom
        /// (v3.1 arcade pivot, §5.7/§9 — replaces the R4 diamond gate). Levels 1-3 are the tutorial
        /// ramp and have no gate. A tunnel-bot earns ~50 pts/row from Depth alone, so the level 4
        /// minimum is trivially cleared by descending; the level 8+ minimums require engaging with
        /// bursts, bombs or enemy kills along the way.
        /// </summary>
        public static int ScoreMinimumForLevel(int level)
        {
            if (level <= 3) return 0;
            if (level <= 5) return 500;
            if (level <= 7) return 1500;
            if (level <= 9) return 3000;
            return 5000; // level 10
        }

        /// <summary>
        /// Advance to the next level. Call after the level-complete transition finishes.
        /// Fires CampaignCompleted when the player clears level 10.
        /// </summary>
        public void Advance()
        {
            if (IsCampaignComplete) return;

            if (CurrentLevel >= LevelCount)
            {
                IsCampaignComplete = true;
                CampaignCompleted?.Invoke();
                return;
            }

            CurrentLevel++;
            _levelWon = false;
        }

        /// <summary>Clear the win guard for a retry of the current level (player restart).</summary>
        public void RestartLevel()
        {
            _levelWon = false;
        }

        /// <summary>Resets progression to the given start level (default 1).</summary>
        public void Reset(int startLevel = 1)
        {
            CurrentLevel       = Math.Max(1, Math.Min(startLevel, LevelCount));
            IsCampaignComplete = false;
            _levelWon          = false;
        }
    }
}
