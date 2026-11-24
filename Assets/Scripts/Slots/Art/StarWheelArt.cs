using UnityEngine;
using K = VideoSlots.SlotArtKit;

namespace VideoSlots
{
    // Symbol, UI and wheel art for STAR WHEEL (cosmic neon).
    public static class StarWheelArt
    {
        public static readonly UiRegion Logo = new UiRegion("logo", 0, 768, 1024, 256);
        public static readonly UiRegion FreeSpins = new UiRegion("freespins", 0, 640, 1024, 128);
        public static readonly UiRegion BigWin = new UiRegion("bigwin", 0, 512, 512, 128);
        public static readonly UiRegion MegaWin = new UiRegion("megawin", 512, 512, 512, 128);
        public static readonly UiRegion EpicWin = new UiRegion("epicwin", 0, 384, 512, 128);
        public static readonly UiRegion TotalWin = new UiRegion("totalwin", 512, 384, 512, 128);
        public static readonly UiRegion WheelBonus = new UiRegion("wheelbonus", 0, 256, 1024, 128);
        public static readonly UiRegion PullToSpin = new UiRegion("pull", 0, 128, 1024, 128);
        public static readonly UiRegion Jackpot = new UiRegion("jackpot", 0, 0, 512, 128);
        public static readonly UiRegion Pointer = new UiRegion("pointer", 512, 0, 128, 128);
        public static readonly UiRegion Hub = new UiRegion("hub", 640, 0, 128, 128);
        public static readonly UiRegion SpinsLeft = new UiRegion("spinsleft", 768, 0, 256, 128);

        static readonly Color Neon1 = Pal.Hex("ff3df2"), Neon2 = Pal.Hex("3df5ff"), Deep = Pal.Hex("12061f"), Gold = Pal.Hex("ffd34a");

        // Wedge colors in wheel order (credits wedges cycle; special wedges have their own).
        public static Color WedgeColor(int i)
        {
            var w = StarWheelMath.Wheel[i];
            if (w.kind == StarWheelMath.WedgeKind.Jackpot) return Pal.Hex("111018");
            if (w.kind == StarWheelMath.WedgeKind.FreeSpins) return Pal.Hex("f2b705");
            Color[] cycle = { Pal.Hex("e0247a"), Pal.Hex("7b2ff7"), Pal.Hex("1f8bff"), Pal.Hex("10b8a6"), Pal.Hex("ff5a1f"), Pal.Hex("c21fff") };
            return cycle[i % cycle.Length];
        }

        public static Color[] BuildSymbols(SdfFont font) => K.AssembleSymbols(i => DrawSymbol(i, font), StarWheelMath.ArtCount);

        public static PaintCanvas DrawSymbol(int s, SdfFont font)
        {
            var c = new PaintCanvas(SymbolAtlas.Cell, SymbolAtlas.Cell);
            switch (s)
            {
                case StarWheelMath.Scatter: WheelScatter(c, font); break;
                case StarWheelMath.Wild: Wild(c, font, null); break;
                case StarWheelMath.WildX2Art: Wild(c, font, "x2"); break;
                case StarWheelMath.WildX3Art: Wild(c, font, "x3"); break;
                case StarWheelMath.Planet: Planet(c); break;
                case StarWheelMath.Rocket: Rocket(c); break;
                case StarWheelMath.Ufo: Ufo(c); break;
                case StarWheelMath.Moon: Moon(c); break;
                case StarWheelMath.A: K.RoyalNeon(c, font, "A", Pal.Hex("ff3d8b")); break;
                case StarWheelMath.K: K.RoyalNeon(c, font, "K", Pal.Hex("3dd8ff")); break;
                case StarWheelMath.Q: K.RoyalNeon(c, font, "Q", Pal.Hex("6dff5a")); break;
                case StarWheelMath.J: K.RoyalNeon(c, font, "J", Pal.Hex("ffa63d")); break;
                case StarWheelMath.Ten: K.RoyalNeon(c, font, "10", Pal.Hex("b56dff")); break;
                default: return null;
            }
            return c;
        }

