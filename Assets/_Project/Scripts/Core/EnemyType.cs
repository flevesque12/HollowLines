namespace HollowLines.Core
{
    /// <summary>
    /// v3.1 arcade pivot (§6.5): enemies simplified to 2 types. Digger and Tank were removed —
    /// both interrupted descent (reactive threat / puzzle-solving), which conflicts with the
    /// arcade identity (design rule 9: enemies amplify the action, they don't gate it).
    ///
    ///   Crawler — mobile bonus target. Moves laterally in its row; killed by any chunk landing,
    ///             burst or bomb blast; contact damage while active.
    ///   Boomer  — stationary chain amplifier. Does not move or deal contact damage; its death
    ///             explodes like a bomb, amplifying whatever destroyed it.
    /// </summary>
    public enum EnemyType
    {
        Crawler,
        Boomer
    }
}
