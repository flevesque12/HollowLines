using System.IO;
using UnityEditor;
using UnityEngine;

namespace HollowLines.EditorTools
{
    /// <summary>
    /// Regenerates Resources/Tiles/tile_capsule.png procedurally.
    ///
    /// R6.5 (F05) drew it as a Mr. Driller-style "AIR" pill. R8.8 (v3.3 Cave-in, dev's pick): there is no
    /// air any more — the block pushes the collapse back — so it is now a **mine timber prop** (étai de
    /// mine): a door-frame "set" of two posts and a cap beam, cross-braced, on a dark rock face. A prop
    /// holds up the ceiling, so drilling or blasting one into play "pushes the cave-in back" — the help page
    /// (R8.9) says it in words. Warm wood on near-black: no block, bomb or diamond uses that pair.
    ///
    /// Shapes are signed distances (~1.5 px anti-aliased edges), wood grain is a cheap per-pixel noise along
    /// each beam. Same 512×512 size and path, so the import settings (512 PPU, bilinear) and BoardView's
    /// Resources.Load are untouched. BoardView adds a warm shimmer on top (DiamondShine material).
    /// </summary>
    public static class CapsuleTileGenerator
    {
        private const int    Size = 512;
        private const string AssetPath = "Assets/_Project/Art/Resources/Tiles/tile_capsule.png";

        [MenuItem("Hollow Lines/Regenerate Capsule Tile (timber prop)")]
        public static void Regenerate()
        {
            var tex = Generate();
            File.WriteAllBytes(AssetPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
            Debug.Log($"[CapsuleTileGenerator] Wrote {AssetPath}");
        }

        // Beams as capsules (x0, y0, x1, y1, half-width), top-left-origin pixels.
        private static readonly float[][] Posts =
        {
            new[] { 128f, 150f, 128f, 470f, 38f },   // left post
            new[] { 384f, 150f, 384f, 470f, 38f },   // right post
        };
        private static readonly float[] Cap    = { 60f, 118f, 452f, 118f, 44f };    // cap beam across the top
        private static readonly float[][] Braces =
        {
            new[] { 150f, 196f, 362f, 430f, 20f },   // the X between the posts
            new[] { 362f, 196f, 150f, 430f, 20f },
        };

        public static Texture2D Generate()
        {
            var rockTop  = new Color(0.16f, 0.13f, 0.11f);
            var rockBot  = new Color(0.07f, 0.06f, 0.05f);
            var frame    = new Color(0.95f, 0.68f, 0.32f);   // warm rim, same family as the cave-in glow
            var woodLit  = new Color(0.86f, 0.60f, 0.32f);
            var woodDark = new Color(0.52f, 0.32f, 0.16f);
            var outline  = new Color(0.05f, 0.03f, 0.02f);
            var iron     = new Color(0.62f, 0.62f, 0.66f);

            var px = new Color[Size * Size];
            for (int ty = 0; ty < Size; ty++)
            for (int x = 0; x < Size; x++)
            {
                float fx = x + 0.5f, fy = ty + 0.5f;

                // 1. Rock face: vertical gradient, a little speckle, a soft warm inner frame.
                Color c = Color.Lerp(rockTop, rockBot, fy / Size);
                c *= 0.92f + 0.08f * Hash(x / 6, ty / 6);
                float edge = Mathf.Min(Mathf.Min(fx, fy), Mathf.Min(Size - fx, Size - fy));
                c = Over(c, frame, 0.40f * (1f - Mathf.SmoothStep(4f, 22f, edge)));

                // 2. Braces behind, then posts, then the cap on top — each with a dark outline and grain.
                foreach (float[] b in Braces) c = DrawBeam(c, fx, fy, b, woodLit * 0.85f, woodDark, outline);
                foreach (float[] b in Posts)  c = DrawBeam(c, fx, fy, b, woodLit, woodDark, outline);
                c = DrawBeam(c, fx, fy, Cap, woodLit, woodDark, outline);

                // 3. Iron bolts where the cap meets the posts.
                foreach (float[] post in Posts)
                {
                    float bd = Mathf.Sqrt((fx - post[0]) * (fx - post[0]) + (fy - Cap[1]) * (fy - Cap[1]));
                    c = Over(c, outline, Coverage(17f - bd));
                    c = Over(c, iron, Coverage(12f - bd));
                    c = Over(c, Color.white, 0.6f * Coverage(5f - Mathf.Sqrt((fx - post[0] + 4f) * (fx - post[0] + 4f) + (fy - Cap[1] + 4f) * (fy - Cap[1] + 4f))));
                }

                c.a = 1f;
                px[(Size - 1 - ty) * Size + x] = c;
            }

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// One beam: outline, then wood lit along its upper/left side, darker below, with grain. Square-ended
        /// (sawn timber — round ends read as a stool): the distance is a box around the segment.
        /// </summary>
        private static Color DrawBeam(Color c, float fx, float fy, float[] b, Color lit, Color dark, Color outline)
        {
            float d = SegmentDistance(fx, fy, b[0], b[1], b[2], b[3], out float t);
            float half = b[4];
            float endOut = EndOvershoot(fx, fy, b);               // > 0 beyond either sawn end
            float boxOutline = Mathf.Min(half + 9f - d, 9f - endOut);
            float boxBody    = Mathf.Min(half - d, -endOut);
            c = Over(c, outline, Coverage(boxOutline));
            float body = Coverage(boxBody);
            if (body <= 0f)
                return c;

            // Across-beam position −1..1 (sign from the side of the axis) → light from the top-left.
            float vx = b[2] - b[0], vy = b[3] - b[1];
            float side = Mathf.Sign((fx - b[0]) * vy - (fy - b[1]) * vx);
            float across = side * d / half;
            Color wood = Color.Lerp(lit, dark, Mathf.InverseLerp(-0.6f, 1f, across));

            // Grain: continuous streaks running along the beam, gently wavering.
            float len = Mathf.Sqrt(vx * vx + vy * vy);
            float along = t * len;
            float grain = Mathf.Sin(across * 11f + Mathf.Sin(along / 37f) * 1.6f + Mathf.Sin(along / 11f + across * 3f) * 0.35f);
            wood *= 0.9f + 0.1f * grain;
            return Over(c, wood, body);
        }

        /// <summary>How far (px) the point lies past either end of the beam's segment, along its axis.</summary>
        private static float EndOvershoot(float px, float py, float[] b)
        {
            float vx = b[2] - b[0], vy = b[3] - b[1];
            float len = Mathf.Sqrt(vx * vx + vy * vy);
            float along = ((px - b[0]) * vx + (py - b[1]) * vy) / len;
            return Mathf.Max(-along, along - len);
        }

        private static float SegmentDistance(float px, float py, float ax, float ay, float bx, float by, out float t)
        {
            float vx = bx - ax, vy = by - ay;
            t = Mathf.Clamp01(((px - ax) * vx + (py - ay) * vy) / (vx * vx + vy * vy));
            float dx = px - (ax + vx * t), dy = py - (ay + vy * t);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private static float Hash(int x, int y)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }

        /// <summary>Signed distance (px, positive inside) → 0..1 coverage with a ~1.5 px soft edge.</summary>
        private static float Coverage(float signedDistance) => Mathf.Clamp01(signedDistance / 1.5f + 0.5f);

        private static Color Over(Color dst, Color src, float a) => Color.Lerp(dst, src, Mathf.Clamp01(a));
    }
}