        static void Stars(PaintCanvas c, int seed, int count, float maxSize)
        {
            var rng = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                Vector2 at = new Vector2(0.08f + (float)rng.NextDouble() * 0.84f, 0.08f + (float)rng.NextDouble() * 0.84f);
                float s = maxSize * (0.4f + 0.6f * (float)rng.NextDouble());
                K.Sparkle(c, at, s, i % 2 == 0 ? Color.white : Pal.Hex("bfefff"));
            }
        }

        static void WheelScatter(PaintCanvas c, SdfFont font)
        {
            Vector2 m = new Vector2(0.5f, 0.55f);
            const float R = 0.34f;
            var disc = c.F(p => Sd.Circle(p, m, R));
            c.Glow(disc, 0.12f, Pal.A(Gold, 0.6f), 1.6f);
            K.DropShadow(c, disc, 0.03f);
            c.Fill(disc, K.Ink, 0.042f);
            c.Fill(disc, (p, d) => Pal.Gold(p.y, m.y - R, m.y + R), 0.03f);
            c.Bevel(disc.Grow(0.03f), 0.025f, 0.7f, K.Light);
            int n = 8;
            c.Fill(disc, (p, d) =>
            {
                float a = Mathf.Repeat(Mathf.Atan2(p.y - m.y, p.x - m.x) * Mathf.Rad2Deg + 90f, 360f);
                int seg = Mathf.FloorToInt(a / (360f / n));
                Color[] cols = { Pal.Hex("e0247a"), Pal.Hex("f2b705"), Pal.Hex("1f8bff"), Pal.Hex("10b8a6"), Pal.Hex("ff5a1f"), Pal.Hex("7b2ff7"), Pal.Hex("6dff5a"), Pal.Hex("ff3df2") };
                Color col = cols[seg];
                float r = (p - m).magnitude / R;
                return Color.Lerp(Pal.Mul(col, 0.6f), Color.Lerp(col, Color.white, 0.2f), r);
            });
            var spokes = c.F(p =>
            {
                Vector2 v = p - m;
                float a = Mathf.Atan2(v.y, v.x);
                float step = Mathf.PI * 2f / n;
                float nearest = Mathf.Round(a / step) * step;
                Vector2 dir = new Vector2(Mathf.Cos(nearest), Mathf.Sin(nearest));
                return Mathf.Max(Sd.Segment(p, m, m + dir * R, 0.006f), (p - m).magnitude - R);
            });
            c.Fill(spokes, Pal.Hex("fff1b0"));
            for (int i = 0; i < n; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / n;
                Vector2 b = m + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (R + 0.015f);
                var bulb = c.F(p => Sd.Circle(p, b, 0.016f));
                c.Glow(bulb, 0.03f, Pal.A(Color.white, 0.7f), 2f);
                c.Fill(bulb, Color.white);
            }
            var hub = c.F(p => Sd.Polygon(p, Sd.StarPoints(m, 0.1f, 0.045f, 5, 0f)));
            c.Fill(hub, K.Ink, 0.012f);
            c.Fill(hub, (p, d) => Pal.Gold(p.y, m.y - 0.1f, m.y + 0.1f));
            c.Bevel(hub, 0.03f, 0.7f, K.Light);
            var pointer = c.F(p => Sd.Polygon(p, new[] { m + new Vector2(-0.045f, R + 0.07f), m + new Vector2(0.045f, R + 0.07f), m + new Vector2(0f, R - 0.03f) }));
            c.Fill(pointer, K.Ink, 0.01f);
            c.Fill(pointer, Pal.Hex("ff2a4a"));
            // BONUS ribbon.
            var ribbon = c.F(p => Sd.Box(p, new Vector2(0.5f, 0.15f), new Vector2(0.4f, 0.085f), 0.03f));
            K.DropShadow(c, ribbon);
            c.Fill(ribbon, K.Ink, 0.012f);
            c.Fill(ribbon, (p, d) => Pal.Ramp(Mathf.InverseLerp(0.07f, 0.23f, p.y), Pal.Hex("5c0b8a"), Neon1, Pal.Hex("ffb3fa")));
            c.Bevel(ribbon, 0.025f, 0.5f, K.Light);
            var t = font.Text(c.W, c.H, "BONUS", new Rect(0.14f, 0.085f, 0.72f, 0.13f), 0.06f, 0.85f).Grow(0.006f);
            c.Fill(t, K.Ink, 0.014f);
            c.Fill(t, Color.white);
        }

