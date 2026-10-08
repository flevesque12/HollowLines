namespace HollowLines.Core
{
    /// <summary>
    /// What earned the points. Replaces v2.1's LineSource — v3 scores actions, not line clears.
    ///
    /// Design intent (GDD v3 §8):
    ///   Streak       — every drill tap, multiplied by the same-color run. The core trickle.
    ///   Burst        — a chunk fell 2+ rows and shattered. Spectacle = score.
    ///   Bomb         — blast payload, multiplied by the sympathetic chain length.
    ///   Depth        — flat award per new deepest row. Descending is always progress.
    ///   PerfectClear — rare full-void-row jackpot. A bonus, never the loop.
    ///   Diamond      — 🆕R4: flat award per diamond collected (drilled, bombed, or burst-liberated).
    ///   EnemyKill    — 🆕R5.11: Crawler/Boomer kill, scaled by the parent bonus (fall_bonus/chain_mult/1).
    ///   BoomerBlast  — 🆕R5.11: a Boomer's death explosion. Scores like a bomb blast (§6.5).
    ///   Drill        — 🆕R7.5: every drill tap × momentum (v3.2). Replaces Streak, which goes in R7.12.
    ///   Graze        — 🆕R7.5: flat near-miss bonus (drill-momentum.md §M3.2).
    ///   Freefall     — 🆕R7.5: flat bonus per void cell fallen through (§M3.3).
    ///   PowerDrill   — 🆕R7.8: blocks destroyed by the Power Drill's mini shockwave, × the ×6 (⚡D1).
    /// </summary>
    public enum ScoreSource
    {
        Streak,
        Burst,
        Bomb,
        Depth,
        PerfectClear,
        Diamond,
        EnemyKill,
        BoomerBlast,
        Drill,
        Graze,
        Freefall,
        PowerDrill
    }
}
