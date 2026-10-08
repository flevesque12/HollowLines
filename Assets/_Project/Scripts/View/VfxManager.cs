using System.Collections;
using System.Collections.Generic;
using HollowLines.Core;
using UnityEngine;

namespace HollowLines.View
{
    /// <summary>
    /// Gameplay VFX: drill flash, perfect-clear dust, bomb burst, chain glow, plus the v3
    /// effects — chunk-burst debris, shockwave ripples and bomb chain flash, and the v3.2 momentum
    /// effects (R7.10): Tier 1+ trail + avatar tint, Tier 2 fissure overlays, Tier 3 Power Drill flash,
    /// and the Danger Zone vignette.
    /// Grid-dependent — created as a child of BoardView in LoadLevel() and torn down with it,
    /// same lifecycle as AvatarView. Subscribes to Core events itself (like ChainTracker does to
    /// CollapseSystem) so GameBootstrap only has to instantiate it.
    /// No particle system or art assets: every effect reuses BoardView's generated white sprite.
    /// </summary>
    public sealed class VfxManager : MonoBehaviour
    {
        [Header("— Drill Flash —")]
        [SerializeField] private Color flashColor = Color.white;
        [SerializeField] private float flashDuration = 0.12f;

        [Header("— Collapse Dust —")]
        [SerializeField] private Color dustColor = new Color(0.85f, 0.80f, 0.70f);
        [SerializeField] private int dustParticlesPerRow = 10;
        [SerializeField] private float dustDuration = 0.35f;

        [Header("— Bomb Burst —")]
        [SerializeField] private Color burstColor = new Color(1f, 0.55f, 0.15f);
        [SerializeField] private int burstParticles = 16;
        [SerializeField] private float burstDuration = 0.4f;

        [Header("— Chain Glow —")]
        [SerializeField] private Color glowColor = new Color(0.95f, 0.85f, 0.25f);
        [SerializeField] private float glowFadeDuration = 1.5f;

        [Header("— Chunk Burst (v3) —")]
        [Tooltip("Debris particles spawned per burst cell.")]
        [SerializeField] private int debrisPerCell = 5;
        [SerializeField] private int debrisMax = 90;
        [SerializeField] private float debrisDuration = 0.5f;

        [Header("— Shockwave Ripple (v3) —")]
        [SerializeField] private Color rippleColor = new Color(1f, 1f, 1f, 0.7f);
        [SerializeField] private float rippleDuration = 0.2f;
        [Tooltip("End diameter in cells. The shockwave reaches 1 cell around the center → 3 cells across.")]
        [SerializeField] private float rippleEndSize = 3f;

        [Header("— Momentum (v3.2, R7.10) —")]
        [Tooltip("Seconds between trail particles at Tier 1; halved at Tier 2, thirded at Tier 3.")]
        [SerializeField] private float trailInterval = 0.09f;
        [SerializeField] private float trailDuration = 0.35f;
        [SerializeField] private Color fissureColor  = new Color(0.05f, 0.03f, 0.02f);
        [SerializeField] private Color powerFlashColor = Color.white;
        [SerializeField] private float powerFlashIntensity = 0.55f;

        [Header("— Danger Zone Vignette (v3.2, R7.10) —")]
        [SerializeField] private Color vignetteColor = new Color(0.95f, 0.08f, 0.05f);
        [Tooltip("Vignette alpha range over one ~1 Hz pulse.")]
        [SerializeField] private Vector2 vignetteAlpha = new Vector2(0.30f, 0.60f);
        [SerializeField] private float vignetteFadeSpeed = 3f;

        [Header("— Bomb Chain Flash (v3) —")]
        [SerializeField] private Color chainFlashColor = new Color(1f, 0.75f, 0.35f);
        [SerializeField] private float chainFlashDuration = 0.25f;

        [Header("— Diamond Collect (R4) —")]
        [SerializeField] private Color diamondSparkleColor = new Color(0.85f, 0.95f, 1f);
        [SerializeField] private int diamondSparkleParticles = 10;
        [SerializeField] private float diamondSparkleDuration = 0.45f;

        [Header("— Enemies (R5.15) —")]
        [Tooltip("Crawler death: a quick, low, earthy crunch — small and cheap, it's a bonus target.")]
        [SerializeField] private Color crawlerDeathColor    = new Color(0.62f, 0.95f, 0.20f); // R6.4: matches the lime Crawler
        [SerializeField] private int   crawlerDeathParticles = 8;
        [SerializeField] private float crawlerDeathDuration  = 0.28f;

        [Tooltip("Boomer death: bigger and brighter than the Crawler's — it's an amplifier, it should read as one.")]
        [SerializeField] private Color boomerDeathColor     = new Color(0.80f, 0.60f, 1.00f); // R6.4: matches the violet Boomer
        [SerializeField] private int   boomerDeathParticles = 18;
        [SerializeField] private float boomerDeathDuration  = 0.5f;

        [Tooltip("The flash when a dormant Crawler wakes up.")]
        [SerializeField] private Color enemyWakeColor    = new Color(0.85f, 1f, 0.6f);
        [SerializeField] private float enemyWakeDuration = 0.25f;

        [Tooltip("Halo colour of a live Boomer — it pulses until something kills it.")]
        [SerializeField] private Color boomerGlowColor = new Color(0.66f, 0.38f, 1.00f); // R6.4: violet, not fuse-orange — a lit bomb and a Boomer must not look alike
        [SerializeField] private float boomerGlowScale = 1.1f;

        [Header("— Bomb Fuse Telegraph (v3) —")]
        [Tooltip("Glow colour of a freshly-armed bomb (plenty of fuse left).")]
        [SerializeField] private Color fuseCalmColor = new Color(1f, 0.35f, 0.12f);
        [Tooltip("Glow colour just before detonation.")]
        [SerializeField] private Color fuseHotColor  = new Color(1f, 0.95f, 0.55f);
        [Tooltip("Base halo size around the bomb cell, in cells.")]
        [SerializeField] private float fuseGlowScale = 1.15f;

        [Header("— Blast Zone Preview (R6.13) —")]
        [Tooltip("Frame colour on the cells a lit bomb's own cross will hit.")]
        [SerializeField] private Color blastZoneColor  = new Color(1f, 0.22f, 0.12f);
        [Tooltip("Frame colour on the cells reached only through the chain (bombs it sets off).")]
        [SerializeField] private Color blastChainColor = new Color(1f, 0.92f, 0.45f);

