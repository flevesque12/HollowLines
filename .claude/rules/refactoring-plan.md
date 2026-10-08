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

### R3 — Endless mode  🟡 IN PROGRESS (R3.1-R3.4 ✅ — only the leaderboard left)

| Step | Task |
|---|---|
| R3.1 ✅ | StrateGenerator: progressive depth ramp. `EndlessSegment(seed, width, rows, startDepth)` resumes the ramp across segments (§6.3); `emptyRate=0` bug fixed (§15.6); **steady-state rates measured against the campaign and retuned — §15.7.** |
| R3.2 ✅ | Depth-based drain ramp — `EndlessManager.DrainRateForDepth` (GDD §7 curve, capped 10 %/s). |
| R3.3 ✅ | Endless game flow in GameBootstrap (no win, play until death): deferred segment swap + fade, drain ramp applied live, HUD/pause/game-over speak absolute depth, "Sans fin" on the main menu. **See §5.15.** |
| R3.4 ✅ | Daily Dig: one well per calendar day, same for every player. `DailyDig.SeedFor(date)` (Core, §6.6) + "Défi du jour" on the main menu + local best-of-day in PlayerPrefs (`DailyDigStore`). |
| R3.5 | Leaderboard API (ASP.NET Core / PostgreSQL, separate project) |

### R4 — Diamonds  ✅ COMPLETE (2026-07-28)

| Step | Task | Tests |
|---|---|---|
| R4.1 ✅ | CellType: add Diamond (value 9, map char `D`). | 6 new |
| R4.2 ✅ | DiamondSystem: new pure C# system (§6.4). | 7 new |
| R4.3 ✅ | GravitySystem: shockwave hitting Diamond → DiamondLiberated event. | 1 new |
| R4.4 ✅ | BombSystem: blast hitting Diamond → DiamondLiberated event. | 2 new |
| R4.5 ✅ | ScoreSystem: AwardDiamond() → +150 pts flat. | 3 new |
| R4.6 ✅ | CampaignManager: `NotifyAvatarPosition` gained `bool diamondsComplete = true`. | 3 new |
| R4.7 ✅ | StrateGenerator: diamond placement (§5.8). | 9 new |
| R4.8 ✅ | GameBootstrap: wired all three collection sources + diamond gate check. | Integration — 258/258 |
| R4.9 ✅ | HUDView: diamond counter "💎 2/5" (campaign) / "💎 7" (Endless). | Visual check |
| R4.10 ✅ | VfxManager + AudioManager: diamond collect sparkle + chime + `DiamondShine` shader. | Visual/audio check |

### R5 — Enemies + Arcade polish  ✅ COMPLETE (2026-09-01)

> **v3.1 arcade pivot** simplifies R5: 2 enemy types (Crawler + Boomer) instead of 3, plus
> arcade-feel changes (streak vertical-only, drill air restore, bomb fuse 1.5s, score gate).

| Step | Task | Tests |
|---|---|---|
| R5.1 ✅ | StreakTracker: `DrillDirection` param, vertical-only streak | 3 new, 263/263 |
| R5.2 ✅ | AirSystem: `RestoreDrill()` → +0.5% air per drill | 3 new, 266/266 |
| R5.3 ✅ | BombSystem: `FuseDuration` 2.5 s → 1.5 s | 0 new (constant-only), 266/266 |
| R5.4 ✅ | CampaignManager: score gate replaces diamond gate | 7 new, 270/270 |
| R5.5 ✅ | EnemyType enum + EnemyEntity struct | Data only, 270/270 |
| R5.6 ✅ | EnemySystem: core lifecycle (spawn, activate, kill) | 19 new, 289/289 |
| R5.7 ✅ | EnemySystem: Crawler movement (lateral bounce, 0.8s interval) | 5 new, 294/294 |
| R5.8 ✅ | EnemySystem: Boomer death explosion (radius 1, chain cap 3) | 9 new, 303/303 |
| R5.9 ✅ | EnemySystem: activation rules (drill, burst, viewport) | 6 new, 309/309 |
| R5.10 ✅ | EnemySystem: avatar collision (active Crawler only) | 5 new, 314/314 |
| R5.11 ✅ | ScoreSystem: `AwardEnemyKill` + `AwardBoomerBlast` | 10 new, 324/324 |
| R5.12 ✅ | GameBootstrap: wire all v3.1 changes + enemy events | Integration, 324/324 |
| R5.13 ✅ | StrateGenerator: campaign enemy placement (§9 table) | 9 new, 333/333 |
| R5.14 ✅ | HUDView: enemy kill + Boomer blast popups | Visual check |
| R5.15 ✅ | VfxManager: Crawler/Boomer death effects, wake flash, Boomer halo | Visual check |
| R5.16 ✅ | AudioManager: enemy SFX (chirp, crunch, boop, boom) | Audio check |
| R5.17 ✅ | EnemyView: enemy sprites on grid (dormant/active states) | Visual check, 333/333 |

