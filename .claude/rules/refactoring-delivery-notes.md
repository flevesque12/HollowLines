---
paths:
  - "**/Core/**"
  - "**/View/**"
---

## Delivery notes — R2.8d, R3, R4, R5

> Verbose implementation notes for completed refactoring steps.
> For the compact step tables, see `refactoring-plan.md`.
> For R6 detailed prompts, see `PROMPTS-CLAUDE-CODE-R6.md` in project root.

---

### R2.8d — decided (2026-07-21): silent settle at board load

`GravitySystem.Settle()` resolves the freshly generated board to a stable rest state in
`LoadLevel()` **before** play — no telegraph, no burst, no shockwave, no crush. This kills the
reported symptom: the board no longer demolishes itself before the player moves. Pinned by
`Settle_DropsUnsupportedChunk_WithoutBursting`,
`Settle_LeavesBoardAtRest_NoWobbleOrBurstOnNextTick`,
`Settle_StacksFallingChunks_WithoutOverlap`.

**Deliberately left as-is:** the *in-play* shockwave cascade (a player-caused burst still erases
neighbor blocks and can chain — §15.1's 78-bursts-per-run mechanism). The designer chose the
settle-only route; the shockwave keeps its block-erasing bite during play. If late-game runs still
over-supply air once R2.8c pacing is finished, the next lever to consider is the shockwave scope or
`BurstFallThreshold` — but re-measure with the playtest harness first, since the board no longer
self-destructs at load and the old §15.1 numbers were taken on one that did.

**Separately, §5.2 dev 5 is resolved:** the shockwave ring no longer damages the avatar (only the
chunk footprint crushes), so a correct dodge is never punished.

---

### R3.1-R3.4 delivered 2026-07-26

`EndlessManager` (§6.3) owns absolute depth, the drain ramp and segment chaining; the View flow
is wired and play-mode verified (§5.15); the steady-state terrain was measured against the campaign
and retuned (§15.7); the Daily Dig ships as a date→seed function (§6.6). **Endless and the Daily
Dig are playable** from the main menu ("Sans fin" / "Défi du jour", with "Menu principal" to
switch back). Only **R3.5 (leaderboard)** is left, and it is a separate ASP.NET Core project rather
than Unity work.

---

### R4 delivered 2026-07-28, all 10 steps in one session

`DiamondSystem` (§6.4) owns collection tracking and the campaign gate; placement is
deterministic-exact in campaign and probabilistic in endless (§5.8); the View layer (HUD counter,
VFX sparkle, audio chime, and a shader-driven tile shine that wasn't in the original plan) is
fully wired and confirmed live in the Unity Editor via UnityMCP — not just compiled. The
hand-authored tutorial showcase (§5.14) also got a Diamonds chamber demonstrating the mechanic
before campaign level 1. Test suite: 232 → **260**.

**One real bug found along the way:** `BoardView.VisualFor()` had no case for `CellType.Diamond`
and silently rendered it as an empty cell — nobody had touched board rendering until R4.10 needed
the shine material, so it went unnoticed through R4.1-R4.9. Fixed alongside the shader (§5.10).

#### R4 step details

| Step | Notes |
|---|---|
| R4.1 | IsDrillable true, CanFuse false, DrillResult Empty (automatic). StreakTracker neutral (automatic via CanFuse). 6 new tests (BlockTypesTests + StreakTrackerTests) |
| R4.2 | Init, NotifyCollected, IsComplete, events. 7 new (`DiamondSystemTests`) |
| R4.3 | Same pattern as AirCapsuleLiberated. 1 new (`Shockwave_LiberatesDiamond`) |
| R4.4 | Same pattern as AirCapsuleLiberated. 2 new (liberated + no-diamond-no-fire) |
| R4.5 | `ScoreSource.Diamond` added. 3 new + `Score_AccumulatesAcrossEverySource` updated |
| R4.6 | `NotifyAvatarPosition` gained `bool diamondsComplete = true`. Levels with 0 diamonds: default keeps the gate open. 3 new |
| R4.7 | Exact per-level count in campaign (`PlaceDiamonds`), flat 2% per-cell roll in endless (`DiamondRate`). 9 new |
| R4.8 | Integration — 258/258 PlayMode tests + live play-mode verification (score +150, HUD updated) |
| R4.9 | Diamond counter "💎 2/5" (campaign) / "💎 7" (Endless, per-segment — §5.9 deviation). Visual check |
| R4.10 | Diamond collect sparkle + chime SFX; **BoardView + new `DiamondShine` shader** for persistent shimmer on undrilled diamonds (added on request, not originally scoped). Visual/audio check |

---

### R5 — Enemies + Arcade polish — delivered 2026-09-01

> **v3.1 arcade pivot** simplifies R5: 2 enemy types (Crawler + Boomer) instead of 3, no diamond
> gate rework needed (diamonds are already optional in scoring — just remove the CampaignManager
> gate check), plus three arcade-feel changes (streak vertical-only, drill air restore, bomb
> fuse 1.5s, score gate).

#### R5.1 — StreakTracker vertical-only

No direction enum existed anywhere in Core — `AvatarModel.TryDrill` only ever took raw `(dx, dy)`.
Added `DrillDirection { Up, Down, Left, Right }` (`Core/DrillDirection.cs`) and had `TryDrill`
derive it from `(dx, dy)` (`dy>0` → Down, matching `GridPos.Below`'s `Y+1`).
`AvatarModel.Drilled` grew a third parameter (`Action<GridPos, CellType, DrillDirection>`), which
rippled to every subscriber: `GameBootstrap`, `AudioManager.OnDrilled`, `VfxManager.OnDrilled`, and
the `tools/playtest` harness (`Avatar.Drilled` lambda). `StreakTracker.NotifyDrill` now early-returns
on any non-`Down` direction before the existing streak-neutral (`CanFuse()`) check — a lateral or
upward drill never reaches the color comparison at all. All pre-existing `NotifyDrill` call sites
(11 `StreakTrackerTests`, the tutorial-showcase streak test, `GameBootstrap`, the playtest harness)
now pass `DrillDirection.Down` to keep their prior behavior. 263/263 PlayMode tests pass.

#### R5.2 — AirSystem drill restore

`AirSystem.DrillRestoreAmount = 0.5f`, matching the unit convention every other restore constant
already uses (`BurstRestoreAmount = 0.5f`, `CapsuleRestoreAmount = 6f`, etc. are percentage-POINTS
added directly by `Restore(amount)`, not fractions of `MaxAir`). Wired into `GameBootstrap`'s
`Drilled` handler right after `NotifyDrill` and before `AwardDrill` — matching the §7 pseudocode
order exactly. `RestoreEconomy_IsSmallRelativeToDrain` gained a third assertion
(`DrillRestoreAmount <= BurstRestoreAmount`) since drill restore ties Burst as the smallest source —
the survival floor, not a shortcut. 266/266 PlayMode tests pass.

#### R5.3 — BombSystem fuse 1.5s

`BombSystem.FuseDuration` 2.5f → 1.5f. No test edits needed: every fuse-timing test already ticks
`BombSystem.FuseDuration + 0.01f` rather than a hardcoded literal (2.5 was never inlined), so all 16
detonation tests plus the tutorial-board bomb-chain test adapted automatically. Verified the fuse
telegraph (§5.10 `VfxManager`, §5.11 `AudioManager`) stays correct at the new tempo: both
`OnFuseProgress` handlers key off the normalized `frac` (0→1) that `BombSystem` already computes as
`1f - (remaining / FuseDuration)` — the beep-index quadratic (`floor(frac² × fuseBeepSteps)`) and
the glow halo never reference the duration directly, so they compress into the shorter fuse rather
than breaking. Fixed one stale doc reference (`hollow-lines-gdd-v3.md` §11 pseudocode still said
"2.5 s fuse"). 266/266 PlayMode tests pass (no new tests — a constant-only change).

#### R5.4 — CampaignManager score gate

`NotifyAvatarPosition`'s third parameter changed from `bool diamondsComplete = true` to
`int currentScore = int.MaxValue` — the `int.MaxValue` default always clears `ScoreMinimumForLevel`,
preserving the exact backward-compat behavior the old `true` default gave (tutorial showcase and any
caller that doesn't care about the gate keeps depth as the only requirement). `ScoreMinimumForLevel`
is a new static method (no state, easy to unit test directly). `GameBootstrap` now passes
`_scoreSystem.Score`; `_diamondSystem` is untouched otherwise — diamonds are still placed and
collected (§6.4), they just feed the score gate as +150 pts each instead of gating directly. The 3
old diamond-gate tests were replaced with 7 score-gate tests (block/pass/catch-up/tutorial-no-gate/
depth-still-required/default-bypass/table-values) — net +4. Also fixed a stale doc comment in
`StrateGenerator.DiamondCountForLevel` that still described diamonds as gating. 270/270 PlayMode
tests pass.

#### R5.5 — EnemyType + EnemyEntity

Three new files, no functional tests (pure data structures, per dev instruction) — compile-checked
and the full 270/270 suite re-run to confirm no regression. `EnemyType.cs` (Crawler, Boomer) and
`KillMethod.cs` (Crush, Burst, Bomb — didn't exist yet) are plain enums. `EnemyEntity.cs` is a
separate file, not folded into `EnemyType.cs` — it's a distinct data structure, matching the
one-type-per-file convention `GridPos.cs`/`DrillDirection.cs` already use. Implemented as an
immutable `readonly struct` with `With*`/`Activated()`/`Killed()` methods that return a new value,
the same pattern `GridPos.Offset()` uses (no `with`-expression: plain structs don't support that
syntax, only C# 9+ records/record structs do, and this project's other value types are plain
structs) — `EnemySystem` (R5.6+) will own the authoritative copy and replace it wholesale on each
state change. `Health` is always 1 for both current types (both die in one hit) but is carried on
the struct so a future tougher enemy doesn't need a shape change. Drive-by fix: `CellType.cs`'s doc
comment for `Bomb` still said "2.5 s fuse" (stale since R5.3).

#### R5.6 — EnemySystem core lifecycle

`EnemySystem.cs` — spawn, activate, query, kill-by-crush/burst/bomb, `Reset()`. Two deliberate
deviations from the original §6.5 API sketch:

- **`NotifyBombBlast` takes `chainMult` as an explicit parameter** (`NotifyBombBlast(List<GridPos>
  blastCells, int chainMult)`), not the bare `NotifyBombBlast(List<GridPos>)` the doc drafted. The
  §7 pseudocode's alternative — `EnemyKilled` fires, then GameBootstrap looks up
  `BombSystem.LastChainMult` to compute the Boomer's parent bonus — would need a "last chain mult"
  field that doesn't exist on `BombSystem` today and adds a second read after the event instead of
  carrying the value on the call that already has it. Since the caller (whoever resolves the blast)
  already knows the chain multiplier at the point it calls in, passing it straight through is
  simpler and matches how `NotifyBurst` already receives `fallDist` directly.
- **`SpawnEnemy` returns the new `int` id** in addition to firing `EnemySpawned` with it — the event
  alone forces every caller (including every test) to capture it via a closure just to reference the
  enemy again. Non-breaking, same id either way.

`KillMethod.Crush` never fires `BoomerDetonated` — only Burst and Bomb kills amplify (§6.5); a
Boomer crushed by a landing chunk just dies like a Crawler would. Dormant enemies die exactly like
active ones to crush/burst/bomb (only avatar CONTACT, R5.10, will check `IsActive`) — the dev's
initial test list had this backwards and self-corrected mid-prompt; implemented per the correction.
19 new tests, 289/289 PlayMode tests pass.

#### R5.7 — Crawler movement

`EnemySystem.Tick(dt, grid, avatarPos)` — only active Crawlers move; dormant ones and every Boomer
are untouched. Movement state (Direction, Timer) lives in a private `CrawlerState` class keyed by
id, NOT on `EnemyEntity` — the public struct has no notion of "which way it's walking" (§6.5 didn't
ask for one), so adding it there would leak Crawler-only concerns onto every enemy type. Default
heading is right (`CrawlerMoveInterval = 0.8f`, a public const so tests and the future GameBootstrap
wiring share one source of truth). Bounce logic: blocked ahead (`col < 0`, `col >= grid.Width`, or
`grid.IsSolid`) flips `Direction` and retries once in the same interval — a Crawler pinned between
two walls just stays put rather than oscillating in place every tick. `state.Timer` is a `while`
loop (not `if`), so a dt larger than 0.8s steps multiple cells in one `Tick` call, matching how
`AvatarModel.Tick`'s fall clock handles a big frame. Dead Crawlers' `CrawlerState` entries are
removed in `KillInZone` (mirrors `AudioManager`'s fuse-index cleanup on detonation) so the
dictionary doesn't accumulate stale entries over a long run. 5 new tests, 294/294 PlayMode tests
pass.

#### R5.8 — Boomer death explosion (biggest deviation of R5)

**`EnemySystem` now takes `GridModel` via its constructor.** The dev's spec describes the Boomer
blast as EnemySystem's OWN behavior ("quand un Boomer est tué, il explose" — destroys blocks,
liberates capsules/diamonds, chain-kills other enemies), not something GameBootstrap assembles from a
bare `BoomerDetonated` event. That requires read/write grid access at kill-time, and the established
pattern for a Core system that needs one (`GravitySystem`, `BombSystem`) is constructor injection,
not a per-call parameter — so `EnemySystem(GridModel grid)` replaces the parameterless constructor,
and `Tick` dropped its now-redundant `GridModel grid` parameter (uses the stored field instead;
every R5.6/R5.7 test updated — 24 call sites, `new GridModel(10, 10)` for the ones that don't
otherwise need a real board). §6.5 and §7 above are resynced to the new constructor/Tick signatures.

Every kill (zone-based or chain-cascaded) now funnels through one `KillOne(enemy, method,
boomerBonus, chainDepth)` — the single choke point that decides whether a Boomer detonates
(`boomerBonus.HasValue && chainDepth <= BoomerChainCap`). `DetonateBoomer` applies a radius-1
cardinal cross using the SAME block-result table as `BombSystem.Blast` (`Core/BombSystem.cs`)
with ONE deliberate difference: **Bomb cells are left completely untouched** — not armed, not
destroyed — because arming them would open a second, uncapped chain reaction through real bombs
that `BoomerChainCap` was never designed to bound (the dev's own stated reason: "pour éviter les
boucles infinies"). Two new events, `AirCapsuleLiberated`/`DiamondLiberated`, mirror
`BombSystem`'s naming exactly so the future GameBootstrap wiring (§7) is a straight copy-paste of
the bomb-liberation pattern.

**No avatar-hit code exists anywhere in the Boomer blast path** — that's the actual mechanism
behind "the explosion never hurts the avatar": there's nothing to wire wrong, because nothing
checks avatar position at all. `AvatarHitByEnemy` stays declared-but-unfired (R5.10 still owns it).

Chain depth: the Boomer directly killed by a burst/bomb is depth 1 and always detonates if it has
a bonus; each Boomer its blast kills is one depth deeper. Verified end-to-end with a 4-Boomer line
(A→B→C→D): killing A cascades through B (depth 2) and C (depth 3, at the cap) but D (depth 4)
dies without detonating — exactly 3 `BoomerDetonated` events, 4 `EnemyKilled` events.

9 new tests (the 10th requested case — "Boomer killed → BoomerDetonated fires" — was already
pinned by two R5.6 tests, not duplicated). 303/303 PlayMode tests pass.

#### R5.9 — Activation rules

Three activation entry points, all funneling through one private `ActivateAdjacent`/
`ActivateDormantAt` pair (mirrors `BombSystem.ArmAdjacent`/`TryArm` exactly — same
four-cardinal-neighbor shape, same "no-op if nothing dormant is there" contract):

- `NotifyAdjacentDrill(GridPos)` — wakes a dormant enemy adjacent to a drilled cell.
- `NotifyBurst`/`NotifyBombBlast` — now ALSO call `ActivateAdjacentToZone` on the full effect zone
  (burst footprint ∪ shockwave ring, or the blast cells) before resolving kills. An enemy just
  outside the zone wakes up without necessarily being the one that dies — verified explicitly:
  `NotifyBurst_ShockwaveAdjacentToDormantEnemy_ActivatesIt` asserts the Crawler is BOTH active AND
  still alive, since it's adjacent to a shockwave cell, not inside it.
- `ActivateInViewport(int minRow, int maxRow)` — Endless has no per-board activation event (there's
  no "drilled" or "burst" to hook), so this is a direct row-range sweep GameBootstrap calls from
  the camera's current view bounds. Inclusive both ends.

Reused `ActivateEnemy(id)` itself (not a duplicate mutation) for every trigger, so the dormant/
already-active/dead guards and the `EnemyActivated` event only exist in one place. 6 new tests,
309/309 PlayMode tests pass.

#### R5.10 — Avatar collision

One check appended to the existing Crawler loop in `Tick` — no new loop, no new per-frame
grid/enemy scan. The loop already filters to `IsAlive && IsActive && Type == Crawler` before doing
anything (movement included), so "only active Crawlers deal contact damage" and "dormant enemies
never deal contact damage" both fall out of that SAME filter for free — a Boomer or a dormant enemy
never reaches the new `if (enemy.Position == avatarPos)` line at all, there's no separate branch to
get wrong. The check runs after the movement `while` loop and reads the (possibly just-updated)
position, so it catches both directions of contact: a Crawler stepping onto the avatar's cell
mid-`Tick`, and the avatar having walked onto a Crawler that didn't move at all that frame.

**`AvatarHitByEnemy` fires on every overlapping frame, not just the first** — by design, per the
dev's note that HealthSystem's i-frames are what make repeated contact harmless. De-duplicating
here would be redundant with that cooldown and would need EnemySystem to track "have I already hit
the avatar this overlap", state it has no other reason to keep. 5 new tests, 314/314 PlayMode
tests pass.

#### R5.11 — ScoreSystem enemy scoring

`ScoreSystem.AwardEnemyKill(EnemyType, int bonus = 1)` and `AwardBoomerBlast(int blocksDestroyed,
int parentBonus)`, plus `ScoreSource.EnemyKill` / `.BoomerBlast`. `AwardBoomerBlast` deliberately
reuses `BombPointsPerBlock` rather than a new constant — the dev's own framing ("scores like a
bomb") and the §8 table's formula both point at the SAME 25-per-block rate, not a parallel one that
could drift out of sync at the next balance pass. Both bonus parameters clamp to a minimum of 1
(matching `AwardBomb`'s `chainMult` clamp) — a plain crush kill or an unscaled blast still pays out,
it just doesn't multiply. `Score_AccumulatesAcrossEverySource` grew the two new calls, 1030 → 1430.
10 new tests, 324/324 PlayMode tests pass.

#### R5.12 — GameBootstrap wiring

Items 1 and 2 of the request were already done (R5.1/R5.2 shipped the drill direction +
`RestoreDrill`, R5.4 shipped the score gate) — only `_enemySystem.NotifyAdjacentDrill(pos)` was
missing from the drill handler. The rest needed **four Core additions, because the §7 draft
referenced three members that never existed**:

- **`GravitySystem.LastShockwaveCells`** (new). The draft read this inside the `ChunkBurst`
  handler, but the ring was computed AFTER `ChunkBurst` fired — a naive property would have handed
  every burst the PREVIOUS burst's ring. `ResolveBurst` now computes the ring *before* firing the
  event (its effects still apply after, so the documented crush → land → burst → shockwave order is
  unchanged). Chosen over widening the `ChunkBurst` event because that event has five subscribers
  and only one wants the ring. It is only valid during the event — the XML doc says "never poll it".
- **`BombSystem.BlastResolved(List<GridPos> cells, int chainMult)`** (new). Nothing exposed which
  cells a blast touched. `Blast()` now appends every in-bounds cell it reaches to a list (plus the
  bomb's own centre), fired next to `BombScored`.
- **`EnemySystem.EnemyKilled` gained the bonus** (`id, type, method, bonus`). The draft had
  GameBootstrap `switch` on `KillMethod` and read `_gravity.LastFallBonus` /
  `_bombSystem.LastChainMult` — two more fields that don't exist, and an out-of-band read that only
  works while the handler runs inside the originating resolution. EnemySystem already holds the exact
  value at kill time, so it just passes it along.
- **`EnemySystem.BoomerDetonated` gained the block count** (`pos, parentBonus, blocksDestroyed`)
  and now fires AFTER the blast resolves, mirroring `BombSystem`'s `BombScored`. **The request
  asked GameBootstrap to apply the Boomer blast to the grid; it must not** — R5.8 already does that
  inside EnemySystem, so re-applying would double-destroy. GameBootstrap only scores it.

Also wired `ActivateEnemiesInView()` (Endless only) to complete R5.9's contract — it derives the
visible row band from the camera's un-shaken centre ± orthographic size, rounded outward.

**⚠️ No enemy actually spawns yet.** `SpawnEnemiesForBoard()` is deliberately an empty, documented
hook: enemies are ACTORS, not CellTypes (§6.5), so they have no map character and *cannot* appear
in the generated `rows` — `GridModel.FromStringMap` throws on unknown characters. R5.13 therefore
has to hand placements over out-of-band.

#### R5.13 — StrateGenerator enemy placement

`StrateGenerator.PlaceEnemies(board, level, rng)` + `CrawlerCountForLevel` / `BoomerCountForLevel`
(§9 table), and R5.12's empty `SpawnEnemiesForBoard()` hook is now filled — **enemies actually
spawn in campaign play from level 6.**

**The board is NOT mutated** — enemies are ACTORS with no map character, and writing one into `rows`
would make `GridModel.FromStringMap` throw. So `PlaceEnemies` is pure — it reads the board and
returns coordinates, pinned by `PlaceEnemies_DoesNotMutateTheBoard`.

**Spacing is enforced by construction, not by rejection sampling**: each enemy claims a whole row,
and rows within `MinEnemyRowSpacing` (3) of a used one are skipped. That makes the hard constraint
unfalsifiable rather than probabilistic, and sidesteps any retry loop. The two soft preferences —
Boomers within `BoomerBombProximity` (2) rows of a buried bomb, Crawlers in rows with
`CrawlerLateralSpace` (3) or more empty cells — are implemented as *ordering*, not filtering:
preferred rows shuffle to the front, everything else follows as fallback, so the per-level count is
always met. Boomers are placed first because "near a bomb" is the narrower preference. GameBootstrap
seeds placement from the level (`level * 0x85EBCA6B`) so a level's enemies are as deterministic as
its board, but on a different constant so the two streams aren't locked in step.

Campaign only: **Endless placement is explicitly deferred to R6**. 9 new tests, 333/333 pass.

#### R5.14 — HUDView enemy popups

Two new popup cases on the existing `OnScore` switch, no new plumbing — `ScoreSource.EnemyKill`
→ "+100 CRAWLER!" / "+450 BOOMER! ×3", `ScoreSource.BoomerBlast` → "+200 BOOM!". Two new colors
so all five celebrations stay tellable apart at a glance: lime for a kill, violet for a Boomer
blast.

**`ScoreEvent` carries no enemy type — the popup recovers it by arithmetic.** `Detail` is the kill
bonus and `AwardEnemyKill` computes `Points = basePoints × bonus`, so `Points / Detail` lands
exactly on `CrawlerKillPoints` (100) or `BoomerKillPoints` (150). Integer division is exact.

One guard: a Boomer that detonates in open air destroys 0 blocks → "+0 BOOM!". `BoomerBlast`
popups are suppressed at 0 points.

#### R5.15 — VfxManager enemy effects

Four effects: Crawler death (8 small olive particles, 0.28 s), Boomer death (18 bright orange
particles radiating evenly + its own `SpawnRipple`, 0.5 s), Crawler wake flash (one-shot expanding
pale square), and the Boomer's standing halo.

**The Boomer glow reuses the bomb-fuse material** (`EnsureFuseMaterial`, the additive radial glow
from §5.10) — a Boomer is a bomb with legs. It pulses ~1.5 Hz, deliberately slower and calmer than
a lit fuse (which races from 2.5 → 12 Hz).

**The events don't carry enough to draw with, so the view caches positions.** `VfxManager` keeps an
id → (type, cell) marker cache, seeded from `EnemySpawned` and refreshed from `GetAllAlive()` on a
**0.2 s timer** rather than per frame: `GetAllAlive()` allocates a fresh list, and a Crawler only
steps every 0.8 s, so 0.2 s is always well inside one move at 5 allocations/second instead of 60.

One edge case: a Boomer killed by a plain crush never detonates (§6.5), so `BoomerDetonated` never
fires — `OnEnemyKilled` covers that path explicitly.

#### R5.16 — AudioManager enemy SFX

Four procedural clips, one shared `_enemySource`, wired in `AudioManager.Rewire()`. The two
activation cues sit at opposite ends of the register on purpose: a Crawler chirps HIGH (something
small just started moving toward you), a Boomer boops LOW (something heavy woke up).

**The Boomer boom is built from `SfxSynth.Shatter` tuned the opposite way to the chunk burst** —
mostly TONE (`noiseMix: 0.35`) at 70 Hz with a heavy low-pass (`0.06`), so it lands round and
bass-heavy rather than crackly. That is exactly what separates it from the bomb blast, which is
pure `Noise` at a much brighter low-pass (0.25).

**Verified by measuring the generated waveforms, not by ear:**

| clip | length | zero-crossings/s |
|---|---|---|
| Crawler wake (Tone 1500 Hz) | 0.100 s | 2 990 (≈1 495 Hz ✓) |
| Crawler death (Noise) | 0.150 s | 13 213 |
| Boomer wake (Tone 150 Hz) | 0.200 s | 295 (≈147 Hz ✓) |
| **Boomer boom** | 0.300 s | **337** |
| *bomb blast (for contrast)* | 0.349 s | *10 884* |

The Boomer boom is **32× darker than the bomb blast**.

#### R5.17 — EnemyView (R5 feature-complete)

`View/EnemyView.cs`: one SpriteRenderer per living enemy, parented under the BoardView GameObject
so it's torn down with the board on the next `LoadLevel()`. Created *before*
`SpawnEnemiesForBoard()` so it receives every `EnemySpawned`.

**Sorting order 8 — above the tiles (0) and exit glow (1), below the avatar (10).** When a Crawler
walks onto the driller's cell, the driller stays visible at the exact moment contact damage fires.

Dormant vs active is the whole visual language: dormant is `dormantAlpha` (0.4) and *perfectly*
still; waking is what makes it opaque and starts it moving. The Crawler shuffles (lateral wiggle +
a half-rate vertical bob); the Boomer only breathes (scale pulse). Animation offsets are applied ON
TOP of the eased base position, the same split `CameraShake` uses for follow-vs-shake.

**Positions are polled, not evented** — `RefreshTargets()` reads `GetAllAlive()` every 0.1 s
(a Crawler only steps every 0.8 s) and the per-frame lerp does the smoothing; 10 list
allocations/second instead of 60. 333/333 tests still pass.

---

### R6.1 — DeathTracker + death recap (F01) — delivered 2026-10-05

`PROMPTS-CLAUDE-CODE-R6.md` / `FEEDBACK-COMPILATION.md` were **not in the repo** at delivery time;
built from the §10 R6.1 row.

**Core.** `DeathCause` enum (ChunkCrush, Collapse, BurstShockwave, BombBlast, Enemy, Suffocation)
and `DeathTracker`: logs every heart actually lost (`DamageHit`: cause, cell, run time, hearts left),
keeps a run clock and the time since the last drill (the only meaningful "why" for suffocation in
v3.1, where drilling is the main air source), and freezes a `DeathReport` on `NotifyDeath`. `Died`
fires **once** per run — `NotifyDeath` returns false afterwards — which also fixes a latent
double-`EndRun` if the last heart and the last breath went in the same frame. The report holds a
copy of the hits, so it survives `Reset()`. Pure bookkeeping: it never deals damage. 10 tests.

**Wiring.** `OnAvatarCrushed()` → `OnAvatarCrushed(DeathCause)`; each of the five damage events
tags its cause. **`HealthDepleted` no longer calls `EndRun`** — it fires *inside*
`TryTakeDamage`, before the hit could be logged — so `OnAvatarCrushed` logs the hit, then declares
the death itself when `!IsAlive`. (`AudioManager` still listens to `HealthDepleted` for the game-over
jingle.) `DeathTracker.Reset()` sits beside every `HealthSystem.Reset()` (per run and per campaign
level, since hearts refill per level); not on the endless seam, where hearts carry over.

**View.** `DeathRecapView` is a plain class (not a MonoBehaviour, no second UIDocument) that builds
a block inside UIScreenManager's overlay panel: the killer in red, one diagnostic line
("Dernier forage il y a 7,3 s" for suffocation, "Coup fatal à 1:16 · air restant N %" otherwise),
a one-sentence French tip pointing at the telegraph the player missed, and the heart-loss timeline
(last 5, fatal row in red). `ShowGameOver(string reason, DeathReport? recap = null)` — the body line
now only carries the Daily Dig result and collapses when empty (`SetBody`).

**Layout fix found by screenshot:** stacked above the run summary, the card overflowed the screen
and UI Toolkit squeezed the rows on top of each other. The recap and the run summary now sit **side
by side** in one row, and the rows are `flexShrink = 0`. Verified in play mode (UnityMCP): a
3-hit death (bomb → crush → burst) and a suffocation, zero console errors.

**Test-mode note:** the suite now runs in **EditMode** (PlayMode discovers 0 tests) after the
"Change and configuration of test script" commit — §13/§14 still say PlayMode.

---

### R6.2 — Air start buffer (F02) — delivered 2026-10-05

`AirSystem.BeginStartBuffer(grace)`: `grace` s of **zero drain**, then a 12 s linear ramp from
**0.5× to 1×** the drain rate (`DefaultStartGrace` 3 s, `StartRampDuration` 12 s, `StartDrainFactor`
0.5). This is both halves of the plan line — the "buffer" is the grace window, the "reduced initial
drain" is the ramp. Net gift: grace + 3 full-rate seconds (= 32 % air on lvl 1-3, 42 % on lvl 4+).

- **Integrated, not sampled:** `Tick` drains `DrainRate × ∫factor`, so one long frame straddling the end
  of the grace window drains exactly what 60 small ones would (pinned by a test).
- **Opt-in:** without `BeginStartBuffer` the factor is 1, so no existing test or caller changed.
  `Reset()` cancels a running buffer; GameBootstrap always resets *then* calls `LoadLevel`, which opens it.
- **Not on the endless seam:** `LoadLevel(freshBoard: false)` from `AdvanceEndlessSegment`, otherwise
  every 60 rows would hand out a free breather.
- **Per-level grace in `CampaignManager.AirStartGraceForLevel`:** 5 s on levels 1-3 (same onboarding
  split as drain and wobble), 3 s after. **`DrainRateForLevel` was NOT touched** — §15.1 says no
  drain retune without harness evidence, and the buffer alone fixed the measured F02 failures.
- Tick no longer fires `AirChanged` for a tick that drained nothing (grace, or `dt <= 0`).

Measured with a new `NEWCOMER` harness profile — see §15.8. Also fixed the harness's missing
`RestoreDrill()` (drift since R5.2). Play-mode verified: air held at 100 % through the 5 s grace on
level 1, then read 94.72 % at 2.4 s into the ramp — exactly the integral. 12 new tests, 365/365.

**HUD grace cue (added in the same step, on request):** pale shimmering fill + `AIR · N` countdown +
a gold fuse strip burning down the bar during grace, a white flash when the clock starts, pale → cyan
through the ramp (§5.9). Core gained `StartGraceRemaining` / `StartGraceDuration` for the countdown
(+1 test, 366/366). Screenshot-verified in grace (menu + live) and in the ramp. Deliberately NOT a
centre-screen "3-2-1" (it would hide the board during the window meant for reading it, and reads as
"wait" when the player can drill immediately) and no per-second beep (collides with the fuse beep).

---

### R6.3 — Drill-to-breathe hint (F02) — delivered 2026-10-05

View only (`HUDView.TickBreatheHint`), no Core change, so no new tests — 366/366 still pass.
v3.1 made drilling the main air source (+0.5 % per drill) but nothing in the game ever said so; the
hint teaches it at the exact moment it matters. Design choices:

- **Placement above the air bar**, not in the centre popup slot (celebrations live there, over the
  avatar) — the hint sits next to the thing it explains.
- **Hysteresis 30 % / 35 %** so a player drilling right at the threshold doesn't make it strobe.
- **Feedback loop:** each air gain while it's up triggers a scale pop — "do this" followed by "yes,
  that". Detected from `AirChanged` (air went up), so drills, capsules and bursts all count with no
  new wiring.
- **Urgency:** pulse 4 → 10 Hz as air drops, amber → red under 15 % (the bar itself turns red at 25 %).

Verified in play mode (UnityMCP): screenshot at 14 % (first attempt was bare amber text, unreadable over
amber/pink blocks → dark pill + outline), then 26 % shown → +6 % to 32 % still shown with pop → 38 % hidden.

Shown every time, not only for new players — it's also the low-air warning. If playtests find it
noisy for veterans, gate it behind R6.11's XP level rather than removing it.

---

### R6.4 — Enemy visibility (F04) — delivered 2026-10-05

View only, 366/366 unchanged. Root cause was in the code, not the tuning: enemies were plain unit
squares tinted from the block palette's own colour families (Crawler dark green ≈ teal blocks, Boomer
orange ≈ amber blocks and bombs), and a dormant enemy was a 40 %-alpha square **on top of a solid
block** — the exact cell where placement (R5.13) always puts them. Full write-up in §5.17. In short:
new procedural silhouettes with an outline and open/shut eyes (`EnemySprites`), lime/violet colours no
block uses, dormant breath + active brightness pulse, a "!" on wake and over a threatening Crawler.

**SFX — the actual collision:** the R5.16 Crawler wake chirp was a 1500 Hz sine, 0.1 s; the bomb fuse
beep is a 1200 Hz sine, 0.035 s. Nearly the same sound, and fuse beeps are frequent. Both wake cues are
now **sweeps** (Crawler 650 → 1700 Hz "bwip!", Boomer 260 → 110 Hz "wom") — nothing else in the game
sweeps that fast. New **danger alert**: a *descending* two-note arpeggio (B5 → F5); every other
arpeggio in the game rises and means good news, so the falling one reads as "uh-oh". Rate-limited to
one per 0.8 s.

Verified: screenshots of dormant/active enemies in open air and on amber/pink blocks, the wake "!" pop,
and the danger "!" (reflection confirmed `InDanger` on a Crawler two cells from the avatar on its row).
Audio not measured by ear in this session — **listen to the three new clips in play mode.**

---

### R6.5 — Air capsule icon (F05) — delivered 2026-10-05 — Sprint 1 complete

The old AI tile had three problems, all visible in the PNG itself: the icon (a jerry can) covered
~20 % of the tile — a few screen pixels at the 16-row camera; its symbol was a water **drop**, so it
read as water/fuel, not air; and the flat cyan field sat close to the teal ColorB blocks.

**First pass rejected:** a bubble tile (one big bubble + two rising). The dev read the bubbles as water
drops too. Lesson: air has no shape, so every abstract icon (bubble, cloud, swirl) is ambiguous — the
tile has to *say* it.

**Shipped:** a Mr. Driller-style **pill capsule with "AIR" written on it** — the genre's own convention
(Mr. Driller is the reference game), in the HUD air bar's cyan, on a deep-blue tile no block uses. The
word is the HUD bar's caption, so "this fills that" needs no explanation. Generated by
**`Editor/CapsuleTileGenerator.cs`** (menu *Hollow Lines → Regenerate Air Capsule Tile*): stadium
SDF + letters as stroked segments (no font in an editor script), ~1.5 px anti-aliased, written to the
SAME path and size (512×512) — `.meta` import settings and `BoardView`'s `Resources.Load` untouched.

**Shimmer:** `BoardView.ApplyMaterial` now gives AirCapsule cells a second instance of the
`DiamondShine` shader (`EnsureCapsuleMaterial`), tuned softer and cyan (pulse 2.2 Hz / 0.18, one slow
gloss sweep ≈ every 3 s) so a capsule and a diamond never twinkle alike. A drilled capsule falls back
to the default material through the same path as a drilled diamond. Screenshot-verified at real cell
size in the tutorial board; 366/366.

---

### R6.6 — Drill score popups (F07) — delivered 2026-10-05

View only, 366/366. Every drill now shows what it earned, right where it happened: "+10", then
"+20 ×2", "+30 ×3"… — a visible staircase that makes the streak multiplier legible (design rule 1,
"drilling IS the reward"). Details in §5.9.

- **Where the number comes from:** GameBootstrap snapshots `Score` at the top of the `Drilled` handler
  and passes the delta at the bottom. Rejected alternatives: re-deriving `10 × streak` in the view
  (duplicates ScoreSystem, misses a drilled diamond's +150), or pairing `OnScore(Streak)` with
  `Drilled` in HUDView (fragile — depends on which handler GameBootstrap subscribed first).
- **Two screenshot fixes:** centred on the cell the popup covered the avatar (it falls into the cell
  it just drilled) → moved to the right of the cell; raw teal was too dark on the black well → colour
  lifted 30 % toward white, outline thickened.
- Verified in play mode: four straight-down drills on the tutorial's teal vein, frozen with
  `timeScale = 0`, show the +10 / +20 ×2 / +30 ×3 / +40 ×4 staircase in streak colour.

**R6.6 follow-up — lateral drills (dev playtest, same day).** The dev saw the streak not grow when
drilling sideways through same-colour blocks — confirmed intended (v3.1 vertical-only streak). But the
popup exposed a quirk: a lateral drill pays `10 × CurrentStreak` regardless of its colour, so it showed
e.g. a teal "+40 ×4" over a pink block. **Option A shipped:** `ShowDrillPopup(..., buildsStreak)` —
GameBootstrap passes `direction == Down && oldType.CanFuse()`; drills that don't build the streak get a
plain white "+40" (no "×N", base size). Score untouched. **Open design question parked on R6.11** (see
the note under §10 R6 in `refactoring-plan.md`): the XP-to-next-tier system may change how lateral
drills should pay.

---

### R6.7 — Streak counter + crack (F07) — delivered 2026-10-05

View only (`HUDView`, `AudioManager`), 366/366. No Core change: `StreakTracker.StreakBroken(lost)`
already carried the ended count. Details in §5.9. Design notes:

- **Hidden at ×1.** StreakBroken is immediately followed by StreakGrew(1) for the new colour; showing
  "×1" made every colour change look like a (tiny) streak. Now the counter only exists for a real run.
- **The crack only plays for a ×2+ loss**, visually and audibly — losing a ×1 isn't a loss.
- **Why clipped halves, not particles:** the thing that breaks is the number itself; two
  `Overflow.Hidden` boxes each holding a full copy of the label (right one offset by -½ width) give a
  clean split with zero new assets.
- Verified in play mode at `timeScale` 0.04: ×6 teal counter after six drills down the tutorial vein,
  then a diamond (neutral) and an amber block (break) — captured the shudder (colour draining) and the
  split ("×" and "6" falling apart in red-grey). The diamond drill also confirmed the R6.6 rule: a white
  "+210" (60 streak pay + 150 diamond, no "×N" because a diamond doesn't build the streak).

---

### R6.8 — D-pad walks (F11) — delivered 2026-10-05

`GameInput`: D-pad ←/→ OR-ed into `MoveAxis` beside the left stick, so `AvatarController`'s walk
repeat treats both the same. D-pad ↑/↓ deliberately stay unbound in gameplay — they would only
duplicate the Y / A drills. Menus are unaffected (gameplay input is still gated on `timeScale == 0`).
Compile-checked; **feel needs a hardware check** (§16: the XInput hop can't be simulated).

---

### R6.14 — Level-end settle (F14) — delivered 2026-10-05, pulled forward from Sprint 4

**Bug (dev playtest):** reaching the floor fired `LevelCompleted` and showed the screen in the SAME
frame with a score snapshot, then froze `timeScale`. Chunks still wobbling/falling, lit fuses and
Boomer chains froze behind the overlay and were thrown away by the next `LoadLevel` — their points
never counted, though the player could see them start.

**Fix (View + one Core query):** `BeginLevelEnd(showScreen)` replaces the direct screen calls for
the campaign level win and the tutorial win. While `_levelEnding`:
- `GameInput.Locked` — no walk, no drill (the board plays on, the avatar stands still);
- `OnAvatarCrushed` early-returns — nothing can hurt a player who already won;
- the air tick is skipped (restores still land; `AirDepleted` can't fire);
- everything else ticks normally, so late bursts/bombs/kills score through the usual events
  (popups, VFX, SFX included).

`TickLevelEnd` shows the screen once `!_gravity.IsBusy && !_bombSystem.HasArmedBombs &&
_chainTracker.CurrentChain == 0`, after at least `LevelEndMinSeconds` (0.6 s — let the win land) and
at most `LevelEndMaxSeconds` (4 s — never strand the player). The screen gets the FINAL score — a
separate "Fin de niveau : +X" line was tried and dropped (dev call: read as a bonus on top). `LoadLevel()` clears the state, so every
restart/advance path cancels a settle in progress. Endless has no win → untouched. Crawlers walking
around don't hold the settle (they never "finish").

**Core:** `BombSystem.HasArmedBombs` (3 tests). 369/369 EditMode.

---

### R6.9 — Volume options (F10) — delivered 2026-10-05 — Sprint 2 complete

`View/OptionsMenu.cs` (new): `VolumeSettings` (master / music / sfx, 0..1, PlayerPrefs keys
`hl.volume.*`, default 1) and `OptionsMenu`, the three slider rows, hosted inside UIScreenManager's
overlay panel. "Options" on the main menu and pause; details in §5.12.

- **Levels scale the designed mix, never exceed it.** 100 % = the mix as tuned (`sfxVolume` 0.6,
  `musicVolume` 0.18), so a fresh install sounds exactly as before. Général goes on
  `AudioListener.volume` (one place for everything). `AudioManager` now keeps its SFX sources in a
  list (`NewSource` registers them; music is removed and follows its own slider) and applies the saved
  levels in `Awake`. A dedicated `_uiSource` plays the Effets preview, so no streak pitch leaks in.
- **Gamepad:** a row is one focusable element; ←/→ adjusts (event swallowed so focus stays), ↑/↓ moves
  between rows by the default navigation. B is read in `UIScreenManager.Update` (with Esc/Start) as
  "back" while the options are open.
- **Verified in play mode (UnityMCP):** screenshot of the screen; NavigationMoveEvent ←×2 on Général
  → 80 %, focus stayed, `AudioListener.volume` 0.8, saved to PlayerPrefs; ↓ → Musique; ← → music
  source 0.162 (= 0.18 × 0.9); Retour → main menu, and from pause → back to pause. Test prefs reset
  afterwards. **Listen to it with a real pad** — the physical XInput hop can't be simulated (§16).
  369/369 (View only, no new tests).

---

### R6.12 — Help page (F16) — delivered 2026-10-06

`View/HelpScreenView.cs` (new), wired like R6.9's Options: "Aide" on the main menu and the pause
screen; Options and Aide now share one back target (`_subScreenReturn` / `CloseSubScreen`, renamed
from `_optionsReturn` / `CloseOptions`). Details in §5.12.

- **8 pages**, short icon + sentence lines, written to answer what playtesters asked: the goal (exit
  line, score gate from level 4, Endless, Daily), controls, drilling and the vertical-only streak (with
  Hard = 2 hits, Steel = explosions only), air and hearts, chunks and bursts, bombs (including "a bomb
  YOU light has radius 1 — one sidestep"), enemies (Crawler hurts, Boomer never does), bonuses.
- **Numbers are read from Core constants**, so a balance change updates the help automatically.
- **Icons are the game's own sprites** (tiles from `Resources/Tiles`, procedural `EnemySprites` at
  ×1.5 — their 16×16 canvas carries margin).
- **Verified in play mode (UnityMCP):** screenshots of pages 1, 3, 7; ←/→ flips pages with focus kept
  on the page; "Retour" from pause returns to pause and hides the help block. First-pass text bug
  caught on screenshot: "Plus tu marques, plus vite tu passes" misdescribed the score gate → now
  "la sortie s'ouvre dès que tu as les points". 369/369 (View only).

---

### R6.13 — Blast radius feedback (F03) — delivered 2026-10-06

Players couldn't tell what a lit bomb would hit — especially since R5.18 made the radius depend on
who lit it (1 for the player's own drill, 2 otherwise) and chains reach cells the first bomb never
could. Now the answer is drawn on the board for the whole fuse.

- **Core:** `BombSystem.ArmedRadiusAt`, `CopyArmedCells`, `PredictBlastZone` (§ core-systems note).
  Prediction follows the real rules exactly (shared cross arrays, chain bombs at ChainBlastRadius) and
  a test compares it with the actual `BlastResolved` cells. 7 new tests, **376/376**.
- **View:** `VfxManager.AnimateBlastZones` (§5.10): red frames on the bomb's own cross, pale-yellow on
  chain-only cells, pulse ramping with the fuse, extra flare while the avatar is inside.
- **Verified in play mode (UnityMCP)** on the tutorial bomb chamber: player-lit bomb (radius 1) →
  its cross red; the bomb below chains (radius 2) → its arms, including the third bomb, pale yellow;
  after detonation all 16 pooled frames hide. **Screenshot fix:** the first frame (tinted rim + faint
  fill) was nearly invisible on pink and amber blocks → 1 px black edge, thicker rim, hatching, and a
  pale-yellow chain colour instead of orange.

---

### R7.1 — MomentumTracker — delivered 2026-10-07

`Core/MomentumTracker.cs`, per drill-momentum.md §M1. `StreakTracker` stays until R7.12; nothing
is wired yet (R7.6). Four gaps in the spec, filled as follows:

- **Color bonus only for real colors (`CanFuse`).** Two capsules, or Hard → HardCracked, in a row
  would otherwise "chain". A non-color drill adds +1.0 and sets `ColorChain` to 0.
- **`PowerDrillActivated` is `Action<float>`** carrying the ×6, per decision D1 (the M1 API block
  still said `()`).
- **Drills during a Power Drill** refresh the window and count in `DrillCount` but add no progress
  and never re-fire — GameBootstrap can route the burst's 2nd block through `NotifyDrill` safely.
- **`MomentumLost` / `TierChanged(old, 0)` fire only when there was momentum** (no per-frame spam
  from an idle `Tick`/`Reset`). `ExtendTimer` is a no-op at idle (no phantom window from a graze)
  and ignores negative values. No cap on graze extensions — revisit at R7.15 if it's abusable.

Extras for the HUD: `TimeRemaining`, `Progress`. 24 new tests, 400/400 EditMode.

---

### R7.2 — GrazeSystem — delivered 2026-10-07

`Core/GrazeSystem.cs`, per drill-momentum.md §M3.2 / §M7.4. Nothing wired yet (R7.6).

- **Danger-agnostic:** `NotifyDrill(GridPos drilled, IEnumerable<GridPos> dangers)` — Core systems
  don't reference each other, so GameBootstrap gathers the cells (active enemies from
  `GetAllAlive()`, armed bombs from `CopyArmedCells()`). Grazes when the drilled cell is ON or
  cardinally adjacent to a danger (Manhattan ≤ 1; "on" covers zone dangers). Returns bool too.
- **`GrazeTriggered` is `Action<GridPos>`** (the drilled cell, for the popup/VFX anchor) — §7 showed `()`.
- **Cooldown 0.5 s** starts only on a real graze. `MomentumExtension = 0.3f` lives here so R7.6
  doesn't hard-code it; the +50 pts belong to `ScoreSystem.AwardGraze()` (R7.5).
- **R7.6 ordering:** call it BEFORE `_bombSystem.NotifyDrilled` / `_enemySystem.NotifyAdjacentDrill`
  in the Drilled handler (§7 already does), or the bomb you just lit / enemy you just woke grazes.
- **⚠️ Open for R7.6 — "shockwave en cours" (M3.2 condition 3) has no source:** the GravitySystem
  shockwave resolves instantaneously in the burst frame, so there is never one "in progress" to
  drill through. Options: drop it, or substitute "adjacent to a wobbling chunk" (a real 0.6 s danger).

13 new tests, 413/413 EditMode.

---

### R7.3 — AvatarModel freefall — delivered 2026-10-07

`AvatarModel.FreefallCells` + `event Action<int> FreefallCell` (running count for this fall), per
drill-momentum.md §M3.3. Counted in `Tick`'s fall loop — one per cell stepped into; back to 0 on
landing and on `Teleport` (respawn). Nothing wired yet (R7.6 → `ScoreSystem.AwardFreefall()`, R7.5).

- **The drill follow-through never counts.** Drilling DOWN makes the avatar drop into the cell it
  just emptied; without a guard every downward drill would also pay a freefall cell. `TryDrill`
  remembers that cell (`_drilledBelow`) and the first fall step into it is skipped; any void below
  it pays normally. Sideways/up drills don't mark anything. Cleared on landing so it can't go stale.
- **Event carries the running count** (`Action<int>`) for a possible "FREEFALL ×N" HUD — §7 showed `()`.
- **⚠️ Balance watch (R7.14):** walking off a 1-row step and stepping back up pays +15 per loop.
  Probably harmless (air keeps draining while grounded and it never descends), but if the harness
  or playtests show farming, add a minimum drop (e.g. ≥ 2 cells) or pay only below the deepest row.

8 new tests, 421/421 EditMode.
