using UnityEngine;
using K = VideoSlots.SlotArtKit;

namespace VideoSlots
{
    // Symbol and UI art for GOLDEN INFERNO (fire + treasure).
    public static class GoldenInfernoArt
    {
        public static readonly UiRegion Logo = new UiRegion("logo", 0, 768, 1024, 256);
        public static readonly UiRegion HoldSpin = new UiRegion("holdspin", 0, 640, 1024, 128);
        public static readonly UiRegion BigWin = new UiRegion("bigwin", 0, 512, 512, 128);
        public static readonly UiRegion MegaWin = new UiRegion("megawin", 512, 512, 512, 128);
        public static readonly UiRegion EpicWin = new UiRegion("epicwin", 0, 384, 512, 128);
        public static readonly UiRegion TotalWin = new UiRegion("totalwin", 512, 384, 512, 128);
        public static readonly UiRegion Grand = new UiRegion("grand", 0, 256, 512, 128);
        public static readonly UiRegion Major = new UiRegion("major", 512, 256, 512, 128);
        public static readonly UiRegion Minor = new UiRegion("minor", 0, 128, 512, 128);
        public static readonly UiRegion Mini = new UiRegion("mini", 512, 128, 512, 128);
        public static readonly UiRegion Respins = new UiRegion("respins", 0, 0, 512, 128);
        public static readonly UiRegion Jackpot = new UiRegion("jackpot", 512, 0, 512, 128);

        static readonly Color FireRed = Pal.Hex("c4160a"), FireOrange = Pal.Hex("ff6a00"), FireYellow = Pal.Hex("ffd23f"), Ember = Pal.Hex("5c0a04");

        public static readonly Color[] JackpotColors = { Pal.Hex("35c24a"), Pal.Hex("2f8cff"), Pal.Hex("b23cff"), Pal.Hex("ff2a2a") }; // mini minor major grand

        public static Color[] BuildSymbols(SdfFont font) => K.AssembleSymbols(i => DrawSymbol(i, font), InfernoMath.Empty);

        public static PaintCanvas DrawSymbol(int s, SdfFont font)
        {
            var c = new PaintCanvas(SymbolAtlas.Cell, SymbolAtlas.Cell);
            switch (s)
            {
                case InfernoMath.Orb: FireOrb(c, 1f); break;
                case InfernoMath.OrbMini: JackpotOrb(c, font, "MINI", JackpotColors[0]); break;
                case InfernoMath.OrbMinor: JackpotOrb(c, font, "MINOR", JackpotColors[1]); break;
                case InfernoMath.OrbMajor: JackpotOrb(c, font, "MAJOR", JackpotColors[2]); break;
                case InfernoMath.OrbGrand: JackpotOrb(c, font, "GRAND", JackpotColors[3]); break;
                case InfernoMath.Wild: Wild(c, font); break;
                case InfernoMath.Crown: Crown(c); break;
                case InfernoMath.Chest: Chest(c); break;
                case InfernoMath.Ruby: K.Gem(c, new Vector2(0.5f, 0.5f), new Vector2(0.34f, 0.3f), Pal.Hex("e0102f")); break;
                case InfernoMath.Bars: GoldBars(c); break;
                case InfernoMath.A: K.RoyalGold(c, font, "A", Pal.Hex("ffb347"), Pal.Hex("d4140c")); break;
                case InfernoMath.K: K.RoyalGold(c, font, "K", Pal.Hex("7fd8ff"), Pal.Hex("0f4fd6")); break;
                case InfernoMath.Q: K.RoyalGold(c, font, "Q", Pal.Hex("f59cff"), Pal.Hex("8a1fc4")); break;
                case InfernoMath.J: K.RoyalGold(c, font, "J", Pal.Hex("a8ff7a"), Pal.Hex("14963a")); break;
                case InfernoMath.Ten: K.RoyalGold(c, font, "10", Pal.Hex("fff07a"), Pal.Hex("e06a00")); break;
                default: return null;
            }
            return c;
        }

