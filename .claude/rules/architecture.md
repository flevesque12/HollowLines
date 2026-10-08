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
    MomentumTracker.cs    — 🔄v3.2 Replaces StreakTracker. Time-based momentum (0.8s window),
                            4 tiers (×1/×2/×4/×6), Power Drill at Tier 3. See drill-momentum.md §M1
    GrazeSystem.cs        — 🆕v3.2 Near-miss scoring (+50 pts for drilling adjacent to danger). §M3.2
    FissureTracker.cs     — 🆕v3.2 ⚡D5 Tier 2+ fissure state (Dictionary<GridPos, int>), 2nd fissure
                            breaks block → FissureBroke event. See drill-momentum.md §M2
    DepthTracker.cs        — 🆕 Deepest row tracking (NotifyPosition → NewDepthReached event)
    ScoreSystem.cs        — 🔄v3.2 REWRITTEN: momentum, burst, bomb, depth, graze, freefall, danger zone scoring
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
    VfxManager.cs         — ✅R4 Burst debris, shockwave ripple, 🔄v3.2 momentum trail/fissures/power drill, chain flash, bomb fuse
                            telegraph, diamond collect sparkle (§5.10);
                            ✅R5.15 Crawler death crunch, Boomer death burst + ripple, wake flash,
                            Boomer standing halo (reuses the fuse glow material)
    CameraShake.cs        — 🔄 Owns BasePosition; camera follow + additive shake (§5.13)
    SfxSynth.cs           — 🔄 Procedural clip generator + Shatter() (layered noise + tone)
    AudioManager.cs       — ✅R4 🔄v3.2 Momentum tier pitch, burst shatter, rewarding bomb SFX, accelerating fuse
                            beep, diamond collect chime (§5.11);
                            ✅R5.16 Crawler chirp/crunch, Boomer boop, bass-heavy Boomer boom
    HUDView.cs            — ✅R5.14 enemy-kill + Boomer-blast popups (§5.9);
                            🔄v3.2 Momentum counter + tier indicator + cascade ×N + graze popup (§5.9);
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

## 7. Game loop wiring (v3 — target state)

### GameBootstrap.Awake() — subscriptions

```csharp
// ── 🔄v3.2 Momentum tracking + drill air restore ─────────────────
// ✅R7.6 SHIPPED order. Graze sits AFTER NotifyDrill (its +0.3 s would otherwise be overwritten by the
// window refresh) and BEFORE bombs/enemies (the bomb this drill lights / enemy it wakes is not a near miss).
// Power Drill completion is deferred to the END of the handler so AwardDrill reads the ×6 (⚡D1).
_avatar.Drilled += (pos, oldType, direction) =>
{
    _momentumTracker.NotifyDrill(oldType);                    // 🔄v3.2: time-based, no direction
    _scoreSystem.AwardDrill(_momentumTracker.Multiplier);     // 🔄v3.2: × cascade × danger inside ScoreSystem
    _airSystem.RestoreDrill();                                // v3.1: +0.5% air per drill
    _grazeSystem.NotifyDrill(pos, CollectDangerCells());      // 🆕v3.2: active enemies + armed bombs (⚡D6)
    if (oldType == CellType.AirCapsule) _airSystem.RestoreCapsule();
    if (oldType == CellType.Diamond) _diamondSystem.NotifyCollected(pos);  // R4
    _bombSystem.NotifyDrilled(pos);
    _enemySystem.NotifyAdjacentDrill(pos);                                 // R5.9 activation
};

// ── 🆕v3.2 Graze scoring ─────────────────────────────────────────
_grazeSystem.GrazeTriggered += () =>
{
    _scoreSystem.AwardGraze();                                // +50 pts flat
    _momentumTracker.ExtendTimer(0.3f);                       // +0.3s momentum window
};

// ── 🆕v3.2 Momentum events ───────────────────────────────────────
// ✅R7.6: sets _powerDrillPending; the Drilled handler calls CompletePowerDrill() at its end.
// R7.8 adds the double-drill + mini shockwave (radius 1) right before that call.
_momentumTracker.PowerDrillActivated += mult => _powerDrillPending = true;

// ── 🆕v3.2 Cascade + Danger Zone → ScoreSystem inputs (✅R7.6) ────
_chainTracker.LinkAdded      += link => _scoreSystem.CascadeMultiplier = link;  // per board
_chainTracker.ChainCompleted += _    => _scoreSystem.CascadeMultiplier = 1;
_airSystem.DangerZoneChanged += d    => _scoreSystem.DangerZone = d;            // once, in Awake

// ── 🆕v3.2 Freefall scoring ──────────────────────────────────────
_avatar.FreefallCell += () =>
{
    _scoreSystem.AwardFreefall();                             // +15 pts per void cell
    // Momentum timer does NOT tick during freefall (handled in MomentumTracker.Tick)
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
> - `AudioManager.OnDrilled` reads `MomentumTracker.CurrentTier` for its pitch. GameBootstrap
>   subscribes to `Drilled` (and calls `NotifyDrill`) in `LoadLevel()` *before* `_audio.Rewire()`,
>   so the audio sees the fresh tier. Don't reorder those two.
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

// 7b. 🆕v3.2 Momentum timer (skip during freefall — see drill-momentum.md §M3.3)
if (!_avatar.IsFalling) _momentumTracker.Tick(dt);

// 7c. 🆕v3.2 Graze cooldown
_grazeSystem.Tick(dt);

// 8. Health i-frames
_healthSystem.Tick(dt);

// 9. Air drain  (gated by the `disableAir` debug toggle — see §14)
//    ⚡D4: ALSO skipped during freefall — reward for finding void space
if (!disableAir && !_avatar.IsFalling)
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
