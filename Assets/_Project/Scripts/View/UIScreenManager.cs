using System;
using HollowLines.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UIElements;

namespace HollowLines.View
{
    /// <summary>
    /// Manages all full-screen overlay panels: pause, game over, level complete, campaign complete.
    /// Uses a single UIDocument with a dark overlay + swappable central panel.
    /// Time.timeScale is set to 0 whenever an overlay is visible and restored to 1 on Hide().
    /// </summary>
    public sealed class UIScreenManager : MonoBehaviour
    {
        private enum ScreenState { None, MainMenu, Paused, GameOver, LevelComplete, CampaignComplete }

        // ── Colors ────────────────────────────────────────────────────────────
        private static readonly Color ColBg      = new Color(0.09f, 0.07f, 0.055f);
        private static readonly Color ColOverlay = new Color(0.00f, 0.00f, 0.00f, 0.72f);
        private static readonly Color ColMuted   = new Color(0.70f, 0.70f, 0.70f);
        private static readonly Color ColAmber   = new Color(0.94f, 0.62f, 0.15f);
        private static readonly Color ColDanger  = new Color(0.95f, 0.25f, 0.10f);
        private static readonly Color ColDark    = new Color(0.06f, 0.05f, 0.04f);

        // ── System refs ───────────────────────────────────────────────────────
        private ScoreSystem     _score;
        private CampaignManager _campaign;
        private EndlessManager  _endless; // non-null only while endless mode is active
        private PanelSettings   _panelSettings;

        // ── Callbacks (provided by GameBootstrap) ─────────────────────────────
        private Action _onPlay;            // main menu → start the campaign
        private Action _onPlayEndless;     // main menu → start an endless run
        private Action _onPlayDaily;       // main menu → start today's Daily Dig
        private Action _onMainMenu;        // any end screen / pause → back to the title screen
        private Action _onRestart;
        private Action _onNextLevel;       // caller is responsible for Hide() or ShowCampaignComplete
        private Action _onRestartCampaign;
        private Action _onQuit;

        // ── UI elements ───────────────────────────────────────────────────────
        private VisualElement _overlay;
        private Label         _titleLabel;
        private Label         _bodyLabel;
        private VisualElement _buttonRow;
        private Button        _primaryButton;   // focused when a screen opens, so the gamepad has a start point

        // Run summary (§5.12) — depth headline, score, then the four highlight stats.
        private VisualElement _statsPanel;
        private Label         _depthValue;
        private Label         _scoreValue;
        private Label         _streakValue;
        private Label         _burstValue;
        private Label         _bombChainValue;
        private Label         _perfectValue;

        // ── Transition fade (endless segment seam) ────────────────────────────
        private VisualElement _fade;
        private float         _fadeTimer;
        private float         _fadeDuration;

        // ── State ─────────────────────────────────────────────────────────────
        private ScreenState _state = ScreenState.None;

        // ─────────────────────────────────────────────────────────────────────
        // Public API
        // ─────────────────────────────────────────────────────────────────────

        public void Init(ScoreSystem score, CampaignManager campaign, PanelSettings ps,
                         Action onPlay, Action onRestart, Action onNextLevel,
                         Action onRestartCampaign, Action onQuit, Action onPlayEndless = null,
                         Action onPlayDaily = null, Action onMainMenu = null)
        {
            _score             = score;
            _campaign          = campaign;
            _panelSettings     = ps;
            _onPlay            = onPlay;
            _onRestart         = onRestart;
            _onNextLevel       = onNextLevel;
            _onRestartCampaign = onRestartCampaign;
            _onQuit            = onQuit;
            _onPlayEndless     = onPlayEndless;
            _onPlayDaily       = onPlayDaily;
            _onMainMenu        = onMainMenu;
        }

