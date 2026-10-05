namespace HollowLines.Core
{
    /// <summary>
    /// R6.1 (F01): what cost the avatar a heart — or the run. One value per damage source wired in
    /// GameBootstrap, so the death recap can name the culprit instead of a generic "game over".
    /// </summary>
    public enum DeathCause
    {
        None = 0,

        /// <summary>A falling chunk landed on the avatar (GravitySystem.AvatarCrushed).</summary>
        ChunkCrush,

        /// <summary>A Perfect Clear row shift crushed the avatar (CollapseSystem.AvatarCrushed).</summary>
        Collapse,

        /// <summary>Standing in a chunk burst's impact footprint (GravitySystem.AvatarHitByBurst).</summary>
        BurstShockwave,

        /// <summary>Inside a bomb's blast cross (BombSystem.AvatarHitByBlast).</summary>
        BombBlast,

        /// <summary>Contact with an active enemy (EnemySystem.AvatarHitByEnemy).</summary>
        Enemy,

        /// <summary>Air reached 0 (AirSystem.AirDepleted). Never a heart hit — always fatal.</summary>
        Suffocation,
    }
}
