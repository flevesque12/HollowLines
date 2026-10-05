using System.Collections;
using HollowLines.Core;
using UnityEngine;

namespace HollowLines.View
{
    /// <summary>
    /// Renders the grid with one pooled SpriteRenderer per cell.
    /// Purely event-driven: subscribes to GridModel.CellChanged, never polls the whole board.
    /// ColorA/B/C/Empty/HardCracked reuse one neutral tile tinted per CellType (falls back to a
    /// generated white square if the art is missing); Hard/Steel/AirCapsule/Bomb have dedicated
    /// AI-generated sprites loaded from Resources/Tiles/ and render untinted.
    /// Grid (x, y) maps to world (x, -y) so row 0 stays at the top.
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        [Header("— Cell Colors —")]
        [SerializeField] private Color colorA        = new Color(0.94f, 0.62f, 0.15f); // amber
        [SerializeField] private Color colorB        = new Color(0.11f, 0.62f, 0.46f); // teal
        [SerializeField] private Color colorC        = new Color(0.83f, 0.33f, 0.49f); // pink
        [SerializeField] private Color hardColor     = new Color(0.55f, 0.45f, 0.35f); // brown
        [SerializeField] private Color crackedColor  = new Color(0.40f, 0.30f, 0.22f); // dark brown
        [SerializeField] private Color steelColor    = new Color(0.70f, 0.75f, 0.80f); // steel blue-grey
        [SerializeField] private Color capsuleColor  = new Color(0.35f, 0.85f, 0.95f); // cyan
        [SerializeField] private Color bombColor     = new Color(0.95f, 0.25f, 0.10f); // red-orange
        [SerializeField] private Color diamondColor  = new Color(0.80f, 0.92f, 1.00f); // icy white-blue gem
        [SerializeField] private Color emptyColor    = new Color(0.05f, 0.04f, 0.03f);

        [Header("— Exit Zone —")]
        [Tooltip("Warm light pooled in the EMPTY cells of the exit zone (campaign/tutorial only). Blocks are never tinted.")]
        [SerializeField] private Color exitGlowColor = new Color(1f, 0.82f, 0.45f, 1f);

        [Tooltip("How many rows above the board's bottom glow in their open cells.")]
        [Range(1, 10)]
        [SerializeField] private int exitGlowRows = 4;

        [Tooltip("Finish-line / margin-arrow gold.")]
        [SerializeField] private Color exitLineColor = new Color(1f, 0.84f, 0.30f, 1f);

        [Tooltip("The exit cues start to quicken when the avatar is this many rows above the finish line.")]
        [Range(4f, 30f)]
        [SerializeField] private float exitApproachRows = 12f;

        [Header("— Wobble Shake —")]
        [Tooltip("Horizontal shake amplitude of a wobbling cell, in world units.")]
        [Range(0f, 0.2f)]
        [SerializeField] private float wobbleAmplitude = 0.06f;

        [Tooltip("Shake frequency in oscillations per second.")]
        [Range(1f, 60f)]
        [SerializeField] private float wobbleFrequency = 24f;

        private GridModel _grid;
        private GravitySystem _gravity;
        private SpriteRenderer[,] _tiles;
        private static Sprite _unitSprite;

        // AI-generated tileset (flat vector style, fal.ai flux/schnell — session 2026-07-16).
        // Loaded from Assets/_Project/Art/Resources/Tiles/ so it works in both Editor and builds.
        private static Sprite _neutralSprite, _hardSprite, _steelSprite, _capsuleSprite, _bombSprite;
        private static bool _tilesetLoaded;

        // R4: Diamond tiles get their own material (DiamondShine, §5.10) instead of the default
        // Sprites-Default every other tile uses — a persistent shimmer, not a tint. Loaded lazily
        // and cached like VfxManager's EnsureFuseMaterial; missing shader → tiles stay plain-tinted.
        private static Material _defaultTileMaterial;
        private static Material _diamondMaterial;
        private static bool _diamondMaterialTried;

        // R6.5 (F05): air capsules shimmer too — a bonus to go get should not sit as still as a block.
        // Same DiamondShine shader, separate instance tuned slower/softer and cyan, so a capsule and a
        // diamond never twinkle alike.
        private static Material _capsuleMaterial;
        private static bool _capsuleMaterialTried;

