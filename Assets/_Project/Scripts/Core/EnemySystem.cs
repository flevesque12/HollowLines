using System;
using System.Collections.Generic;

namespace HollowLines.Core
{
    /// <summary>
    /// Manages enemy entities as grid-based ACTORS, not CellTypes — enemies move and carry
    /// per-instance state, so they live in a parallel list rather than as grid cells (§6.5).
    ///
    /// R5.6 delivered core lifecycle — spawn, activate, kill by crush/burst/bomb, queries.
    /// R5.7 added Crawler lateral movement. R5.8 added the Boomer's death blast: radius-1 cardinal
    /// destruction, capsule/diamond liberation, and a capped chain reaction through other enemies —
    /// which is why this class takes a GridModel (matching GravitySystem/BombSystem convention: a
    /// Core system that needs to read/write cells holds the grid via its constructor, not a
    /// reference passed around per-call). R5.9 added the three activation triggers: adjacent drill,
    /// burst/bomb zone proximity, and Endless viewport scroll. R5.10 (this pass) adds avatar contact
    /// damage: only an ACTIVE Crawler standing on the avatar's cell fires AvatarHitByEnemy — a
    /// Boomer never deals contact damage (its explosion is a bonus for the player, not a hazard,
    /// §6.5), and a dormant enemy of either type is buried and cannot touch anything.
    ///
    /// Dormant vs active is a CONTACT distinction: an enemy is buried/dormant until activated, but
    /// it is still physically present underground — a chunk landing, a burst or a bomb blast kills
    /// it exactly the same whether it's dormant or active. Only the avatar-touch check (R5.10, this
    /// pass) cares whether IsActive is true.
    ///
    /// Pure C# — no MonoBehaviour, no UnityEngine.
    /// </summary>
    public sealed class EnemySystem
    {
        /// <summary>A dormant enemy was placed on the board.</summary>
        public event Action<int, EnemyType, GridPos> EnemySpawned;

        /// <summary>A dormant enemy started moving/dealing damage.</summary>
        public event Action<int> EnemyActivated;

        /// <summary>
        /// An enemy died: (id, type, how it died, the parent bonus that scaled the kill).
        ///
        /// The bonus is carried ON the event rather than left for the caller to look up after the
        /// fact — it is fall_bonus for a burst kill, chain_mult for a bomb kill, and 1 for a plain
        /// crush. The §7 draft had GameBootstrap switch on KillMethod and read it back off
        /// GravitySystem.LastFallBonus / BombSystem.LastChainMult; that needs two "last thing that
        /// happened" fields that don't exist, and only works while the handler runs inside the
        /// originating burst/bomb resolution. EnemySystem already has the exact value in hand at
        /// kill time (it's the same one it threads through KillOne), so it just passes it along —
        /// the same reasoning that made NotifyBombBlast take chainMult explicitly (R5.6).
        /// </summary>
        public event Action<int, EnemyType, KillMethod, int> EnemyKilled;

        /// <summary>
        /// An active Crawler is standing on the avatar's cell. Fired every Tick the overlap holds,
        /// not just on the frame contact begins — HealthSystem's i-frames (the caller's job to wire,
        /// §6.5) are what make repeated hits harmless, not de-duplication here.
        /// </summary>
        public event Action<int> AvatarHitByEnemy;

        /// <summary>
        /// A Boomer's death blast: (position, parent bonus, blocks destroyed).
        ///
        /// The parent bonus is what killed it — fall_bonus for a burst kill, chain_mult for a bomb
        /// kill — so the secondary explosion scores like the blast that triggered it (design rule 9:
        /// enemies amplify the action). Fires once per Boomer that actually detonates, including
        /// chain-reaction hops (up to BoomerChainCap deep).
        ///
        /// Fired AFTER the blast resolves, carrying its block count — the same ordering (and for the
        /// same reason) as BombSystem's BombScored: the consumer's job is to score it, and the count
        /// isn't known until the cells are actually cleared. Steel softening to Hard is NOT counted,
        /// matching BombSystem's "softening is not a destruction" rule.
        /// </summary>
        public event Action<GridPos, int, int> BoomerDetonated;

        /// <summary>
        /// A Boomer's blast freed an air capsule instead of destroying it (same principle as
        /// BombSystem.AirCapsuleLiberated — design rule 3, bombs/booms are friends).
        /// Caller wires this to AirSystem.RestoreCapsule().
        /// </summary>
        public event Action<GridPos> AirCapsuleLiberated;

        /// <summary>A Boomer's blast freed a diamond instead of destroying it.</summary>
        public event Action<GridPos> DiamondLiberated;

        /// <summary>Seconds between one-cell Crawler steps (§6.5).</summary>
        public const float CrawlerMoveInterval = 0.8f;

