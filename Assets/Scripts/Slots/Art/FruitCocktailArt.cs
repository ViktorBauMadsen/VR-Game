using UnityEngine;
using K = VideoSlots.SlotArtKit;
using M = VideoSlots.FruitCocktailMath;

namespace VideoSlots
{
    // Symbol and UI art for FRUIT COCKTAIL (remastered): glossy juicy fruit,
    // a sparkling diamond scatter and a sunrise-cocktail wild.
    public static class FruitCocktailArt
    {
        public static readonly UiRegion Logo = new UiRegion("logo", 0, 768, 1024, 256);
        public static readonly UiRegion FreeSpins = new UiRegion("freespins", 0, 640, 1024, 128);
        public static readonly UiRegion BigWin = new UiRegion("bigwin", 0, 512, 512, 128);
        public static readonly UiRegion MegaWin = new UiRegion("megawin", 512, 512, 512, 128);
        public static readonly UiRegion EpicWin = new UiRegion("epicwin", 0, 384, 512, 128);
        public static readonly UiRegion TotalWin = new UiRegion("totalwin", 512, 384, 512, 128);
        public static readonly UiRegion Special = new UiRegion("special", 0, 256, 1024, 128);
        public static readonly UiRegion Expanding = new UiRegion("expanding", 0, 128, 512, 128);
        public static readonly UiRegion PlusSpins = new UiRegion("plusspins", 512, 128, 512, 128);
        public static readonly UiRegion Spins = new UiRegion("spins", 0, 0, 256, 128);
        public static readonly UiRegion Win = new UiRegion("win", 256, 0, 256, 128);
        public static readonly UiRegion Bonus = new UiRegion("bonus", 512, 0, 512, 128);

        static readonly Color Pink = Pal.Hex("ff2f92"), Aqua = Pal.Hex("2ee6ff"), Lime = Pal.Hex("8cff3a"), Juice = Pal.Hex("ff8a1a");

        public static Color[] BuildSymbols(SdfFont font) => K.AssembleSymbols(i => DrawSymbol(i, font), M.SymbolCount);

        public static PaintCanvas DrawSymbol(int s, SdfFont font)
        {
            var c = new PaintCanvas(SymbolAtlas.Cell, SymbolAtlas.Cell);
            switch (s)
            {
                case M.Diamond: Diamond(c); break;
                case M.Wild: Cocktail(c, font, true); break;
                case M.Seven: Seven(c, font); break;
                case M.Bar: BarPlate(c, font); break;
                case M.Bell: Bell(c); break;
                case M.Melon: Melon(c); break;
                case M.Grapes: Grapes(c); break;
                case M.Orange: OrangeFruit(c); break;
                case M.Lemon: Lemon(c); break;
                case M.Cherry: Cherries(c); break;
                default: return null;
            }
            return c;
        }

        // ------------------------------------------------------------ helpers
        // Glossy round fruit: shadow, dark rim, light from the upper left,
        // reflected light on the lower right, gloss and a specular dot.
        static void Ball(PaintCanvas c, Field f, Vector2 c0, float r, Color dark, Color mid, Color light, float gloss = 0.7f, float dimples = 0f)
        {
            K.DropShadow(c, f);
            c.Fill(f, K.Ink, 0.012f);
            Vector2 hl = c0 + new Vector2(-0.36f, 0.4f) * r;
            Color hot = Color.Lerp(light, Color.white, 0.5f), deep = Pal.Mul(mid, 0.78f);
            c.Fill(f, (p, d) =>
            {
                float u = Mathf.Clamp01((p - hl).magnitude / (r * 1.9f));
                Color col = Pal.Ramp(u, hot, light, mid, deep, dark);
                if (dimples > 0f) col = Pal.Mul(col, 1f - dimples * Mathf.PerlinNoise(p.x * 75f, p.y * 75f));
                col.a = 1f;
                return col;
            });
            Vector2 rimDir = new Vector2(0.72f, -0.69f);
            c.Stroke(f, r * 0.05f, (p, d) => new Color(1f, 1f, 1f, 0.32f * Mathf.Clamp01(Vector2.Dot((p - c0) / r, rimDir))), -r * 0.1f);
            c.Bevel(f, r * 0.22f, 0.28f, K.Light);
            K.Gloss(c, f, p => Sd.Ellipse(Sd.Rotate(p, hl, 40f), hl, new Vector2(0.34f, 0.19f) * r), gloss);
            Vector2 dot = hl + new Vector2(0.12f, -0.08f) * r;
            c.Fill(c.F(p => Sd.Circle(p, dot, r * 0.055f)), new Color(1f, 1f, 1f, 0.85f));
        }

        // Leaf from a (stem end) to b (tip), width = half its breadth.
        static void Leaf(PaintCanvas c, Vector2 a, Vector2 b, float width)
        {
            Vector2 dir = b - a;
            float len = dir.magnitude;
            Vector2 n = new Vector2(-dir.y, dir.x) / len;
            Vector2 mid = (a + b) * 0.5f;
            float half = len * 0.5f;
            float R = (half * half + width * width) / (2f * width), off = R - width;
            Vector2 c1 = mid + n * off, c2 = mid - n * off;
            var leaf = c.F(p => Mathf.Max(Sd.Circle(p, c1, R), Sd.Circle(p, c2, R)));
            K.DropShadow(c, leaf, 0f, 0.35f);
            c.Fill(leaf, K.Ink, 0.01f);
            Color dark = Pal.Hex("1d6b14"), baseC = Pal.Hex("3fae2a"), light = Pal.Hex("a6e862");
            c.Fill(leaf, (p, d) =>
            {
                float t = Mathf.Clamp01(Vector2.Dot(p - a, dir) / (len * len));
                Color col = Pal.Ramp(t, dark, baseC, light);
                return Vector2.Dot(p - mid, n) > 0f ? Color.Lerp(col, Color.white, 0.12f) : Pal.Mul(col, 0.78f);
            });
            var veins = c.F(p =>
            {
                float v = Sd.Segment(p, a + dir * 0.05f, b - dir * 0.1f, 0.0035f);
                for (int i = 1; i <= 3; i++)
                {
                    Vector2 s0 = a + dir * (0.2f * i);
                    v = Mathf.Min(v, Sd.Segment(p, s0, s0 + dir * 0.14f + n * width * 0.55f, 0.0022f));
                    v = Mathf.Min(v, Sd.Segment(p, s0, s0 + dir * 0.14f - n * width * 0.55f, 0.0022f));
                }
                return v;
            });
            c.Inside(leaf, veins, (p, d) => new Color(0.85f, 1f, 0.6f, 0.55f));
            c.Bevel(leaf, 0.018f, 0.35f, K.Light);
        }

