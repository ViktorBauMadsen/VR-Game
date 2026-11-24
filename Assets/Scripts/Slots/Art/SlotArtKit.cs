using System;
using UnityEngine;

namespace VideoSlots
{
    // Atlas layouts shared by the art baker and the machines.
    public static class SymbolAtlas
    {
        public const int Cell = 256, Cols = 4, Rows = 4;
        public const int Width = Cell * Cols, Height = Cell * Rows * 2; // sharp half on top, motion-blurred half below

        public static Rect Uv(int index, bool blurred)
        {
            int col = index % Cols, row = index / Cols;
            float x = col * Cell + 1, y = (blurred ? 0 : Rows * Cell) + (Rows - 1 - row) * Cell + 1;
            return new Rect(x / Width, y / Height, (Cell - 2f) / Width, (Cell - 2f) / Height);
        }
    }

    public static class FxAtlas
    {
        public const int Cell = 128, Cols = 4, Size = 512;
        public const int Glow = 0, Sparkle = 1, Ring = 2, Coin = 3, White = 4, Line = 5, Flame = 6, Star = 7,
            Rays = 8, Bulb = 9, Frame = 10, SoftRect = 11, Disc = 12, Panel = 13, Smoke = 14, Diamond = 15;

        public static Rect Uv(int index)
        {
            int col = index % Cols, row = index / Cols;
            float inset = index == White ? 16f : 1f; // sample the middle of the solid cell
            float x = col * Cell + inset, y = (Cols - 1 - row) * Cell + inset;
            return new Rect(x / Size, y / Size, (Cell - 2 * inset) / Size, (Cell - 2 * inset) / Size);
        }
    }

    // Named rectangles inside a 1024x1024 UI atlas (logo, banners, badges).
    public struct UiRegion
    {
        public readonly string name;
        public readonly RectInt px;
        public UiRegion(string name, int x, int y, int w, int h) { this.name = name; px = new RectInt(x, y, w, h); }
        public Rect Uv => new Rect((px.x + 1f) / 1024f, (px.y + 1f) / 1024f, (px.width - 2f) / 1024f, (px.height - 2f) / 1024f);
        public float Aspect => (float)px.width / px.height;
    }

    public static class SlotArtKit
    {
        public static readonly Vector2 Light = new Vector2(-0.55f, 0.83f);
        public static readonly Color Ink = Pal.Hex("170805");

        public static void DropShadow(PaintCanvas c, Field f, float grow = 0f, float strength = 0.6f)
            => c.Shadow(grow != 0f ? f.Grow(grow) : f, new Vector2(0.012f, -0.022f), 0.035f, new Color(0f, 0f, 0f, strength));

        public static void GoldBody(PaintCanvas c, Field f, float y0, float y1, float ink = 0.016f, float bevel = 0.045f)
        {
            if (ink > 0f) c.Fill(f, Ink, ink);
            c.Fill(f, (p, d) => Pal.Gold(p.y, y0, y1));
            c.Bevel(f, bevel, 0.65f, Light);
        }

        public static void Gloss(PaintCanvas c, Field shape, Sdf highlight, float alpha)
        {
            var h = c.F(highlight);
            c.Inside(shape, h, (p, d) => new Color(1f, 1f, 1f, alpha * Mathf.Clamp01(-d * 25f)));
        }

        public static void Sparkle(PaintCanvas c, Vector2 at, float size, Color color)
        {
            var star = c.F(p => Sd.Polygon(p, Sd.StarPoints(at, size, size * 0.18f, 4, 0f)));
            c.Glow(star, size * 0.9f, Pal.A(color, 0.5f), 2f, true);
            c.Fill(star, color);
            c.Fill(c.F(p => Sd.Circle(p, at, size * 0.16f)), Color.white);
        }