        static Sdf FlameShape(Vector2 center, float radius, float lick, float phase)
        {
            return p =>
            {
                Vector2 v = p - center;
                float r = v.magnitude;
                float th = Mathf.Atan2(v.y, v.x);
                float up = Mathf.Max(0f, v.y / (r + 1e-5f));
                float R = radius * (1f + 0.13f * Mathf.Sin(7f * th + phase) + 0.08f * Mathf.Sin(13f * th + 2.1f + phase)) + lick * up * up * (0.75f + 0.25f * Mathf.Sin(5f * th + phase));
                return (r - R) * 0.8f;
            };
        }

        public static void FireOrb(PaintCanvas c, float size)
        {
            Vector2 c0 = new Vector2(0.5f, 0.46f);
            var outer = c.F(FlameShape(c0, 0.31f * size, 0.13f * size, 0.8f));
            c.Glow(outer, 0.12f, Pal.A(FireOrange, 0.75f), 1.6f);
            c.Fill(outer, (p, d) => Pal.Ramp(Mathf.Clamp01(-d / 0.09f), Pal.A(FireRed, 0.9f), FireOrange, FireYellow));
            var inner = c.F(FlameShape(c0 + new Vector2(0f, 0.02f), 0.26f * size, 0.08f * size, 2.4f));
            c.Fill(inner, (p, d) => Pal.Ramp(Mathf.Clamp01(-d / 0.08f), Pal.A(FireOrange, 0.6f), FireYellow, Pal.Hex("fff4c2")));

            var orb = c.F(p => Sd.Circle(p, c0, 0.215f * size));
            c.Fill(orb, Ember, 0.014f);
            Vector2 hl = c0 + new Vector2(-0.07f, 0.08f) * size;
            c.Fill(orb, (p, d) => Pal.Ramp((p - hl).magnitude / (0.3f * size), Pal.Hex("fffbe0"), Pal.Hex("ffd84a"), Pal.Hex("ff8a12"), Pal.Hex("e0400a"), Pal.Hex("8a1305")));
            // Swirling flame veins inside the orb.
            var vein = c.F(p =>
            {
                Vector2 v = p - c0;
                float th = Mathf.Atan2(v.y, v.x);
                float r = v.magnitude / size;
                return Mathf.Abs(Mathf.Sin(th * 3f + r * 18f)) * 0.03f - 0.006f + Mathf.Max(0f, 0.08f - r) * 0.5f;
            });
            c.Inside(orb, vein, (p, d) => new Color(1f, 0.95f, 0.6f, 0.35f));
            c.Bevel(orb, 0.05f, 0.45f, K.Light);
            K.Gloss(c, orb, p => Sd.Ellipse(Sd.Rotate(p, hl, 30f), hl + new Vector2(0f, 0.02f), new Vector2(0.09f, 0.05f) * size), 0.75f);
            // Embers.
            Vector2[] embers = { new Vector2(0.18f, 0.72f), new Vector2(0.83f, 0.78f), new Vector2(0.86f, 0.3f), new Vector2(0.14f, 0.26f), new Vector2(0.62f, 0.9f) };
            for (int i = 0; i < embers.Length; i++)
            {
                Vector2 e = c0 + (embers[i] - c0) * size;
                var dot = c.F(p => Sd.Circle(p, e, 0.012f + 0.004f * (i % 3)));
                c.Glow(dot, 0.04f, Pal.A(FireOrange, 0.8f), 2f);
                c.Fill(dot, FireYellow);
            }
        }