        // A tapering quadratic curve (stems).
        static Field Curve(PaintCanvas c, Vector2 a, Vector2 ctrl, Vector2 b, float r0, float r1)
        {
            const int n = 14;
            var pts = new Vector2[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                pts[i] = (1f - t) * (1f - t) * a + 2f * (1f - t) * t * ctrl + t * t * b;
            }
            return c.F(p =>
            {
                float d = 1e9f;
                for (int i = 0; i < n; i++) d = Mathf.Min(d, Sd.Segment(p, pts[i], pts[i + 1], Mathf.Lerp(r0, r1, (i + 0.5f) / n)));
                return d;
            });
        }

        // A citrus slice seen face-on.
        static void OrangeWheel(PaintCanvas c, Vector2 w0, float R, float rot)
        {
            var rind = c.F(p => Sd.Circle(p, w0, R));
            K.DropShadow(c, rind);
            c.Fill(rind, K.Ink, 0.01f);
            Color r0 = Pal.Hex("c24f00"), r1 = Pal.Hex("ff9414"), r2 = Pal.Hex("ffc64f");
            c.Fill(rind, (p, d) => Pal.Ramp(Mathf.InverseLerp(w0.y - R, w0.y + R, p.y), r0, r1, r2));
            c.Bevel(rind, R * 0.08f, 0.5f, K.Light);
            c.Fill(c.F(p => Sd.Circle(p, w0, R * 0.87f)), Pal.Hex("fff2d4"));
            var flesh = c.F(p => Sd.Circle(p, w0, R * 0.8f));
            float step = Mathf.PI * 2f / 9f, fr = R * 0.8f, off = rot * Mathf.Deg2Rad;
            Color f0 = Pal.Hex("ffe6a8"), f1 = Pal.Hex("ffb53a"), f2 = Pal.Hex("ff8a12"), pith = Pal.Hex("fff3d8");
            c.Fill(flesh, (p, d) =>
            {
                Vector2 v = p - w0;
                float m = v.magnitude, a = Mathf.Atan2(v.y, v.x) + off;
                float local = Mathf.Repeat(a, step) - step * 0.5f;
                float edge = (step * 0.5f - Mathf.Abs(local)) * m;
                float u = m / fr;
                Color col = Pal.Mul(Pal.Ramp(u, f0, f1, f2), 0.9f + 0.1f * Mathf.Sin(a * 70f + u * 25f));
                float k = Mathf.Clamp01((edge - R * 0.03f) / (R * 0.025f)) * Mathf.Clamp01((u - 0.14f) * 14f);
                Color res = Color.Lerp(pith, col, k);
                res.a = 1f;
                return res;
            });
            Vector2 g = w0 + new Vector2(-R * 0.3f, R * 0.38f);
            K.Gloss(c, flesh, p => Sd.Ellipse(Sd.Rotate(p, g, 35f), g, new Vector2(R * 0.42f, R * 0.18f)), 0.4f);
            c.Fill(c.F(p => Sd.Circle(p, g + new Vector2(-R * 0.05f, R * 0.02f), R * 0.06f)), new Color(1f, 1f, 1f, 0.8f));
        }

        // ------------------------------------------------------------ symbols
        static void Diamond(PaintCanvas c)
        {
            Vector2 TL = new Vector2(0.35f, 0.81f), TR = new Vector2(0.65f, 0.81f), GL = new Vector2(0.13f, 0.64f), GR = new Vector2(0.87f, 0.64f);
            Vector2 g1 = new Vector2(0.31f, 0.64f), g2 = new Vector2(0.5f, 0.64f), g3 = new Vector2(0.69f, 0.64f), C = new Vector2(0.5f, 0.16f);
            Vector2[] outline = { GL, TL, TR, GR, C };
            var shape = c.F(p => Sd.Polygon(p, outline));

            // Light burst and a cool glow behind.
            Vector2 rc = new Vector2(0.5f, 0.56f);
            var star = Sd.StarPoints(rc, 0.48f, 0.075f, 12, 7.5f);
            Color rayC = Pal.Hex("c8f6ff");
            c.Fill(c.F(p => Sd.Polygon(p, star)), (p, d) => Pal.A(rayC, 0.55f * Mathf.Clamp01(1f - (p - rc).magnitude / 0.48f)));
            c.Glow(shape, 0.15f, Pal.A(Pal.Hex("38d2ff"), 0.7f), 1.6f);
            K.DropShadow(c, shape);
            c.Fill(shape, K.Ink, 0.014f);

            Color[] ramp = { Pal.Hex("071c5c"), Pal.Hex("1466e0"), Pal.Hex("3cc8ff"), Pal.Hex("b8f3ff"), Color.white };
            void Facet(Vector2[] pts, float v, Color tint, float tintK, float y0, float y1)
            {
                var f = c.F(p => Sd.Polygon(p, pts));
                Color lo = Color.Lerp(Pal.Ramp(Mathf.Clamp01(v - 0.2f), ramp), tint, tintK);
                Color hi = Color.Lerp(Pal.Ramp(Mathf.Clamp01(v + 0.2f), ramp), tint, tintK * 0.6f);
                c.Fill(f, (p, d) => Color.Lerp(lo, hi, Mathf.InverseLerp(y0, y1, p.y)));
                c.Stroke(f, 0.0028f, new Color(1f, 1f, 1f, 0.5f));
            }
            Color none = Color.white, pink = Pal.Hex("ff9ae6"), violet = Pal.Hex("8a5cff"), mint = Pal.Hex("7affd8");
            float gy = g2.y, ty = TL.y, cy = C.y;
            Facet(new[] { GL, TL, g1 }, 0.8f, mint, 0.18f, gy, ty);
            Facet(new[] { TL, g2, g1 }, 0.55f, none, 0f, gy, ty);
            Facet(new[] { TL, TR, g2 }, 0.98f, none, 0f, gy, ty);
            Facet(new[] { TR, g3, g2 }, 0.42f, violet, 0.22f, gy, ty);
            Facet(new[] { TR, GR, g3 }, 0.3f, none, 0f, gy, ty);
            Facet(new[] { GL, g1, C }, 0.64f, none, 0f, cy, gy);
            Facet(new[] { g1, g2, C }, 0.88f, pink, 0.32f, cy, gy);
            Facet(new[] { g2, g3, C }, 0.4f, violet, 0.32f, cy, gy);
            Facet(new[] { g3, GR, C }, 0.16f, none, 0f, cy, gy);

            c.Inside(shape, c.F(p => Mathf.Abs(p.y - gy) - 0.004f), (p, d) => new Color(1f, 1f, 1f, 0.75f));
            Vector2 streak = new Vector2(0.44f, 0.6f);
            K.Gloss(c, shape, p => Sd.Box(Sd.Rotate(p, streak, 32f), streak, new Vector2(0.016f, 0.2f)), 0.3f);
            c.Stroke(shape, 0.0035f, new Color(1f, 1f, 1f, 0.8f), -0.004f);
            K.Sparkle(c, TL + new Vector2(0.012f, -0.012f), 0.12f, Color.white);
            K.Sparkle(c, GR + new Vector2(-0.03f, 0f), 0.07f, Color.white);
            K.Sparkle(c, new Vector2(0.43f, 0.33f), 0.05f, Pal.Hex("d8f6ff"));
        }