        // Faceted emerald-cut gem.
        public static void Gem(PaintCanvas c, Vector2 center, Vector2 half, Color baseColor, float cut = 0.32f)
        {
            float hx = half.x, hy = half.y, cx = hx * cut, cy = hy * cut;
            Vector2[] outer =
            {
                new Vector2(-hx + cx, -hy), new Vector2(hx - cx, -hy), new Vector2(hx, -hy + cy), new Vector2(hx, hy - cy),
                new Vector2(hx - cx, hy), new Vector2(-hx + cx, hy), new Vector2(-hx, hy - cy), new Vector2(-hx, -hy + cy),
            };
            var inner = new Vector2[outer.Length];
            for (int i = 0; i < outer.Length; i++)
            {
                inner[i] = center + new Vector2(outer[i].x * 0.6f, outer[i].y * 0.56f) + new Vector2(0f, 0.008f);
                outer[i] += center;
            }

            var shape = c.F(p => Sd.Polygon(p, outer));
            DropShadow(c, shape);
            c.Glow(shape, 0.08f, Pal.A(baseColor, 0.35f), 2f);
            c.Fill(shape, Ink, 0.016f);
            Color dark = Pal.Mul(baseColor, 0.35f), light = Color.Lerp(baseColor, Color.white, 0.55f);
            for (int i = 0; i < outer.Length; i++)
            {
                int j = (i + 1) % outer.Length;
                Vector2[] quad = { outer[i], outer[j], inner[j], inner[i] };
                Vector2 mid = (outer[i] + outer[j]) * 0.5f - center;
                float shade = Vector2.Dot(mid.normalized, Light.normalized); // -1..1
                Color fc = shade > 0 ? Color.Lerp(baseColor, light, shade) : Color.Lerp(baseColor, dark, -shade);
                var facet = c.F(p => Sd.Polygon(p, quad));
                Vector2 o = outer[i], ii = inner[i];
                c.Fill(facet, (p, d) => Color.Lerp(fc, Pal.Mul(fc, 0.75f), Mathf.Clamp01((p - o).magnitude / Mathf.Max(0.01f, (ii - o).magnitude) * 0.6f)));
                c.Stroke(facet, 0.0025f, new Color(1f, 1f, 1f, 0.28f));
            }
            var table = c.F(p => Sd.Polygon(p, inner));
            float ty0 = center.y - hy * 0.56f, ty1 = center.y + hy * 0.56f;
            c.Fill(table, (p, d) => Pal.Ramp(Mathf.InverseLerp(ty0, ty1, p.y), Pal.Mul(baseColor, 0.55f), baseColor, Color.Lerp(baseColor, Color.white, 0.35f)));
            // Inner reflections.
            c.Inside(table, c.F(p => Sd.Polygon(p, new[] { inner[5], inner[6], center + new Vector2(-hx * 0.1f, 0f) })), (p, d) => new Color(1f, 1f, 1f, 0.35f));
            c.Inside(table, c.F(p => Sd.Polygon(p, new[] { inner[1], inner[2], center + new Vector2(hx * 0.15f, -hy * 0.05f) })), (p, d) => Pal.A(Pal.Mul(baseColor, 0.4f), 0.5f));
            c.Stroke(shape, 0.004f, new Color(1f, 1f, 1f, 0.5f), -0.004f);
            Sparkle(c, outer[5] + new Vector2(0.03f, -0.02f), 0.07f, Color.white);
        }

        // Royal (A K Q J 10) with a gold rim - Golden Inferno style.
        public static void RoyalGold(PaintCanvas c, SdfFont font, string s, Color top, Color bottom)
        {
            var t = font.Text(c.W, c.H, s, new Rect(0.08f, 0.1f, 0.84f, 0.8f), 0.0f, 0.78f).Grow(0.01f);
            DropShadow(c, t, 0.05f);
            c.Glow(t, 0.1f, Pal.A(bottom, 0.45f), 2f);
            c.Fill(t, Ink, 0.052f);
            c.Fill(t, (p, d) => Pal.Gold(p.y, 0.08f, 0.92f), 0.038f);
            c.Bevel(t.Grow(0.038f), 0.02f, 0.6f, Light);
            c.Fill(t, Pal.Mul(bottom, 0.3f), 0.01f);
            c.Fill(t, (p, d) => Pal.Ramp(Mathf.InverseLerp(0.18f, 0.82f, p.y), Pal.Mul(bottom, 0.8f), bottom, top, Color.Lerp(top, Color.white, 0.5f)));
            c.Bevel(t, 0.035f, 0.55f, Light);
            Gloss(c, t, p => Sd.Ellipse(p, new Vector2(0.42f, 0.78f), new Vector2(0.4f, 0.16f)), 0.28f);
        }

