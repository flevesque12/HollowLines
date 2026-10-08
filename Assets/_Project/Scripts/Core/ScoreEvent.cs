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
        /// momentum multiplier (Drill / FissureBreak) · burst cell count · bomb chain multiplier · depth award count · cascade step ·
        /// enemy-kill bonus · Boomer-blast parent bonus.
        /// </summary>
        public readonly int Detail;

        /// <summary>v3.2: cascade multiplier applied to these points (1 = none, or a source it never scales).</summary>
        public readonly int CascadeMult;

        /// <summary>v3.2: true if the Danger Zone ×2 was applied to these points.</summary>
        public readonly bool DangerZone;

        public ScoreEvent(int points, ScoreSource source, int detail, int cascadeMult = 1, bool dangerZone = false)
        {
            Points      = points;
            Source      = source;
            Detail      = detail;
            CascadeMult = cascadeMult;
            DangerZone  = dangerZone;
        }

        public override string ToString() =>
            $"[Score] +{Points} ({Source}, detail {Detail}, cascade ×{CascadeMult}{(DangerZone ? ", danger ×2" : "")})";
    }
}