        // Tequila-sunrise cocktail; with the WILD plaque on the symbol, without it on the logo.
        static void Cocktail(PaintCanvas c, SdfFont font, bool plaque)
        {
            Vector2 rimL = new Vector2(0.2f, 0.78f), rimR = new Vector2(0.8f, 0.78f), apex = new Vector2(0.5f, 0.44f), rimC = new Vector2(0.5f, 0.78f);
            var halo = c.F(p => Sd.Circle(p, new Vector2(0.5f, 0.6f), 0.12f));
            c.Glow(halo, 0.26f, Pal.A(Pink, 0.5f), 2.2f);

            Vector2[] bowlPts = { rimL, rimR, apex };
            var bowl = c.F(p => Mathf.Min(Sd.Polygon(p, bowlPts), Sd.Ellipse(p, rimC, new Vector2(0.3f, 0.035f))));
            var stem = c.F(p => Mathf.Min(Sd.Box(p, new Vector2(0.5f, 0.34f), new Vector2(0.016f, 0.11f)), Sd.Ellipse(p, new Vector2(0.5f, 0.235f), new Vector2(0.14f, 0.03f))));
            var glass = Field.Union(bowl, stem);
            K.DropShadow(c, glass, 0f, 0.45f);
            c.Fill(glass, Pal.A(K.Ink, 0.85f), 0.01f);
            Color glassC = Pal.Hex("d8f4ff");
            c.Fill(glass, (p, d) => Pal.A(glassC, 0.22f + 0.6f * Mathf.Clamp01(1f + d / 0.018f)));

            // The drink.
            Vector2[] drinkPts = { new Vector2(0.235f, 0.748f), new Vector2(0.765f, 0.748f), new Vector2(0.5f, 0.478f) };
            var drink = c.F(p => Sd.Polygon(p, drinkPts));
            Color d0 = Pal.Hex("d40c3a"), d1 = Pal.Hex("ff4a1f"), d2 = Pal.Hex("ffa82a"), d3 = Pal.Hex("ffe46a");
            c.Fill(drink, (p, d) => Pal.Ramp(Mathf.InverseLerp(0.48f, 0.745f, p.y), d0, d1, d2, d3));
            var surface = c.F(p => Sd.Ellipse(p, new Vector2(0.5f, 0.748f), new Vector2(0.265f, 0.024f)));
            Color s0 = Pal.Hex("ffd04a"), s1 = Pal.Hex("fff6c8");
            c.Fill(surface, (p, d) => Color.Lerp(s0, s1, Mathf.InverseLerp(0.725f, 0.77f, p.y)));
            Vector2[] bubbles = { new Vector2(0.45f, 0.56f), new Vector2(0.53f, 0.63f), new Vector2(0.41f, 0.68f), new Vector2(0.61f, 0.7f), new Vector2(0.5f, 0.52f), new Vector2(0.35f, 0.72f) };
            for (int i = 0; i < bubbles.Length; i++)
            {
                Vector2 b = bubbles[i];
                float r = 0.008f + 0.004f * (i % 3);
                c.Stroke(c.F(p => Sd.Circle(p, b, r)), 0.0025f, new Color(1f, 1f, 1f, 0.6f));
                c.Fill(c.F(p => Sd.Circle(p, b + new Vector2(-0.003f, 0.003f), r * 0.35f)), new Color(1f, 1f, 1f, 0.75f));
            }

            // Striped straw.
            Vector2 sa = new Vector2(0.55f, 0.56f), sb = new Vector2(0.64f, 0.9f);
            var straw = c.F(p => Sd.Segment(p, sa, sb, 0.016f));
            c.Fill(straw, K.Ink, 0.006f);
            Vector2 sd = (sb - sa).normalized;
            Color red = Pal.Hex("ff2a5a"), white = Pal.Hex("fff4f8");
            c.Fill(straw, (p, d) => Mathf.Sin(Vector2.Dot(p - sa, sd) * 90f) > 0f ? red : white);
            c.Bevel(straw, 0.012f, 0.5f, K.Light);

            // Glass highlights over the drink.
            c.Inside(bowl, c.F(p => Sd.Segment(p, new Vector2(0.27f, 0.74f), new Vector2(0.45f, 0.5f), 0.008f)), (p, d) => new Color(1f, 1f, 1f, 0.7f));
            c.Inside(stem, c.F(p => Sd.Box(p, new Vector2(0.494f, 0.36f), new Vector2(0.004f, 0.08f))), (p, d) => new Color(1f, 1f, 1f, 0.7f));
            c.Stroke(bowl, 0.003f, new Color(1f, 1f, 1f, 0.75f), -0.004f);
            c.Stroke(c.F(p => Sd.Ellipse(p, rimC, new Vector2(0.3f, 0.035f))), 0.0035f, new Color(1f, 1f, 1f, 0.85f));

            // Garnish: a cherry on a pick, an orange wheel on the rim.
            var pick = c.F(p => Sd.Segment(p, new Vector2(0.33f, 0.82f), new Vector2(0.43f, 0.58f), 0.006f));
            c.Fill(pick, K.Ink, 0.004f);
            c.Fill(pick, Pal.Hex("ffe2a0"));
            Cherry(c, new Vector2(0.33f, 0.83f), 0.05f);
            OrangeWheel(c, new Vector2(0.77f, 0.8f), 0.085f, 10f);

            if (plaque)
            {
                Vector2 pc = new Vector2(0.5f, 0.2f);
                var plate = c.F(p => Sd.Box(p, pc, new Vector2(0.39f, 0.1f), 0.05f));
                K.DropShadow(c, plate);
                K.GoldBody(c, plate, 0.1f, 0.3f, 0.016f, 0.025f);
                var inner = c.F(p => Sd.Box(p, pc, new Vector2(0.355f, 0.068f), 0.03f));
                c.Fill(inner, K.Ink, 0.008f);
                Color w0 = Pal.Hex("4a0030"), w1 = Pal.Hex("c0106a"), w2 = Pal.Hex("ff4fa8");
                c.Fill(inner, (p, d) => Pal.Ramp(Mathf.InverseLerp(0.132f, 0.268f, p.y), w0, w1, w2));
                var t = font.Text(c.W, c.H, "WILD", new Rect(0.17f, 0.125f, 0.66f, 0.15f), 0.08f, 0.9f).Grow(0.006f);
                c.Fill(t, K.Ink, 0.014f);
                Color t0 = Pal.Hex("ffc83a"), t1 = Pal.Hex("fffbe0");
                c.Fill(t, (p, d) => Color.Lerp(t0, t1, Mathf.InverseLerp(0.14f, 0.26f, p.y)));
                c.Bevel(t, 0.012f, 0.6f, K.Light);
                K.Gloss(c, inner, p => p.y > 0.21f ? -0.01f : 0.01f, 0.15f);
                K.Sparkle(c, new Vector2(0.87f, 0.28f), 0.07f, Color.white);
            }
            K.Sparkle(c, new Vector2(0.15f, 0.6f), 0.06f, Color.white);
        }