        /// <summary>Cardinal reach of a Boomer's death blast — matches BombSystem.DirectBlastRadius (§6.5).</summary>
        public const int BoomerBlastRadius = 1;

        /// <summary>
        /// Maximum Boomer-detonation chain depth (§6.5). The Boomer directly killed by the
        /// triggering burst/bomb is depth 1; each further Boomer killed by the PREVIOUS one's blast
        /// is one depth deeper. A Boomer at depth &gt; this cap still dies (EnemyKilled fires) but
        /// does not detonate — no BoomerDetonated, no blast, chain stops there.
        /// </summary>
        public const int BoomerChainCap = 3;

        private readonly GridModel _grid;
        private readonly Dictionary<int, EnemyEntity> _enemies = new Dictionary<int, EnemyEntity>();

        /// <summary>Per-Crawler movement state — NOT on EnemyEntity, which is a plain immutable
        /// snapshot with no notion of "which way it's walking". Only Crawlers get an entry.</summary>
        private readonly Dictionary<int, CrawlerState> _crawlerState = new Dictionary<int, CrawlerState>();

        private int _nextId;

        public EnemySystem(GridModel grid)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        }

        /// <summary>Places a dormant, full-health enemy. Returns its id (also carried by EnemySpawned).</summary>
        public int SpawnEnemy(EnemyType type, GridPos pos)
        {
            int id = _nextId++;
            _enemies[id] = new EnemyEntity(id, type, pos);
            if (type == EnemyType.Crawler)
                _crawlerState[id] = new CrawlerState(); // starts moving right (§6.5)

            EnemySpawned?.Invoke(id, type, pos);
            return id;
        }

        /// <summary>Wakes a dormant enemy. No-op for an unknown id, an already-active one, or a dead one.</summary>
        public void ActivateEnemy(int id)
        {
            if (!_enemies.TryGetValue(id, out EnemyEntity enemy) || !enemy.IsAlive || enemy.IsActive)
                return;

            _enemies[id] = enemy.Activated();
            EnemyActivated?.Invoke(id);
        }

        /// <summary>
        /// Call after a successful drill. Wakes any dormant enemy in one of the drilled cell's four
        /// cardinal neighbors — same trigger shape as BombSystem.NotifyDrilled/ArmAdjacent (§6.5).
        /// </summary>
        public void NotifyAdjacentDrill(GridPos drilledPos) => ActivateAdjacent(drilledPos);

        /// <summary>
        /// Endless mode has no per-board activation event — enemies wake as the camera scrolls them
        /// into view (§6.5). Wakes every dormant enemy whose row falls within [minRow, maxRow]
        /// (inclusive both ends). GameBootstrap calls this from the camera's current view bounds.
        /// </summary>
        public void ActivateInViewport(int minRow, int maxRow)
        {
            foreach (int id in new List<int>(_enemies.Keys))
            {
                EnemyEntity enemy = _enemies[id];
                if (enemy.IsAlive && !enemy.IsActive &&
                    enemy.Position.Y >= minRow && enemy.Position.Y <= maxRow)
                {
                    ActivateEnemy(id);
                }
            }
        }

        /// <summary>
        /// Advances active Crawlers (§6.5) and checks avatar contact. Dormant enemies and Boomers
        /// are untouched by both halves — a Boomer never moves and never deals contact damage (only
        /// its death blast is dangerous, and that's a bonus, not a hazard), and a dormant Crawler is
        /// buried: it neither walks nor hurts anyone. No climbing, no falling, no drilling: a Crawler
        /// never leaves its spawn row.
        ///
        /// The contact check runs every Tick for every active Crawler, whether or not it stepped
        /// this frame — the avatar walking into a stationary Crawler counts exactly like a Crawler
        /// stepping into the avatar. HealthSystem's i-frames (wired by the caller) are what make
        /// repeated overlap harmless; this method fires on every overlapping frame regardless.
        /// </summary>
        public void Tick(float dt, GridPos avatarPos)
        {
            foreach (int id in new List<int>(_enemies.Keys))
            {
                EnemyEntity enemy = _enemies[id];
                if (!enemy.IsAlive || !enemy.IsActive || enemy.Type != EnemyType.Crawler)
                    continue;

                CrawlerState state = _crawlerState[id];
                state.Timer += dt;

                while (state.Timer >= CrawlerMoveInterval)
                {
                    state.Timer -= CrawlerMoveInterval;
                    enemy = StepCrawler(enemy, state);
                }

                _enemies[id] = enemy;

                if (enemy.Position == avatarPos)
                    AvatarHitByEnemy?.Invoke(id);
            }
        }