        static void JackpotOrb(PaintCanvas c, SdfFont font, string label, Color color)
        {
            FireOrb(c, 1f);
            var plate = c.F(p => Sd.Box(p, new Vector2(0.5f, 0.43f), new Vector2(0.43f, 0.12f), 0.06f));
            K.DropShadow(c, plate);
            c.Fill(plate, K.Ink, 0.014f);
            c.Fill(plate, (p, d) => Pal.Gold(p.y, 0.31f, 0.55f), 0.012f);
            c.Fill(plate, (p, d) => Pal.Ramp(Mathf.InverseLerp(0.31f, 0.55f, p.y), Pal.Mul(color, 0.45f), color, Color.Lerp(color, Color.white, 0.45f)));
            c.Bevel(plate, 0.03f, 0.5f, K.Light);
            var t = font.Text(c.W, c.H, label, new Rect(0.11f, 0.33f, 0.78f, 0.2f), 0.02f, 0.8f).Grow(0.006f);
            c.Fill(t, K.Ink, 0.018f);
            c.Fill(t, (p, d) => Pal.Ramp(Mathf.InverseLerp(0.35f, 0.52f, p.y), Pal.Hex("ffe9a8"), Color.white));
        }

        static void Wild(PaintCanvas c, SdfFont font)
        {
            var burst = c.F(FlameShape(new Vector2(0.5f, 0.47f), 0.36f, 0.16f, 0.2f));
            c.Glow(burst, 0.1f, Pal.A(FireOrange, 0.7f), 1.6f);
            c.Fill(burst, (p, d) => Pal.Ramp(Mathf.Clamp01(-d / 0.1f), Pal.A(FireRed, 0.85f), FireOrange, FireYellow));
            var plaque = c.F(p => Sd.Box(p, new Vector2(0.5f, 0.47f), new Vector2(0.43f, 0.17f), 0.06f));
            K.DropShadow(c, plaque);
            K.GoldBody(c, plaque, 0.3f, 0.64f);
            var inner = c.F(p => Sd.Box(p, new Vector2(0.5f, 0.47f), new Vector2(0.37f, 0.115f), 0.035f));
            c.Fill(inner, K.Ink, 0.01f);
            c.Fill(inner, (p, d) => Pal.Ramp(Mathf.InverseLerp(0.355f, 0.585f, p.y), Pal.Hex("3a0302"), Pal.Hex("9c0e06"), Pal.Hex("d8300c")));
            var t = font.Text(c.W, c.H, "WILD", new Rect(0.15f, 0.375f, 0.7f, 0.19f), 0.03f, 0.85f).Grow(0.012f);
            c.Fill(t, K.Ink, 0.02f);
            c.Fill(t, (p, d) => Pal.Gold(p.y, 0.38f, 0.56f));
            c.Bevel(t, 0.02f, 0.6f, K.Light);
            K.Sparkle(c, new Vector2(0.86f, 0.64f), 0.09f, Color.white);
            K.Sparkle(c, new Vector2(0.15f, 0.31f), 0.06f, Pal.Hex("fff1b0"));
        }

