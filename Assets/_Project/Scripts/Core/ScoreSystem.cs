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
    ///   Drill           DrillPoints × momentumMult               Tier 3 (×6) → 60   (🔄v3.2)
    ///   Chunk Burst     cells × BurstPointsPerCell × fallBonus   6 cells, fall 4 → 300
    ///                     fallBonus = floor(fallDistance / 2)
    ///   Bomb            blocks × BombPointsPerBlock × chainMult  5 blocks, chain 3 → 375
    ///   Depth           DepthPoints                              +50 per new deepest row
    ///   Perfect Clear   PerfectClearBase (flat)                  +500, cascade does NOT scale it
    ///   Diamond         DiamondPoints (flat)                     +150 per diamond, however collected
    ///   Enemy Kill      killPoints × bonus                       Crawler 100/Boomer 150, ×fall_bonus/chain_mult/1
    ///   Boomer Blast    blocks × BombPointsPerBlock × parentBonus  Boomer's death explosion scores like a bomb
    ///   Graze           GrazePoints (flat)                       +50 near-miss            (🆕v3.2)
    ///   Freefall        FreefallPointsPerCell (flat)             +15 per void cell fallen (🆕v3.2)
    ///   Power Drill     blocks × DrillPoints × momentumMult      4 blocks at ×6 → 240     (🆕v3.2)
    ///   Fissure Break   DrillPoints × momentumMult               Tier 2 (×4) → 40          (🆕v3.2)
    ///
    /// === v3.2 global multipliers (drill-momentum.md §M5) ===
    ///
    ///   score = base × momentum × cascade × danger_zone  +  graze + freefall
    ///
    ///   CascadeMultiplier and DangerZone are INPUTS the caller keeps current (Core systems don't
    ///   reference each other). ScoreSystem alone decides which source each one scales:
    ///     × cascade × danger — Drill, Burst, Bomb, EnemyKill, BoomerBlast, PowerDrill, FissureBreak
    ///     × danger only      — PerfectClear (rule 7: a cascade never scales the jackpot)
    ///     flat               — Depth, Diamond, Graze, Freefall (⚡D3)
    ///   With the defaults (×1, no danger) every v3.1 formula is unchanged.
    ///
    /// === Design rationale (v3) ===
    ///
    /// Every drill tap pays (×momentum, v3.2), so the player is never digging "for free" —
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
        public const int CrawlerKillPoints  = 100;
        public const int BoomerKillPoints   = 150;
        public const int GrazePoints           = 50;  // 🆕v3.2 §M3.2
        public const int FreefallPointsPerCell = 15;  // 🆕v3.2 §M3.3
        public const int DangerZoneMultiplier  = 2;   // 🆕v3.2 §M3.4

        /// <summary>
        /// R7.14: ceiling on the cascade multiplier. Uncapped, the harness measured chains of 37-49 links
        /// (fissures keep gravity busy, so chains rarely close) and level scores of 64k-223k against gates
        /// of 500-5,000 — the §15.2 runaway again. ×3 is also the peak §M5 was designed around
        /// (×6 momentum × 3 cascade × 2 danger = ×36). The chain COUNT is not capped — only what it pays.
        /// </summary>
        public const int MaxCascadeMultiplier = 3;

        /// <summary>Rows per +1 burst multiplier: fallBonus = floor(fallDistance / BurstFallDivisor).</summary>
        public const int BurstFallDivisor = 2;

        // ── v3.2 multiplier inputs (kept current by the caller) ─────

        private int _cascadeMultiplier = 1;

        /// <summary>Running cascade multiplier (×1 when idle), clamped to [1, MaxCascadeMultiplier].</summary>
        public int CascadeMultiplier
        {
            get => _cascadeMultiplier;
            set => _cascadeMultiplier = value < 1 ? 1 : value > MaxCascadeMultiplier ? MaxCascadeMultiplier : value;
        }

        /// <summary>Mirror of AirSystem.IsDangerZone — doubles base points while true (§M3.4).</summary>
        public bool DangerZone { get; set; }

        // ── Observable state ────────────────────────────────────────

        /// <summary>Total score this run.</summary>
        public int Score { get; private set; }

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

        /// <summary>🆕v3.2 Highest momentum multiplier a drill was paid at this run (×1/×2/×4/×6).</summary>
        public float PeakMomentum { get; private set; }

        /// <summary>🆕v3.2 (R7.12) Power Drills fired this run — one per AwardPowerShockwave.</summary>
        public int PowerDrills { get; private set; }

        /// <summary>🆕v3.2 Grazes this run.</summary>
        public int Grazes { get; private set; }

        /// <summary>🆕v3.2 Void cells fallen through this run.</summary>
        public int FreefallCells { get; private set; }

        /// <summary>🆕v3.2 (R7.7b) Blocks broken by Tier 2+ fissures this run.</summary>
        public int FissureBreaks { get; private set; }

        // ── Events ──────────────────────────────────────────────────

        /// <summary>
        /// Fired every time points are awarded, whatever the source.
        /// View hooks here for floating text, popups, SFX, screen shake.
        /// </summary>
        public event Action<ScoreEvent> OnScore;

        // ── Public API ──────────────────────────────────────────────

        /// <summary>
        /// 🆕v3.2 Award one drill tap at the current momentum (MomentumTracker.Multiplier), × cascade
        /// × danger. Every drill pays at least DrillPoints (rule 1): a multiplier below 1 — or NaN —
        /// clamps to ×1. Event Detail = the momentum multiplier, rounded.
        /// </summary>
        public void AwardDrill(float momentumMult)
        {
            if (!(momentumMult >= 1f)) momentumMult = 1f; // also catches NaN

            int pts = Amplified((int)Math.Round(DrillPoints * momentumMult));
            Score += pts;

            if (momentumMult > PeakMomentum)
                PeakMomentum = momentumMult;

            OnScore?.Invoke(new ScoreEvent(pts, ScoreSource.Drill, (int)Math.Round(momentumMult),
                                           CascadeMultiplier, DangerZone));
        }

        /// <summary>
        /// 🆕v3.2 (R7.8) The Power Drill's mini shockwave: every block it destroys is worth one drill,
        /// paid at the Power Drill's momentum (×6 — ⚡D1 says the ×6 covers the whole burst), × cascade
        /// × danger. Negative counts clamp to 0; a multiplier below 1 (or NaN) clamps to ×1.
        /// Event Detail = blocks destroyed.
        /// </summary>
        public void AwardPowerShockwave(int blocksDestroyed, float momentumMult)
        {
            if (blocksDestroyed < 0) blocksDestroyed = 0;
            if (!(momentumMult >= 1f)) momentumMult = 1f;

            int pts = Amplified((int)Math.Round(blocksDestroyed * DrillPoints * momentumMult));
            Score += pts;
            PowerDrills++;

            OnScore?.Invoke(new ScoreEvent(pts, ScoreSource.PowerDrill, blocksDestroyed, CascadeMultiplier, DangerZone));
        }

        /// <summary>
        /// 🆕v3.2 (R7.7b) A Tier 2+ fissure broke a block: worth one drill at the momentum it broke at
        /// (×4 at Tier 2, ×6 during a Power Drill), × cascade × danger. A multiplier below 1 (or NaN)
        /// clamps to ×1. Event Detail = the momentum multiplier, rounded.
        /// </summary>
        public void AwardFissureBreak(float momentumMult)
        {
            if (!(momentumMult >= 1f)) momentumMult = 1f;

            int pts = Amplified((int)Math.Round(DrillPoints * momentumMult));
            Score += pts;
            FissureBreaks++;

            OnScore?.Invoke(new ScoreEvent(pts, ScoreSource.FissureBreak, (int)Math.Round(momentumMult),
                                           CascadeMultiplier, DangerZone));
        }

        /// <summary>🆕v3.2 Flat near-miss bonus (§M3.2). Never multiplied (⚡D3).</summary>
        public void AwardGraze()
        {
            Score += GrazePoints;
            Grazes++;
            OnScore?.Invoke(new ScoreEvent(GrazePoints, ScoreSource.Graze, Grazes));
        }

        /// <summary>
        /// 🆕v3.2 Flat bonus for one void cell fallen through (§M3.3) — wire to AvatarModel.FreefallCell.
        /// Never multiplied (⚡D3).
        /// </summary>
        public void AwardFreefall()
        {
            Score += FreefallPointsPerCell;
            FreefallCells++;
            OnScore?.Invoke(new ScoreEvent(FreefallPointsPerCell, ScoreSource.Freefall, 1));
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
            int pts       = Amplified(cellCount * BurstPointsPerCell * fallBonus);

            Score += pts;

            if (cellCount > BiggestBurst)
            {
                BiggestBurst          = cellCount;
                BiggestBurstFallBonus = fallBonus;
            }

            OnScore?.Invoke(new ScoreEvent(pts, ScoreSource.Burst, cellCount, CascadeMultiplier, DangerZone));
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

            int pts = Amplified(blocksDestroyed * BombPointsPerBlock * chainMult);
            Score += pts;

            if (chainMult > BestBombChain)
                BestBombChain = chainMult;

            OnScore?.Invoke(new ScoreEvent(pts, ScoreSource.Bomb, chainMult, CascadeMultiplier, DangerZone));
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

            // v3.2: the Danger Zone doubles the jackpot like any base action; the cascade never does (rule 7).
            int pts = DangerZone ? PerfectClearBase * DangerZoneMultiplier : PerfectClearBase;
            Score += pts;
            PerfectClears++;

            OnScore?.Invoke(new ScoreEvent(pts, ScoreSource.PerfectClear, cascadeStep, 1, DangerZone));
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

        /// <summary>
        /// Award points for killing a Crawler or Boomer (§6.5). Crawler = 100 pts, Boomer = 150 pts,
        /// both scaled by the parent bonus.
        /// </summary>
        /// <param name="type">Which enemy died — sets the base value.</param>
        /// <param name="bonus">
        ///   fall_bonus if killed by a chunk burst, chain_mult if killed by a bomb blast, or 1 for a
        ///   plain crush kill. GameBootstrap reads this off GravitySystem/BombSystem and passes it
        ///   through — EnemySystem's own EnemyKilled event only carries the KillMethod, not the
        ///   bonus value itself (Core systems don't reference each other, §7). Values &lt; 1 clamp to 1.
        /// </param>
        public void AwardEnemyKill(EnemyType type, int bonus = 1)
        {
            if (bonus < 1) bonus = 1;

            int basePoints = type == EnemyType.Boomer ? BoomerKillPoints : CrawlerKillPoints;
            int pts = Amplified(basePoints * bonus);
            Score += pts;

            OnScore?.Invoke(new ScoreEvent(pts, ScoreSource.EnemyKill, bonus, CascadeMultiplier, DangerZone));
        }

        /// <summary>
        /// Award points for a Boomer's death blast (§6.5) — it explodes like a bomb, so it scores
        /// like one: same BombPointsPerBlock rate, scaled by the SAME parent bonus that killed the
        /// Boomer in the first place (carried straight through, not recomputed).
        /// </summary>
        /// <param name="blocksDestroyed">Blocks removed by the Boomer's radius-1 blast.</param>
        /// <param name="parentBonus">fall_bonus or chain_mult from whatever killed the Boomer.</param>
        public void AwardBoomerBlast(int blocksDestroyed, int parentBonus)
        {
            if (blocksDestroyed < 0) blocksDestroyed = 0;
            if (parentBonus     < 1) parentBonus     = 1;

            int pts = Amplified(blocksDestroyed * BombPointsPerBlock * parentBonus);
            Score += pts;

            OnScore?.Invoke(new ScoreEvent(pts, ScoreSource.BoomerBlast, parentBonus, CascadeMultiplier, DangerZone));
        }

        /// <summary>Reset all state for a new run.</summary>
        public void Reset()
        {
            Score                 = 0;
            BiggestBurst          = 0;
            BiggestBurstFallBonus = 0;
            BestBombChain         = 0;
            PerfectClears         = 0;
            MaxDepth              = 0;
            PeakMomentum          = 0f;
            PowerDrills           = 0;
            Grazes                = 0;
            FreefallCells         = 0;
            FissureBreaks         = 0;
            CascadeMultiplier     = 1;
            DangerZone            = false;
        }

        /// <summary>base × cascade × danger — the shared multiplier for every "base action" (§M5).</summary>
        private int Amplified(int basePoints)
        {
            int pts = basePoints * CascadeMultiplier;
            return DangerZone ? pts * DangerZoneMultiplier : pts;
        }
    }
}
