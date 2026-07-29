using System.Collections;
using HollowLines.Core;
using UnityEngine;

namespace HollowLines.View
{
    /// <summary>
    /// Gameplay VFX: drill flash, perfect-clear dust, bomb burst, chain glow, plus the v3
    /// effects — chunk-burst debris, shockwave ripples, streak glow/trail and bomb chain flash.
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

        [Header("— Streak Glow (v3) —")]
        [Tooltip("Streak step at which the avatar starts taking the streak color.")]
        [SerializeField] private int streakTintStep = 3;
        [Tooltip("Streak step at which the avatar starts leaving a particle trail.")]
        [SerializeField] private int streakTrailStep = 8;
        [SerializeField] private float trailInterval = 0.06f;
        [SerializeField] private float trailDuration = 0.35f;

        [Header("— Bomb Chain Flash (v3) —")]
        [SerializeField] private Color chainFlashColor = new Color(1f, 0.75f, 0.35f);
        [SerializeField] private float chainFlashDuration = 0.25f;

        [Header("— Diamond Collect (R4) —")]
        [SerializeField] private Color diamondSparkleColor = new Color(0.85f, 0.95f, 1f);
        [SerializeField] private int diamondSparkleParticles = 10;
        [SerializeField] private float diamondSparkleDuration = 0.45f;

        [Header("— Bomb Fuse Telegraph (v3) —")]
        [Tooltip("Glow colour of a freshly-armed bomb (plenty of fuse left).")]
        [SerializeField] private Color fuseCalmColor = new Color(1f, 0.35f, 0.12f);
        [Tooltip("Glow colour just before detonation.")]
        [SerializeField] private Color fuseHotColor  = new Color(1f, 0.95f, 0.55f);
        [Tooltip("Base halo size around the bomb cell, in cells.")]
        [SerializeField] private float fuseGlowScale = 1.15f;

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
        private StreakTracker _streak;
        private AvatarView _avatarView;

        private SpriteRenderer _glow;
        private Coroutine _glowFadeRoutine;

        private SpriteRenderer _flash;          // full-screen bomb chain flash
        private Coroutine _flashRoutine;

        private int   _currentStreak;
        private float _trailTimer;

        // One pulsing halo per armed bomb, keyed by its grid cell. FuseProgress feeds the fraction;
        // Update() animates the pulse; a bomb that stops reporting (detonated, disarmed, or shifted by
        // a Perfect Clear) is culled by its LastSeen timestamp.
        private readonly System.Collections.Generic.Dictionary<GridPos, FuseGlow> _fuses =
            new System.Collections.Generic.Dictionary<GridPos, FuseGlow>();
        private static Material _fuseMaterial;
        private static bool _fuseMaterialTried;

        private sealed class FuseGlow
        {
            public SpriteRenderer Sr;
            public float Frac;      // 0 → 1, 1 = detonation
            public float LastSeen;  // Time.time of the last FuseProgress for this cell
        }

        public void Init(AvatarModel avatar, CollapseSystem collapse, ChainTracker chain, BombSystem bombs,
                         GridModel grid, GravitySystem gravity = null, StreakTracker streak = null,
                         AvatarView avatarView = null)
        {
            _avatar     = avatar;
            _collapse   = collapse;
            _chain      = chain;
            _bombs      = bombs;
            _grid       = grid;
            _gravity    = gravity;
            _streak     = streak;
            _avatarView = avatarView;

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

            if (_streak != null)
            {
                _streak.StreakGrew   += OnStreakGrew;
                _streak.StreakBroken += OnStreakBroken;
            }

            BuildGlowSprite();
            BuildFlashSprite();
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
            if (_streak != null)
            {
                _streak.StreakGrew   -= OnStreakGrew;
                _streak.StreakBroken -= OnStreakBroken;
            }
        }

        private void Update()
        {
            AnimateFuses();

            // Streak trail: a fading breadcrumb behind the driller at high streaks.
            if (_currentStreak < streakTrailStep || _avatarView == null)
                return;

            _trailTimer -= Time.deltaTime;
            if (_trailTimer > 0f)
                return;

            _trailTimer = trailInterval;
            GameObject go = NewParticle("StreakTrail", _avatarView.transform.localPosition,
                                        StreakColor(), 0.7f, 6);
            go.transform.localScale = Vector3.one * 0.45f;
            StartCoroutine(FadeAndScale(go.transform, go.GetComponent<SpriteRenderer>(), trailDuration, 0.1f));
        }

        // ── Event handlers ──────────────────────────────────────────────

        private void OnDrilled(GridPos cell, CellType oldType)
        {
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
            ShowChainFlash(intensity);
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

        private void OnStreakGrew(int count)
        {
            _currentStreak = count;
            if (_avatarView == null) return;

            if (count < streakTintStep)
            {
                _avatarView.ClearStreakTint();
                return;
            }

            // Ramp the tint in from the first qualifying step up to the trail threshold.
            float t = Mathf.InverseLerp(streakTintStep, streakTrailStep, count);
            _avatarView.SetStreakTint(StreakColor(), Mathf.Lerp(0.45f, 1f, t));
        }

        private void OnStreakBroken(int _)
        {
            _currentStreak = 0;
            _avatarView?.ClearStreakTint();
        }

        private void OnLinkAdded(int chainStep)
        {
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

        private void ShowChainFlash(float intensity)
        {
            if (_flashRoutine != null)
            {
                StopCoroutine(_flashRoutine);
                _flashRoutine = null;
            }
            _flash.color = new Color(chainFlashColor.r, chainFlashColor.g, chainFlashColor.b, intensity);
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

        private Color StreakColor() => BlockColor(_streak?.CurrentColor ?? CellType.Empty);

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
