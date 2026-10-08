namespace HollowLines.Core
{
    /// <summary>
    /// The cardinal direction of a drill. Carried on <see cref="AvatarModel.Drilled"/> so
    /// downstream systems (the Power Drill follow-through, R7.8) can tell a downward drill from a lateral one
    /// without re-deriving it from raw dx/dy.
    /// </summary>
    public enum DrillDirection
    {
        Up,
        Down,
        Left,
        Right
    }
}