        static void Crown(PaintCanvas c)
        {
            var velvet = c.F(p => Sd.Ellipse(p, new Vector2(0.5f, 0.52f), new Vector2(0.27f, 0.2f)));
            c.Fill(velvet, K.Ink, 0.014f);
            c.Fill(velvet, (p, d) => Pal.Ramp(Mathf.InverseLerp(0.34f, 0.72f, p.y), Pal.Hex("3d0208"), Pal.Hex("a3101e"), Pal.Hex("e23a3a")));
            Vector2[] body =
            {
                new Vector2(0.2f, 0.2f), new Vector2(0.8f, 0.2f), new Vector2(0.84f, 0.36f), new Vector2(0.88f, 0.7f), new Vector2(0.74f, 0.5f),
                new Vector2(0.67f, 0.78f), new Vector2(0.58f, 0.52f), new Vector2(0.5f, 0.86f), new Vector2(0.42f, 0.52f),
                new Vector2(0.33f, 0.78f), new Vector2(0.26f, 0.5f), new Vector2(0.12f, 0.7f), new Vector2(0.16f, 0.36f),
            };
            var crown = c.F(p => Sd.Polygon(p, body));
            K.DropShadow(c, crown);
            K.GoldBody(c, crown, 0.2f, 0.86f);
            Vector2[] tips = { new Vector2(0.12f, 0.72f), new Vector2(0.33f, 0.8f), new Vector2(0.5f, 0.88f), new Vector2(0.67f, 0.8f), new Vector2(0.88f, 0.72f) };
            foreach (var tip in tips)
            {
                var ball = c.F(p => Sd.Circle(p, tip, 0.038f));
                c.Fill(ball, K.Ink, 0.012f);
                c.Fill(ball, (p, d) => Pal.Gold(p.y, tip.y - 0.04f, tip.y + 0.04f));
                c.Bevel(ball, 0.03f, 0.8f, K.Light);
            }
            var band = c.F(p => Sd.Box(p, new Vector2(0.5f, 0.28f), new Vector2(0.33f, 0.075f), 0.02f));
            c.Fill(band, K.Ink, 0.01f);
            c.Fill(band, (p, d) => Pal.Gold(p.y, 0.36f, 0.2f));
            c.Bevel(band, 0.03f, 0.6f, K.Light);
            SmallGem(c, new Vector2(0.5f, 0.28f), new Vector2(0.06f, 0.05f), Pal.Hex("e0102f"));
            SmallGem(c, new Vector2(0.31f, 0.28f), new Vector2(0.04f, 0.04f), Pal.Hex("1f6bff"));
            SmallGem(c, new Vector2(0.69f, 0.28f), new Vector2(0.04f, 0.04f), Pal.Hex("1f6bff"));
            SmallGem(c, new Vector2(0.5f, 0.62f), new Vector2(0.035f, 0.045f), Pal.Hex("1fcf6b"));
            K.Sparkle(c, new Vector2(0.78f, 0.84f), 0.08f, Color.white);
        }

        static void SmallGem(PaintCanvas c, Vector2 at, Vector2 r, Color color)
        {
            var g = c.F(p => Sd.Ellipse(p, at, r));
            c.Fill(g, K.Ink, 0.008f);
            c.Fill(g, (p, d) => Pal.Ramp(Mathf.InverseLerp(at.y - r.y, at.y + r.y, p.y), Pal.Mul(color, 0.45f), color, Color.Lerp(color, Color.white, 0.5f)));
            K.Gloss(c, g, p => Sd.Circle(p, at + new Vector2(-r.x * 0.3f, r.y * 0.35f), Mathf.Min(r.x, r.y) * 0.35f), 0.85f);
        }