        static void Wild(PaintCanvas c, SdfFont font, string mult)
        {
            Vector2 m = new Vector2(0.5f, 0.5f);
            var burst = c.F(p => Sd.Polygon(p, Sd.StarPoints(m, 0.47f, 0.3f, 12, 7f)));
            c.Glow(burst, 0.08f, Pal.A(Neon1, 0.6f), 1.6f);
            c.Fill(burst, Deep, 0.012f);
            c.Fill(burst, (p, d) => Pal.Ramp(Mathf.Clamp01((p - m).magnitude / 0.47f), Pal.Hex("ffd6fb"), Neon1, Pal.Hex("7b2ff7"), Pal.Hex("2a0a5e")));
            var swirl = c.F(p =>
            {
                Vector2 v = p - m;
                float th = Mathf.Atan2(v.y, v.x);
                return Mathf.Abs(Mathf.Sin(th * 2f + v.magnitude * 22f)) * 0.04f - 0.012f;
            });
            c.Inside(burst, swirl, (p, d) => new Color(1f, 1f, 1f, 0.18f));
            var plate = c.F(p => Sd.Box(p, new Vector2(0.5f, mult != null ? 0.56f : 0.5f), new Vector2(0.42f, 0.13f), 0.06f));
            c.Fill(plate, Deep, 0.016f);
            c.Fill(plate, (p, d) => Pal.Ramp(Mathf.Clamp01(-d / 0.08f), Neon2, Pal.Hex("1a0b3d")), 0.01f);
            float ty = mult != null ? 0.48f : 0.42f;
            var t = font.Text(c.W, c.H, "WILD", new Rect(0.14f, ty, 0.72f, 0.16f), 0.04f, 0.88f).Grow(0.01f);
            c.Glow(t, 0.06f, Pal.A(Neon2, 0.8f), 1.5f);
            c.Fill(t, Deep, 0.016f);
            c.Fill(t, (p, d) => Color.Lerp(Neon2, Color.white, Mathf.Clamp01(-d / 0.02f)));
            if (mult != null)
            {
                Vector2 bc = new Vector2(0.5f, 0.24f);
                var badge = c.F(p => Sd.Circle(p, bc, 0.14f));
                K.DropShadow(c, badge);
                c.Glow(badge, 0.06f, Pal.A(Gold, 0.7f), 1.5f);
                K.GoldBody(c, badge, bc.y - 0.14f, bc.y + 0.14f);
                var inner = c.F(p => Sd.Circle(p, bc, 0.105f));
                c.Fill(inner, (p, d) => Pal.Ramp(Mathf.InverseLerp(bc.y - 0.1f, bc.y + 0.1f, p.y), Pal.Hex("8a0a2a"), Pal.Hex("ff2a5a")));
                var mt = font.Text(c.W, c.H, mult.ToUpper(), new Rect(0.39f, 0.17f, 0.22f, 0.14f), 0.0f, 0.85f).Grow(0.008f);
                c.Fill(mt, K.Ink, 0.012f);
                c.Fill(mt, Color.white);
            }
            K.Sparkle(c, new Vector2(0.84f, 0.8f), 0.08f, Color.white);
        }