        // Block palette — matches BoardView so debris reads as "that chunk".
        private static readonly Color ColBlockA = new Color(0.94f, 0.62f, 0.15f);
        private static readonly Color ColBlockB = new Color(0.11f, 0.62f, 0.46f);
        private static readonly Color ColBlockC = new Color(0.83f, 0.33f, 0.49f);

        private AvatarModel _avatar;
        private CollapseSystem _collapse;
        private ChainTracker _chain;
        private BombSystem _bombs;
        private GridModel _grid;
        private GravitySystem _gravity;
        private MomentumTracker _momentum;   // persistent — MUST be unsubscribed in OnDestroy (this view is per board)
        private FissureTracker  _fissures;   // per board
        private AirSystem       _air;        // persistent
        private AvatarView _avatarView;

        private SpriteRenderer _glow;
        private Coroutine _glowFadeRoutine;

        private SpriteRenderer _flash;          // full-screen bomb chain flash
        private Coroutine _flashRoutine;

        private float _trailTimer;
        private Color _trailColor = Color.white; // colour of the last drilled block (§M8)

        // Tier 2 fissure overlays, one per cracked cell; polled each frame so a drilled / broken /
        // fallen block takes its crack with it (fissures are keyed by position, §M2).
        private readonly Dictionary<GridPos, SpriteRenderer> _fissureOverlays = new Dictionary<GridPos, SpriteRenderer>();
        private readonly List<GridPos> _fissureStale = new List<GridPos>();
        private static Sprite _fissureSprite1, _fissureSprite2;

        // Danger Zone vignette — parented to the camera (the board scrolls under it), torn down with this view.
        private SpriteRenderer _vignette;
        private float _vignetteLevel;   // 0 → 1 eased presence
        private static Sprite _vignetteSprite;

        // One pulsing halo per armed bomb, keyed by its grid cell. FuseProgress feeds the fraction;
        // Update() animates the pulse; a bomb that stops reporting (detonated, disarmed, or shifted by
        // a Perfect Clear) is culled by its LastSeen timestamp.
        private readonly System.Collections.Generic.Dictionary<GridPos, FuseGlow> _fuses =
            new System.Collections.Generic.Dictionary<GridPos, FuseGlow>();
        private static Material _fuseMaterial;
        private static bool _fuseMaterialTried;

        // R6.13 (F03): one frame per cell a lit bomb will hit — BombSystem.PredictBlastZone, merged
        // across every burning fuse, rebuilt each frame (a handful of cells) so it follows Perfect
        // Clear shifts and newly lit chain bombs at once. Pooled by cell; unused frames are disabled.
        private readonly Dictionary<GridPos, SpriteRenderer> _zoneCells = new Dictionary<GridPos, SpriteRenderer>();
        private readonly Dictionary<GridPos, int>   _zoneDepth = new Dictionary<GridPos, int>();
        private readonly Dictionary<GridPos, float> _zoneFrac  = new Dictionary<GridPos, float>();
        private readonly Dictionary<GridPos, int>   _zoneOne   = new Dictionary<GridPos, int>();
        private readonly List<GridPos> _armedCells = new List<GridPos>();
        private bool _zoneShown;
        private static Sprite _zoneFrameSprite;

        private sealed class FuseGlow
        {
            public SpriteRenderer Sr;
            public float Frac;      // 0 → 1, 1 = detonation
            public float LastSeen;  // Time.time of the last FuseProgress for this cell
        }

        private EnemySystem _enemies;

        /// <summary>
        /// Last known type + cell for every living enemy, keyed by id.
        ///
        /// EnemySystem's events don't carry enough to draw with: EnemyActivated is just an id, and
        /// EnemyKilled has the type but no position (by the time it fires the enemy is already dead,
        /// so it can't be looked up either). Rather than widen two Core events for a purely visual
        /// need, the view keeps its own cache — the same shape as the _fuses dictionary above, and
        /// the same idea: per-entity view state that Core has no reason to carry.
        ///
        /// Refreshed on a timer rather than every frame: GetAllAlive() allocates a fresh list, and a
        /// Crawler only steps every CrawlerMoveInterval (0.8 s), so 0.2 s is always well inside one
        /// move and costs 5 allocations/second instead of 60.
        /// </summary>
        private readonly System.Collections.Generic.Dictionary<int, EnemyMarker> _enemyMarkers =
            new System.Collections.Generic.Dictionary<int, EnemyMarker>();
        private float _enemyCacheTimer;
        private const float EnemyCacheRefresh = 0.2f;

        private struct EnemyMarker
        {
            public EnemyType Type;
            public GridPos   Pos;
        }

        // One pulsing halo per ACTIVE Boomer, keyed by enemy id (not by cell: a Boomer never moves,
        // but the id is what the kill event gives us to tear it down with).
        private readonly System.Collections.Generic.Dictionary<int, SpriteRenderer> _boomerGlows =
            new System.Collections.Generic.Dictionary<int, SpriteRenderer>();

        public void Init(AvatarModel avatar, CollapseSystem collapse, ChainTracker chain, BombSystem bombs,
                         GridModel grid, GravitySystem gravity = null, MomentumTracker momentum = null,
                         AvatarView avatarView = null, EnemySystem enemies = null,
                         FissureTracker fissures = null, AirSystem air = null)
        {
            _avatar     = avatar;
            _collapse   = collapse;
            _chain      = chain;
            _bombs      = bombs;
            _grid       = grid;
            _gravity    = gravity;
            _momentum   = momentum;
            _fissures   = fissures;
            _air        = air;
            _avatarView = avatarView;
            _enemies    = enemies;

            _avatar.Drilled          += OnDrilled;
            _collapse.PerfectClear   += OnPerfectClear;
            _chain.LinkAdded         += OnLinkAdded;
            _chain.ChainCompleted   += OnChainCompleted;
            _bombs.BombExploded      += OnBombExploded;
            _bombs.BombScored        += OnBombScored;
            _bombs.FuseProgress      += OnFuseProgress;

            if (_gravity != null)
            {
                _gravity.ChunkBurst       += OnChunkBurst;
                _gravity.DiamondLiberated += OnDiamondLiberated;
            }
            _bombs.DiamondLiberated += OnDiamondLiberated;

            if (_momentum != null)
            {
                _momentum.TierChanged         += OnTierChanged;
                _momentum.PowerDrillActivated += OnPowerDrill;
                OnTierChanged(0, _momentum.CurrentTier); // momentum survives an endless seam (R7.6)
            }
            if (_fissures != null)
            {
                _fissures.FissureAdded += OnFissureAdded;
                _fissures.FissureBroke += OnFissureBroke;
            }

            if (_enemies != null)
            {
                _enemies.EnemySpawned    += OnEnemySpawned;
                _enemies.EnemyActivated  += OnEnemyActivated;
                _enemies.EnemyKilled     += OnEnemyKilled;
                _enemies.BoomerDetonated += OnBoomerDetonated;
            }

            BuildGlowSprite();
            BuildFlashSprite();
            BuildVignette();
        }