        static void Chest(PaintCanvas c)
        {
            // Glow of the treasure inside.
            var glow = c.F(p => Sd.Ellipse(p, new Vector2(0.5f, 0.6f), new Vector2(0.3f, 0.12f)));
            c.Glow(glow, 0.22f, Pal.A(FireYellow, 0.6f), 1.5f);
            // Open lid, tilted back.
            Vector2[] lidPts = { new Vector2(0.17f, 0.56f), new Vector2(0.83f, 0.56f), new Vector2(0.78f, 0.86f), new Vector2(0.22f, 0.86f) };
            var lid = c.F(p => Sd.Polygon(p, lidPts));
            K.DropShadow(c, lid);
            c.Fill(lid, K.Ink, 0.014f);
            c.Fill(lid, (p, d) => Pal.Ramp(Mathf.InverseLerp(0.56f, 0.86f, p.y), Pal.Hex("3b1806"), Pal.Hex("7a3a12"), Pal.Hex("a85a22")));
            var lidInner = c.F(p => Sd.Polygon(p, new[] { new Vector2(0.24f, 0.6f), new Vector2(0.76f, 0.6f), new Vector2(0.72f, 0.82f), new Vector2(0.28f, 0.82f) }));
            c.Fill(lidInner, (p, d) => Pal.Ramp(Mathf.InverseLerp(0.6f, 0.82f, p.y), Pal.Hex("ffb43a"), Pal.Hex("5a2408")));
            var lidBand = c.F(p => Mathf.Min(Sd.Box(p, new Vector2(0.5f, 0.71f), new Vector2(0.045f, 0.16f)), Sd.Box(p, new Vector2(0.5f, 0.86f), new Vector2(0.29f, 0.022f))));
            K.GoldBody(c, lidBand, 0.55f, 0.88f, 0.008f, 0.02f);

            // Coin pile.
            Vector2[] coins =
            {
                new Vector2(0.27f, 0.55f), new Vector2(0.38f, 0.58f), new Vector2(0.5f, 0.6f), new Vector2(0.62f, 0.58f), new Vector2(0.73f, 0.55f),
                new Vector2(0.33f, 0.63f), new Vector2(0.45f, 0.66f), new Vector2(0.57f, 0.66f), new Vector2(0.68f, 0.62f), new Vector2(0.51f, 0.71f),
            };
            foreach (var co in coins)
            {
                var coin = c.F(p => Sd.Ellipse(p, co, new Vector2(0.06f, 0.045f)));
                c.Fill(coin, Pal.Hex("6b3a04"), 0.008f);
                c.Fill(coin, (p, d) => Pal.Gold(p.y, co.y - 0.05f, co.y + 0.05f));
                c.Stroke(coin, 0.004f, Pal.Hex("a86a0c"), -0.014f);
            }
            var ruby = c.F(p => Sd.Polygon(p, Sd.RegularPoints(new Vector2(0.6f, 0.69f), new Vector2(0.045f, 0.04f), 6, 0f)));
            c.Fill(ruby, K.Ink, 0.008f);
            c.Fill(ruby, (p, d) => Pal.Ramp(Mathf.InverseLerp(0.65f, 0.73f, p.y), Pal.Hex("6a0412"), Pal.Hex("ff3050")));

            // Body.
            var body = c.F(p => Sd.Box(p, new Vector2(0.5f, 0.37f), new Vector2(0.34f, 0.19f), 0.025f));
            K.DropShadow(c, body);
            c.Fill(body, K.Ink, 0.014f);
            c.Fill(body, (p, d) =>
            {
                Color wood = Pal.Ramp(Mathf.InverseLerp(0.18f, 0.56f, p.y), Pal.Hex("3d1a07"), Pal.Hex("8a4416"), Pal.Hex("b0652a"));
                float plank = Mathf.Abs(Mathf.Sin((p.y - 0.18f) * Mathf.PI / 0.095f));
                return Color.Lerp(Pal.Mul(wood, 0.55f), wood, Mathf.Clamp01(plank * 6f));
            });
            var straps = c.F(p => Mathf.Min(Mathf.Min(Sd.Box(p, new Vector2(0.24f, 0.37f), new Vector2(0.04f, 0.19f)), Sd.Box(p, new Vector2(0.76f, 0.37f), new Vector2(0.04f, 0.19f))),
                Sd.Box(p, new Vector2(0.5f, 0.535f), new Vector2(0.34f, 0.025f))));
            K.GoldBody(c, straps, 0.18f, 0.56f, 0.008f, 0.025f);
            var lockPlate = c.F(p => Sd.Box(p, new Vector2(0.5f, 0.42f), new Vector2(0.07f, 0.085f), 0.025f));
            K.GoldBody(c, lockPlate, 0.33f, 0.51f, 0.01f, 0.03f);
            var keyhole = c.F(p => Mathf.Min(Sd.Circle(p, new Vector2(0.5f, 0.44f), 0.018f), Sd.Box(p, new Vector2(0.5f, 0.405f), new Vector2(0.008f, 0.03f))));
            c.Fill(keyhole, K.Ink);
            for (int i = 0; i < 4; i++)
            {
                Vector2 rv = new Vector2(i < 2 ? 0.24f : 0.76f, i % 2 == 0 ? 0.24f : 0.48f);
                c.Fill(c.F(p => Sd.Circle(p, rv, 0.012f)), Pal.Hex("fff3b0"));
            }
            K.Sparkle(c, new Vector2(0.3f, 0.75f), 0.07f, Color.white);
            K.Sparkle(c, new Vector2(0.72f, 0.68f), 0.05f, Pal.Hex("fff1b0"));
        }