        static void Planet(PaintCanvas c)
        {
            Vector2 m = new Vector2(0.5f, 0.5f);
            const float tilt = -18f;
            Sdf ring = p =>
            {
                Vector2 q = Sd.Rotate(p, m, -tilt);
                return Mathf.Abs(Sd.Ellipse(q, m, new Vector2(0.45f, 0.12f))) - 0.028f;
            };
            Vector2 ringDir = new Vector2(Mathf.Cos(tilt * Mathf.Deg2Rad), Mathf.Sin(tilt * Mathf.Deg2Rad));
            Sdf back = p => Mathf.Max(ring(p), -Vector2.Dot(Sd.Rotate(p, m, -tilt) - m, Vector2.up));
            Sdf front = p => Mathf.Max(ring(p), Vector2.Dot(Sd.Rotate(p, m, -tilt) - m, Vector2.up));
            Color ringCol = Pal.Hex("ffd9a0");
            var fb = c.F(back);
            c.Fill(fb, K.Ink, 0.01f);
            c.Fill(fb, (p, d) => Color.Lerp(Pal.Mul(ringCol, 0.6f), ringCol, Mathf.Abs(Mathf.Sin(Sd.Rotate(p, m, -tilt).x * 40f))));

            var sphere = c.F(p => Sd.Circle(p, m, 0.27f));
            c.Glow(sphere, 0.1f, Pal.A(Pal.Hex("ff7a3d"), 0.5f), 1.6f);
            c.Fill(sphere, K.Ink, 0.014f);
            Vector2 hl = m + new Vector2(-0.09f, 0.1f);
            c.Fill(sphere, (p, d) =>
            {
                float band = Mathf.Sin((Sd.Rotate(p, m, -tilt).y - m.y) * 38f) * 0.5f + 0.5f;
                Color b = Color.Lerp(Pal.Hex("ff8a3d"), Pal.Hex("ffd27a"), band);
                b = Color.Lerp(b, Pal.Hex("d94a8a"), Mathf.Clamp01((Sd.Rotate(p, m, -tilt).y - m.y + 0.05f) * 3f) * 0.4f);
                float shade = Mathf.Clamp01((p - hl).magnitude / 0.42f);
                return Color.Lerp(Color.Lerp(b, Color.white, 0.25f), Pal.Mul(b, 0.35f), shade);
            });
            K.Gloss(c, sphere, p => Sd.Ellipse(Sd.Rotate(p, hl, 35f), hl, new Vector2(0.08f, 0.045f)), 0.6f);
            var ff = c.F(front);
            c.Fill(ff, K.Ink, 0.01f);
            c.Fill(ff, (p, d) => Color.Lerp(Pal.Mul(ringCol, 0.7f), Color.Lerp(ringCol, Color.white, 0.4f), Mathf.Abs(Mathf.Sin(Sd.Rotate(p, m, -tilt).x * 40f))));
            K.Sparkle(c, new Vector2(0.82f, 0.82f), 0.07f, Color.white);
            K.Sparkle(c, new Vector2(0.16f, 0.2f), 0.05f, Pal.Hex("bfefff"));
        }

        static void Rocket(PaintCanvas c)
        {
            Vector2 m = new Vector2(0.5f, 0.5f);
            const float ang = -35f;
            Vector2 R(Vector2 p) => Sd.Rotate(p, m, -ang); // into rocket space (rocket points up)
            var flame = c.F(p =>
            {
                Vector2 q = R(p) - new Vector2(0.5f, 0.17f);
                return new Vector2(q.x * (q.y < 0 ? 0.9f : 1.4f), q.y * 0.55f).magnitude - 0.075f;
            });
            c.Glow(flame, 0.1f, Pal.A(Pal.Hex("ff8a1f"), 0.7f), 1.6f);
            c.Fill(flame, (p, d) => Pal.Ramp(Mathf.Clamp01(-d / 0.06f), Pal.Hex("ff3a1f"), Pal.Hex("ffb31f"), Pal.Hex("fff8d0")));

            var fins = c.F(p =>
            {
                Vector2 q = R(p);
                float l = Sd.Polygon(q, new[] { new Vector2(0.4f, 0.42f), new Vector2(0.4f, 0.26f), new Vector2(0.27f, 0.2f), new Vector2(0.3f, 0.34f) });
                float r = Sd.Polygon(q, new[] { new Vector2(0.6f, 0.26f), new Vector2(0.6f, 0.42f), new Vector2(0.7f, 0.34f), new Vector2(0.73f, 0.2f) });
                return Mathf.Min(l, r);
            });
            K.DropShadow(c, fins);
            c.Fill(fins, K.Ink, 0.014f);
            c.Fill(fins, (p, d) => Pal.Ramp(Mathf.InverseLerp(0.15f, 0.45f, R(p).y), Pal.Hex("8a0a1f"), Pal.Hex("ff2a4a")));
            c.Bevel(fins, 0.025f, 0.5f, K.Light);

            var body = c.F(p =>
            {
                Vector2 q = R(p);
                float hull = Sd.Ellipse(q, new Vector2(0.5f, 0.5f), new Vector2(0.11f, 0.32f));
                return Mathf.Max(hull, 0.24f - q.y);
            });
            K.DropShadow(c, body);
            c.Fill(body, K.Ink, 0.016f);
            c.Fill(body, (p, d) => { Vector2 q = R(p); return Pal.Silver(q.x * 1.3f, 0.38f, 0.82f); });
            c.Bevel(body, 0.04f, 0.5f, K.Light);
            var nose = c.F(p => Mathf.Max(Sd.Ellipse(R(p), new Vector2(0.5f, 0.5f), new Vector2(0.11f, 0.32f)), 0.68f - R(p).y));
            c.Fill(nose, (p, d) => Pal.Ramp(Mathf.InverseLerp(0.68f, 0.82f, R(p).y), Pal.Hex("ff2a4a"), Pal.Hex("ff8aa0")));
            c.Stroke(c.F(p => 0.68f - R(p).y), 0.004f, K.Ink);
            var window = c.F(p => Sd.Circle(R(p), new Vector2(0.5f, 0.53f), 0.055f));
            c.Fill(window, K.Ink, 0.012f);
            c.Fill(window, (p, d) => Pal.Ramp(Mathf.InverseLerp(0.47f, 0.59f, R(p).y), Pal.Hex("0a3a6b"), Neon2, Color.white), 0f);
            c.Stroke(window, 0.007f, Pal.Hex("c9d2e0"), 0.008f);
            K.Sparkle(c, new Vector2(0.2f, 0.78f), 0.07f, Color.white);
            K.Sparkle(c, new Vector2(0.82f, 0.62f), 0.05f, Pal.Hex("bfefff"));
        }

