# HOLLOW LINES — CLAUDE.md

> **What this file is:** Project context for any AI assistant (Claude chat, Claude Code,
> or any future session). Read this entire file before writing or modifying any code.

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
tested and play-mode verified; see the R5 delivery notes in §10 for the per-step rationale and
deviations. The one piece deliberately left for later is **enemy placement in Endless** (the §9
counts are per campaign level, and Endless has no level number) — campaign placement ships.
- **Color Streak** → vertical-only: only drills **downward** build the streak. Lateral drills
  are streak-neutral. The streak becomes a passive bonus of natural descent, not an active
  routing system. Eliminates the Streak × Burst conflict (bursts no longer destroy streak material).
- **Diamonds** → optional bonus: no longer a campaign win gate. Collecting a diamond = +150 pts
  anywhere (campaign and endless). The player grabs them on the way down, never hunts them.
  `DiamondSystem.IsComplete` is no longer checked by `CampaignManager`.
- **Enemies** → simplified to 2 types: **Crawler** (lateral, fragile, bonus target) + **Boomer**
  (stationary, explodes when hit by burst/bomb — amplifies destruction). **Digger and Tank removed**
  (Digger = reactive threat that interrupted descent; Tank = puzzle that required planning).
- **Bomb fuse** → reduced from 2.5 s to **1.5 s** for faster arcade tempo.
- **Air** → every drill restores **+0.5% air**. The player who drills actively stays alive.
  Stopping to plan = suffocation. Aligns air with the core action.
- **Campaign win** → depth reached + **score minimum** (replaces diamond gate). Forces engagement
  with burst/bomb/enemy mechanics without requiring lateral routing.

---

## 3. Architecture

### 3.1 Core principle: pure C# logic, Unity as View

All game rules live in **pure C# classes** under `HollowLines.Core` — no `MonoBehaviour`,
no `UnityEngine` dependency, no `Update()`. This makes the core deterministic and
unit-testable with `dotnet run` outside of Unity.

Unity MonoBehaviours in the `Unity/` folder are thin wrappers that:
- Feed input into the core (drill direction, movement)
- Call `Tick()` on systems each frame
- Read core state and render sprites / play SFX

### 3.2 Namespace

```
HollowLines.Core    — all pure-C# game logic
```

Unity wrappers have no namespace (default global, Unity convention).

### 3.3 Folder structure (v3 target)

```
Assets/_Project/Scripts/
  Core/
    GridModel.cs          — 2D grid, cell read/write, row queries (Width parameterized, default 7)
    CellType.cs           — Cell enum + IsSolid / CanFuse / IsDrillable / DrillResult
    GridPos.cs            — Immutable grid coordinate struct
    ChunkSystem.cs        — Union-find for same-color block fusion
    GravitySystem.cs      — Wobble → fall → crush pipeline + fall distance tracking → ChunkBurst event
    AvatarModel.cs        — Player position, drilling, movement
    CollapseSystem.cs     — Void-row detection + row shift (fires PerfectClear event)
    StreakTracker.cs       — 🆕 Color streak counting (NotifyDrill → increment or reset)
    DepthTracker.cs        — 🆕 Deepest row tracking (NotifyPosition → NewDepthReached event)
    ScoreSystem.cs        — 🔄 REWRITTEN: streak, burst, bomb, depth, perfect clear scoring
    ScoreEvent.cs         — 🔄 Updated scoring event struct (Points / Source / Detail)
    ScoreSource.cs        — 🆕 Streak, Burst, Bomb, Depth, PerfectClear
    ❌ LineSource.cs      — DELETED (replaced by ScoreSource.cs)
    ChainTracker.cs       — Temporal chain counting (kept for burst cascades)
    AirSystem.cs          — 🔄 Added RestoreBurst, RestoreBombChain; drain ramp for endless
    HealthSystem.cs       — 3 hearts, i-frames (unchanged)
    BombSystem.cs         — 🔄 Capsule liberation, chain mult, scoring events, ArmBombAt()
    StrateDescriptor.cs   — Immutable config struct for one board layer
    StrateGenerator.cs    — 🔄 Width 7, v3 rates, color veins, streak/burst tutorials,
                            diamond placement (R4), ✅R5.13 campaign enemy placement (§9)
    CampaignManager.cs    — 🔄R5 Depth + score gate win (diamond gate removed — v3.1 arcade pivot)
    EndlessManager.cs     — 🆕R3 Endless progression: absolute depth across segments, drain ramp, segment chaining
    DailyDig.cs           — 🆕R3 Date → seed for the daily well (§6.6)
    DiamondSystem.cs      — ✅R4 IMPLEMENTED. Diamond collection tracking + win gate (§6.4)
    EnemySystem.cs        — ✅R5.6-5.10 Enemy entities: spawn, activation, Crawler movement,
                            kills, Boomer death blast + capped chain, avatar contact. Takes GridModel.
    EnemyType.cs          — 🔄R5 Crawler / Boomer enum (Digger + Tank removed — v3.1 arcade pivot)
  View/
    GameBootstrap.cs      — ✅R5.12 Scene entry point; game loop + wiring + endless mode flow (§5.15)
                            + diamond wiring + full enemy wiring (§7) + ✅R5.13 enemy spawning.
    AvatarController.cs   — Walk-input repeat; does NOT call AvatarModel.Tick()
    AvatarView.cs         — Sprite + lerp follow for the avatar
    EnemyView.cs          — 🆕R5.17 Enemy sprites layered over the cell grid (sortingOrder 8):
                            dim+still when dormant, shuffle/pulse when active (§6.5)
    BoardView.cs          — ✅R4 One SpriteRenderer per cell, event-driven updates + wobble shake;
                            Diamond cells get the DiamondShine material instead of a tint (§5.10);
                            🆕 exit-zone glow strips for campaign/tutorial boards (§5.16)
    GameInput.cs          — 🔄 Input System adapter: keyboard + Xbox/gamepad (§16), gated while paused
    VfxManager.cs         — ✅R4 Burst debris, shockwave ripple, streak glow/trail, chain flash, bomb fuse
                            telegraph, diamond collect sparkle (§5.10);
                            ✅R5.15 Crawler death crunch, Boomer death burst + ripple, wake flash,
                            Boomer standing halo (reuses the fuse glow material)
    CameraShake.cs        — 🔄 Owns BasePosition; camera follow + additive shake (§5.13)
    SfxSynth.cs           — 🔄 Procedural clip generator + Shatter() (layered noise + tone)
    AudioManager.cs       — ✅R4 Streak pitch-rising, burst shatter, rewarding bomb SFX, accelerating fuse
                            beep, diamond collect chime (§5.11);
                            ✅R5.16 Crawler chirp/crunch, Boomer boop, bass-heavy Boomer boom
    HUDView.cs            — ✅R5.14 enemy-kill + Boomer-blast popups (§5.9);
                            ✅R4 Streak counter, depth display, burst popup, diamond counter (§5.9);
                            🔄R3 SetDepthSource (per-board vs endless)
    UIScreenManager.cs    — 🔄 Score screen + main menu + runtime EventSystem for gamepad nav (§16);
                            🔄R3 "Sans fin" / "Défi du jour" / "Menu principal", endless wording, seam fade (§5.15)
    DailyDigStore.cs      — 🆕R3 Local best-of-day for the Daily Dig (PlayerPrefs, §6.6)
  Tests/
    EditMode/
      CoreTests.cs        — NUnit suite (runs in PlayMode — see §10)
Assets/_Project/Art/Resources/
  Tiles/                  — AI-generated block sprites (loaded by BoardView)
  Shaders/
    FuseGlow.shader       — 🆕 additive radial glow for the bomb fuse telegraph (§5.10 / §5.11)
    DiamondShine.shader   — 🆕R4 persistent per-tile shimmer for undrilled diamonds (§5.10)
    ExitGlow.shader       — 🆕 additive ambient floor glow for the campaign/tutorial exit zone (§5.16)
tools/
  playtest/
    Program.cs            — Headless bot playtest harness
    playtest.csproj       — .NET 8; compiles Core/*.cs directly
```

Legend: 🆕 = new file · 🔄 = needs modification · ✅ = R-step complete · unmarked = unchanged

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

## 5. Systems — TO MODIFY

### 5.1 GridModel
**Change:** parameterize `Width` (currently hardcoded at 10 → default 7).
- Constructor: `GridModel(int width, int height)` — `Width` becomes a readonly property.
- `IsRowVoid(row)` already iterates `0..Width-1`, so it adapts automatically.
- `FromStringMap()`: validate that all rows have the same length; set `Width` from row length.
- All systems that reference grid width must use `grid.Width` (not a hardcoded 10).

### 5.2 GravitySystem — Add Chunk Burst  ✅ IMPLEMENTED
**Change:** track fall distance per chunk; fire `ChunkBurst` event on impact.
- When a chunk enters wobble, record its starting row (bottom edge) in `ChunkState.StartRow`.
- When it lands, compute `fallDistance = chunk.MaxY - StartRow`.
- If `fallDistance >= BurstFallThreshold` (2): fire the burst.
- Burst effect: all cells in the chunk → `CellType.Empty` on the grid.
- Shockwave: 1-cell radius (cardinal) from every burst cell. Effects:
  - Color/Hard/HardCracked → Empty
  - Steel → Hard
  - AirCapsule → Empty + fire `AirCapsuleLiberated(GridPos)`
  - Bomb → fire `BombArmedByBurst(GridPos)` (left on the grid; its fuse resolves it)
  - Avatar in zone → fire `AvatarHitByBurst`
- **Crush still applies separately** — crush fires during the fall, burst on the landing tick,
  so ordering is always crush → land → burst → shockwave.

> **⚠️ Deviations from the original spec — read before touching this file:**
>
> 1. **`ChunkSystem.Rebuild()` does not exist.** ChunkSystem is stateless: `ComputeChunks(grid)`
>    recomputes from scratch at the top of every `Tick()`. No rebuild call is needed.
> 2. **A burst returns early from `Tick()`.** Mutating the grid mid-tick invalidates the
>    `chunks`/`supported` snapshot the tick is holding — without the early return, `TryMoveDown`
>    re-stamps chunks the shockwave just destroyed. The next tick recomputes from the new grid.
>    Cost: one frame (~16 ms) of latency. States are deliberately left unpruned on that path.
> 3. **Event signature carries the color:**
>    `ChunkBurst(List<GridPos> cells, int fallDistance, CellType color)`.
>    The cells are already `Empty` when it fires, so the view cannot read the color back off the
>    grid — and VfxManager needs it to tint the debris (§5.10).
> 4. **`AvatarHitByBurst` covers the chunk footprint too**, not just the shockwave ring: a crushed
>    avatar stands *inside* the chunk's cells, so ring-only would never hit them.
> 5. **✅ RESOLVED — the shockwave ring is harmless to the avatar.** Only the chunk's own footprint
>    (where a crushed player actually stands) fires `AvatarHitByBurst`; the 1-cell ring keeps every
>    effect on blocks / capsules / bombs but no longer damages the player. A single sidestep out of
>    the footprint during the wobble is now a clean dodge, satisfying design rule 4 ("no unfair
>    deaths"). Pinned by `Avatar_InShockwaveRing_IsNotHit` and the flipped
>    `DrillUp_FreesBlockAbove_KeystoneScenario` (the sidestep now escapes the burst entirely).
>    `CrushFiresBeforeBurst_WhenAvatarIsUnderTheLandingChunk` still asserts a footprint hit.

### 5.3 BombSystem — Liberate capsules + scoring  ✅ IMPLEMENTED
**Changes:**
- Blast hitting `AirCapsule` → set cell to Empty + fire `AirCapsuleLiberated(GridPos)` event.
- Track `chainMultiplier` (int): starts at 1 for the first bomb; increments for each
  sympathetic detonation in the same `ProcessExplosions()` BFS call.
- Fire `BombScored(int blocksDestroyed, int chainMultiplier)` after each individual blast.
- Pocket bomb behavior unchanged.

> **Deviations / decisions:**
> - **`ArmBombAt(GridPos)` added.** `BombArmedByBurst` (§5.2) had no way to act: `NotifyDrilled`
>   arms *neighbors*, and `TryArm` is private. Without this the burst shockwave silently failed
>   to light fuses.
> - **Steel does not count as a destroyed block.** `Steel → Hard` is softening, not destruction,
>   so it is excluded from `blocksDestroyed`. Pinned by
>   `BombScored_SteelSoftened_DoesNotCountAsDestroyed`.
> - **`chainMultiplier` is the BFS index, not the chain depth.** Two bombs that expire on the same
>   tick without being linked still score ×1 then ×2, because they share one `ProcessExplosions()`
>   call. Rare in practice; revisit if it shows up in playtest data.

> **🔄R5.18 (2026-09-01): two blast radii, keyed by who armed the bomb.** A bomb the player arms
> directly — by drilling a cell adjacent to it (`NotifyDrilled`) — blasts a tight
> `DirectBlastRadius = 1` cross (only its 4 cardinal neighbors). Every other arming source keeps
> the original `ChainBlastRadius = 2`: a landing chunk (`NotifyChunkLanded`), a burst shockwave
> (`ArmBombAt`), and — always, even if the chain traces back to a player-armed bomb — a sympathetic
> detonation. Each `ArmedBomb` entry now carries its own `ArmedByPlayer` flag alongside its fuse
> timer; `ProcessExplosions`' BFS queue carries `(GridPos, armedByPlayer)` pairs instead of bare
> positions so a sympathetic ignition can be hard-coded to `armedByPlayer: false` regardless of
> what armed the bomb that triggered it (only the ONE bomb the player's own drill directly touches
> gets the reduced radius). `TryUsePocketBomb`'s instant detonation is not "armed by drilling
> adjacent" either, so it also keeps `ChainBlastRadius` (unchanged from before this pass).
> Scoring (`blocksDestroyed`) is unaffected — it's still just however many cells the (now smaller
> or larger) cross actually reached and cleared.
>
> **The tutorial showcase's bomb chamber (§5.14) needed a layout fix.** The original three bombs
> sat 2 apart in a row (`AXCXCXA`) so the player-drilled center bomb could reach both neighbors
> directly — that needs `ChainBlastRadius`, and the center bomb now only gets `DirectBlastRadius`.
> Re-authored as a vertical stack instead: the player-armed bomb sits directly above a second bomb
> (distance 1, reachable even at radius 1), whose sympathetic `ChainBlastRadius` then reaches
> sideways to ignite a third. Still a genuine ×3 chain, verified against the real BombSystem by
> `TutorialBoard_BombChamber_ArmingCenter_ChainsToThree`.

> **R5.18 dodge-validation pass (2026-09-01):** four tests proving the radius split actually buys
> the player a dodge, not just a smaller number. `PlayerCanDodge_DirectBomb_WithOneStep` confirms a
> single lateral step off a player-armed bomb's row/column is always safe (the step lands diagonal
> to the bomb, which the cross can never reach at any radius), while the drilled cell itself — the
> player's old spot — stays lethal. `SympatheticBomb_HasLargerBlast` and
> `PlayerSafe_FromChainedBombs_BecauseAlreadyFar` confirm the flip side: a sympathetically-triggered
> bomb keeps `ChainBlastRadius` and can reach cells the originating player-armed bomb never could,
> but a player already clear of BOTH bombs' rows and columns stays safe regardless.
>
> **`PlayerCanDodge_DirectBomb_ByDrillingDown` needed a correction from spec.** The cell 1 row
> straight down from the bomb is not a valid dodge and isn't diagonal — it sits ON the down-axis at
> distance 1, exactly as reachable as the drilled cell above the bomb (the cross is symmetric across
> all four cardinal directions, not just the side the player approached from). It's also not
> reachable in one move from the arming spot — it's two rows away. The test instead verifies the
> real payoff: drilling a SECOND row down lands at distance 2, outside `DirectBlastRadius` — a
> `ChainBlastRadius` bomb would still reach that far, a player-armed one does not.