        static void Seven(PaintCanvas c, SdfFont font)
        {
            Vector2 m = new Vector2(0.5f, 0.5f);
            var star = Sd.StarPoints(m, 0.47f, 0.15f, 10, 0f);
            Color rayC = new Color(1f, 0.82f, 0.35f);
            c.Fill(c.F(p => Sd.Polygon(p, star)), (p, d) => Pal.A(rayC, 0.4f * Mathf.Clamp01(1f - (p - m).magnitude / 0.47f)));
            Color top = Pal.Hex("ff8a8a"), bottom = Pal.Hex("d8001c"), deep = Pal.Hex("4a0008");
            var t = font.Text(c.W, c.H, "7", new Rect(0.1f, 0.08f, 0.8f, 0.84f), 0f, 0.82f, p => new Vector2(p.x - 0.2f * (p.y - 0.5f), p.y)).Grow(0.012f);
            K.DropShadow(c, t, 0.05f);
            c.Glow(t, 0.12f, Pal.A(Pal.Hex("ff2a2a"), 0.5f), 2f);
            c.Fill(t, K.Ink, 0.055f);
            c.Fill(t, (p, d) => Pal.Gold(p.y, 0.08f, 0.92f), 0.04f);
            c.Bevel(t.Grow(0.04f), 0.022f, 0.6f, K.Light);
            c.Fill(t, deep, 0.01f);
            c.Fill(t, (p, d) => Pal.Ramp(Mathf.InverseLerp(0.14f, 0.86f, p.y), Pal.Mul(bottom, 0.7f), bottom, top, Color.Lerp(top, Color.white, 0.55f)));
            c.Bevel(t, 0.04f, 0.55f, K.Light);
            c.Stroke(t, 0.004f, new Color(1f, 1f, 1f, 0.5f), -0.024f);
            K.Gloss(c, t, p => Sd.Ellipse(p, new Vector2(0.5f, 0.8f), new Vector2(0.42f, 0.13f)), 0.32f);
            K.Sparkle(c, new Vector2(0.8f, 0.8f), 0.085f, Color.white);
            K.Sparkle(c, new Vector2(0.27f, 0.2f), 0.05f, Pal.Hex("ffe0e0"));
        }

        static void BarPlate(PaintCanvas c, SdfFont font)
        {
            Vector2 m = new Vector2(0.5f, 0.5f);
            var plate = c.F(p => Sd.Box(p, m, new Vector2(0.41f, 0.2f), 0.06f));
            c.Glow(plate, 0.1f, Pal.A(Pal.Hex("3a7aff"), 0.45f), 2f);
            K.DropShadow(c, plate);
            K.GoldBody(c, plate, 0.3f, 0.7f, 0.016f, 0.03f);
            var inner = c.F(p => Sd.Box(p, m, new Vector2(0.365f, 0.155f), 0.035f));
            Color b0 = Pal.Hex("06103a"), b1 = Pal.Hex("1438b0"), b2 = Pal.Hex("4f8aff");
            c.Fill(inner, K.Ink, 0.008f);
            c.Fill(inner, (p, d) => Pal.Ramp(Mathf.InverseLerp(0.345f, 0.655f, p.y), b0, b1, b2));
            c.Bevel(inner, 0.02f, -0.5f, K.Light);
            var t = font.Text(c.W, c.H, "BAR", new Rect(0.15f, 0.375f, 0.7f, 0.25f), 0.04f, 0.88f).Grow(0.006f);
            c.Fill(t, K.Ink, 0.018f);
            c.Fill(t, (p, d) => Pal.Gold(p.y, 0.38f, 0.62f));
            c.Bevel(t, 0.016f, 0.6f, K.Light);
            K.Gloss(c, inner, p => p.y > 0.53f ? -0.01f : 0.01f, 0.13f);
            K.Sparkle(c, new Vector2(0.85f, 0.69f), 0.08f, Color.white);
            K.Sparkle(c, new Vector2(0.14f, 0.32f), 0.05f, Pal.Hex("cfe0ff"));
        }