        static void Ufo(PaintCanvas c)
        {
            var beam = c.F(p => Sd.Polygon(p, new[] { new Vector2(0.4f, 0.42f), new Vector2(0.6f, 0.42f), new Vector2(0.78f, 0.06f), new Vector2(0.22f, 0.06f) }));
            c.Fill(beam, (p, d) => new Color(0.6f, 1f, 0.9f, 0.4f * Mathf.InverseLerp(0.06f, 0.42f, p.y)));
            var dome = c.F(p => Mathf.Max(Sd.Ellipse(p, new Vector2(0.5f, 0.52f), new Vector2(0.17f, 0.2f)), 0.52f - p.y));
            c.Glow(dome, 0.08f, Pal.A(Pal.Hex("6dff5a"), 0.35f), 1.6f);
            c.Fill(dome, K.Ink, 0.014f);
            c.Fill(dome, (p, d) => Pal.Ramp(Mathf.InverseLerp(0.52f, 0.72f, p.y), Pal.Hex("1a8a6b"), Pal.Hex("7dffd9"), Color.white));
            var alien = c.F(p => Mathf.Min(Sd.Ellipse(p, new Vector2(0.5f, 0.58f), new Vector2(0.06f, 0.07f)), Mathf.Min(Sd.Circle(p, new Vector2(0.45f, 0.67f), 0.012f), Sd.Circle(p, new Vector2(0.55f, 0.67f), 0.012f))));
            c.Inside(dome, alien, (p, d) => Pal.Hex("2fbf3a"));
            c.Fill(c.F(p => Mathf.Min(Sd.Ellipse(p, new Vector2(0.476f, 0.6f), new Vector2(0.018f, 0.025f)), Sd.Ellipse(p, new Vector2(0.524f, 0.6f), new Vector2(0.018f, 0.025f)))), K.Ink);
            K.Gloss(c, dome, p => Sd.Ellipse(p, new Vector2(0.44f, 0.64f), new Vector2(0.05f, 0.03f)), 0.7f);
            var saucer = c.F(p => Sd.Ellipse(p, new Vector2(0.5f, 0.46f), new Vector2(0.4f, 0.11f)));
            K.DropShadow(c, saucer);
            c.Fill(saucer, K.Ink, 0.016f);
            c.Fill(saucer, (p, d) => Pal.Silver(p.y, 0.35f, 0.57f));
            c.Bevel(saucer, 0.04f, 0.5f, K.Light);
            var rim = c.F(p => Mathf.Abs(Sd.Ellipse(p, new Vector2(0.5f, 0.44f), new Vector2(0.4f, 0.05f))) - 0.008f);
            c.Inside(saucer, rim, (p, d) => Pal.Hex("5f6878"));
            Color[] lights = { Pal.Hex("ff3d8b"), Pal.Hex("ffd34a"), Neon2, Pal.Hex("6dff5a"), Pal.Hex("ff3d8b") };
            for (int i = 0; i < 5; i++)
            {
                Vector2 at = new Vector2(0.22f + i * 0.14f, 0.43f - Mathf.Abs(i - 2) * 0.012f);
                var l = c.F(p => Sd.Circle(p, at, 0.022f));
                c.Glow(l, 0.04f, Pal.A(lights[i], 0.8f), 2f);
                c.Fill(l, K.Ink, 0.006f);
                c.Fill(l, Color.Lerp(lights[i], Color.white, 0.3f));
            }
            K.Sparkle(c, new Vector2(0.84f, 0.78f), 0.07f, Color.white);
        }