### 5.4 CollapseSystem — Rename to PerfectClear
**Changes:**
- `RowCollapsed` event → rename to `PerfectClear` (or add a new `PerfectClear` event alongside).
- Behavior is identical: detect full-void rows, shift everything above down.
- This event fires much less frequently in v3 (it's a rare bonus, not the core loop).
- GameBootstrap wires `PerfectClear` to `ScoreSystem.AwardPerfectClear(cascadeStep)`.

### 5.5 AirSystem — New restore sources  ✅ IMPLEMENTED
**Changes:**
- Add `RestoreBurst()` (per burst event).
- Add `RestoreBombChain(int bombCount)` (per bomb in a sympathetic chain).
- `RestoreLine()` renamed to `RestorePerfectClear()`. The constant `LineRestoreAmount` was
  renamed `PerfectClearRestoreAmount` at the same time — the v2 vocabulary was in the public API.
- Add `DrainRate` ramp for endless: property already settable (v2.1 change); GameBootstrap
  or EndlessManager sets it based on current depth.
- `RestoreCapsule()` now also fires on bomb/burst liberation, not just on drilling.

> **⚠️ The draft amounts were wrong by ~20× — see §15.1.** They assumed rare events; measured on a
> real board a single level-10 descent produced 78 bursts and 34 capsules. Current values:
>
> | Source | Draft | **Now** |
> |---|---|---|
> | Air capsule (drill or liberate) | +25 % | **+6 %** |
> | Chunk Burst | +5 % | **+0.5 %** |
> | Bomb chain (per bomb) | +3 % | **+1 %** |
> | Perfect Clear | +15 % | **+6 %** |
>
> `RestoreEconomy_IsSmallRelativeToDrain` guards these against drifting back up. **The root cause
> (the burst cascade) was closed by R2.8d**: `Settle()` at board load stops the board demolishing
> itself, and the in-play shockwave cascade was kept deliberately (§15.1). Re-measured on settled
> boards the pacing holds, so these values are final — **do not re-tune them** without new harness
> evidence.

### 5.6 ScoreSystem — FULL REWRITE
**New API:**

| Method | Formula | Source enum |
|---|---|---|
| `AwardDrill(int streakStep, CellType streakColor = Empty)` | `10 × streakStep` | `ScoreSource.Streak` |
| `AwardBurst(int cellCount, int fallDistance)` | `cellCount × 25 × floor(fallDistance / 2)` | `ScoreSource.Burst` |
| `AwardBomb(int blocksDestroyed, int chainMult)` | `blocksDestroyed × 25 × chainMult` | `ScoreSource.Bomb` |
| `AwardDepth(int row)` | `50` | `ScoreSource.Depth` |
| `AwardPerfectClear(int cascadeStep)` | `500` **flat** — cascade does not scale it (§15.2) | `ScoreSource.PerfectClear` |
| `AwardDiamond()` | `150` **flat** (R4, §6.4) | `ScoreSource.Diamond` |
| `AwardEnemyKill(EnemyType type, int bonus = 1)` | ✅R5.11: Crawler=100, Boomer=150, ×bonus (fall_bonus/chain_mult/1 — Digger/Tank removed, v3.1 pivot) | `ScoreSource.EnemyKill` |
| `AwardBoomerBlast(int blocksDestroyed, int parentBonus)` | ✅R5.11: `blocksDestroyed × BombPointsPerBlock × parentBonus` — scores like a bomb blast (§6.5) | `ScoreSource.BoomerBlast` |

**Read-only state:** `Score`, `BestStreak`, `BestStreakColor`, `BiggestBurst`,
`BiggestBurstFallBonus`, `BestBombChain`, `PerfectClears`, `MaxDepth`.

**Event:** `OnScore(ScoreEvent)` — View hooks for popups, SFX, shake.

**ScoreEvent struct (updated):**
```csharp
public readonly struct ScoreEvent
{
    public int Points { get; }
    public ScoreSource Source { get; }
    public int Detail { get; }  // streak step, cell count, chain mult, cascade step, depth row, or enemy-kill/Boomer-blast bonus
}
```

> **Deviations from the first draft of this section:**
> - **`AwardDepth` takes the row.** The original no-arg signature made `MaxDepth` a *count of
>   awards*, which cannot back the "DEPTH: 248" display (§5.12). It now takes the row index from
>   `DepthTracker.NewDepthReached` and keeps the max — ScoreSystem spans a whole run while
>   DepthTracker resets per board, so a shallower new level must not erase the record.
> - **`AwardDrill` takes an optional color**, and `BiggestBurstFallBonus` was added, because
>   §5.12 asks for "×12 amber" and "9-cell ×3" — neither was derivable from the original state.
> - Both additions are backward-compatible (optional param / extra field).

**ScoreSource enum (replaces LineSource):**
```csharp
public enum ScoreSource { Streak, Burst, Bomb, Depth, PerfectClear, Diamond, EnemyKill, BoomerBlast }
```

### 5.7 CampaignManager — Remove line gate
**Changes:**
- Remove `RequiredLines(level)`, `NotifyLineCleared()`, `LinesClearedThisLevel`,
  `LineProgressChanged` event.
- Win condition: `NotifyAvatarPosition()` checks depth only (avatar reaches
  `boardHeight - WinDepthFromFloor`).
- `DrainRateForLevel` and `WobbleDurationForLevel` unchanged.

### 5.8 StrateGenerator — 7-wide + v3 tutorials
**Changes:**
- Default width: 7 (all `Build`, `CampaignBoard`, `EndlessBoard`).
- Remove `InjectUndermineTutorial` (void-line tutorial no longer relevant).
- Add `InjectStreakTutorial(level 2)`: long vertical vein of Color A (4+ blocks) in the
  spawn column for the first 5 rows — drilling straight down triggers a ×4+ streak.
- Add `InjectBurstTutorial(level 3)`: large chunk (6+ cells same color) resting on a single
  drillable block with 3+ rows of empty space below — drilling the support triggers a
  guaranteed burst.

> **⚠️ The burst tutorial as originally worded is geometrically impossible.** Support is
> orthogonal-only: anchoring the lone support *vertically* makes a pillar that blocks the fall,
> and anchoring it *horizontally* puts blocks under the chunk that also block the fall. Either way
> the chunk drops 1 row and never bursts.
>
> **Implemented layout** (width 7, overwritten in place so §9 row counts stay exact): the single
> support sits at the chunk's *edge*, anchored by a pillar **outside** the chunk's footprint.
> ```
> .CCCCC.   ← 10-cell chunk
> .CCCCC.
> .....AA   ← lone support (col 5) + grounding pillar (col 6)
> ......A
> ......A   ← 4 clear rows under the overhang: the drop zone
> ......A
> AAAAAAA   ← authored floor, merges with the bedrock row directly beneath it
> ```
> Drilling the support drops all 10 cells 4 rows → burst. A player who instead digs straight down
> splits the chunk and undermines the left half, which also falls 4 rows and bursts — both routes
> teach the lesson. Verified end-to-end against the real GravitySystem by
> `CampaignBoard_Level3_BurstSetup_DrillingSupport_ActuallyBursts`.
>
> **Position: `BurstTutorialTop()` puts the band on the bedrock floor, not under the spawn zone.**
> Placed mid-board it is unanchored — with porous terrain beneath it the whole band settles, falls
> 2+ rows and bursts *on load*, wrecking the setup before the player arrives. This actually
> happened when depths were tripled in §15.1. Only the bedrock row never moves.
- Campaign ramp (unchanged structure, adjusted for v3):
  Level 1: A+B only · Level 2: +C, streak tutorial · Level 3: burst tutorial ·
  Level 4: air drain active · Level 5: Hard blocks · Level 6: single bombs ·
  Level 7: bomb chains · Level 8: Steel · Level 9-10: full mix.
- Endless ramp: see GDD v3 §7 for rate tables.
- Porosity (`EmptyRate`) still needed — chunks need gaps below them to fall and burst.
  **Raised for v3:** `EarlyEmptyRate` 0.20 → **0.28**, `LateEmptyRate` 0.30 → **0.36**. A burst
  needs *two stacked* gaps under a chunk, so opportunity scales roughly as `EmptyRate²`; the v2.1
  values made wild bursts rare. These numbers were set by estimate — see §15 for what the playtest
  harness actually measured.
- **Colors generate as veins, not per-cell rolls** (`VerticalColorCohesion` /
  `HorizontalColorCohesion`, plus the column-memory threading in `Build`). This is what makes the
  Color Streak system playable at all — **read §15.3 before touching `PickColor` or `Build`'s
  `columnColors` array**, including why it must NOT be simplified to "the previous row".
- 🆕R4 **Diamond placement** — two different mechanisms, on purpose:
  - **Campaign** (`DiamondCountForLevel(level)` + `PlaceDiamonds(board, count, rng)`): places an
    EXACT per-level count (§9 table: 0/0/0/2/3/3/4/4/5/5) by picking `count` random drillable
    color/Hard cells (never Empty, Steel or Bomb — §9 "never behind walls") via a partial
    Fisher-Yates shuffle, restricted to content rows only (never spawn, never floor). A per-cell
    probability roll can't guarantee an exact total, and the campaign win gate needs an exact
    total (§6.4), so this runs as a deterministic post-process after `Build()` and the tutorials.
  - **Endless** (`StrateDescriptor.DiamondRate`, wired into `PickCellType`'s priority chain right
    after Air, `EndlessDiamondRate = 0.02f`): a flat per-cell roll, because Endless diamonds are an
    ungated bonus (§6.4) — there is nothing to guarantee.

### 5.9 HUDView — New displays
**Changes:**
- **Remove:** line gate panel ("N / M"), void-line tutorial hint.
- **Add:** Streak counter (top-left or below score) — shows "×N" with current streak color;
  hidden when streak = 0.
- **Add:** Depth display — "DEPTH: 42" (top area, prominent in Endless).
- **Add:** Burst popup — "BURST! +300" centered, fades after 1 s.
- **Add:** Bomb chain popup — "CHAIN ×3! +750" (chainMult > 1 only).
- **Add:** Perfect Clear popup — "PERFECT CLEAR! +500" (rare, celebratory).
- 🆕R5.14 **Add:** Enemy kill popup — "+100 CRAWLER!" / "+450 BOOMER! ×3" (lime; the `×N` suffix
  only when bonus > 1, same convention as the bomb chain). The enemy type isn't on `ScoreEvent` —
  it's recovered as `Points / Detail` == `ScoreSystem.CrawlerKillPoints` or `BoomerKillPoints`.
- 🆕R5.14 **Add:** Boomer blast popup — "+200 BOOM!" (violet, distinct from the red bomb chain).
  Suppressed at 0 points, since a Boomer detonating in open air destroys nothing.
- 🆕R4 **Add:** Diamond counter, under the depth panel — "💎 2/5" in campaign, "💎 7" in Endless.
  Hidden entirely when the board has none (levels 1-3).
- Score, air bar, hearts: unchanged layout (update score source).

> **Deviation:** the three popups are driven by **`ScoreSystem.OnScore`**, not by subscribing to
> GravitySystem / BombSystem / CollapseSystem directly. Those events don't carry the point value
> the popup has to print, and re-deriving it in the view would duplicate the scoring formulas.
> `ScoreEvent` already carries `Points` + `Source` + `Detail` (= cell count / chain mult / cascade
> step) — exactly what the popups need — and it avoids three more `Rewire*` hooks for systems that
> are rebuilt every level, plus the subscription-order fragility that comes with them.
>
> `HUDView.OnLevelLoaded()` is called from `LoadLevel()`: `StreakTracker.Reset()` and
> `DepthTracker.Reset()` are silent by design, so without it the HUD keeps the previous board's
> streak and depth.

> **🆕R4 Deviation: the Endless diamond counter is per-segment, not a whole-run tally.** Both
> numbers come straight from the live `DiamondSystem`, which `GameBootstrap.LoadLevel()` re-`Init()`s
> every board load — same per-board reset as `StreakTracker`/`DepthTracker`. Unlike depth (which
> `EndlessManager` deliberately keeps absolute across segments, §6.3), diamonds were left to reset
> at each seam: adding a second, run-spanning counter for a decorative bonus with no gate felt like
> the wrong trade against just mirroring `DiamondSystem`'s actual semantics. So "💎 7" in Endless
> reads "diamonds on THIS stretch of the well", not a running total. Revisit if playtesting says
> the Endless run summary needs a real cumulative count.

### 5.10 VfxManager — New effects
**Add:**
- Burst explosion: colored particles matching chunk color, radiating from burst zone.
  Scale with chunk size (more cells = more particles).
- Streak glow: avatar tints toward streak color at ×3+; particle trail at ×8+.
- Bomb chain flash: screen flash on sympathetic detonation, intensity scales with chain mult.
- Shockwave ripple: visual wave expanding from burst/bomb center (1 cell radius, 0.2 s).
- **Bomb fuse telegraph** (added 2026-07-23): one pulsing halo per armed bomb, driven by
  `BombSystem.FuseProgress` (the 0→1 event that was previously consumed nowhere). The pulse
  frequency (2.5→12 Hz) and brightness rise, and the color ramps calm-red → hot-yellow → white,
  as detonation nears — the "flash" half of the GDD §4.8 readability contract. Rendered with the
  **`FuseGlow` shader** (additive radial glow, `Resources/Shaders/FuseGlow.shader`); a single shared
  material animates every bomb via `SpriteRenderer.color`. If the shader fails to load it degrades to
  a plain tinted halo. Halos are keyed by cell and culled by a `LastSeen` timeout, so a bomb that
  detonates, is disarmed, or is shifted by a Perfect Clear cleans up correctly.
  > **No Core change:** `FuseProgress` already existed on `BombSystem` — the telegraph is pure View.
- 🆕R4 **Diamond shine + collect sparkle.** Two separate pieces, not one:
  - **Persistent shine (BoardView, not VfxManager):** undrilled Diamond cells render with the
    **`DiamondShine` shader** (`Resources/Shaders/DiamondShine.shader`) instead of a plain tint — a
    soft pulsing glow plus a diagonal glint sweep, both phase-offset per-tile by a hash of world
    position so a whole board of diamonds doesn't twinkle in lockstep (no `MaterialPropertyBlock`
    needed, one shared material). Unlike `FuseGlow` (an additive halo layered *over* a bomb's own
    sprite), this shader *is* the diamond tile's own material — normal alpha blend, since it's the
    tile's content, not an overlay. `BoardView.ApplyMaterial()` assigns it only to `CellType.Diamond`
    cells and reverts to the plain default material the instant one is drilled. Same missing-shader
    fallback contract as `FuseGlow`.
  - **Collect sparkle (VfxManager):** a bright icy white-blue particle burst + ripple, fired from
    `SpawnDiamondSparkle()`, wired to all three collection sources — `_avatar.Drilled` (diamond
    branch), `_gravity.DiamondLiberated`, `_bombs.DiamondLiberated` — via one shared handler,
    `OnDiamondLiberated`, so however the diamond was freed it reads as a pickup, not a destruction.