        static void GoldBars(PaintCanvas c)
        {
            void Bar(Vector2 at)
            {
                Vector2[] front = { at + new Vector2(-0.2f, -0.075f), at + new Vector2(0.2f, -0.075f), at + new Vector2(0.155f, 0.045f), at + new Vector2(-0.155f, 0.045f) };
                Vector2[] top = { at + new Vector2(-0.155f, 0.045f), at + new Vector2(0.155f, 0.045f), at + new Vector2(0.115f, 0.085f), at + new Vector2(-0.115f, 0.085f) };
                var all = c.F(p => Mathf.Min(Sd.Polygon(p, front), Sd.Polygon(p, top)));
                K.DropShadow(c, all, 0f, 0.5f);
                c.Fill(all, K.Ink, 0.012f);
                var f = c.F(p => Sd.Polygon(p, front));
                c.Fill(f, (p, d) => Pal.Gold(p.y, at.y - 0.09f, at.y + 0.07f));
                c.Bevel(f, 0.03f, 0.6f, K.Light);
                var t = c.F(p => Sd.Polygon(p, top));
                c.Fill(t, (p, d) => Pal.Ramp(Mathf.InverseLerp(at.y + 0.045f, at.y + 0.085f, p.y), Pal.Hex("ffe58a"), Pal.Hex("fffbe6")));
                var stamp = c.F(p => Sd.Box(p, at + new Vector2(0f, -0.015f), new Vector2(0.07f, 0.03f), 0.01f));
                c.Stroke(stamp, 0.004f, Pal.Hex("9a5a07"));
            }
            Bar(new Vector2(0.29f, 0.3f));
            Bar(new Vector2(0.71f, 0.3f));
            Bar(new Vector2(0.5f, 0.47f));
            Bar(new Vector2(0.5f, 0.64f));
            K.Sparkle(c, new Vector2(0.76f, 0.74f), 0.09f, Color.white);
            K.Sparkle(c, new Vector2(0.2f, 0.48f), 0.05f, Pal.Hex("fff1b0"));
        }