        static void Moon(PaintCanvas c)
        {
            Vector2 m = new Vector2(0.47f, 0.5f);
            var moon = c.F(p => Mathf.Max(Sd.Circle(p, m, 0.33f), -Sd.Circle(p, m + new Vector2(0.15f, 0.08f), 0.29f)));
            c.Glow(moon, 0.12f, Pal.A(Pal.Hex("ffe27a"), 0.55f), 1.6f);
            K.DropShadow(c, moon);
            c.Fill(moon, K.Ink, 0.016f);
            c.Fill(moon, (p, d) => Pal.Ramp(Mathf.Clamp01(-d / 0.12f), Pal.Hex("d99a1a"), Pal.Hex("ffd34a"), Pal.Hex("fff6c9")));
            c.Bevel(moon, 0.04f, 0.5f, K.Light);
            Vector2[] craters = { new Vector2(0.3f, 0.6f), new Vector2(0.27f, 0.42f), new Vector2(0.38f, 0.28f) };
            float[] rad = { 0.035f, 0.025f, 0.03f };
            for (int i = 0; i < craters.Length; i++)
            {
                Vector2 cc = craters[i]; float rr = rad[i];
                var cr = c.F(p => Sd.Circle(p, cc, rr));
                c.Inside(moon, cr, (p, d) => Pal.A(Pal.Hex("b07812"), 0.7f));
                c.Inside(moon, c.F(p => Sd.Circle(p, cc + new Vector2(0.006f, -0.006f), rr * 0.75f)), (p, d) => Pal.A(Pal.Hex("e8b23a"), 0.8f));
            }
            K.Sparkle(c, new Vector2(0.74f, 0.66f), 0.1f, Color.white);
            K.Sparkle(c, new Vector2(0.8f, 0.36f), 0.06f, Pal.Hex("fff1b0"));
            K.Sparkle(c, new Vector2(0.64f, 0.84f), 0.05f, Pal.Hex("bfefff"));
        }

