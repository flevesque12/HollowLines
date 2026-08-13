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

        [Header("— Exit Glow —")]
        [Tooltip("Warm ambient glow color for the exit zone (campaign/tutorial only) — the last rows before the win threshold.")]
        [SerializeField] private Color exitGlowColor = new Color(1f, 0.82f, 0.45f, 1f);

        [Tooltip("How many rows above the board's bottom breathe with the exit glow.")]
        [Range(1, 10)]
        [SerializeField] private int exitGlowRows = 4;

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

        // Exit-zone ambient glow (campaign/tutorial only — endless and the debug map have no
        // fixed floor to signal). One wide strip per row, loaded/cached the same way as the
        // diamond material above.
        private static Material _exitGlowMaterial;
        private static bool _exitGlowMaterialTried;

        public void Init(GridModel grid, GravitySystem gravity, bool showExitGlow = false)
        {
            _grid = grid;
            _gravity = gravity;
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
                BuildExitGlow(grid);
        }

        /// <summary>
        /// One full-width glow strip per row in the exit zone, parented under this BoardView so it
        /// is torn down automatically on the next LoadLevel(). Rendered ON TOP of the tile layer
        /// (sortingOrder above the tiles' default 0) with an additive blend, so it washes the whole
        /// exit zone in warm light without hiding the block colors underneath — sitting it behind
        /// the tiles instead doesn't work here, since even "empty" cells render with an opaque
        /// emptyColor (alpha 1) that would fully occlude anything drawn earlier. Brightness ramps
        /// toward the bottom row so the glow reads as "getting closer", not a flat band appearing
        /// all at once.
        /// </summary>
        private void BuildExitGlow(GridModel grid)
        {
            int startRow = Mathf.Max(0, grid.Height - exitGlowRows);
            int bandRows = grid.Height - startRow;
            Material material = EnsureExitGlowMaterial() ?? _defaultTileMaterial;

            for (int y = startRow; y < grid.Height; y++)
            {
                float t = (y - startRow + 1) / (float)bandRows;

                var go = new GameObject($"ExitGlow row {y}");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3((grid.Width - 1) / 2f, -y, 0f);
                go.transform.localScale    = new Vector3(grid.Width, 1f, 1f);

                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite         = GetUnitSprite();
                renderer.sharedMaterial = material;
                renderer.color          = new Color(exitGlowColor.r, exitGlowColor.g, exitGlowColor.b, Mathf.Lerp(0.12f, 0.85f, t));
                renderer.sortingOrder   = 1; // above the tile layer's default 0 — additive, so it washes on top instead of hiding behind it
            }
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
        }

        private void LateUpdate()
        {
            if (_grid == null || _gravity == null)
                return;

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
        /// R4: Diamond cells get the DiamondShine material (a persistent shimmer, §5.10); every
        /// other cell — including a diamond that was just drilled to Empty — uses the plain default
        /// sprite material every other tile already renders with.
        /// </summary>
        private static void ApplyMaterial(SpriteRenderer renderer, CellType type)
        {
            renderer.sharedMaterial = type == CellType.Diamond
                ? (EnsureDiamondMaterial() ?? _defaultTileMaterial)
                : _defaultTileMaterial;
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
