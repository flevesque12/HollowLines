using System;
using HollowLines.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace HollowLines.View
{
    /// <summary>
    /// Scene entry point — boots all Core systems, wires events, owns the game loop.
    /// One component on one empty GameObject; no FindObjectOfType anywhere.
    ///
    /// Persistent systems (ScoreSystem, AirSystem, HealthSystem, CampaignManager) are created
    /// once in Awake and survive level transitions.
    ///
    /// Grid-dependent systems (GridModel, AvatarModel, GravitySystem, CollapseSystem,
    /// BombSystem, ChainTracker) are fully rebuilt by LoadLevel() on each transition.
    ///
    /// Tick order (CLAUDE.md §7): avatar → depth → campaign → bombs → collapse → gravity
    /// → chain → health → air.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Header("— HUD —")]
        [Tooltip("UI Toolkit Panel Settings. Créer via Assets > Create > UI Toolkit > Panel Settings.")]
        [SerializeField] private PanelSettings hudPanelSettings;

        [Header("— Debug / Testing —")]
        [Tooltip("Disable air drain — the air bar stays full so you can test a board without suffocating. Restores still fire; they just don't matter.")]
        [SerializeField] private bool disableAir = false;

        [Tooltip("Invincibility — the avatar ignores every crush and blast (hearts never drop). No teleport-to-spawn, no shake: you just pass through.")]
        [SerializeField] private bool invincible = false;

        [Header("— Campaign —")]
        [Tooltip("Use Campaign mode (procedural boards per level). Uncheck to use the debug map below.")]
        [SerializeField] private bool useCampaign = true;

        [Tooltip("Starting level for Campaign mode (1–10).")]
        [Range(1, 10)]
        [SerializeField] private int startLevel = 1;

        [Tooltip("Play the hand-authored tutorial showcase (teaches every scoring method) before campaign level 1.")]
        [SerializeField] private bool playTutorialFirst = true;

        [Header("— Endless —")]
        [Tooltip("Boot straight into Endless mode (no win, play until death). The main menu can also start it.")]
        [SerializeField] private bool endlessMode = false;

        [Tooltip("Run seed. 0 = a fresh random seed every run. A fixed value replays the same well — this is what Daily Dig (R3.4) will set.")]
        [SerializeField] private int endlessSeed = 0;

        [Tooltip("Content rows per generated segment. The well is a chain of these; the player never sees the seam as a level boundary.")]
        [Range(20, 200)]
        [SerializeField] private int endlessSegmentRows = EndlessManager.DefaultSegmentRows;

        [Header("— Debug Map (useCampaign = false only) —")]
        [Tooltip("Rows top to bottom.\n'.' vide  A/B/C couleurs  H=Hard  S=Steel  P=AirCapsule  X=Bombe  D=Diamant")]
        [TextArea(8, 20)]
        [SerializeField] private string debugMap =
            ".......\n" +
            ".......\n" +
            ".......\n" +
            "AABBAAC\n" +
            "BC.ABBC\n" +
            "ABHP.PH\n" +
            "BB.HHAB\n" +
            "AXSBBSX\n" +
            "BC.SHSA\n" +
            "AAXBBXA\n" +
            "CA.ACAB\n" +
            "ABBCAAC\n" +
            "AABBCAA";

        [Header("— Spawn —")]
        [Tooltip("X should be the middle column (3 on the 7-wide v3 board) — the level 2/3 tutorials are authored there.")]
        [SerializeField] private Vector2Int spawnCell = new Vector2Int(3, 2);

        [Header("— Gravity Tuning —")]
        [Range(0.2f, 1.5f)]
        [SerializeField] private float wobbleDuration   = 0.6f;
        [Range(0.03f, 0.2f)]
        [SerializeField] private float fallStepInterval = 0.08f;

        [Header("— Camera —")]
        [Tooltip("How many grid rows fill the view vertically. Smaller = closer. The camera follows the avatar down the well.")]
        [Range(8f, 40f)]
        [SerializeField] private float cameraViewRows = 16f;

        [Tooltip("Camera follow smoothing. Higher = snappier.")]
        [Range(2f, 20f)]
        [SerializeField] private float cameraFollowSpeed = 8f;

        // ── Persistent systems (survive level transitions) ────────────────────
        private ScoreSystem     _scoreSystem;
        private AirSystem       _airSystem;
        private HealthSystem    _healthSystem;
        private CampaignManager _campaign;
        private EndlessManager  _endless;
        private StreakTracker   _streakTracker;
        private DepthTracker    _depthTracker;
        private DiamondSystem   _diamondSystem;

        // Intro tutorial state. While _inTutorial the board is the authored showcase, not a campaign
        // level; the campaign win check is bypassed and a depth-only tutorial win takes over.
        private bool _inTutorial;
        private bool _tutorialWon; // one-shot guard so the completion screen shows once

        // Endless state. The serialized toggle is only the boot default — the main menu can switch
        // modes at runtime, so every mode branch reads this field, never `endlessMode`.
        private bool _endlessMode;

        // Daily Dig: an endless run whose seed comes from the calendar day instead of the clock.
        // Everything else about the run is identical — that is the whole design (§6.6).
        private bool   _dailyDig;
        private string _dailyLabel;

        // Segment swaps are DEFERRED to the top of the next Update. SegmentExhausted fires from
        // inside the tick, and rebuilding the grid mid-frame would leave the rest of Update ticking
        // half-old, half-new systems.
        private bool _pendingSegmentAdvance;

        // ── Grid-dependent systems (rebuilt every LoadLevel) ──────────────────
        private GridModel     _grid;
        private GridPos       _spawn;
        private AvatarModel   _avatar;
        private GravitySystem _gravity;
        private CollapseSystem _collapse;
        private BombSystem    _bombSystem;
        private ChainTracker  _chainTracker;

        // ── Persistent views ──────────────────────────────────────────────────
        private GameInput        _input;
        private HUDView          _hud;
        private UIScreenManager  _screens;
        private CameraShake      _cameraShake;
        private AudioManager     _audio;

        // ── Rebuilt views ─────────────────────────────────────────────────────
        private GameObject _boardViewGo;
        private GameObject _avatarViewGo;

        // ─────────────────────────────────────────────────────────────────────
        // Unity lifecycle
        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            // ── Persistent systems ───────────────────────────────────────────

            _campaign = new CampaignManager(startLevel);
            _campaign.LevelCompleted    += lvl => _screens.ShowLevelComplete(lvl, _scoreSystem.Score);
            _campaign.CampaignCompleted += ()  => _screens.ShowCampaignComplete(_scoreSystem.Score);

            _endlessMode = endlessMode;

            // The showcase only fronts a real campaign run that begins at level 1.
            _inTutorial = !_endlessMode && useCampaign && playTutorialFirst && startLevel == 1;

            _endless = new EndlessManager(NewRunSeed(), endlessSegmentRows);
            // Depth is ABSOLUTE across segments here (§6.3) — a per-board DepthTracker would restart
            // the counter at every seam, so in endless it is EndlessManager that scores depth.
            _endless.DepthChanged     += depth => { if (_endlessMode) _scoreSystem.AwardDepth(depth); };
            _endless.DrainRateChanged += rate  => { if (_endlessMode) _airSystem.DrainRate = rate; };
            _endless.SegmentExhausted += _     => _pendingSegmentAdvance = true;

            _scoreSystem = new ScoreSystem();
            _scoreSystem.OnScore += evt => Debug.Log($"[Score] {evt}  total={_scoreSystem.Score}");

            _streakTracker = new StreakTracker();

            _depthTracker = new DepthTracker();
            _depthTracker.NewDepthReached += row => { if (!_endlessMode) _scoreSystem.AwardDepth(row); };

            // R4: however a diamond is freed (drill, bomb blast, burst shockwave), it always
            // funnels through DiamondSystem.NotifyCollected — so the score award lives here once,
            // instead of being duplicated at every collection call site (§7).
            _diamondSystem = new DiamondSystem();
            _diamondSystem.DiamondCollected += (collected, total) => _scoreSystem.AwardDiamond();

            _airSystem = new AirSystem();
            _airSystem.AirDepleted += () => EndRun("Air épuisé");

            _healthSystem = new HealthSystem();
            _healthSystem.HealthDepleted += () => EndRun("Plus de cœurs");

            // ── Persistent views ─────────────────────────────────────────────

            var inputGo = new GameObject("GameInput");
            inputGo.transform.SetParent(transform, false);
            _input = inputGo.AddComponent<GameInput>();

            var hudGo = new GameObject("HUD");
            hudGo.transform.SetParent(transform, false);
            _hud = hudGo.AddComponent<HUDView>();
            _hud.Init(_scoreSystem, _airSystem, _healthSystem, null,
                      _streakTracker, _depthTracker,
                      useCampaign ? _campaign : null, hudPanelSettings,
                      _diamondSystem);
            _hud.SetDepthSource(_endlessMode ? _endless : null);

            var screensGo = new GameObject("Screens");
            screensGo.transform.SetParent(transform, false);
            _screens = screensGo.AddComponent<UIScreenManager>();
            _screens.Init(_scoreSystem, _campaign, hudPanelSettings,
                onPlay:            StartGame,
                onRestart:         RestartLevel,
                onNextLevel:       AdvanceLevel,
                onRestartCampaign: RestartCampaign,
                onQuit:            QuitGame,
                onPlayEndless:     StartEndless,
                onPlayDaily:       StartDailyDig,
                onMainMenu:        ReturnToMainMenu);
            _screens.SetEndless(_endlessMode ? _endless : null);

            var audioGo = new GameObject("AudioManager");
            audioGo.transform.SetParent(transform, false);
            _audio = audioGo.AddComponent<AudioManager>();
            _audio.InitPersistent(_airSystem, _healthSystem, _campaign, _streakTracker);

            // ── First board load ─────────────────────────────────────────────
            LoadLevel();
        }

        private void Update()
        {
            // Deferred segment swap (endless): the request came from inside last frame's tick.
            if (_pendingSegmentAdvance)
            {
                _pendingSegmentAdvance = false;
                AdvanceEndlessSegment();
            }

            float dt = Time.deltaTime;

            // Canonical tick order (CLAUDE.md §7):
            _avatar.Tick(dt);                                                   // 1. avatar gravity
            _depthTracker.NotifyPosition(_avatar.Position);                     // 2. depth tracking
            if (_endlessMode)                                                   // 3. no win — depth, drain ramp, seam
                _endless.NotifyAvatarPosition(_avatar.Position, _grid.Height);
            else if (_inTutorial)                                               //    win detection (depth only)
                CheckTutorialWin();
            else if (useCampaign)
                _campaign.NotifyAvatarPosition(_avatar.Position, _grid.Height, _diamondSystem.IsComplete); // R4 gate
            _bombSystem.Tick(dt, _avatar.Position);                             // 4. bomb fuses
            _collapse.Resolve(_avatar.Position);                                // 5. Perfect Clear detection
            _gravity.Tick(dt, _avatar.Position);                                // 6. chunk gravity + burst
            _chainTracker.Tick(dt);                                             // 7. chain close timer
            _healthSystem.Tick(dt);                                             // 8. health i-frames
            if (!disableAir)                                                     // 9. air drain (debug: skippable)
                _airSystem.Tick(dt);

            // Camera follows the avatar down the well (smoothed, frame-rate independent). CameraShake
            // lays its offset on top of this base, so the follow and the shake never fight.
            if (_cameraShake != null)
                _cameraShake.BasePosition = Vector3.Lerp(
                    _cameraShake.BasePosition, CameraTarget(),
                    1f - Mathf.Exp(-cameraFollowSpeed * dt));
        }

        // ─────────────────────────────────────────────────────────────────────
        // Level loading
        // ─────────────────────────────────────────────────────────────────────

        private void LoadLevel()
        {
            // Tear down previous board views (queued for end-of-frame).
            if (_boardViewGo != null)  Destroy(_boardViewGo);
            if (_avatarViewGo != null) Destroy(_avatarViewGo);

            // Build new grid. The tutorial showcase is a fixed authored board; otherwise the
            // campaign level (or the debug map) drives it. Drain/wobble already read CurrentLevel = 1,
            // which gives the tutorial the gentle onboarding values (4 %/s, 0.8 s wobble).
            string[] rows = _endlessMode
                ? _endless.BuildCurrentSegment()
                : _inTutorial
                    ? StrateGenerator.TutorialBoard()
                    : useCampaign
                        ? _campaign.BuildCurrentBoard()
                        : debugMap.Replace("\r", "").Split('\n');
            _grid  = GridModel.FromStringMap(rows);

            // Clamp: the serialized spawn predates the v3 width change, and a debug map can be
            // any width. Out-of-bounds would throw on the first Get().
            _spawn = new GridPos(
                Mathf.Clamp(spawnCell.x, 0, _grid.Width  - 1),
                Mathf.Clamp(spawnCell.y, 0, _grid.Height - 1));

            // Streak and depth are per-board: a new level starts both from scratch.
            _streakTracker.Reset();
            _depthTracker.Reset();

            // R4: diamonds are per-board too. Count 'D' straight off the generated rows rather than
            // a separate lookup (e.g. StrateGenerator.DiamondCountForLevel) — that way the gate
            // always matches what is actually on the board, whatever the source (campaign, endless,
            // debug map).
            int totalDiamonds = 0;
            foreach (string row in rows)
                foreach (char c in row)
                    if (c == 'D') totalDiamonds++;
            _diamondSystem.Init(totalDiamonds);

            // Campaign levels drain air slower than the endless default. In endless the rate is
            // owned by the depth ramp, so a new segment inherits whatever the descent earned.
            _airSystem.DrainRate = _endlessMode
                ? _endless.DrainRate
                : useCampaign
                    ? CampaignManager.DrainRateForLevel(_campaign.CurrentLevel)
                    : AirSystem.DefaultDrainRate;

            // Rebuild grid-dependent systems.
            _avatar       = new AvatarModel(_grid, _spawn);
            _gravity      = new GravitySystem(_grid)
                            {
                                // Campaign levels 1-3 telegraph longer; debug boards use the Inspector value.
                                WobbleDuration = _endlessMode
                                    ? EndlessManager.WobbleDuration
                                    : useCampaign
                                        ? CampaignManager.WobbleDurationForLevel(_campaign.CurrentLevel)
                                        : wobbleDuration,
                                FallStepInterval = fallStepInterval
                            };
            _collapse     = new CollapseSystem(_grid);
            _bombSystem   = new BombSystem(_grid, _collapse);
            _chainTracker = new ChainTracker(_collapse, _gravity);

            // Settle the freshly generated board into a stable rest state BEFORE play. Generated
            // boards carry unsupported mass over gaps; without this the first ticks would wobble,
            // fall and burst it — the shockwave cascade demolishing the level before the player
            // moves (R2.8d). Settle() drops everything silently: no burst, no shockwave, no crush.
            _gravity.Settle();

            // ── Streak tracking (CLAUDE.md §7) ───────────────────────────────
            // Every drill pays, scaled by the same-color run. Capsules are streak-neutral,
            // so grabbing air mid-streak is never a punishment.
            _avatar.Drilled += (drilledCell, oldType) =>
            {
                _streakTracker.NotifyDrill(oldType);
                _scoreSystem.AwardDrill(_streakTracker.CurrentStreak, _streakTracker.CurrentColor);
                if (oldType == CellType.AirCapsule) _airSystem.RestoreCapsule();
                if (oldType == CellType.Diamond) _diamondSystem.NotifyCollected(drilledCell); // R4
                _bombSystem.NotifyDrilled(drilledCell);
            };

            // ── Chunk gravity + burst ────────────────────────────────────────
            _gravity.ChunkLanded += chunk => _bombSystem.NotifyChunkLanded(chunk);

            _gravity.ChunkBurst += (cells, fallDistance, _) =>
            {
                _scoreSystem.AwardBurst(cells.Count, fallDistance);
                _airSystem.RestoreBurst();
                _cameraShake.Shake(0.10f + 0.02f * cells.Count, 0.25f);
            };

            // Shockwave side effects: capsules freed, diamonds freed, bombs lit, avatar caught in the blast.
            _gravity.AirCapsuleLiberated += _ => _airSystem.RestoreCapsule();
            _gravity.DiamondLiberated    += pos => _diamondSystem.NotifyCollected(pos); // R4
            _gravity.BombArmedByBurst    += pos => _bombSystem.ArmBombAt(pos);
            _gravity.AvatarHitByBurst    += OnAvatarCrushed;
            _gravity.AvatarCrushed       += _ => OnAvatarCrushed();

            // ── Bombs ────────────────────────────────────────────────────────
            _bombSystem.BombScored += (destroyed, chainMult) =>
            {
                _scoreSystem.AwardBomb(destroyed, chainMult);
                if (chainMult > 1) _airSystem.RestoreBombChain(chainMult);
            };
            _bombSystem.AirCapsuleLiberated += _ => _airSystem.RestoreCapsule();
            _bombSystem.DiamondLiberated    += pos => _diamondSystem.NotifyCollected(pos); // R4
            _bombSystem.AvatarHitByBlast    += _ => OnAvatarCrushed();
            _bombSystem.BombArmed           += pos => Debug.Log($"[Bomb] Armée en {pos}");
            _bombSystem.BombExploded        += pos => { _cameraShake.Shake(0.35f, 0.3f); Debug.Log($"[Bomb] BOOM en {pos}"); };

            // ── Perfect Clear (rare jackpot, no longer the core loop) ─────────
            _collapse.PerfectClear  += _ =>
            {
                _scoreSystem.AwardPerfectClear(_chainTracker.CurrentChain);
                _airSystem.RestorePerfectClear();
                _cameraShake.Shake(0.12f, 0.2f);
            };
            _collapse.AvatarCrushed += _ => OnAvatarCrushed();

            // Point HUD and audio at the new grid-dependent systems.
            _hud.RewireChain(_chainTracker);
            _hud.OnLevelLoaded();
            _audio.Rewire(_avatar, _collapse, _chainTracker, _bombSystem, _gravity);

            // Rebuild board views.
            _boardViewGo = new GameObject("BoardView");
            _boardViewGo.transform.SetParent(transform, false);
            // Exit glow only makes sense where there's a fixed floor to signal (campaign/tutorial
            // share the same depth-only win threshold, §7). Endless has no floor, and the debug
            // map has no win check at all (useCampaign gates that branch in Update()).
            bool showExitGlow = !_endlessMode && (useCampaign || _inTutorial);
            _boardViewGo.AddComponent<BoardView>().Init(_grid, _gravity, showExitGlow);

            _avatarViewGo = new GameObject("AvatarView");
            _avatarViewGo.transform.SetParent(_boardViewGo.transform, false);
            var avatarView = _avatarViewGo.AddComponent<AvatarView>();
            avatarView.Init(_avatar);
            _avatarViewGo.AddComponent<AvatarController>().Init(_avatar, _input);

            var vfxGo = new GameObject("VfxManager");
            vfxGo.transform.SetParent(_boardViewGo.transform, false);
            vfxGo.AddComponent<VfxManager>().Init(_avatar, _collapse, _chainTracker, _bombSystem, _grid,
                                                  _gravity, _streakTracker, avatarView);

            FrameCamera();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Screen callbacks
        // ─────────────────────────────────────────────────────────────────────

        // Main menu → begin play. At boot the first board is already built and frozen behind the
        // title screen (LoadLevel ran in Awake), so starting is just lifting the overlay — but the
        // player can also reach this menu from a finished endless run, where the board behind is a
        // segment of the wrong mode and has to be rebuilt.
        private void StartGame()
        {
            if (_endlessMode) EnterCampaign();
            else              _screens.Hide();
        }

        /// <summary>Back to the title screen, keeping the (frozen) board behind it. Mode is chosen there.</summary>
        private void ReturnToMainMenu()
        {
            _cameraShake.StopShake();
            _screens.ShowMainMenu();
        }

        /// <summary>Switch out of endless and start the campaign from the top, tutorial included.</summary>
        private void EnterCampaign()
        {
            _cameraShake.StopShake();
            _endlessMode           = false;
            _dailyDig              = false;
            _dailyLabel            = null;
            _pendingSegmentAdvance = false;
            _hud.SetDepthSource(null);      // back to the per-board DepthTracker
            _screens.SetEndless(null);

            _campaign.Reset(startLevel);
            _inTutorial  = useCampaign && playTutorialFirst && startLevel == 1;
            _tutorialWon = false;

            _scoreSystem.Reset();
            _airSystem.Reset();
            _healthSystem.Reset();
            _screens.Hide();
            LoadLevel();
        }

        /// <summary>
        /// Main menu → Endless. Switches mode at runtime (the serialized toggle is only the boot
        /// default), then starts a fresh run on a new seed.
        /// </summary>
        private void StartEndless() => EnterEndless(daily: false);

        /// <summary>Main menu → Daily Dig: the same endless run, seeded by today's date (§6.6).</summary>
        private void StartDailyDig() => EnterEndless(daily: true);

        private void EnterEndless(bool daily)
        {
            _endlessMode = true;
            _inTutorial  = false;
            _tutorialWon = false;
            _dailyDig    = daily;
            _dailyLabel  = daily ? DailyDig.LabelFor(DateTime.UtcNow) : null;
            _hud.SetDepthSource(_endless);
            _screens.SetEndless(_endless);
            _screens.Hide();
            StartEndlessRun();
        }

        /// <summary>Fresh endless run: new well, new seed (unless one is pinned), everything reset.</summary>
        private void StartEndlessRun()
        {
            _cameraShake.StopShake();
            _pendingSegmentAdvance = false;
            _endless.Reset(NewRunSeed());
            _scoreSystem.Reset();
            _airSystem.Reset();
            _healthSystem.Reset();
            LoadLevel();
        }

        /// <summary>
        /// The seam. The avatar reached the bottom of the segment, so the next one is loaded with
        /// score, air and hearts CARRIED OVER — this is one continuous run, not a level transition.
        /// The cut lands in open air (board floor → spawn zone of the next board); the fade-in is
        /// what keeps it from reading as a teleport.
        /// </summary>
        private void AdvanceEndlessSegment()
        {
            _cameraShake.StopShake();
            _endless.AdvanceSegment();
            LoadLevel();
            _screens.PlayFadeIn(SegmentFadeSeconds);
            Debug.Log($"[Endless] Segment {_endless.SegmentIndex} — profondeur {_endless.Depth} m, drain {_endless.DrainRate:0.0} %/s");
        }

        private const float SegmentFadeSeconds = 0.4f;

        /// <summary>
        /// Daily Dig pins the seed to today's UTC date (same well for every player, §6.6);
        /// otherwise a serialized `endlessSeed` pins it for testing, and 0 means a fresh well
        /// each run. Positive range only — see the int.MinValue note in `EndlessSegment`.
        /// </summary>
        private int NewRunSeed()
        {
            if (_dailyDig)          return DailyDig.SeedFor(DateTime.UtcNow);
            if (endlessSeed != 0)   return endlessSeed;
            return UnityEngine.Random.Range(1, int.MaxValue);
        }

        private void RestartLevel()
        {
            if (_endlessMode)
            {
                // No level to restart in endless — "Recommencer" means a whole new descent.
                _screens.Hide();
                StartEndlessRun();
                return;
            }

            _cameraShake.StopShake();
            _campaign.RestartLevel();
            _scoreSystem.Reset();
            _airSystem.Reset();
            _healthSystem.Reset();
            _screens.Hide();
            LoadLevel();
        }

        // Tutorial win = reach the bottom, same depth rule as a campaign level, but it must NOT
        // advance the campaign — the real run still starts at level 1.
        private void CheckTutorialWin()
        {
            if (_tutorialWon) return;
            if (_avatar.Position.Y < _grid.Height - CampaignManager.WinDepthFromFloor) return;

            _tutorialWon = true;
            _cameraShake.StopShake();
            _screens.ShowTutorialComplete(_scoreSystem.Score);
        }

        private void AdvanceLevel()
        {
            _cameraShake.StopShake();

            // Finishing the tutorial: drop the showcase and start the real campaign at level 1,
            // with a clean slate (the practice score does not carry into the run).
            if (_inTutorial)
            {
                _inTutorial  = false;
                _tutorialWon = false;
                _scoreSystem.Reset();
                _airSystem.Reset();
                _healthSystem.Reset();
                _screens.Hide();
                LoadLevel(); // now builds campaign level 1 (CurrentLevel is still 1)
                return;
            }

            _campaign.Advance(); // fires CampaignCompleted if last level → ShowCampaignComplete()
            if (_campaign.IsCampaignComplete) return;
            _airSystem.Reset();
            _healthSystem.Reset();
            _screens.Hide();
            LoadLevel();
        }

        private void RestartCampaign()
        {
            _cameraShake.StopShake();
            _campaign.Reset();
            _scoreSystem.Reset();
            _airSystem.Reset();
            _healthSystem.Reset();
            _screens.Hide();
            LoadLevel();
        }

        private static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ─────────────────────────────────────────────────────────────────────
        // Gameplay handlers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The run is over (suffocated or out of hearts). Banks the Daily Dig result before the
        /// screen goes up, so the game-over card can show today's best next to this attempt.
        /// </summary>
        private void EndRun(string reason)
        {
            _cameraShake.StopShake();

            if (_dailyDig)
            {
                var record = DailyDigStore.Record(_dailyLabel, _endless.Depth, _scoreSystem.Score);
                _screens.ShowGameOver($"Défi du jour {_dailyLabel} — {reason}\n" +
                                      $"Meilleur du jour : {record.BestDepth} m ({record.Runs} descente(s))");
                return;
            }

            _screens.ShowGameOver(reason);
        }

        private void OnAvatarCrushed()
        {
            if (invincible) return;                     // debug: ignore all damage
            if (!_healthSystem.TryTakeDamage()) return; // i-frames active
            _cameraShake.Shake(0.3f, 0.35f);
            _audio.PlayCrush();
            Debug.Log($"[Health] Crush — {_healthSystem.Hearts} cœur(s) restant(s).");
            if (_healthSystem.IsAlive)
                _avatar.Teleport(_spawn);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Utilities
        // ─────────────────────────────────────────────────────────────────────

        private void FrameCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogError("[GameBootstrap] No main camera — tag one 'MainCamera'.");
                return;
            }
            cam.orthographic = true;
            // Show a fixed slice of the well instead of the whole board, so the avatar reads big and
            // the camera can follow it down. Never zoom out past the board on a short level.
            cam.orthographicSize = Mathf.Min(cameraViewRows, _grid.Height + 1f) / 2f;
            cam.backgroundColor  = new Color(0.09f, 0.07f, 0.055f);

            if (_cameraShake == null)
                _cameraShake = cam.gameObject.AddComponent<CameraShake>();

            // Snap to the initial follow target so a level load doesn't swoop.
            Vector3 start = CameraTarget();
            _cameraShake.BasePosition = start;
            cam.transform.position    = start;
        }

        /// <summary>
        /// Where the un-shaken camera wants to be this frame: horizontally centered on the well,
        /// vertically tracking the avatar, clamped so the view never runs off the top or bottom of
        /// the board. On a board shorter than the view, it just centers the whole board.
        /// </summary>
        private Vector3 CameraTarget()
        {
            Camera cam = Camera.main;
            float half = cam != null ? cam.orthographicSize : cameraViewRows / 2f;
            float x = (_grid.Width - 1) / 2f;

            float top    = 0f;                      // world y of row 0 (BoardView.ToLocal: world y = -row)
            float bottom = -(_grid.Height - 1);     // world y of the last row
            float minCenter = bottom + half - 0.5f;
            float maxCenter = top    - half + 0.5f;

            float y = minCenter >= maxCenter
                ? -(_grid.Height - 1) / 2f                              // board shorter than view → center it
                : Mathf.Clamp(-_avatar.Position.Y, minCenter, maxCenter);

            return new Vector3(x, y, -10f);
        }
    }
}
