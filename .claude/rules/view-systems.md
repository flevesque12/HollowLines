---
paths:
  - "**/View/**"
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
  renamed `PerfectClearRestoreAmount` at the same time — the v2.1 vocabulary was in the public API.
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
>
> **🆕R6.2 start buffer (F02).** `BeginStartBuffer(grace)` opens a board with `grace` s of zero drain,
> then a `StartRampDuration` (12 s) linear ramp from `StartDrainFactor` (0.5) to the full rate —
> integrated, not sampled, so it's frame-rate independent. Opt-in (no call = old behavior), cancelled
> by `Reset()`. GameBootstrap calls it in `LoadLevel()` on every fresh board, **not** on an endless
> seam (`LoadLevel(freshBoard: false)`). Grace = `CampaignManager.AirStartGraceForLevel` (5 s on
> levels 1-3, 3 s after), `DefaultStartGrace` (3 s) for endless/debug. Measured in §15.8.

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
- 🆕R6.2 **Air start-buffer cue** on the air bar (§5.5): during the grace window the fill turns pale
  (`ColAirIdle`) with a slow shimmer, the caption counts down (`AIR · 3`), and a 4 px gold fuse strip
  burns down across the top of the bar; at grace end a 0.35 s white flash; through the ramp the fill
  slides pale → cyan with `StartBufferFactor`. Polled in `Update` (`TickAirBufferCue`), because
  `AirChanged` stays silent while nothing drains. Danger red (< 25 %) always wins. The fuse was white
  first — invisible on the pale fill in a screenshot, so it's gold.
- 🆕R6.6 **Drill score popups** (F07): `HUDView.ShowDrillPopup(worldCellCentre, points, streak)` —
  "+30 ×3" rising 0.7 cell over 0.7 s, fading over the second half, **right of the drilled cell**
  (centred on it, it sat on the driller, who falls into that cell). Colour = streak colour lifted 30 %
  toward white (raw teal was too dark on the well); size 18 → 30 px with the streak; "×N" from ×2 up.
  Pool of 16 labels (oldest recycled), each keeps its WORLD position and is re-projected every frame
  (`RuntimePanelUtils.CameraTransformWorldToPanel`), so it rides with the board as the camera follows.
  **Points = score delta measured around GameBootstrap's whole `Drilled` handler** — includes the
  streak multiplier and a drilled diamond's +150, duplicates no formula, and is independent of
  subscription order. Separate from the centre celebration popup (bursts/chains/Perfect Clear).
- 🆕R6.7 **Streak counter** (F07): shown from **×2** (a ×1 is just the colour you're on), 30 → 60 px
  with the run (capped ×10), outlined, streak colour lifted 15 % toward white, scale punch (1.35 → 1,
  0.18 s) on every step. **Crack** when a ×2+ streak breaks (`StreakBroken(lost)`): two overflow-
  clipped halves holding a copy of the old "×N" sit over the label (`_streakBox`, relative) — 0.12 s
  shudder while it drains to red-grey, then the halves drift apart, fall, tilt ±22° and fade (0.6 s
  total). A ×1 ending is silent. Paired with `AudioManager` `_streakBreakClip` (short bright mostly-
  noise `Shatter`, 1400 Hz) on the same ×2+ condition.
- 🆕R6.3 **"FORE POUR RESPIRER !" hint** (F02) — centred just above the air bar (not the centre
  popup slot: that's for celebrations and sits over the avatar). Shows below 30 % air, hides at 35 %
  (hysteresis, so drilling at the edge doesn't flicker) or at 0. Eased fade; pulse speeds up as the
  tank empties, amber → red under 15 %; a scale **pop on every air gain** while shown (detected in
  `RefreshAir` as air > last value — a drill, capsule or burst all count), so the player sees the
  advice working. Dark pill + text outline: bare amber text was unreadable over amber/pink blocks.

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
  - **🆕R6.5 Air capsule shimmer (BoardView):** AirCapsule cells get a second `DiamondShine`
    instance (`EnsureCapsuleMaterial`) — cyan, softer and slower than the diamond's, so the two
    never twinkle alike. The tile itself is the procedural "AIR" pill from `Editor/CapsuleTileGenerator.cs`.
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

> **🔄 Reworked 2026-10-05 (dev playtest: "les cubes de fin de niveau manquent de feedback, les
> couleurs sont bizarres").** Screenshot confirmed it: the full-width additive wash bleached every
> block in the band (pink → peach, amber → yellow, bombs pale, holes khaki) — it read as a bug, not a
> destination, and nothing marked the actual win row. Now (`BoardView.BuildExitZone`):
> - **Blocks are never tinted.** Light only in the **empty** cells of the band — one additive sprite
>   per cell, toggled in `OnCellChanged`, with a bottom-bright gradient sprite (the `ExitGlow` shader
>   now samples `_MainTex`) so it reads as light rising out of the exit.
> - **Checkered gold finish line** on the top edge of row `Height - WinDepthFromFloor` (sortingOrder 6).
> - **Gold ▼ arrows** in both side margins (3 per side) with a light running down toward the line;
>   faster and brighter as the avatar closes in (`exitApproachRows`, 12). `Init` takes the
>   `AvatarModel` (optional) for that.
> - **On reach** (`PlayExitReached`, from `GameBootstrap.BeginLevelEnd`): the line flashes white and
>   swells, the holes flare, 28 gold sparks arc up; HUD "SORTIE !" (`ShowExitReached`). The tutorial
>   win now also plays the level-complete jingle (`AudioManager.PlayLevelComplete`, made public) —
>   before, only campaign levels had it.
> Screenshot-verified (approach + reach). View only.

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

> **🔄R6.4 (F04 — "je voyais pas les ennemis") — visibility pass.** Diagnosis from the code: enemies
> were plain unit squares (same silhouette as every block), tinted dark green (≈ teal ColorB) and
> orange (≈ amber ColorA and the red-orange bomb), and dormant ones sat at 40 % alpha ON TOP of a solid
> block — effectively invisible. Changes:
> - **`EnemySprites`** (new, procedural, point-filtered 16×16): Crawler = wide bug with legs and
>   antennae, Boomer = round body with a lit fuse; black 1 px outline so they separate from any block;
>   **eyes shut while dormant, open once active**. Colours: **lime Crawler, violet Boomer** — no block
>   uses either, and they match the R5.14 kill / BOOM popups. Colour is baked into the texture (the
>   eyes must stay white), so `SpriteRenderer.color` only carries grey/alpha.
> - **Dormant** = `dormantTint` grey at 0.95 alpha + a slow 5 % breath (alive, not a tile).
>   **Active** = full colour + a brightness pulse (0.82 ↔ 1) on top of the R5.17 shuffle / swell.
> - **"!" alert** (sortingOrder 9, a sibling so it doesn't inherit the body's pulse): bounces in with
>   overshoot for 1.1 s when an enemy wakes; **blinks over an active Crawler on the avatar's row within
>   3 columns** (Boomers never threaten — their blast never hurts the avatar). The rising edge fires
>   `EnemyView.DangerStarted` → `AudioManager.PlayEnemyAlert()`. `Init` now takes the `AvatarModel`
>   (optional; no avatar = no danger "!").
> - **VfxManager**: Crawler death particles → lime, Boomer death → pale violet, Boomer halo → violet
>   (a lit bomb and a Boomer must not glow the same orange).
> - Verified by screenshot (UnityMCP) on open air AND on amber/pink blocks, dormant and active. First
>   pass had a 2 px "!" that read as a hairline → widened to 3 px, scale 0.9.