        /// <summary>The living enemy at this cell, if any — dormant or active, both count.</summary>
        public EnemyEntity? GetEnemyAt(GridPos pos)
        {
            foreach (EnemyEntity enemy in _enemies.Values)
            {
                if (enemy.IsAlive && enemy.Position == pos)
                    return enemy;
            }
            return null;
        }

        /// <summary>Snapshot of every currently-living enemy (dormant and active).</summary>
        public List<EnemyEntity> GetAllAlive()
        {
            var alive = new List<EnemyEntity>();
            foreach (EnemyEntity enemy in _enemies.Values)
            {
                if (enemy.IsAlive)
                    alive.Add(enemy);
            }
            return alive;
        }

        /// <summary>
        /// A chunk finished falling. Any enemy standing in its footprint dies (design: gravity
        /// doesn't care whether you're the player or a Crawler). Never triggers BoomerDetonated —
        /// only Burst and Bomb kills amplify (§6.5).
        /// </summary>
        public void NotifyChunkLanded(List<GridPos> landingCells) =>
            KillInZone(landingCells, KillMethod.Crush, boomerBonus: null);

        /// <summary>
        /// A chunk shattered. Enemies in the burst footprint OR the 1-cell shockwave ring die.
        /// A Boomer killed this way detonates with fall_bonus = floor(fallDist / BurstFallDivisor) —
        /// the same divisor ScoreSystem.AwardBurst uses, so the secondary blast's bonus always
        /// matches what the triggering burst itself scored.
        /// </summary>
        public void NotifyBurst(List<GridPos> burstCells, List<GridPos> shockwaveCells, int fallDist)
        {
            var zone = new HashSet<GridPos>(burstCells);
            zone.UnionWith(shockwaveCells);

            // Wake dormant enemies bordering the effect zone BEFORE resolving kills — a Crawler
            // just outside the blast should wake up even though it isn't the one dying (§6.5).
            ActivateAdjacentToZone(zone);

            int fallBonus = fallDist / ScoreSystem.BurstFallDivisor; // integer division IS the floor
            KillInZone(zone, KillMethod.Burst, fallBonus);
        }

        /// <summary>
        /// A bomb (or a sympathetic chain of them) detonated. Enemies in the blast footprint die.
        /// A Boomer killed this way detonates with the SAME chainMult that scored the triggering
        /// blast — the caller (GameBootstrap) reads it off BombSystem.BombScored/LastChainMult and
        /// passes it straight through, since Core systems don't reference each other (§7).
        /// </summary>
        public void NotifyBombBlast(List<GridPos> blastCells, int chainMult)
        {
            ActivateAdjacentToZone(blastCells);
            KillInZone(blastCells, KillMethod.Bomb, chainMult);
        }

        /// <summary>Clears every enemy for a new run/level.</summary>
        public void Reset()
        {
            _enemies.Clear();
            _crawlerState.Clear();
            _nextId = 0;
        }

        // ── Private ──────────────────────────────────────────────────

        /// <summary>Wakes a dormant enemy in any of the four cells cardinally adjacent to origin.</summary>
        private void ActivateAdjacent(GridPos origin)
        {
            ActivateDormantAt(origin.Above);
            ActivateDormantAt(origin.Below);
            ActivateDormantAt(origin.Offset(-1, 0));
            ActivateDormantAt(origin.Offset(1, 0));
        }

        private void ActivateAdjacentToZone(IEnumerable<GridPos> zone)
        {
            foreach (GridPos cell in zone)
                ActivateAdjacent(cell);
        }

        private void ActivateDormantAt(GridPos pos)
        {
            EnemyEntity? enemy = GetEnemyAt(pos);
            if (enemy.HasValue && !enemy.Value.IsActive)
                ActivateEnemy(enemy.Value.Id);
        }

        private void KillInZone(ICollection<GridPos> zone, KillMethod method, int? boomerBonus)
        {
            if (zone.Count == 0)
                return;

            // Snapshot the ids: killing mutates _enemies, and iterating a dictionary while
            // reassigning its values is safe in C#, but we still want a stable id list up front.
            // Note: if two Boomers both sit in the zone AND are adjacent to each other, whichever is
            // processed first will chain-kill the second via its own blast before this loop reaches
            // it — the second Boomer still detonates, just at chain depth 2 instead of 1. Rare and
            // harmless (same total detonations either way); not worth the bookkeeping to avoid.
            var ids = new List<int>(_enemies.Keys);
            foreach (int id in ids)
            {
                EnemyEntity enemy = _enemies[id];
                if (!enemy.IsAlive || !zone.Contains(enemy.Position))
                    continue;

                KillOne(enemy, method, boomerBonus, chainDepth: 1);
            }
        }

