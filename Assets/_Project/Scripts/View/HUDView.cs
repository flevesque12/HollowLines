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

        // ── R6.7 streak counter (F07) ─────────────────────────────────────────
        // Bigger, and alive: the "×N" grows with the run and punches on every step; when a real
        // streak (×2+) breaks, a copy of the old number shakes, splits in two and falls away.
        private VisualElement _streakBox;                  // relative container: label + crack halves
        private VisualElement _crackLeft, _crackRight;     // overflow-clipped halves
        private Label         _crackLeftText, _crackRightText;
        private float         _streakPunch;                // 1 → 0 after each step
        private float         _crackTimer;                 // counts down CrackSeconds → 0
        private float         _crackHalfWidth;
        private Color         _crackColor;
        private const float   StreakPunchSeconds = 0.18f;
        private const float   CrackSeconds       = 0.6f;
        private const float   CrackShakeSeconds  = 0.12f;
        private Label         _diamondLabel;
        private VisualElement _airFill;
        private Label         _airCaption;
        private VisualElement _graceFuse;   // R6.2: thin strip over the air bar that burns down through the grace window

        // ── R6.2 air start-buffer cue ─────────────────────────────────────────
        // Grace: pale, slowly shimmering fill + "AIR · 3" countdown + a shrinking fuse strip.
        // Grace end: a short white flash. Ramp: the fill slides from pale to cyan as the drain wakes up.
        private bool        _wasInGrace;
        private float       _airFlashTimer;
        private const float AirFlashSeconds = 0.35f;

        // ── R6.6 drill score popups (F07) ─────────────────────────────────────
        // "+30 ×3" rising off every drilled cell, so the player SEES that drilling pays (design
        // rule 1). World-anchored: each popup keeps its world position and is re-projected every
        // frame, so it rides with the board while the camera follows the avatar down. Pooled —
        // a fast player drills ~7 cells/s, so allocating a Label per drill would churn the GC.
        private sealed class DrillPopup
        {
            public Label   Label;
            public Vector3 World;
            public float   Age;
            public bool    Live;
        }
        private VisualElement              _root;
        private readonly System.Collections.Generic.List<DrillPopup> _drillPopups =
            new System.Collections.Generic.List<DrillPopup>();
        private const int   DrillPopupPool     = 16;
        private const float DrillPopupLife     = 0.7f;
        private const float DrillPopupRise     = 0.7f;  // cells risen over its life
        private const float DrillPopupStartY   = 0.1f;  // cells above the drilled cell's centre
        // To the RIGHT of the cell, left-aligned: drilling down, the avatar falls into the drilled cell,
        // so a popup centred on it sat right on the driller (screenshot check).
        private const float DrillPopupOffsetX  = 0.6f;

        // ── R6.3 "drill to breathe" hint (F02) ────────────────────────────────
        // v3.1 made drilling the main air source, but nothing ever said so — new players watched the
        // bar empty without knowing the answer was under their feet. Shown above the air bar (the
        // centre is the celebration popup and the avatar), with hysteresis so drilling right at the
        // threshold doesn't make it flicker, and a pop each time air comes back to confirm the fix.
        private Label       _breatheHint;
        private bool        _breatheShown;
        private float       _breatheAlpha;      // eased 0..1 so it fades in/out instead of popping
        private float       _breathePop;        // 1 → 0 after each air gain while shown
        private float       _lastAir = AirSystem.MaxAir;
        private const float BreatheShowBelow = 30f; // % air
        private const float BreatheHideAbove = 35f;
        private const float BreatheFadeSpeed = 6f;  // alpha per second
        private const float BreathePopSeconds = 0.25f;
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
        private static readonly Color ColAirIdle = new Color(0.78f, 0.93f, 1.00f); // R6.2: "frozen" air (grace)
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
            _onStreakBroken   = lost  => BreakStreak(lost);
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

            TickAirBufferCue(dt);
            TickBreatheHint(dt);
            TickDrillPopups(dt);
            TickStreakCounter(dt);

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

        /// <summary>
        /// R6.7: shown from ×2 (a ×1 "streak" is just the colour you're on), 30 → 60 px as the run grows
        /// (capped at ×10), with a scale punch on every step.
        /// </summary>
        private void ShowStreak(int count)
        {
            if (count < 2)
            {
                HideStreak();
                return;
            }

            _streakLabel.text           = $"×{count}";
            _streakLabel.style.color    = new StyleColor(Color.Lerp(StreakColor(), Color.white, 0.15f));
            _streakLabel.style.fontSize = 30f + 3f * Mathf.Min(count, 10);
            _streakLabel.style.display  = DisplayStyle.Flex;
            _streakPunch = 1f;
        }

        private void HideStreak()
        {
            _streakLabel.style.display = DisplayStyle.None;
        }

        /// <summary>
        /// R6.7: StreakBroken carries the count that just ended. A ×1 ending is just a colour change —
        /// no ceremony. A real streak cracks: a copy of the last "×N" (clipped into a left and a right
        /// half) shakes, then the halves fall apart, tilt and fade. StreakGrew(1) for the new colour
        /// fires right after this and keeps the live label hidden (count < 2).
        /// </summary>
        private void BreakStreak(int lost)
        {
            if (lost >= 2 && _streakLabel.style.display == DisplayStyle.Flex)
            {
                float w = _streakLabel.resolvedStyle.width;
                float h = _streakLabel.resolvedStyle.height;
                if (float.IsNaN(w) || w <= 0f) w = 80f;
                if (float.IsNaN(h) || h <= 0f) h = 40f;

                _crackHalfWidth = w * 0.5f;
                _crackColor     = _streakLabel.resolvedStyle.color;
                foreach (Label l in new[] { _crackLeftText, _crackRightText })
                {
                    l.text           = _streakLabel.text;
                    l.style.fontSize = _streakLabel.style.fontSize;
                    l.style.color    = new StyleColor(_crackColor);
                    l.style.width    = w;
                }
                _crackLeftText.style.left  = 0f;
                _crackRightText.style.left = -_crackHalfWidth;

                _crackLeft.style.left  = 0f;
                _crackRight.style.left = _crackHalfWidth;
                foreach (VisualElement half in new[] { _crackLeft, _crackRight })
                {
                    half.style.width   = _crackHalfWidth;
                    half.style.height  = h;
                    half.style.display = DisplayStyle.Flex;
                }
                _streakBox.style.minHeight = h; // the live label hides; keep the slot while it breaks
                _crackTimer = CrackSeconds;
            }
            HideStreak();
        }

        private void TickStreakCounter(float dt)
        {
            if (!_built) return;

            if (_streakPunch > 0f)
            {
                _streakPunch = Mathf.Max(0f, _streakPunch - dt / StreakPunchSeconds);
                float k = 1f + 0.35f * _streakPunch * _streakPunch;
                _streakLabel.style.scale = new StyleScale(new Scale(new Vector3(k, k, 1f)));
            }

            if (_crackTimer <= 0f) return;
            _crackTimer = Mathf.Max(0f, _crackTimer - dt);
            float age = CrackSeconds - _crackTimer;

            if (_crackTimer <= 0f)
            {
                _crackLeft.style.display   = DisplayStyle.None;
                _crackRight.style.display  = DisplayStyle.None;
                _streakBox.style.minHeight = StyleKeyword.Null;
                return;
            }

            // Drained of colour as it dies: toward a dull red-grey.
            Color dead = Color.Lerp(_crackColor, new Color(0.55f, 0.35f, 0.32f), Mathf.Clamp01(age / CrackShakeSeconds));
            _crackLeftText.style.color  = new StyleColor(dead);
            _crackRightText.style.color = new StyleColor(dead);

            if (age < CrackShakeSeconds)
            {
                // Phase 1: the number shudders in place (both halves together — still one piece).
                float shake = Mathf.Sin(age * 120f) * 4f;
                SetCrackHalf(_crackLeft,  shake, 0f, 0f, 1f);
                SetCrackHalf(_crackRight, shake, 0f, 0f, 1f);
                return;
            }

            // Phase 2: it splits — halves drift apart, drop with gravity, tilt outward, fade.
            float u = (age - CrackShakeSeconds) / (CrackSeconds - CrackShakeSeconds);
            float fall = 60f * u * u;
            SetCrackHalf(_crackLeft,  -18f * u, fall, -22f * u, 1f - u);
            SetCrackHalf(_crackRight,  18f * u, fall,  22f * u, 1f - u);
        }

        private static void SetCrackHalf(VisualElement half, float dx, float dy, float degrees, float alpha)
        {
            half.style.translate = new Translate(dx, dy);
            half.style.rotate    = new Rotate(new Angle(degrees, AngleUnit.Degree));
            half.style.opacity   = alpha;
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

        /// <summary>
        /// R6.6: a small "+N" rising off a drilled cell. `points` is what the drill actually earned
        /// (GameBootstrap measures the score delta, so streak × base and a drilled diamond's +150 are
        /// both included without duplicating any formula here). Coloured and sized by the streak;
        /// "×N" is appended from a ×2 streak up, so the multiplier is visible, not just the total.
        /// Call after StreakTracker.NotifyDrill so the colour is the current streak's.
        ///
        /// `buildsStreak` = this drill was a downward drill on a fusable colour, i.e. it grew or reset
        /// the streak. A lateral/upward drill (v3.1: streak-neutral) still PAYS the current multiplier
        /// — rule 1, unchanged — but showing "×4" in teal over a pink block it doesn't belong to read
        /// as "this block is part of the run". So those get a plain white "+40": same points, no claim.
        /// (Dev playtest, 2026-10-05 — option A. See refactoring-plan.md R6.11 note before changing.)
        /// </summary>
        public void ShowDrillPopup(Vector3 worldCellCentre, int points, int streak, bool buildsStreak = true)
        {
            if (!_built || points <= 0) return;

            DrillPopup p = null;
            foreach (DrillPopup c in _drillPopups)
                if (!c.Live) { p = c; break; }
            if (p == null)
            {
                // Pool exhausted (very fast drilling): recycle the oldest one.
                p = _drillPopups[0];
                foreach (DrillPopup c in _drillPopups)
                    if (c.Age > p.Age) p = c;
            }

            bool streaking = buildsStreak && streak >= 2;
            p.Label.text = streaking ? $"+{points} ×{streak}" : $"+{points}";
            // Lifted toward white: raw block colours (teal especially) were too dark on the black well.
            p.Label.style.color    = new StyleColor(streaking ? Color.Lerp(StreakColor(), Color.white, 0.3f) : Color.white);
            p.Label.style.fontSize = 18f + 2f * Mathf.Min(streaking ? streak : 1, 6);
            p.World = worldCellCentre;
            p.Age   = 0f;
            p.Live  = true;
            p.Label.style.display = DisplayStyle.Flex;
            PlaceDrillPopup(p);
        }

        private void TickDrillPopups(float dt)
        {
            if (!_built) return;
            foreach (DrillPopup p in _drillPopups)
            {
                if (!p.Live) continue;
                p.Age += dt;
                if (p.Age >= DrillPopupLife)
                {
                    p.Live = false;
                    p.Label.style.display = DisplayStyle.None;
                    continue;
                }
                PlaceDrillPopup(p);
            }
        }

        /// <summary>World → panel every frame (the camera moves); rise, and fade over the second half.</summary>
        private void PlaceDrillPopup(DrillPopup p)
        {
            Camera cam = Camera.main;
            if (cam == null || _root?.panel == null) return;

            float t = p.Age / DrillPopupLife;
            Vector3 world = p.World + new Vector3(DrillPopupOffsetX, DrillPopupStartY + DrillPopupRise * (1f - (1f - t) * (1f - t)), 0f);
            Vector2 panelPos = RuntimePanelUtils.CameraTransformWorldToPanel(_root.panel, world, cam);

            p.Label.style.left    = panelPos.x;
            p.Label.style.top     = panelPos.y;
            p.Label.style.opacity = t < 0.5f ? 1f : 1f - (t - 0.5f) * 2f;
        }

        /// <summary>The avatar crossed the finish line (after R6.14). A late cascade popup may replace it — fine.</summary>
        public void ShowExitReached() => ShowPopup("SORTIE !", ColGold);

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
            _airFill.style.backgroundColor = new StyleColor(AirColor(pct));

            // R6.3: any air gain while the hint is up (a drill, a capsule, a burst) earns a pop —
            // the hint says what to do, the pop says "yes, that".
            if (_breatheShown && air > _lastAir + 0.01f)
                _breathePop = 1f;
            _lastAir = air;
        }

        /// <summary>
        /// R6.3: "FORE POUR RESPIRER !" while air is low. Show below 30 %, hide above 35 % (hysteresis),
        /// eased fade, slow pulse that speeds up as the tank empties, amber → red under 15 %.
        /// Scaled time throughout, so it freezes with the game under the pause/game-over overlay.
        /// </summary>
        private void TickBreatheHint(float dt)
        {
            if (!_built || _air == null) return;

            float air = _air.Air;
            if (!_breatheShown && air < BreatheShowBelow && air > 0f) _breatheShown = true;
            else if (_breatheShown && (air >= BreatheHideAbove || air <= 0f)) _breatheShown = false;

            _breatheAlpha = Mathf.MoveTowards(_breatheAlpha, _breatheShown ? 1f : 0f, BreatheFadeSpeed * dt);
            if (_breathePop > 0f) _breathePop = Mathf.Max(0f, _breathePop - dt / BreathePopSeconds);

            if (_breatheAlpha <= 0f)
            {
                _breatheHint.style.display = DisplayStyle.None;
                return;
            }
            _breatheHint.style.display = DisplayStyle.Flex;

            float urgency = 1f - Mathf.Clamp01(air / BreatheShowBelow);           // 0 at 30 %, 1 at 0 %
            float pulse   = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.Lerp(4f, 10f, urgency));
            _breatheHint.style.opacity = _breatheAlpha * Mathf.Lerp(0.65f, 1f, pulse);
            _breatheHint.style.color   = new StyleColor(air < 15f ? Color.Lerp(ColAmber, ColDanger, pulse) : ColAmber);

            float scale = 1f + 0.18f * _breathePop + 0.04f * pulse;
            _breatheHint.style.scale = new StyleScale(new Scale(new Vector3(scale, scale, 1f)));
        }

        /// <summary>
        /// Air fill color. Danger always wins; otherwise it tells the start-buffer story (R6.2):
        /// pale + shimmer while the clock is stopped, pale→cyan while the drain ramps up, white flash
        /// the moment it starts, plain cyan once the buffer is over.
        /// </summary>
        private Color AirColor(float pct)
        {
            if (pct < 0.25f) return ColDanger;

            Color c;
            if (_air.InStartGrace)
            {
                // Slow breathing shimmer — scaled time, so it freezes with the game when paused.
                float s = 0.5f + 0.5f * Mathf.Sin(Time.time * 2.4f);
                c = Color.Lerp(ColAirIdle, Color.white, 0.35f * s);
            }
            else
            {
                float ramp = Mathf.InverseLerp(AirSystem.StartDrainFactor, 1f, _air.StartBufferFactor);
                c = Color.Lerp(ColAirIdle, ColCyan, ramp);
            }

            if (_airFlashTimer > 0f)
                c = Color.Lerp(c, Color.white, _airFlashTimer / AirFlashSeconds);
            return c;
        }

        /// <summary>
        /// Per-frame part of the R6.2 cue. Polled, not evented: AirChanged deliberately stays silent
        /// through the grace window (nothing drained), so the countdown and shimmer have to read the
        /// buffer state themselves.
        /// </summary>
        private void TickAirBufferCue(float dt)
        {
            if (!_built || _air == null) return;

            bool inGrace = _air.InStartGrace;
            if (_wasInGrace && !inGrace && _air.StartBufferFactor < 1f)
                _airFlashTimer = AirFlashSeconds; // the clock just started (not a Reset mid-grace)
            _wasInGrace = inGrace;

            if (_airFlashTimer > 0f)
                _airFlashTimer = Mathf.Max(0f, _airFlashTimer - dt);

            if (inGrace)
            {
                float left = _air.StartGraceRemaining;
                _airCaption.text = $"AIR · {Mathf.CeilToInt(left)}";
                _graceFuse.style.display = DisplayStyle.Flex;
                float frac = _air.StartGraceDuration > 0f ? left / _air.StartGraceDuration : 0f;
                _graceFuse.style.width = new StyleLength(new Length(frac * 100f, LengthUnit.Percent));
            }
            else
            {
                _airCaption.text = "AIR";
                _graceFuse.style.display = DisplayStyle.None;
            }

            _airFill.style.backgroundColor = new StyleColor(AirColor(Mathf.Clamp01(_air.Air / AirSystem.MaxAir)));
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
            _root = root;

            BuildTopBar(root);
            BuildChainLabel(root);
            BuildPopupLabel(root);
            BuildAirBar(root);
            BuildDrillPopups(root);
        }

        /// <summary>R6.6 pool. Left edge on the anchor, vertically centred (-50 % Y translate); never pickable.</summary>
        private void BuildDrillPopups(VisualElement root)
        {
            for (int i = 0; i < DrillPopupPool; i++)
            {
                var l = new Label();
                l.style.position                = Position.Absolute;
                l.style.unityFontStyleAndWeight = FontStyle.Bold;
                l.style.unityTextOutlineWidth   = 0.3f;
                l.style.unityTextOutlineColor   = new StyleColor(Color.black);
                l.style.translate               = new Translate(new Length(0f, LengthUnit.Percent),
                                                                new Length(-50f, LengthUnit.Percent));
                l.style.display                 = DisplayStyle.None;
                l.pickingMode                   = PickingMode.Ignore;
                root.Add(l);
                _drillPopups.Add(new DrillPopup { Label = l });
            }
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

            // Streak counter lives under the score (§5.9), hidden below ×2. R6.7: inside a relative box
            // that also holds the two clipped halves of the crack effect, laid exactly over the label.
            _streakBox = new VisualElement();
            _streakBox.style.position  = Position.Relative;
            _streakBox.style.marginTop = 2f;
            panel.Add(_streakBox);

            _streakLabel = new Label("×0");
            _streakLabel.style.fontSize                = 30f;
            _streakLabel.style.color                   = new StyleColor(ColAmber);
            _streakLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _streakLabel.style.unityTextOutlineWidth   = 0.15f;
            _streakLabel.style.unityTextOutlineColor   = new StyleColor(Color.black);
            _streakLabel.style.transformOrigin         = new TransformOrigin(new Length(0f), new Length(50f, LengthUnit.Percent));
            _streakLabel.style.display                 = DisplayStyle.None;
            _streakBox.Add(_streakLabel);

            (_crackLeft,  _crackLeftText)  = BuildCrackHalf(_streakBox);
            (_crackRight, _crackRightText) = BuildCrackHalf(_streakBox);

            return panel;
        }

        /// <summary>R6.7: one half of the cracking "×N" — an overflow-clipped box holding a full copy of the text.</summary>
        private static (VisualElement box, Label text) BuildCrackHalf(VisualElement parent)
        {
            var box = new VisualElement();
            box.style.position = Position.Absolute;
            box.style.top      = 0f;
            box.style.overflow = Overflow.Hidden;
            box.style.display  = DisplayStyle.None;
            box.pickingMode    = PickingMode.Ignore;
            parent.Add(box);

            var text = new Label();
            text.style.position                = Position.Absolute;
            text.style.top                     = 0f;
            text.style.unityFontStyleAndWeight = FontStyle.Bold;
            text.style.unityTextOutlineWidth   = 0.15f;
            text.style.unityTextOutlineColor   = new StyleColor(Color.black);
            text.pickingMode                   = PickingMode.Ignore;
            box.Add(text);
            return (box, text);
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

            _airCaption = new Label("AIR");
            StyleCaption(_airCaption);
            _airCaption.style.marginBottom = 4f;
            section.Add(_airCaption);

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

            // R6.2 grace fuse: a 4 px gold strip along the top of the bar, burning right-to-left.
            // Gold, not white — white vanished against the pale grace fill (screenshot check).
            _graceFuse = new VisualElement();
            _graceFuse.style.position        = Position.Absolute;
            _graceFuse.style.top             = 0f;
            _graceFuse.style.left            = 0f;
            _graceFuse.style.height          = 4f;
            _graceFuse.style.width           = new StyleLength(new Length(100f, LengthUnit.Percent));
            _graceFuse.style.backgroundColor = new StyleColor(ColGold);
            _graceFuse.style.display         = DisplayStyle.None;
            bg.Add(_graceFuse);

            // R6.3 hint: centred just above the bar, on the root so it isn't clipped by the section.
            // Dark pill + outline: bare amber text vanished against the amber/pink blocks behind it.
            var hintRow = new VisualElement();
            hintRow.style.position   = Position.Absolute;
            hintRow.style.left       = 0f;
            hintRow.style.right      = 0f;
            hintRow.style.bottom     = 48f;
            hintRow.style.alignItems = Align.Center;
            hintRow.pickingMode      = PickingMode.Ignore;
            root.Add(hintRow);

            _breatheHint = new Label("FORE POUR RESPIRER !");
            _breatheHint.style.fontSize                = 26f;
            _breatheHint.style.color                   = new StyleColor(ColAmber);
            _breatheHint.style.unityFontStyleAndWeight = FontStyle.Bold;
            _breatheHint.style.unityTextAlign          = TextAnchor.MiddleCenter;
            _breatheHint.style.unityTextOutlineWidth   = 0.15f;
            _breatheHint.style.unityTextOutlineColor   = new StyleColor(Color.black);
            _breatheHint.style.backgroundColor         = new StyleColor(new Color(0f, 0f, 0f, 0.75f));
            _breatheHint.style.paddingTop              = 6f;
            _breatheHint.style.paddingBottom           = 6f;
            _breatheHint.style.paddingLeft             = 18f;
            _breatheHint.style.paddingRight            = 18f;
            _breatheHint.style.borderTopLeftRadius     = 8f;
            _breatheHint.style.borderTopRightRadius    = 8f;
            _breatheHint.style.borderBottomLeftRadius  = 8f;
            _breatheHint.style.borderBottomRightRadius = 8f;
            _breatheHint.style.display                 = DisplayStyle.None;
            _breatheHint.pickingMode                   = PickingMode.Ignore;
            hintRow.Add(_breatheHint);
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
