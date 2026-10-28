# HOLLOW LINES — CLAUDE.md

> **What this file is:** Project context for any AI assistant (Claude chat, Claude Code,
> or any future session). Read this entire file before writing or modifying any code.
> **Split structure:** This root file is always loaded. Detailed sections live in
> `.claude/rules/` — Claude Code loads them automatically (always or conditionally by file path).

---

## 1. Project identity

| Field | Value |
|---|---|
| **Title** | Hollow Lines |
| **Genre** | Casual arcade / action descent |
| **Engine** | Unity 6 |
| **Platform** | PC first, mobile stretch goal |
| **GDD version** | v3 (mechanical redesign — see §2) |
| **Solo dev** | Frédéric Lévesque (C#/.NET, Unity) |
| **Art style** | Pixel art |
| **Inspiration** | Mr. Driller (avatar-in-well, chunk gravity), Downwell (score-chasing descent) |

**Elevator pitch:** You're a tiny pixel-art driller descending through layers of colored
blocks. Build drilling momentum for rising combos, undermine huge chunks to make
them shatter on impact, chain bombs together for massive bursts — graze past danger for
bonus points, crush lurking creatures with falling blocks — and stay ahead of the well that
collapses behind you. Campaign teaches you the ropes; Endless is the real game.

**Design philosophy (v3.1 — arcade pivot):** The fun is in drilling, bursting, and exploding.
Every drill tap rewards the player AND keeps them alive. Spectacle and score are aligned.
Nothing should ever ask the player to stop descending. If a mechanic creates a choice between
"go deeper" and "go sideways to engage with it", it must be optional — never gated.

---

## 2. v3 redesign — What changed and why

The v2.1 design centered on **void-lines** (clearing a full row of 10 empty cells). After
prototyping through M4, the developer identified a core problem: void-lines were too
cerebral and hard to execute for a casual game, chains were near-impossible, and the
feeling didn't match the vision of visceral arcade action.

**v3 changes:**
- **Grid reduced** from 10 to 7 columns (tighter decisions, mobile-friendly).
- **Core scoring replaced:** void-lines demoted to rare bonus ("Perfect Clear", +500 pts).
  Three new action-reward systems are the core:
  1. **Drill Momentum** — 🔄v3.2 (was "Color Streak"). Time-based momentum: drill fast → rising
     multipliers (×2/×4/×6). Colors become physics (chunk fusion), not active scoring.
  2. **Chunk Burst** — chunks falling 2+ rows shatter on impact for massive points.
  3. **Bomb Bonanza** — bomb chain explosions are rewarded (not punished); bombs liberate
     capsules (timber props since v3.3) instead of destroying them.
- **Depth scoring** added — every new deepest row = +50 pts.
- **Campaign simplified** — win = reach the bottom (no line gate).
- **Endless mode** — infinite descent, the pressure (air drain, then the Cave-in front since v3.3)
  accelerates with depth, primary leaderboard metric is depth reached.

**Full GDD:** see `hollow-lines-gdd-v3.md` in project root.

### v3.1 arcade pivot — What changed from the first v3 pass

After completing R1-R4 and running the matrix (pairwise mechanic analysis), a core tension
emerged: several mechanics asked the player to **stop descending** to engage with them (streak
routing, diamond gate, Digger/Tank puzzle-solving). This conflicted with the game's identity
as an arcade action-descent.

**v3.1 changes — ✅ ALL SHIPPED (R5.1-R5.17, 2026-09-01).** Every item below is implemented,
tested and play-mode verified; see the R5 delivery notes in `.claude/rules/refactoring-delivery-notes.md`.
- **Color Streak** → vertical-only (v3.1), then **replaced by Drill Momentum** (v3.2 — see below).
- **Diamonds** → optional bonus: no longer a campaign win gate. +150 pts anywhere.
- **Enemies** → simplified to 2 types: **Crawler** + **Boomer** (Digger and Tank removed).
- **Bomb fuse** → reduced from 2.5 s to **1.5 s** for faster arcade tempo.
- **Air** → every drill restores **+0.5% air**. Drill to stay alive. *(Superseded by the v3.3 Cave-in — see below.)*
- **Campaign win** → depth reached + **score minimum** (replaces diamond gate).

### v3.2 Drill Momentum — What changed from v3.1

The v3.1 color streak still created a tension: scoring required same-color routing (33%
chance of continuing straight down), while survival required fast descent. The two loops
fought for the same input (drill direction). Every successful arcade game aligns these:
Downwell, Tetris, NecroDancer, Pac-Man CE.

**v3.2 changes — ✅ SHIPPED R7.1-R7.15 (2026-10-08), incl. tutorial + balance (see `drill-momentum.md`, `balance.md` §15.9-15.10):**
- **Color Streak → Drill Momentum:** `StreakTracker` replaced by `MomentumTracker`. Time-based
  (0.8s window), not color-based. All drill directions count equally. 4 tiers: ×1 → ×2 → ×4 → ×6.
  Tier 3 = **Power Drill** (pierce 2 blocks + mini shockwave), then cycle resets to Tier 1.
- **Colors → physics system:** colors drive chunk fusion/burst (like Mr. Driller), not scoring.
  Optional color bonus: same-color drills build momentum ×1.5 faster (skill expression).
- **Cascade visible:** `ChainTracker` surfaced as ×N popup (chain events multiply subsequent points).
- **Graze bonus:** +50 pts for drilling adjacent to active enemy or armed bomb (+0.3s momentum).
- **Freefall:** +15 pts per void cell traversed in free fall; momentum maintained during fall.
- **Danger Zone:** air < 15% = all points ×2 (comeback mechanic). *(v3.3: the Cave-in margin < 15 %.)*
- **Scoring formula:** `base × momentum_mult × cascade_mult × danger_zone_mult + graze + freefall`.

### v3.3 The Cave-in — What changed from v3.2

The air was an invisible clock (a bar at the bottom of the screen), and the harness measured it as
decorative for anyone faster than a beginner. The Cave-in keeps the exact same equation — stop and die,
descend and live — but makes it **visible in the world**: the well collapses behind you.

**v3.3 changes — ✅ SHIPPED R8.1-R8.9 (2026-10-28), see `.claude/rules/cave-in.md`, `balance.md` §15.11:**
- **Air → Cave-in front** (`CeilingSystem`, absolute rows). The margin between you and the front (max 12 rows,
  the leash) is the new air; swallowed rows become `Rubble`. A hit = −1 heart and the front backs off 4 rows;
  you stay where you are (no respawn at the top — it is rubble). `AirSystem` is deleted.
- **Speeds** translated from the measured drain: 0.48 / 0.84 rows/s in campaign (levels 1-3 / 4+), 0.6 → 1.2
  in Endless by depth. Same start buffer (5 s / 3 s grace). Measured in R8.7 and kept.
- **Recoils** replace the air restores: capsules (now a **timber prop**, "étai"), bursts, bomb chains,
  Perfect Clears, Power Drills push the front back. Descending is the main source of margin.
- **Danger Zone** = margin < 15 %. HUD bar "ÉBOULEMENT", hint "DESCENDS !", crumbling-front visuals, rumble.
- **Gas pockets** were designed, built (R8.3-R8.4) and dropped (dev call) — the capsule stays instead.

---

## 11. Design rules — NON-NEGOTIABLE (v3.2 momentum)

1. **Drilling IS the reward.** Every drill tap gives points (×momentum) — and drilling **down** buys distance from the collapse. Never drill "for free." 🔄v3.2: momentum multiplier replaces color streak. 🔄v3.3: margin replaces air.
2. **Big chunks burst.** Fall from 2+ rows → shatter. Spectacle = score.
3. **Bombs are friends.** Liberate capsules (push the collapse back), chain for multipliers, clear paths. Player wants bombs.
4. **Wobble telegraphs everything.** 0.6 s warning. No unfair deaths. (0.8 s campaign 1–3.) 🔄v3.3: the collapse is always on screen.
5. **The collapse rewards aggression.** Descending is the escape; capsules, bursts, bomb chains, Perfect Clears and Power Drills push the collapse back. Passive = buried. 🔄v3.3 (was "Air rewards aggression").
6. **Depth is always forward.** Never stop descending. No gates that require lateral routing or backtracking. 🔄v3.1: score gate replaced diamond gate.
7. **Perfect Clear is a bonus, not a goal.** Full void row = rare jackpot (+500), not the loop.
8. **Diamonds are free candy.** 🔄v3.1 (was "on the way down"). Optional +150 pts bonus. Grab them if they're there; ignore them if they're not. Never gated, never required.
9. **Enemies amplify the action.** 🔄v3.1 (was "die to physics"). Crawlers are bonus targets killed by physics. Boomers amplify destruction — their death explosion creates more chaos. No enemy should interrupt the descent or require puzzle-solving to defeat.
10. **Scoring and surviving are the same gesture.** 🆕v3.2. Forer vite = survivre (rester devant l'éboulement) = scorer (momentum). No mechanic should ever create a choice between scoring and staying alive. If a scoring system requires the player to stop descending, it is broken.

---

## 12. Coding conventions

### Style
- All code comments and identifiers: **English**
- All communication with the developer: **informal Québécois French**
- `readonly struct` for immutable data carriers (ScoreEvent, etc.)
- `enum` for closed sets (ScoreSource, CellType, etc.)
- `event Action<T>` for inter-system communication (no UnityEvent in Core)
- Defensive clamping over exceptions

### Testing
- Primary: NUnit suite in `Tests/EditMode/CoreTests.cs` — runs via Unity Test Runner
  in **EditMode** (see historical note below)
- Behavioral: headless bot harness in `tools/playtest/` (`dotnet run`, .NET 8)
- Every new Core API ships with NUnit coverage in the same session

### Workflow
- **One step at a time.** Deliver one system per session.
- **Tests before integration.** Every new system ships with passing tests.
- **Explain, then code.** Design rationale before implementation.
- **No MonoBehaviour in Core.** Unity wrappers are separate and thin.

---

## 14. Historical notes

- **Tests run in EditMode** — they used to run in PlayMode because the assembly definition lacked
  the `Editor` platform. Commit `aee9538` added it; a PlayMode run now finds 0 tests.
- **v2.1 CLAUDE.md** documented the void-line-centric design. This file (v3) supersedes it.
- **`hollow-lines-gdd-v3.md` was synced to shipped values on 2026-07-23.**
- **Procedural-first assets, not zero-asset.** All SFX are synthesized (`SfxSynth`), and VFX are
  built from generated primitives — including three procedural **shaders**: `FuseGlow`, `DiamondShine`,
  and `ExitGlow`. The block *sprites* are the exception: AI-generated tiles loaded from
  `Resources/Tiles/` by `BoardView`, with a procedural white-square fallback.
- **Bot playtest harness** — `tools/playtest/` compiles real Core sources. Fully v3-wired (R2.7).
- **`spawnCell` must be the middle column** — `(3,2)` for 7-wide. Scene serialized value beats code default.
- **`AirSystem` was deleted in R8.9** (with `DeathCause.Suffocation`, the drain APIs of `CampaignManager` /
  `EndlessManager` and 54 tests). The Cave-in (`CeilingSystem`) is the only pressure system — `cave-in.md`.
- **Debug/testing toggles on GameBootstrap:** `disableCeiling` (freezes the Cave-in front — was `disableAir`
  until R8.6, renamed with `FormerlySerializedAs`) and `invincible` (early-returns in `OnAvatarCrushed`).
  Both off by default. Pure View flags — Core untouched. (`previewCaveIn` is gone since R8.6: the Cave-in IS the game.)
- **Endless serialized fields** (`endlessMode` / `endlessSeed` / `endlessSegmentRows`) have the
  same serialized-beats-default caveat. Untick `endlessMode` before shipping.

---

## Cross-references — `.claude/rules/` files

| File | Content | Loading |
|---|---|---|
| `architecture.md` | §3 Architecture + §7 Game loop wiring | Always |
| `core-systems.md` | §4 Unchanged systems + §6 New systems (6.1–6.9) | `**/Core/**` |
| `view-systems.md` | §5 Systems to modify (5.1–5.17) | `**/View/**` |
| `scoring-campaign.md` | §8 Scoring summary + §9 Campaign table | `**/ScoreSystem*`, `**/CampaignManager*`, `**/MomentumTracker*`, `**/GrazeSystem*` |
| `drill-momentum.md` | 🆕v3.2 Drill Momentum directives (M1–M10) | `**/MomentumTracker*`, `**/ScoreSystem*`, `**/CeilingSystem*`, `**/GrazeSystem*`, `**/FissureTracker*`, `**/HUDView*`, `**/VfxManager*`, `**/AudioManager*`, `**/GameBootstrap*` |
| `refactoring-plan.md` | §10 R1–R7 step tables (compact) | Always |
| `refactoring-delivery-notes.md` | R5 verbose delivery notes + R6 details | `**/Core/**`, `**/View/**` |
| `tests.md` | §13 Test status | `**/Tests/**`, `**/CoreTests*` |
| `balance.md` | §15 Balance findings (all resolved) | `**/CeilingSystem*`, `**/ScoreSystem*`, `**/StrateGenerator*` |
| `controls.md` | §16 Controls & input | `**/GameInput*` |
| `cave-in.md` | 🆕v3.3 Cave-in design (replaces air; gas dropped) — R8 ✅ | `**/CeilingSystem*`, `**/CellType*`, `**/GameBootstrap*` + 12 more (see frontmatter) |
