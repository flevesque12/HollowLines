namespace HollowLines.Core
{
    /// <summary>
    /// Immutable snapshot of a scoring event (GDD v3 §5.6).
    /// The View layer consumes these for floating text, popups, SFX pitch, and shake intensity.
    /// </summary>
    public readonly struct ScoreEvent
    {
        /// <summary>Points awarded this event.</summary>
        public readonly int Points;

        /// <summary>What earned the points.</summary>
        public readonly ScoreSource Source;

        /// <summary>
        /// Source-specific magnitude, for popups that read "×N":
        /// streak step · burst cell count · bomb chain multiplier · depth award count · cascade step.
        /// </summary>
        public readonly int Detail;

        public ScoreEvent(int points, ScoreSource source, int detail)
        {
            Points = points;
            Source = source;
            Detail = detail;
        }

        public override string ToString() => $"[Score] +{Points} ({Source}, detail {Detail})";
    }
}