        /// <summary>
        /// "Menu principal" — offered on every screen that ends or suspends a run. With three modes
        /// (campagne / sans fin / défi du jour) and only one menu, without it the player would have
        /// to relaunch the game to switch.
        /// </summary>
        private void AddMainMenuButton()
        {
            if (_onMainMenu == null) return;
            AddButton("Menu principal", primary: false, danger: false, () => _onMainMenu.Invoke());
        }

        /// <summary>
        /// Tell the screens which mode is running: an EndlessManager while endless is active, null
        /// for campaign. Endless has no level number and no win, so the pause and end screens speak
        /// depth instead.
        /// </summary>
        public void SetEndless(EndlessManager endless) => _endless = endless;

        /// <summary>
        /// Title screen shown at boot, over the (frozen) first board. "Jouer" starts the campaign.
        /// Uses the callbacks stored in Init, so it can be re-shown later (e.g. from a future menu).
        /// </summary>
        public void ShowMainMenu()
        {
            _state           = ScreenState.MainMenu;
            Time.timeScale   = 0f;
            _titleLabel.text = "HOLLOW LINES";
            _bodyLabel.text  = DailyMenuLine();
            HideRunSummary();
            ClearButtons();
            AddButton("Jouer",   primary: true,  danger: false, () => _onPlay?.Invoke());
            if (_onPlayEndless != null)
                AddButton("Sans fin", primary: false, danger: false, () => _onPlayEndless.Invoke());
            if (_onPlayDaily != null)
                AddButton("Défi du jour", primary: false, danger: false, () => _onPlayDaily.Invoke());
            AddButton("Quitter", primary: false, danger: true,  () => _onQuit?.Invoke());
            ShowOverlay();
        }

        /// <summary>
        /// Menu subtitle. Once the Daily Dig is reachable it also reports today's local best, so
        /// the player sees the day's challenge without opening it.
        /// </summary>
        private static string DailyMenuLine()
        {
            const string tagline = "Un petit foreur dans un puits sans fond.";
            string label = DailyDig.LabelFor(DateTime.UtcNow);
            var record = DailyDigStore.Load(label);

            return record.HasRun
                ? $"{tagline}\nDéfi du jour {label} — ton meilleur : {record.BestDepth} m"
                : $"{tagline}\nDéfi du jour {label} — pas encore tenté";
        }

        public void ShowGameOver(string reason)
        {
            _state           = ScreenState.GameOver;
            Time.timeScale   = 0f;
            _titleLabel.text = "GAME OVER";
            _bodyLabel.text  = reason;
            ShowRunSummary();
            ClearButtons();
            AddButton(_endless != null ? "Nouvelle descente" : "Recommencer",
                      primary: true,  danger: false, () => _onRestart?.Invoke());
            AddMainMenuButton();
            AddButton("Quitter",     primary: false, danger: true,  () => _onQuit?.Invoke());
            ShowOverlay();
        }

        public void ShowLevelComplete(int level, int totalScore)
        {
            _state           = ScreenState.LevelComplete;
            Time.timeScale   = 0f;
            _titleLabel.text = $"NIVEAU {level} COMPLÉTÉ !";
            _bodyLabel.text  = $"Score : {totalScore:N0}";
            HideRunSummary(); // mid-campaign: the run is not over yet
            ClearButtons();
            AddButton("Niveau suivant", primary: true,  danger: false, () => _onNextLevel?.Invoke());
            AddButton("Quitter",        primary: false, danger: true,  () => _onQuit?.Invoke());
            ShowOverlay();
        }

        public void ShowTutorialComplete(int totalScore)
        {
            _state           = ScreenState.LevelComplete;
            Time.timeScale   = 0f;
            _titleLabel.text = "TUTORIEL TERMINÉ !";
            _bodyLabel.text  = $"Entraînement : {totalScore:N0} pts  —  à toi de jouer.";
            HideRunSummary();
            ClearButtons();
            // Reuses the LevelComplete "next" callback: GameBootstrap.AdvanceLevel starts real level 1.
            AddButton("Commencer l'aventure", primary: true,  danger: false, () => _onNextLevel?.Invoke());
            AddButton("Quitter",              primary: false, danger: true,  () => _onQuit?.Invoke());
            ShowOverlay();
        }

