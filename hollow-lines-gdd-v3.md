# HOLLOW LINES — Game Design Document v3.1 (arcade pivot)

**Working title:** Hollow Lines
**Genre:** Casual arcade / action descent
**Platform:** PC first, mobile stretch goal
**Engine:** Unity 6 (URP 2D), C#
**Session length:** 1–3 min (campaign level) · 3–10 min (endless run)
**Target audience:** Casual-to-mid players who like arcade action with score chasing (Mr. Driller, Downwell, Spelunky crowd), ages 16–40
**Art style:** Pixel art

**v3.1 arcade pivot:** After completing the v3 implementation (R1-R4, 260 tests), a pairwise
mechanic analysis (matrix) revealed that several systems conflicted with the game's arcade
identity: the color streak rewarded lateral routing instead of descent, the diamond gate forced
collection that interrupted forward momentum, and the planned Digger/Tank enemies added
puzzle-solving that slowed the tempo. The v3.1 pivot resolves every conflict identified in the
matrix by making all non-descent mechanics either passive (streak) or optional (diamonds, enemies).

Key changes from v3: Color Streak is vertical-only (lateral drills are streak-neutral). Diamonds
are an optional bonus (+150 pts, no gate). Enemies are Crawler + Boomer only (Digger/Tank removed).
Bomb fuse reduced 2.5→1.5 s. Every drill restores +0.5% air. Campaign win = depth + score minimum.

---

## 1. Elevator pitch

> You're a tiny driller descending through layers of colored blocks. Drill streaks of the same color for rising combos, undermine huge chunks to make them shatter on impact, chain bombs together for massive bursts — and find air pockets before you suffocate. Campaign teaches you the ropes; Endless is the real game: how deep can you go?

**Design DNA:** Mr. Driller's avatar-in-well with self-created danger × Downwell's score-chasing depth run × the visceral satisfaction of blocks bursting and chains popping.

---

## 2. Design pillars

1. **Drilling IS the fun.** Every drill tap must feel good. The score systems reward the act of drilling itself, not an abstract goal built on top of it. 🆕v3.1: every drill also restores air — drilling IS survival.
2. **Big bursts, big rewards.** Chunk Bursts and Bomb chains are the highlight moments — spectacle and score aligned. The player should think "yes!" when they see a big chunk wobbling or three bombs lined up.
3. **Readable danger.** Every hazard is telegraphed (0.6 s wobble, bomb fuse beep). No unfair deaths.
4. **Depth is destiny.** Going deeper is always good. The game never asks the player to stop descending. 🔄v3.1: no mechanic may require lateral routing or backtracking. Everything optional is on the way down.
5. **Survive by attacking.** Air drains constantly; drilling, capsules and aggressive play (burst bonuses, bomb liberations) are the only way to breathe. Passive play = suffocation. 🔄v3.1: drilling itself is the primary air source.

---

## 3. Core loop

### 30-second loop

```
Read the blocks below → Pick a color path or a sap target
     → Drill down (streak builds) → Chunks collapse / burst
          → Bombs chain → Grab air capsules → Go deeper
```

### Run loop (Endless)

```
Spawn at surface → drill down through procedural layers
→ score via streaks, bursts, bombs → find air to survive
→ layers get harder (more hard/steel, fewer capsules)
→ die when air or hearts run out → final score = depth + points
```

### Campaign loop

```
Level N → reach the bottom + meet score minimum → level N+1 (new mechanic introduced)
→ complete all 10 → unlock Endless mode
```

---

## 4. Core mechanics

### 4.1 The well

- Grid: **7 columns × variable rows** (portrait-friendly, reduced from 10 for tighter routing decisions).
- Top 3 rows are empty (spawn zone).
- Bottom row is solid floor (campaign = win line; endless = extends as the player descends).
- Cells are typed: see §4.4.

### 4.2 The driller (avatar)

- Pixel art character on the grid; occupies one cell.
- **Move:** step left/right on the grid (one cell per input, with repeat-on-hold).
- **Drill:** 4 directions (down = primary, left/right = routing, up = risky). Drill a solid cell → cell becomes void.
- **Fall:** if the cell below is void, the avatar falls (same gravity as chunks but instant — no wobble for the avatar).
- **Step-up:** drilling left/right at the same height as a single block automatically climbs 1 row (existing mechanic preserved).

### 4.3 Chunks (color fusion)

