using System;

namespace HollowLines.Core
{
    /// <summary>
    /// Tracks campaign progression across 10 levels.
    /// Win condition (v3): reach the bottom — the avatar gets within WinDepthFromFloor rows of the
    /// solid floor. The v2.1 void-line gate is gone: depth is always forward (design rule 6), and
    /// scoring now rewards drilling, bursting and bombing along the way rather than gating on them.
    ///
    /// R4: on levels with diamonds (4+, §9), reaching the bottom is necessary but not sufficient —
    /// the caller also passes DiamondSystem.IsComplete into NotifyAvatarPosition. Levels 1-3 have no
    /// diamonds, and the parameter defaults to true, so the gate stays a no-op there (§6.4).
    /// Pure C# — no MonoBehaviour, no UnityEngine. CampaignManager does not hold a DiamondSystem
    /// reference; Core systems don't reference each other, the caller reads the flag (§7).
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
        /// diamond collection on levels that have any (R4, §6.4).
        /// </summary>
        /// <param name="avatarPos">The avatar's current grid position.</param>
        /// <param name="boardHeight">Total row count of the current board (grid.Height).</param>
        /// <param name="diamondsComplete">
        ///   DiamondSystem.IsComplete for the current board. Defaults to true so levels without
        ///   diamonds (1-3) and callers that predate R4 keep depth as the only requirement.
        /// </param>
        public void NotifyAvatarPosition(GridPos avatarPos, int boardHeight, bool diamondsComplete = true)
        {
            if (_levelWon || IsCampaignComplete) return;

            int threshold = boardHeight - WinDepthFromFloor;
            if (avatarPos.Y >= threshold && diamondsComplete)
            {
                _levelWon = true;
                LevelCompleted?.Invoke(CurrentLevel);
            }
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