        // Exit-zone ambient glow (campaign/tutorial only — endless and the debug map have no
        // fixed floor to signal). One wide strip per row, loaded/cached the same way as the
        // diamond material above.
        private static Material _exitGlowMaterial;
        private static bool _exitGlowMaterialTried;

        // Exit zone state (§5.16, reworked after R6.14). The old full-width additive wash over the
        // tiles bleached every block in the band (pink -> peach, amber -> yellow, bombs pale), so the
        // zone read as a rendering bug rather than a destination. Now: light only in the open cells,
        // a checkered finish line at the win row, and arrows in the side margins.
        private const int   ExitChevronsPerSide     = 3;
        private const float ExitReachedFlareSeconds = 0.8f;

        private AvatarModel _avatar;
        private bool  _hasExit;
        private int   _exitRow;      // first winning row (CampaignManager.WinDepthFromFloor above the floor)
        private int   _exitBandTop;  // first row of the glowing band
        private SpriteRenderer[,] _exitHoleGlow;  // [x, y - _exitBandTop]
        private float[] _exitHoleAlpha;           // base alpha per band row
        private SpriteRenderer _exitLine;
        private Vector3 _exitLineScale;
        private SpriteRenderer[] _exitChevrons;
        private Vector3[] _exitChevronBase;
        private float _exitClock;
        private float _exitReachedTimer = -1f;    // >= 0 while the "reached" flare plays
        private bool  _exitReached;
        private static Sprite _checkerSprite, _chevronSprite, _glowGradientSprite;