Same-color adjacent cells (cardinal) fuse into rigid chunks via union-find. Chunks are the unit of gravity: when any cell in a chunk loses support, the *entire chunk* wobbles (0.6 s telegraph) then falls as one slab.

Chunks do **not** auto-clear on any color count. They are physical objects, not match-3 groups. Their role in v3 is twofold:
- **Routing targets:** a vein of same-color blocks is a scoring highway (Color Streak).
- **Burst targets:** a large chunk that falls from height shatters for massive points (Chunk Burst).

### 4.4 Cell types

| CellType | Drill cost | Fuses? | Notes |
|---|---|---|---|
| Empty (void) | — | — | Passable; the result of drilling or bursting |
| Color A | 1 tap | Yes | ~40% of terrain. 3 colors max per board |
| Color B | 1 tap | Yes | ~30% of terrain |
| Color C | 1 tap | Yes | ~20% of terrain (introduced level 2+) |
| Hard | 2 taps | No | 1st tap → HardCracked (visual change), 2nd → Empty |
| HardCracked | 1 tap | No | Already cracked Hard block |
| Steel | Not drillable | No | Removed only by bomb blast (→ Hard) or Chunk Burst shockwave (→ Empty) |
| AirCapsule | 1 tap | No | Drill → Empty + air restored; bomb proximity → liberated (collected) |
| Bomb | Not drillable | No | Armed by adjacent drill or chunk landing; see §4.8 |

Map chars for string maps: `.` Empty · `A/B/C` Colors · `H` Hard · `S` Steel · `P` AirCapsule · `X` Bomb

### 4.5 Gravity & wobble

Unchanged from v2. When a chunk (or single non-fusing cell that lost support) has no solid cell below any of its bottom edge:
1. **Wobble phase** (0.6 s default; 0.8 s on campaign levels 1–3): visual shake, audio warning. The player can move out from under it.
2. **Fall phase:** chunk drops 1 cell per physics tick until it lands on something solid or the floor.
3. **Crush:** if the avatar is in the landing zone, −1 heart.

**New in v3 — Burst on impact:** see §4.6.

### 4.6 Chunk Burst (new core mechanic)

When a chunk lands after falling **2 or more rows**, it **shatters on impact**:
- Every cell in the chunk → Empty.
- VFX: explosion of colored particles matching the chunk's color.
- SFX: satisfying crunch/shatter, pitch scales with chunk size.
- Camera shake proportional to chunk size.

**Scoring:**
```
Burst points = cells_in_chunk × 25 × fall_height_bonus
fall_height_bonus = floor(fall_distance / 2)   — i.e. ×1 at 2-3 rows, ×2 at 4-5, ×3 at 6-7...
```

A 6-cell chunk falling 4 rows = 6 × 25 × 2 = **300 pts**.

**Shockwave:** a burst generates a 1-cell-radius shockwave (cardinal directions from every cell of the burst). Shockwave effects:
- Color / Hard / HardCracked → Empty (destroyed).
- Steel → Hard (softened — same as bomb).
- AirCapsule → liberated (air collected).
- Bomb → armed (starts fuse).
- Avatar → hit only by the chunk's own **footprint** (where a crushed player stands), **not** by
  the shockwave ring. A sidestep out from under the chunk during the wobble is a clean dodge — the
  ring damaging the avatar would punish a correct read and break rule 4 ("no unfair deaths").
  Resolved in R2.8; see `CLAUDE.md` §5.2 deviation 5.

**Chain potential:** a burst's shockwave can remove supports from adjacent chunks, causing *them* to wobble and fall — potentially triggering another burst. This is the primary chain mechanic in v3 and is far more intuitive than void-line cascades: the player saps a big chunk, it falls and bursts, the burst destabilizes neighbors, chain reaction.

**Design intent:** the player scans the board for large chunks with sapable supports. Undermining a 10-cell chunk perched 4 rows above the floor is the "clip-worthy" moment. It's visual, dramatic, and plannable without requiring a full row to be empty.

### 4.7 Color Streak (v3.1: vertical-only passive bonus)

Successive **downward** drills on the **same color** build a streak multiplier:

```
Drill DOWN on color X:  base_drill_pts × streak_step
  1st X in a row: 10 × 1 = 10
  2nd X in a row: 10 × 2 = 20
  3rd X in a row: 10 × 3 = 30
  ...
  Nth X in a row: 10 × N
```

