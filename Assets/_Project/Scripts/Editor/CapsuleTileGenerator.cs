using System.IO;
using UnityEditor;
using UnityEngine;

namespace HollowLines.EditorTools
{
    /// <summary>
    /// R6.5 (F05): regenerates Resources/Tiles/tile_capsule.png procedurally.
    ///
    /// The old AI tile was a small jerry can with a water DROP on flat cyan: the icon covered ~20 % of
    /// the tile (a few screen pixels at the 16-row camera), it read as water/fuel rather than air, and
    /// flat cyan sat close to the teal ColorB blocks. A first redesign used bubbles — the dev read
    /// them as water drops too: air has no shape, so any abstract icon is ambiguous.
    ///
    /// So the tile now says it: a Mr. Driller-style **pill capsule with "AIR" written on it**, in the
    /// HUD air bar's cyan, on a deep-blue tile no block uses. The word is the same as the HUD bar's
    /// caption, so "this fills that" needs no explanation. Letters are drawn as stroked segments
    /// (signed distance, ~1.5 px anti-aliased edges) — no font needed in an editor script.
    ///
    /// Same 512×512 size and path, so the import settings (512 PPU, bilinear) and BoardView's
    /// Resources.Load are untouched. BoardView adds a soft shimmer on top (DiamondShine shader, a
    /// separate gentler material instance).
    /// </summary>
    public static class CapsuleTileGenerator
    {
        private const int    Size = 512;
        private const string AssetPath = "Assets/_Project/Art/Resources/Tiles/tile_capsule.png";

        [MenuItem("Hollow Lines/Regenerate Air Capsule Tile")]
        public static void Regenerate()
        {
            var tex = Generate();
            File.WriteAllBytes(AssetPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
            Debug.Log($"[CapsuleTileGenerator] Wrote {AssetPath}");
        }

        // Pill (top-left-origin pixels): a horizontal stadium filling most of the tile.
        private const float PillX0 = 34f, PillX1 = 478f, PillY0 = 146f, PillY1 = 366f;
        private const float StrokeHalf  = 17f;  // letter stroke half-width
        private const float OutlineHalf = 29f;  // letter outline half-width (stroke + 12 px dark rim)

        // "AIR" as line segments (x0, y0, x1, y1), cap height 196 → 316, centred on x = 256.
        private static readonly float[][] Letters =
        {
            // A
            new[] { 112f, 316f, 158f, 196f }, new[] { 158f, 196f, 204f, 316f }, new[] { 132f, 270f, 184f, 270f },
            // I
            new[] { 252f, 196f, 252f, 316f },
            // R — stem, bowl (top bar, three-segment curve, middle bar), leg
            new[] { 300f, 196f, 300f, 316f },
            new[] { 300f, 196f, 340f, 196f }, new[] { 340f, 196f, 362f, 208f }, new[] { 362f, 208f, 368f, 228f },
            new[] { 368f, 228f, 362f, 246f }, new[] { 362f, 246f, 340f, 256f }, new[] { 340f, 256f, 300f, 256f },
            new[] { 336f, 256f, 378f, 316f },
        };

        public static Texture2D Generate()
        {
            var bgTop     = new Color(0.10f, 0.20f, 0.38f);
            var bgBottom  = new Color(0.04f, 0.09f, 0.20f);
            var frame     = new Color(0.35f, 0.85f, 0.95f);   // = HUD air bar cyan (HUDView.ColCyan)
            var pillTop   = new Color(0.62f, 0.95f, 1.00f);
            var pillBot   = new Color(0.20f, 0.68f, 0.88f);
            var pillRim   = new Color(0.03f, 0.10f, 0.20f);
            var letterRim = new Color(0.04f, 0.18f, 0.32f);

            var px = new Color[Size * Size];
            for (int ty = 0; ty < Size; ty++)
            for (int x = 0; x < Size; x++)
            {
                float fx = x + 0.5f, fy = ty + 0.5f;

                // 1. Tile: vertical gradient + a soft cyan inner frame.
                Color c = Color.Lerp(bgTop, bgBottom, fy / Size);
                float edge = Mathf.Min(Mathf.Min(fx, fy), Mathf.Min(Size - fx, Size - fy));
                c = Over(c, frame, 0.45f * (1f - Mathf.SmoothStep(4f, 22f, edge)));

                // 2. Pill: dark rim, then a vertically shaded body with a glossy top band.
                float pd = PillDistance(fx, fy);                     // negative inside
                c = Over(c, pillRim, Coverage(-pd + 10f));
                float body = Coverage(-pd - 2f);
                if (body > 0f)
                {
                    float v = Mathf.InverseLerp(PillY0, PillY1, fy);
                    Color fill = Color.Lerp(pillTop, pillBot, v);
                    c = Over(c, fill, body);
                    // Gloss: a pale band across the upper third, inset from the ends.
                    float gloss = Coverage(-PillDistance(fx, fy, inset: 26f)) * (1f - Mathf.SmoothStep(0.10f, 0.32f, v));
                    c = Over(c, Color.white, 0.45f * gloss);
                }

                // 3. "AIR": dark outline, then white strokes.
                float ld = LettersDistance(fx, fy);
                c = Over(c, letterRim, Coverage(OutlineHalf - ld));
                c = Over(c, Color.white, Coverage(StrokeHalf - ld));

                c.a = 1f;
                px[(Size - 1 - ty) * Size + x] = c;
            }

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        /// <summary>Signed distance to the pill (stadium) shape; negative inside.</summary>
        private static float PillDistance(float fx, float fy, float inset = 0f)
        {
            float r  = (PillY1 - PillY0) * 0.5f - inset;
            float cy = (PillY0 + PillY1) * 0.5f;
            float ax = PillX0 + (PillY1 - PillY0) * 0.5f, bx = PillX1 - (PillY1 - PillY0) * 0.5f;
            float qx = Mathf.Clamp(fx, ax, bx);
            float dx = fx - qx, dy = fy - cy;
            return Mathf.Sqrt(dx * dx + dy * dy) - r;
        }

        private static float LettersDistance(float fx, float fy)
        {
            float best = float.MaxValue;
            foreach (float[] s in Letters)
                best = Mathf.Min(best, SegmentDistance(fx, fy, s[0], s[1], s[2], s[3]));
            return best;
        }

        private static float SegmentDistance(float px, float py, float ax, float ay, float bx, float by)
        {
            float vx = bx - ax, vy = by - ay;
            float t = Mathf.Clamp01(((px - ax) * vx + (py - ay) * vy) / (vx * vx + vy * vy));
            float dx = px - (ax + vx * t), dy = py - (ay + vy * t);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Signed distance (px, positive inside) → 0..1 coverage with a ~1.5 px soft edge.</summary>
        private static float Coverage(float signedDistance) => Mathf.Clamp01(signedDistance / 1.5f + 0.5f);

        private static Color Over(Color dst, Color src, float a) => Color.Lerp(dst, src, Mathf.Clamp01(a));
    }
}
