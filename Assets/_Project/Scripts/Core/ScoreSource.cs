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
    /// </summary>
    public enum ScoreSource
    {
        Streak,
        Burst,
        Bomb,
        Depth,
        PerfectClear,
        Diamond
    }
}