        // ------------------------------------------------------------------ UI
        public static Color[] BuildUi(SdfFont font)
        {
            var atlas = new Color[1024 * 1024];
            for (int i = 0; i < atlas.Length; i++) atlas[i] = new Color(0, 0, 0, 0);
            Color goldTop = Pal.Hex("fff3b0"), goldBottom = Pal.Hex("e08a10"), rimRed = Pal.Hex("b0140a"), glow = Pal.A(FireOrange, 0.55f);

            Draw(atlas, Logo, c =>
            {
                float w = (float)c.W / c.H;
                var fire = c.F(p =>
                {
                    float x = p.x / w;
                    float top = 0.55f + 0.18f * Mathf.Pow(Mathf.Abs(Mathf.Sin(x * 31f)), 3f) + 0.12f * Mathf.Sin(x * 17f + 1f) * Mathf.Sin(x * 5f);
                    return Mathf.Max(p.y - top, Sd.Box(p, new Vector2(w * 0.5f, 0.4f), new Vector2(w * 0.43f, 0.35f), 0.15f));
                });
                c.Glow(fire, 0.12f, Pal.A(FireOrange, 0.5f), 1.6f);
                c.Fill(fire, (p, d) => Pal.A(Pal.Ramp(Mathf.Clamp01(-d / 0.12f), FireRed, FireOrange, FireYellow), 0.9f));
                Sd_Arc(w, out var warp);
                K.BannerText(c, font, "GOLDEN", new Rect(w * 0.3f, 0.6f, w * 0.4f, 0.34f), goldTop, goldBottom, rimRed, glow, 0.07f, 0.08f, null, true);
                K.BannerText(c, font, "INFERNO", new Rect(w * 0.06f, 0.04f, w * 0.88f, 0.6f), Pal.Hex("fff7d6"), Pal.Hex("ff7a00"), rimRed, glow, 0.06f, 0.04f, warp, true);
            });
            Draw(atlas, HoldSpin, c => K.BannerText(c, font, "HOLD & SPIN", Inset(c, 0.1f), goldTop, goldBottom, rimRed, glow, 0.07f, 0.06f, null, true));
            Draw(atlas, BigWin, c => K.BannerText(c, font, "BIG WIN", Inset(c, 0.08f), goldTop, goldBottom, rimRed, glow, 0.07f, 0.05f, null, true));
            Draw(atlas, MegaWin, c => K.BannerText(c, font, "MEGA WIN", Inset(c, 0.08f), Pal.Hex("ffe0ff"), Pal.Hex("ff3aa0"), Pal.Hex("5a0a6b"), Pal.A(Pal.Hex("ff3aa0"), 0.5f), 0.07f, 0.05f, null, true));
            Draw(atlas, EpicWin, c => K.BannerText(c, font, "EPIC WIN", Inset(c, 0.08f), Pal.Hex("e0f7ff"), Pal.Hex("1fa8ff"), Pal.Hex("0a1f6b"), Pal.A(Pal.Hex("39c7ff"), 0.5f), 0.07f, 0.05f, null, true));
            Draw(atlas, TotalWin, c => K.BannerText(c, font, "TOTAL WIN", Inset(c, 0.12f), goldTop, goldBottom, rimRed, glow, 0.07f, 0.05f, null, true));
            Draw(atlas, Grand, c => Badge(c, font, "GRAND", JackpotColors[3]));
            Draw(atlas, Major, c => Badge(c, font, "MAJOR", JackpotColors[2]));
            Draw(atlas, Minor, c => Badge(c, font, "MINOR", JackpotColors[1]));
            Draw(atlas, Mini, c => Badge(c, font, "MINI", JackpotColors[0]));
            Draw(atlas, Respins, c => K.BannerText(c, font, "RESPINS", Inset(c, 0.14f), Pal.Hex("ffffff"), Pal.Hex("ffc23a"), rimRed, new Color(0, 0, 0, 0), 0.07f, 0.06f));
            Draw(atlas, Jackpot, c => K.BannerText(c, font, "JACKPOT!", Inset(c, 0.08f), goldTop, goldBottom, rimRed, glow, 0.07f, 0.05f, null, true));
            return atlas;
        }

        static void Sd_Arc(float w, out System.Func<Vector2, Vector2> warp)
        {
            // Gentle upward arch for the big word.
            float cx = w * 0.5f;
            warp = p => new Vector2(p.x, p.y + 0.12f * (1f - Mathf.Pow((p.x - cx) / (w * 0.5f), 2f)) - 0.06f);
        }

        static Rect Inset(PaintCanvas c, float m)
        {
            float w = (float)c.W / c.H;
            return new Rect(m, m, w - 2 * m, 1f - 2 * m);
        }

        // Jackpot name badge (the meters on the top screen).
        static void Badge(PaintCanvas c, SdfFont font, string label, Color color)
        {
            float w = (float)c.W / c.H;
            var plate = c.F(p => Sd.Box(p, new Vector2(w * 0.5f, 0.5f), new Vector2(w * 0.5f - 0.06f, 0.4f), 0.2f));
            c.Fill(plate, K.Ink, 0.03f);
            c.Fill(plate, (p, d) => Pal.Gold(p.y, 0.08f, 0.92f), 0.02f);
            c.Bevel(plate.Grow(0.02f), 0.03f, 0.6f, K.Light);
            c.Fill(plate, (p, d) => Pal.Ramp(Mathf.InverseLerp(0.12f, 0.88f, p.y), Pal.Mul(color, 0.35f), Pal.Mul(color, 0.85f), Color.Lerp(color, Color.white, 0.35f)));
            K.Gloss(c, plate, p => p.y > 0.55f ? -0.01f : 0.01f, 0.18f);
            K.BannerText(c, font, label, new Rect(0.25f, 0.16f, w - 0.5f, 0.68f), Color.white, Pal.Hex("ffe08a"), Pal.Mul(color, 0.4f), new Color(0, 0, 0, 0), 0.06f, 0.08f);
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