**🔄v3.1: only downward drills count.** Lateral and upward drills are completely ignored
(streak-neutral — no increment, no reset). This eliminates the Streak × Burst conflict
identified in the pairwise matrix: bursts destroy blocks laterally, but the streak only cares
about the vertical column the player is descending through. The streak becomes a passive
bonus of natural descent rather than an active routing system.

**The streak resets to 0 only when the player drills DOWN on a *different* color.**

- **Streak-neutral (ignored):** Hard, HardCracked, AirCapsule, Diamond, all lateral/up drills.
- **Never reached:** Steel and Bomb are not drillable.
- **Does not reset on:** moving without drilling, falling, being hit, waiting.

**Design intent (v3.1):** the player drills downward through vertical color veins generated
by the StrateGenerator's cohesion system. The streak builds naturally without routing decisions.
The player never thinks "should I go left for the blue vein?" — they just drill down, and the
streak is a pleasant bonus when the colors line up. The SFX pitch rises, the glow appears, and
it feels good — but it's never worth a detour.

### 4.8 Bombs (redesigned philosophy)

**v3 philosophy: bombs are a jackpot, not a punishment.**

Buried bombs are terrain cells. Not drillable directly. Armed by:
- Drilling any adjacent cell (cardinal).
- A chunk landing on or adjacent to the bomb.
- A Chunk Burst shockwave reaching the bomb.

**Fuse:** 🔄v3.1: **1.5 s** (was 2.5 s) with accelerating beep + flash. The shorter fuse keeps the
arcade tempo fast — the player arms, sidesteps, boom, continues. No long pauses.

**Blast:** cross-shaped, radius 2 (all cells in cardinal directions up to 2 cells away). Effects:
- Color / Hard / HardCracked → Empty.
- Steel → Hard (softened).
- AirCapsule → **liberated** (air collected, NOT destroyed — v3 change).
- Other bombs in blast radius → sympathetic detonation (chain).
- Avatar in blast radius → −1 heart.

**Bomb scoring:**
```
Per bomb explosion:   blocks_destroyed × 25
Sympathetic chain:    ×2 multiplier per additional bomb in the chain
  1 bomb:  blocks × 25 × 1
  2 bombs: blocks × 25 × 2
  3 bombs: blocks × 25 × 3
  ...
```

A 3-bomb sympathetic chain destroying 20 blocks total = 20 × 25 × 3 = **1,500 pts**.

**Pocket bomb:** rare consumable (0–1 per board). Placed on an adjacent void cell, instant blast (no fuse). Useful for triggering a chain from a safe position.

**Design intent:** the player sees a cluster of bombs and plans how to set them off in sequence for maximum destruction. Three bombs in a line = a setup worth routing toward. Bombs also liberate air capsules instead of destroying them, so bombing near capsules is actively good — the incentives are aligned with spectacle instead of fighting it.

### 4.9 Air system

> **Values below are the shipped R2.8-balanced numbers, not the original v3 draft.** The draft
> assumed these were rare events; measured on a real board they are not (a level-10 descent produced
> 78 bursts + 34 capsules), so the restore economy was cut ~20× to keep the suffocation clock from
> going decorative. Full rationale and the air-ledger evidence: `CLAUDE.md` §15.1.

| Parameter | Value | Notes |
|---|---|---|
| MaxAir | 100% | |
| DefaultDrainRate | 5%/s | Endless / debug baseline |
| Campaign drain | 4%/s levels 1–3 · 7%/s levels 4–10 | Raised (R2.8c) once depths were tripled — drain only bites on a long-enough run |
| Endless drain ramp | 5%/s + 0.5%/s per 20 rows of depth, capped at 10%/s | ✅ R3.2 — `EndlessManager.DrainRateForDepth` (CLAUDE.md §6.3); depth is ABSOLUTE across segments |
| **Drill restore** | **+0.5%** | 🆕v3.1 — every drill tap restores air. The core arcade survival loop: drill to breathe |
| AirCapsule restore | +6% | Drilled or liberated by bomb/burst |
| Chunk Burst restore | +0.5% per burst | Flat bonus per burst event (not per cell) |
| Bomb chain restore | +1% per bomb in chain | Rewards aggressive bombing |

Air at 0% → game over.

**v3.1 change:** every drill restores +0.5% air. This is the single most important arcade change:
the player who drills actively stays alive. The player who stops to plan, route, or solve a puzzle
suffocates. Air is no longer just a timer — it's a direct reward for the core action. Combined with
the vertical-only streak, the message is clear: drill down fast, everything else follows.

