---
paths:
  - "**/Core/**"
---

## 4. Systems — UNCHANGED by the v3 redesign

These systems survived the v3 redesign with no code changes needed. A couple have since taken
**post-v3 tweaks** unrelated to v3 (called out inline, e.g. AvatarModel's coyote time below) —
"unchanged" means "not touched by the v3 mechanical redesign", not "frozen forever".

### AvatarModel
Player grid position, drill in 4 directions, step-up, gravity.
| API | Behavior |
|---|---|
| `Drill(direction)` | Drills adjacent cell; fires `Drilled(GridPos, CellType)`. Locked while falling. |
| `Move(direction)` | Step left/right (with step-up over single blocks). Locked mid-fall EXCEPT during the coyote window (see below). |
| `Tick(dt)` | Avatar gravity (fall if void below); accumulates the fall clock for coyote |
| `Position` | Current GridPos |
| `CoyoteTime` / `InCoyoteWindow` | 🆕 grace window (default 0.12 s) at the start of a fall |
| `FreefallCells` / `FreefallCell` event | 🆕R7.3 void cells traversed in the current fall (`Action<int>`, running count); 0 on landing/`Teleport`; the drop into a cell you just drilled DOWN never counts |

> **🆕 Coyote time (added post-v3).** `TryMove` used to hard-lock all horizontal control while
> `IsFalling` ("falling is a commitment"). That made it impossible to step back onto a ledge you'd
> just walked off, which read as an unfair death (design rule 4). Now a sideways step **into an empty
> cell** is allowed for the first `CoyoteTime` seconds of a fall (`InCoyoteWindow`), letting you catch
> an adjacent ledge — but **long falls stay committed** (no mid-air steering), and **step-up and
> drilling never work mid-air**, even during the window. The fall clock (`_fallElapsed`) is NOT reset
> by a sideways coyote step, so the grace can't be extended by moving; it recharges only on landing.
> Set `CoyoteTime = 0` to restore the old fully-committed fall. Pinned by the five `Move_*Coyote*` /
> `Drill_StaysLocked_DuringCoyote` tests.

### ChunkSystem
Union-find — same-color adjacent cells form rigid chunks.
| API | Behavior |
|---|---|
| `Rebuild()` | Recalculates all chunks from current grid state |
| `GetChunk(col, row)` | Returns chunk ID for a cell |
| `GetChunkCells(id)` | Returns all cells in a chunk |

### HealthSystem
3 hearts, 1.5 s i-frames after each hit.
| API | Behavior |
|---|---|
| `TryTakeDamage()` | Returns false during i-frames; decrements hearts |
| `Tick(dt)` | I-frame cooldown |
| `HealthDepleted` event | Fires at 0 hearts |
| `HeartsChanged` event | Fires on every heart change |

### ChainTracker
Temporal chain counting — observes gravity settle to detect cascades.
| API | Behavior |
|---|---|
| `CurrentChain` (int) | 1-based chain step; 0 when idle |
| `Tick(dt)` | After collapse + gravity each frame |
| `SettleDelay` | 0.15 s |
| `LinkAdded` / `ChainCompleted` events | Chain progression and close |
| 🔄R7.5b links | **Perfect Clear AND chunk burst** (was PC only). Bombs are never links (they have `chainMult`). Optional ctor `BombSystem` → an armed bomb keeps an open chain open. This is the v3.2 `cascade_mult` (§M3.1). |

### CameraShake
Additive shake on Camera.main. `Shake(amplitude, duration)`. Persists across level loads.

### SfxSynth
Procedural clip generator (Tone, Sweep, Noise, Arpeggio). No audio assets.

### GameInput
Input System adapter — the only script that reads Keyboard/Gamepad. `MoveAxis` (polled) +
`DrillRequested` event. Keyboard **and** Xbox/gamepad, suppressed while an overlay is up. Full
binding table and rationale in **§16**.

### GridPos
Immutable grid coordinate struct.

### CellType
Cell enum (0–9) + helper methods. Map chars: `.` `A` `B` `C` `H` `S` `P` `X` `D`.

> **🆕 Diamond (`D`, CellType.Diamond).** Added in R4. Drillable in 1 tap (like a color
> block). Drilling → Empty + fires `DiamondCollected(GridPos)`. Does NOT fuse with anything.
> Streak-neutral (like AirCapsule — drilling a diamond does not break or build a streak).
> Liberated by bomb blast or chunk burst shockwave (same as AirCapsule: cell → Empty + event
> fires). Cannot be crushed/destroyed by chunk landing (chunk lands ON it, diamond survives
> underneath — the player must drill it). This ensures diamonds are never lost to physics.

> **Note:** `CellType` values and helpers (IsSolid, CanFuse, IsDrillable, DrillResult) are
> unchanged. `AirCapsule` behavior changes only in BombSystem (liberation instead of
> destruction) — the enum itself is the same.

---

## 6. Systems — NEW TO BUILD

### 6.1 ~~StreakTracker~~ → MomentumTracker (Core/)

> **⚠️ v3.2 — REMPLACÉ.** `StreakTracker` (streak par couleur, v3.1) est remplacé par
> `MomentumTracker` (streak temporel, v3.2). Le fichier `StreakTracker.cs` sera supprimé.
>
> **Spec complète:** voir `drill-momentum.md` → §M1 (MomentumTracker) + §M2 (paliers).

Pure C# class. Tracks consecutive drills within a time window (0.8s). **All drill directions
count** — no color matching, no directional filtering.

```
API:
  NotifyDrill(CellType drilled)
    → _timer = MomentumWindow (0.8s), _drillCount++
    → color chain tracking (optional bonus: same color = ×1.5 faster accumulation)
    → if Tier 3 reached: fire PowerDrillActivated, then reset to Tier 1

  Tick(float dt)
    → _timer -= dt; if expired: Reset (fire MomentumLost)

  CurrentTier (int 0-3), DrillCount (int), Multiplier (float: ×1/×2/×4/×6)
  TierChanged event: Action<int, int>    — (oldTier, newTier)
  MomentumLost event: Action             — returned to Tier 0
  PowerDrillActivated event: Action      — Tier 3 trigger
  Reset()
```

**Pourquoi le changement:** le streak par couleur forçait du routing latéral (33% de chance
de continuer en descendant), en antithèse avec la survie (descendre vite pour l'air).
Le momentum temporel aligne scoring et survie : forer vite = survivre = scorer.

**Nouveaux systèmes compagnons** (voir `drill-momentum.md`):
- `GrazeSystem` (§M3.2) — +50 pts pour drill adjacent à un danger
- Freefall tracking dans `AvatarModel` (§M3.3) — +15 pts/cellule vide
- `AirSystem.IsDangerZone` (§M3.4) — air < 15% = ×2 global

### 6.2 DepthTracker (Core/)
Pure C# class. Tracks the deepest row the avatar has reached in the current run.

```
API:
  NotifyPosition(GridPos pos)
    → if pos.Y > _maxDepth: _maxDepth = pos.Y, fire NewDepthReached(pos.Y)
  MaxDepth (int, read-only)
  NewDepthReached event: Action<int>  — the row index
  Reset()                             — zeroes for new run
```

GameBootstrap calls `NotifyPosition()` in `Update()` after `AvatarModel.Tick()`.

### 6.3 EndlessManager (Core/) — 🆕R3  ✅ IMPLEMENTED (R3.2)
Pure C# class. The endless counterpart of `CampaignManager`: no levels, no win — the run ends on
suffocation or heart loss, and the metric is how deep you got (design rule 6, GDD §7).

```
API:
  EndlessManager(int seed, int segmentRows = DefaultSegmentRows /* 60 */)

  Seed / SegmentIndex / SegmentRows / SegmentStartDepth  (read-only)
  Depth (int)        — deepest ABSOLUTE row this run (leaderboard metric)
  DrainRate (float)  — current air drain, derived from Depth

  static DrainRateForDepth(int depth) → 5 + (depth / 20) × 0.5, capped at 10 %/s  (R3.2, GDD §7)
  static SegmentSeed(int runSeed, int segmentIndex)
  DepthForLocalRow(int localRow) → absolute depth of a row on the loaded board
  BuildCurrentSegment(int width = 7) → string[]   (StrateGenerator.EndlessSegment)
  NotifyAvatarPosition(GridPos pos, int boardHeight)  — call in Update() after AvatarModel.Tick()
  AdvanceSegment()   — SegmentIndex++, SegmentStartDepth += SegmentRows
  Reset(int seed)

  DepthChanged     event: Action<int>    — new deepest absolute row
  DrainRateChanged event: Action<float>  — new %/s; GameBootstrap assigns it to AirSystem.DrainRate
  SegmentExhausted event: Action<int>    — avatar reached the bottom; arg = the exhausted index

  const WobbleDuration = 0.6f            — matches campaign level 4+ (endless unlocks after it)
  const ExitDepthFromFloor = CampaignManager.WinDepthFromFloor
```

**Why segments.** A `GridModel` has a fixed height, so "infinite" descent is a *chain of finite
boards*. Three things have to survive the seam, and they are exactly what this class owns:

1. **Depth accumulates across boards.** `DepthTracker` is per-board (it resets in `LoadLevel`), so
   it can never be the run metric. `Depth = SegmentStartDepth + localRow − SpawnRows`.
2. **Difficulty resumes, it does not restart.** `StrateGenerator.EndlessSegment(seed, width, rows,
   startDepth)` (🆕R3) seeds the ramp at `startDepth / EndlessRampRows` instead of 0 — otherwise the
   well would get *easier* the deeper you dig, inverting the whole mode. `EndlessBoard` is now just
   `EndlessSegment(..., startDepth: 0)`, so the existing endless tests still describe segment 0.
3. **Drain ramps with absolute depth**, not with a level number that endless doesn't have.

> **⚠️ The spawn zone maps ABOVE `SegmentStartDepth`, and that clamp direction is load-bearing.**
> `DepthForLocalRow` floors at 0 for the run as a whole, **not** at `SegmentStartDepth`. On segment
> 2+ the avatar respawns at the top of a fresh board; clamping per-segment would credit the seam
> itself as a new deepest row (+1 free depth, and a drain bump, every board swap). Pinned by
> `SpawningIntoANewSegment_DoesNotLoseDepth` + `Depth_IsContinuousAcrossASegmentBoundary` (the last
> content row of segment N and the first of segment N+1 are consecutive depths — no jump, no repeat).

**View wiring: §5.15** (R3.3, delivered). GameBootstrap owns the transition; this class stays pure C#.

### 6.4 DiamondSystem (Core/) — 🆕R4  ✅ IMPLEMENTED
Pure C# class. Tracks diamond collection and provides the campaign win gate.

```
API:
  Init(int totalDiamonds)
    → sets _total; resets _collected to 0
  NotifyCollected(GridPos pos)
    → _collected++; fires DiamondCollected(_collected, _total)
    → if _collected == _total: fires AllDiamondsCollected
  Collected (int, read-only)
  Total (int, read-only)
  IsComplete (bool) → _collected >= _total
  DiamondCollected event: Action<int, int>   — (collected, total)
  AllDiamondsCollected event: Action          — win gate opens
  Reset()
```

**Collection sources:**
- Avatar drills a Diamond cell → `AvatarModel.Drilled` fires with `CellType.Diamond` →
  GameBootstrap calls `DiamondSystem.NotifyCollected(pos)`
- Bomb blast hits a Diamond cell → BombSystem liberates it (same as AirCapsule) →
  `BombSystem.DiamondLiberated(GridPos)` → GameBootstrap calls `NotifyCollected(pos)`
- Chunk Burst shockwave hits a Diamond cell → GravitySystem liberates it →
  `GravitySystem.DiamondLiberated(GridPos)` → GameBootstrap calls `NotifyCollected(pos)`

**Streak interaction:** Diamond is streak-neutral (like AirCapsule). Drilling a diamond
does NOT break or build a color streak. The player is never punished for collecting.

**🔄 v3.1 arcade pivot — diamond gate REMOVED.** Diamonds are now a **pure bonus** in all modes:
- Campaign: each diamond = +150 pts. NOT required to win. Win = depth + score minimum.
- Endless: each diamond = +150 pts. Same as before.
- `CampaignManager` no longer checks `DiamondSystem.IsComplete`. The `diamondsComplete`
  parameter on `NotifyAvatarPosition` is removed.
- `DiamondSystem.IsComplete` and `AllDiamondsCollected` event still exist (for HUD feedback:
  "all diamonds collected!" popup) but they don't gate progression.

> **✅ Implementation notes / deviations (2026-07-28):**
> - **`AwardDiamond()` is wired once, off `DiamondCollected`, not at each of the three collection
>   call sites.** `GameBootstrap.Awake()` does
>   `_diamondSystem.DiamondCollected += (collected, total) => _scoreSystem.AwardDiamond();` — since
>   `NotifyCollected()` always fires that event regardless of how the diamond was freed, the score
>   award lives in exactly one place instead of being duplicated at the drill handler, the burst
>   shockwave handler and the bomb blast handler.
> - **`CampaignManager.NotifyAvatarPosition` grew a third parameter**, `bool diamondsComplete = true`
>   (defaulting to true, not a `DiamondSystem` reference — Core systems don't reference each other,
>   §7). The caller reads `_diamondSystem.IsComplete` and passes it in. The default keeps every
>   pre-R4 caller and test (levels 1-3, anything that predates R4) on depth-only behavior.
> - **`DiamondSystem.Init(totalDiamonds)` is called every `LoadLevel()`, counting `'D'` characters
>   straight off the generated `rows`** rather than calling `StrateGenerator.DiamondCountForLevel()`
>   separately — the gate then always matches what is actually on the board, whatever the source
>   (campaign, endless, debug map, or the tutorial showcase, §5.14).
> - **BoardView rendering was missing entirely until this pass** — `CellType.Diamond` fell into
>   `VisualFor()`'s `default:` case and rendered as an empty cell. Fixed alongside the `DiamondShine`
>   shader (§5.10).

### 6.5 EnemySystem (Core/) — 🔄R5 (v3.1 arcade pivot)
Pure C# class. Manages enemy entities as grid-based actors (NOT CellTypes — enemies move,
cells don't). **v3.1 reduced to 2 types:** Crawler (mobile bonus target) + Boomer (stationary
chain amplifier). Digger and Tank were removed — they interrupted descent and added puzzle
elements that conflicted with the arcade identity.

```
Data:
  EnemyEntity struct:
    Id (int)
    Type (EnemyType)        — Crawler, Boomer
    Position (GridPos)
    IsActive (bool)         — false = buried/dormant, true = active
    IsAlive (bool)
    Health (int)            — always 1 (both types die in one hit)

API:
  EnemySystem(GridModel grid)                                  — 🔄 R5.8: takes the grid via constructor, see deviation note
  SpawnEnemy(EnemyType type, GridPos pos) → int id
    → creates a dormant enemy at pos; fires EnemySpawned(id, type, pos); returns the new id
  ActivateEnemy(int id)
    → IsActive = true; fires EnemyActivated(id)
  Tick(float dt, GridPos avatarPos)                             — 🔄 R5.8: grid dropped from the signature, reads the stored grid
    → moves active Crawlers per their pattern                                — ✅ R5.7
    → checks avatar collision → fires AvatarHitByEnemy(id) if an ACTIVE Crawler occupies avatarPos — ✅ R5.10.
      Runs every Tick regardless of whether the Crawler stepped this frame (avatar walking into a
      stationary Crawler counts too). Fires every overlapping frame — no de-dup here, HealthSystem's
      i-frames (caller-wired) are what make repeated hits harmless. Boomers never check this (they
      don't move and don't deal contact damage — only their blast does anything, and it's a bonus).
      Dormant enemies are filtered out before this check runs at all.
  NotifyChunkLanded(List<GridPos> landingCells)
    → any enemy under the chunk: killed (dormant or active — see the note below)
  NotifyBurst(List<GridPos> burstCells, List<GridPos> shockwaveCells, int fallDist)
    → enemies in burst zone or shockwave: killed
    → Boomer killed by burst → fires BoomerDetonated(GridPos, int fallBonus), fallBonus = floor(fallDist / ScoreSystem.BurstFallDivisor)
  NotifyBombBlast(List<GridPos> blastCells, int chainMult)     — 🔄 chainMult is an explicit param, see R5.6 deviation note
    → enemies in blast zone: killed
    → Boomer killed by bomb → fires BoomerDetonated(GridPos, chainMult) — the SAME value passed in
  GetEnemyAt(GridPos pos) → EnemyEntity? (for collision checks)
  GetAllAlive() → List<EnemyEntity>
  Reset() — clears all enemies

  ── Activation triggers (✅ R5.9) ──
  NotifyAdjacentDrill(GridPos drilledPos)
    → wakes a dormant enemy in any of the drilled cell's 4 cardinal neighbors (same shape as
      BombSystem.NotifyDrilled/ArmAdjacent). Wire to the same Drilled handler that calls
      _bombSystem.NotifyDrilled(pos).
  NotifyBurst / NotifyBombBlast — BOTH also wake dormant enemies bordering the effect zone (every
      cardinal neighbor of every zone cell), independent of and before the kill pass, so an enemy
      just outside a blast wakes up without necessarily being the one that dies.
  ActivateInViewport(int minRow, int maxRow)
    → wakes every dormant enemy with minRow <= Position.Y <= maxRow. No per-board event covers
      Endless — GameBootstrap calls this from the camera's current view bounds each frame/scroll.

  ── Boomer death blast (✅ R5.8) — fires automatically from inside KillOne, not a separate call ──
  Radius-1 cardinal cross (BoomerBlastRadius = 1), same block-result table as BombSystem.Blast
  EXCEPT Bomb is left completely untouched (never armed, never destroyed — avoids a second,
  uncapped chain reaction through real bombs). Any other enemy caught in the radius dies too
  (KillMethod.Bomb), and if IT is a Boomer within BoomerChainCap (= 3) depth, it detonates in turn.
  No avatar-hit check exists anywhere in this path — a Boomer's blast can never harm the avatar.

Events:
  EnemySpawned:         Action<int, EnemyType, GridPos>
  EnemyActivated:       Action<int>
  EnemyKilled:          Action<int, EnemyType, KillMethod, int>   — 🔄 R5.12: the 4th arg is the parent
                        bonus (fall_bonus / chain_mult / 1). Carried on the event so the consumer
                        never has to look it back up — see the R5.12 deviation note.
  AvatarHitByEnemy:     Action<int>                               — ✅ R5.10, fires from Tick() (see above)
  BoomerDetonated:      Action<GridPos, int, int>  — 🔄 R5.12: pos + parent bonus + blocks destroyed.
                        Fires AFTER the blast resolves (like BombSystem.BombScored), because the
                        count isn't known until the cells are cleared. Steel softening isn't counted.
  AirCapsuleLiberated:  Action<GridPos>          — 🆕 R5.8, a Boomer blast freeing a capsule
  DiamondLiberated:     Action<GridPos>          — 🆕 R5.8, a Boomer blast freeing a diamond

Dormant vs active is a CONTACT distinction only: a dormant enemy is buried but physically present,
so a chunk landing / burst / bomb blast kills it exactly like an active one. Only the avatar-touch
check (✅ R5.10, Tick()) cares whether IsActive is true — and even then only for a Crawler; a
Boomer's IsActive state affects nothing about avatar contact (it never has any).
```

**Activation rules:** enemies start buried/dormant. They activate when:
- The avatar drills a cell adjacent to the enemy (cardinal, same as bomb arming).
- A chunk burst shockwave reaches an adjacent cell.
- The camera scrolls them into view (for Endless mode — they activate on-screen).

Dormant enemies are **visible on the grid** (the player can see them and plan) but don't
move or deal damage until activated. They are NOT drillable (like Steel). They occupy a
grid cell that blocks movement.

**Crawler** — moves laterally in its row. Bounces off walls and solid blocks.
- Move interval: 0.8 s
- Cannot leave its row. Cannot climb. Cannot drill.
- Activation: adjacent drill or burst.
- Kill: any chunk landing, any burst, any bomb blast. 1 HP.
- Avatar collision: active Crawler on avatar cell = −1 heart (i-frames apply). Dormant = safe.
- Score: 100 pts (× fall_bonus if killed by burst, × chain_mult if killed by bomb).
- Visual: small insect-like sprite, shuffles left-right.

**Boomer** — stationary. Does NOT move, does NOT deal contact damage.
- Activation: adjacent drill or burst (same as Crawler).
- Kill: any chunk landing, burst, or bomb blast. 1 HP.
- **On death:** explodes like a bomb — blast radius 1 (cardinal), destroys adjacent blocks.
  Fires `BoomerDetonated(pos, parentBonus)`. GameBootstrap wires this to the same blast
  logic as BombSystem: destroyed blocks score as bomb points, the parent bonus (fall_bonus
  from burst, or chain_mult from bomb) carries through.
- The Boomer **amplifies** the action the player is already doing. A burst that kills a Boomer
  triggers a secondary explosion = more destruction = more points = more chaos.
- Boomer blast does NOT arm other bombs (to prevent infinite loops). It CAN kill other enemies
  (including other Boomers — chain reaction capped at 3 deep to prevent runaway).
- Score: 150 pts for the kill itself (× parent bonus).
- Visual: glowing orb, pulses when active, explodes with a distinct color.

### 6.6 DailyDig (Core/) — 🆕R3  ✅ IMPLEMENTED (R3.4)
A **date → seed** function, and deliberately nothing more.

```
API (static, pure):
  SeedFor(DateTime date)  → int   — date-only; same calendar day = same well, worldwide
  LabelFor(DateTime date) → "2026-07-26"  — invariant key for the save + the future leaderboard
```

The Daily Dig **is an ordinary endless run** started on that seed — no separate mode, no separate
generator, no separate scoring. That is what makes it nearly free, and it means every future
generator or balance change lands in the daily automatically.

- **UTC, always.** `GameBootstrap` passes `DateTime.UtcNow`: the day has to roll over at the same
  instant everywhere, or two players "on the same day" would compare runs on different terrain.
  (Expect the label to be tomorrow's date late in the evening in Québec — that is correct, not a bug.)
- **SplitMix64 finalizer, not the raw day number.** Consecutive integers fed to `System.Random`
  produce visibly related boards, which would make today's dig feel like a re-skin of yesterday's.
  Pinned by `ConsecutiveDays_ProduceUnrelatedBoards`, which compares the *generated rows*, not the seeds.
- **Never returns `int.MinValue`** (no positive counterpart; some `System.Random` implementations
  throw). `StrateGenerator.EndlessSegment` guards the same value defensively, because
  `EndlessManager.SegmentSeed` wraps `unchecked` and can reach it.

**View side — `DailyDigStore` (PlayerPrefs).** Keeps *best depth, best score and run count* per day
label. Depth and score keep separate maxima: depth is the leaderboard metric (design rule 6), but the
deepest run is not automatically the highest-scoring one.

> **Deviation from the GDD: no "one attempt per day" lock.** The GDD floats it, but it only means
> something once scores are posted somewhere (R3.5); enforced client-side today it would punish
> honest players while a PlayerPrefs edit walks straight around it. So the day keeps your **best**
> and counts your attempts — the run count is what a future submission would flag "first attempt"
> with. The Wordle-style share card is also deferred to R3.5, where there is something to share.

### 6.7 DeathTracker (Core/) — 🆕R6  🟡 PLANNED (R6.1)
Tracks the cause of the player's death for the death recap screen.

```
API:
  RecordDeath(DeathCause cause, GridPos? pos)
  LastDeath → DeathRecord?   — cause + position + timestamp
  Reset()
```

`DeathCause` enum: `AirDepleted`, `Crushed`, `BombBlast`, `EnemyContact`.
Pure Core — no Unity dependency. GameBootstrap wires each death path to `RecordDeath`.

### 6.8 PuzzleBoard (Core/) — 🆕R6  🟡 PLANNED (R6.10)
Pre-authored micro-puzzles for the tutorial system. Each puzzle isolates one mechanic
(streak, burst, bomb chain, air capsule, diamond, enemy) with a win condition.

### 6.9 ProgressionSystem (Core/) — 🆕R6  🟡 PLANNED (R6.11)
XP-based unlock of game mechanics. Tracks player XP across sessions (PlayerPrefs-backed).
Determines which mechanics are available at the player's current level.
