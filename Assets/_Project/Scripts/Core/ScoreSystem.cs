using System;

namespace HollowLines.Core
{
    /// <summary>
    /// Pure-C# scoring engine. No MonoBehaviour, no Unity dependency.
    /// Unit-testable, deterministic, event-driven.
    ///
    /// === Point table (GDD v3 §8) ===
    ///
    ///   Action          Formula                                  Example
    ///   ──────────────  ───────────────────────────────────────  ───────
    ///   Drill           DrillPoints × streakStep                 streak 5 → 50
    ///   Chunk Burst     cells × BurstPointsPerCell × fallBonus   6 cells, fall 4 → 300
    ///                     fallBonus = floor(fallDistance / 2)
    ///   Bomb            blocks × BombPointsPerBlock × chainMult  5 blocks, chain 3 → 375
    ///   Depth           DepthPoints                              +50 per new deepest row
    ///   Perfect Clear   PerfectClearBase (flat)                  +500, cascade does NOT scale it
    ///   Diamond         DiamondPoints (flat)                     +150 per diamond, however collected
    ///
    /// === Design rationale (v3) ===
    ///
    /// Every drill tap pays (×streak), so the player is never digging "for free" —
    /// design rule 1. Burst and Bomb both scale on spectacle, aligning what looks
    /// impressive with what scores. Perfect Clear stays a rare jackpot rather than
    /// the core loop it was in v2.1.
    /// </summary>
    public sealed class ScoreSystem
    {
        // ── Tunables ────────────────────────────────────────────────
        // Constants for now; these become ScriptableObject fields when balance passes start.

        public const int DrillPoints        = 10;
        public const int BurstPointsPerCell = 25;
        public const int BombPointsPerBlock = 25;
        public const int DepthPoints        = 50;
        public const int PerfectClearBase   = 500;
        public const int DiamondPoints      = 150;

        /// <summary>Rows per +1 burst multiplier: fallBonus = floor(fallDistance / BurstFallDivisor).</summary>
        public const int BurstFallDivisor = 2;

        // ── Observable state ────────────────────────────────────────

        /// <summary>Total score this run.</summary>
        public int Score { get; private set; }

        /// <summary>Longest color streak reached this run.</summary>
        public int BestStreak { get; private set; }

        /// <summary>Which color <see cref="BestStreak"/> was built on. Empty if no color was drilled.</summary>
        public CellType BestStreakColor { get; private set; }

        /// <summary>Cell count of the largest chunk burst this run.</summary>
        public int BiggestBurst { get; private set; }

        /// <summary>Fall multiplier of the burst recorded in <see cref="BiggestBurst"/>.</summary>
        public int BiggestBurstFallBonus { get; private set; }

        /// <summary>Longest sympathetic bomb chain this run.</summary>
        public int BestBombChain { get; private set; }

        /// <summary>How many Perfect Clears this run.</summary>
        public int PerfectClears { get; private set; }

        /// <summary>Deepest row index reached this run — the headline stat on the run summary.</summary>
        public int MaxDepth { get; private set; }

        // ── Events ──────────────────────────────────────────────────

        /// <summary>
        /// Fired every time points are awarded, whatever the source.
        /// View hooks here for floating text, popups, SFX, screen shake.
        /// </summary>
        public event Action<ScoreEvent> OnScore;

        // ── Public API ──────────────────────────────────────────────

        /// <summary>
        /// Award points for one drill tap, scaled by the current color streak.
        /// </summary>
        /// <param name="streakStep">
        ///   1-based streak count from StreakTracker.CurrentStreak.
        ///   Values &lt; 1 are clamped to 1 — a drill always pays at least once.
        /// </param>
        /// <param name="streakColor">
        ///   The color the streak is running on, recorded alongside <see cref="BestStreak"/> so the
        ///   run summary can read "×12 amber". Optional: omit it and only the count is tracked.
        /// </param>
        public void AwardDrill(int streakStep, CellType streakColor = CellType.Empty)
        {
            if (streakStep < 1) streakStep = 1;

            int pts = DrillPoints * streakStep;
            Score += pts;

            if (streakStep > BestStreak)
            {
                BestStreak      = streakStep;
                BestStreakColor = streakColor;
            }

            OnScore?.Invoke(new ScoreEvent(pts, ScoreSource.Streak, streakStep));
        }

