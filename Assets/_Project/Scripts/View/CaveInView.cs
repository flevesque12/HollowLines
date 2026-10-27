using System.Collections.Generic;
using HollowLines.Core;
using UnityEngine;

namespace HollowLines.View
{
    /// <summary>
    /// R8.8 Cave-in visuals (cave-in.md). Replaces the R8.4b placeholder (CaveInPreviewView: an orange line
    /// and an OnGUI readout). The rubble itself is a BoardView tile (procedural cobbles); this draws what
    /// moves:
    ///   • the front — a row of jagged rock teeth hanging at CeilingRow, with a hot crack-glow along their
    ///     edge that pulses red in the Danger Zone;
    ///   • the crumbling band — dark dust over the part of a row the front is eating through;
    ///   • falling debris — pebbles shaken loose from the front, more of them the closer it gets, a burst
    ///     whenever a row is swallowed, and a big one when the front strikes the avatar.
    ///
    /// The front lives in ABSOLUTE rows; `rowOffset` maps them onto this board (Endless segments). Parented
    /// under the BoardView, so it is torn down with the board — it unsubscribes from the persistent
    /// CeilingSystem in OnDestroy. No Core logic here.
    /// </summary>
    public sealed class CaveInView : MonoBehaviour
    {
        private const int   TeethVariants   = 3;
        private const int   PebblePool      = 48;
        private const float PebbleLife      = 0.7f;
        private const float PebbleGravity   = 14f;  // rows/s²
        private const float BaseDebrisRate  = 3f;   // pebbles/s at a full leash while the front moves
        private const float CloseDebrisRate = 28f;  // extra pebbles/s as the margin reaches 0

        private static readonly Color RockColor   = new Color(0.36f, 0.31f, 0.27f);
        private static readonly Color GlowColor   = new Color(1.00f, 0.55f, 0.18f, 0.85f);
        private static readonly Color DangerColor = new Color(1.00f, 0.18f, 0.10f, 1.00f);
        private static readonly Color DustColor   = new Color(0.09f, 0.07f, 0.06f, 0.80f);
        private static readonly Color PebbleColor = new Color(0.55f, 0.48f, 0.42f);

        private CeilingSystem _ceiling;
        private GridModel     _grid;
        private AvatarModel   _avatar;
        private int           _rowOffset;

        private SpriteRenderer[] _teeth;
        private SpriteRenderer   _glow;
        private SpriteRenderer   _dust;
        private float            _debrisAccumulator;

        private sealed class Pebble
        {
            public SpriteRenderer Sr;
            public Vector2 Pos, Vel;
            public float   Age;
            public bool    Live;
        }
        private readonly List<Pebble> _pebbles = new List<Pebble>();

        private static Sprite   _unit;
        private static Sprite[] _teethSprites;
        private static Sprite   _glowSprite;

        public void Init(CeilingSystem ceiling, GridModel grid, int rowOffset, AvatarModel avatar)
        {
            _ceiling   = ceiling;
            _grid      = grid;
            _rowOffset = rowOffset;
            _avatar    = avatar;
            EnsureSprites();

            _dust = NewRenderer("CaveInDust", _unit, DustColor, 7);

            // One tooth per column, alternating variants, so the edge is ragged but never repeats in a row.
            _teeth = new SpriteRenderer[_grid.Width];
            for (int x = 0; x < _grid.Width; x++)
                _teeth[x] = NewRenderer("CaveInTooth", _teethSprites[(x * 7 + 3) % TeethVariants], RockColor, 9);
            _glow = NewRenderer("CaveInGlow", _glowSprite, GlowColor, 8); // behind the teeth: they read as silhouettes

            for (int i = 0; i < PebblePool; i++)
            {
                var sr = NewRenderer("CaveInPebble", _unit, PebbleColor, 9);
                sr.enabled = false;
                _pebbles.Add(new Pebble { Sr = sr });
            }

            _ceiling.RowsSwallowed += OnRowsSwallowed;
            _ceiling.AvatarReached += OnAvatarReached;
        }