### 4.10 Perfect Clear (former void-line, now rare bonus)

When all 7 cells of a row are void (from any combination of drilling, bursting, bombing), the row collapses: everything above shifts down 1 row.

This is **no longer the core mechanic**. It's a rare, emergent bonus:
- **+500 points** flat.
- **+6% air** restored.
- Text popup: "PERFECT CLEAR!"
- The cascade step is still tracked (the popup and chain SFX read it) but **no longer scales the
  points** — Perfect Clear is a flat +500. The draft's `+500 × cascade` averaged ×4–×7 and let
  Perfect Clear take 79–91 % of a run's score, the opposite of "a bonus, not a goal" (rule 7).
  Removed in R2.8b; see `CLAUDE.md` §15.2.

The player doesn't plan for this; it happens naturally when a big Chunk Burst or bomb chain clears a row. When it does happen, it's a "holy shit" moment.

**CollapseSystem behavior is unchanged** — it still detects full-void rows and shifts. It just fires much less often and feeds into the bonus scoring path instead of being the primary reward.

### 4.11 Depth score

Every time the avatar reaches a new deepest row (tracked per run):

```
+50 points per new row of depth
```

In Endless mode, the depth counter is the **primary leaderboard metric** (displayed prominently). Score is secondary.

### 4.12 Health system

Unchanged from v2: 3 hearts, 1.5 s i-frames. Damage sources:
- Chunk landing on avatar (crush).
- Chunk Burst footprint on the avatar (a crushed player standing in the shattering chunk — the burst
  ring itself is harmless, §4.6).
- Bomb blast hitting avatar.
- Hearts at 0 → game over.

### 4.13 Win / lose

**Campaign (🔄v3.1):**
- **Win:** avatar reaches the bottom rows + **score meets the level minimum** (§6). Levels 1-3 have no score minimum. The score gate forces engagement with burst/bomb/enemy mechanics without requiring lateral routing.
- **Lose:** air depleted OR hearts depleted.

**Endless:**
- **No win condition.** Play until death.
- **Lose:** air depleted OR hearts depleted.
- **Score screen:** depth reached + total score + best streak + biggest burst.

---

## 5. Scoring summary

| Action | Points | Notes |
|---|---|---|
| Drill (base) | 10 × streak_step | 🔄v3.1: streak tracks downward drills only |
| Chunk Burst | cells × 25 × fall_bonus | fall_bonus = floor(distance/2) |
| Bomb explosion | blocks_destroyed × 25 × chain_mult | chain_mult = # of bombs in chain |
| Depth (new row) | 50 | Per new deepest row reached |
| Perfect Clear | 500 flat | Rare bonus; cascade no longer multiplies (R2.8b, §15.2) |
| Diamond | 150 | 🔄v3.1: optional bonus in all modes (campaign gate removed) |
| Crawler kill | 100 × parent_bonus | 🆕v3.1: × fall_bonus (burst) or × chain_mult (bomb) |
| Boomer kill | 150 × parent_bonus | 🆕v3.1: plus secondary explosion scores as bomb blast |
| Air capsule drill | 0 (streak-neutral) | Reward is the air, not points |

**Score hierarchy (by design):** Bomb chains > Chunk Bursts > Color Streaks > Depth > Perfect Clear (by rarity-adjusted expected value). The player should feel that every scoring system is worth pursuing in the moment.

---

## 6. Campaign — 10 levels

Each level is a fixed-depth board (procedurally generated from a deterministic seed). Win = reach the bottom.

> **Rows and drain are the shipped R2.8c values, not the original draft.** Depths were tripled
> (14–26 → 24–76) because the v2.1 line gate was the run's time sink and depth-only win left boards
> too short for the air clock to act; drain was raised (3/4 → 4/7 %/s) to match. The two levers are
> multiplicative — tune them together, never one alone. See `CLAUDE.md` §15.1 / §9.

