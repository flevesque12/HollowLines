using UnityEngine;

namespace HollowLines.View
{
    /// <summary>
    /// R6.4 (F04): procedural pixel-art sprites for the enemies and their "!" alert.
    ///
    /// Before this, enemies were plain unit squares tinted green/orange — the same shape as every
    /// block, and colours from the same family as the teal/amber blocks and the red bombs, so
    /// testers simply didn't see them. Now each type has its own silhouette, a black outline that
    /// separates it from whatever block it sits on, and eyes: open when active, shut when dormant.
    ///
    /// Colours are baked into the texture (the eyes must stay white), so EnemyView tints with
    /// grey/alpha only. 16×16 at 16 px per unit = one cell; point-filtered to stay crisp.
    /// </summary>
    public static class EnemySprites
    {
        private const int Size = 16;

        private static readonly Color32 Clear   = new Color32(0, 0, 0, 0);
        private static readonly Color32 Outline = new Color32(12, 10, 8, 255);
        private static readonly Color32 White   = new Color32(255, 255, 255, 255);
        private static readonly Color32 Spark   = new Color32(255, 220, 60, 255);

        /// <summary>Crawler: a wide bug — oval body, three legs a side, antennae.</summary>
        public static Sprite Crawler(Color body, bool eyesOpen)
        {
            var px = NewCanvas();

            // Body: ellipse centred slightly high so the legs fit underneath.
            FillEllipse(px, 7.5f, 7.5f, 6.2f, 4.4f, body);
            // Top highlight band — reads as a shell, not a flat blob.
            Color32 hi = Lighten(body, 0.35f);
            for (int x = 4; x <= 11; x++) Set(px, x, 11, hi, onlyOver: body);

            OutlineMask(px);

            // Legs and antennae drawn after the outline so they stay 1 px thin.
            foreach (int x in new[] { 3, 7, 11 })
            {
                Set(px, x, 2, Outline); Set(px, x + 1, 1, Outline);
            }
            Set(px, 5, 13, Outline); Set(px, 4, 14, Outline);
            Set(px, 10, 13, Outline); Set(px, 11, 14, Outline);

            DrawEyes(px, 5, 9, 8, eyesOpen);
            return Build(px, Size, Size);
        }

        /// <summary>Boomer: a round bomb-creature with a lit fuse on top — "a bomb with legs" (§5.10).</summary>
        public static Sprite Boomer(Color body, bool eyesOpen)
        {
            var px = NewCanvas();

            FillEllipse(px, 7.5f, 6.5f, 6.0f, 6.0f, body);
            Color32 hi = Lighten(body, 0.4f);
            Set(px, 4, 9, hi, onlyOver: body); Set(px, 5, 10, hi, onlyOver: body); Set(px, 4, 10, hi, onlyOver: body);

            OutlineMask(px);

            // Fuse + spark.
            Set(px, 8, 13, Outline); Set(px, 9, 14, Outline);
            Set(px, 10, 15, Spark); Set(px, 11, 14, Spark); Set(px, 10, 14, Spark);

            DrawEyes(px, 5, 9, 7, eyesOpen);
            return Build(px, Size, Size);
        }

        /// <summary>A yellow "!" (3 px wide) with a black outline, 8×16 — floats above an enemy that matters.</summary>
        public static Sprite Alert()
        {
            const int w = 8, h = 16;
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = Clear;

            // 3 px wide: at 2 px it read as a hairline once scaled to a cell (screenshot check).
            for (int y = 6; y <= 14; y++) for (int x = 2; x <= 4; x++) px[y * w + x] = Spark;
            for (int y = 1; y <= 3; y++)  for (int x = 2; x <= 4; x++) px[y * w + x] = Spark;

            OutlineMask(px, w, h);
            return Build(px, w, h);
        }

        // ── Drawing helpers ───────────────────────────────────────────────

        private static Color32[] NewCanvas()
        {
            var px = new Color32[Size * Size];
            for (int i = 0; i < px.Length; i++) px[i] = Clear;
            return px;
        }

        private static void FillEllipse(Color32[] px, float cx, float cy, float rx, float ry, Color c)
        {
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float dx = (x - cx) / rx, dy = (y - cy) / ry;
                if (dx * dx + dy * dy <= 1f) px[y * Size + x] = c;
            }
        }

        /// <summary>Every transparent pixel touching an opaque one (8-neighbour) becomes outline.</summary>
        private static void OutlineMask(Color32[] px, int w = Size, int h = Size)
        {
            var src = (Color32[])px.Clone();
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (src[y * w + x].a != 0) continue;
                for (int oy = -1; oy <= 1; oy++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    int nx = x + ox, ny = y + oy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    if (src[ny * w + nx].a != 0 && !Same(src[ny * w + nx], Outline))
                    {
                        px[y * w + x] = Outline;
                        goto next;
                    }
                }
                next:;
            }
        }

        /// <summary>Two 2×2 eyes at (lx, y) and (rx, y). Open = white with a pupil; shut = a dark line.</summary>
        private static void DrawEyes(Color32[] px, int lx, int rx, int y, bool open)
        {
            foreach (int x in new[] { lx, rx })
            {
                if (open)
                {
                    Set(px, x, y, White); Set(px, x + 1, y, White);
                    Set(px, x, y + 1, White); Set(px, x + 1, y + 1, White);
                    Set(px, x + 1, y, Outline); // pupil, looking down-right — toward the well
                }
                else
                {
                    Set(px, x, y, Outline); Set(px, x + 1, y, Outline);
                }
            }
        }

        private static void Set(Color32[] px, int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= Size || y >= Size) return;
            px[y * Size + x] = c;
        }

        private static void Set(Color32[] px, int x, int y, Color32 c, Color onlyOver)
        {
            if (x < 0 || y < 0 || x >= Size || y >= Size) return;
            if (Same(px[y * Size + x], onlyOver)) px[y * Size + x] = c;
        }

        private static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

        private static Color32 Lighten(Color c, float t) => Color.Lerp(c, Color.white, t);

        private static Sprite Build(Color32[] px, int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode   = TextureWrapMode.Clamp,
            };
            tex.SetPixels32(px);
            tex.Apply();
            // Pivot at the centre, 16 px per unit: a 16-px-tall sprite is exactly one cell tall.
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), Size);
        }
    }
}