        static void Bell(PaintCanvas c)
        {
            Color[] metal = { Pal.Hex("5a3503"), Pal.Hex("b9780c"), Pal.Hex("ffd65a"), Pal.Hex("fff6d0"), Pal.Hex("ffd040"), Pal.Hex("b0700a"), Pal.Hex("4f2d02") };
            Vector2[] body = { new Vector2(0.31f, 0.6f), new Vector2(0.69f, 0.6f), new Vector2(0.735f, 0.42f), new Vector2(0.83f, 0.31f), new Vector2(0.17f, 0.31f), new Vector2(0.265f, 0.42f) };
            var clapper = c.F(p => Sd.Circle(p, new Vector2(0.5f, 0.215f), 0.055f));
            K.DropShadow(c, clapper);
            K.GoldBody(c, clapper, 0.16f, 0.27f, 0.012f, 0.03f);
            var bell = c.F(p => Sd.SmoothMin(Sd.SmoothMin(Sd.Circle(p, new Vector2(0.5f, 0.6f), 0.19f), Sd.Polygon(p, body), 0.06f),
                Sd.Ellipse(p, new Vector2(0.5f, 0.305f), new Vector2(0.35f, 0.055f)), 0.03f));
            K.DropShadow(c, bell);
            c.Fill(bell, K.Ink, 0.015f);
            c.Fill(bell, (p, d) => Pal.Mul(Pal.Ramp(Mathf.InverseLerp(0.14f, 0.86f, p.x), metal), Mathf.Lerp(0.78f, 1f, Mathf.InverseLerp(0.25f, 0.8f, p.y))));
            c.Inside(bell, c.F(p => Mathf.Abs(p.y - 0.375f) - 0.009f), (p, d) => new Color(0.4f, 0.22f, 0.02f, 0.55f));
            c.Inside(bell, c.F(p => Mathf.Abs(p.y - 0.393f) - 0.005f), (p, d) => new Color(1f, 0.95f, 0.75f, 0.6f));
            c.Bevel(bell, 0.04f, 0.5f, K.Light);
            Vector2 g = new Vector2(0.37f, 0.57f);
            K.Gloss(c, bell, p => Sd.Ellipse(Sd.Rotate(p, g, -8f), g, new Vector2(0.04f, 0.17f)), 0.55f);
            Bow(c, new Vector2(0.5f, 0.785f));
            K.Sparkle(c, new Vector2(0.76f, 0.64f), 0.08f, Color.white);
            K.Sparkle(c, new Vector2(0.21f, 0.37f), 0.05f, Pal.Hex("fff1b0"));
        }

        static void Bow(PaintCanvas c, Vector2 at)
        {
            Vector2 lc = at + new Vector2(-0.075f, 0.03f), rc = at + new Vector2(0.075f, 0.03f);
            Vector2[] tailL = { at, at + new Vector2(-0.075f, -0.095f), at + new Vector2(-0.035f, -0.105f) };
            Vector2[] tailR = { at, at + new Vector2(0.035f, -0.105f), at + new Vector2(0.075f, -0.095f) };
            var bow = c.F(p => Mathf.Min(
                Mathf.Min(Sd.Ellipse(Sd.Rotate(p, lc, 25f), lc, new Vector2(0.075f, 0.042f)), Sd.Ellipse(Sd.Rotate(p, rc, -25f), rc, new Vector2(0.075f, 0.042f))),
                Mathf.Min(Sd.Polygon(p, tailL), Sd.Polygon(p, tailR))));
            K.DropShadow(c, bow, 0f, 0.45f);
            c.Fill(bow, K.Ink, 0.01f);
            Color r0 = Pal.Hex("6a0010"), r1 = Pal.Hex("e0182a"), r2 = Pal.Hex("ff7070");
            c.Fill(bow, (p, d) => Pal.Ramp(Mathf.InverseLerp(at.y - 0.1f, at.y + 0.08f, p.y), r0, r1, r2));
            c.Bevel(bow, 0.016f, 0.5f, K.Light);
            var knot = c.F(p => Sd.Ellipse(p, at + new Vector2(0f, 0.01f), new Vector2(0.026f, 0.03f)));
            c.Fill(knot, K.Ink, 0.006f);
            c.Fill(knot, (p, d) => Pal.Ramp(Mathf.InverseLerp(at.y - 0.02f, at.y + 0.04f, p.y), r0, r1, r2));
            c.Bevel(knot, 0.012f, 0.6f, K.Light);
        }