| Level | Rows | New mechanic | Drain | Wobble | Content |
|---|---|---|---|---|---|
| 1 | 24 | Movement + drilling | 4%/s | 0.8 s | Colors A+B only, generous air capsules |
| 2 | 30 | Color Streak | 4%/s | 0.8 s | Color C introduced; vein-like color distribution |
| 3 | 32 | Chunks + Chunk Burst | 4%/s | 0.8 s | Scripted large chunk over a gap — first burst |
| 4 | 40 | Air capsules + drain | 7%/s | 0.6 s | Air drain becomes meaningful; capsule routing |
| 5 | 44 | Hard blocks | 7%/s | 0.6 s | 2-hit blocks force routing decisions |
| 6 | 50 | Bombs (single) | 7%/s | 0.6 s | First bomb encounter; scripted safe detonation |
| 7 | 54 | Bomb chains | 7%/s | 0.6 s | 2-3 bombs placed for chain; first sympathetic |
| 8 | 60 | Steel blocks | 7%/s | 0.6 s | Steel as routing obstacle; bomb to soften |
| 9 | 68 | Full mix | 7%/s | 0.6 s | All mechanics combined |
| 10 | 76 | Boss layer | 7%/s | 0.6 s | Dense, hard final board; high bomb density for spectacle |

**Onboarding approach:** each new mechanic is introduced with a scripted setup that teaches it safely before it appears randomly. Level 2 has a long same-color vein from spawn. Level 3 has a pre-built large chunk resting on a single drillable support. Level 6 has a lone bomb next to a non-threatening cluster.

### Level 2 — Streak tutorial

The first 5 rows below spawn contain an obvious vertical vein of color A (4+ blocks straight down). Drilling straight through it triggers a ×4 streak with rising SFX — the player learns the mechanic by doing the most natural thing (drilling downward).

### Level 3 — Burst tutorial

Below the spawn zone, a large chunk (6+ cells of the same color) sits on a single drillable block with 3+ rows of empty space beneath. Drilling the support → wobble → fall → burst. The player can't miss it.

---

## 7. Endless mode

### Board generation

- Procedural layers (strates) with progressive difficulty.
- Seed-based for daily leaderboard consistency (same seed for all players on a given day = "Daily Dig").
- A run is a **chain of generated segments** (60 content rows each): a GridModel has a fixed height,
  so "infinite" is segment chaining, with depth, difficulty and the air tank carried across the seam.
  See CLAUDE.md §6.3 (Core) and §5.15 (View flow).
- Depth ramp over ~80 rows, then steady-state. Rates are **continuous**, not banded:
  `rate = difficulty × cap`, where `difficulty = min(1, depth / 80)`.

✅ **Shipped values (R3.1, measured 2026-07-26 — CLAUDE.md §15.7).** The original draft table
(hard 25 % / steel 12 % / empty 12 % at the deep end) was never measured; when it finally was, deep
endless had *less* color material and *fewer* burst opportunities than the hardest campaign board —
breaking design rules 1 and 2 at the exact point a run should peak.

| | Cap at steady state | Note |
|---|---|---|
| Colors | 2 below difficulty 0.10, then 3 | |
| Hard rate | 10 % | was 15 % |
| Steel rate | 8 % | was 12 % |
| Bomb rate | 8 % | kept — campaign 10 ships ~9 % bomb cells |
| Air rate | 6–10 % (random per strate) | |
| Empty rate | 28 % → **40 %** with difficulty | NOT the draft's 25 %→12 %: burst opportunity scales ~`EmptyRate²` (§15.3) |

Resulting terrain at steady state: ~27 % color cells, ~19 % hard+steel, ~14 % of solid cells sitting
over a 2-row drop (campaign 10, for reference: 32 % / 13 % / 13 %).

### Air drain ramp

```
drain_rate = 5.0 + (depth / 20) × 0.5   (capped at 10%/s)
```

At row 0: 5%/s (20 s budget). At row 100: 7.5%/s (13.3 s budget). The player must play increasingly aggressively to survive.

### Score screen

| Metric | Display |
|---|---|
| Depth | Primary — "248 m" (large font) |
| Score | Secondary — "14,350 pts" |
| Best streak | "×12 Blue" |
| Biggest burst | "9-cell burst ×3" |
| Bombs chained | "5-bomb chain" |
| Perfect Clears | "2 Perfect Clears" |

### Daily Dig