        private void OnDestroy()
        {
            if (_ceiling == null) return;
            _ceiling.RowsSwallowed -= OnRowsSwallowed;
            _ceiling.AvatarReached -= OnAvatarReached;
        }

        private void LateUpdate()
        {
            if (_ceiling == null)
                return;

            float frontRow = _ceiling.CeilingRow - _rowOffset;
            float frontY   = -frontRow + 0.5f;                   // world y = -row; a row's top edge is +0.5
            float danger   = _ceiling.IsDangerZone ? 0.5f + 0.5f * Mathf.Sin(Time.time * 12f) : 0f;

            // Teeth hang from the front line, one per column, with a slight per-column jiggle while it moves.
            bool moving = !_ceiling.InStartGrace && _ceiling.Speed > 0f;
            for (int x = 0; x < _teeth.Length; x++)
            {
                float jiggle = moving ? 0.03f * Mathf.Sin(Time.time * 17f + x * 1.9f) : 0f;
                _teeth[x].transform.localPosition = new Vector3(x, frontY + jiggle, 0f);
            }

            float centerX = (_grid.Width - 1) / 2f;
            // The glow's bright top sits just under the front line, so the teeth stand out against it.
            _glow.transform.localPosition = new Vector3(centerX, frontY - 0.7f, 0f);
            _glow.transform.localScale    = new Vector3(_grid.Width * _glowSprite.pixelsPerUnit, 1f, 1f); // 1 px wide sprite
            Color glow = Color.Lerp(GlowColor, DangerColor, danger);
            glow.a *= 0.65f + 0.35f * Mathf.Sin(Time.time * (_ceiling.IsDangerZone ? 12f : 3f));
            _glow.color = glow;

            // Dust over the row being eaten: from the rubble's bottom edge down to the front.
            float swallowed = _ceiling.SwallowedRows - _rowOffset;
            float crumble   = frontRow - swallowed;
            _dust.enabled = crumble > 0.01f;
            if (_dust.enabled)
            {
                float top = -swallowed + 0.5f;
                _dust.transform.localPosition = new Vector3(centerX, top - crumble / 2f, 0f);
                _dust.transform.localScale    = new Vector3(_grid.Width, crumble, 1f);
            }

            // Steady trickle of pebbles off the front — a drizzle at a full leash, a hail when it is close.
            if (moving && Time.timeScale > 0f)
            {
                float proximity = 1f - Mathf.Clamp01(_ceiling.LeadPercent / 100f);
                _debrisAccumulator += Time.deltaTime * (BaseDebrisRate + CloseDebrisRate * proximity * proximity);
                while (_debrisAccumulator >= 1f)
                {
                    _debrisAccumulator -= 1f;
                    Spawn(new Vector2(Random.Range(-0.45f, _grid.Width - 0.55f), frontY - 0.35f), 0f);
                }
            }

            TickPebbles(Time.deltaTime);
        }

        private void OnRowsSwallowed(int fromRow, int toRow)
        {
            // A puff of pebbles along each row as it turns to rubble (bounded — the leash can swallow many).
            for (int row = Mathf.Max(fromRow, toRow - 3); row < toRow; row++)
            {
                float y = -(row - _rowOffset);
                for (int i = 0; i < 4; i++)
                    Spawn(new Vector2(Random.Range(-0.45f, _grid.Width - 0.55f), y - 0.3f), 1.5f);
            }
        }

        private void OnAvatarReached()
        {
            // The strike: a burst of rock around the avatar's column (the shake and crush SFX are GameBootstrap's).
            Vector3 a = BoardView.ToLocal(_avatar.Position);
            for (int i = 0; i < 16; i++)
                Spawn(new Vector2(a.x + Random.Range(-1.2f, 1.2f), a.y + Random.Range(0f, 0.6f)), 4f);
        }