        static void Melon(PaintCanvas c)
        {
            Vector2 piv = new Vector2(0.5f, 0.5f), A = new Vector2(0.5f, 0.85f);
            const float tilt = -9f, R = 0.67f;
            float half = 31f * Mathf.Deg2Rad;
            Vector2 nL = new Vector2(-Mathf.Cos(half), Mathf.Sin(half)), nR = new Vector2(Mathf.Cos(half), Mathf.Sin(half));
            Vector2 Q(Vector2 p) => Sd.Rotate(p, piv, -tilt);
            float Wedge(Vector2 q, float r) => Mathf.Max(Sd.Circle(q, A, r), Mathf.Max(Vector2.Dot(q - A, nL), Vector2.Dot(q - A, nR)));

            var outer = c.F(p => Wedge(Q(p), R));
            K.DropShadow(c, outer);
            c.Fill(outer, K.Ink, 0.014f);
            Color rindDark = Pal.Hex("0c3f12"), rindLight = Pal.Hex("4fb83a");
            c.Fill(outer, (p, d) =>
            {
                Vector2 q = Q(p) - A;
                float ang = Mathf.Atan2(q.x, -q.y);
                float u = Mathf.InverseLerp(R - 0.07f, R, q.magnitude);
                Color col = Color.Lerp(rindLight, rindDark, u);
                if (Mathf.Sin(ang * 36f) > 0.25f) col = Pal.Mul(col, 0.62f);
                return col;
            });
            Color w0 = Pal.Hex("e8ffd0"), w1 = Pal.Hex("a8e87a");
            var band = c.F(p => Wedge(Q(p), R - 0.05f));
            c.Fill(band, (p, d) => Color.Lerp(w0, w1, Mathf.InverseLerp(R - 0.075f, R - 0.05f, (Q(p) - A).magnitude)));
            var flesh = c.F(p => Wedge(Q(p), R - 0.075f));
            Color f0 = Pal.Hex("ff5a6a"), f1 = Pal.Hex("f0203c"), f2 = Pal.Hex("e0123a"), f3 = Pal.Hex("ff7a8a");
            c.Fill(flesh, (p, d) =>
            {
                Vector2 q = Q(p);
                float u = Mathf.Clamp01((q - A).magnitude / (R - 0.075f));
                Color col = Pal.Ramp(u, f0, f1, f2, f3);
                return Pal.Mul(col, 0.92f + 0.08f * Mathf.PerlinNoise(q.x * 45f, q.y * 45f));
            });

            float[,] seeds = { { 0.3f, -11f }, { 0.3f, 10f }, { 0.42f, -20f }, { 0.42f, -1f }, { 0.42f, 18f }, { 0.53f, -24f }, { 0.53f, -8f }, { 0.53f, 8f }, { 0.53f, 24f } };
            Color seedC = Pal.Hex("1a0805");
            for (int i = 0; i < seeds.GetLength(0); i++)
            {
                float sr = seeds[i, 0], deg = seeds[i, 1], sa = deg * Mathf.Deg2Rad;
                Vector2 sp = A + new Vector2(Mathf.Sin(sa), -Mathf.Cos(sa)) * sr;
                c.Fill(c.F(p => Sd.Ellipse(Sd.Rotate(Q(p), sp, -deg), sp, new Vector2(0.016f, 0.028f))), seedC);
                Vector2 hl = sp + new Vector2(-0.005f, 0.008f);
                c.Fill(c.F(p => Sd.Circle(Q(p), hl, 0.005f)), new Color(1f, 1f, 1f, 0.55f));
            }
            // Juicy shine along the cut.
            K.Gloss(c, flesh, p =>
            {
                Vector2 q = Q(p);
                return Mathf.Max(Mathf.Abs(Vector2.Dot(q - A, nL) + 0.04f) - 0.012f, Sd.Circle(q, A + new Vector2(-0.12f, -0.28f), 0.24f));
            }, 0.4f);
            c.Bevel(outer, 0.03f, 0.45f, K.Light);
            K.Sparkle(c, new Vector2(0.3f, 0.74f), 0.07f, Color.white);
        }

        static void Grapes(PaintCanvas c)
        {
            var stem = Curve(c, new Vector2(0.5f, 0.66f), new Vector2(0.49f, 0.8f), new Vector2(0.56f, 0.88f), 0.016f, 0.012f);
            K.DropShadow(c, stem, 0f, 0.4f);
            c.Fill(stem, K.Ink, 0.008f);
            Color st0 = Pal.Hex("4a2a0a"), st1 = Pal.Hex("8a5a22");
            c.Fill(stem, (p, d) => Color.Lerp(st0, st1, Mathf.InverseLerp(0.66f, 0.88f, p.y)));
            c.Bevel(stem, 0.012f, 0.5f, K.Light);
            Leaf(c, new Vector2(0.52f, 0.79f), new Vector2(0.8f, 0.86f), 0.06f);
            float[] rowY = { 0.645f, 0.52f, 0.395f, 0.27f };
            float[][] rowX = { new[] { 0.29f, 0.43f, 0.57f, 0.71f }, new[] { 0.36f, 0.5f, 0.64f }, new[] { 0.43f, 0.57f }, new[] { 0.5f } };
            Color dark = Pal.Hex("1e0438"), mid = Pal.Hex("7a2bd0"), light = Pal.Hex("c48cff");
            for (int row = 0; row < rowY.Length; row++)
                foreach (float x in rowX[row])
                {
                    Vector2 g = new Vector2(x, rowY[row]);
                    Ball(c, c.F(p => Sd.Ellipse(p, g, new Vector2(0.074f, 0.08f))), g, 0.078f, dark, mid, light, 0.65f);
                }
            K.Sparkle(c, new Vector2(0.23f, 0.75f), 0.07f, Color.white);
        }

        static void OrangeFruit(PaintCanvas c)
        {
            Vector2 o = new Vector2(0.43f, 0.53f);
            const float r = 0.29f;
            Vector2 top = o + new Vector2(0.04f, r - 0.035f);
            Leaf(c, top + new Vector2(0.01f, 0.005f), new Vector2(0.73f, 0.87f), 0.055f);
            Ball(c, c.F(p => Sd.Circle(p, o, r)), o, r, Pal.Hex("7a2400"), Pal.Hex("ff8200"), Pal.Hex("ffbf3f"), 0.55f, 0.12f);
            var calyxPts = Sd.StarPoints(top, 0.035f, 0.015f, 5, 10f);
            var calyx = c.F(p => Sd.Polygon(p, calyxPts));
            c.Fill(calyx, K.Ink, 0.006f);
            c.Fill(calyx, Pal.Hex("4f8a1e"));
            OrangeWheel(c, new Vector2(0.7f, 0.29f), 0.165f, 20f);
            K.Sparkle(c, new Vector2(0.19f, 0.79f), 0.065f, Color.white);
        }

        static void Lemon(PaintCanvas c)
        {
            Vector2 o = new Vector2(0.48f, 0.47f);
            const float tilt = 24f;
            Leaf(c, new Vector2(0.75f, 0.6f), new Vector2(0.87f, 0.84f), 0.05f);
            var f = c.F(p =>
            {
                Vector2 q = Sd.Rotate(p, o, -tilt);
                float body = Sd.Ellipse(q, o, new Vector2(0.315f, 0.225f));
                float tips = Mathf.Min(Sd.Circle(q, o + new Vector2(0.33f, 0f), 0.04f), Sd.Circle(q, o - new Vector2(0.33f, 0f), 0.04f));
                return Sd.SmoothMin(body, tips, 0.06f);
            });
            Ball(c, f, o, 0.27f, Pal.Hex("6b4a00"), Pal.Hex("f5c400"), Pal.Hex("fff27a"), 0.6f, 0.1f);
            K.Sparkle(c, new Vector2(0.2f, 0.74f), 0.07f, Color.white);
            K.Sparkle(c, new Vector2(0.72f, 0.26f), 0.045f, Pal.Hex("fff8c0"));
        }