Same seed for all players on a given day. One attempt per day (or unlimited with a "practice" flag that doesn't post to leaderboard). Shareable score card (Wordle-style minimal grid showing depth + key moments).

✅ **Shipped (R3.4)** — `DailyDig.SeedFor(date)` maps the **UTC calendar day** to a seed (SplitMix64 finalizer, so consecutive days give unrelated wells); the run itself is an ordinary endless run on that seed. Entry point: "Défi du jour" on the main menu.

**Two deliberate deviations, both waiting on R3.5** (CLAUDE.md §6.6): the *one-attempt lock* is not enforced — client-side it would only punish honest players while a save-file edit walks around it, so the day keeps your **best depth + best score** and counts attempts instead. The *share card* is deferred to when there is a leaderboard to share against.

---

## 8. Game design — Critical rules (v3)

These rules are **non-negotiable** — they define Hollow Lines v3:

1. **Drilling IS the reward.** Every drill tap gives points (×streak). The player never drills "for free."
2. **Big chunks burst.** Fall from 2+ rows → shatter. Spectacle and score are the same thing.
3. **Bombs are friends.** They liberate air capsules, chain for multipliers, and clear paths. The player wants to find bombs.
4. **Wobble telegraphs everything.** 0.6 s warning. No unfair deaths. (0.8 s on campaign 1–3.)
5. **Air rewards aggression.** Capsule drilling, burst bonuses, and bomb chain bonuses all restore air. Passive play = suffocation.
6. **Depth is always forward.** Campaign win = reach the bottom. Endless score = how deep. Never stop descending.
7. **Perfect Clear is a bonus, not a goal.** A full void row is a rare jackpot (+500), not the core mechanic.

---

## 9. Delta from v2.1 — What changes in the codebase

### Systems to rewrite

| System | v2.1 behavior | v3 behavior |
|---|---|---|
| **ScoreSystem** | DrillPoints=10 flat; LineBase×chain+source bonus | Streak multiplier on drill; Burst scoring; Bomb scoring; Depth scoring; Perfect Clear bonus |
| **CampaignManager** | Win = depth + line gate | Win = depth only; no line gate |

### Systems to modify

| System | Change |
|---|---|
| **GridModel** | Column count 10 → 7 (parameterize `Width`) |
| **ChunkSystem / GravitySystem** | Add Burst detection: when chunk lands, if `fall_distance >= 2` → fire `ChunkBurst` event with chunk cells + fall distance; clear all chunk cells |
| **BombSystem** | AirCapsule in blast → liberate (collect air) instead of destroy; add bomb scoring event; add chain multiplier tracking |
| **CollapseSystem** | Void-line still detected + shifts rows; fires `PerfectClear` event instead of being the primary mechanic |
| **AirSystem** | Add `RestoreBurst()` (+0.5%) and `RestoreBombChain(bombCount)` (+1% per bomb); remove `RestoreLine()` from primary loop (keep for Perfect Clear) |
| **StrateGenerator** | Width 10 → 7; adjust rates for v3 ramp; remove undermine tutorial band; add streak tutorial vein (level 2) + burst tutorial setup (level 3) |
| **HUDView** | Replace line gate panel with streak counter; add burst popup; remove tutorial hint about void lines |
| **AudioManager** | Add streak pitch-rising; add burst shatter SFX; modify bomb SFX to feel rewarding |
| **VfxManager** | Add burst particle explosion; add streak glow on avatar; add bomb chain flash |

### Systems unchanged

| System | Why |
|---|---|
| **AvatarModel** | Movement, drilling, step-up — all unchanged |
| **HealthSystem** | 3 hearts, i-frames — unchanged (burst shockwave is a new damage source wired in GameBootstrap) |
| **CameraShake** | API unchanged; just new callers (burst, bomb chain) |
| **BoardView** | Cell rendering logic unchanged; new VFX layered on top |
| **GameInput** | Input unchanged |
| **UIScreenManager** | Screens unchanged; score screen content updated |

### New systems

| System | Role |
|---|---|
| **StreakTracker** | Pure C# in Core/. Tracks current color + count. `NotifyDrill(CellType)` → increments or resets. `CurrentStreak`, `CurrentColor`, `StreakBroken` event. Streak-neutral types (AirCapsule, Hard, etc.) are ignored, not reset. |
| **DepthTracker** | Pure C# in Core/. Tracks deepest row reached. `NotifyPosition(GridPos)` → fires `NewDepthReached(int row)` if deeper than previous best. |

### Tests impact

Most of the 112 existing tests survive — they test Core system APIs that don't change (GridModel, ChunkSystem, GravitySystem, AvatarModel, CollapseSystem, ChainTracker, AirSystem, HealthSystem, BombSystem basics). Tests to update:
- ScoreSystem tests → full rewrite (new formulas).
- CampaignManager tests → remove line gate assertions, simplify win condition.
- BombSystem tests → update capsule blast behavior (liberate vs destroy).
- StrateGenerator tests → update width and rate parameters.

New test suites needed:
- StreakTracker tests (streak build, reset on different color, neutral on capsule, etc.).
- DepthTracker tests.
- Chunk Burst integration tests (fall distance detection, cell clearing, shockwave propagation).
- Bomb scoring + chain multiplier tests.

---

## 10. Milestone plan (from current state)

### R1 — Core redesign (~1 week)

| Step | Task |
|---|---|
| 1 | GridModel: parameterize width (7 default); update FromStringMap |
| 2 | StreakTracker: new system + tests |
| 3 | DepthTracker: new system + tests |
| 4 | ChunkBurst: add fall tracking to GravitySystem; burst detection + shockwave; events + tests |
| 5 | ScoreSystem: full rewrite (streak, burst, bomb, depth, perfect clear) + tests |
| 6 | BombSystem: capsule liberation; chain multiplier tracking; scoring events + tests |
| 7 | CampaignManager: remove line gate; depth-only win + tests |
| 8 | CollapseSystem: rename void-line event to PerfectClear; adjust wiring |
| 9 | AirSystem: add RestoreBurst, RestoreBombChain; adjust wiring |

### R2 — View layer update (~1 week)

| Step | Task |
|---|---|
| 1 | StrateGenerator: 7-wide boards; v3 rate tables; tutorial setups (streak vein, burst setup) |
| 2 | HUDView: streak counter, burst popup, depth display; remove line gate panel + void-line hint |
| 3 | VfxManager: burst explosion particles, streak glow, bomb chain flash |
| 4 | AudioManager: streak pitch-rising, burst shatter SFX, rewarding bomb SFX |
| 5 | UIScreenManager: updated score screen (depth + score + stats) |
| 6 | GameBootstrap: rewire all new events; update tick order for burst + streak |

### R3 — Endless mode (~1 week)

| Step | Task |
|---|---|
| 1 | StrateGenerator.EndlessBoard: progressive depth ramp with v3 rate tables |
| 2 | AirSystem: depth-based drain ramp |
| 3 | DepthTracker: integrate with Endless win/display |
| 4 | Daily Dig: seeded daily board + score card |
| 5 | Leaderboard: ASP.NET Core / PostgreSQL API (separate project, already scoped) |

---

## 11. Event flow (v3) — Typical burst chain

```
Avatar drills colored cell (Color A)
  → StreakTracker.NotifyDrill(A) → streak = 3 (was 2)
  → ScoreSystem.AwardDrill(streak=3) → +30 pts
  → The drilled cell was supporting a 6-cell chunk of Color B
  → GravitySystem: chunk loses support → wobble (0.6 s) → falls 3 rows
  → Chunk lands → fall_distance = 3 → ChunkBurst fires
    → 6 cells cleared → +6 × 25 × 1 = 150 pts
    → AirSystem.RestoreBurst() → +0.5% air
    → Shockwave: arms a buried bomb adjacent to burst zone
  → BombSystem: bomb armed → 1.5 s fuse → BOOM
    → Blast destroys 7 blocks, liberates 1 air capsule
    → AirSystem.RestoreCapsule() → +6% air
    → ScoreSystem.AwardBomb(destroyed=7, chainMult=1) → +175 pts
    → Blast hits another bomb → sympathetic detonation
      → 2nd bomb destroys 5 blocks
      → ScoreSystem.AwardBomb(destroyed=5, chainMult=2) → +250 pts
      → AirSystem.RestoreBombChain(2) → +2% air
  → Meanwhile, CollapseSystem detects a full void row (row 12 was mostly empty, burst + bombs finished it)
    → PerfectClear! → +500 pts (flat), +6% air
    → Everything above shifts down 1 row
  → DepthTracker: avatar is now at row 18 (new deepest) → +50 pts

Total from one drill tap: 30 + 150 + 175 + 250 + 500 + 50 = 1,155 pts + 14.5% air restored
```

That's the dream sequence. It won't happen every tap, but when it does, the player clips it.

---

## 12. Coding conventions

Unchanged from v2 — see CLAUDE.md §6. Key points:
- All code comments and identifiers: **English**.
- All communication with the developer: **informal Québécois French**.
- Pure C# in `Core/` (no MonoBehaviour, no UnityEngine).
- `readonly struct` for data carriers.
- `event Action<T>` for inter-system communication.
- One step at a time. Tests before integration. Explain then code.