        // ------------------------------------------------------------ wheel
        // The face of the bonus wheel (labels are live text on top of it).
        public static Color[] BuildWheelFace(int size = 1024)
        {
            var c = new PaintCanvas(size, size);
            Vector2 m = new Vector2(0.5f, 0.5f);
            int n = StarWheelMath.Wheel.Length;
            float step = 360f / n;
            const float R = 0.485f, inner = 0.44f;
            var disc = c.F(p => Sd.Circle(p, m, R));
            c.Fill(disc, K.Ink, 0.004f);
            c.Fill(disc, (p, d) => Pal.Gold(Sd.Rotate(p, m, 30f).y, 0.02f, 0.98f));
            c.Bevel(disc, 0.03f, 0.7f, K.Light);
            var face = c.F(p => Sd.Circle(p, m, inner));
            c.Fill(face, K.Ink, 0.006f);
            c.Fill(face, (p, d) =>
            {
                // Wedge 0 is centered at the top; indices run clockwise.
                float a = Mathf.Repeat(90f - Mathf.Atan2(p.y - m.y, p.x - m.x) * Mathf.Rad2Deg + step * 0.5f, 360f);
                int i = Mathf.FloorToInt(a / step) % n;
                Color col = WedgeColor(i);
                float r = (p - m).magnitude / inner;
                float within = Mathf.Repeat(a, step) / step; // 0..1 across the wedge
                Color lit = Color.Lerp(Pal.Mul(col, 0.55f), Color.Lerp(col, Color.white, 0.25f), Mathf.SmoothStep(0.1f, 1f, r));
                lit = Color.Lerp(lit, Pal.Mul(lit, 0.75f), Mathf.Abs(within - 0.5f) * 0.6f);
                if (StarWheelMath.Wheel[i].kind == StarWheelMath.WedgeKind.Jackpot)
                {
                    float sp = Mathf.Abs(Mathf.Sin(r * 60f)) * Mathf.Abs(Mathf.Sin(within * 30f));
                    lit = Color.Lerp(lit, Pal.Hex("ffd34a"), sp > 0.9f ? 0.6f : 0f);
                }
                return lit;
            });
            // Gold separators.
            var seps = c.F(p =>
            {
                Vector2 v = p - m;
                float a = 90f - Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg + step * 0.5f;
                float nearest = Mathf.Round(a / step) * step - step * 0.5f;
                float rad = (90f - nearest) * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                return Mathf.Max(Sd.Segment(p, m, m + dir * inner, 0.004f), (p - m).magnitude - inner);
            });
            c.Fill(seps, K.Ink, 0.002f);
            c.Fill(seps, (p, d) => Pal.Gold((p - m).magnitude, 0.1f, inner));
            // Pegs on the rim between wedges.
            for (int i = 0; i < n; i++)
            {
                float rad = (90f - (i * step - step * 0.5f)) * Mathf.Deg2Rad;
                Vector2 at = m + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * (inner + 0.022f);
                var peg = c.F(p => Sd.Circle(p, at, 0.014f));
                c.Fill(peg, K.Ink, 0.004f);
                c.Fill(peg, (p, d) => Pal.Silver(p.y, at.y - 0.015f, at.y + 0.015f));
            }
            // Hub.
            var hub = c.F(p => Sd.Circle(p, m, 0.1f));
            K.DropShadow(c, hub);
            K.GoldBody(c, hub, 0.4f, 0.6f, 0.005f, 0.03f);
            var hubInner = c.F(p => Sd.Circle(p, m, 0.075f));
            c.Fill(hubInner, (p, d) => Pal.Ramp(Mathf.InverseLerp(0.43f, 0.57f, p.y), Pal.Hex("2a0a5e"), Pal.Hex("7b2ff7")));
            var star = c.F(p => Sd.Polygon(p, Sd.StarPoints(m, 0.062f, 0.026f, 5, 0f)));
            K.GoldBody(c, star, 0.44f, 0.56f, 0.004f, 0.02f);
            return c.Px;
        }