        static void Cherries(PaintCanvas c)
        {
            Vector2 top = new Vector2(0.6f, 0.84f);
            Vector2 l = new Vector2(0.34f, 0.33f), r = new Vector2(0.65f, 0.28f);
            const float rad = 0.16f;
            var stems = Field.Union(
                Curve(c, l + new Vector2(0.01f, rad * 0.75f), new Vector2(0.4f, 0.68f), top, 0.013f, 0.01f),
                Curve(c, r + new Vector2(0f, rad * 0.75f), new Vector2(0.69f, 0.6f), top, 0.013f, 0.01f));
            K.DropShadow(c, stems, 0f, 0.4f);
            c.Fill(stems, K.Ink, 0.008f);
            Color s0 = Pal.Hex("3d7a14"), s1 = Pal.Hex("78bf2e"), s2 = Pal.Hex("b5e06a");
            c.Fill(stems, (p, d) => Pal.Ramp(Mathf.InverseLerp(0.4f, 0.86f, p.y), s0, s1, s2));
            c.Bevel(stems, 0.012f, 0.5f, K.Light);
            Leaf(c, top + new Vector2(0.005f, 0f), new Vector2(0.86f, 0.76f), 0.055f);
            var knot = c.F(p => Sd.Circle(p, top, 0.022f));
            c.Fill(knot, K.Ink, 0.006f);
            c.Fill(knot, Pal.Hex("5a3a12"));
            Cherry(c, l, rad);
            Cherry(c, r, rad);
            K.Sparkle(c, new Vector2(0.19f, 0.57f), 0.07f, Color.white);
        }

        static void Cherry(PaintCanvas c, Vector2 c0, float rad)
        {
            Vector2 o = new Vector2(rad * 0.18f, 0f);
            var f = c.F(p => Sd.SmoothMin(Sd.Circle(p, c0 - o, rad * 0.9f), Sd.Circle(p, c0 + o, rad * 0.9f), rad * 0.2f));
            Ball(c, f, c0, rad, Pal.Hex("3d0010"), Pal.Hex("d0061f"), Pal.Hex("ff4d5e"), 0.8f);
            var dent = c.F(p => Sd.Ellipse(p, c0 + new Vector2(0f, rad * 0.8f), new Vector2(rad * 0.28f, rad * 0.12f)));
            c.Inside(f, dent, (p, d) => new Color(0.25f, 0f, 0.04f, 0.55f * Mathf.Clamp01(-d * 60f)));
        }

        // ------------------------------------------------------------------ UI
        public static Color[] BuildUi(SdfFont font)
        {
            var atlas = new Color[1024 * 1024];
            for (int i = 0; i < atlas.Length; i++) atlas[i] = new Color(0, 0, 0, 0);
            Color goldTop = Pal.Hex("fff3b0"), goldBottom = Pal.Hex("ff9a1a"), plum = Pal.Hex("5a0a46"), none = new Color(0, 0, 0, 0);
            Color glowPink = Pal.A(Pink, 0.55f), glowJuice = Pal.A(Juice, 0.55f), glowAqua = Pal.A(Aqua, 0.5f), glowLime = Pal.A(Lime, 0.5f);

            Draw(atlas, Logo, c =>
            {
                float w = (float)c.W / c.H;
                // Berry-juice splash behind the words.
                Vector2[] drops = { new Vector2(w * 0.22f, 0.85f), new Vector2(w * 0.31f, 0.87f), new Vector2(w * 0.7f, 0.86f), new Vector2(w * 0.79f, 0.83f), new Vector2(w * 0.27f, 0.14f), new Vector2(w * 0.74f, 0.13f) };
                float[] dropR = { 0.032f, 0.018f, 0.024f, 0.03f, 0.024f, 0.03f };
                var splash = c.F(p =>
                {
                    float d = Sd.Box(p, new Vector2(w * 0.5f, 0.5f), new Vector2(w * 0.385f, 0.29f), 0.27f)
                        - 0.035f * Mathf.Sin(p.x * 13f + 0.6f) - 0.02f * Mathf.Sin(p.x * 29f + 2f);
                    for (int i = 0; i < drops.Length; i++) d = Sd.SmoothMin(d, Sd.Circle(p, drops[i], dropR[i]), 0.035f);
                    return d;
                });
                Color sp0 = Pal.Hex("5a0a7a"), sp1 = Pal.Hex("c8167a"), sp2 = Pal.Hex("ff3a8a");
                c.Glow(splash, 0.1f, Pal.A(Pink, 0.45f), 1.6f);
                c.Fill(splash, K.Ink, 0.012f);
                c.Fill(splash, (p, d) =>
                {
                    float x = Mathf.Abs(p.x / w - 0.5f) * 2f;
                    Color col = Pal.Ramp(x, sp1, Color.Lerp(sp0, sp1, 0.4f), sp0);
                    return Color.Lerp(Pal.Mul(col, 0.75f), Color.Lerp(col, sp2, 0.35f), Mathf.InverseLerp(0.2f, 0.8f, p.y));
                });
                c.Bevel(splash, 0.04f, 0.35f, K.Light);
                K.Gloss(c, splash, p => p.y > 0.62f ? -0.01f : 0.01f, 0.12f);

                // Fruit at the ends: cherries on the left, the cocktail on the right.
                var cherry = new PaintCanvas(SymbolAtlas.Cell, SymbolAtlas.Cell);
                Cherries(cherry);
                Stamp(c, cherry, new Vector2(0.5f, 0.5f), 0.92f, -10f);
                var glass = new PaintCanvas(SymbolAtlas.Cell, SymbolAtlas.Cell);
                Cocktail(glass, font, false);
                Stamp(c, glass, new Vector2(w - 0.5f, 0.52f), 0.95f, 9f);

                System.Func<Vector2, Vector2> arch = p => new Vector2(p.x, p.y + 0.1f * (1f - Mathf.Pow((p.x - w * 0.5f) / (w * 0.5f), 2f)) - 0.05f);
                K.BannerText(c, font, "FRUIT", new Rect(w * 0.33f, 0.57f, w * 0.34f, 0.36f), Pal.Hex("f4ffd8"), Pal.Hex("4ac81a"), plum, none, 0.07f, 0.1f, null, true);
                K.BannerText(c, font, "COCKTAIL", new Rect(w * 0.15f, 0.06f, w * 0.7f, 0.56f), Pal.Hex("fffbe0"), Pal.Hex("ff8a00"), plum, glowJuice, 0.06f, 0.05f, arch, true);
                K.Sparkle(c, new Vector2(w * 0.3f, 0.84f), 0.07f, Color.white);
                K.Sparkle(c, new Vector2(w * 0.72f, 0.2f), 0.06f, Color.white);
                K.Sparkle(c, new Vector2(w * 0.64f, 0.88f), 0.05f, Pal.Hex("fff1b0"));
            });
            Draw(atlas, FreeSpins, c => K.BannerText(c, font, "FREE SPINS", Inset(c, 0.1f), Pal.Hex("fffbe0"), Pal.Hex("ff3a9a"), plum, glowPink, 0.07f, 0.06f, null, true));
            Draw(atlas, BigWin, c => K.BannerText(c, font, "BIG WIN", Inset(c, 0.08f), goldTop, goldBottom, Pal.Hex("7a1a00"), glowJuice, 0.07f, 0.05f, null, true));
            Draw(atlas, MegaWin, c => K.BannerText(c, font, "MEGA WIN", Inset(c, 0.08f), Pal.Hex("ffe6ff"), Pal.Hex("ff2fb0"), Pal.Hex("4a0a5a"), glowPink, 0.07f, 0.05f, null, true));
            Draw(atlas, EpicWin, c => K.BannerText(c, font, "EPIC WIN", Inset(c, 0.08f), Pal.Hex("f0ffd8"), Pal.Hex("3ad10a"), Pal.Hex("0a3a10"), glowLime, 0.07f, 0.05f, null, true));
            Draw(atlas, TotalWin, c => K.BannerText(c, font, "TOTAL WIN", Inset(c, 0.12f), goldTop, goldBottom, Pal.Hex("7a1a00"), glowJuice, 0.07f, 0.05f, null, true));
            Draw(atlas, Special, c => K.BannerText(c, font, "SPECIAL SYMBOL", Inset(c, 0.1f), Pal.Hex("e6fffb"), Pal.Hex("16c4d8"), Pal.Hex("063a4a"), glowAqua, 0.07f, 0.05f, null, true));
            Draw(atlas, Expanding, c => K.BannerText(c, font, "EXPANDING!", Inset(c, 0.1f), Pal.Hex("fffbe0"), Pal.Hex("ffb01a"), Pal.Hex("6a2a00"), glowJuice, 0.07f, 0.04f, null, true));
            Draw(atlas, PlusSpins, c => K.BannerText(c, font, $"+{M.FreeSpinsRetrigger} SPINS", Inset(c, 0.1f), Color.white, Pal.Hex("ff3a9a"), plum, glowPink, 0.07f, 0.05f, null, true));
            Draw(atlas, Spins, c => K.BannerText(c, font, "SPINS", Inset(c, 0.16f), Color.white, Pal.Hex("ffd84a"), plum, none, 0.07f, 0.06f));
            Draw(atlas, Win, c => K.BannerText(c, font, "WIN", Inset(c, 0.16f), Color.white, Pal.Hex("ffd84a"), plum, none, 0.07f, 0.08f));
            Draw(atlas, Bonus, c => K.BannerText(c, font, "DIAMOND BONUS", Inset(c, 0.1f), Color.white, Pal.Hex("38c8ff"), Pal.Hex("0a2a6a"), glowAqua, 0.07f, 0.04f, null, true));
            return atlas;
        }