        /// <summary>
        /// Kills one specific enemy and, if it's a Boomer detonating (bonus present AND still within
        /// the chain cap), fires BoomerDetonated and applies its blast. This is the single choke
        /// point every kill path (zone kills and Boomer-chain kills alike) funnels through, so the
        /// cap and the "Crush never detonates" rule only need to be enforced in one place.
        /// </summary>
        private void KillOne(EnemyEntity enemy, KillMethod method, int? boomerBonus, int chainDepth)
        {
            _enemies[enemy.Id] = enemy.Killed();
            _crawlerState.Remove(enemy.Id); // a dead Crawler doesn't need a walking direction any more

            // A crush carries no multiplier, so the kill still scores at ×1 (ScoreSystem clamps it too).
            EnemyKilled?.Invoke(enemy.Id, enemy.Type, method, boomerBonus ?? 1);

            if (enemy.Type != EnemyType.Boomer || !boomerBonus.HasValue || chainDepth > BoomerChainCap)
                return;

            int destroyed = DetonateBoomer(enemy.Position, boomerBonus.Value, chainDepth);
            BoomerDetonated?.Invoke(enemy.Position, boomerBonus.Value, destroyed);
        }

        /// <summary>
        /// A Boomer's death blast (§6.5): radius-1 cardinal cross, same block-result table as
        /// BombSystem.Blast EXCEPT Bomb cells are left completely untouched (not armed, not
        /// destroyed) — arming them would let a Boomer chain detonate real bombs, which is a second,
        /// uncapped chain reaction this cap was never designed to bound. No avatar-hit check at all:
        /// unlike a bomb blast, a Boomer's explosion never harms the avatar (design rule — it's a
        /// bonus for the player, not a hazard), so there is simply nothing here that could fire one.
        /// </summary>
        /// <returns>How many blocks the blast removed outright (Steel softening does not count).</returns>
        private int DetonateBoomer(GridPos center, int parentBonus, int chainDepth)
        {
            GridPos[] neighbors =
            {
                center.Above, center.Below, center.Offset(-1, 0), center.Offset(1, 0)
            };

            int destroyed = 0;

            foreach (GridPos target in neighbors)
            {
                if (!_grid.InBounds(target))
                    continue;

                CellType hit = _grid.Get(target);
                CellType result = BoomerBlastResult(hit);
                if (result != hit)
                {
                    _grid.Set(target, result);

                    // Steel → Hard is softening, not destruction (same rule as BombSystem's count).
                    if (result == CellType.Empty)
                        destroyed++;

                    if (hit == CellType.AirCapsule)
                        AirCapsuleLiberated?.Invoke(target);
                    if (hit == CellType.Diamond)
                        DiamondLiberated?.Invoke(target);
                }

                EnemyEntity? victim = GetEnemyAt(target);
                if (victim.HasValue)
                    KillOne(victim.Value, KillMethod.Bomb, parentBonus, chainDepth + 1);
            }

            return destroyed;
        }

        /// <summary>Same table as BombSystem.BlastResult, minus Bomb (deliberately left as Bomb — §6.5).</summary>
        private static CellType BoomerBlastResult(CellType type)
        {
            switch (type)
            {
                case CellType.ColorA:
                case CellType.ColorB:
                case CellType.ColorC:
                case CellType.Hard:
                case CellType.HardCracked:
                case CellType.AirCapsule:
                case CellType.Diamond:
                    return CellType.Empty;
                case CellType.Steel:
                    return CellType.Hard; // softened — still needs a drill or a real bomb to remove
                default:
                    return type; // Empty stays Empty; Bomb stays Bomb (never armed by a Boomer blast)
            }
        }

        /// <summary>
        /// One 1-cell step for an active Crawler. Blocked ahead (wall or solid block) → flip
        /// direction and try once more; still blocked (pinned between two walls) → stay put.
        /// The state's Direction is mutated in place so the next call continues from the new
        /// heading — this is the ONLY place Direction changes.
        /// </summary>
        private EnemyEntity StepCrawler(EnemyEntity enemy, CrawlerState state)
        {
            int row = enemy.Position.Y;
            int nextCol = enemy.Position.X + state.Direction;

            if (IsBlocked(nextCol, row))
            {
                state.Direction = -state.Direction;
                nextCol = enemy.Position.X + state.Direction;

                if (IsBlocked(nextCol, row))
                    return enemy; // pinned between two obstacles — no legal step this interval
            }

            return enemy.WithPosition(new GridPos(nextCol, row));
        }

        private bool IsBlocked(int col, int row) =>
            col < 0 || col >= _grid.Width || _grid.IsSolid(new GridPos(col, row));

        /// <summary>Mutable walking state for one active Crawler — direction + move-timer accumulator.</summary>
        private sealed class CrawlerState
        {
            public int   Direction = 1; // +1 = right (the §6.5 default start heading), -1 = left
            public float Timer;
        }
    }
}