        private void OnDestroy()
        {
            if (_avatar != null)   _avatar.Drilled -= OnDrilled;
            if (_collapse != null) _collapse.PerfectClear -= OnPerfectClear;
            if (_chain != null)
            {
                _chain.LinkAdded -= OnLinkAdded;
                _chain.ChainCompleted -= OnChainCompleted;
            }
            if (_bombs != null)
            {
                _bombs.BombExploded -= OnBombExploded;
                _bombs.BombScored   -= OnBombScored;
                _bombs.FuseProgress -= OnFuseProgress;
                _bombs.DiamondLiberated -= OnDiamondLiberated;
            }
            if (_gravity != null)
            {
                _gravity.ChunkBurst       -= OnChunkBurst;
                _gravity.DiamondLiberated -= OnDiamondLiberated;
            }
            if (_momentum != null)
            {
                _momentum.TierChanged         -= OnTierChanged;
                _momentum.PowerDrillActivated -= OnPowerDrill;
            }
            if (_fissures != null)
            {
                _fissures.FissureAdded -= OnFissureAdded;
                _fissures.FissureBroke -= OnFissureBroke;
            }
            if (_vignette != null)
                Destroy(_vignette.gameObject); // lives under the camera, not under this view
            if (_enemies != null)
            {
                _enemies.EnemySpawned    -= OnEnemySpawned;
                _enemies.EnemyActivated  -= OnEnemyActivated;
                _enemies.EnemyKilled     -= OnEnemyKilled;
                _enemies.BoomerDetonated -= OnBoomerDetonated;
            }
        }

        private void Update()
        {
            AnimateFuses();
            AnimateBlastZones();
            RefreshEnemyMarkers();
            AnimateBoomerGlows();

            PruneFissureOverlays();
            AnimateVignette();
            TickMomentumTrail();
        }

        /// <summary>
        /// R7.10 Tier 1+ trail (§M8): a fading breadcrumb behind the driller, in the colour of the last
        /// drilled block, denser at each tier (interval ÷ tier).
        /// </summary>
        private void TickMomentumTrail()
        {
            int tier = _momentum?.CurrentTier ?? 0;
            if (tier < 1 || _avatarView == null)
                return;

            _trailTimer -= Time.deltaTime;
            if (_trailTimer > 0f)
                return;

            _trailTimer = trailInterval / tier;
            GameObject go = NewParticle("MomentumTrail", _avatarView.transform.localPosition,
                                        _trailColor, 0.7f, 6);
            go.transform.localScale = Vector3.one * (0.35f + 0.08f * tier);
            StartCoroutine(FadeAndScale(go.transform, go.GetComponent<SpriteRenderer>(), trailDuration, 0.1f));
        }

        // ── Event handlers ──────────────────────────────────────────────

        private void OnDrilled(GridPos cell, CellType oldType, DrillDirection direction)
        {
            if (oldType.CanFuse()) _trailColor = BlockColor(oldType); // non-colour blocks keep the last colour
            SpawnFlash(cell);
            if (oldType == CellType.Diamond) SpawnDiamondSparkle(cell);
        }

        /// <summary>Shared by drill, bomb blast and burst shockwave — however the diamond was freed.</summary>
        private void OnDiamondLiberated(GridPos pos) => SpawnDiamondSparkle(pos);

        private void OnPerfectClear(int row) => SpawnDustRow(row);

        private void OnBombExploded(GridPos pos)
        {
            RemoveFuse(pos); // the telegraph's job is done — the blast VFX takes over
            SpawnBurst(pos);
            SpawnRipple(BoardView.ToLocal(pos), burstColor);
        }

        /// <summary>
        /// Bomb fuse telegraph (GDD §4.8 readability contract): record the fuse fraction for this
        /// cell; Update() turns it into a halo that pulses faster and whiter as detonation nears.
        /// </summary>
        private void OnFuseProgress(GridPos pos, float frac)
        {
            if (!_fuses.TryGetValue(pos, out FuseGlow g))
            {
                g = CreateFuseGlow(pos);
                _fuses[pos] = g;
            }
            g.Frac     = Mathf.Clamp01(frac);
            g.LastSeen = Time.time;
        }

        /// <summary>Sympathetic detonations flash the screen; brightness rises with the chain.</summary>
        private void OnBombScored(int destroyed, int chainMult)
        {
            if (chainMult <= 1) return;

            float intensity = Mathf.Clamp01(0.12f + (chainMult - 1) * 0.10f);
            ShowScreenFlash(chainFlashColor, intensity);
        }

        /// <summary>
        /// Chunk burst: debris tinted with the chunk's own color, scaled by how many cells died,
        /// plus a ripple from the middle of the wreckage.
        /// </summary>
        private void OnChunkBurst(System.Collections.Generic.List<GridPos> cells, int fallDistance, CellType color)
        {
            Color tint = BlockColor(color);

            int budget = Mathf.Min(cells.Count * debrisPerCell, debrisMax);
            if (budget <= 0) return;

            // Spread the budget over the cells so a big chunk throws more debris, not denser debris.
            for (int i = 0; i < budget; i++)
            {
                GridPos cell = cells[i % cells.Count];
                Vector3 origin = BoardView.ToLocal(cell)
                                 + new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(-0.3f, 0.3f), 0f);

                GameObject go = NewParticle("BurstDebris", origin, tint, 0.85f, 5);
                go.transform.localScale = Vector3.one * Random.Range(0.18f, 0.4f);

                float angle = Random.Range(0f, Mathf.PI * 2f);
                // Harder landings throw debris further.
                float speed = Random.Range(1.2f, 2.6f) * (1f + fallDistance * 0.12f);
                var drift = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * speed;

                StartCoroutine(FadeAndDrift(go.transform, go.GetComponent<SpriteRenderer>(),
                                            debrisDuration, drift));
            }

