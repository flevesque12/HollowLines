namespace HollowLines.Core
{
    /// <summary>
    /// Configuration for one horizontal strate (geological layer) of the well.
    /// Pass an array of these to StrateGenerator.Build() to compose a full board.
    ///
    /// Rates are per-cell probabilities applied in priority order:
    ///   EmptyRate → SteelRate → BombRate → HardRate → AirRate → DiamondRate → color
    /// The remaining probability becomes color blocks distributed across MaxColors.
    /// All rates should sum to at most 1.0.
    /// </summary>
    public readonly struct StrateDescriptor
    {
        /// <summary>Height of this layer in rows.</summary>
        public readonly int Rows;

        /// <summary>Probability per cell of a Hard block (2 drill hits to clear).</summary>
        public readonly float HardRate;

        /// <summary>Probability per cell of a Steel block (not drillable; bomb-only).</summary>
        public readonly float SteelRate;

        /// <summary>Probability per cell of a buried Bomb (armed by adjacent drill or landing chunk).</summary>
        public readonly float BombRate;

        /// <summary>Probability per cell of an AirCapsule (+25 % air on drill).</summary>
        public readonly float AirRate;

        /// <summary>Probability per cell of an empty hole (no block).</summary>
        public readonly float EmptyRate;

        /// <summary>How many color types appear in this layer: 1 = A only, 2 = A+B, 3 = A+B+C.</summary>
        public readonly int MaxColors;

        /// <summary>
        /// R4: probability per cell of a Diamond pickup. Campaign levels leave this at 0 — they
        /// place an EXACT per-level count instead (StrateGenerator.PlaceDiamonds), because the
        /// campaign win gate requires collecting ALL of them (§6.4) and a per-cell roll can't
        /// guarantee a specific total. Only Endless rolls this, where diamonds are a rare bonus
        /// with no gate.
        /// </summary>
        public readonly float DiamondRate;

        public StrateDescriptor(
            int   rows,
            float hardRate    = 0f,
            float steelRate   = 0f,
            float bombRate    = 0f,
            float airRate     = 0f,
            float emptyRate   = 0f,
            int   maxColors   = 3,
            float diamondRate = 0f)
        {
            Rows        = rows;
            HardRate    = hardRate;
            SteelRate   = steelRate;
            BombRate    = bombRate;
            AirRate     = airRate;
            EmptyRate   = emptyRate;
            MaxColors   = maxColors < 1 ? 1 : maxColors > 3 ? 3 : maxColors;
            DiamondRate = diamondRate;
        }
    }
}
