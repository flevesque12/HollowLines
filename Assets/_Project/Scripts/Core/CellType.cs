namespace HollowLines.Core
{
    /// <summary>
    /// Content of one grid cell.
    /// Values are stable across milestones — never reorder or reuse a number.
    ///
    ///   0  Empty       — void, no physics
    ///   1  ColorA      — fusable, drillable
    ///   2  ColorB      — fusable, drillable
    ///   3  ColorC      — fusable, drillable
    ///   4  Hard        — 2 drill hits; solid chunk; does NOT fuse
    ///   5  HardCracked — Hard after 1 hit; 1 more drill removes it
    ///   6  Steel       — bomb-only; NOT drillable; collapse clears outright
    ///   7  AirCapsule  — drill to collect; restores air; does NOT fuse
    ///   8  Bomb        — NOT drillable; armed by adjacent drill or chunk landing; 1.5 s fuse; cross blast radius 2
    ///   9  Diamond     — drill to collect (1 tap); does NOT fuse; streak-neutral (§6.1/§6.4)
    /// </summary>
    public enum CellType : byte
    {
        Empty       = 0,
        ColorA      = 1,
        ColorB      = 2,
        ColorC      = 3,
        Hard        = 4,
        HardCracked = 5,
        Steel       = 6,
        AirCapsule  = 7,
        Bomb        = 8,
        Diamond     = 9,
    }

    public static class CellTypeExtensions
    {
        /// <summary>Occupies space: blocks movement, participates in gravity, fills rows.</summary>
        public static bool IsSolid(this CellType type) => type != CellType.Empty;

        /// <summary>Can this cell fuse with orthogonal same-type neighbors into one rigid chunk?</summary>
        public static bool CanFuse(this CellType type) =>
            type == CellType.ColorA || type == CellType.ColorB || type == CellType.ColorC;

        /// <summary>
        /// Can the avatar's drill remove or crack this cell?
        /// Steel returns false — bomb-only by GDD design.
        /// </summary>
        public static bool IsDrillable(this CellType type) =>
            type == CellType.ColorA || type == CellType.ColorB || type == CellType.ColorC ||
            type == CellType.Hard   || type == CellType.HardCracked ||
            type == CellType.AirCapsule || type == CellType.Diamond;

        /// <summary>
        /// The cell type left behind after one successful drill hit.
        /// Hard → HardCracked (first hit); everything else → Empty (removed outright).
        /// </summary>
        public static CellType DrillResult(this CellType type) =>
            type == CellType.Hard ? CellType.HardCracked : CellType.Empty;
    }
}