        // Royal as a neon tube - Star Wheel style.
        public static void RoyalNeon(PaintCanvas c, SdfFont font, string s, Color color)
        {
            var t = font.Text(c.W, c.H, s, new Rect(0.1f, 0.12f, 0.8f, 0.76f), 0.0f, 0.78f).Grow(0.008f);
            c.Glow(t, 0.17f, Pal.A(color, 0.6f), 1.7f);
            c.Fill(t, Pal.Hex("12061f"), 0.04f);
            c.Fill(t, Pal.Mul(color, 0.75f), 0.026f);
            c.Fill(t, Color.Lerp(color, Color.white, 0.1f), 0.012f);
            c.Fill(t, (p, d) => Color.Lerp(color, Color.white, Mathf.Clamp01(-d / 0.022f) * 0.9f));
            Gloss(c, t, p => Sd.Ellipse(p, new Vector2(0.4f, 0.8f), new Vector2(0.4f, 0.14f)), 0.25f);
        }

        // Big chunky banner text ("BIG WIN", logos) in a box of the canvas.
        public static Field BannerText(PaintCanvas c, SdfFont font, string s, Rect box, Color top, Color bottom, Color rim, Color glow,
            float rimWidth = 0.05f, float tracking = 0.04f, Func<Vector2, Vector2> warp = null, bool goldRim = false)
        {
            var t = font.Text(c.W, c.H, s, box, tracking, 0.72f, warp).Grow(box.height * 0.02f);
            float h = box.height;
            if (glow.a > 0f) c.Glow(t, h * 0.35f, glow, 1.8f);
            c.Shadow(t.Grow(rimWidth * h * 1.4f), new Vector2(0.006f, -0.012f) * (h / 0.3f), 0.03f, new Color(0, 0, 0, 0.7f));
            c.Fill(t, Ink, rimWidth * h * 1.45f);
            if (goldRim) c.Fill(t, (p, d) => Pal.Gold(p.y, box.yMin, box.yMax), rimWidth * h);
            else c.Fill(t, rim, rimWidth * h);
            c.Bevel(t.Grow(rimWidth * h), rimWidth * h * 0.6f, 0.5f, Light);
            c.Fill(t, Ink, rimWidth * h * 0.25f);
            c.Fill(t, (p, d) => Pal.Ramp(Mathf.InverseLerp(box.yMin + h * 0.12f, box.yMax - h * 0.12f, p.y), Pal.Mul(bottom, 0.85f), bottom, top, Color.Lerp(top, Color.white, 0.6f)));
            c.Bevel(t, h * 0.06f, 0.5f, Light);
            float midY = box.center.y + h * 0.14f;
            Gloss(c, t, p => p.y > midY ? -0.01f : 0.01f, 0.18f);
            return t;
        }

        // ---------------------------------------------------------------- FX
        public static Color[] BuildFxAtlas()
        {
            var atlas = new Color[FxAtlas.Size * FxAtlas.Size];
            for (int i = 0; i < 16; i++)
            {
                var c = new PaintCanvas(FxAtlas.Cell, FxAtlas.Cell);
                DrawFx(c, i);
                int col = i % FxAtlas.Cols, row = i / FxAtlas.Cols;
                c.CopyTo(atlas, FxAtlas.Size, col * FxAtlas.Cell, (FxAtlas.Cols - 1 - row) * FxAtlas.Cell);
            }
            return atlas;
        }

