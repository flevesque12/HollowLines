---
paths:
  - "**/Tests/**"
  - "**/CoreTests*"
---

## 13. Test status

**353 / 353 passing** (+10 for R6.1 `DeathTrackerTests`; was 112 before the v3 refactor; +3 for `Settle`, +0 net from the §5.2
dev-5 flip, +1 for `EndlessBoard_ContentRows_HavePorosity` — see §15.6; +6 for the tutorial
showcase board — see §5.14; +5 for AvatarModel coyote time — see §4; +16 for R3.2 endless,
+2 for the R3.1 steady-state guards (§15.7), +6 for R3.4 Daily Dig (§6.6); +28 for R4 Diamonds
— CellType/StreakTracker (§4), `DiamondSystemTests`, GravitySystem/BombSystem liberation,
`ScoreSystemTests`, the campaign gate, `StrateGenerator` placement (campaign exact-count +
endless rate), and the tutorial showcase's new Diamonds chamber (§5.14); +3 for R5.1 — the
vertical-only streak (`DrillDirection`, §6.1); +3 for R5.2 — `AirSystem.RestoreDrill()`; +0 for
R5.3 — `BombSystem.FuseDuration` constant-only change; +4 net for R5.4 — the `CampaignManager`
score gate replacing the diamond gate (3 diamond-gate tests removed, 7 score-gate tests added);
+0 for R5.5 — `EnemyType`/`KillMethod`/`EnemyEntity`, pure data structures; +19 for R5.6 —
`EnemySystemTests`, the new core-lifecycle suite; +5 for R5.7 — Crawler lateral movement;
+9 for R5.8 — the Boomer death blast and its chain reaction; +6 for R5.9 — the three
activation triggers; +5 for R5.10 — avatar contact damage; +10 for R5.11 — `AwardEnemyKill`
and `AwardBoomerBlast`; +9 for R5.13 — campaign enemy placement; +6 for R5.18 — the BombSystem
blast-radius split (§5.3) — `DirectBlastRadius`/`ChainBlastRadius` by arming source, plus the
sympathetic-chain-always-radius-2 rule; **+4 for the R5.18 dodge-validation pass** — one-step
lateral dodge, drilling two rows past a player-armed bomb, a sympathetic bomb's wider reach, and
a player already off both bombs' cross entirely).
Run via `run_tests` in **PlayMode** (see §14). The `tools/playtest` harness builds and runs clean.
R4 was also confirmed live in the Unity Editor (UnityMCP): compile, full PlayMode run, and
screenshots of the diamond counter, the `DiamondShine` tile glint, and a live score/DiamondSystem
delta on collection — not just `dotnet build` against the Core sources.

| Suite | Notes |
|---|---|
| GridModel, ChunkSystem, AvatarModel, Gravity | M1 tests, several reworked for the v3 burst rules |
| `ChunkBurstTests` | 🆕 burst threshold, shockwave effects (ring no longer hits the avatar — §5.2 dev 5), crush→burst ordering, cascade, `Settle`; 🆕R4 `Shockwave_LiberatesDiamond` |
| `StreakTrackerTests` / `DepthTrackerTests` | 🆕; 🆕R4 `Diamond_IsStreakNeutral_DoesNotBreakOrGrow` |
| `ScoreSystemTests` | 🆕 all 5 paths + clamping + tracked bests; 🆕R4 `AwardDiamond` (flat, per-collection, event source); 🆕R5.11 +10 `AwardEnemyKill` (both types, default bonus, bonus clamp, event source+detail) and `AwardBoomerBlast` (formula, bonus clamp, negative-blocks clamp, event source+detail) |
| `DiamondSystemTests` | 🆕R4 7 tests: init, collect tally, gate at 0/N, `AllDiamondsCollected` fires once, reset, re-`Init` between levels (§6.4) |
| `CampaignManagerTests` | 🔄R5.4 score gate (replaces the R4 diamond gate) — 7 tests: blocks below minimum, passes at minimum, catches up while standing at the bottom, no gate on levels 1-3, depth still required regardless of score, default `int.MaxValue` keeps gate-agnostic callers on depth-only, `ScoreMinimumForLevel` matches the §9 table |
| `EnemySystemTests` | 🆕R5.6 19 tests: spawn/activate (+ unknown-id no-op), `GetEnemyAt`/`GetAllAlive` queries, kill by crush/burst/bomb (+ shockwave ring, dead-doesn't-reappear, already-dead no-op, miss-doesn't-kill), Boomer detonation on Burst (fall_bonus) and Bomb (chainMult) kills but never on Crush, Crawler never detonates, dormant enemies die like active ones (§6.5 — only avatar contact, R5.10, will check `IsActive`), `Reset`; 🔄R5.7 +5 Crawler movement: moves right after one interval, bounces off the right wall then keeps heading left, bounces off a solid block, stays put while dormant even after 2s, a full 12-interval round trip in a 7-wide row lands back at the start; 🔄R5.8 +9 Boomer death blast: destroys an adjacent color block, softens Steel to Hard, liberates an AirCapsule/Diamond (event + grid), leaves a buried Bomb completely untouched, kills an adjacent Crawler, a chain-killed Boomer also detonates, a 4-deep chain caps at 3 detonations (the 4th still dies), never fires an avatar-harm event; 🔄R5.9 +6 activation triggers: adjacent drill wakes a dormant Crawler (and 2 cells away does not), a burst shockwave cell adjacent to a dormant enemy wakes it without killing it, a bomb blast cell adjacent does the same, `ActivateInViewport` wakes enemies at both edges of an inclusive row range and leaves enemies outside it dormant; 🔄R5.10 +5 avatar contact: an active Crawler overlapping the avatar fires (even without moving that frame), a dormant Crawler on the same cell never fires, an active Boomer on the same cell never fires, an active Crawler elsewhere never fires, a Crawler that steps into the avatar's cell mid-Tick fires |
| Air, Health, Blocks, Bombs, Strate, Campaign | M3 tests, updated for v3 APIs; 🆕R4 `BombSystemTests` diamond liberation ×2 |
| `StrateGeneratorTests.TutorialBoard_*` | 🆕 6 tests: well-formed/deterministic, settles-without-bursting, ×6 streak, support-drill burst, ×3 bomb chain, Perfect Clear collapse (§5.14); 🆕R4 2 more for the Diamonds chamber (settle survival, end-to-end collection via `DiamondSystem`) |
| `StrateGeneratorTests` (enemies) | 🆕R5.13 9 tests: level 5 places none, level 6 = 3 Crawlers, level 7 = 3C+2B, all ten levels match the §9 table, content-rows-only (never spawn/floor), minimum row spacing holds across levels 6-10, always on a solid Color/Hard cell, the board is never mutated, and placement is deterministic for a given seed |
| `StrateGeneratorTests` (diamonds) | 🆕R4 9 tests: `DiamondCountForLevel` matches §9, campaign board diamond count matches the table, `PlaceDiamonds` never touches spawn/floor/Steel/Bomb/Empty, count=0 no-op, endless rate produces diamonds across seeds |
| `EndlessManagerTests` | 🆕R3.2 14 tests: GDD drain curve + cap, depth accounting (spawn zone = 0, records only), segment seam continuity, exhaust guard, deterministic/distinct segments, Reset |
| `StrateGeneratorTests.EndlessSegment_*` | 🆕R3.2 2 tests: `startDepth 0` ≡ `EndlessBoard`, and the difficulty ramp resumes; 🆕R3.1 2 steady-state guards: color material > 24 %, burst opportunity > 11 % (§15.7) |
| `DeathTrackerTests` | 🆕R6.1 10 tests: run clock (ignores dt ≤ 0), hit recording (cause/pos/time/hearts), None/Suffocation never a heart hit + hearts clamp, time-since-last-drill, `Died` fires once with a frozen report (second same-frame death ignored), suffocation carries the drill gap + air clamps to 0, `None` falls back to last hit then Suffocation, everything frozen after death, report survives `Reset`, `Reset` re-arms |
| `DailyDigTests` | 🆕R3.4 6 tests: same day = same seed (any hour), different days differ, consecutive days generate *unrelated boards*, seed stable across runs, never `int.MinValue`, invariant label |

> **Note:** the M2 Collapse (21) and Score (23) suites referenced in the old plan were never
> actually ported to NUnit — they lived in the retired console harness. CollapseSystem had only
> 2 Steel tests; ScoreSystem had none. Both now have real coverage.

**Three v3 gravity tests were deliberately shortened to 1-row drops** (`Bridge_LosingBothSupports`,
`StackedChunks_FallAndLandStacked`, `FallingChunk_OntoAvatar_FiresCrush`) so they keep testing
slab cohesion / stacking / crush rather than accidentally testing bursts. `ChunkBurstTests` owns
the shatter cases.