            SpawnRipple(CenterOf(cells), tint);
        }

        // ── v3.2 momentum (R7.10) ────────────────────────────────────────

        /// <summary>The avatar takes the tier colour — the same yellow / orange / red as the HUD counter.</summary>
        private void OnTierChanged(int from, int to)
        {
            if (_avatarView == null) return;
            if (to <= 0)
            {
                _avatarView.ClearStreakTint();
                return;
            }
            _avatarView.SetStreakTint(TierColor(to), Mathf.Lerp(0.45f, 1f, (to - 1) / 2f));
        }

        /// <summary>Tier 3: white screen flash + a wide white ring off the driller (§M8). Shake is GameBootstrap's.</summary>
        private void OnPowerDrill(float mult)
        {
            ShowScreenFlash(powerFlashColor, powerFlashIntensity);
            Vector3 at = _avatarView != null ? _avatarView.transform.localPosition : BoardView.ToLocal(_avatar.Position);
            SpawnRipple(at, Color.white);
            SpawnRipple(at + new Vector3(0f, -1f, 0f), Color.white);
        }

        /// <summary>Tier 2 crack overlay on a block (§M2). The 2nd fissure breaks the block, so only count 1 lands here.</summary>
        private void OnFissureAdded(GridPos pos, int count)
        {
            if (!_fissureOverlays.TryGetValue(pos, out SpriteRenderer sr))
            {
                var go = new GameObject("Fissure");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = BoardView.ToLocal(pos);
                // Random quarter-turn so a cracked wall doesn't repeat one pattern.
                go.transform.localRotation = Quaternion.Euler(0f, 0f, 90f * ((pos.X * 7 + pos.Y * 13) & 3));
                sr = go.AddComponent<SpriteRenderer>();
                sr.sortingOrder = 3; // over the tile (0) and exit glow (1), under ripples (4) and the avatar (10)
                _fissureOverlays[pos] = sr;
            }
            sr.sprite = count >= 2 ? GetFissureSprite(2) : GetFissureSprite(1);
            sr.color  = new Color(fissureColor.r, fissureColor.g, fissureColor.b, 0.9f);
        }