- 🆕R5.15 **Enemy effects** (§6.5). Four of them, sized to match what each enemy is worth:
  - **Crawler death** — 8 small olive particles, 0.28 s. Cheap and quick: it's a bonus target.
  - **Boomer death** — 18 bright orange particles radiating evenly + its own `SpawnRipple`, 0.5 s.
    Visibly bigger and brighter than the Crawler's, because a Boomer is an amplifier. Fired from
    `BoomerDetonated`, plus a fallback in `OnEnemyKilled` for the crush case, which never detonates.
  - **Crawler wake flash** — one-shot expanding pale square when a dormant Crawler activates.
  - **Boomer standing halo** — a ~1.5 Hz pulse that lives as long as the active Boomer does,
    **reusing the `FuseGlow` material**: a Boomer is a bomb with legs, so it borrows the bomb's
    visual language. Deliberately slower and calmer than a lit fuse (2.5 → 12 Hz) — a Boomer is a
    standing opportunity, not a countdown.
  > **The events don't carry enough to draw with**, so VfxManager keeps an id → (type, cell)
  > marker cache: `EnemyActivated` is only an id, and `EnemyKilled` has the type but no position
  > (the enemy is already dead, so it can't be looked up either). Seeded from `EnemySpawned`,
  > refreshed from `GetAllAlive()` on a **0.2 s timer** — that call allocates a list, and a Crawler
  > only steps every 0.8 s, so 0.2 s costs 5 allocations/second instead of 60. Same per-entity
  > view-state pattern as the `_fuses` dictionary.

**Keep:** drill flash, collapse dust (now for PerfectClear), bomb burst, chain glow.

**Removed (2026-07-23):** the **line-target markers** — the gold edge markers `BoardView` drew on
rows with 1-3 drillable blocks left. They were vestigial v2.1 UI that guided the player toward
row-clearing; in v3 Perfect Clear is a rare emergent bonus, not a goal (rule 7), and §15.2 flagged
row-clear farming as a *problem*. The markers worked against the design, so `CreateRowMarker` /
`RefreshRowMarker` / `AnySolidAbove` and their fields were deleted from `BoardView`.

### 5.11 AudioManager — New sounds
**Add/modify:**
- Drill SFX: pitch rises with streak step (same principle as old chain pitch-rising, but
  on drill instead of line clear). `pitch = 1.0 + (streakStep - 1) × 0.05`, capped at streak 10.
- Burst SFX: new "shatter" clip via SfxSynth (Noise + Tone layered, 0.3 s). Pitch scales
  down with chunk size (bigger = deeper = more satisfying).
- Bomb SFX: unchanged clip, but play a rising arpeggio overlay on chain mult > 1.
- Perfect Clear SFX: special fanfare (Arpeggio C5-E5-G5-C6, same as old level complete but
  higher energy).
- **Bomb fuse beep** (added 2026-07-23): a high tick on `BombSystem.FuseProgress` whose cadence and
  pitch climb as the fuse burns — the audible half of the GDD §4.8 telegraph, paired with the
  VfxManager flash (§5.10). Beep indices are packed quadratically (`floor(frac² × fuseBeepSteps)`),
  so ticks start slow and accelerate toward detonation. Dedicated `_fuseSource`; per-bomb index map
  ensures each fuse only ticks forward and is cleared on detonation / `Unwire`.
- 🆕R4 **Diamond chime:** a fast 3-note arpeggio (C6-E6-G6) — the highest register of any SFX in
  the game, so a diamond always reads as a bonus pickup rather than a scoring action. One handler,
  `OnDiamondLiberated`, shared by drill / bomb blast / burst shockwave (same three sources as the
  VfxManager sparkle, §5.10).
- 🆕R5.16 **Enemy SFX** (§6.5), one shared `_enemySource`, wired in `Rewire()` since EnemySystem is
  grid-dependent. The two activation cues sit at **opposite ends of the register on purpose**: a
  Crawler chirps high (`Tone` 1500 Hz, 0.1 s — something small just started moving toward you), a
  Boomer boops low (`Tone` 150 Hz, 0.2 s — something heavy woke up and is now just standing there).
  Crawler death is a short dry crunch (`Noise`, 0.15 s), brighter than the crush clip so it reads as
  something small breaking rather than the player getting hit.
  - **Boomer detonation** is `Shatter` tuned the OPPOSITE way to the chunk burst: mostly tone
    (`noiseMix: 0.35`) at 70 Hz with a heavy low-pass (`0.06`), so it lands round and bass-heavy
    instead of crackly. That is what separates it from the bomb blast, which is pure `Noise` at a
    much brighter low-pass (0.25). Measured, not asserted: zero-crossing rate is **337/s vs the
    bomb blast's 10 884/s** — 32× darker. Same crush fallback as the VFX (§5.10), at 0.6 volume.
- Remove: void-line clear SFX trigger on every RowCollapsed (now only on PerfectClear).

### 5.12 UIScreenManager — Updated score screen
**Changes:**
- Game over / run end screen shows: Depth (primary, large), Score (secondary),
  Best Streak ("×12 Blue"), Biggest Burst ("9-cell ×3"), Best Bomb Chain ("5-chain"),
  Perfect Clears count.
- **Main menu** (`ShowMainMenu`) — the title screen shown at boot over the frozen first board;
  "Jouer" lifts the overlay to start, "Quitter" exits. Same overlay mechanic as pause/game-over.
  Auto-shown from `Start()` when `Init` was given an `onPlay` callback.
- **Gamepad UI navigation** — the scene ships no EventSystem (UI Toolkit buttons were mouse-only),
  so `EnsureEventSystem()` creates one at runtime with `InputSystemUIInputModule.AssignDefaultActions()`
  (D-pad/stick = Navigate, A = Submit, B = Cancel). Every screen focuses its primary button on open,
  and buttons draw a 2px focus border (reserved always, transparent → white on focus, so focus never
  shifts layout). Pause toggles on **Esc or the Start button**. See §16.

### 5.13 CameraShake + camera follow
**Changes:**
- The camera now **follows the avatar down the well** instead of framing the whole board. `FrameCamera`
  sets a fixed orthographic slice (`cameraViewRows`, default 16) and `GameBootstrap.Update` lerps the
  camera toward `CameraTarget()` — horizontally centered on the well, vertically tracking the avatar,
  clamped so the view never runs off the top/bottom of the board (a board shorter than the view is
  just centered).
- `CameraShake` now **owns `BasePosition`** (the un-shaken position the follow drives) and lays the
  shake offset on top of it each `LateUpdate`. The old version juggled `transform.position` directly,
  which would have fought a per-frame follow; owning the base means follow and shake never conflict.
- Serialized knobs on GameBootstrap: `cameraViewRows` (zoom, 8-40) and `cameraFollowSpeed` (smoothing).

> **Known cosmetic, deferred:** a 7-wide well in a ~2:1 view leaves large empty side-margins. Accepted
> for now; side-wall decoration is a later View task (not scheduled).

### 5.14 Tutorial showcase board  ✅ IMPLEMENTED
A hand-authored, fully deterministic **intro level** that teaches every scoring method in isolated
chambers before campaign level 1. Not part of the R-plan; added on request as a level-design pass.

- **`StrateGenerator.TutorialBoard()`** — a fixed 26×7 board built with NO RNG and NO `Build()`
  (contrast the procedural `CampaignBoard`). The campaign 1-10 ramp and its tests are untouched.
- **The anchoring frame is the whole trick.** Every chamber hangs off a continuous `ColorA` frame —
  side walls at col 0 / col 6 plus the solid divider rows — that fuses down to the bedrock floor, so
  `GravitySystem.Settle()` at load finds everything supported and drops nothing. Movable lesson pieces
  are a **different** color (`ColorB` vein, `ColorC` chunk/slab) so they never fuse into the frame and
  only move when the player acts. This is the direct countermeasure to the §5.8 "unanchored authored
  structure self-destructs at load" trap. Pinned by `TutorialBoard_SettlesAtLoad_WithoutBursting`.

  | Rows | Chamber | Method taught |
  |---|---|---|
  | 3-8 | Streak + Depth | 6-block `ColorB` vein straight down → ×6 |
  | 9-10 | 🆕R4 Diamonds | one free pickup on the straight-down path (col 3), one just off to the side (col 1) — routing tension, §6.4 |
  | 11 | ★ Perfect Clear **secret** | full-width `ColorC` slab; clear all 7 → +500 (optional, off the win path) |
  | 12 | Air | capsule on the main path |
  | 13-18 | Chunk Burst | 10-cell `ColorC` chunk on ONE support over a drop zone → shatter |
  | 19-23 | Bomb chain | drilling in arms a bomb → ×3 sympathetic chain that blasts the floor open to the win line |

  Constants expose the key rows for tests/wiring: `TutorialSpawnColumn` (3), `TutorialDiamondRow` (9),
  `TutorialPerfectClearRow` (11), `TutorialBurstChunkTop` (13), `TutorialBombRow` (21).

  > **🆕R4 (2026-07-28): the Diamonds chamber was inserted between the streak vein and the Perfect
  > Clear secret, shifting every row below it by +2.** Each diamond follows the same grounding rule
  > as everything else on this board — it rests on a solid cell of its own (the col-3 diamond on the
  > `ColorA` divider at row 10, the col-1 diamond on the Perfect Clear slab at row 11) rather than
  > empty air, or `Settle()` would drop it onto the slab below and wreck the layout before the player
  > ever sees it (the §5.2 "unanchored structure" trap applies to any solid cell, not just fused
  > chunks). `TutorialBoard_StreakChamber_YieldsSixStreak`'s drill loop had to switch its upper bound
  > from `TutorialPerfectClearRow` to the new `TutorialDiamondRow` — it used to mark the end of the
  > streak vein only because the Perfect Clear row happened to follow it directly.

- **Wiring (View).** `GameBootstrap` plays it first when `playTutorialFirst` is set (default) and
  `startLevel == 1`: `_inTutorial` swaps the board source in `LoadLevel()` and **bypasses the campaign
  win check** in `Update()` for a depth-only `CheckTutorialWin()`. Reaching the bottom shows
  `UIScreenManager.ShowTutorialComplete()`; its "Commencer l'aventure" button reuses the LevelComplete
  `onNextLevel` callback, and `AdvanceLevel()` detects `_inTutorial` to start real level 1 (score
  reset, campaign **not** advanced — `CurrentLevel` stays 1). Drain/wobble already read `CurrentLevel = 1`,
  giving the tutorial the gentle onboarding values (4 %/s, 0.8 s).

> **Two accepted rough edges (by design, flagged to the dev):**
> - **The bomb lesson costs ~1 heart.** In a 5-wide interior, blast radius 2 covers everything — there
>   is no fully safe arming spot. Thematic ("arm and retreat"); i-frames cap the whole chain at one hit.
> - **The Perfect Clear secret carries the §4.5 self-crush risk.** Clearing a load-bearing full row
>   drops the frame above; finishing on a wall column (col 0 / col 6) crushes you, finishing in the
>   interior is safe. Left as a skill check — it is a hidden bonus, not a taught path (design rule 7).

### 5.15 Endless mode flow (View)  ✅ IMPLEMENTED (R3.3)
How `EndlessManager` (§6.3) is driven from Unity. No win condition: you play until the air runs out
or the hearts do, and the run's metric is absolute depth.

**Serialized on GameBootstrap** (under `— Endless —`):

| Field | Meaning |
|---|---|
| `endlessMode` | Boot straight into endless. Only the **boot default** — see the runtime-mode note below. |
| `endlessSeed` | 0 = fresh random seed per run. A fixed value replays the same well (what R3.4 Daily Dig will set). |
| `endlessSegmentRows` | Content rows per generated segment (default 60). |

**Mode switching is runtime, not serialized.** `_endlessMode` (field) is initialised from the
checkbox but the main menu's **"Sans fin"** button flips it live, so **every mode branch reads the
field, never the serialized `endlessMode`.** `StartEndless()` also repoints the HUD
(`HUDView.SetDepthSource`) and the screens (`UIScreenManager.SetEndless`).

**Four things the mode changes:**

1. **Board source** — `LoadLevel()` branches endless → tutorial → campaign → debug map, in that order.
2. **Drain** — `_airSystem.DrainRate` comes from `_endless.DrainRate` (the depth ramp), and
   `DrainRateChanged` keeps assigning it during play. A new segment inherits the earned rate.
3. **Wobble** — `EndlessManager.WobbleDuration` (0.6 s, campaign 4+ value).
4. **Depth scoring** — `EndlessManager.DepthChanged` awards depth, and the per-board
   `DepthTracker` handler no-ops. Both handlers are guarded by `_endlessMode`, so a runtime switch
   can't double-award. This is also what makes the run summary's "70 m" absolute: `AwardDepth`
   receives the absolute row, so `ScoreSystem.MaxDepth` spans the whole descent.

> **⚠️ The segment swap is DEFERRED by one frame.** `SegmentExhausted` fires from inside the tick
> (step 3 of §7), and rebuilding the grid right there would leave the rest of `Update()` ticking a
> mix of old and new systems — the same hazard as §5.2's "a burst returns early from Tick". The
> handler only sets `_pendingSegmentAdvance`; `AdvanceEndlessSegment()` runs at the **top of the
> next Update**, before any tick.

**The seam.** `AdvanceEndlessSegment()` = `AdvanceSegment()` + `LoadLevel()`, carrying **score, air
and hearts over** (only the per-board systems are rebuilt) — it is one continuous run, not a level
transition. The cut lands in open air (board floor → spawn zone of the next board), and
`UIScreenManager.PlayFadeIn(0.4 s)` fades the screen up from black so the new segment rises out of
the dark instead of the avatar appearing to teleport from the floor to the ceiling.

> **`PlayFadeIn` is deliberately fade-IN only, and deliberately not an overlay.** The board is
> rebuilt instantly, so there is nothing to sequence a fade-out against. It never touches
> `Time.timeScale` (the run keeps going) and uses `PickingMode.Ignore` + unscaled time, so it can
> neither eat input nor freeze as a black screen if the player pauses mid-fade.

**Endless-aware screens.** No level number and no win, so: pause reads "Score … — Profondeur N m",
and both pause and game-over label the restart **"Nouvelle descente"** (`RestartLevel()` branches to
`StartEndlessRun()`, which re-seeds and resets everything).

**Mode navigation.** Three modes share one menu, so every screen that ends or suspends a run (pause,
game over, campaign complete) carries a **"Menu principal"** button → `ReturnToMainMenu()`, which
just re-shows the title screen over the frozen board.

> **`StartGame()` ("Jouer") is mode-aware, and it has to be.** At boot the campaign board is already
> built and frozen behind the title screen, so starting is just `Hide()`. But the player can now
> reach that same menu *from a finished endless run*, where the board behind is a 64-row segment of
> the wrong mode — so when `_endlessMode` is set, "Jouer" routes to `EnterCampaign()` instead, which
> unwinds every endless-specific piece of state (mode flags, pending seam, HUD depth source, screens'
> endless ref) and reloads campaign level 1 with the tutorial. Without that branch, "Jouer" would
> drop the player into the endless well with a campaign HUD.

> **Verified in play mode (2026-07-26), not just compiled:** forced a seam at row 62 of a 64-row
> board → `SegmentIndex 0→1`, `SegmentStartDepth 60`, depth continued to 70, drain ramped 5 → 6.75 %/s
> and landed on `AirSystem`, HUD read `DEPTH: 70`, fade cleaned itself up, game-over summary read
> "70 m" with a "Nouvelle descente" button, and restart reset to seg 0 / depth 0 / air 100 / new seed.
> Zero console errors.

### 5.16 Exit Glow (BoardView)  🆕 IMPLEMENTED (2026-08-12)
Not part of the R-plan; added on request as a small level-feel pass. A passive environmental cue
for the campaign/tutorial win threshold (§5.7, CampaignManager) — before this, the only signal that
a level was ending was the `ShowLevelComplete` popup firing *after* the avatar already crossed the
line (design rule 6, "depth is always forward" — reaching the exit should read as arriving
somewhere, not just triggering a menu).

- `BoardView.BuildExitGlow()` spawns one full-width `SpriteRenderer` strip per row across the last
  `exitGlowRows` rows of the board (default 4, Inspector-configurable), parented under the
  BoardView GameObject so it's torn down automatically on the next `LoadLevel()`. Brightness ramps
  from alpha 0.12 (top of the band) to 0.85 (bottom row), so the glow reads as "getting closer,"
  not a flat band appearing all at once.
- Gated by a new `showExitGlow` parameter on `BoardView.Init()`, set in `GameBootstrap` as
  `!_endlessMode && (useCampaign || _inTutorial)` — Endless has no fixed floor to signal, and the
  debug map has no win check at all (`useCampaign` gates that branch in `Update()`), so both stay
  dark.
- Rendered with the new **`ExitGlow` shader** (`Resources/Shaders/ExitGlow.shader`) — additive,
  one shared material, self-animated pulse via `_Time` (dephased per row by a hash of world Y so
  the band doesn't breathe in lockstep). Same missing-shader fallback contract as `FuseGlow` /
  `DiamondShine` (§5.10).
- No Core changes: the band is just "the last N rows," independent of the exact
  `CampaignManager.WinDepthFromFloor` math, so no new NUnit coverage — verified live in the Unity
  Editor via UnityMCP (positioned screenshots at the exit zone, before and after the fix below).

> **⚠️ First attempt was invisible — sortingOrder was backwards.** The original design put the glow
> strips at `sortingOrder = -1` (behind the tile layer), meaning to let it "shine through" gaps and
> drilled cells. That doesn't work in this renderer: `emptyColor` (the tint for a drilled/empty
> cell) is opaque (alpha 1, not transparent), so the regular tile layer — drawn at sortingOrder 0
> for *every* cell, empty or not — completely painted over anything behind it. A screenshot
> confirmed zero visible effect. Fixed by moving the glow to `sortingOrder = 1` (**above** the
> tiles); the additive blend does the actual work, washing warm light on top of whatever's already
> drawn (blocks *and* gaps alike) instead of trying to peek through it. Re-verified with a
> screenshot showing the gradient building from nothing at the top of the band to a strong gold
> wash at the final row.

### 5.17 EnemyView  🆕 IMPLEMENTED (R5.17)
One `SpriteRenderer` per living enemy, layered OVER the cell grid. Enemies are ACTORS, not
CellTypes (§6.5), so they never enter BoardView's tile array and need their own renderers.

- **`sortingOrder = 8`** — above the tiles (0) and the exit glow (1), **below the avatar (10)**.
  That ordering is load-bearing: when a Crawler walks onto the driller's cell, the driller stays
  visible at the exact moment contact damage fires (§6.5), instead of being hidden by the thing
  hitting it.
- **Dormant vs active is the whole visual language.** Dormant = `dormantAlpha` (0.4) and
  *perfectly* still, because a buried enemy genuinely cannot hurt you; waking is what makes it
  opaque and starts it moving. Active Crawler = lateral wiggle + a half-rate vertical bob (it
  scuttles rather than slides); active Boomer = a scale pulse only, since it never moves.
- Animation offsets are applied **on top of** the eased base position — the same split
  `CameraShake` uses for follow-vs-shake, so movement and idle animation never fight.
- **Positions are polled, not evented**: EnemySystem has no "moved" event, so `RefreshTargets()`
  re-reads `GetAllAlive()` every 0.1 s (8× more responsive than the 0.8 s Crawler step) and the
  per-frame lerp does the smoothing. Same allocation reasoning as VfxManager's marker cache (§5.10).
- Death VFX belong to VfxManager (§5.10); this class only removes the sprite.
- Parented under the BoardView GameObject, so it is torn down with the board on the next
  `LoadLevel()`. GameBootstrap creates it **before** `SpawnEnemiesForBoard()` so it receives
  every `EnemySpawned`.

---

## 6. Systems — NEW TO BUILD

### 6.1 StreakTracker (Core/)
Pure C# class. Tracks consecutive same-color drills **downward only** (v3.1 arcade pivot).

```
API:
  NotifyDrill(CellType drilled, DrillDirection direction)
    → if direction != Down: IGNORE entirely (streak-neutral, no reset, no increment)
    → if direction == Down AND drilled is Color and same as _currentColor: _streakCount++, fire StreakGrew(count)
    → if direction == Down AND drilled is Color and DIFFERENT from _currentColor: fire StreakBroken(oldCount), reset to 1
    → if drilled is non-color (Hard, HardCracked, AirCapsule, Diamond): IGNORE (streak-neutral, no reset)
    → Steel and Bomb are not drillable so they never reach here
  CurrentStreak (int, read-only)
  CurrentColor (CellType, read-only)
  StreakGrew event: Action<int>       — new streak count
  StreakBroken event: Action<int>     — the streak count that just ended
  Reset()                             — zeroes state for new run/level
```

**v3.1 change:** the streak now only tracks **downward drills**. Lateral and upward drills are
completely ignored (no reset, no increment). This eliminates the Streak × Burst conflict
(bursts destroy blocks laterally, but the streak only cares about vertical drilling) and makes
the streak a passive bonus of natural descent rather than an active routing system.

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

---

## 7. Game loop wiring (v3 — target state)

### GameBootstrap.Awake() — subscriptions

```csharp
// ── Streak tracking + drill air restore ───────────────────────
_avatar.Drilled += (pos, oldType, direction) =>              // 🔄v3.1: direction added
{
    _streakTracker.NotifyDrill(oldType, direction);           // 🔄v3.1: vertical-only streak
    _scoreSystem.AwardDrill(_streakTracker.CurrentStreak, _streakTracker.CurrentColor);
    _airSystem.RestoreDrill();                                // 🆕v3.1: +0.5% air per drill
    if (oldType == CellType.AirCapsule) _airSystem.RestoreCapsule();
    if (oldType == CellType.Diamond) _diamondSystem.NotifyCollected(pos);  // 🆕R4
    _bombSystem.NotifyDrilled(pos);
    _enemySystem.NotifyAdjacentDrill(pos);                                 // ✅R5.9 activation
};

// ── Chunk gravity + burst ──────────────────────────────────────
_gravity.ChunkLanded += chunk => _bombSystem.NotifyChunkLanded(chunk);

_gravity.ChunkBurst += (cells, fallDistance, color) =>   // 🆕 (color: see §5.2 deviation 3)
{
    _scoreSystem.AwardBurst(cells.Count, fallDistance);
    _airSystem.RestoreBurst();
};

// Shockwave side effects are events, NOT handled inside GravitySystem — Core systems
// must not reference each other, so the wiring lives here.
_gravity.AirCapsuleLiberated += _   => _airSystem.RestoreCapsule();
_gravity.BombArmedByBurst    += pos => _bombSystem.ArmBombAt(pos);
_gravity.AvatarHitByBurst    += OnAvatarCrushed;         // 🆕 (no arg)

// ── Bombs ──────────────────────────────────────────────────────
_bombSystem.BombScored += (destroyed, chainMult) =>   // 🔄
{
    _scoreSystem.AwardBomb(destroyed, chainMult);
    if (chainMult > 1) _airSystem.RestoreBombChain(chainMult);
};
_bombSystem.AirCapsuleLiberated += _ => _airSystem.RestoreCapsule();  // 🆕
_bombSystem.AvatarHitByBlast += _ => OnAvatarCrushed();

// ── Perfect Clear (formerly void-line) ─────────────────────────
_collapse.PerfectClear += (row, cascadeStep) =>       // 🔄
{
    _scoreSystem.AwardPerfectClear(cascadeStep);
    _airSystem.RestorePerfectClear();
};

// ── Depth ──────────────────────────────────────────────────────
// Campaign/tutorial: the per-board tracker scores depth. Endless must NOT — its tracker
// resets at every segment seam, so EndlessManager scores the ABSOLUTE row instead (§5.15).
// Both handlers are guarded, because the menu can switch modes at runtime.
_depthTracker.NewDepthReached += row =>                // 🆕
{
    if (!_endlessMode) _scoreSystem.AwardDepth(row);   // row index — see §5.6 deviation
};

// ── Endless (🆕R3, §5.15 / §6.3) ───────────────────────────────
_endless.DepthChanged     += depth => { if (_endlessMode) _scoreSystem.AwardDepth(depth); };
_endless.DrainRateChanged += rate  => { if (_endlessMode) _airSystem.DrainRate = rate; };
_endless.SegmentExhausted += _     => _pendingSegmentAdvance = true;   // deferred — see tick step 0

// ── Diamonds (🆕R4) ───────────────────────────────────────────
// Diamond drilled — handled in the Drilled handler above (CellType.Diamond check)
// Diamond liberated by burst shockwave:
_gravity.DiamondLiberated += pos => _diamondSystem.NotifyCollected(pos);
// Diamond liberated by bomb blast:
_bombSystem.DiamondLiberated += pos => _diamondSystem.NotifyCollected(pos);
// Campaign win gate: CampaignManager checks _diamondSystem.IsComplete

// ── Enemies (✅R5.12 — this is the SHIPPED wiring, not a sketch) ───────────────
// ChunkLanded carries a Chunk, not a cell list — enemies want the plain footprint.
_gravity.ChunkLanded += chunk =>
{
    _bombSystem.NotifyChunkLanded(chunk);
    _enemySystem.NotifyChunkLanded(new List<GridPos>(chunk.Cells));
};
_gravity.ChunkBurst += (cells, fallDist, color) =>
{
    /* ...score/air/shake... */
    // LastShockwaveCells is ONLY valid during this event (§ GravitySystem fills it just before
    // firing, R5.12). Read it here — never poll it.
    _enemySystem.NotifyBurst(cells, new List<GridPos>(_gravity.LastShockwaveCells), fallDist);
};
_bombSystem.BlastResolved += (blastCells, chainMult) => _enemySystem.NotifyBombBlast(blastCells, chainMult);

// The bonus rides ON EnemyKilled (fall_bonus / chain_mult / 1) — no KillMethod switch, no
// LastFallBonus/LastChainMult lookups. See the R5.12 deviation note for why the draft's version
// couldn't work.
_enemySystem.EnemyKilled += (id, type, method, bonus) => _scoreSystem.AwardEnemyKill(type, bonus);
_enemySystem.AvatarHitByEnemy += _ => OnAvatarCrushed();

// ✅R5.8/R5.12 Boomer detonation: EnemySystem ALREADY applied the radius-1 blast to the grid (and
// any chain-reaction kills) before this fires, and reports its own block count — GameBootstrap
// must NOT re-apply the blast, it only banks what Core can't reach (score, air, shake).
_enemySystem.AirCapsuleLiberated += _ => _airSystem.RestoreCapsule();
_enemySystem.DiamondLiberated    += pos => _diamondSystem.NotifyCollected(pos);
_enemySystem.BoomerDetonated += (pos, parentBonus, blocksDestroyed) =>
{
    _scoreSystem.AwardBoomerBlast(blocksDestroyed, parentBonus);
    _cameraShake.Shake(0.25f, 0.28f);
};

// Drill handler (above) also carries: _enemySystem.NotifyAdjacentDrill(pos);
```

> **Subscription order matters in two places** (multicast delegates fire in subscription order):
> - `AudioManager.OnDrilled` reads `StreakTracker.CurrentStreak` for its rising pitch. GameBootstrap
>   subscribes to `Drilled` (and calls `NotifyDrill`) in `LoadLevel()` *before* `_audio.Rewire()`,
>   so the audio sees the fresh value. Don't reorder those two.
> - HUD popups deliberately do **not** subscribe to ChunkBurst/BombScored/PerfectClear — they read
>   `ScoreSystem.OnScore`, which carries Points + Source + Detail and is order-independent (§5.9).

### GameBootstrap.Update() — tick order

```csharp
// 0. 🆕R3 Deferred endless segment swap, BEFORE any tick (§5.15 — never mid-frame)
if (_pendingSegmentAdvance) { _pendingSegmentAdvance = false; AdvanceEndlessSegment(); }

float dt = Time.deltaTime;

// 1. Avatar gravity
_avatar.Tick(dt);

// 2. Depth tracking
_depthTracker.NotifyPosition(_avatar.Position);

// 3. Win / progression check — one branch per mode
if (_endlessMode)                                    // 🆕R3 no win: depth, drain ramp, seam
    _endless.NotifyAvatarPosition(_avatar.Position, _grid.Height);
else if (_inTutorial)                                // showcase board: depth-only tutorial win
    CheckTutorialWin();
else if (useCampaign)                                // 🔄v3.1: depth + score gate (was depth + diamond gate)
    _campaign.NotifyAvatarPosition(_avatar.Position, _grid.Height, _scoreSystem.Score);

// 4. Bomb fuses
_bombSystem.Tick(dt, _avatar.Position);

// 5. Void-row detection (Perfect Clear)
_collapse.Resolve(_avatar.Position);

// 6. Chunk gravity + burst detection
_gravity.Tick(dt, _avatar.Position);

// 7. Chain close timer
_chainTracker.Tick(dt);

// 8. Health i-frames
_healthSystem.Tick(dt);

// 9. Air drain  (gated by the `disableAir` debug toggle — see §14)
if (!disableAir)
    _airSystem.Tick(dt);

// 10. ✅R5.12 Enemy movement + avatar collision (grid comes from the constructor since R5.8)
_enemySystem.Tick(dt, _avatar.Position);

// 10b. ✅R5.12 Endless only: wake enemies the camera has scrolled into view (§6.5). Campaign
//      boards don't need it — the drill and blast triggers already cover activation there.
if (_endlessMode)
    ActivateEnemiesInView();
```

> **Debug/testing toggles (serialized on GameBootstrap, §14).** Two Inspector checkboxes,
> both off by default, both live-togglable in play mode:
> - `disableAir` — skips step 9 entirely, so air never drains and `AirDepleted` can't fire.
> - `invincible` — early-returns in `OnAvatarCrushed()`, so hearts never drop (and no shake / crush
>   SFX / teleport-to-spawn). `HealthDepleted` can't fire.
>
> Both are pure View flags — Core (`AirSystem`, `HealthSystem`) is untouched, and no test depends
> on them.

---

## 8. Scoring summary

| Action | Points | Formula |
|---|---|---|
| Drill | 10 × streak | 🔄v3.1: streak tracks **downward drills only**; lateral/up are neutral |
| Chunk Burst | cells × 25 × fall_bonus | fall_bonus = floor(fall_distance / 2) |
| Bomb | blocks × 25 × chain_mult | chain_mult = # bombs in sympathetic chain |
| Depth | 50 | Per new deepest row |
| Perfect Clear | 500 **flat** | Rare bonus. The cascade multiplier was removed — see §15.2 |
| Diamond | 150 | 🔄v3.1: bonus in ALL modes (campaign gate removed) |
| Enemy Kill | 100 (Crawler) / 150 (Boomer) | ✅R5.11: 2 types only. Kill-by-burst × fall_bonus, kill-by-bomb × chain_mult |
| Boomer explosion | blocks × 25 × parent_bonus | ✅R5.11: Boomer death blast scores like a bomb. Parent bonus carries through |

**Air restore** (revised in §15.1 — the draft values were ~20× oversupplied):

| Source | Amount |
|---|---|
| **Every drill** | **+0.5%** 🆕v3.1 — the core arcade survival loop: drill to breathe |
| Air capsule (drill or liberate) | +6% |
| Chunk Burst | +0.5% |
| Bomb chain (per bomb) | +1% |
| Perfect Clear | +6% |

**Campaign drain:** 4 %/s on levels 1-3, 7 %/s from level 4 (`CampaignManager.DrainRateForLevel`).

---

## 9. Campaign — 10 levels

Win = reach the bottom + **score minimum** (🔄v3.1 — replaces diamond gate).

Row counts and drain were revised in §15.1 — the v2.1 depths were far too short for the air clock
to function once the line gate was removed.

| Level | Rows | New mechanic | Drain | Wobble | Diamonds | Score min | Enemies |
|---|---|---|---|---|---|---|---|
| 1 | 24 | Movement + drilling | 4%/s | 0.8 s | 0 | 0 | — |
| 2 | 30 | Color Streak (tutorial vein) | 4%/s | 0.8 s | 0 | 0 | — |
| 3 | 32 | Chunks + Chunk Burst (tutorial setup) | 4%/s | 0.8 s | 0 | 0 | — |
| 4 | 40 | Air capsules + drain + **Diamonds** | 7%/s | 0.6 s | 2 | 500 | — |
| 5 | 44 | Hard blocks | 7%/s | 0.6 s | 3 | 500 | — |
| 6 | 50 | Bombs (single) + **Crawler** (🔄R5) | 7%/s | 0.6 s | 3 | 1,500 | 3 Crawlers |
| 7 | 54 | Bomb chains + **Boomer** (🆕v3.1) | 7%/s | 0.6 s | 4 | 1,500 | 3 Crawlers + 2 Boomers |
| 8 | 60 | Steel blocks | 7%/s | 0.6 s | 4 | 3,000 | 4 Crawlers + 3 Boomers |
| 9 | 68 | Full mix | 7%/s | 0.6 s | 5 | 3,000 | 5 Crawlers + 4 Boomers |
| 10 | 76 | Dense final board | 7%/s | 0.6 s | 5 | 5,000 | 6 Crawlers + 5 Boomers |

> **🔄v3.1 Score gate (replaces diamond gate).** The diamond gate was removed because it
> conflicted with design rule 6 (depth is always forward) — players could miss a diamond and
> have no way to backtrack, making the level impossible. The score gate forces engagement with
> the scoring mechanics (bursts, bombs, enemy kills) without requiring lateral routing.
> Levels 1-3 have no gate (tutorial). A pure tunnel-bot at 50 pts/row earns ~2,000 on a 40-row
> level, so the 500-pt gate at level 4 is trivially passed by descending. The 3,000+ gates on
> levels 8-9 require at least a few bursts or bomb chains — which happen naturally for a player
> who is engaging with the game.
>
> **Diamonds are still placed** (same counts, same placement logic) — they're just not required.
> Each one is +150 pts toward the score gate, making them a helpful bonus on the path.
>
> **🔄v3.1 Enemies simplified.** Digger and Tank removed. Crawlers appear from level 6 (alongside
> bombs, since the player already understands burst kills). Boomers appear from level 7 (the
> player has seen bomb chains and can appreciate the amplification effect).

> The burst tutorial band sits **directly on the bedrock floor** (`BurstTutorialTop()`), not below
> the spawn zone. An authored solid row placed mid-board is not anchored: with porous terrain
> beneath it the whole band settles, falls 2+ rows and bursts on load, destroying the setup before
> the player sees it. Only the bedrock never moves.

---

## 10. Refactoring plan — Step by step

> **Rule: one step at a time.** Each step below is a single session's work.
> Deliver, test, verify, then move to the next.

### R1 — Core redesign  ✅ COMPLETE

| Step | Task | Tests |
|---|---|---|
| R1.1 ✅ | GridModel: parameterize Width (default 7); update FromStringMap | Was already compliant; added width-7 coverage |
| R1.2 ✅ | StreakTracker: new system | 11 tests |
| R1.3 ✅ | DepthTracker: new system | 5 tests |
| R1.4 ✅ | GravitySystem: fall distance + ChunkBurst + shockwave | 12 tests (`ChunkBurstTests`) |
| R1.5 ✅ | ScoreSystem: full rewrite | 20 tests |
| R1.6 ✅ | BombSystem: capsule liberation + chain mult + scoring | 5 new + 1 updated |
| R1.7 ✅ | CollapseSystem: PerfectClear rename | Full rename, 2 new event tests |
| R1.8 ✅ | AirSystem: RestoreBurst, RestoreBombChain, RestorePerfectClear | 6 new, 3 renamed |
| R1.9 ✅ | CampaignManager: remove line gate, depth-only win | 6 gate tests removed, 4 depth tests added |

### R2 — View layer  ✅ COMPLETE

| Step | Task |
|---|---|
| R2.1 ✅ | StrateGenerator: 7-wide, v3 rates, streak + burst tutorials |
| R2.2 ✅ | GameBootstrap: rewire all events per §7 |
| R2.3 ✅ | HUDView: streak counter, depth display, burst/bomb/perfect popups |
| R2.4 ✅ | VfxManager: burst explosion, streak glow, bomb chain flash, shockwave ripple |
| R2.5 ✅ | AudioManager: streak pitch, burst shatter, bomb reward SFX |
| R2.6 ✅ | UIScreenManager: run summary (depth headline + 4 highlight stats) |

### R2.7 — Playtest harness  ✅ COMPLETE
`tools/playtest` rewired to full v3 (DepthTracker, burst/bomb events, points-by-source
attribution). The stale `PorousBoard` variant was deleted — it duplicated the *old* private
strate tables and no longer matched `CampaignStrates`. **Findings: see §15 — they are blocking.**

### R2.8 — Balance pass  ✅ COMPLETE

| Step | Task | Status |
|---|---|---|
| R2.8a | Color veins so the streak system has material (§15.3) | ✅ done |
| R2.8b | Nerf Perfect Clear — cascade multiplier removed (§15.2) | ✅ done |
| R2.8c | Campaign pacing: depths ×3, drain 4/7 %/s, restore economy corrected (§15.1) | ✅ done — re-measured post-settle, pacing meets rules 5/6; lvl 7 bomb-wall fixed |
| R2.8d | **Burst cascade** — settle-on-load kills the pre-play demolition | ✅ done (load); in-play cascade left as designed |
| R2.8e | Bomb-hunting bot, then judge the bomb economy (§15.4) | ✅ done — bombs pay out (chains ×4, 22–33 % of points when hunted) |
| R2.8f | Re-run the wobble comparison once pacing is fixed (§15.5) | ✅ done — closed: not measurable by non-dodging bots, defaults kept |

> **R2.8d — decided (2026-07-21): silent settle at board load.** `GravitySystem.Settle()` resolves
> the freshly generated board to a stable rest state in `LoadLevel()` **before** play — no telegraph,
> no burst, no shockwave, no crush. This kills the reported symptom: the board no longer demolishes
> itself before the player moves. Pinned by `Settle_DropsUnsupportedChunk_WithoutBursting`,
> `Settle_LeavesBoardAtRest_NoWobbleOrBurstOnNextTick`, `Settle_StacksFallingChunks_WithoutOverlap`.
>
> **Deliberately left as-is:** the *in-play* shockwave cascade (a player-caused burst still erases
> neighbor blocks and can chain — §15.1's 78-bursts-per-run mechanism). The designer chose the
> settle-only route; the shockwave keeps its block-erasing bite during play. If late-game runs still
> over-supply air once R2.8c pacing is finished, the next lever to consider is the shockwave scope or
> `BurstFallThreshold` — but re-measure with the playtest harness first, since the board no longer
> self-destructs at load and the old §15.1 numbers were taken on one that did.
>
> **Separately, §5.2 dev 5 is resolved:** the shockwave ring no longer damages the avatar (only the
> chunk footprint crushes), so a correct dodge is never punished.

### R3 — Endless mode  🟡 IN PROGRESS (R3.1-R3.4 ✅ — only the leaderboard left)

| Step | Task |
|---|---|
| R3.1 ✅ | StrateGenerator: progressive depth ramp. `EndlessSegment(seed, width, rows, startDepth)` resumes the ramp across segments (§6.3); `emptyRate=0` bug fixed (§15.6); **steady-state rates measured against the campaign and retuned — §15.7.** |
| R3.2 ✅ | Depth-based drain ramp — `EndlessManager.DrainRateForDepth` (GDD §7 curve, capped 10 %/s). Lives beside `CampaignManager.DrainRateForLevel`, NOT inside AirSystem: AirSystem stays a dumb clock whose `DrainRate` the mode owner sets. |
| R3.3 ✅ | Endless game flow in GameBootstrap (no win, play until death): deferred segment swap + fade, drain ramp applied live, HUD/pause/game-over speak absolute depth, "Sans fin" on the main menu. **See §5.15.** |
| R3.4 ✅ | Daily Dig: one well per calendar day, same for every player. `DailyDig.SeedFor(date)` (Core, §6.6) + "Défi du jour" on the main menu + local best-of-day in PlayerPrefs (`DailyDigStore`). |
| R3.5 | Leaderboard API (ASP.NET Core / PostgreSQL, separate project) |

> **R3.1-R3.4 delivered 2026-07-26.** `EndlessManager` (§6.3) owns absolute depth, the drain ramp and
> segment chaining; the View flow is wired and play-mode verified (§5.15); the steady-state terrain
> was measured against the campaign and retuned (§15.7); the Daily Dig ships as a date→seed function
> (§6.6). **Endless and the Daily Dig are playable** from the main menu ("Sans fin" / "Défi du jour",
> with "Menu principal" to switch back). Only **R3.5 (leaderboard)** is left, and it is a separate
> ASP.NET Core project rather than Unity work.

### R4 — Diamonds  ✅ COMPLETE (2026-07-28)

| Step | Task | Tests |
|---|---|---|
| R4.1 ✅ | CellType: add Diamond (value 9, map char `D`). IsDrillable true, CanFuse false, DrillResult Empty (automatic). StreakTracker neutral (automatic via CanFuse). | 6 new (BlockTypesTests + StreakTrackerTests) |
| R4.2 ✅ | DiamondSystem: new pure C# system (§6.4). Init, NotifyCollected, IsComplete, events. | 7 new (`DiamondSystemTests`) |
| R4.3 ✅ | GravitySystem: shockwave hitting Diamond → DiamondLiberated event (same pattern as AirCapsuleLiberated). | 1 new (`Shockwave_LiberatesDiamond`) |
| R4.4 ✅ | BombSystem: blast hitting Diamond → DiamondLiberated event (same pattern as AirCapsuleLiberated). | 2 new (liberated + no-diamond-no-fire) |
| R4.5 ✅ | ScoreSystem: AwardDiamond() → +150 pts flat. `ScoreSource.Diamond` added. | 3 new + `Score_AccumulatesAcrossEverySource` updated |
| R4.6 ✅ | CampaignManager: `NotifyAvatarPosition` gained `bool diamondsComplete = true`. Win = depth + gate. Levels with 0 diamonds: default keeps the gate open. | 3 new (gate blocks / opens / default-true) |
| R4.7 ✅ | StrateGenerator: diamond placement (§5.8) — exact per-level count in campaign (`PlaceDiamonds`), flat 2% per-cell roll in endless (`DiamondRate`). | 9 new (count-matches-table, placement bounds, endless rate) |
| R4.8 ✅ | GameBootstrap: wired all three collection sources + the diamond gate check + `DiamondSystem.Init()` per `LoadLevel()` (§6.4 deviations). | Integration — 258/258 PlayMode tests + live play-mode verification (score +150, HUD updated) |
| R4.9 ✅ | HUDView: diamond counter "💎 2/5" (campaign) / "💎 7" (Endless, per-segment — §5.9 deviation). | Visual check — live screenshot in Unity Play Mode |
| R4.10 ✅ | VfxManager + AudioManager: diamond collect sparkle + chime SFX; **BoardView + new `DiamondShine` shader** for a persistent shimmer on undrilled diamonds (added on request, not originally scoped). | Visual/audio check — live screenshot showing the shader glint + verified score/DiamondSystem delta on collection |

> **R4 delivered 2026-07-28, all 10 steps in one session.** `DiamondSystem` (§6.4) owns collection
> tracking and the campaign gate; placement is deterministic-exact in campaign and probabilistic in
> endless (§5.8); the View layer (HUD counter, VFX sparkle, audio chime, and a shader-driven tile
> shine that wasn't in the original plan) is fully wired and confirmed live in the Unity Editor via
> UnityMCP — not just compiled. The hand-authored tutorial showcase (§5.14) also got a Diamonds
> chamber demonstrating the mechanic before campaign level 1. Test suite: 232 → **260**.
>
> **One real bug found along the way:** `BoardView.VisualFor()` had no case for `CellType.Diamond`
> and silently rendered it as an empty cell — nobody had touched board rendering until R4.10 needed
> the shine material, so it went unnoticed through R4.1-R4.9. Fixed alongside the shader (§5.10).

### R5 — Enemies + Arcade polish  ✅ COMPLETE (2026-09-01)

> **Prerequisite:** R4 complete ✅. **v3.1 arcade pivot** simplifies R5 significantly:
> 2 enemy types (Crawler + Boomer) instead of 3, no diamond gate rework needed (diamonds are
> already optional in scoring — just remove the CampaignManager gate check), plus three
> arcade-feel changes (streak vertical-only, drill air restore, bomb fuse 1.5s, score gate).

| Step | Task | Tests |
|---|---|---|
| R5.1 ✅ | 🔄v3.1 **StreakTracker**: add `DrillDirection` parameter to `NotifyDrill`. Only `Down` builds/resets the streak; all other directions are ignored (streak-neutral). Update existing StreakTracker tests. | Update 11 existing + 3 new (lateral-neutral, up-neutral, down-builds) — done, 263/263 passing |
| R5.2 ✅ | 🔄v3.1 **AirSystem**: add `RestoreDrill()` → +0.5% air. Called on every drill (in GameBootstrap's `Drilled` handler). | 2 new (restore amount, clamp) — done, 3 new, 266/266 passing |
| R5.3 ✅ | 🔄v3.1 **BombSystem**: reduce `FuseDuration` from 2.5 s to **1.5 s**. | Update existing fuse timing tests — done, no test edits needed |
| R5.4 ✅ | 🔄v3.1 **CampaignManager**: replace diamond gate with **score gate**. New `ScoreMinimumForLevel(int level)` method. Win = depth reached + `ScoreSystem.Score >= minimum`. Remove `diamondsComplete` parameter from `NotifyAvatarPosition`. | 4 new (score gate blocks/passes, level 1-3 no gate, exact thresholds) — done, 7 new, 270/270 passing |
| R5.5 ✅ | EnemyType enum (Crawler, Boomer) + EnemyEntity struct (§6.5). | Data structure tests — none, pure data (per dev instruction); compile verified, 270/270 still passing |
| R5.6 ✅ | EnemySystem: core lifecycle — SpawnEnemy, ActivateEnemy, GetEnemyAt, GetAllAlive, kill logic. No movement yet. | New suite: spawn, activate, kill by crush/burst/bomb, dormant vs active — done, 19 new, 289/289 passing |
| R5.7 ✅ | EnemySystem: Crawler movement — lateral bounce in row, 0.8s interval. | Movement tests: bounce off wall, bounce off solid, interval timing — done, 5 new, 294/294 passing |
| R5.8 ✅ | EnemySystem: Boomer death explosion — on kill, blast radius 1 (cardinal), destroy adjacent blocks, fire BoomerDetonated event. Chain cap at 3. | Boomer blast tests, chain cap test, no-bomb-arming test — done, 9 new, 303/303 passing |
| R5.9 ✅ | EnemySystem: activation rules — adjacent drill, burst shockwave reveal, camera scroll (Endless). | Activation tests — done, 6 new, 309/309 passing |
| R5.10 ✅ | EnemySystem: avatar collision — active Crawler on avatar cell = damage. Boomer = no contact damage. Dormant = no damage. | Collision tests + i-frame interaction — done, 5 new, 314/314 passing |
| R5.11 ✅ | ScoreSystem: `AwardEnemyKill(EnemyType, int bonus)`. Crawler=100, Boomer=150. Bonus = fall_bonus or chain_mult from parent kill method. Also `AwardBoomerBlast`. | Score tests for both types + bonus multipliers — done, 10 new, 324/324 passing |
| R5.12 ✅ | GameBootstrap: wire all v3.1 changes — streak direction, drill air restore, bomb fuse, score gate, enemy events, Boomer detonation → BombSystem-like blast scoring. | Integration — compile check + play-mode smoke test; 324/324 still passing. **Enemies don't spawn yet — R5.13 owns placement.** |
| R5.13 ✅ | StrateGenerator: enemy placement — Crawlers from level 6, Boomers from level 7 (§9 table). Both always below spawn. | Generator tests — done, 9 new, 333/333 passing. Endless placement deferred to R6. |
| R5.14 ✅ | HUDView: enemy kill popup "+100 CRAWLER!" / "+150 BOOMER!". Score gate display (if needed). | Visual check — done, verified live in play mode (4 screenshots). Score-gate display not added: not requested, and §9's gates are cleared by descending. |
| R5.15 ✅ | VfxManager: Crawler death crunch, Boomer explosion (distinct from bomb — brighter, different color). | Visual check — done, all four effects confirmed on one screenshot |
| R5.16 ✅ | AudioManager: Crawler activation chirp, kill crunch. Boomer detonation boom (bass-heavy). | Audio check — done, verified by measuring the generated clips (see the R5.16 note) |
| R5.17 ✅ | EnemyView: enemy sprites on the grid — Crawler: dormant (dim insect), active (animated shuffle). Boomer: dormant (dim orb), active (pulsing glow). Separate from cell grid. | Visual check — done, all four states confirmed on screen and by transform sampling |

> **R5.1 delivered 2026-09-01.** No direction enum existed anywhere in Core — `AvatarModel.TryDrill`
> only ever took raw `(dx, dy)`. Added `DrillDirection { Up, Down, Left, Right }` (`Core/DrillDirection.cs`)
> and had `TryDrill` derive it from `(dx, dy)` (`dy>0` → Down, matching `GridPos.Below`'s `Y+1`).
> `AvatarModel.Drilled` grew a third parameter (`Action<GridPos, CellType, DrillDirection>`), which
> rippled to every subscriber: `GameBootstrap`, `AudioManager.OnDrilled`, `VfxManager.OnDrilled`, and
> the `tools/playtest` harness (`Avatar.Drilled` lambda). `StreakTracker.NotifyDrill` now early-returns
> on any non-`Down` direction before the existing streak-neutral (`CanFuse()`) check — a lateral or
> upward drill never reaches the color comparison at all. All pre-existing `NotifyDrill` call sites
> (11 `StreakTrackerTests`, the tutorial-showcase streak test, `GameBootstrap`, the playtest harness)
> now pass `DrillDirection.Down` to keep their prior behavior. 263/263 PlayMode tests pass.

> **R5.2 delivered 2026-09-01.** `AirSystem.DrillRestoreAmount = 0.5f`, matching the unit convention
> every other restore constant already uses (`BurstRestoreAmount = 0.5f`, `CapsuleRestoreAmount = 6f`,
> etc. are percentage-POINTS added directly by `Restore(amount)`, not fractions of `MaxAir`). Wired
> into `GameBootstrap`'s `Drilled` handler right after `NotifyDrill` and before `AwardDrill` — matching
> the §7 pseudocode order exactly. `RestoreEconomy_IsSmallRelativeToDrain` gained a third assertion
> (`DrillRestoreAmount <= BurstRestoreAmount`) since drill restore ties Burst as the smallest source —
> the survival floor, not a shortcut. 266/266 PlayMode tests pass.

> **R5.3 delivered 2026-09-01.** `BombSystem.FuseDuration` 2.5f → 1.5f. No test edits needed: every
> fuse-timing test already ticks `BombSystem.FuseDuration + 0.01f` rather than a hardcoded literal
> (2.5 was never inlined), so all 16 detonation tests plus the tutorial-board bomb-chain test adapted
> automatically. Verified the fuse telegraph (§5.10 `VfxManager`, §5.11 `AudioManager`) stays correct
> at the new tempo: both `OnFuseProgress` handlers key off the normalized `frac` (0→1) that
> `BombSystem` already computes as `1f - (remaining / FuseDuration)` — the beep-index quadratic
> (`floor(frac² × fuseBeepSteps)`) and the glow halo never reference the duration directly, so they
> compress into the shorter fuse rather than breaking. Fixed one stale doc reference
> (`hollow-lines-gdd-v3.md` §11 pseudocode still said "2.5 s fuse"). 266/266 PlayMode tests pass
> (no new tests — a constant-only change).

> **R5.4 delivered 2026-09-01.** `NotifyAvatarPosition`'s third parameter changed from
> `bool diamondsComplete = true` to `int currentScore = int.MaxValue` — the `int.MaxValue` default
> always clears `ScoreMinimumForLevel`, preserving the exact backward-compat behavior the old
> `true` default gave (tutorial showcase and any caller that doesn't care about the gate keeps
> depth as the only requirement). `ScoreMinimumForLevel` is a new static method (no state, easy to
> unit test directly). `GameBootstrap` now passes `_scoreSystem.Score`; `_diamondSystem` is untouched
> otherwise — diamonds are still placed and collected (§6.4), they just feed the score gate as
> +150 pts each instead of gating directly. The 3 old diamond-gate tests were replaced with 7 score-gate
> tests (block/pass/catch-up/tutorial-no-gate/depth-still-required/default-bypass/table-values) —
> net +4. Also fixed a stale doc comment in `StrateGenerator.DiamondCountForLevel` that still
> described diamonds as gating. 270/270 PlayMode tests pass.

> **R5.5 delivered 2026-09-01.** Three new files, no functional tests (pure data structures, per
> dev instruction) — compile-checked and the full 270/270 suite re-run to confirm no regression.
> `EnemyType.cs` (Crawler, Boomer) and `KillMethod.cs` (Crush, Burst, Bomb — didn't exist yet)
> are plain enums. `EnemyEntity.cs` is a separate file, not folded into `EnemyType.cs` — it's a
> distinct data structure, matching the one-type-per-file convention `GridPos.cs`/`DrillDirection.cs`
> already use. Implemented as an immutable `readonly struct` with `With*`/`Activated()`/`Killed()`
> methods that return a new value, the same pattern `GridPos.Offset()` uses (no `with`-expression:
> plain structs don't support that syntax, only C# 9+ records/record structs do, and this project's
> other value types are plain structs) — `EnemySystem` (R5.6+) will own the authoritative copy and
> replace it wholesale on each state change. `Health` is always 1 for both current types (both die
> in one hit) but is carried on the struct so a future tougher enemy doesn't need a shape change.
> Drive-by fix: `CellType.cs`'s doc comment for `Bomb` still said "2.5 s fuse" (stale since R5.3).

> **R5.6 delivered 2026-09-01.** `EnemySystem.cs` — spawn, activate, query, kill-by-crush/burst/bomb,
> `Reset()`. Two deliberate deviations from the original §6.5 API sketch:
> - **`NotifyBombBlast` takes `chainMult` as an explicit parameter** (`NotifyBombBlast(List<GridPos>
>   blastCells, int chainMult)`), not the bare `NotifyBombBlast(List<GridPos>)` the doc drafted. The
>   §7 pseudocode's alternative — `EnemyKilled` fires, then GameBootstrap looks up
>   `BombSystem.LastChainMult` to compute the Boomer's parent bonus — would need a "last chain mult"
>   field that doesn't exist on `BombSystem` today and adds a second read after the event instead of
>   carrying the value on the call that already has it. Since the caller (whoever resolves the blast)
>   already knows the chain multiplier at the point it calls in, passing it straight through is
>   simpler and matches how `NotifyBurst` already receives `fallDist` directly.
> - **`SpawnEnemy` returns the new `int` id** in addition to firing `EnemySpawned` with it — the event
>   alone forces every caller (including every test) to capture it via a closure just to reference the
>   enemy again. Non-breaking, same id either way.
> `KillMethod.Crush` never fires `BoomerDetonated` — only Burst and Bomb kills amplify (§6.5); a
> Boomer crushed by a landing chunk just dies like a Crawler would. Dormant enemies die exactly like
> active ones to crush/burst/bomb (only avatar CONTACT, R5.10, will check `IsActive`) — the dev's
> initial test list had this backwards and self-corrected mid-prompt; implemented per the correction.
> 19 new tests, 289/289 PlayMode tests pass.

> **R5.7 delivered 2026-09-01.** `EnemySystem.Tick(dt, grid, avatarPos)` — only active Crawlers move;
> dormant ones and every Boomer are untouched. Movement state (Direction, Timer) lives in a private
> `CrawlerState` class keyed by id, NOT on `EnemyEntity` — the public struct has no notion of "which
> way it's walking" (§6.5 didn't ask for one), so adding it there would leak Crawler-only concerns
> onto every enemy type. Default heading is right (`CrawlerMoveInterval = 0.8f`, a public const so
> tests and the future GameBootstrap wiring share one source of truth). Bounce logic: blocked ahead
> (`col < 0`, `col >= grid.Width`, or `grid.IsSolid`) flips `Direction` and retries once in the same
> interval — a Crawler pinned between two walls just stays put rather than oscillating in place every
> tick. `state.Timer` is a `while` loop (not `if`), so a dt larger than 0.8s steps multiple cells in
> one `Tick` call, matching how `AvatarModel.Tick`'s fall clock handles a big frame. Dead Crawlers'
> `CrawlerState` entries are removed in `KillInZone` (mirrors `AudioManager`'s fuse-index cleanup on
> detonation) so the dictionary doesn't accumulate stale entries over a long run. 5 new tests,
> 294/294 PlayMode tests pass.

> **R5.8 delivered 2026-09-01 — biggest deviation of the R5 pass: `EnemySystem` now takes
> `GridModel` via its constructor.** The dev's spec describes the Boomer blast as EnemySystem's OWN
> behavior ("quand un Boomer est tué, il explose" — destroys blocks, liberates capsules/diamonds,
> chain-kills other enemies), not something GameBootstrap assembles from a bare `BoomerDetonated`
> event. That requires read/write grid access at kill-time, and the established pattern for a Core
> system that needs one (`GravitySystem`, `BombSystem`) is constructor injection, not a per-call
> parameter — so `EnemySystem(GridModel grid)` replaces the parameterless constructor, and `Tick`
> dropped its now-redundant `GridModel grid` parameter (uses the stored field instead; every R5.6/
> R5.7 test updated — 24 call sites, `new GridModel(10, 10)` for the ones that don't otherwise need
> a real board). §6.5 and §7 above are resynced to the new constructor/Tick signatures.
>
> Every kill (zone-based or chain-cascaded) now funnels through one `KillOne(enemy, method,
> boomerBonus, chainDepth)` — the single choke point that decides whether a Boomer detonates
> (`boomerBonus.HasValue && chainDepth <= BoomerChainCap`). `DetonateBoomer` applies a radius-1
> cardinal cross using the SAME block-result table as `BombSystem.Blast` (`Core/BombSystem.cs`)
> with ONE deliberate difference: **Bomb cells are left completely untouched** — not armed, not
> destroyed — because arming them would open a second, uncapped chain reaction through real bombs
> that `BoomerChainCap` was never designed to bound (the dev's own stated reason: "pour éviter les
> boucles infinies"). Two new events, `AirCapsuleLiberated`/`DiamondLiberated`, mirror
> `BombSystem`'s naming exactly so the future GameBootstrap wiring (§7) is a straight copy-paste of
> the bomb-liberation pattern.
>
> **No avatar-hit code exists anywhere in the Boomer blast path** — that's the actual mechanism
> behind "the explosion never hurts the avatar": there's nothing to wire wrong, because nothing
> checks avatar position at all. `AvatarHitByEnemy` stays declared-but-unfired (R5.10 still owns it).
>
> Chain depth: the Boomer directly killed by a burst/bomb is depth 1 and always detonates if it has
> a bonus; each Boomer its blast kills is one depth deeper. Verified end-to-end with a 4-Boomer line
> (A→B→C→D): killing A cascades through B (depth 2) and C (depth 3, at the cap) but D (depth 4)
> dies without detonating — exactly 3 `BoomerDetonated` events, 4 `EnemyKilled` events.
>
> 9 new tests (the 10th requested case — "Boomer killed → BoomerDetonated fires" — was already
> pinned by two R5.6 tests, not duplicated). 303/303 PlayMode tests pass.

> **R5.9 delivered 2026-09-01.** Three activation entry points, all funneling through one private
> `ActivateAdjacent`/`ActivateDormantAt` pair (mirrors `BombSystem.ArmAdjacent`/`TryArm` exactly —
> same four-cardinal-neighbor shape, same "no-op if nothing dormant is there" contract):
> - `NotifyAdjacentDrill(GridPos)` — wakes a dormant enemy adjacent to a drilled cell.
> - `NotifyBurst`/`NotifyBombBlast` — now ALSO call `ActivateAdjacentToZone` on the full effect zone
>   (burst footprint ∪ shockwave ring, or the blast cells) before resolving kills. An enemy just
>   outside the zone wakes up without necessarily being the one that dies — verified explicitly:
>   `NotifyBurst_ShockwaveAdjacentToDormantEnemy_ActivatesIt` asserts the Crawler is BOTH active AND
>   still alive, since it's adjacent to a shockwave cell, not inside it.
> - `ActivateInViewport(int minRow, int maxRow)` — Endless has no per-board activation event (there's
>   no "drilled" or "burst" to hook), so this is a direct row-range sweep GameBootstrap calls from
>   the camera's current view bounds. Inclusive both ends, checked against the two boundary rows
>   directly in `ActivateInViewport_WakesEnemiesInsideTheRange`.
>
> Reused `ActivateEnemy(id)` itself (not a duplicate mutation) for every trigger, so the dormant/
> already-active/dead guards and the `EnemyActivated` event only exist in one place. 6 new tests,
> 309/309 PlayMode tests pass.

> **R5.10 delivered 2026-09-01.** One check appended to the existing Crawler loop in `Tick` — no new
> loop, no new per-frame grid/enemy scan. The loop already filters to `IsAlive && IsActive &&
> Type == Crawler` before doing anything (movement included), so "only active Crawlers deal contact
> damage" and "dormant enemies never deal contact damage" both fall out of that SAME filter for
> free — a Boomer or a dormant enemy never reaches the new `if (enemy.Position == avatarPos)` line
> at all, there's no separate branch to get wrong. The check runs after the movement `while` loop
> and reads the (possibly just-updated) position, so it catches both directions of contact: a
> Crawler stepping onto the avatar's cell mid-`Tick`, and the avatar having walked onto a Crawler
> that didn't move at all that frame (verified with `dt = 0.01f`, far below `CrawlerMoveInterval`,
> in three of the five new tests).
>
> **`AvatarHitByEnemy` fires on every overlapping frame, not just the first** — by design, per the
> dev's note that HealthSystem's i-frames are what make repeated contact harmless. De-duplicating
> here would be redundant with that cooldown and would need EnemySystem to track "have I already hit
> the avatar this overlap", state it has no other reason to keep. 5 new tests, 314/314 PlayMode
> tests pass.

> **R5.11 delivered 2026-09-01.** `ScoreSystem.AwardEnemyKill(EnemyType, int bonus = 1)` and
> `AwardBoomerBlast(int blocksDestroyed, int parentBonus)`, plus `ScoreSource.EnemyKill` /
> `.BoomerBlast` (both already forward-declared in the §5.6 enum listing, but never added to the
> real `ScoreSource.cs` until now). `AwardBoomerBlast` deliberately reuses `BombPointsPerBlock`
> rather than a new constant — the dev's own framing ("scores like a bomb") and the §8 table's
> formula both point at the SAME 25-per-block rate, not a parallel one that could drift out of sync
> at the next balance pass. Both bonus parameters clamp to a minimum of 1 (matching `AwardBomb`'s
> `chainMult` clamp) — a plain crush kill or an unscaled blast still pays out, it just doesn't
> multiply. `Score_AccumulatesAcrossEverySource` — the running regression guard every new scoring
> source gets added to (diamonds joined it at R4) — grew the two new calls, 1030 → 1430. Also fixed
> a stale §5.6 API table that still listed the pre-v3.1-pivot `Digger=200, Tank=300` enemy types
> (removed) and was missing `AwardDiamond` entirely. 10 new tests, 324/324 PlayMode tests pass.

> **R5.12 delivered 2026-09-01.** Items 1 and 2 of the request were already done (R5.1/R5.2 shipped
> the drill direction + `RestoreDrill`, R5.4 shipped the score gate) — only
> `_enemySystem.NotifyAdjacentDrill(pos)` was missing from the drill handler. The rest needed
> **four Core additions, because the §7 draft referenced three members that never existed**:
> - **`GravitySystem.LastShockwaveCells`** (new). The draft read this inside the `ChunkBurst`
>   handler, but the ring was computed AFTER `ChunkBurst` fired — a naive property would have handed
>   every burst the PREVIOUS burst's ring. `ResolveBurst` now computes the ring *before* firing the
>   event (its effects still apply after, so the documented crush → land → burst → shockwave order is
>   unchanged). Chosen over widening the `ChunkBurst` event because that event has five subscribers
>   (GameBootstrap, VfxManager, AudioManager, playtest harness, ChunkBurstTests) and only one wants
>   the ring. It is only valid during the event — the XML doc says "never poll it".
> - **`BombSystem.BlastResolved(List<GridPos> cells, int chainMult)`** (new). Nothing exposed which
>   cells a blast touched. `Blast()` now appends every in-bounds cell it reaches to a list (plus the
>   bomb's own centre, which a pocket bomb can share with an enemy), fired next to `BombScored`.
> - **`EnemySystem.EnemyKilled` gained the bonus** (`id, type, method, bonus`). The draft had
>   GameBootstrap `switch` on `KillMethod` and read `_gravity.LastFallBonus` / `_bombSystem
>   .LastChainMult` — two more fields that don't exist, and an out-of-band read that only works while
>   the handler happens to run inside the originating resolution. EnemySystem already holds the exact
>   value at kill time, so it just passes it along. Same reasoning as R5.6's `NotifyBombBlast`.
> - **`EnemySystem.BoomerDetonated` gained the block count** (`pos, parentBonus, blocksDestroyed`)
>   and now fires AFTER the blast resolves, mirroring `BombSystem`'s `BombScored`. **The request
>   asked GameBootstrap to apply the Boomer blast to the grid; it must not** — R5.8 already does that
>   inside EnemySystem, so re-applying would double-destroy (Steel → Hard → Empty in one detonation)
>   and double-count. GameBootstrap only scores it. `DetonateBoomer` returns the count and excludes
>   Steel softening, matching BombSystem's "softening is not a destruction" rule.
>
> Also wired `ActivateEnemiesInView()` (Endless only) to complete R5.9's contract — it derives the
> visible row band from the camera's un-shaken centre ± orthographic size, rounded outward.
>
> **⚠️ No enemy actually spawns yet.** `SpawnEnemiesForBoard()` is deliberately an empty, documented
> hook: enemies are ACTORS, not CellTypes (§6.5), so they have no map character and *cannot* appear
> in the generated `rows` — `GridModel.FromStringMap` throws on unknown characters. R5.13 therefore
> has to hand placements over out-of-band (e.g. `StrateGenerator.EnemiesForLevel(level, rng)`), and
> filling the hook is then a one-line loop. Everything downstream of it is live and verified to
> compile + run. Verified by play-mode smoke test (boot → board loads → depth scores → zero console
> errors), since GameBootstrap has no unit coverage by design.

> **R5.13 delivered 2026-09-01.** `StrateGenerator.PlaceEnemies(board, level, rng)` +
> `CrawlerCountForLevel` / `BoomerCountForLevel` (§9 table), and R5.12's empty
> `SpawnEnemiesForBoard()` hook is now filled — **enemies actually spawn in campaign play from
> level 6.**
>
> **The board is NOT mutated — that resolves a contradiction in the brief**, which said both
> "l'ennemi REMPLACE la cellule" and "la cellule sous l'ennemi reste telle quelle, l'ennemi est une
> entité séparée". The parenthetical is the one that matches §6.5 and reality: enemies are ACTORS
> with no map character, and writing one into `rows` would make `GridModel.FromStringMap` throw.
> So `PlaceEnemies` is pure — it reads the board and returns coordinates, pinned by
> `PlaceEnemies_DoesNotMutateTheBoard`. An enemy's position is the coordinate of an existing solid
> Color/Hard cell, which IS what "buried/dormant" means; the cell keeps its type. That also makes
> the Crawler coherent: it's walled in until the player drills beside it, which simultaneously wakes
> it (§6.5 activation) and opens the tunnel it then walks into.
>
> **Spacing is enforced by construction, not by rejection sampling**: each enemy claims a whole row,
> and rows within `MinEnemyRowSpacing` (3) of a used one are skipped. That makes the hard constraint
> unfalsifiable rather than probabilistic, and sidesteps any retry loop. The two soft preferences —
> Boomers within `BoomerBombProximity` (2) rows of a buried bomb, Crawlers in rows with
> `CrawlerLateralSpace` (3) or more empty cells — are implemented as *ordering*, not filtering:
> preferred rows shuffle to the front, everything else follows as fallback, so the per-level count is
> always met. Boomers are placed first because "near a bomb" is the narrower preference and would
> otherwise lose its rows to Crawlers. `PlaceEnemies_CountsMatchTheCampaignTable` walks all ten
> levels and confirms spacing never starves placement on real boards (level 10 is the tightest:
> 11 enemies × 3 rows = 33 needed against 72 content rows).
>
> GameBootstrap seeds placement from the level (`level * 0x85EBCA6B`) so a level's enemies are as
> deterministic as its board, but on a different constant so the two streams aren't locked in step.
> Campaign only: the tutorial showcase teaches scoring not combat, the debug map is a sandbox, and
> **Endless placement is explicitly deferred to R6** (the counts are per campaign level, and Endless
> has no level number). 9 new tests, 333/333 PlayMode tests pass, plus a boot smoke test.

> **R5.14 delivered 2026-09-01.** Two new popup cases on the existing `OnScore` switch, no new
> plumbing — `ScoreSource.EnemyKill` → "+100 CRAWLER!" / "+450 BOOMER! ×3", `ScoreSource.BoomerBlast`
> → "+200 BOOM!". Two new colors so all five celebrations stay tellable apart at a glance: lime for
> a kill, violet for a Boomer blast (neither is amber/burst nor red/bomb-chain).
>
> **`ScoreEvent` carries no enemy type — the popup recovers it by arithmetic.** `Detail` is the kill
> bonus and `AwardEnemyKill` computes `Points = basePoints × bonus`, so `Points / Detail` lands
> exactly on `CrawlerKillPoints` (100) or `BoomerKillPoints` (150). Integer division is exact here
> because the multiplication was, so no rounding case exists. This keeps the popups on the
> OnScore-only diet §5.9 insists on: the HUD never subscribes to EnemySystem and never re-derives a
> scoring formula — it compares against ScoreSystem's own constants.
>
> One guard the brief didn't ask for: a Boomer that detonates in open air destroys 0 blocks, which
> would print "+0 BOOM!". `BoomerBlast` popups are suppressed at 0 points; verified live (the label
> stays `display: None`).
>
> Verified in play mode by firing the awards straight into the live `ScoreSystem` via reflection
> (pausing first so the 1 s fade timer doesn't run) rather than grinding to level 6 — screenshots
> confirm all three formats plus the `×N` suffix appearing only when bonus > 1. Screenshots landed
> in `Assets/Screenshots/` alongside R4's, untracked as before.

> **R5.15 delivered 2026-09-01.** Four effects: Crawler death (8 small olive particles, 0.28 s),
> Boomer death (18 bright orange particles radiating evenly + its own `SpawnRipple`, 0.5 s — visibly
> bigger and brighter than the Crawler's, per §6.5's "amplifier"), Crawler wake flash (one-shot
> expanding pale square), and the Boomer's standing halo. `VfxManager.Init` gained an optional
> trailing `EnemySystem` parameter; GameBootstrap already creates the VfxManager *before*
> `SpawnEnemiesForBoard()`, so `EnemySpawned` reaches it (that ordering was put in place at R5.12).
>
> **The Boomer glow reuses the bomb-fuse material** (`EnsureFuseMaterial`, the additive radial glow
> from §5.10) — a Boomer is a bomb with legs, so borrowing its visual language is both cheaper and
> more legible than inventing a second glow. It pulses ~1.5 Hz, deliberately slower and calmer than
> a lit fuse (which races from 2.5 → 12 Hz): a Boomer is a standing opportunity, not a countdown.
>
> **The events don't carry enough to draw with, so the view caches positions.** `EnemyActivated` is
> just an id (no type, no cell) and `EnemyKilled` has the type but no position — and by the time it
> fires the enemy is dead, so it can't be looked up either. Rather than widen two Core events for a
> purely visual need, `VfxManager` keeps an id → (type, cell) marker cache, seeded from
> `EnemySpawned` and refreshed from `GetAllAlive()` on a **0.2 s timer** rather than per frame:
> `GetAllAlive()` allocates a fresh list, and a Crawler only steps every 0.8 s, so 0.2 s is always
> well inside one move at 5 allocations/second instead of 60. This mirrors how `_fuses` already
> holds per-entity view state Core has no reason to carry.
>
> One case the brief didn't mention: a Boomer killed by a plain crush never detonates (§6.5), so
> `BoomerDetonated` never fires and it would have died with no VFX at all. `OnEnemyKilled` covers
> that path explicitly.
>
> Verified in play mode by spawning a real Crawler + Boomer, activating them (halo confirmed on
> screen), detonating the Boomer through the real `NotifyBurst` path (score +525, halo torn down,
> block destroyed), then re-firing all three transient effects on an `EditorApplication.update`
> loop so a single screenshot could catch them mid-flight — they're 0.25-0.5 s and otherwise faded
> before the capture round-trip landed.

> **R5.16 delivered 2026-09-01.** Four procedural clips, one shared `_enemySource`, wired in
> `AudioManager.Rewire()` (EnemySystem is grid-dependent, so it re-points every LoadLevel like
> BombSystem and GravitySystem do). The two activation cues sit at opposite ends of the register on
> purpose: a Crawler chirps HIGH (something small just started moving toward you), a Boomer boops
> LOW (something heavy woke up and is now just standing there).
>
> **The Boomer boom is built from `SfxSynth.Shatter` tuned the opposite way to the chunk burst** —
> mostly TONE (`noiseMix: 0.35`) at 70 Hz with a heavy low-pass (`0.06`), so it lands round and
> bass-heavy rather than crackly. That is exactly what separates it from the bomb blast, which is
> pure `Noise` at a much brighter low-pass (0.25).
>
> **Verified by measuring the generated waveforms, not by ear** (no audio over this connection):
> zero-crossing rate is a cheap proxy for brightness, and the numbers confirm the brief rather than
> just asserting it —
>
> | clip | length | zero-crossings/s |
> |---|---|---|
> | Crawler wake (Tone 1500 Hz) | 0.100 s | 2 990 (≈1 495 Hz ✓) |
> | Crawler death (Noise) | 0.150 s | 13 213 |
> | Boomer wake (Tone 150 Hz) | 0.200 s | 295 (≈147 Hz ✓) |
> | **Boomer boom** | 0.300 s | **337** |
> | *bomb blast (for contrast)* | 0.349 s | *10 884* |
>
> The Boomer boom is **32× darker than the bomb blast** — "distinct, rounder, bass-heavy" is a
> measured fact here, not a vibe. All four durations match the brief exactly. Playback was then
> confirmed end-to-end by firing each real event path (activate ×2, crush kill, burst detonation)
> and checking the source actually started.
>
> Same crushed-Boomer edge case as R5.15: a Boomer killed by a plain crush never detonates (§6.5),
> so `OnEnemyKilled` plays the boom at 0.6 volume for that path — it popped, it didn't detonate.

> **R5.17 delivered 2026-09-01 — R5 (and the v3.1 arcade pivot) is now feature-complete.**
> `View/EnemyView.cs`: one SpriteRenderer per living enemy, parented under the BoardView GameObject
> so it's torn down with the board on the next `LoadLevel()`. Created *before*
> `SpawnEnemiesForBoard()` so it receives every `EnemySpawned`.
>
> **Sorting order 8 — above the tiles (0) and exit glow (1), below the avatar (10).** That ordering
> is deliberate: when a Crawler walks onto the driller's cell, the driller stays visible at the exact
> moment contact damage fires (R5.10), instead of being covered by the thing hitting it.
>
> Dormant vs active is the whole visual language: dormant is `dormantAlpha` (0.4) and *perfectly*
> still, because a buried enemy genuinely cannot hurt you (§6.5) — waking is what makes it opaque
> and starts it moving. The Crawler shuffles (lateral wiggle + a half-rate vertical bob, so it
> scuttles rather than slides); the Boomer, which never moves, only breathes (scale pulse).
> Animation offsets are applied ON TOP of the eased base position, the same split `CameraShake` uses
> for follow-vs-shake, so movement and idle animation never fight.
>
> **Positions are polled, not evented** — EnemySystem has no "moved" event. `RefreshTargets()` reads
> `GetAllAlive()` every 0.1 s (a Crawler only steps every 0.8 s, so that's 8× more responsive than
> anything that can change) and the per-frame lerp does the smoothing; 10 list allocations/second
> instead of 60. Same reasoning as VfxManager's marker cache (R5.15).
>
> Verified in play mode with all four states side by side, confirmed both visually and by sampling
> the live transforms twice — dormant enemies sat at exactly integer positions and base scale on
> both samples (frozen), the active Crawler moved `x 1.122 → 0.907` with a bobbing `y`, and the
> active Boomer held position while its scale oscillated `0.788 → 0.733`. 333/333 tests still pass.

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

## 13. Test status

**343 / 343 passing** (was 112 before the v3 refactor; +3 for `Settle`, +0 net from the §5.2
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
| `DailyDigTests` | 🆕R3.4 6 tests: same day = same seed (any hour), different days differ, consecutive days generate *unrelated boards*, seed stable across runs, never `int.MinValue`, invariant label |

> **Note:** the M2 Collapse (21) and Score (23) suites referenced in the old plan were never
> actually ported to NUnit — they lived in the retired console harness. CollapseSystem had only
> 2 Steel tests; ScoreSystem had none. Both now have real coverage.

**Three v3 gravity tests were deliberately shortened to 1-row drops** (`Bridge_LosingBothSupports`,
`StackedChunks_FallAndLandStacked`, `FallingChunk_OntoAvatar_FiresCrush`) so they keep testing
slab cohesion / stacking / crush rather than accidentally testing bursts. `ChunkBurstTests` owns
the shatter cases.

---

## 14. Historical notes

- **Tests run in PlayMode, not EditMode** — the assembly definition doesn't have `Editor` in
  its platforms. Not blocking; left as-is.
- **v2.1 CLAUDE.md** documented the void-line-centric design. This file (v3) supersedes it.
  The v2.1 GDD and CLAUDE.md are kept for reference but are no longer authoritative.
- **`hollow-lines-gdd-v3.md` was synced to shipped values on 2026-07-23.** The GDD had kept the
  original v3 *draft* numbers; §4.9/§5/§6 now carry the R2.8-balanced ones (air restore 6/0.5/1/6,
  drain 4/7 %/s, depths 24-76, Perfect Clear flat 500, shockwave ring harmless to the avatar). Each
  spot links back to the relevant §15 subsection for the rationale, so the two docs no longer
  disagree. §4.7 was also fixed to state Hard/HardCracked/AirCapsule are streak-neutral (it used to
  contradict itself) — matching `StreakTracker` and §6.1.
- **Procedural-first assets, not zero-asset.** All SFX are synthesized (`SfxSynth`), and VFX are
  built from generated primitives — including three procedural **shaders**: `FuseGlow` (additive
  radial glow, no texture, §5.10), 🆕R4 `DiamondShine` (persistent per-tile shimmer + glint
  sweep, §5.10), and 🆕 `ExitGlow` (additive ambient floor wash for the campaign/tutorial exit
  zone, §5.16). The block *sprites* are the exception: AI-generated tiles loaded from
  `Resources/Tiles/` by `BoardView`, with a procedural white-square fallback.
- **Bot playtest harness** — `tools/playtest/` compiles real Core sources. Now fully v3-wired
  (R2.7); run with `dotnet run` from that folder. Its output is the evidence behind §15.
  **R3.1 added endless runs:** a `Persist` object (score / air tank / hearts / clock / RunResult)
  is shared across a chain of per-segment `Sim`s, mirroring GameBootstrap's seam (§5.15) — persistent
  handlers subscribe once (`Persist.Wired`) or every award would be counted per segment. Output
  sections 6 = endless descents + terrain composition by depth.
- **`spawnCell` must be the middle column.** The scene serializes it (`PrototypeGym.unity`), and a
  serialized value always beats the code default. It was `(4,2)` — correct for the old 9-wide
  board, wrong for 7 — and both level 2/3 tutorials are authored at `width/2`. Now `(3,2)`.
  `LoadLevel()` also clamps it defensively.
- **Debug/testing toggles on GameBootstrap (added 2026-07-24).** Two serialized checkboxes under a
  "— Debug / Testing —" Inspector header, both off by default, both safe to flip live in play mode:
  - `disableAir` — gates the `_airSystem.Tick(dt)` call in `Update()` (tick step 9, §7), so air
    never drains and the "Air épuisé" game-over can't trigger. Restores still fire, harmlessly.
  - `invincible` — early-returns at the top of `OnAvatarCrushed()` before `TryTakeDamage()`, so the
    avatar ignores every crush/blast: no heart loss, no shake, no crush SFX, and **no teleport-to-
    spawn** (you pass through / can stay lodged in a chunk — decouple it if testing crush ejection).
  Pure View flags: `AirSystem`/`HealthSystem` and the test suite are untouched. Since a serialized
  value beats the code default, confirm they're unchecked in `PrototypeGym.unity` before shipping.
- **Endless serialized fields have the same serialized-beats-default caveat (2026-07-26).**
  `endlessMode` / `endlessSeed` / `endlessSegmentRows` were added after the scene was last saved, so
  the scene currently holds no override and the code defaults apply (false / 0 / 60). If you tick
  `endlessMode` in the Inspector to test, **untick it before shipping** — it would boot the game past
  the campaign. The main menu's "Sans fin" button is the intended entry point and needs no serialized
  change. Same for `endlessSeed`: a non-zero value pins every run to the same well.

---

## 15. ✅ Balance findings — ALL RESOLVED (R2.8 complete)

First real v3 playtest run (`cd tools/playtest && dotnet run`, R2.7) found the systems all worked
but the **balance contradicted three of the seven non-negotiable design rules.** All five findings
below are now resolved (R2.8a-f, 2026-07-21): color veins (§15.3), Perfect Clear nerf (§15.2),
settle-on-load + pacing (§15.1 / R2.8d/c), bomb economy proven (§15.4), wobble closed (§15.5).
Endless (R3) can proceed — it inherits a scoring/pacing model that now holds. Kept as a record of
*why* each constant is where it is; **re-read the relevant subsection before re-tuning any of them.**

### 15.1 Pacing — ✅ RESOLVED (R2.8c): cascade fixed, re-measured, pacing satisfies the design rules

Original symptom: the tunnel bot won all 10 levels in **1.3–4.5 s**, never dropping below **95 %
air**. Removing the line gate (§5.7) left depth as the only win condition, and drilling straight
down is both the fastest path and the natural instinct.

Two of my original three "directions" were wrong and are retracted:
- ~~"Make boards wider"~~ — the win is vertical; a tunnel is a tunnel at any width.
- ~~"Raise drain"~~ *alone* — making a 2 s run costly needs ~20 %/s, which kills exploration.
  Drain only works **after** runs are long enough for it to act. The two levers are multiplicative.

#### What landed

1. **Depths tripled** (§9 table rewritten: 14-26 → 24-76 rows). The v2.1 line gate was the run's
   time sink; nothing replaced it. Even a human-paced player was spending only 9-25 % of their air.
   → descent went from 1.3-2.3 s to **2.4-7.1 s** (fast bot), 4.6-8.9 s (human-paced).
2. **Drain raised** 3/4 %/s → **4/7 %/s**, now that runs are long enough for it to bite.
3. **Restore economy corrected** — see below. The draft values assumed rare events.
   `+25 capsule / +5 burst / +3 bomb / +15 PC` → `+6 / +0.5 / +1 / +6`.

The harness now prints an air ledger (`drain -X caps +Y burst +Z`) so this is measurable, and
`RestoreEconomy_IsSmallRelativeToDrain` guards against the values drifting back up.

#### ⛔ Root cause — the burst cascade (NOT yet addressed)

Air *income* still outruns drain ~4× on level 10 even after the nerf, and the ledger says why:

```
lvl 10  drain -62.1   caps +204.0(34)   burst +39.0(78)
```

**78 bursts and 34 capsules in a 8.9 s run.** The board only *has* ~35 capsules — the run collects
nearly all of them, including ones nowhere near the player's tunnel.

The mechanism: a burst clears its chunk **plus a 1-cell shockwave ring** (§5.2). That much removal
destabilises the neighbours, which burst, which destabilise more — positive feedback. One tunnel
sets off a chain reaction that demolishes the board, and along the way it:

- liberates nearly every air capsule → air becomes free (§15.1)
- generates 78 × burst restore → air becomes free again
- **destroys the blocks before the player can drill them** → which is very likely why streaks
  stayed at 1-8 even after the vein fix (§15.3)

8.7 bursts per second is not "spectacle" (design rule 2), it is continuous demolition. **Further
tuning of restore values is treating a symptom** — the cascade needs a design decision first:

- shrink or remove the shockwave (it is what propagates the cascade),
- raise `BurstFallThreshold` from 2 to 3,
- or require the board to settle between bursts.

Each changes game feel materially, so this is a call for the designer, not a constant to twiddle.

#### ✅ Resolution (R2.8c / R2.8d, 2026-07-21)

The designer chose **silent settle-on-load** (`GravitySystem.Settle()`, §5.2 / R2.8d) over shrinking
the shockwave. The board now rests before play; only player-caused falls burst. Re-measured on the
settled board (the harness now calls `Settle()` too, matching GameBootstrap):

```
                           BEFORE (self-destructing)     AFTER (settled)
tunnel bot, lvl 10 bursts  78                            24
                 capsules  34                            17
air income vs drain        ~4×                           ~2×
```

With the cascade gone the pacing measures honestly, and it **satisfies design rules 5 and 6**:

- **Aggressive descent** (tunnel bot): WINS levels 1-9 with air to spare, crushed on 10 by gravity
  (fair, telegraphed). A perfect straight-down line is *meant* to be survivable — that is the skill.
- **Passive / greedy play** (row-clear bot, sweeps every row): dies to air or crush almost every
  level. Rule 5 ("passive = death") holds.
- The insight: the air clock is effectively a **tax on score-farming** (streaks / bursts / bombs all
  slow you down and cost air), not a tax on descending. That matches the arcade design, so the three
  landed fixes (depths ×3, drain 4/7 %/s, restore +6/+0.5/+1/+6) are kept as-is — **no further
  air/drain tuning.**

**One generator fix landed here:** level 7's `bombRate` (0.12/0.14 — the densest level) **walled the
player in** (bombs are not drillable) and produced *fewer* chains than levels 8-10. Lowered to
0.09/0.10; re-measured, level 7 went from **1 detonation / 0 chains** to **8 detonations / 4 chains
(×3), 13 % bomb points** — it finally teaches what it is themed on (§15.4).

> **Residual, not blocking:** a pure speedrunner still ends levels 1-9 at 73-99 % air — air is nearly
> decorative for the optimal line. Left as-is on purpose (see the "tax on score-farming" insight); if
> Endless (R3) wants more descent pressure, that is the depth-based drain ramp's job (R3.2), not a
> campaign retune.

### 15.2 Perfect Clear pays for the whole game — ✅ CASCADE REMOVED, judgment call remains

**The diagnosis.** The v3 draft specified `500 × cascade`, contradicting design rule 7's flat
"+500". Measured, cascades were averaging **×4–×7**: level 7 scored 45 427 points from 13 Perfect
Clears — ~3 494 each. The base was never the problem; the multiplier was.

**The fix.** `AwardPerfectClear` is now **flat `PerfectClearBase`**. Rule 7 is listed
non-negotiable, so it wins over the §8 table. The cascade step is still reported in
`ScoreEvent.Detail` (the HUD popup and chain SFX read it) — it just no longer scales the points.

#### Measured effect — row-clear bot, points mix (`% streak / burst / bomb / depth / perfect`)

| lvl | before | after |
|---|---|---|
| 7 | 0/ 2/ 4/ 1/**91** | 7/14/14/ 9/**53** |
| 8 | 0/ 6/ 0/ 8/**84** | 1/13/ 0/21/**64** |
| 9 | 0/10/ 0/ 8/**80** | 1/20/ 0/20/**57** |
| 10 | 0/10/ 2/ 6/**79** | 1/25/ 0/16/**55** |

The runaway is gone. **Burst share roughly tripled** on late levels (8–10 % → 20–26 %) and **bombs
became visible for the first time** (0 % → 12–18 % on levels 6-7) — both simply by removing what
was drowning them out. Run totals fell ~60 % (lvl 7: 27 330 → 9 330), which is the point.

#### ⚠️ Remaining judgment call — NOT a data question

Perfect Clear is still **44–68 %** for the row-clear bot. But look at the two bots separately:

- **Tunnel bot (natural descent):** Perfect Clear is **0 %** on most levels, 37–42 % at worst.
- **Row-clear bot (deliberately farms void rows):** 44–68 %.

So PC is already rare for a player who just descends; it is only dominant for someone
*optimising* for it. Whether that gap violates rule 7 is a **design decision, not a measurement**:

- Cutting `PerfectClearBase` further (500 → ~250) would fix the farmer's mix, but it also devalues
  the rare jackpot the normal player earns maybe twice a run — punishing the intended experience
  to correct the degenerate one.
- The alternative is to make Perfect Clear *harder to farm* (a level-design / generator lever)
  rather than cheaper.

**Left at flat 500 pending that call.** Note this interacts with §15.1: the row-clear bot only gets
to farm because runs are slow and unpressured. Fixing pacing may resolve this on its own — do
§15.1 before touching the base again.

### 15.3 Drilling is not the reward — ✅ ROOT CAUSE FIXED (color veins), points mix still blocked

Design rule 1: "Drilling IS the reward. Every drill tap gives points (×streak)."

**Original measurement:** best streak 1–10 (usually 2–4), streak share of points falling to **0 %**
on later levels.

**Diagnosed cause:** `PickCell` rolled every cell's color independently, so a same-color run of 4+
essentially never occurred outside the authored level-2 vein. The streak system had no material.

#### The fix (implemented)

Color choice is now split out of the type roll:

- `PickCellType()` decides the **type** only (empty / steel / bomb / hard / air / *color slot*).
- `PickColor()` resolves the color by **looking at its neighbors**, with
  `VerticalColorCohesion = 0.75` and `HorizontalColorCohesion = 0.55`.
  Vertical is weighted higher because drilling straight down is the natural instinct.
  Expected vein length ≈ `1/(1-p)` ≈ 4, i.e. a routine ×4 streak.
- Inheritance respects the strate's `MaxColors`, so a 2-color band never inherits a `C`.

> **⚠️ The non-obvious part — `Build` threads a COLUMN MEMORY, not the previous row.**
> The first implementation passed the literal row above and **did not work**: with `EmptyRate` at
> 0.36 the inheritance chain was shredded by gaps, and level 8 still capped at a 3-run.
> `Build` now carries `char[] columnColors` — the last color placed in *each column*, however many
> empty / Hard / Steel cells sit in between. A vein survives holes **exactly as the player's streak
> survives streak-neutral cells** (§6.1). That symmetry is the point; do not "simplify" this back
> to the previous row.

#### Measured effect

| Metric (tunnel bot) | Before | After |
|---|---|---|
| Best streak | 1–5 (median 2) | 2–8 (median 4) |
| Biggest burst | 1–6 cells | up to **12** |
| Best bomb chain | rare | up to **8** |

Bigger chunks were the predicted side benefit, and they materialised — cohesion feeds Chunk Burst
and bomb chains for free.

**Still open:** the streak *share of points* barely moved (0–29 %) and Perfect Clear still takes
79–91 %. That is **not** a streak problem any more — it is §15.2 alone. This fix was necessary but
not sufficient; it makes the Perfect Clear nerf measurable instead of trading one imbalance for a
vacuum. **Do §15.2 next.**

Pinned by `GenerateRow_InheritsColorFromTheRowAbove`, `GenerateRow_CohesionNeverViolatesMaxColors`,
and `CampaignBoard_ProducesVerticalVeins_LongEnoughForStreaks` (which scores a column the way
StreakTracker would: only a *different* color breaks the run).

#### Two side effects to watch

1. **Levels 1 and 3 now end "crushed" at the 0.25 s cadence** (they used to be won). More mass per
   chunk = gravity is a real threat again. Healthy, or too punishing for onboarding? Undecided.
2. **Level 2 burst opportunity dropped to 4.6 %** (from 1.5 %, vs 10–15 % elsewhere) — veins
   redistribute the gaps. Consistent with bursts being taught at level 3, so not acted on.

### 15.4 Bursts are working; bombs are proven (R2.8e ✅)

Bursts fire well and the raised porosity does its job — **11–15 % of solid cells sit over a 2+ gap**
on levels 3-10 (levels 1-2 sit lower, which is fine: bursts are taught at level 3).
Since the §15.3 fix, burst *size* is up substantially (biggest burst 1–6 → up to 12).

**Bombs: verdict = the economy works; the old "0 %" was a bot artifact, not a design flaw.**
R2.8e added a `BombBot` to `tools/playtest` that deliberately digs at buried bombs to light fuses
(the tunnel/row bots avoid them, so they could never answer this). Measured, hunting bombs:

| lvl | bombs on board | detonations | chain events | best chain | bomb % of points |
|---|---|---|---|---|---|
| 6 | 11 | 6 | 0 | ×1 | 3 % |
| 7 | **41** | **1** | 0 | ×1 | 1 % |
| 8 | 25 | 8 | 3 | ×3 | **22 %** |
| 9 | 30 | 20 | 7 | ×3 | 3 % |
| 10 | 45 | 5 | 3 | ×4 | **33 %** |

So bombs detonate, **chain up to ×4**, and can be **22–33 % of a run's points** when actively
pursued. Design rule 3 ("bombs are friends") holds. The bot dies every run (it does not dodge its
own blasts — deliberate; the point was to measure payout, not survival), which caps the sample but
the chain data is real.

> **✅ Level 7 anomaly — FIXED (R2.8c).** Level 7 authored **41 bombs** (vs 11 on lvl 6, 25 on lvl 8)
> yet the hunter triggered only **1** detonation before air-death: bombs are not drillable, so the
> `bombRate` 0.12/0.14 band (the densest of any level) **walled the player in** and produced *fewer*
> chains than the lower-rate levels 8-10 — the opposite of the "bomb chains" intent. Lowered to
> 0.09/0.10; re-measured, level 7 went from **1 detonation / 0 chains** to **8 / 4 (×3), 13 % bomb
> points**. The table above is the pre-fix run.

**Side benefit measured here:** the R2.8d settle-on-load also tamed the burst cascade the old
§15.1 numbers were taken on. Tunnel bot, level 10: **78 bursts / 34 capsules → 24 / 17**, and air
income fell from ~4× drain to ~2×. The board no longer demolishes itself, so the remaining air
oversupply is now a straight pacing question (§15.1 / R2.8c), not a cascade artifact.

### 15.5 Wobble tuning — ✅ CLOSED (R2.8f): not answerable by non-dodging bots; keep the defaults

Re-ran on settled boards with the R2.8c pacing (runs are now 12-31 s, no longer "too short"). The
0.6 / 0.8 / 0.9 s comparison **still shows no self-crush reduction from a longer telegraph** — if
anything the longer wobble slightly *worsens* bot outcomes (row-clear bot, 0.15 s: lvl 1 goes
2♥ WIN → 2♥ WIN → 3♥ crushed as wobble rises; levels 2-3 the same shape).

**Why it can't be measured this way:** neither bot dodges on the telegraph. The row-clear bot keeps
drilling and stepping under chunks regardless of the wobble, so it never cashes in the extra dodge
window; a longer wobble just holds the chunk aloft longer and shifts the run's timing (swapping
crush deaths for air deaths). The telegraph's value is a **human-reaction** question (can a player
read and sidestep in 0.6 vs 0.8 s?), which a wobble-blind bot cannot answer.

**Decision:** keep the shipped values — **0.8 s on campaign 1-3, 0.6 s from level 4** (design rule 4).
A real answer needs a dodging bot or a human playtest; that is out of scope for the R2.8 balance
pass and belongs with playtesting, not the harness. §15.5 is closed as "measurement not applicable",
not as a tuning change.

### 15.6 Endless generator — `emptyRate = 0` bug (fixed 2026-07-23, ahead of R3.1)

Found while auditing the code against the GDD: `StrateGenerator.EndlessStrates` passed `0f` in the
`emptyRate` constructor slot, so **endless boards generated with zero porosity** — a solid wall.
With no gaps a chunk can never fall 2+ rows, so Chunk Burst would have been dead on arrival in
endless, and the board would be one undrillable-through mass.

Fixed by ramping the two v3-calibrated campaign constants with difficulty:
`empty = EarlyEmptyRate + (LateEmptyRate - EarlyEmptyRate) × difficulty` (0.28 → 0.36). **Not** the
GDD §7 "empty rate" column (25 %→12 %): those are the pre-balance numbers §15.3 already rejected as
too low for bursts (opportunity scales ~`EmptyRate²`).

Pinned by `EndlessBoard_ContentRows_HavePorosity` (asserts >10 % empty in the content rows — a loose
floor that survives seed variance but fails hard if `emptyRate` regresses toward 0). **The value was
provisional until R3.1 measured it — see §15.7.**

### 15.7 Endless steady state — ✅ MEASURED AND RETUNED (R3.1, 2026-07-26)

Endless generated its own rates (`EndlessStrates`), and every one of them was set by **estimate**:
the mode was not playable, so nothing had ever been measured. R3.1 taught `tools/playtest` to run
endless (a chain of segments sharing one score / air tank / hearts — the harness mirror of §5.15)
and compared the generated terrain against **the campaign, the only balance yardstick this project
has evidence for**.

#### The finding: deep endless was worse than any authored board

| Board | color cells | hard+steel | burst-ready¹ |
|---|---|---|---|
| campaign 10 (hardest authored) | **32 %** | 13 % | **13 %** |
| endless steady state (before) | **22 %** | **27 %** | 11 % |

¹ share of solid cells with 2+ empty cells stacked beneath — a *genuine* burst opportunity, not just any gap.

Two design rules were breaking at once:

- **Rule 1 ("drilling IS the reward").** Color blocks are the streak's raw material. At 22 %, two
  thirds of every solid cell was hazard — the streak starved exactly where a run is supposed to
  peak, and steel isn't even drillable, so it also forced ever more air-costly detours.
- **Rule 2 ("big chunks burst").** Burst opportunity *fell below* the campaign band and then flatlined,
  so the deepest boards offered the FEWEST bursts. Backwards for the mode whose entire payoff is spectacle.

The cause was simply that the estimated caps (hard 0.15 / steel 0.12) were higher than anything the
campaign ships, and porosity ramped to the campaign's 0.36 while hazards ramped past it.

#### The fix (endless-only constants — campaign strates untouched)

| Constant | Before | After |
|---|---|---|
| `EndlessMaxHardRate` | 0.15 | **0.10** |
| `EndlessMaxSteelRate` | 0.12 | **0.08** |
| `EndlessMaxBombRate` | 0.08 | **0.08** (kept — campaign 10 ships ~9 % bomb cells) |
| `EndlessLateEmptyRate` | 0.36 (`LateEmptyRate`) | **0.40** |

Endless should **end harder than the campaign, not stop being the same game** — late difficulty is
the *drain ramp's* job (§15.1: the air clock is a tax on score-farming, not on descending).

#### Measured effect

| | color | hard+steel | burst-ready |
|---|---|---|---|
| endless steady state (after) | 27.5 % | 18.6 % | **13.7 %** |

And behaviourally, tunnel bot over 3 seeds:

| | before | after |
|---|---|---|
| depth reached | 45 / 99 / 85 | **157 / 162 / 109** |
| seams crossed | 0 / 1 / 1 | **2 / 2 / 1** |
| bursts | 18 / 40 / 8 | **60 / 57 / 30** |
| score | 9.8k / 13.3k / 8.5k | **27.6k / 31k / 15k** |

Pinned by `EndlessSegment_AtSteadyState_KeepsColorMaterialForStreaks` (color > 24 %) and
`EndlessSegment_AtSteadyState_KeepsBurstOpportunity` (burst-ready > 11 %) — both averaged over 8
seeds so they survive seed variance but fail hard if the rates drift back.

> **Two honest limits on this pass.** (1) Every endless run now ends **"crushed"**, because neither
> bot dodges the wobble (§15.5) — more bursts means more falling mass, so that is expected and says
> nothing about human play. (2) The tunnel bot still ends deep runs at 63-77 % air even with drain
> ramped to ~9 %/s: for a pure speedrunner the air clock stays soft, the same residual §15.1 accepted
> for the campaign. If endless wants real descent pressure, the lever is the drain ramp's slope
> (R3.2 constants), not the terrain — but that needs a *dodging* bot or a human to judge.

---

## 16. Controls & input

Added after R2.8, before R3. All input flows through **`GameInput`** (gameplay) and the
**`InputSystemUIInputModule`** created by `UIScreenManager` (menus) — no other script reads devices.

### Bindings

| Action | Keyboard | Xbox / gamepad |
|---|---|---|
| Walk left / right | A/D (or Q/D) | Left stick X (deadzone 0.5) |
| Drill **up** | ↑ | **Y** |
| Drill **down** | ↓ | **A** |
| Drill **left** | ← | **X** |
| Drill **right** | → | **B** |
| Pause / resume | Esc | Start |
| Menu: navigate | arrows | D-pad / left stick |
| Menu: select / back | Enter / Esc | A / B |

**Face-button drill mapping (gamepad):** the four face buttons drill toward their **physical
position** on the diamond — Y↑ A↓ X← B→ — so it reads without a legend. This replaced an earlier
D-pad drill scheme (dev call). The D-pad is currently unused in gameplay (free for a future "also
move" binding if wanted).

### Two rules that keep it from crossing wires

1. **Gameplay input is suppressed while any overlay is open** (`GameInput.Update` early-returns when
   `Time.timeScale == 0f`). Menus freeze time, so the same buttons the UI navigates with — A (Submit),
   D-pad (Navigate) — do **not** also drill/move the avatar behind the menu. This is why A can mean
   both "Submit" (menu) and "drill down" (play) with no conflict.
2. **The EventSystem is created at runtime**, not in the scene (`UIScreenManager.EnsureEventSystem`,
   §5.12). Without it, UI Toolkit buttons are mouse-only — a gamepad cannot navigate them.

### Verified (2026-07-21, Unity play mode)

Xbox 360 pad shows up as `Gamepad.current` (an `XInputControllerWindows` device); EventSystem +
module present; the main-menu primary button auto-focuses; a `NavigationSubmitEvent` on the focused
"Jouer" started the game (overlay → None, `timeScale` → 1). The one hop that **cannot** be simulated
from the editor is the physical XInput-button → module step (a generic `GamepadState` event is
rejected by the XInput device's state format), so per-button feel is confirmed by hardware testing,
not code.