        static void DrawFx(PaintCanvas c, int i)
        {
            Vector2 m = new Vector2(0.5f, 0.5f);
            switch (i)
            {
                case FxAtlas.Glow:
                    for (int k = 0; k < c.Px.Length; k++)
                    {
                        Vector2 p = new Vector2((k % c.W + 0.5f) / c.W, (k / c.W + 0.5f) / c.H);
                        float r = (p - m).magnitude / 0.5f;
                        float a = Mathf.Clamp01(1f - r); a = a * a * (0.6f + 0.4f * a);
                        c.Px[k] = new Color(1, 1, 1, a);
                    }
                    break;
                case FxAtlas.Sparkle:
                    c.Glow(c.F(p => Sd.Circle(p, m, 0.02f)), 0.35f, new Color(1, 1, 1, 0.55f), 2.5f);
                    c.Fill(c.F(p => Sd.Polygon(p, Sd.StarPoints(m, 0.48f, 0.05f, 4, 0f))), Color.white);
                    c.Fill(c.F(p => Sd.Polygon(p, Sd.StarPoints(m, 0.25f, 0.04f, 4, 45f))), new Color(1, 1, 1, 0.8f));
                    break;
                case FxAtlas.Ring:
                {
                    var f = c.F(p => Mathf.Abs(Sd.Circle(p, m, 0.38f)) - 0.035f);
                    c.Glow(f, 0.1f, new Color(1, 1, 1, 0.6f), 2f);
                    c.Fill(f, Color.white);
                    break;
                }
                case FxAtlas.Coin:
                {
                    var f = c.F(p => Sd.Circle(p, m, 0.44f));
                    c.Fill(f, Ink, 0.02f);
                    c.Fill(f, (p, d) => Pal.Gold(p.y, 0.06f, 0.94f));
                    c.Bevel(f, 0.08f, 0.8f, Light);
                    var inner = c.F(p => Sd.Circle(p, m, 0.31f));
                    c.Stroke(inner, 0.012f, Pal.Hex("8a5206"));
                    var star = c.F(p => Sd.Polygon(p, Sd.StarPoints(m, 0.22f, 0.095f, 5, 0f)));
                    c.Fill(star, (p, d) => Pal.Gold(p.y, 0.75f, 0.25f));
                    c.Bevel(star, 0.04f, 0.7f, Light);
                    break;
                }
                case FxAtlas.White:
                    for (int k = 0; k < c.Px.Length; k++) c.Px[k] = Color.white;
                    break;
                case FxAtlas.Line:
                    for (int k = 0; k < c.Px.Length; k++)
                    {
                        float y = (k / c.W + 0.5f) / c.H;
                        float d = Mathf.Abs(y - 0.5f) / 0.5f;
                        float a = Mathf.Clamp01(1f - d);
                        float core = Mathf.Clamp01(1f - d * 4f);
                        c.Px[k] = new Color(1, 1, 1, Mathf.Clamp01(a * a * 0.7f + core));
                    }
                    break;
                case FxAtlas.Flame:
                {
                    var f = c.F(p =>
                    {
                        Vector2 q = p - new Vector2(0.5f, 0.38f);
                        float r = 0.28f - Mathf.Max(0f, q.y) * 0.55f;
                        return new Vector2(q.x, q.y * (q.y > 0 ? 0.55f : 1f)).magnitude - Mathf.Max(0.02f, r);
                    });
                    c.Glow(f, 0.15f, new Color(1, 1, 1, 0.5f), 2f);
                    c.Fill(f, (p, d) => new Color(1, 1, 1, Mathf.Clamp01(-d * 12f + 0.3f)));
                    break;
                }
                case FxAtlas.Star:
                {
                    var f = c.F(p => Sd.Polygon(p, Sd.StarPoints(m, 0.46f, 0.2f, 5, 0f)));
                    c.Fill(f, Ink, 0.02f);
                    c.Fill(f, (p, d) => Pal.Gold(p.y, 0.05f, 0.95f));
                    c.Bevel(f, 0.08f, 0.8f, Light);
                    break;
                }
                case FxAtlas.Rays:
                    for (int k = 0; k < c.Px.Length; k++)
                    {
                        Vector2 p = new Vector2((k % c.W + 0.5f) / c.W, (k / c.W + 0.5f) / c.H) - m;
                        float ang = Mathf.Atan2(p.y, p.x);
                        float ray = Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 6f)), 6f);
                        float r = p.magnitude / 0.5f;
                        float a = ray * Mathf.Clamp01(1f - r) * Mathf.Clamp01(r * 4f);
                        c.Px[k] = new Color(1, 1, 1, a);
                    }
                    break;
                case FxAtlas.Bulb:
                {
                    var f = c.F(p => Sd.Circle(p, m, 0.2f));
                    c.Glow(f, 0.3f, new Color(1, 1, 1, 0.65f), 2f);
                    c.Fill(f, Color.white);
                    break;
                }
                case FxAtlas.Frame:
                {
                    var f = c.F(p => Mathf.Abs(Sd.Box(p, m, new Vector2(0.43f, 0.43f), 0.08f)) - 0.025f);
                    c.Glow(f, 0.06f, new Color(1, 1, 1, 0.7f), 1.5f);
                    c.Fill(f, Color.white);
                    break;
                }
                case FxAtlas.SoftRect:
                    for (int k = 0; k < c.Px.Length; k++)
                    {
                        Vector2 p = new Vector2((k % c.W + 0.5f) / c.W, (k / c.W + 0.5f) / c.H);
                        float d = Sd.Box(p, m, new Vector2(0.3f, 0.3f), 0.05f);
                        float a = Mathf.Clamp01(1f - d / 0.2f);
                        c.Px[k] = new Color(1, 1, 1, a * a * (3f - 2f * a));
                    }
                    break;
                case FxAtlas.Disc:
                    c.Fill(c.F(p => Sd.Circle(p, m, 0.47f)), Color.white);
                    break;
                case FxAtlas.Panel:
                    c.Fill(c.F(p => Sd.Box(p, m, new Vector2(0.47f, 0.47f), 0.12f)), Color.white);
                    break;
                case FxAtlas.Smoke:
                    for (int k = 0; k < c.Px.Length; k++)
                    {
                        Vector2 p = new Vector2((k % c.W + 0.5f) / c.W, (k / c.W + 0.5f) / c.H) - m;
                        float ang = Mathf.Atan2(p.y, p.x);
                        float r = p.magnitude / (0.38f + 0.05f * Mathf.Sin(ang * 5f) + 0.03f * Mathf.Sin(ang * 9f + 1f));
                        float a = Mathf.Clamp01(1f - r);
                        c.Px[k] = new Color(1, 1, 1, a * a * 0.8f);
                    }
                    break;
                case FxAtlas.Diamond:
                {
                    var f = c.F(p => Sd.Polygon(p, new[] { new Vector2(0.5f, 0.06f), new Vector2(0.94f, 0.5f), new Vector2(0.5f, 0.94f), new Vector2(0.06f, 0.5f) }));
                    c.Glow(f, 0.1f, new Color(1, 1, 1, 0.5f), 2f);
                    c.Fill(f, Color.white);
                    break;
                }
            }
        }

        // Assembles 16 symbol canvases into the symbol atlas, sharp + blurred.
        public static Color[] AssembleSymbols(Func<int, PaintCanvas> draw, int count)
        {
            var atlas = new Color[SymbolAtlas.Width * SymbolAtlas.Height];
            for (int i = 0; i < atlas.Length; i++) atlas[i] = new Color(0, 0, 0, 0);
            for (int i = 0; i < count; i++)
            {
                var canvas = draw(i);
                if (canvas == null) continue;
                canvas.FadeEdges(12);
                int col = i % SymbolAtlas.Cols, row = i / SymbolAtlas.Cols;
                int x = col * SymbolAtlas.Cell, yTop = SymbolAtlas.Rows * SymbolAtlas.Cell + (SymbolAtlas.Rows - 1 - row) * SymbolAtlas.Cell;
                canvas.CopyTo(atlas, SymbolAtlas.Width, x, yTop);
                canvas.BlurVertical(14).CopyTo(atlas, SymbolAtlas.Width, x, yTop - SymbolAtlas.Rows * SymbolAtlas.Cell);
            }
            return atlas;
        }
    }
}