        /// <summary>The block snapped: crumbs + a small ripple, and the overlay goes with it.</summary>
        private void OnFissureBroke(GridPos pos)
        {
            RemoveFissureOverlay(pos);
            Vector3 origin = BoardView.ToLocal(pos);
            for (int i = 0; i < 8; i++)
            {
                GameObject go = NewParticle("FissureCrumb", origin + new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(-0.3f, 0.3f), 0f),
                                            new Color(0.75f, 0.70f, 0.62f), 0.9f, 5);
                go.transform.localScale = Vector3.one * Random.Range(0.12f, 0.25f);
                var drift = new Vector3(Random.Range(-0.8f, 0.8f), Random.Range(-1.4f, -0.4f), 0f); // crumbs fall
                StartCoroutine(FadeAndDrift(go.transform, go.GetComponent<SpriteRenderer>(), 0.4f, drift));
            }
            SpawnRipple(origin, new Color(0.85f, 0.80f, 0.70f));
        }

        /// <summary>Drops overlays whose block is gone (drilled, blasted, fell) — the tracker reads 0 there.</summary>
        private void PruneFissureOverlays()
        {
            if (_fissureOverlays.Count == 0 || _fissures == null) return;
            _fissureStale.Clear();
            foreach (var kv in _fissureOverlays)
                if (_fissures.GetFissures(kv.Key) == 0)
                    _fissureStale.Add(kv.Key);
            foreach (GridPos p in _fissureStale)
                RemoveFissureOverlay(p);
        }

        private void RemoveFissureOverlay(GridPos pos)
        {
            if (!_fissureOverlays.TryGetValue(pos, out SpriteRenderer sr)) return;
            if (sr != null) Destroy(sr.gameObject);
            _fissureOverlays.Remove(pos);
        }

        /// <summary>
        /// Procedural hairline crack, 16×16 (§M8 "hairline cracks, overlay sprite"). Level 1: one jagged line
        /// from a corner toward the centre with a short branch; level 2: a second line from the opposite side.
        /// White pixels, tinted by fissureColor — dark on every block colour.
        /// </summary>
        private static Sprite GetFissureSprite(int level)
        {
            if (level >= 2 && _fissureSprite2 != null) return _fissureSprite2;
            if (level < 2 && _fissureSprite1 != null) return _fissureSprite1;

            const int n = 16;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px  = new Color32[n * n];
            void Dot(int x, int y) { if (x >= 0 && x < n && y >= 0 && y < n) px[y * n + x] = new Color32(255, 255, 255, 255); }
            void Path(int[] pts) { for (int i = 0; i + 1 < pts.Length; i += 2) Dot(pts[i], pts[i + 1]); }

            // Main crack: top-left corner zig-zagging to the centre, plus a branch.
            Path(new[] { 1,14, 2,13, 3,13, 4,12, 5,11, 5,10, 6,9, 7,9, 8,8, 9,7 });
            Path(new[] { 5,11, 6,12, 7,12, 8,13 });
            if (level >= 2)
            {
                // Second crack from the bottom-right, meeting the first — the block is about to go.
                Path(new[] { 14,1, 13,2, 13,3, 12,4, 11,5, 10,5, 10,6, 9,7 });
                Path(new[] { 12,4, 13,5, 14,6 });
                Path(new[] { 8,8, 7,7, 6,6, 6,5 });
            }

            tex.SetPixels32(px);
            tex.Apply();
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
            if (level >= 2) _fissureSprite2 = sprite; else _fissureSprite1 = sprite;
            return sprite;
        }

        // ── Danger Zone vignette (R7.10, §M3.4) ──────────────────────────

        /// <summary>
        /// Red edges, clear centre, parented to the camera and sized to its orthographic view each frame
        /// (the camera follows the avatar). Fades in/out, pulses at ~1 Hz — in step with the HUD badge.
        /// </summary>
        private void AnimateVignette()
        {
            if (_vignette == null || _air == null) return;

            // Unscaled: entering the zone right before a pause still shows it (the pulse itself uses scaled time).
            _vignetteLevel = Mathf.MoveTowards(_vignetteLevel, _air.IsDangerZone ? 1f : 0f, vignetteFadeSpeed * Time.unscaledDeltaTime);
            if (_vignetteLevel <= 0f)
            {
                _vignette.enabled = false;
                return;
            }

            Camera cam = Camera.main;
            if (cam != null && _vignette.transform.parent != cam.transform)
                _vignette.transform.SetParent(cam.transform, false);
            if (cam != null && cam.orthographic)
            {
                float h = cam.orthographicSize * 2f;
                _vignette.transform.localScale = new Vector3(h * cam.aspect, h, 1f);
            }

            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 2f * Mathf.PI);
            float a = Mathf.Lerp(vignetteAlpha.x, vignetteAlpha.y, pulse) * _vignetteLevel;
            _vignette.color   = new Color(vignetteColor.r, vignetteColor.g, vignetteColor.b, a);
            _vignette.enabled = true;
        }

        private void BuildVignette()
        {
            var go = new GameObject("DangerVignette");
            Camera cam = Camera.main;
            if (cam != null) go.transform.SetParent(cam.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, 1f); // just in front of the camera

            _vignette = go.AddComponent<SpriteRenderer>();
            _vignette.sprite       = GetVignetteSprite();
            _vignette.sortingOrder = 30; // over the board, the avatar and the chain flash (20)
            _vignette.enabled      = false;
        }

        /// <summary>64×64 radial ramp, alpha 0 inside ~62 % of the centre-to-corner distance, smoothstepped to 1 at the corners.</summary>
        private static Sprite GetVignetteSprite()
        {
            if (_vignetteSprite != null) return _vignetteSprite;

            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px  = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f;
                    float dy = (y + 0.5f) / n * 2f - 1f;
                    float d  = Mathf.Sqrt(dx * dx + dy * dy) / 1.4142f; // 0 centre → 1 corner
                    float t  = Mathf.Clamp01((d - 0.62f) / (1.00f - 0.62f)); // first screenshot: 0.38 reddened the whole screen
                    byte  al = (byte)(255f * t * t * (3f - 2f * t));
                    px[y * n + x] = new Color32(255, 255, 255, al);
                }
            tex.SetPixels32(px);
            tex.Apply();
            _vignetteSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
            return _vignetteSprite;
        }

        private void OnLinkAdded(int chainStep)
        {
            if (chainStep < 2) return; // R7.5b: bursts are links now — a single one is not a cascade
            if (_glowFadeRoutine != null)
            {
                StopCoroutine(_glowFadeRoutine);
                _glowFadeRoutine = null;
            }
            float alpha = Mathf.Clamp01(0.15f + chainStep * 0.15f);
            _glow.color = new Color(glowColor.r, glowColor.g, glowColor.b, alpha);
            _glow.gameObject.SetActive(true);
        }

        private void OnChainCompleted(int _) => _glowFadeRoutine = StartCoroutine(FadeGlow());

        // ── Effects ──────────────────────────────────────────────────────

        private void SpawnFlash(GridPos cell)
        {
            GameObject go = NewParticle("DrillFlash", BoardView.ToLocal(cell), flashColor, 1f, 5);
            StartCoroutine(FadeAndScale(go.transform, go.GetComponent<SpriteRenderer>(), flashDuration, 1.6f));
        }

        private void SpawnDustRow(int row)
        {
            for (int i = 0; i < dustParticlesPerRow; i++)
            {
                float x = Random.Range(-0.5f, _grid.Width - 0.5f);
                var pos = new Vector3(x, -row + Random.Range(-0.2f, 0.2f), 0f);
                GameObject go = NewParticle("Dust", pos, dustColor, 0.4f, 3);
                var drift = new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(0.2f, 0.8f), 0f);
                StartCoroutine(FadeAndDrift(go.transform, go.GetComponent<SpriteRenderer>(), dustDuration, drift));
            }
        }

        private void SpawnBurst(GridPos center)
        {
            Vector3 origin = BoardView.ToLocal(center);
            for (int i = 0; i < burstParticles; i++)
            {
                GameObject go = NewParticle("Burst", origin, burstColor, 0.6f, 4);
                float angle = i * (360f / burstParticles) * Mathf.Deg2Rad;
                var drift = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * Random.Range(1.5f, 2.5f);
                StartCoroutine(FadeAndDrift(go.transform, go.GetComponent<SpriteRenderer>(), burstDuration, drift));
            }
        }

        /// <summary>
        /// R4: a bright twinkling burst on diamond collection, however it happened (drill, bomb
        /// blast, burst shockwave — see OnDrilled / OnDiamondLiberated). Distinct in color (icy
        /// white-blue, matching BoardView's diamondColor) from every other burst so it reads as a
        /// pickup, not a destruction.
        /// </summary>
        private void SpawnDiamondSparkle(GridPos cell)
        {
            Vector3 origin = BoardView.ToLocal(cell);
            for (int i = 0; i < diamondSparkleParticles; i++)
            {
                GameObject go = NewParticle("DiamondSparkle", origin, diamondSparkleColor, 0.95f, 7);
                go.transform.localScale = Vector3.one * Random.Range(0.12f, 0.28f);

                float angle = Random.Range(0f, Mathf.PI * 2f);
                var drift = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * Random.Range(1.0f, 2.0f);
                StartCoroutine(FadeAndDrift(go.transform, go.GetComponent<SpriteRenderer>(),
                                            diamondSparkleDuration, drift));
            }

            SpawnRipple(origin, diamondSparkleColor);
        }

        // ── Enemies (R5.15) ──────────────────────────────────────────────

        private void OnEnemySpawned(int id, EnemyType type, GridPos pos)
        {
            _enemyMarkers[id] = new EnemyMarker { Type = type, Pos = pos };
        }

        /// <summary>
        /// A buried enemy woke up. A Crawler gets a one-shot flash (it's about to start moving);
        /// a Boomer instead lights a permanent pulsing halo — it never moves, so the standing threat
        /// (and opportunity) has to read continuously, not for a quarter second.
        /// </summary>
        private void OnEnemyActivated(int id)
        {
            if (!_enemyMarkers.TryGetValue(id, out EnemyMarker marker))
                return;

            if (marker.Type == EnemyType.Crawler)
                SpawnWakeFlash(marker.Pos);
            else
                AddBoomerGlow(id, marker.Pos);
        }

        private void OnEnemyKilled(int id, EnemyType type, KillMethod method, int bonus)
        {
            // The cached cell is the last one seen while it was alive — EnemyKilled carries no
            // position, and the enemy is already dead so it can't be looked up any more.
            if (_enemyMarkers.TryGetValue(id, out EnemyMarker marker))
            {
                if (type == EnemyType.Crawler)
                    SpawnCrawlerDeath(marker.Pos);
                // A Boomer's own death burst is drawn by OnBoomerDetonated, which knows the real
                // position — but a Boomer killed by a plain crush never detonates, so it would
                // otherwise die silently. Cover that case here.
                else if (!_boomerGlows.ContainsKey(id) || method == KillMethod.Crush)
                    SpawnBoomerDeath(marker.Pos);

                _enemyMarkers.Remove(id);
            }

            RemoveBoomerGlow(id);
        }

        /// <summary>The Boomer's payoff: a bigger, brighter burst plus its own shockwave ripple.</summary>
        private void OnBoomerDetonated(GridPos pos, int parentBonus, int blocksDestroyed)
        {
            SpawnBoomerDeath(pos);
        }

        private void SpawnCrawlerDeath(GridPos cell)
        {
            Vector3 origin = BoardView.ToLocal(cell);
            for (int i = 0; i < crawlerDeathParticles; i++)
            {
                GameObject go = NewParticle("CrawlerDeath", origin, crawlerDeathColor, 0.85f, 6);
                go.transform.localScale = Vector3.one * Random.Range(0.14f, 0.26f);

                float angle = Random.Range(0f, Mathf.PI * 2f);
                var drift = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * Random.Range(0.8f, 1.6f);
                StartCoroutine(FadeAndDrift(go.transform, go.GetComponent<SpriteRenderer>(),
                                            crawlerDeathDuration, drift));
            }
        }

        private void SpawnBoomerDeath(GridPos cell)
        {
            Vector3 origin = BoardView.ToLocal(cell);
            for (int i = 0; i < boomerDeathParticles; i++)
            {
                GameObject go = NewParticle("BoomerDeath", origin, boomerDeathColor, 0.95f, 7);
                go.transform.localScale = Vector3.one * Random.Range(0.2f, 0.42f);

                float angle = i * (360f / boomerDeathParticles) * Mathf.Deg2Rad;
                var drift = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * Random.Range(1.8f, 3.0f);
                StartCoroutine(FadeAndDrift(go.transform, go.GetComponent<SpriteRenderer>(),
                                            boomerDeathDuration, drift));
            }

            SpawnRipple(origin, boomerDeathColor);
        }

        private void SpawnWakeFlash(GridPos cell)
        {
            GameObject go = NewParticle("EnemyWake", BoardView.ToLocal(cell), enemyWakeColor, 0.9f, 7);
            go.transform.localScale = Vector3.one * 0.9f;
            StartCoroutine(FadeAndScale(go.transform, go.GetComponent<SpriteRenderer>(),
                                        enemyWakeDuration, 1.5f));
        }

        private void AddBoomerGlow(int id, GridPos pos)
        {
            if (_boomerGlows.ContainsKey(id))
                return;

            var go = new GameObject("BoomerGlow");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = BoardView.ToLocal(pos);
            go.transform.localScale    = Vector3.one * boomerGlowScale;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite       = BoardView.GetUnitSprite();
            sr.sortingOrder = 6;

            // Same additive radial glow the bomb fuse uses — a Boomer IS a bomb with legs, so
            // borrowing its material keeps the visual language consistent (and costs nothing).
            Material mat = EnsureFuseMaterial();
            if (mat != null)
                sr.sharedMaterial = mat;

            sr.color = new Color(boomerGlowColor.r, boomerGlowColor.g, boomerGlowColor.b, 0f);
            _boomerGlows[id] = sr;
        }

        private void RemoveBoomerGlow(int id)
        {
            if (!_boomerGlows.TryGetValue(id, out SpriteRenderer sr))
                return;
            if (sr != null)
                Destroy(sr.gameObject);
            _boomerGlows.Remove(id);
        }

        /// <summary>A steady breathing pulse — slower and calmer than a lit fuse, which is racing.</summary>
        private void AnimateBoomerGlows()
        {
            if (_boomerGlows.Count == 0)
                return;

            float phase = Mathf.Sin(Time.time * 3f * Mathf.PI) * 0.5f + 0.5f; // 0 → 1, ~1.5 Hz

            foreach (var kvp in _boomerGlows)
            {
                SpriteRenderer sr = kvp.Value;
                if (sr == null)
                    continue;

                sr.color = new Color(boomerGlowColor.r, boomerGlowColor.g, boomerGlowColor.b,
                                     Mathf.Lerp(0.30f, 0.75f, phase));
                sr.transform.localScale = Vector3.one * boomerGlowScale * Mathf.Lerp(0.9f, 1.1f, phase);
            }
        }

        /// <summary>Keeps the id → cell cache current so Crawler death bursts land where it actually is.</summary>
        private void RefreshEnemyMarkers()
        {
            if (_enemies == null)
                return;

            _enemyCacheTimer -= Time.deltaTime;
            if (_enemyCacheTimer > 0f)
                return;
            _enemyCacheTimer = EnemyCacheRefresh;

            foreach (EnemyEntity e in _enemies.GetAllAlive())
                _enemyMarkers[e.Id] = new EnemyMarker { Type = e.Type, Pos = e.Position };
        }

        /// <summary>Expanding ring, 1 cell of radius, that reads as the shockwave's reach.</summary>
        private void SpawnRipple(Vector3 localCenter, Color tint)
        {
            var go = new GameObject("Shockwave");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localCenter;
            go.transform.localScale    = Vector3.one * 0.4f;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite       = BoardView.GetUnitSprite();
            sr.sortingOrder = 4;
            sr.color        = new Color(tint.r, tint.g, tint.b, rippleColor.a);

            StartCoroutine(FadeAndScale(go.transform, sr, rippleDuration, rippleEndSize));
        }

        /// <summary>Full-board flash — bomb chains (warm) and the Power Drill (white, R7.10).</summary>
        private void ShowScreenFlash(Color color, float intensity)
        {
            if (_flashRoutine != null)
            {
                StopCoroutine(_flashRoutine);
                _flashRoutine = null;
            }
            _flash.color = new Color(color.r, color.g, color.b, intensity);
            _flash.gameObject.SetActive(true);
            _flashRoutine = StartCoroutine(FadeFlash());
        }

        // ── Bomb fuse telegraph ────────────────────────────────────────────

        /// <summary>
        /// Animate every armed bomb's halo: a sine pulse whose frequency and brightness rise with the
        /// fuse fraction, colour ramping calm→hot→white — the accelerating "flash" half of GDD §4.8.
        /// Halos that stopped reporting (detonated, disarmed, or shifted by a Perfect Clear) time out.
        /// </summary>
        private void AnimateFuses()
        {
            if (_fuses.Count == 0)
                return;

            float now = Time.time;
            System.Collections.Generic.List<GridPos> stale = null;

            foreach (var kvp in _fuses)
            {
                FuseGlow g = kvp.Value;
                if (g.Sr == null || now - g.LastSeen > 0.25f)
                {
                    (stale ??= new System.Collections.Generic.List<GridPos>()).Add(kvp.Key);
                    continue;
                }

                float frac  = g.Frac;
                float freq  = Mathf.Lerp(2.5f, 12f, frac);                       // flashes speed up
                float phase = Mathf.Sin(now * freq * Mathf.PI * 2f) * 0.5f + 0.5f; // 0 → 1
                float alpha = Mathf.Lerp(0.35f, 1f, frac) * Mathf.Lerp(0.35f, 1f, phase);

                Color c = Color.Lerp(fuseCalmColor, fuseHotColor, frac);
                c = Color.Lerp(c, Color.white, phase * frac); // whitens on the peak, more so near the end
                g.Sr.color = new Color(c.r, c.g, c.b, alpha);

                float s = fuseGlowScale * Mathf.Lerp(0.85f, 1.25f, phase) * Mathf.Lerp(1f, 1.15f, frac);
                g.Sr.transform.localScale = Vector3.one * s;
            }

            if (stale != null)
                foreach (GridPos p in stale)
                    RemoveFuse(p);
        }

        /// <summary>
        /// R6.13 (F03): frame every cell the lit bombs will hit — red for a bomb's own cross, orange
        /// for what its chain will reach. Pulse speeds up with the fuse (same ramp as the halo), and
        /// the whole zone flares while the avatar stands inside it: that is the "step out" cue.
        /// </summary>
        private void AnimateBlastZones()
        {
            _armedCells.Clear();
            _bombs.CopyArmedCells(_armedCells);
            if (_armedCells.Count == 0 && !_zoneShown)
                return;

            _zoneDepth.Clear();
            _zoneFrac.Clear();
            foreach (GridPos bomb in _armedCells)
            {
                float frac = _fuses.TryGetValue(bomb, out FuseGlow g) ? g.Frac : 0f;
                _zoneOne.Clear();
                _bombs.PredictBlastZone(bomb, _zoneOne);
                foreach (var kv in _zoneOne)
                {
                    if (!_zoneDepth.TryGetValue(kv.Key, out int d) || kv.Value < d) _zoneDepth[kv.Key] = kv.Value;
                    if (!_zoneFrac.TryGetValue(kv.Key, out float f) || frac > f)    _zoneFrac[kv.Key]  = frac;
                }
            }

            bool  avatarInside = _zoneDepth.ContainsKey(_avatar.Position);
            float now = Time.time;

            foreach (var kv in _zoneDepth)
            {
                if (!_zoneCells.TryGetValue(kv.Key, out SpriteRenderer sr))
                {
                    sr = CreateZoneFrame(kv.Key);
                    _zoneCells[kv.Key] = sr;
                }

                float frac  = _zoneFrac[kv.Key];
                float freq  = Mathf.Lerp(2f, 10f, frac) * (avatarInside ? 1.5f : 1f);
                float phase = Mathf.Sin(now * freq * Mathf.PI * 2f) * 0.5f + 0.5f;
                float alpha = Mathf.Lerp(0.65f, 1f, frac) * Mathf.Lerp(0.7f, 1f, phase);
                Color c     = kv.Value == 0 ? blastZoneColor : blastChainColor;
                if (avatarInside)
                {
                    alpha = Mathf.Min(1f, alpha * 1.3f);
                    c     = Color.Lerp(c, Color.white, phase * 0.35f);
                }
                sr.color   = new Color(c.r, c.g, c.b, alpha);
                sr.enabled = true;
            }

            foreach (var kv in _zoneCells)
                if (!_zoneDepth.ContainsKey(kv.Key))
                    kv.Value.enabled = false;

            _zoneShown = _zoneDepth.Count > 0;
        }

        private SpriteRenderer CreateZoneFrame(GridPos cell)
        {
            var go = new GameObject("BlastZone");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = BoardView.ToLocal(cell);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite       = GetZoneFrameSprite();
            sr.sortingOrder = 5; // over tiles and the exit glow, under the fuse halo (6) and the avatar (10)
            return sr;
        }

        /// <summary>
        /// 16×16 danger frame: a 1 px black edge around a 2 px rim (tinted), and sparse diagonal
        /// hatching inside. The black edge is what keeps it readable on any block colour — a tinted
        /// rim alone vanished on pink (red frame) and amber (chain frame) in the first screenshot.
        /// </summary>
        private static Sprite GetZoneFrameSprite()
        {
            if (_zoneFrameSprite != null) return _zoneFrameSprite;

            const int n = 16;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px  = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int  edge  = Mathf.Min(Mathf.Min(x, y), Mathf.Min(n - 1 - x, n - 1 - y));
                    bool hatch = (x + y) % 6 < 2;
                    px[y * n + x] = edge == 0 ? new Color32(0, 0, 0, 220)        // black outline (tint can't lighten it)
                                  : edge <= 2 ? new Color32(255, 255, 255, 255)  // rim
                                  : hatch     ? new Color32(255, 255, 255, 80)   // danger stripes
                                              : new Color32(255, 255, 255, 0);
                }
            tex.SetPixels32(px);
            tex.Apply();

            _zoneFrameSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
            return _zoneFrameSprite;
        }

        private FuseGlow CreateFuseGlow(GridPos pos)
        {
            var go = new GameObject("FuseGlow");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = BoardView.ToLocal(pos);
            go.transform.localScale    = Vector3.one * fuseGlowScale;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite       = BoardView.GetUnitSprite();
            sr.sortingOrder = 6; // above the bomb tile, below the chain screen flash (20)

            Material mat = EnsureFuseMaterial();
            if (mat != null)
                sr.sharedMaterial = mat; // shared: the pulse is driven by SpriteRenderer.color, per-renderer

            sr.color = new Color(fuseCalmColor.r, fuseCalmColor.g, fuseCalmColor.b, 0f);
            return new FuseGlow { Sr = sr, Frac = 0f, LastSeen = Time.time };
        }

        private void RemoveFuse(GridPos pos)
        {
            if (!_fuses.TryGetValue(pos, out FuseGlow g))
                return;
            if (g.Sr != null)
                Destroy(g.Sr.gameObject);
            _fuses.Remove(pos);
        }

        /// <summary>
        /// Lazily build the shared additive-glow material from Resources/Shaders/FuseGlow. If the
        /// shader is missing the halo still renders as a plain pulsing tint (default sprite material),
        /// so the telegraph degrades gracefully rather than breaking.
        /// </summary>
        private static Material EnsureFuseMaterial()
        {
            if (_fuseMaterialTried)
                return _fuseMaterial;
            _fuseMaterialTried = true;

            Shader shader = Resources.Load<Shader>("Shaders/FuseGlow");
            if (shader != null)
                _fuseMaterial = new Material(shader) { name = "FuseGlow (runtime)" };
            return _fuseMaterial;
        }

        private static Vector3 CenterOf(System.Collections.Generic.List<GridPos> cells)
        {
            Vector3 sum = Vector3.zero;
            foreach (GridPos c in cells)
                sum += BoardView.ToLocal(c);
            return sum / cells.Count;
        }

        // Same tier colours as the HUD counter (R7.9) — the avatar and the "×N" must agree.
        private static readonly Color ColTier1 = new Color(1.00f, 0.90f, 0.30f);
        private static readonly Color ColTier2 = new Color(1.00f, 0.56f, 0.12f);
        private static readonly Color ColTier3 = new Color(1.00f, 0.22f, 0.16f);

        private static Color TierColor(int tier) => tier >= 3 ? ColTier3 : tier == 2 ? ColTier2 : ColTier1;

        private static Color BlockColor(CellType type)
        {
            switch (type)
            {
                case CellType.ColorA: return ColBlockA;
                case CellType.ColorB: return ColBlockB;
                case CellType.ColorC: return ColBlockC;
                default:              return Color.white;
            }
        }

        private void BuildFlashSprite()
        {
            var go = new GameObject("ChainFlash");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3((_grid.Width - 1) / 2f, -(_grid.Height - 1) / 2f, 0.4f);
            go.transform.localScale    = new Vector3(_grid.Width + 4f, _grid.Height + 4f, 1f);

            _flash = go.AddComponent<SpriteRenderer>();
            _flash.sprite       = BoardView.GetUnitSprite();
            _flash.sortingOrder = 20; // above the board and the avatar — it is a screen flash
            _flash.color        = new Color(chainFlashColor.r, chainFlashColor.g, chainFlashColor.b, 0f);
            go.SetActive(false);
        }

        private void BuildGlowSprite()
        {
            var go = new GameObject("ChainGlow");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3((_grid.Width - 1) / 2f, -(_grid.Height - 1) / 2f, 0.5f);
            go.transform.localScale = new Vector3(_grid.Width + 2f, _grid.Height + 2f, 1f);

            _glow = go.AddComponent<SpriteRenderer>();
            _glow.sprite = BoardView.GetUnitSprite();
            _glow.sortingOrder = -1;
            _glow.color = new Color(glowColor.r, glowColor.g, glowColor.b, 0f);
            go.SetActive(false);
        }

        private GameObject NewParticle(string name, Vector3 localPos, Color color, float alpha, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * 0.5f;

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = BoardView.GetUnitSprite();
            renderer.sortingOrder = sortingOrder;
            renderer.color = new Color(color.r, color.g, color.b, alpha);
            return go;
        }

        // ── Coroutines ───────────────────────────────────────────────────

        private static IEnumerator FadeAndScale(Transform t, SpriteRenderer sr, float duration, float endScale)
        {
            float startScale = t.localScale.x;
            Color startColor = sr.color;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float f = elapsed / duration;
                t.localScale = Vector3.one * Mathf.Lerp(startScale, endScale, f);
                sr.color = new Color(startColor.r, startColor.g, startColor.b, Mathf.Lerp(startColor.a, 0f, f));
                yield return null;
            }
            Destroy(t.gameObject);
        }

        private static IEnumerator FadeAndDrift(Transform t, SpriteRenderer sr, float duration, Vector3 drift)
        {
            Vector3 start = t.localPosition;
            Color startColor = sr.color;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float f = elapsed / duration;
                t.localPosition = start + drift * f;
                sr.color = new Color(startColor.r, startColor.g, startColor.b, Mathf.Lerp(startColor.a, 0f, f));
                yield return null;
            }
            Destroy(t.gameObject);
        }

        private IEnumerator FadeFlash()
        {
            Color start = _flash.color;
            float elapsed = 0f;
            while (elapsed < chainFlashDuration)
            {
                elapsed += Time.deltaTime;
                float f = elapsed / chainFlashDuration;
                _flash.color = new Color(start.r, start.g, start.b, Mathf.Lerp(start.a, 0f, f));
                yield return null;
            }
            _flash.gameObject.SetActive(false);
            _flashRoutine = null;
        }

        private IEnumerator FadeGlow()
        {
            Color startColor = _glow.color;
            float elapsed = 0f;
            while (elapsed < glowFadeDuration)
            {
                elapsed += Time.deltaTime;
                float f = elapsed / glowFadeDuration;
                _glow.color = new Color(startColor.r, startColor.g, startColor.b, Mathf.Lerp(startColor.a, 0f, f));
                yield return null;
            }
            _glow.gameObject.SetActive(false);
            _glowFadeRoutine = null;
        }
    }
}