        /// <param name="avatar">Optional: drives the exit cues' approach speed-up. Null = steady cues.</param>
        public void Init(GridModel grid, GravitySystem gravity, bool showExitGlow = false, AvatarModel avatar = null)
        {
            _grid = grid;
            _gravity = gravity;
            _avatar = avatar;
            _grid.CellChanged += OnCellChanged;
            EnsureTilesetLoaded();

            _tiles = new SpriteRenderer[grid.Width, grid.Height];
            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    var p = new GridPos(x, y);
                    var go = new GameObject($"Tile ({x},{y})");
                    go.transform.SetParent(transform, false);
                    go.transform.localPosition = ToLocal(p);

                    var renderer = go.AddComponent<SpriteRenderer>();
                    if (_defaultTileMaterial == null)
                        _defaultTileMaterial = renderer.sharedMaterial; // capture Unity's builtin Sprites-Default once
                    (renderer.sprite, renderer.color) = VisualFor(grid.Get(p));
                    ApplyMaterial(renderer, grid.Get(p));
                    _tiles[x, y] = renderer;
                }
            }

            if (showExitGlow)
                BuildExitZone(grid);
        }

        /// <summary>
        /// Exit zone, parented under this BoardView so the next LoadLevel() tears it down:
        /// warm light in the EMPTY cells of the last <c>exitGlowRows</c> rows only (additive, one sprite
        /// per cell, toggled by OnCellChanged — blocks keep their true colours); a checkered gold finish
        /// line on the top edge of the first winning row; and gold arrows in both side margins above
        /// the line, flowing down and quickening as the avatar closes in.
        /// </summary>
        private void BuildExitZone(GridModel grid)
        {
            _hasExit     = true;
            _exitRow     = Mathf.Clamp(grid.Height - CampaignManager.WinDepthFromFloor, 0, grid.Height - 1);
            _exitBandTop = Mathf.Max(0, grid.Height - exitGlowRows);
            int bandRows = grid.Height - _exitBandTop;
            Material glowMaterial = EnsureExitGlowMaterial() ?? _defaultTileMaterial;

            // ── Light pooled in the open cells ───────────────────────────────
            _exitHoleGlow  = new SpriteRenderer[grid.Width, bandRows];
            _exitHoleAlpha = new float[bandRows];
            for (int y = _exitBandTop; y < grid.Height; y++)
            {
                int band = y - _exitBandTop;
                _exitHoleAlpha[band] = Mathf.Lerp(0.35f, 0.9f, (band + 1) / (float)bandRows);
                for (int x = 0; x < grid.Width; x++)
                {
                    var p  = new GridPos(x, y);
                    var go = new GameObject($"ExitGlow ({x},{y})");
                    go.transform.SetParent(transform, false);
                    go.transform.localPosition = ToLocal(p);

                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sprite         = GetGlowGradientSprite();
                    sr.sharedMaterial = glowMaterial;
                    sr.color          = new Color(exitGlowColor.r, exitGlowColor.g, exitGlowColor.b, _exitHoleAlpha[band]);
                    sr.sortingOrder   = 1; // above the opaque empty tile (0), below every overlay
                    sr.enabled        = grid.Get(p) == CellType.Empty;
                    _exitHoleGlow[x, band] = sr;
                }
            }

            // ── Finish line ──────────────────────────────────────────────────
            float lineY = -_exitRow + 0.5f; // top edge of the winning row
            var lineGo = new GameObject("ExitLine");
            lineGo.transform.SetParent(transform, false);
            lineGo.transform.localPosition = new Vector3((grid.Width - 1) / 2f, lineY, 0f);
            Sprite checker = GetCheckerSprite();
            _exitLineScale = new Vector3((grid.Width + 0.5f) / checker.bounds.size.x, 0.3f / checker.bounds.size.y, 1f);
            lineGo.transform.localScale = _exitLineScale;
            _exitLine = lineGo.AddComponent<SpriteRenderer>();
            _exitLine.sprite       = checker;
            _exitLine.color        = exitLineColor;
            _exitLine.sortingOrder = 6; // over tiles and glow, under enemies (8) and the avatar (10)

            // ── Arrows in the side margins ───────────────────────────────────
            _exitChevrons    = new SpriteRenderer[ExitChevronsPerSide * 2];
            _exitChevronBase = new Vector3[ExitChevronsPerSide * 2];
            for (int side = 0; side < 2; side++)
            {
                float x = side == 0 ? -1.1f : grid.Width + 0.1f;
                for (int i = 0; i < ExitChevronsPerSide; i++)
                {
                    int k = side * ExitChevronsPerSide + i;
                    // i = 0 is the arrow nearest the line; the stack climbs away from it.
                    var pos = new Vector3(x, lineY + 0.55f + i * 0.85f, 0f);
                    var go  = new GameObject($"ExitArrow {k}");
                    go.transform.SetParent(transform, false);
                    go.transform.localPosition = pos;
                    go.transform.localScale    = Vector3.one * 0.8f;
                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sprite       = GetChevronSprite();
                    sr.color        = exitLineColor;
                    sr.sortingOrder = 6;
                    _exitChevrons[k]    = sr;
                    _exitChevronBase[k] = pos;
                }
            }
        }

        /// <summary>
        /// The avatar crossed the finish line (GameBootstrap.BeginLevelEnd). The line flashes white and
        /// swells, the open cells flare, gold sparks spray up off the line — then the cues stay lit and
        /// steady while the level settles.
        /// </summary>
        public void PlayExitReached()
        {
            if (!_hasExit || _exitReached) return;
            _exitReached      = true;
            _exitReachedTimer = 0f;

            float lineY = -_exitRow + 0.5f;
            for (int i = 0; i < 28; i++)
            {
                var go = new GameObject("ExitSpark");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(Random.Range(-0.4f, _grid.Width - 0.6f), lineY, 0f);
                go.transform.localScale    = Vector3.one * Random.Range(0.12f, 0.22f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite       = GetUnitSprite();
                sr.color        = Color.Lerp(exitLineColor, Color.white, Random.value * 0.6f);
                sr.sortingOrder = 7;
                var velocity = new Vector3(Random.Range(-1.2f, 1.2f), Random.Range(3f, 6.5f), 0f);
                StartCoroutine(Spark(go.transform, sr, velocity, Random.Range(0.6f, 1.0f)));
            }
        }

        private static IEnumerator Spark(Transform t, SpriteRenderer sr, Vector3 velocity, float duration)
        {
            float elapsed = 0f;
            Color c = sr.color;
            while (elapsed < duration && t != null)
            {
                float dt = Time.deltaTime;
                elapsed         += dt;
                velocity.y      -= 9f * dt; // a little gravity so the spray arcs back down
                t.localPosition += velocity * dt;
                c.a      = 1f - elapsed / duration;
                sr.color = c;
                yield return null;
            }
            if (t != null) Destroy(t.gameObject);
        }

        /// <summary>Per-frame exit cues: arrows flowing down, finish-line pulse, the reached flare.</summary>
        private void AnimateExitZone(float dt)
        {
            // 0 when far above the line, 1 at it. No avatar -> a calm middle value.
            float near = _avatar == null
                ? 0.4f
                : Mathf.Clamp01(1f - (_exitRow - _avatar.Position.Y) / exitApproachRows);
            if (_exitReached) near = 1f;

            _exitClock += dt * Mathf.Lerp(1.2f, 4f, near);

            float flare = 0f;
            if (_exitReachedTimer >= 0f)
            {
                _exitReachedTimer += dt;
                flare = Mathf.Clamp01(1f - _exitReachedTimer / ExitReachedFlareSeconds);
                if (flare <= 0f) _exitReachedTimer = -1f;
            }

            // Arrows: a light runs down each stack toward the line (top arrow first), so the margin
            // reads as "this way" even in a still frame. Brighter and faster as the avatar closes in.
            for (int k = 0; k < _exitChevrons.Length; k++)
            {
                int   i     = k % ExitChevronsPerSide;
                float wave  = Mathf.Sin(_exitClock * Mathf.PI * 2f + i * 1.6f) * 0.5f + 0.5f;
                float alpha = _exitReached ? 1f : Mathf.Lerp(0.25f, 1f, wave) * Mathf.Lerp(0.55f, 1f, near);
                _exitChevrons[k].color = new Color(exitLineColor.r, exitLineColor.g, exitLineColor.b, alpha);
                _exitChevrons[k].transform.localPosition =
                    _exitChevronBase[k] + new Vector3(0f, -0.12f * Mathf.Sin(_exitClock * Mathf.PI * 2f), 0f);
            }

            // Finish line: soft pulse; on reach, flash to white and swell, then stay bright.
            float pulse = 0.8f + 0.2f * Mathf.Sin(_exitClock * Mathf.PI * 2f);
            Color line  = Color.Lerp(exitLineColor, Color.white, flare);
            line.a      = _exitReached ? 1f : pulse;
            _exitLine.color = line;
            _exitLine.transform.localScale = new Vector3(_exitLineScale.x, _exitLineScale.y * (1f + 1.5f * flare), 1f);

            // The open cells flare with the line.
            if (_exitReached)
            {
                float boost = 1f + 0.8f * flare;
                for (int band = 0; band < _exitHoleAlpha.Length; band++)
                    for (int x = 0; x < _exitHoleGlow.GetLength(0); x++)
                    {
                        SpriteRenderer sr = _exitHoleGlow[x, band];
                        Color c = sr.color;
                        c.a      = Mathf.Min(1f, _exitHoleAlpha[band] * boost);
                        sr.color = c;
                    }
            }
        }

        /// <summary>2-row checker strip, light / dark, point-filtered: reads as a finish line, not a bar.</summary>
        private static Sprite GetCheckerSprite()
        {
            if (_checkerSprite != null) return _checkerSprite;

            const int cols = 16, rows = 2;
            var tex = new Texture2D(cols, rows, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode   = TextureWrapMode.Clamp
            };
            var light = new Color32(255, 255, 255, 255); // tinted gold by SpriteRenderer.color
            var dark  = new Color32(70, 40, 10, 255);
            var px = new Color32[cols * rows];
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                    px[y * cols + x] = ((x + y) & 1) == 0 ? light : dark;
            tex.SetPixels32(px);
            tex.Apply();

            _checkerSprite = Sprite.Create(tex, new Rect(0, 0, cols, rows), new Vector2(0.5f, 0.5f), cols);
            return _checkerSprite;
        }

        /// <summary>
        /// Per-cell exit light: white, alpha ramping from 1 at the bottom edge to 0.25 at the top, so
        /// stacked rows read as light rising out of the exit rather than a flat tan fill.
        /// </summary>
        private static Sprite GetGlowGradientSprite()
        {
            if (_glowGradientSprite != null) return _glowGradientSprite;

            const int h = 16;
            var tex = new Texture2D(h, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode   = TextureWrapMode.Clamp
            };
            var px = new Color32[h * h];
            for (int y = 0; y < h; y++)
            {
                var c = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * Mathf.Lerp(1f, 0.25f, y / (h - 1f))));
                for (int x = 0; x < h; x++) px[y * h + x] = c;
            }
            tex.SetPixels32(px);
            tex.Apply();

            // 1 x 1 unit, like the tile it lights.
            _glowGradientSprite = Sprite.Create(tex, new Rect(0, 0, h, h), new Vector2(0.5f, 0.5f), h);
            return _glowGradientSprite;
        }

        /// <summary>16x16 downward chevron with a dark rim so it holds on any background.</summary>
        private static Sprite GetChevronSprite()
        {
            if (_chevronSprite != null) return _chevronSprite;

            const int n = 16;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode   = TextureWrapMode.Clamp
            };
            var px = new Color32[n * n];

            // A "V": arm centre line at y = tip + |x - centre| (texture y grows upward, tip at the bottom).
            bool OnArm(int x, int y, float thickness)
            {
                float cx = (n - 1) / 2f;
                float vy = 3f + Mathf.Abs(x - cx);
                return y >= vy - thickness * 0.5f && y <= vy + thickness * 0.5f && y < n - 2;
            }
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var c = new Color32(0, 0, 0, 0);
                    if (OnArm(x, y, 5.2f)) c = new Color32(40, 24, 6, 230);    // rim
                    if (OnArm(x, y, 3f))   c = new Color32(255, 255, 255, 255); // body, tinted gold
                    px[y * n + x] = c;
                }
            tex.SetPixels32(px);
            tex.Apply();

            _chevronSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
            return _chevronSprite;
        }

        private static Material EnsureExitGlowMaterial()
        {
            if (_exitGlowMaterialTried)
                return _exitGlowMaterial;
            _exitGlowMaterialTried = true;

            Shader shader = Resources.Load<Shader>("Shaders/ExitGlow");
            if (shader != null)
                _exitGlowMaterial = new Material(shader) { name = "ExitGlow (runtime)" };
            return _exitGlowMaterial;
        }

        private void OnDestroy()
        {
            if (_grid != null)
                _grid.CellChanged -= OnCellChanged;
        }

        private void OnCellChanged(GridPos p, CellType type)
        {
            SpriteRenderer renderer = _tiles[p.X, p.Y];
            (renderer.sprite, renderer.color) = VisualFor(type);
            ApplyMaterial(renderer, type); // a drilled diamond must fall back to the plain material

            // Exit zone: light lives only in the open cells, so it follows drills and falling blocks.
            if (_hasExit && p.Y >= _exitBandTop)
                _exitHoleGlow[p.X, p.Y - _exitBandTop].enabled = type == CellType.Empty;
        }

        private void LateUpdate()
        {
            if (_grid == null || _gravity == null)
                return;

            if (_hasExit)
                AnimateExitZone(Time.deltaTime);

            // Wobble telegraph: shake only the cells whose chunk is in its warning window.
            float offset = Mathf.Sin(Time.time * wobbleFrequency * Mathf.PI * 2f) * wobbleAmplitude;
            for (int y = 0; y < _grid.Height; y++)
            {
                for (int x = 0; x < _grid.Width; x++)
                {
                    var p = new GridPos(x, y);
                    Vector3 basePosition = ToLocal(p);
                    _tiles[x, y].transform.localPosition = _gravity.IsCellWobbling(p)
                        ? basePosition + new Vector3(offset, 0f, 0f)
                        : basePosition;
                }
            }
        }

        public static Vector3 ToLocal(GridPos p) => new Vector3(p.X, -p.Y, 0f);

        /// <summary>
        /// (sprite, tint) pair for a cell. ColorA/B/C/Empty/HardCracked reuse the neutral tile,
        /// tinted per palette (same trick as the old procedural white square). Hard/Steel/AirCapsule/Bomb
        /// have their own AI-generated art with baked-in color, so they render at full white (no tint) —
        /// tinting them would muddy colors already in the texture.
        /// </summary>
        private (Sprite sprite, Color color) VisualFor(CellType type)
        {
            switch (type)
            {
                case CellType.ColorA:      return (_neutralSprite, colorA);
                case CellType.ColorB:      return (_neutralSprite, colorB);
                case CellType.ColorC:      return (_neutralSprite, colorC);
                case CellType.Hard:        return (_hardSprite, Color.white);
                case CellType.HardCracked: return (_hardSprite, crackedColor);
                case CellType.Steel:       return (_steelSprite, Color.white);
                case CellType.AirCapsule:  return (_capsuleSprite, Color.white);
                case CellType.Bomb:        return (_bombSprite, Color.white);
                case CellType.Diamond:     return (_neutralSprite, diamondColor);
                default:                   return (_neutralSprite, emptyColor);
            }
        }

        /// <summary>
        /// R4: Diamond cells get the DiamondShine material (a persistent shimmer, §5.10); R6.5: air
        /// capsules get a gentler cyan instance of it. Every other cell — including a diamond that was just drilled to Empty — uses the plain default
        /// sprite material every other tile already renders with.
        /// </summary>
        private static void ApplyMaterial(SpriteRenderer renderer, CellType type)
        {
            switch (type)
            {
                case CellType.Diamond:    renderer.sharedMaterial = EnsureDiamondMaterial() ?? _defaultTileMaterial; break;
                case CellType.AirCapsule: renderer.sharedMaterial = EnsureCapsuleMaterial() ?? _defaultTileMaterial; break;
                default:                  renderer.sharedMaterial = _defaultTileMaterial; break;
            }
        }

        private static Material EnsureCapsuleMaterial()
        {
            if (_capsuleMaterialTried)
                return _capsuleMaterial;
            _capsuleMaterialTried = true;

            Shader shader = Resources.Load<Shader>("Shaders/DiamondShine");
            if (shader == null)
                return null;

            _capsuleMaterial = new Material(shader) { name = "CapsuleShine (runtime)" };
            _capsuleMaterial.SetColor("_ShineColor",    new Color(0.55f, 0.95f, 1f));
            _capsuleMaterial.SetFloat("_PulseSpeed",    2.2f);  // a breath, faster than the diamond's 1.6
            _capsuleMaterial.SetFloat("_PulseStrength", 0.18f);
            _capsuleMaterial.SetFloat("_SweepSpeed",    0.35f); // one slow gloss pass every ~3 s
            _capsuleMaterial.SetFloat("_SweepWidth",    0.10f);
            _capsuleMaterial.SetFloat("_SweepStrength", 0.45f);
            return _capsuleMaterial;
        }

        private static Material EnsureDiamondMaterial()
        {
            if (_diamondMaterialTried)
                return _diamondMaterial;
            _diamondMaterialTried = true;

            Shader shader = Resources.Load<Shader>("Shaders/DiamondShine");
            if (shader != null)
                _diamondMaterial = new Material(shader) { name = "DiamondShine (runtime)" };
            return _diamondMaterial;
        }

        private static void EnsureTilesetLoaded()
        {
            if (_tilesetLoaded)
                return;
            _tilesetLoaded = true;

            // Resources.Load returns null (not an exception) on a missing asset — falls back to the
            // procedural white square per-sprite, so a missing art file never breaks the board.
            _neutralSprite = Resources.Load<Sprite>("Tiles/tile_neutral") ?? GetUnitSprite();
            _hardSprite    = Resources.Load<Sprite>("Tiles/tile_hard")    ?? GetUnitSprite();
            _steelSprite   = Resources.Load<Sprite>("Tiles/tile_steel")   ?? GetUnitSprite();
            _capsuleSprite = Resources.Load<Sprite>("Tiles/tile_capsule") ?? GetUnitSprite();
            _bombSprite    = Resources.Load<Sprite>("Tiles/tile_bomb")    ?? GetUnitSprite();
        }

        public static Sprite GetUnitSprite()
        {
            if (_unitSprite != null)
                return _unitSprite;

            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(255, 255, 255, 255);
            texture.SetPixels32(pixels);
            texture.Apply();

            _unitSprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            return _unitSprite;
        }
    }
}