### R6 — Feedback fixes (player testing Sept 2026)  🟡 PLANNED

> **Source:** `FEEDBACK-COMPILATION.md` (16 items, F01–F16).
> **Detailed prompts:** `PROMPTS-CLAUDE-CODE-R6.md` (R6.1–R6.18).
> **Prerequisite:** R5 complete ✅.

#### Sprint 1 — "Je comprends pourquoi je suis mort" (P0 — Critical)

| Step | Task | Feedback | Systems touched |
|---|---|---|---|
| R6.1 | **DeathTracker** + death recap screen | F01 | 🆕 `Core/DeathTracker.cs`, `View/DeathRecapView.cs`, GameBootstrap |
| R6.2 | **AirSystem** start buffer + reduced initial drain | F02 | `Core/AirSystem.cs`, `CampaignManager` |
| R6.3 | **Drill-to-breathe popup** when air < 30% | F02 | `View/HUDView.cs` |
| R6.4 | **Enemy visibility** — unique color, pulse, "!" icon, distinct SFX | F04 | `View/EnemyView.cs`, `View/VfxManager.cs`, `View/AudioManager.cs` |
| R6.5 | **Air capsule icon** redesign | F05 | `View/BoardView.cs`, `Resources/Tiles/` |

#### Sprint 2 — "Je vois le score" (P1 — Important)

| Step | Task | Feedback | Systems touched |
|---|---|---|---|
| R6.6 | **Score popups on drilled blocks** | F07 | `View/HUDView.cs` or `View/VfxManager.cs` |
| R6.7 | **Momentum counter** — 🔄v3.2 replaces streak counter (bigger, tier-based) | F07 | `View/HUDView.cs`, `View/VfxManager.cs` |
| R6.8 | **D-pad movement mapping** | F11 | `View/GameInput.cs` |
| R6.9 | **Volume control menu** | F10 | 🆕 `View/OptionsMenu.cs`, `View/AudioManager.cs`, `View/UIScreenManager.cs` |

#### Sprint 3 — "J'apprends les mécaniques" (P2 — Important)

| Step | Task | Feedback | Systems touched |
|---|---|---|---|
| R6.10 | **Puzzle tutorial system** | F08, F09 | 🆕 `Core/PuzzleBoard.cs`, 🆕 `View/PuzzleTutorialView.cs`, `View/UIScreenManager.cs` |
| R6.11 | **XP progression system** (🔄v3.2: may be simplified — momentum tiers handle skill expression; XP becomes tutorial-only) | F06 | 🆕 `Core/ProgressionSystem.cs`, `View/UIScreenManager.cs`, PlayerPrefs |
| R6.12 | **Help page in-game** | F16 | 🆕 `View/HelpScreenView.cs`, `View/UIScreenManager.cs` |
| R6.13 | **Blast radius visual feedback** | F03 | `View/VfxManager.cs`, `Core/BombSystem.cs` |

#### Sprint 4 — Polish (P3 — Nice to have)

