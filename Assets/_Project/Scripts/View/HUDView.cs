using System;
using HollowLines.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace HollowLines.View
{
    /// <summary>
    /// Gameplay HUD: score, depth, color streak, air bar, hearts, chain multiplier,
    /// plus the v3 celebration popups (burst / bomb chain / perfect clear / enemy kill / Boomer blast).
    /// All UI built in code via UI Toolkit — no UXML/USS asset required.
    ///
    /// Usage: GameBootstrap calls Init() in Awake(); this MonoBehaviour builds its
    /// VisualElement tree in Start() (after Init stores system references).
    ///
    /// PanelSettings: assign a PanelSettings asset in the Inspector for proper font
    /// rendering. If none is assigned a runtime fallback is created with a warning.
    /// </summary>
    public sealed class HUDView : MonoBehaviour
    {
        [Tooltip("UI Toolkit panel settings. Create via Assets > Create > UI Toolkit > Panel Settings.\n" +
                 "Leave empty to use a runtime fallback (fonts may not render correctly).")]
        [SerializeField] private PanelSettings panelSettings;

        // ── Cached VisualElement refs ─────────────────────────────────────────
        private Label         _scoreLabel;
        private Label         _depthLabel;
        private Label         _streakLabel;
        private Label         _diamondLabel;
        private VisualElement _airFill;
        private VisualElement[] _heartIcons;
        private Label         _chainLabel;
        private Label         _popupLabel;

        // ── Chain label auto-hide ─────────────────────────────────────────────
        private float         _chainHideTimer;
        private const float   ChainHoldSeconds = 1.5f;

        // ── Popup fade ────────────────────────────────────────────────────────
        private float       _popupTimer;
        private const float PopupHoldSeconds = 1.0f;

        // ── System refs (stored by Init, used by Start) ───────────────────────
        private ScoreSystem     _score;
        private AirSystem       _air;
        private HealthSystem    _health;
        private ChainTracker    _chain;
        private StreakTracker   _streak;
        private DepthTracker    _depth;
        private DiamondSystem   _diamonds; // R4: per-board collection state; see RefreshDiamonds
        private CampaignManager _campaign; // null when campaign mode is off
        private EndlessManager  _endless;  // non-null ONLY in endless mode — see SetDepthSource
        private bool            _built;    // true once Start() has built the UI and subscribed

        // ── Stored delegates for clean unsubscription ─────────────────────────
        private Action<ScoreEvent> _onScore;
        private Action<float>      _onAirChanged;
        private Action<int>        _onHeartsChanged;
        private Action<int>        _onChainLink;
        private Action<int>        _onChainCompleted;
        private Action<int>        _onStreakGrew;
        private Action<int>        _onStreakBroken;
        private Action<int>        _onNewDepth;
        private Action<int>        _onEndlessDepth;
        private Action<int, int>   _onDiamondCollected;

        // ── Colors (match BoardView palette) ─────────────────────────────────
        private static readonly Color ColAmber  = new Color(0.94f, 0.62f, 0.15f);
        private static readonly Color ColCyan   = new Color(0.35f, 0.85f, 0.95f);
        private static readonly Color ColDanger = new Color(0.95f, 0.25f, 0.10f);
        private static readonly Color ColGold   = new Color(1.00f, 0.85f, 0.20f);
        private static readonly Color ColDim    = new Color(0.20f, 0.16f, 0.12f);
        private static readonly Color ColPanel  = new Color(0.00f, 0.00f, 0.00f, 0.55f);
        private static readonly Color ColMuted  = new Color(0.70f, 0.70f, 0.70f);
        // R5.14 enemy popups: lime for a kill, violet for a Boomer's blast — deliberately neither
        // amber (burst) nor red (bomb chain), so all four celebrations stay tellable apart at a glance.
        private static readonly Color ColKill   = new Color(0.55f, 0.95f, 0.35f);
        private static readonly Color ColBoom   = new Color(0.78f, 0.45f, 1.00f);

        // Streak tint mirrors the BoardView block palette so "×5" reads as "×5 amber".
        private static readonly Color ColBlockA = new Color(0.94f, 0.62f, 0.15f); // amber
        private static readonly Color ColBlockB = new Color(0.11f, 0.62f, 0.46f); // teal
        private static readonly Color ColBlockC = new Color(0.83f, 0.33f, 0.49f); // pink

        // ─────────────────────────────────────────────────────────────────────
        // Public API
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Store system references. Must be called from GameBootstrap.Awake() before
        /// this component's Start() runs.
        /// </summary>
        public void Init(ScoreSystem score, AirSystem air, HealthSystem health, ChainTracker chain,
                         StreakTracker streak = null, DepthTracker depth = null,
                         CampaignManager campaign = null, PanelSettings ps = null,
                         DiamondSystem diamonds = null)
        {
            _score    = score;
            _air      = air;
            _health   = health;
            _chain    = chain;
            _streak   = streak;
            _depth    = depth;
            _campaign = campaign;
            _diamonds = diamonds;
            if (ps != null) panelSettings = ps;
        }

        /// <summary>
        /// Pick where the depth readout comes from: pass an EndlessManager for endless mode,
        /// null for campaign / debug boards.
        ///
        /// The two sources are NOT interchangeable. <see cref="DepthTracker"/> is per-board and is
        /// reset by every LoadLevel, so in endless it would restart the counter at each segment
        /// seam; <see cref="EndlessManager.Depth"/> is absolute across the whole run (§6.3).
        /// Safe to call before or after Start() — the mode can change at runtime from the menu.
        /// </summary>
        public void SetDepthSource(EndlessManager endless)
        {
            if (_endless == endless) return;

            if (_built) UnsubscribeDepth();
            _endless = endless;
            if (_built)
            {
                SubscribeDepth();
                RefreshDepth(CurrentDepth());
            }
        }

        private int CurrentDepth() => _endless != null ? _endless.Depth : _depth?.MaxDepth ?? 0;

        private void SubscribeDepth()
        {
            if (_endless != null) _endless.DepthChanged     += _onEndlessDepth;
            else if (_depth != null) _depth.NewDepthReached += _onNewDepth;
        }

        private void UnsubscribeDepth()
        {
            if (_endless != null) _endless.DepthChanged     -= _onEndlessDepth;
            if (_depth   != null) _depth.NewDepthReached    -= _onNewDepth;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Unity lifecycle
        // ─────────────────────────────────────────────────────────────────────

        private void Start()
        {
            if (_score == null)
            {
                Debug.LogError("[HUDView] Init() was not called before Start(). HUD is disabled.");
                enabled = false;
                return;
            }

            BuildUI();

            // Subscribe — store delegates so OnDestroy can unsubscribe cleanly.
            _onScore          = OnScoreEvent;
            _onAirChanged     = pct => RefreshAir(pct);
            _onHeartsChanged  = h   => RefreshHearts(h);
            _onChainLink      = step => ShowChain(step);
            _onChainCompleted = _    => _chainHideTimer = ChainHoldSeconds;
            _onStreakGrew     = count => ShowStreak(count);
            _onStreakBroken   = _     => HideStreak();
            _onNewDepth       = row   => RefreshDepth(row);
            _onEndlessDepth   = depth => RefreshDepth(depth);
            _onDiamondCollected = (collected, total) => RefreshDiamonds(collected, total);

            _score.OnScore        += _onScore;
            _air.AirChanged       += _onAirChanged;
            _health.HeartsChanged += _onHeartsChanged;
            if (_chain != null)
            {
                _chain.LinkAdded      += _onChainLink;
                _chain.ChainCompleted += _onChainCompleted;
            }
            if (_streak != null)
            {
                _streak.StreakGrew   += _onStreakGrew;
                _streak.StreakBroken += _onStreakBroken;
            }
            if (_diamonds != null)
                _diamonds.DiamondCollected += _onDiamondCollected;
            SubscribeDepth();
            _built = true;

            // Initial state
            RefreshScore();
            RefreshAir(_air.Air);
            RefreshHearts(_health.Hearts);
            RefreshDepth(CurrentDepth());
            HideStreak();
            RefreshDiamonds(_diamonds?.Collected ?? 0, _diamonds?.Total ?? 0);
        }

        /// <summary>
        /// Call after LoadLevel() to point the HUD at the new ChainTracker instance.
        /// (ChainTracker subscribes to CollapseSystem in its constructor, so it must be
        /// recreated whenever CollapseSystem is recreated.)
        /// </summary>
        public void RewireChain(ChainTracker newChain)
        {
            if (_chain != null)
            {
                _chain.LinkAdded      -= _onChainLink;
                _chain.ChainCompleted -= _onChainCompleted;
            }
            _chain = newChain;
            if (_chain != null)
            {
                _chain.LinkAdded      += _onChainLink;
                _chain.ChainCompleted += _onChainCompleted;
            }
        }

        /// <summary>
        /// Call after LoadLevel(). StreakTracker.Reset()/DepthTracker.Reset() are silent by design,
        /// so the HUD would otherwise keep showing the previous board's streak and depth. Same
        /// story for DiamondSystem.Init() (R4) — it re-arms Collected/Total with no event.
        /// </summary>
        public void OnLevelLoaded()
        {
            if (_streakLabel == null) return; // UI not built yet — the first load runs during Awake

            HideStreak();
            RefreshDepth(CurrentDepth());
            RefreshDiamonds(_diamonds?.Collected ?? 0, _diamonds?.Total ?? 0);
        }

        private void OnDestroy()
        {
            if (_score  != null) _score.OnScore          -= _onScore;
            if (_air    != null) _air.AirChanged          -= _onAirChanged;
            if (_health != null) _health.HeartsChanged    -= _onHeartsChanged;
            if (_chain  != null)
            {
                _chain.LinkAdded      -= _onChainLink;
                _chain.ChainCompleted -= _onChainCompleted;
            }
            if (_streak != null)
            {
                _streak.StreakGrew   -= _onStreakGrew;
                _streak.StreakBroken -= _onStreakBroken;
            }
            if (_diamonds != null)
                _diamonds.DiamondCollected -= _onDiamondCollected;
            UnsubscribeDepth();
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            if (_chainHideTimer > 0f)
            {
                _chainHideTimer -= dt;
                if (_chainHideTimer <= 0f)
                    _chainLabel.style.display = DisplayStyle.None;
            }

            if (_popupTimer > 0f)
            {
                _popupTimer -= dt;
                if (_popupTimer <= 0f)
                {
                    _popupLabel.style.display = DisplayStyle.None;
                }
                else
                {
                    // Hold at full opacity for the first half, then fade out.
                    float t = _popupTimer / PopupHoldSeconds;
                    _popupLabel.style.opacity = Mathf.Clamp01(t * 2f);
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Event handlers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Single entry point for every award. ScoreEvent already carries Points, Source and
        /// Detail (§5.6), which is exactly what the celebration popups need — reading them here
        /// avoids duplicating the scoring formulas in the view just to print "+300".
        /// </summary>
        private void OnScoreEvent(ScoreEvent evt)
        {
            RefreshScore();

            switch (evt.Source)
            {
                case ScoreSource.Burst:
                    ShowPopup($"BURST! +{evt.Points:N0}", ColAmber);
                    break;

                case ScoreSource.Bomb:
                    // Detail = chain multiplier; a lone bomb is not a "chain".
                    if (evt.Detail > 1)
                        ShowPopup($"CHAIN ×{evt.Detail}! +{evt.Points:N0}", ColDanger);
                    break;

                case ScoreSource.PerfectClear:
                    ShowPopup($"PERFECT CLEAR! +{evt.Points:N0}", ColGold);
                    break;

                case ScoreSource.EnemyKill:
                    ShowEnemyKillPopup(evt);
                    break;

                case ScoreSource.BoomerBlast:
                    // A Boomer that detonates in open air destroys nothing — "+0 BOOM!" is noise.
                    if (evt.Points > 0)
                        ShowPopup($"+{evt.Points:N0} BOOM!", ColBoom);
                    break;
            }
        }

        /// <summary>
        /// "+100 CRAWLER!" / "+450 BOOMER! ×3" (R5.14).
        ///
        /// ScoreEvent has no enemy-type field — Detail carries the kill bonus — but the type is
        /// exactly recoverable: AwardEnemyKill computes Points = basePoints × bonus, so
        /// basePoints = Points / Detail lands precisely on CrawlerKillPoints or BoomerKillPoints.
        /// That keeps the popup on the same OnScore-only diet as every other one (§5.9): the view
        /// never subscribes to EnemySystem directly and never re-derives a scoring formula.
        /// </summary>
        private void ShowEnemyKillPopup(ScoreEvent evt)
        {
            int bonus      = Mathf.Max(1, evt.Detail);
            int basePoints = evt.Points / bonus;

            string name  = basePoints == ScoreSystem.BoomerKillPoints ? "BOOMER" : "CRAWLER";
            string mult   = bonus > 1 ? $" ×{bonus}" : string.Empty;

            ShowPopup($"+{evt.Points:N0} {name}!{mult}", ColKill);
        }

        private void RefreshScore()
        {
            _scoreLabel.text = _score.Score.ToString("N0");
        }

        private void RefreshDepth(int row)
        {
            _depthLabel.text = $"DEPTH: {row}";
        }

        /// <summary>
        /// R4/§5.9: "💎 2/5" in campaign (the denominator is gate information — how many are left
        /// before the win condition opens, §6.4) versus "💎 7" in Endless, where diamonds are an
        /// ungated bonus so only the count matters. Hidden entirely when the board has none
        /// (levels 1-3, or an endless segment that rolled zero).
        ///
        /// Both numbers come straight from the live DiamondSystem, so in Endless this resets at
        /// every segment seam along with the rest of the per-board state (DiamondSystem.Init() in
        /// GameBootstrap.LoadLevel()) — it reads "diamonds on THIS stretch of the well", not a
        /// whole-run tally. That mirrors DiamondSystem's actual per-board semantics rather than
        /// adding a second, run-spanning counter for a decorative bonus.
        /// </summary>
        private void RefreshDiamonds(int collected, int total)
        {
            if (total <= 0)
            {
                _diamondLabel.style.display = DisplayStyle.None;
                return;
            }

            _diamondLabel.text = _endless != null ? $"\U0001F48E {collected}" : $"\U0001F48E {collected}/{total}";
            _diamondLabel.style.display = DisplayStyle.Flex;
        }

        private void ShowStreak(int count)
        {
            _streakLabel.text          = $"×{count}";
            _streakLabel.style.color   = new StyleColor(StreakColor());
            _streakLabel.style.display = DisplayStyle.Flex;
        }

        private void HideStreak()
        {
            _streakLabel.style.display = DisplayStyle.None;
        }

        private Color StreakColor()
        {
            switch (_streak?.CurrentColor)
            {
                case CellType.ColorA: return ColBlockA;
                case CellType.ColorB: return ColBlockB;
                case CellType.ColorC: return ColBlockC;
                default:              return Color.white;
            }
        }

        /// <summary>Latest popup wins — a burst during a bomb chain replaces the older line.</summary>
        private void ShowPopup(string text, Color color)
        {
            _popupLabel.text            = text;
            _popupLabel.style.color     = new StyleColor(color);
            _popupLabel.style.opacity   = 1f;
            _popupLabel.style.display   = DisplayStyle.Flex;
            _popupTimer                 = PopupHoldSeconds;
        }

        private void RefreshAir(float air)
        {
            float pct = Mathf.Clamp01(air / AirSystem.MaxAir);
            _airFill.style.width = new StyleLength(new Length(pct * 100f, LengthUnit.Percent));
            _airFill.style.backgroundColor = new StyleColor(pct < 0.25f ? ColDanger : ColCyan);
        }

        private void RefreshHearts(int hearts)
        {
            for (int i = 0; i < _heartIcons.Length; i++)
            {
                _heartIcons[i].style.backgroundColor =
                    new StyleColor(i < hearts ? ColAmber : ColDim);
            }
        }

        private void ShowChain(int step)
        {
            _chainLabel.text                  = $"×{step}";
            _chainLabel.style.display         = DisplayStyle.Flex;
            _chainHideTimer                   = 0f; // cancel any pending hide
        }

        // ─────────────────────────────────────────────────────────────────────
        // UI construction
        // ─────────────────────────────────────────────────────────────────────

        private void BuildUI()
        {
            if (panelSettings == null)
            {
                panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                panelSettings.scaleMode           = PanelScaleMode.ScaleWithScreenSize;
                panelSettings.referenceResolution = new Vector2Int(1920, 1080);
                Debug.LogWarning("[HUDView] No PanelSettings assigned — runtime fallback in use. " +
                                 "Assign a PanelSettings asset in the Inspector for correct font rendering.");
            }

            var doc = gameObject.AddComponent<UIDocument>();
            doc.panelSettings = panelSettings;
            var root = doc.rootVisualElement;
            root.style.flexGrow = 1f;

            BuildTopBar(root);
            BuildChainLabel(root);
            BuildPopupLabel(root);
            BuildAirBar(root);
        }

        private void BuildTopBar(VisualElement root)
        {
            var topBar = new VisualElement();
            topBar.style.flexDirection  = FlexDirection.Row;
            topBar.style.justifyContent = Justify.SpaceBetween;
            topBar.style.alignItems     = Align.FlexStart;
            topBar.style.paddingTop     = 12f;
            topBar.style.paddingLeft    = 16f;
            topBar.style.paddingRight   = 16f;
            root.Add(topBar);

            topBar.Add(BuildScorePanel());
            topBar.Add(BuildDepthPanel());
            topBar.Add(BuildHeartsPanel());
        }

        private VisualElement BuildScorePanel()
        {
            var panel = MakePanel();

            var caption = new Label("SCORE");
            StyleCaption(caption);
            panel.Add(caption);

            _scoreLabel = new Label("0");
            _scoreLabel.style.fontSize                    = 28f;
            _scoreLabel.style.color                       = new StyleColor(Color.white);
            _scoreLabel.style.unityFontStyleAndWeight     = FontStyle.Bold;
            panel.Add(_scoreLabel);

            // Streak counter lives under the score (§5.9), hidden while the streak is 0.
            _streakLabel = new Label("×0");
            _streakLabel.style.fontSize                = 22f;
            _streakLabel.style.color                   = new StyleColor(ColAmber);
            _streakLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _streakLabel.style.marginTop               = 2f;
            _streakLabel.style.display                 = DisplayStyle.None;
            panel.Add(_streakLabel);

            return panel;
        }

        private VisualElement BuildDepthPanel()
        {
            var panel = MakePanel();
            panel.style.alignItems = Align.Center;

            _depthLabel = new Label("DEPTH: 0");
            _depthLabel.style.fontSize                = 24f;
            _depthLabel.style.color                   = new StyleColor(Color.white);
            _depthLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            panel.Add(_depthLabel);

            // Diamond counter lives under depth (§5.9) — both are "progress toward the bottom".
            // Hidden while the board has no diamonds (levels 1-3).
            _diamondLabel = new Label("");
            _diamondLabel.style.fontSize                = 18f;
            _diamondLabel.style.color                   = new StyleColor(ColCyan);
            _diamondLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _diamondLabel.style.marginTop               = 2f;
            _diamondLabel.style.display                 = DisplayStyle.None;
            panel.Add(_diamondLabel);

            return panel;
        }

        private VisualElement BuildHeartsPanel()
        {
            var panel = MakePanel();
            panel.style.alignItems = Align.FlexEnd;

            var caption = new Label("HP");
            StyleCaption(caption);
            panel.Add(caption);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop     = 4f;

            _heartIcons = new VisualElement[HealthSystem.MaxHearts];
            for (int i = 0; i < HealthSystem.MaxHearts; i++)
            {
                var icon = new VisualElement();
                icon.style.width  = 22f;
                icon.style.height = 22f;
                icon.style.marginLeft              = i == 0 ? 0f : 5f;
                icon.style.borderTopLeftRadius     = 3f;
                icon.style.borderTopRightRadius    = 3f;
                icon.style.borderBottomLeftRadius  = 3f;
                icon.style.borderBottomRightRadius = 3f;
                icon.style.backgroundColor         = new StyleColor(ColAmber);
                _heartIcons[i] = icon;
                row.Add(icon);
            }

            panel.Add(row);
            return panel;
        }

        private void BuildChainLabel(VisualElement root)
        {
            _chainLabel = new Label("×2");
            _chainLabel.style.position    = Position.Absolute;
            _chainLabel.style.left        = 0f;
            _chainLabel.style.right       = 0f;
            _chainLabel.style.top         = new StyleLength(new Length(35f, LengthUnit.Percent));
            _chainLabel.style.fontSize    = 72f;
            _chainLabel.style.color       = new StyleColor(ColGold);
            _chainLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _chainLabel.style.unityTextAlign          = TextAnchor.MiddleCenter;
            _chainLabel.style.display     = DisplayStyle.None;
            root.Add(_chainLabel);
        }

        /// <summary>
        /// Celebration line for burst / bomb chain / perfect clear. Sits below the chain
        /// multiplier so the two never overlap.
        /// </summary>
        private void BuildPopupLabel(VisualElement root)
        {
            _popupLabel = new Label("BURST!");
            _popupLabel.style.position    = Position.Absolute;
            _popupLabel.style.left        = 0f;
            _popupLabel.style.right       = 0f;
            _popupLabel.style.top         = new StyleLength(new Length(48f, LengthUnit.Percent));
            _popupLabel.style.fontSize    = 40f;
            _popupLabel.style.color       = new StyleColor(ColAmber);
            _popupLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _popupLabel.style.unityTextAlign          = TextAnchor.MiddleCenter;
            _popupLabel.style.display     = DisplayStyle.None;
            root.Add(_popupLabel);
        }

        private void BuildAirBar(VisualElement root)
        {
            var section = new VisualElement();
            section.style.position = Position.Absolute;
            section.style.bottom   = 16f;
            section.style.left     = 16f;
            section.style.right    = 16f;
            root.Add(section);

            var caption = new Label("AIR");
            StyleCaption(caption);
            caption.style.marginBottom = 4f;
            section.Add(caption);

            var bg = new VisualElement();
            bg.style.height                    = 10f;
            bg.style.borderTopLeftRadius       = 5f;
            bg.style.borderTopRightRadius      = 5f;
            bg.style.borderBottomLeftRadius    = 5f;
            bg.style.borderBottomRightRadius   = 5f;
            bg.style.backgroundColor           = new StyleColor(new Color(0.10f, 0.10f, 0.10f));
            bg.style.overflow                  = Overflow.Hidden;
            section.Add(bg);

            _airFill = new VisualElement();
            _airFill.style.position              = Position.Absolute;
            _airFill.style.top                   = 0f;
            _airFill.style.bottom                = 0f;
            _airFill.style.left                  = 0f;
            _airFill.style.width                 = new StyleLength(new Length(100f, LengthUnit.Percent));
            _airFill.style.borderTopLeftRadius   = 5f;
            _airFill.style.borderBottomLeftRadius = 5f;
            _airFill.style.backgroundColor       = new StyleColor(ColCyan);
            bg.Add(_airFill);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        private static VisualElement MakePanel()
        {
            var p = new VisualElement();
            p.style.paddingTop    = 6f;
            p.style.paddingBottom = 8f;
            p.style.paddingLeft   = 10f;
            p.style.paddingRight  = 10f;
            p.style.backgroundColor            = new StyleColor(ColPanel);
            p.style.borderTopLeftRadius        = 6f;
            p.style.borderTopRightRadius       = 6f;
            p.style.borderBottomLeftRadius     = 6f;
            p.style.borderBottomRightRadius    = 6f;
            return p;
        }

        private static void StyleCaption(Label label)
        {
            label.style.fontSize = 11f;
            label.style.color    = new StyleColor(ColMuted);
        }
    }
}