        private void Spawn(Vector2 pos, float kick)
        {
            foreach (Pebble p in _pebbles)
            {
                if (p.Live) continue;
                p.Live = true;
                p.Age  = 0f;
                p.Pos  = pos;
                p.Vel  = new Vector2(Random.Range(-0.4f, 0.4f) * (1f + kick), Random.Range(-0.5f, 0.5f) + kick * 0.6f);
                float s = Random.Range(0.10f, 0.20f);
                p.Sr.transform.localScale = new Vector3(s, s, 1f);
                p.Sr.color   = PebbleColor * Random.Range(0.75f, 1.1f);
                p.Sr.enabled = true;
                return;
            }
        }

        private void TickPebbles(float dt)
        {
            foreach (Pebble p in _pebbles)
            {
                if (!p.Live) continue;
                p.Age += dt;
                if (p.Age >= PebbleLife)
                {
                    p.Live = false;
                    p.Sr.enabled = false;
                    continue;
                }
                p.Vel.y -= PebbleGravity * dt;
                p.Pos   += p.Vel * dt;
                p.Sr.transform.localPosition = new Vector3(p.Pos.x, p.Pos.y, 0f);
                Color c = p.Sr.color;
                c.a = 1f - p.Age / PebbleLife;
                p.Sr.color = c;
            }
        }

        private SpriteRenderer NewRenderer(string name, Sprite sprite, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite       = sprite;
            sr.color        = color;
            sr.sortingOrder = order;
            return sr;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Procedural sprites (pixel art, point-filtered, built once)
        // ─────────────────────────────────────────────────────────────────────

        private static void EnsureSprites()
        {
            if (_unit != null)
                return;

            var white = new Texture2D(1, 1) { filterMode = FilterMode.Point };
            white.SetPixel(0, 0, Color.white);
            white.Apply();
            _unit = Sprite.Create(white, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);

            _teethSprites = new Sprite[TeethVariants];
            for (int v = 0; v < TeethVariants; v++)
                _teethSprites[v] = BuildTooth(v);

            _glowSprite = BuildGlow();
        }

        /// <summary>
        /// 16×12 px (one cell wide, ¾ tall), pivot at the top: rock hanging down from the front line, its
        /// lower edge a random jagged profile, a darker underside and a few lit pixels on top. Grey-scale
        /// (tinted by RockColor); each variant has its own profile.
        /// </summary>
        private static Sprite BuildTooth(int variant)
        {
            const int w = 16, h = 12;
            var rng = new System.Random(31 + variant * 17);

            // Jagged depth per column: 4..11 px hanging down, smoothed so it reads as rock, not noise.
            var depth = new float[w];
            for (int x = 0; x < w; x++) depth[x] = 4 + rng.Next(8);
            for (int pass = 0; pass < 2; pass++)
                for (int x = 1; x < w - 1; x++)
                    depth[x] = (depth[x - 1] + 2f * depth[x] + depth[x + 1]) / 4f;

            var px = new Color[w * h];
            for (int y = 0; y < h; y++)           // y = 0 is the BOTTOM row of the texture
            for (int x = 0; x < w; x++)
            {
                int fromTop = h - 1 - y;
                float d = depth[x];
                if (fromTop >= d) { px[y * w + x] = Color.clear; continue; }

                float v = 0.85f - 0.35f * (fromTop / d);          // darker toward the hanging tip
                if (fromTop >= d - 1.2f) v *= 0.55f;               // shadowed underside
                if (fromTop == 0 && x % 3 != 1) v = 1f;            // lit top edge
                if (rng.NextDouble() < 0.10) v *= 0.8f;            // grain
                px[y * w + x] = new Color(v, v, v, 1f);
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 1f), w);
        }

        /// <summary>A soft horizontal glow strip (1 unit tall), brightest at the top where the rock cracks.</summary>
        private static Sprite BuildGlow()
        {
            const int h = 16;
            var tex = new Texture2D(1, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < h; y++)
            {
                float t = (float)y / (h - 1);                    // 0 bottom → 1 top
                tex.SetPixel(0, y, new Color(1f, 1f, 1f, Mathf.Pow(t, 2.2f) * 0.9f));
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, h), new Vector2(0.5f, 0.5f), h);
        }
    }
}
