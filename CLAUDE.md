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
blocks. Drill streaks of the same color for rising combos, undermine huge chunks to make
them shatter on impact, chain bombs together for massive bursts — grab diamonds for bonus
points, crush lurking creatures with falling blocks — and find air pockets before you
suffocate. Campaign teaches you the ropes; Endless is the real game.

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
  1. **Color Streak** — successive same-color drills multiply points.
  2. **Chunk Burst** — chunks falling 2+ rows shatter on impact for massive points.
  3. **Bomb Bonanza** — bomb chain explosions are rewarded (not punished); bombs liberate
     air capsules instead of destroying them.
- **Depth scoring** added — every new deepest row = +50 pts.
- **Campaign simplified** — win = reach the bottom (no line gate).
- **Endless mode** — infinite descent, air drain accelerates with depth, primary leaderboard
  metric is depth reached.

**Full GDD:** see `hollow-lines-gdd-v3.md` in project root.

### v3.1 arcade pivot — What changed from the first v3 pass

After completing R1-R4 and running the matrix (pairwise mechanic analysis), a core tension
emerged: several mechanics asked the player to **stop descending** to engage with them (streak
routing, diamond gate, Digger/Tank puzzle-solving). This conflicted with the game's identity
as an arcade action-descent.

**v3.1 changes — ✅ ALL SHIPPED (R5.1-R5.17, 2026-09-01).** Every item below is implemented,
tested and play-mode verified; see the R5 delivery notes in `.claude/rules/refactoring-delivery-notes.md`.
- **Color Streak** → vertical-only: only drills **downward** build the streak.
- **Diamonds** → optional bonus: no longer a campaign win gate. +150 pts anywhere.
- **Enemies** → simplified to 2 types: **Crawler** + **Boomer** (Digger and Tank removed).
- **Bomb fuse** → reduced from 2.5 s to **1.5 s** for faster arcade tempo.
- **Air** → every drill restores **+0.5% air**. Drill to stay alive.
- **Campaign win** → depth reached + **score minimum** (replaces diamond gate).

---

## 11. Design rules — NON-NEGOTIABLE (v3.1 arcade)

1. **Drilling IS the reward.** Every drill tap gives points (×streak) AND air (+0.5%). Never drill "for free." 🔄v3.1: drill also restores air.
2. **Big chunks burst.** Fall from 2+ rows → shatter. Spectacle = score.
3. **Bombs are friends.** Liberate air, chain for multipliers, clear paths. Player wants bombs.
4. **Wobble telegraphs everything.** 0.6 s warning. No unfair deaths. (0.8 s campaign 1–3.)
5. **Air rewards aggression.** Drilling, capsules, bursts, bomb chains all restore air. Passive = death. 🔄v3.1: drilling itself is the primary air source.
6. **Depth is always forward.** Never stop descending. No gates that require lateral routing or backtracking. 🔄v3.1: score gate replaced diamond gate.
7. **Perfect Clear is a bonus, not a goal.** Full void row = rare jackpot (+500), not the loop.
8. **Diamonds are free candy.** 🔄v3.1 (was "on the way down"). Optional +150 pts bonus. Grab them if they're there; ignore them if they're not. Never gated, never required.
9. **Enemies amplify the action.** 🔄v3.1 (was "die to physics"). Crawlers are bonus targets killed by physics. Boomers amplify destruction — their death explosion creates more chaos. No enemy should interrupt the descent or require puzzle-solving to defeat.

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
  in **PlayMode** (not EditMode — see historical note below)
- Behavioral: headless bot harness in `tools/playtest/` (`dotnet run`, .NET 8)
- Every new Core API ships with NUnit coverage in the same session

### Workflow
- **One step at a time.** Deliver one system per session.
- **Tests before integration.** Every new system ships with passing tests.
- **Explain, then code.** Design rationale before implementation.
- **No MonoBehaviour in Core.** Unity wrappers are separate and thin.

---

## 14. Historical notes

- **Tests run in PlayMode, not EditMode** — the assembly definition doesn't have `Editor` in
  its platforms. Not blocking; left as-is.
- **v2.1 CLAUDE.md** documented the void-line-centric design. This file (v3) supersedes it.
- **`hollow-lines-gdd-v3.md` was synced to shipped values on 2026-07-23.**
- **Procedural-first assets, not zero-asset.** All SFX are synthesized (`SfxSynth`), and VFX are
  built from generated primitives — including three procedural **shaders**: `FuseGlow`, `DiamondShine`,
  and `ExitGlow`. The block *sprites* are the exception: AI-generated tiles loaded from
  `Resources/Tiles/` by `BoardView`, with a procedural white-square fallback — except `tile_capsule.png`,
  which is generated by `Editor/CapsuleTileGenerator.cs` since R6.5. Enemy sprites are procedural (`EnemySprites`, R6.4).
- **Bot playtest harness** — `tools/playtest/` compiles real Core sources. Fully v3-wired (R2.7).
- **`spawnCell` must be the middle column** — `(3,2)` for 7-wide. Scene serialized value beats code default.
- **Debug/testing toggles on GameBootstrap:** `disableAir` (gates air tick) and `invincible`
  (early-returns in `OnAvatarCrushed`). Both off by default. Pure View flags — Core untouched.
- **Endless serialized fields** (`endlessMode` / `endlessSeed` / `endlessSegmentRows`) have the
  same serialized-beats-default caveat. Untick `endlessMode` before shipping.

---

## Cross-references — `.claude/rules/` files

| File | Content | Loading |
|---|---|---|
| `architecture.md` | §3 Architecture + §7 Game loop wiring | Always |
| `core-systems.md` | §4 Unchanged systems + §6 New systems (6.1–6.9) | `**/Core/**` |
| `view-systems.md` | §5 Systems to modify (5.1–5.17) | `**/View/**` |
| `scoring-campaign.md` | §8 Scoring summary + §9 Campaign table | `**/ScoreSystem*`, `**/CampaignManager*` |
| `refactoring-plan.md` | §10 R1–R6 step tables (compact) | Always |
| `refactoring-delivery-notes.md` | R5 verbose delivery notes + R6 details | `**/Core/**`, `**/View/**` |
| `tests.md` | §13 Test status | `**/Tests/**`, `**/CoreTests*` |
| `balance.md` | §15 Balance findings (all resolved) | `**/AirSystem*`, `**/ScoreSystem*`, `**/StrateGenerator*` |
| `controls.md` | §16 Controls & input | `**/GameInput*` |