        // ------------------------------------------------------------------ UI
        public static Color[] BuildUi(SdfFont font)
        {
            var atlas = new Color[1024 * 1024];
            for (int i = 0; i < atlas.Length; i++) atlas[i] = new Color(0, 0, 0, 0);
            Color rim = Pal.Hex("2a0a5e"), glowPink = Pal.A(Neon1, 0.55f), glowCyan = Pal.A(Neon2, 0.5f);

            Draw(atlas, Logo, c =>
            {
                float w = (float)c.W / c.H;
                var rng = new System.Random(7);
                for (int i = 0; i < 26; i++)
                {
                    Vector2 at = new Vector2((float)rng.NextDouble() * w, 0.1f + (float)rng.NextDouble() * 0.8f);
                    K.Sparkle(c, at, 0.03f + 0.04f * (float)rng.NextDouble(), i % 3 == 0 ? Pal.Hex("ffd6fb") : Color.white);
                }
                var orbit = c.F(p => Mathf.Abs(Sd.Ellipse(p, new Vector2(w * 0.5f, 0.48f), new Vector2(w * 0.46f, 0.36f))) - 0.012f);
                c.Glow(orbit, 0.06f, Pal.A(Neon2, 0.6f), 1.5f);
                c.Fill(orbit, Pal.A(Color.Lerp(Neon2, Color.white, 0.5f), 0.9f));
                K.BannerText(c, font, "STAR", new Rect(w * 0.08f, 0.14f, w * 0.38f, 0.72f), Pal.Hex("fff6c9"), Pal.Hex("ffb31f"), rim, glowPink, 0.06f, 0.05f, null, true);
                K.BannerText(c, font, "WHEEL", new Rect(w * 0.47f, 0.14f, w * 0.47f, 0.72f), Pal.Hex("ffe0ff"), Neon1, rim, glowCyan, 0.06f, 0.05f, null, true);
                K.Sparkle(c, new Vector2(w * 0.44f, 0.8f), 0.12f, Color.white);
            });
            Draw(atlas, FreeSpins, c => K.BannerText(c, font, "FREE SPINS", Inset(c, 0.1f), Pal.Hex("e0ffff"), Neon2, rim, glowCyan, 0.07f, 0.06f, null, true));
            Draw(atlas, BigWin, c => K.BannerText(c, font, "BIG WIN", Inset(c, 0.08f), Pal.Hex("fff6c9"), Pal.Hex("ffb31f"), rim, Pal.A(Gold, 0.5f), 0.07f, 0.05f, null, true));
            Draw(atlas, MegaWin, c => K.BannerText(c, font, "MEGA WIN", Inset(c, 0.08f), Pal.Hex("ffe0ff"), Neon1, rim, glowPink, 0.07f, 0.05f, null, true));
            Draw(atlas, EpicWin, c => K.BannerText(c, font, "EPIC WIN", Inset(c, 0.08f), Pal.Hex("e0ffff"), Neon2, rim, glowCyan, 0.07f, 0.05f, null, true));
            Draw(atlas, TotalWin, c => K.BannerText(c, font, "TOTAL WIN", Inset(c, 0.12f), Pal.Hex("fff6c9"), Pal.Hex("ffb31f"), rim, Pal.A(Gold, 0.4f), 0.07f, 0.05f, null, true));
            Draw(atlas, WheelBonus, c => K.BannerText(c, font, "WHEEL BONUS", Inset(c, 0.1f), Pal.Hex("fff6c9"), Pal.Hex("ffb31f"), rim, glowPink, 0.07f, 0.06f, null, true));
            Draw(atlas, PullToSpin, c => K.BannerText(c, font, "PULL TO SPIN!", Inset(c, 0.14f), Color.white, Neon2, rim, glowCyan, 0.07f, 0.05f));
            Draw(atlas, Jackpot, c => K.BannerText(c, font, "JACKPOT!", Inset(c, 0.08f), Pal.Hex("fff6c9"), Pal.Hex("ffb31f"), rim, Pal.A(Gold, 0.6f), 0.07f, 0.05f, null, true));
            Draw(atlas, SpinsLeft, c => K.BannerText(c, font, "SPINS", Inset(c, 0.18f), Color.white, Neon2, rim, new Color(0, 0, 0, 0), 0.07f, 0.06f));
            Draw(atlas, Pointer, c =>
            {
                var ptr = c.F(p => Sd.Polygon(p, new[] { new Vector2(0.24f, 0.86f), new Vector2(0.76f, 0.86f), new Vector2(0.5f, 0.08f) }));
                K.DropShadow(c, ptr);
                c.Fill(ptr, K.Ink, 0.03f);
                c.Fill(ptr, (p, d) => Pal.Gold(p.x, 0.2f, 0.8f), 0.02f);
                c.Fill(ptr, (p, d) => Pal.Ramp(Mathf.InverseLerp(0.1f, 0.86f, p.y), Pal.Hex("7a0418"), Pal.Hex("ff2a4a"), Pal.Hex("ff9aae")));
                c.Bevel(ptr, 0.06f, 0.6f, K.Light);
                var gem = c.F(p => Sd.Circle(p, new Vector2(0.5f, 0.7f), 0.08f));
                K.GoldBody(c, gem, 0.62f, 0.78f, 0.01f, 0.04f);
            });
            Draw(atlas, Hub, c =>
            {
                var star = c.F(p => Sd.Polygon(p, Sd.StarPoints(new Vector2(0.5f, 0.5f), 0.46f, 0.2f, 5, 0f)));
                c.Glow(star, 0.06f, Pal.A(Gold, 0.6f), 1.5f);
                K.GoldBody(c, star, 0.05f, 0.95f, 0.02f, 0.08f);
            });
            return atlas;
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
            c.FadeEdges(region.px.height >= 256 ? 24 : region.px.width <= 128 ? 3 : 16);
            c.CopyTo(atlas, 1024, region.px.x, region.px.y);
        }
    }
}
