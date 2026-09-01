namespace HollowLines.Core
{
    /// <summary>
    /// One enemy instance tracked by EnemySystem (§6.5). Enemies are grid-based ACTORS, not
    /// CellTypes — unlike a block they move and carry per-instance state, so they live in a
    /// parallel list rather than as grid cells.
    ///
    /// Immutable, like GridPos: state changes (move, activate, kill) go through the With* methods
    /// below, which return a new value rather than mutating in place. EnemySystem owns the
    /// authoritative copy in its internal list and replaces it wholesale on each change.
    ///
    /// Health is always 1 for both enemy types (§6.5) — Crawler and Boomer die in a single hit.
    /// It is carried on the struct anyway so a future enemy with more HP doesn't need a shape change.
    /// </summary>
    public readonly struct EnemyEntity
    {
        public int        Id       { get; }
        public EnemyType  Type     { get; }
        public GridPos    Position { get; }

        /// <summary>False while buried/dormant — visible on the grid but not moving or dealing damage.</summary>
        public bool IsActive { get; }

        public bool IsAlive { get; }
        public int  Health  { get; }

        /// <summary>Spawns a dormant, full-health enemy at the given position.</summary>
        public EnemyEntity(int id, EnemyType type, GridPos position)
            : this(id, type, position, isActive: false, isAlive: true, health: 1)
        {
        }

        private EnemyEntity(int id, EnemyType type, GridPos position, bool isActive, bool isAlive, int health)
        {
            Id       = id;
            Type     = type;
            Position = position;
            IsActive = isActive;
            IsAlive  = isAlive;
            Health   = health;
        }

        public EnemyEntity WithPosition(GridPos position) =>
            new EnemyEntity(Id, Type, position, IsActive, IsAlive, Health);

        public EnemyEntity Activated() =>
            new EnemyEntity(Id, Type, Position, isActive: true, IsAlive, Health);

        /// <summary>Both enemy types die in one hit (§6.5) — Health drops straight to 0.</summary>
        public EnemyEntity Killed() =>
            new EnemyEntity(Id, Type, Position, IsActive, isAlive: false, health: 0);
    }
}
