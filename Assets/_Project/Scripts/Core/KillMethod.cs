namespace HollowLines.Core
{
    /// <summary>
    /// How an enemy died (§6.5). ScoreSystem.AwardEnemyKill scales the payout by the parent
    /// bonus that matches the method — fall_bonus for Burst, chain_mult for Bomb, none for Crush or Gas.
    /// </summary>
    public enum KillMethod
    {
        Crush,
        Burst,
        Bomb,

        /// <summary>R8.4: a Crawler caught in a gas puff (ceiling-gas.md §4.3). No bonus, never a detonation.</summary>
        Gas
    }
}
