namespace HollowLines.Core
{
    /// <summary>
    /// How an enemy died (§6.5). ScoreSystem.AwardEnemyKill scales the payout by the parent
    /// bonus that matches the method — fall_bonus for Burst, chain_mult for Bomb, none for Crush.
    /// </summary>
    public enum KillMethod
    {
        Crush,
        Burst,
        Bomb
    }
}