        /// <summary>
        /// Award points for a chunk that shattered on impact.
        /// </summary>
        /// <param name="cellCount">Cells in the burst chunk.</param>
        /// <param name="fallDistance">Rows fallen; every 2 rows adds one multiplier step.</param>
        public void AwardBurst(int cellCount, int fallDistance)
        {
            if (cellCount   < 0) cellCount   = 0;
            if (fallDistance < 0) fallDistance = 0;

            int fallBonus = fallDistance / BurstFallDivisor; // integer division IS the floor
            int pts       = cellCount * BurstPointsPerCell * fallBonus;

            Score += pts;

            if (cellCount > BiggestBurst)
            {
                BiggestBurst          = cellCount;
                BiggestBurstFallBonus = fallBonus;
            }

            OnScore?.Invoke(new ScoreEvent(pts, ScoreSource.Burst, cellCount));
        }

        /// <summary>
        /// Award points for one bomb blast, scaled by its sympathetic chain position.
        /// </summary>
        /// <param name="blocksDestroyed">Blocks removed by this blast.</param>
        /// <param name="chainMult">1 for a lone bomb, +1 per sympathetic detonation.</param>
        public void AwardBomb(int blocksDestroyed, int chainMult)
        {
            if (blocksDestroyed < 0) blocksDestroyed = 0;
            if (chainMult       < 1) chainMult       = 1;

            int pts = blocksDestroyed * BombPointsPerBlock * chainMult;
            Score += pts;

            if (chainMult > BestBombChain)
                BestBombChain = chainMult;

            OnScore?.Invoke(new ScoreEvent(pts, ScoreSource.Bomb, chainMult));
        }

        /// <summary>
        /// Award the flat bonus for reaching a new deepest row.
        /// </summary>
        /// <param name="row">
        ///   The row index just reached, from DepthTracker.NewDepthReached.
        ///   <see cref="MaxDepth"/> keeps the deepest value seen: ScoreSystem spans a whole run
        ///   while DepthTracker resets per board, so a shallower new level must not erase the record.
        ///   Negative values are clamped to 0.
        /// </param>
        public void AwardDepth(int row)
        {
            if (row < 0) row = 0;

            Score += DepthPoints;
            if (row > MaxDepth)
                MaxDepth = row;

            OnScore?.Invoke(new ScoreEvent(DepthPoints, ScoreSource.Depth, row));
        }

        /// <summary>
        /// Award the rare full-void-row jackpot. FLAT — see the cascade note below.
        /// </summary>
        /// <param name="cascadeStep">
        ///   1-based index within a run of consecutive void rows. Reported in the event's Detail
        ///   (the HUD popup and the chain SFX still read it) but it no longer scales the points.
        ///
        ///   The v3 draft specified `500 × cascade`, which contradicted design rule 7's flat
        ///   "+500". Measured with the real generator, cascades averaged ×4–×7 and Perfect Clear
        ///   took 79–91 % of every run's score — the exact opposite of "a bonus, not a goal".
        ///   Rule 7 is non-negotiable, so the multiplier is gone. See §15.2.
        /// </param>
        public void AwardPerfectClear(int cascadeStep)
        {
            if (cascadeStep < 1) cascadeStep = 1;

            int pts = PerfectClearBase;
            Score += pts;
            PerfectClears++;

            OnScore?.Invoke(new ScoreEvent(pts, ScoreSource.PerfectClear, cascadeStep));
        }

        /// <summary>
        /// Award the flat bonus for collecting a diamond (R4, §6.4). Same value whether it was
        /// drilled directly, freed by a bomb blast, or freed by a burst shockwave — DiamondSystem
        /// tracks the collection itself (and the campaign win gate); this just banks the points.
        /// </summary>
        public void AwardDiamond()
        {
            int pts = DiamondPoints;
            Score += pts;

            OnScore?.Invoke(new ScoreEvent(pts, ScoreSource.Diamond, 1));
        }

        /// <summary>Reset all state for a new run.</summary>
        public void Reset()
        {
            Score                 = 0;
            BestStreak            = 0;
            BestStreakColor       = CellType.Empty;
            BiggestBurst          = 0;
            BiggestBurstFallBonus = 0;
            BestBombChain         = 0;
            PerfectClears         = 0;
            MaxDepth              = 0;
        }
    }
}