        // Draws src (a square canvas) into dst, centered at center, size wide, rotated deg (dst units: 1 = dst height).
        static void Stamp(PaintCanvas dst, PaintCanvas src, Vector2 center, float size, float deg)
        {
            float half = size * 0.75f;
            float cs = Mathf.Cos(-deg * Mathf.Deg2Rad), sn = Mathf.Sin(-deg * Mathf.Deg2Rad);
            int x0 = Mathf.Max(0, Mathf.FloorToInt((center.x - half) * dst.H)), x1 = Mathf.Min(dst.W - 1, Mathf.CeilToInt((center.x + half) * dst.H));
            int y0 = Mathf.Max(0, Mathf.FloorToInt((center.y - half) * dst.H)), y1 = Mathf.Min(dst.H - 1, Mathf.CeilToInt((center.y + half) * dst.H));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 p = new Vector2((x + 0.5f) / dst.H, (y + 0.5f) / dst.H) - center;
                    Vector2 q = new Vector2(p.x * cs - p.y * sn, p.x * sn + p.y * cs) / size + new Vector2(0.5f, 0.5f);
                    if (q.x < 0f || q.x >= 1f || q.y < 0f || q.y >= 1f) continue;
                    dst.Blend(y * dst.W + x, Sample(src, q));
                }
        }

        // Bilinear, premultiplied.
        static Color Sample(PaintCanvas c, Vector2 uv)
        {
            float fx = uv.x * c.W - 0.5f, fy = uv.y * c.H - 0.5f;
            int ix = Mathf.FloorToInt(fx), iy = Mathf.FloorToInt(fy);
            float tx = fx - ix, ty = fy - iy;
            float r = 0f, g = 0f, b = 0f, a = 0f;
            for (int k = 0; k < 4; k++)
            {
                int x = ix + (k & 1), y = iy + (k >> 1);
                if (x < 0 || y < 0 || x >= c.W || y >= c.H) continue;
                float wgt = ((k & 1) == 1 ? tx : 1f - tx) * ((k >> 1) == 1 ? ty : 1f - ty);
                Color s = c.Px[y * c.W + x];
                r += s.r * s.a * wgt; g += s.g * s.a * wgt; b += s.b * s.a * wgt; a += s.a * wgt;
            }
            return a > 1e-5f ? new Color(r / a, g / a, b / a, a) : new Color(0, 0, 0, 0);
        }

        static Rect Inset(PaintCanvas c, float m)
        {
            float w = (float)c.W / c.H;
            return new Rect(m, m, w - 2 * m, 1f - 2 * m);
        }

        static void Draw(Color[] atlas, UiRegion region, System.Action<PaintCanvas> paint)
        {
            var c = new PaintCanvas(region.px.width, region.px.height);
            paint(c);
            c.FadeEdges(region.px.height >= 256 ? 24 : 16);
            c.CopyTo(atlas, 1024, region.px.x, region.px.y);
        }
    }
}