        public void ShowCampaignComplete(int totalScore)
        {
            _state           = ScreenState.CampaignComplete;
            Time.timeScale   = 0f;
            _titleLabel.text = "BRAVO !";
            _bodyLabel.text  = "Campagne complétée !";
            ShowRunSummary();
            ClearButtons();
            AddButton("Rejouer",  primary: true,  danger: false, () => _onRestartCampaign?.Invoke());
            AddMainMenuButton();
            AddButton("Quitter",  primary: false, danger: true,  () => _onQuit?.Invoke());
            ShowOverlay();
        }

        public void ShowPause()
        {
            _state           = ScreenState.Paused;
            Time.timeScale   = 0f;
            _titleLabel.text = "PAUSE";
            _bodyLabel.text  = _endless != null
                ? $"Score : {_score.Score:N0}  —  Profondeur {_endless.Depth} m"
                : $"Score : {_score.Score:N0}  —  Niveau {_campaign.CurrentLevel}";
            HideRunSummary();
            ClearButtons();
            AddButton("Reprendre",   primary: true,  danger: false, () => Hide());
            // In endless there is no level to retry — "Recommencer" starts a whole new descent.
            AddButton(_endless != null ? "Nouvelle descente" : "Recommencer",
                      primary: false, danger: false, () => _onRestart?.Invoke());
            AddMainMenuButton();
            AddButton("Quitter",     primary: false, danger: true,  () => _onQuit?.Invoke());
            ShowOverlay();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Run summary
        // ─────────────────────────────────────────────────────────────────────

        private void HideRunSummary() => _statsPanel.style.display = DisplayStyle.None;

        /// <summary>Fill the end-of-run stat block from ScoreSystem (§5.12).</summary>
        private void ShowRunSummary()
        {
            _depthValue.text = $"{_score.MaxDepth:N0} m";
            _scoreValue.text = $"{_score.Score:N0} pts";

            _streakValue.text = _score.BestStreak > 0
                ? $"×{_score.BestStreak} {ColorName(_score.BestStreakColor)}".TrimEnd()
                : "—";

            _burstValue.text = _score.BiggestBurst > 0
                ? $"{_score.BiggestBurst}-cell ×{_score.BiggestBurstFallBonus}"
                : "—";

            _bombChainValue.text = _score.BestBombChain > 1
                ? $"{_score.BestBombChain}-chain"
                : "—";

            _perfectValue.text = _score.PerfectClears == 1
                ? "1 Perfect Clear"
                : $"{_score.PerfectClears} Perfect Clears";

            _statsPanel.style.display = DisplayStyle.Flex;
        }

        /// <summary>Palette names, so "×12 amber" matches the block the player was drilling.</summary>
        private static string ColorName(CellType type)
        {
            switch (type)
            {
                case CellType.ColorA: return "amber";
                case CellType.ColorB: return "teal";
                case CellType.ColorC: return "pink";
                default:              return "";
            }
        }

        public void Hide()
        {
            _state                 = ScreenState.None;
            _overlay.style.display = DisplayStyle.None;
            Time.timeScale         = 1f;
        }

        /// <summary>
        /// Fade the screen in FROM black — the mask for an endless segment swap. Not an overlay:
        /// it never touches Time.timeScale and never eats input, because the run keeps going.
        ///
        /// Fade-IN only, deliberately: the board is rebuilt instantly, so there is nothing to
        /// sequence a fade-out against, and the new segment simply rises out of the dark instead of
        /// the avatar appearing to teleport from the floor back to the ceiling.
        /// </summary>
        public void PlayFadeIn(float seconds)
        {
            if (_fade == null || seconds <= 0f) return;

            _fadeDuration          = seconds;
            _fadeTimer             = seconds;
            _fade.style.opacity    = 1f;
            _fade.style.display    = DisplayStyle.Flex;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Unity lifecycle
        // ─────────────────────────────────────────────────────────────────────

        private void Start()
        {
            if (_score == null)
            {
                Debug.LogError("[UIScreenManager] Init() was not called before Start(). Screens disabled.");
                enabled = false;
                return;
            }
            EnsureEventSystem();
            BuildUI();

            // Open on the title screen (over the frozen first board) when a Play callback was wired.
            if (_onPlay != null)
                ShowMainMenu();
        }

        private void Update()
        {
            TickFade();

            // Pause toggles on Esc (keyboard) or Start (gamepad). Only during live play or an
            // existing pause — never on the main menu or an end screen.
            bool pausePressed = (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                             || (Gamepad.current  != null && Gamepad.current.startButton.wasPressedThisFrame);
            if (!pausePressed) return;

            if (_state == ScreenState.None)
                ShowPause();
            else if (_state == ScreenState.Paused)
                Hide();
            // Pause does nothing during MainMenu / GameOver / LevelComplete / CampaignComplete.
        }

        /// <summary>Drives the segment-seam fade. Unscaled time, so pausing mid-fade can't freeze a black screen.</summary>
        private void TickFade()
        {
            if (_fadeTimer <= 0f) return;

            _fadeTimer -= Time.unscaledDeltaTime;
            if (_fadeTimer <= 0f)
            {
                _fadeTimer          = 0f;
                _fade.style.opacity = 0f;
                _fade.style.display = DisplayStyle.None;
                return;
            }

            _fade.style.opacity = Mathf.Clamp01(_fadeTimer / _fadeDuration);
        }

        /// <summary>
        /// UI Toolkit needs an EventSystem + InputSystemUIInputModule for keyboard/gamepad navigation
        /// of the overlay buttons (mouse works without it, a controller does not). The scene ships
        /// none, so create one at runtime with the default UI actions (Navigate/Submit/Cancel bound
        /// to D-pad + left stick / A / B out of the box).
        /// </summary>
        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;

            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            var module = go.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }

        // ─────────────────────────────────────────────────────────────────────
        // UI construction
        // ─────────────────────────────────────────────────────────────────────

        private void BuildUI()
        {
            if (_panelSettings == null)
            {
                _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                _panelSettings.scaleMode           = PanelScaleMode.ScaleWithScreenSize;
                _panelSettings.referenceResolution = new Vector2Int(1920, 1080);
            }

            var doc = gameObject.AddComponent<UIDocument>();
            doc.panelSettings  = _panelSettings;
            doc.sortingOrder   = 10; // renders on top of HUDView (sortingOrder 0)

            var root = doc.rootVisualElement;
            root.style.flexGrow = 1f;

            // ── Full-screen overlay ──────────────────────────────────────────
            _overlay = new VisualElement();
            _overlay.style.position         = Position.Absolute;
            _overlay.style.top              = 0f;
            _overlay.style.bottom           = 0f;
            _overlay.style.left             = 0f;
            _overlay.style.right            = 0f;
            _overlay.style.backgroundColor  = new StyleColor(ColOverlay);
            _overlay.style.alignItems       = Align.Center;
            _overlay.style.justifyContent   = Justify.Center;
            _overlay.style.display          = DisplayStyle.None;
            root.Add(_overlay);

            // ── Transition fade (above the overlay, but click-through and never modal) ──
            _fade = new VisualElement();
            _fade.style.position        = Position.Absolute;
            _fade.style.top             = 0f;
            _fade.style.bottom          = 0f;
            _fade.style.left            = 0f;
            _fade.style.right           = 0f;
            _fade.style.backgroundColor = new StyleColor(Color.black);
            _fade.style.display         = DisplayStyle.None;
            _fade.pickingMode           = PickingMode.Ignore;
            root.Add(_fade);

            // ── Modal panel ──────────────────────────────────────────────────
            var panel = new VisualElement();
            panel.style.backgroundColor         = new StyleColor(ColBg);
            panel.style.paddingTop              = 40f;
            panel.style.paddingBottom           = 40f;
            panel.style.paddingLeft             = 56f;
            panel.style.paddingRight            = 56f;
            panel.style.borderTopLeftRadius     = 10f;
            panel.style.borderTopRightRadius    = 10f;
            panel.style.borderBottomLeftRadius  = 10f;
            panel.style.borderBottomRightRadius = 10f;
            panel.style.minWidth                = 360f;
            panel.style.alignItems              = Align.Center;
            panel.style.borderTopWidth          = 1f;
            panel.style.borderBottomWidth       = 1f;
            panel.style.borderLeftWidth         = 1f;
            panel.style.borderRightWidth        = 1f;
            panel.style.borderTopColor          = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            panel.style.borderBottomColor       = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            panel.style.borderLeftColor         = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            panel.style.borderRightColor        = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            _overlay.Add(panel);

            // Title
            _titleLabel = new Label("PAUSE");
            _titleLabel.style.fontSize                = 40f;
            _titleLabel.style.color                   = new StyleColor(Color.white);
            _titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _titleLabel.style.unityTextAlign          = TextAnchor.MiddleCenter;
            _titleLabel.style.marginBottom            = 12f;
            panel.Add(_titleLabel);

            // Body
            _bodyLabel = new Label();
            _bodyLabel.style.fontSize        = 18f;
            _bodyLabel.style.color           = new StyleColor(ColMuted);
            _bodyLabel.style.unityTextAlign  = TextAnchor.MiddleCenter;
            _bodyLabel.style.whiteSpace      = WhiteSpace.Normal;
            _bodyLabel.style.marginBottom    = 20f;
            panel.Add(_bodyLabel);

            BuildRunSummary(panel);

            // Button row
            _buttonRow = new VisualElement();
            _buttonRow.style.flexDirection  = FlexDirection.Row;
            _buttonRow.style.justifyContent = Justify.Center;
            _buttonRow.style.flexWrap       = Wrap.Wrap;
            panel.Add(_buttonRow);
        }

        /// <summary>
        /// End-of-run stat block: depth is the headline (v3's primary metric), score is secondary,
        /// then the four highlight stats as label/value rows.
        /// </summary>
        private void BuildRunSummary(VisualElement panel)
        {
            _statsPanel = new VisualElement();
            _statsPanel.style.alignItems   = Align.Center;
            _statsPanel.style.marginBottom = 28f;
            _statsPanel.style.minWidth     = 320f;
            _statsPanel.style.display      = DisplayStyle.None;
            panel.Add(_statsPanel);

            // ── Depth: the big number ────────────────────────────────────────
            _depthValue = new Label("0 m");
            _depthValue.style.fontSize                = 64f;
            _depthValue.style.color                   = new StyleColor(ColAmber);
            _depthValue.style.unityFontStyleAndWeight = FontStyle.Bold;
            _depthValue.style.unityTextAlign          = TextAnchor.MiddleCenter;
            _statsPanel.Add(_depthValue);

            var depthCaption = new Label("PROFONDEUR");
            depthCaption.style.fontSize       = 12f;
            depthCaption.style.color          = new StyleColor(ColMuted);
            depthCaption.style.unityTextAlign = TextAnchor.MiddleCenter;
            depthCaption.style.marginBottom   = 12f;
            _statsPanel.Add(depthCaption);

            // ── Score: secondary ─────────────────────────────────────────────
            _scoreValue = new Label("0 pts");
            _scoreValue.style.fontSize                = 26f;
            _scoreValue.style.color                   = new StyleColor(Color.white);
            _scoreValue.style.unityFontStyleAndWeight = FontStyle.Bold;
            _scoreValue.style.unityTextAlign          = TextAnchor.MiddleCenter;
            _scoreValue.style.marginBottom            = 18f;
            _statsPanel.Add(_scoreValue);

            // ── Highlight stats ──────────────────────────────────────────────
            _streakValue    = AddStatRow(_statsPanel, "MEILLEUR STREAK");
            _burstValue     = AddStatRow(_statsPanel, "PLUS GROS BURST");
            _bombChainValue = AddStatRow(_statsPanel, "MEILLEURE CHAÎNE");
            _perfectValue   = AddStatRow(_statsPanel, "PERFECT CLEARS");
        }

        /// <summary>One "LABEL ................ value" line. Returns the value label to fill in.</summary>
        private static Label AddStatRow(VisualElement parent, string caption)
        {
            var row = new VisualElement();
            row.style.flexDirection  = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.alignItems     = Align.Center;
            row.style.width          = new StyleLength(new Length(100f, LengthUnit.Percent));
            row.style.marginTop      = 5f;
            parent.Add(row);

            var label = new Label(caption);
            label.style.fontSize    = 12f;
            label.style.color       = new StyleColor(ColMuted);
            label.style.marginRight = 24f;
            row.Add(label);

            var value = new Label("—");
            value.style.fontSize                = 16f;
            value.style.color                   = new StyleColor(Color.white);
            value.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(value);

            return value;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Button helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Reveal the overlay and hand focus to its primary button, so a gamepad or the
        /// keyboard can drive it immediately (Navigate to move, Submit/A to activate).</summary>
        private void ShowOverlay()
        {
            _overlay.style.display = DisplayStyle.Flex;
            // schedule: focus after the panel has laid the new buttons out this frame.
            _primaryButton?.schedule.Execute(() => _primaryButton?.Focus());
        }

        private void ClearButtons()
        {
            _buttonRow.Clear();
            _primaryButton = null;
        }

        private static void SetBorderColor(VisualElement e, Color c)
        {
            var sc = new StyleColor(c);
            e.style.borderTopColor    = sc;
            e.style.borderBottomColor = sc;
            e.style.borderLeftColor   = sc;
            e.style.borderRightColor  = sc;
        }

        private void AddButton(string label, bool primary, bool danger, Action onClick)
        {
            var btn = new Button(() => onClick?.Invoke());
            btn.text = label;
            if (primary)
                _primaryButton = btn;

            // A 2px border, transparent until focused, then amber — a visible selection cursor for
            // gamepad/keyboard navigation. Reserving the width always keeps focus from shifting layout.
            btn.style.borderTopWidth    = 2f;
            btn.style.borderBottomWidth = 2f;
            btn.style.borderLeftWidth   = 2f;
            btn.style.borderRightWidth  = 2f;
            SetBorderColor(btn, Color.clear);
            btn.RegisterCallback<FocusInEvent>(_  => SetBorderColor(btn, Color.white));
            btn.RegisterCallback<FocusOutEvent>(_ => SetBorderColor(btn, Color.clear));

            btn.style.paddingTop    = 12f;
            btn.style.paddingBottom = 12f;
            btn.style.paddingLeft   = 24f;
            btn.style.paddingRight  = 24f;
            btn.style.marginLeft    = 6f;
            btn.style.marginRight   = 6f;
            btn.style.borderTopLeftRadius     = 6f;
            btn.style.borderTopRightRadius    = 6f;
            btn.style.borderBottomLeftRadius  = 6f;
            btn.style.borderBottomRightRadius = 6f;
            btn.style.fontSize = 16f;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            if (danger)
            {
                btn.style.backgroundColor = new StyleColor(ColDanger);
                btn.style.color           = new StyleColor(Color.white);
            }
            else if (primary)
            {
                btn.style.backgroundColor = new StyleColor(ColAmber);
                btn.style.color           = new StyleColor(ColDark);
            }
            else
            {
                btn.style.backgroundColor = new StyleColor(new Color(0.20f, 0.17f, 0.14f));
                btn.style.color           = new StyleColor(Color.white);
            }

            _buttonRow.Add(btn);
        }
    }
}