| Step | Task | Feedback | Systems touched |
|---|---|---|---|
| R6.14 | **Level end delay** — cascades finish before score screen | F14 | `GameBootstrap`, `View/UIScreenManager.cs` |
| R6.15 | **Multi-monitor fix** | F12 | Unity PlayerSettings / startup script |
| R6.16 | **Multi-input fix** | F13 | `View/GameInput.cs` |
| R6.17 | **Void-line yellow review** | F15 | `View/BoardView.cs` |
| R6.18 | **Death replay** — 3 s slow-motion replay | F01 | 🆕 `View/DeathReplaySystem.cs`, `GameBootstrap` |

> **Notes:**
> - Sprint 1 is blocking: the game is unplayable for new players without death recap and longer parties.
> - R6.2 (air buffer) must be balanced against the existing drain curve (§15) — playtest after implementation.
> - R6.10 (puzzle tutorial) is the largest single item; may span multiple sessions.
> - R6.18 (death replay) is the most technically complex — requires frame buffering or state snapshot.
> - Endless enemy placement (deferred from R5.13) should be addressed as part of R6 or as R6.19.

### R7 — Drill Momentum (v3.2)  🟡 PLANNED

> **Source:** `drill-momentum.md` (directives M1–M10).
> **Prerequisite:** R5 complete ✅. Can run in parallel with R6.
> **Design doc:** `hollow-lines-drill-momentum-directives.docx`.

#### Sprint 1 — Core Momentum (replaces StreakTracker)

| Step | Task | Tests |
|---|---|---|
| R7.1 ✅ | `MomentumTracker`: 4 tiers, 0.8s window, PowerDrill event, color bonus | 24 new, 400/400 |
| R7.2 ✅ | `GrazeSystem`: adjacent-danger detection, +50 pts, +0.3s timer, 0.5s cooldown | 13 new, 413/413 |
| R7.3 ✅ | `AvatarModel`: freefall cell counter + `FreefallCell` event | 8 new, 421/421 |
| R7.4 | `AirSystem`: `IsDangerZone` flag (air < 15%) | TBD |
| R7.5 | `ScoreSystem`: new `AwardDrill(float)`, `AwardGraze()`, `AwardFreefall()`, momentum×cascade×danger formula | TBD |

#### Sprint 2 — Wiring + Fissures

| Step | Task | Tests |
|---|---|---|
| R7.6 | `GameBootstrap`: rewire Drilled handler, momentum tick, graze events, freefall, Power Drill orchestration | Integration |
| R7.7 | ⚡D5 `FissureTracker`: `Dictionary<GridPos, int>`, Tier 2+ drill → +1 fissure adjacents, 2nd fissure → `FissureBroke` event + vide la cellule | TBD |
| R7.8 | Power Drill: double-drill + mini shockwave (radius 1), cycle reset to Tier 1 | TBD |

#### Sprint 3 — View Layer

| Step | Task |
|---|---|
| R7.9 | `HUDView`: momentum counter + tier indicator, cascade ×N popup, graze popup, danger badge |
| R7.10 | `VfxManager`: Tier 1 trail, Tier 2 fissure overlay, Tier 3 flash, Danger Zone vignette |
| R7.11 | `AudioManager`: tier pitch, T2 rumble, T3 impact, graze ting, danger heartbeat |

#### Sprint 4 — Migration + Balance

| Step | Task |
|---|---|
| R7.12 | Delete `StreakTracker.cs`, update all streak tests → momentum tests |
| R7.13 | `StrateGenerator`: level 2 tutorial zone for momentum (replace color streak tutorial vein) |
| R7.14 | Campaign rebalance: score gates vs. momentum economy (playtest harness) |
| R7.15 | Endless rebalance: momentum progression vs. drain ramp |

> **Notes:**
> - R7.1–R7.5 are pure Core — no Unity dependency, fully testable with NUnit.
> - R7.6 is the integration pivot: once wired, the old streak system is dead.
> - R7.12 is the cleanup — only after R7.6 is verified.
> - R7.14–R7.15 require the playtest harness (`tools/playtest/`) to be updated for momentum.
