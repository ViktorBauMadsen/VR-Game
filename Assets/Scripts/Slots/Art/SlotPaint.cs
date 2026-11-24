using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// A tiny procedural painter for the video slot art (symbols, logos,
// banners). Shapes are signed distance fields in normalized canvas
// coordinates (0..1, y up, negative inside); a Field holds one sampled on
// the canvas grid so it can be filled, outlined, beveled, glowed and
// shadowed. Text comes from the TextMeshPro font's SDF atlas.
namespace VideoSlots
{
    public delegate float Sdf(Vector2 p);

    public static class Sd
    {
        public static float Circle(Vector2 p, Vector2 c, float r) => (p - c).magnitude - r;

        public static float Ellipse(Vector2 p, Vector2 c, Vector2 r)
        {
            Vector2 q = p - c;
            float k = new Vector2(q.x / r.x, q.y / r.y).magnitude;
            return (k - 1f) * Mathf.Min(r.x, r.y);
        }

        public static float Box(Vector2 p, Vector2 c, Vector2 half, float round = 0f)
        {
            Vector2 d = new Vector2(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y)) - half + new Vector2(round, round);
            return new Vector2(Mathf.Max(d.x, 0f), Mathf.Max(d.y, 0f)).magnitude + Mathf.Min(Mathf.Max(d.x, d.y), 0f) - round;
        }

        public static float Segment(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - r;
        }

        // Exact distance to a simple polygon (Inigo Quilez).
        public static float Polygon(Vector2 p, Vector2[] v)
        {
            float d = Vector2.Dot(p - v[0], p - v[0]);
            float s = 1f;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
            {
                Vector2 e = v[j] - v[i];
                Vector2 w = p - v[i];
                Vector2 b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
                d = Mathf.Min(d, Vector2.Dot(b, b));
                bool c1 = p.y >= v[i].y, c2 = p.y < v[j].y, c3 = e.x * w.y > e.y * w.x;
                if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
            }
            return s * Mathf.Sqrt(d);
        }

        public static Vector2[] StarPoints(Vector2 c, float outer, float inner, int points, float rotationDeg)
        {
            var v = new Vector2[points * 2];
            for (int i = 0; i < v.Length; i++)
            {
                float a = (rotationDeg + 90f + i * 180f / points) * Mathf.Deg2Rad;
                float r = i % 2 == 0 ? outer : inner;
                v[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            }
            return v;
        }

        public static Vector2[] RegularPoints(Vector2 c, Vector2 radius, int sides, float rotationDeg)
        {
            var v = new Vector2[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = (rotationDeg + i * 360f / sides) * Mathf.Deg2Rad;
                v[i] = c + new Vector2(Mathf.Cos(a) * radius.x, Mathf.Sin(a) * radius.y);
            }
            return v;
        }

        public static Vector2 Rotate(Vector2 p, Vector2 pivot, float deg)
        {
            float a = deg * Mathf.Deg2Rad, cs = Mathf.Cos(a), sn = Mathf.Sin(a);
            Vector2 q = p - pivot;
            return pivot + new Vector2(q.x * cs - q.y * sn, q.x * sn + q.y * cs);
        }

        public static float SmoothMin(float a, float b, float k)
        {
            float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, h) - k * h * (1f - h);
        }
    }

    // A signed distance field sampled on the canvas grid. Units: 1 = the canvas height (x runs 0..W/H).
    public sealed class Field
    {
        public readonly int W, H;
        public readonly float[] D;

        public Field(int w, int h) { W = w; H = h; D = new float[w * h]; }

        public static Field Of(int w, int h, Sdf sdf)
        {
            var f = new Field(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    f.D[y * w + x] = sdf(new Vector2((x + 0.5f) / h, (y + 0.5f) / h));
            return f;
        }

        public Field Map(Func<float, float> fn)
        {
            var f = new Field(W, H);
            for (int i = 0; i < D.Length; i++) f.D[i] = fn(D[i]);
            return f;
        }

        public Field Grow(float r) => Map(d => d - r);
        public static Field Union(Field a, Field b) { var f = new Field(a.W, a.H); for (int i = 0; i < f.D.Length; i++) f.D[i] = Mathf.Min(a.D[i], b.D[i]); return f; }
        public static Field Intersect(Field a, Field b) { var f = new Field(a.W, a.H); for (int i = 0; i < f.D.Length; i++) f.D[i] = Mathf.Max(a.D[i], b.D[i]); return f; }
        public static Field Subtract(Field a, Field b) { var f = new Field(a.W, a.H); for (int i = 0; i < f.D.Length; i++) f.D[i] = Mathf.Max(a.D[i], -b.D[i]); return f; }

        public float At(int x, int y) => D[Mathf.Clamp(y, 0, H - 1) * W + Mathf.Clamp(x, 0, W - 1)];

        public float Sample(Vector2 p)
        {
            float fx = p.x * H - 0.5f, fy = p.y * H - 0.5f;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            float tx = fx - x0, ty = fy - y0;
            float a = Mathf.Lerp(At(x0, y0), At(x0 + 1, y0), tx);
            float b = Mathf.Lerp(At(x0, y0 + 1), At(x0 + 1, y0 + 1), tx);
            return Mathf.Lerp(a, b, ty);
        }

        // Outward direction (normalized gradient) at a grid cell.
        public Vector2 Normal(int x, int y)
        {
            var g = new Vector2(At(x + 1, y) - At(x - 1, y), At(x, y + 1) - At(x, y - 1));
            float m = g.magnitude;
            return m > 1e-7f ? g / m : Vector2.zero;
        }

        // Exact Euclidean distances everywhere from a coverage threshold
        // (used to outline text much wider than the font's SDF spread).
        public Field Rebuild()
        {
            var inside = new bool[D.Length];
            for (int i = 0; i < D.Length; i++) inside[i] = D[i] < 0f;
            float[] dOut = Edt(inside, true), dIn = Edt(inside, false);
            float px = 1f / H;
            var f = new Field(W, H);
            for (int i = 0; i < D.Length; i++)
            {
                float exact = inside[i] ? -(Mathf.Sqrt(dIn[i]) - 0.5f) * px : (Mathf.Sqrt(dOut[i]) - 0.5f) * px;
                // Keep the original sub-pixel precision right at the edge.
                f.D[i] = Mathf.Abs(D[i]) < 1.5f * px ? D[i] : exact;
            }
            return f;
        }

        // Squared distance (in pixels) to the nearest cell where target == mask.
        float[] Edt(bool[] mask, bool target)
        {
            const float Inf = 1e20f;
            var g = new float[W * H];
            for (int i = 0; i < g.Length; i++) g[i] = mask[i] == target ? 0f : Inf;
            int n = Mathf.Max(W, H);
            var f = new float[n]; var dOut = new float[n]; var v = new int[n]; var z = new float[n + 1];
            for (int x = 0; x < W; x++)
            {
                for (int y = 0; y < H; y++) f[y] = g[y * W + x];
                Edt1D(f, H, dOut, v, z);
                for (int y = 0; y < H; y++) g[y * W + x] = dOut[y];
            }
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++) f[x] = g[y * W + x];
                Edt1D(f, W, dOut, v, z);
                for (int x = 0; x < W; x++) g[y * W + x] = dOut[x];
            }
            return g;
        }

        static float Meet(float[] f, int q, int r) => ((f[q] + q * q) - (f[r] + r * r)) / (2f * q - 2f * r);

        static void Edt1D(float[] f, int n, float[] d, int[] v, float[] z)
        {
            int k = 0;
            v[0] = 0; z[0] = float.NegativeInfinity; z[1] = float.PositiveInfinity;
            for (int q = 1; q < n; q++)
            {
                float s = Meet(f, q, v[k]);
                while (s <= z[k]) { k--; s = Meet(f, q, v[k]); }
                k++;
                v[k] = q; z[k] = s; z[k + 1] = float.PositiveInfinity;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                int r = v[k];
                d[q] = (q - r) * (q - r) + f[r];
            }
        }
    }

    public sealed class PaintCanvas
    {
        public readonly int W, H;
        public readonly Color[] Px;

        public PaintCanvas(int w, int h)
        {
            W = w; H = h;
            Px = new Color[w * h];
            for (int i = 0; i < Px.Length; i++) Px[i] = new Color(0f, 0f, 0f, 0f);
        }

        public Field F(Sdf sdf) => Field.Of(W, H, sdf);
        float Coverage(float d) => Mathf.Clamp01(0.5f - d * H);
        static Vector2 P(int x, int y, int w, int h) => new Vector2((x + 0.5f) / h, (y + 0.5f) / h);

        public void Blend(int i, Color c)
        {
            if (c.a <= 0f) return;
            Color d = Px[i];
            float a = c.a + d.a * (1f - c.a);
            if (a <= 1e-6f) return;
            float k = d.a * (1f - c.a);
            Px[i] = new Color((c.r * c.a + d.r * k) / a, (c.g * c.a + d.g * k) / a, (c.b * c.a + d.b * k) / a, a);
        }

        // Lightens what's already there (for glints on top of art).
        public void Add(int i, Color c)
        {
            Color d = Px[i];
            float s = c.a;
            Px[i] = new Color(Mathf.Min(1f, d.r + c.r * s), Mathf.Min(1f, d.g + c.g * s), Mathf.Min(1f, d.b + c.b * s), Mathf.Max(d.a, Mathf.Min(1f, d.a + s * 0.5f)));
        }

        public void Fill(Field f, Func<Vector2, float, Color> paint, float grow = 0f)
        {
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    float d = f.D[i] - grow;
                    float cov = Coverage(d);
                    if (cov <= 0f) continue;
                    Color c = paint(P(x, y, W, H), d);
                    c.a *= cov;
                    Blend(i, c);
                }
        }

        public void Fill(Field f, Color color, float grow = 0f) => Fill(f, (p, d) => color, grow);

        // A band |d - center| < halfWidth (an outline straddling the edge when center = 0).
        public void Stroke(Field f, float halfWidth, Func<Vector2, float, Color> paint, float center = 0f)
        {
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    float d = Mathf.Abs(f.D[i] - center) - halfWidth;
                    float cov = Coverage(d);
                    if (cov <= 0f) continue;
                    Color c = paint(P(x, y, W, H), f.D[i]);
                    c.a *= cov;
                    Blend(i, c);
                }
        }

        public void Stroke(Field f, float halfWidth, Color color, float center = 0f) => Stroke(f, halfWidth, (p, d) => color, center);

        // Soft light falling off outside the shape over radius.
        public void Glow(Field f, float radius, Color color, float power = 2f, bool additive = false)
        {
            for (int i = 0; i < Px.Length; i++)
            {
                float d = f.D[i];
                if (d >= radius) continue;
                float k = d <= 0f ? 1f : Mathf.Pow(1f - d / radius, power);
                Color c = color; c.a *= k;
                if (additive) Add(i, c); else Blend(i, c);
            }
        }

        public void Shadow(Field f, Vector2 offset, float softness, Color color)
        {
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float d = f.Sample(P(x, y, W, H) - offset);
                    float k = Mathf.Clamp01(0.5f - d / Mathf.Max(softness, 1f / H));
                    if (k <= 0f) continue;
                    Color c = color; c.a *= k * k * (3f - 2f * k);
                    Blend(y * W + x, c);
                }
        }

        // Embossed edge: inside the shape, within width of the edge, pixels
        // facing the light get lighter and the others darker.
        public void Bevel(Field f, float width, float strength, Vector2 lightDir)
        {
            lightDir.Normalize();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    float d = f.D[i];
                    if (d > 0.5f / H || d < -width) continue;
                    float t = 1f - Mathf.Clamp01(-d / width);
                    float shade = Vector2.Dot(f.Normal(x, y), lightDir) * t * t * strength;
                    Color c = Px[i];
                    if (shade > 0) c = Color.Lerp(c, Color.white, Mathf.Min(1f, shade));
                    else c = Color.Lerp(c, Color.black, Mathf.Min(1f, -shade));
                    c.a = Px[i].a;
                    Px[i] = c;
                }
        }

        // Paint only where the shape already is (gloss bands, inner shading).
        public void Inside(Field clip, Field shape, Func<Vector2, float, Color> paint, bool additive = false)
        {
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    float cov = Coverage(clip.D[i]) * Coverage(shape.D[i]);
                    if (cov <= 0f) continue;
                    Color c = paint(P(x, y, W, H), shape.D[i]);
                    c.a *= cov;
                    if (additive) Add(i, c); else Blend(i, c);
                }
        }

        // Motion-blurred copy (for spinning reels): vertical box blur, applied twice.
        public PaintCanvas BlurVertical(int radius)
        {
            var src = this;
            for (int pass = 0; pass < 2; pass++)
            {
                var dst = new PaintCanvas(W, H);
                for (int x = 0; x < W; x++)
                {
                    for (int y = 0; y < H; y++)
                    {
                        Color acc = new Color(0, 0, 0, 0); float n = 0;
                        for (int k = -radius; k <= radius; k++)
                        {
                            int yy = y + k;
                            if (yy < 0 || yy >= H) { n++; continue; }
                            Color c = src.Px[yy * W + x];
                            acc.r += c.r * c.a; acc.g += c.g * c.a; acc.b += c.b * c.a; acc.a += c.a; n++;
                        }
                        float a = acc.a / n;
                        dst.Px[y * W + x] = a > 1e-5f ? new Color(acc.r / acc.a, acc.g / acc.a, acc.b / acc.a, a) : new Color(0, 0, 0, 0);
                    }
                }
                src = dst;
            }
            return src;
        }

        // Fades alpha out within px pixels of the canvas border, so glows
        // that reach the edge of their cell don't end in a hard line.
        public PaintCanvas FadeEdges(int px)
        {
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int d = Mathf.Min(Mathf.Min(x, W - 1 - x), Mathf.Min(y, H - 1 - y));
                    if (d >= px) continue;
                    float k = (d + 0.5f) / px;
                    k = k * k * (3f - 2f * k);
                    int i = y * W + x;
                    Color c = Px[i];
                    c.a *= k;
                    Px[i] = c;
                }
            return this;
        }

        public void CopyTo(Color[] atlas, int atlasW, int ox, int oy)
        {
            for (int y = 0; y < H; y++)
                Array.Copy(Px, y * W, atlas, (oy + y) * atlasW + ox, W);
        }
    }

    public static class Pal
    {
        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out Color c);
            return c;
        }

        public static Color A(Color c, float a) { c.a = a; return c; }
        public static Color Mul(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        // Multi-stop gradient, t in 0..1, stops evenly spaced.
        public static Color Ramp(float t, params Color[] stops)
        {
            t = Mathf.Clamp01(t) * (stops.Length - 1);
            int i = Mathf.Min(Mathf.FloorToInt(t), stops.Length - 2);
            return Color.Lerp(stops[i], stops[i + 1], t - i);
        }

        static readonly Color[] GoldStops =
        {
            Hex("5a3104"), Hex("b8730f"), Hex("ffd65a"), Hex("fff6c9"), Hex("e3a01e"), Hex("9a5a07"), Hex("f7c948"), Hex("fff1a8"),
        };
        // Polished gold: banded reflections across the shape's height.
        public static Color Gold(float y, float y0, float y1) => Ramp(Mathf.InverseLerp(y0, y1, y), GoldStops);

        static readonly Color[] SilverStops =
        {
            Hex("3a4150"), Hex("8d97a8"), Hex("e8eef7"), Hex("ffffff"), Hex("aab4c4"), Hex("5f6878"), Hex("c9d2e0"), Hex("f4f7fb"),
        };
        public static Color Silver(float y, float y0, float y1) => Ramp(Mathf.InverseLerp(y0, y1, y), SilverStops);
    }

    // Text from the TextMeshPro font atlas (its alpha channel is an SDF).
    public sealed class SdfFont
    {
        readonly float[] _sdf; // 0..1, 0.5 = edge
        readonly int _aw, _ah;
        readonly float _padding;
        readonly Dictionary<char, UnityEngine.TextCore.Glyph> _glyphs = new Dictionary<char, UnityEngine.TextCore.Glyph>();
        public readonly float CapHeight;

        public SdfFont(TMP_FontAsset font)
        {
            var atlas = font.atlasTexture;
            _aw = atlas.width; _ah = atlas.height;
            _padding = font.atlasPadding;
            CapHeight = font.faceInfo.capLine > 0 ? font.faceInfo.capLine : font.faceInfo.pointSize * 0.7f;

            // The atlas isn't readable: copy it through a render texture.
            var rt = RenderTexture.GetTemporary(_aw, _ah, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var prev = RenderTexture.active;
            Graphics.Blit(atlas, rt);
            RenderTexture.active = rt;
            var tex = new Texture2D(_aw, _ah, TextureFormat.RGBA32, false, true);
            tex.ReadPixels(new Rect(0, 0, _aw, _ah), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = tex.GetPixels32();
            UnityEngine.Object.DestroyImmediate(tex);
            _sdf = new float[px.Length];
            for (int i = 0; i < px.Length; i++) _sdf[i] = px[i].a / 255f;

            foreach (var ch in font.characterTable)
                if (ch.unicode < 0x10000 && ch.glyph != null) _glyphs[(char)ch.unicode] = ch.glyph;
        }

        float AtlasSample(float x, float y)
        {
            x -= 0.5f; y -= 0.5f;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, _aw - 2), y0 = Mathf.Clamp(Mathf.FloorToInt(y), 0, _ah - 2);
            float tx = Mathf.Clamp01(x - x0), ty = Mathf.Clamp01(y - y0);
            float a = Mathf.Lerp(_sdf[y0 * _aw + x0], _sdf[y0 * _aw + x0 + 1], tx);
            float b = Mathf.Lerp(_sdf[(y0 + 1) * _aw + x0], _sdf[(y0 + 1) * _aw + x0 + 1], tx);
            return Mathf.Lerp(a, b, ty);
        }

        struct Placed { public UnityEngine.TextCore.Glyph g; public float x; }

        float Layout(string text, float tracking, List<Placed> placed)
        {
            float x = 0f;
            placed.Clear();
            for (int i = 0; i < text.Length; i++)
            {
                if (!_glyphs.TryGetValue(text[i], out var g)) { x += CapHeight * 0.3f; continue; }
                placed.Add(new Placed { g = g, x = x });
                x += g.metrics.horizontalAdvance + tracking * CapHeight;
            }
            return x - tracking * CapHeight;
        }

        // A field for the text fitted (centered) into box, in canvas space.
        // capFraction = cap height as a fraction of the box height (the rest
        // leaves room for outlines). warp lets the caller bend the text
        // (canvas point -> unwarped point).
        public Field Text(int w, int h, string text, Rect box, float tracking = 0.05f, float capFraction = 0.75f, Func<Vector2, Vector2> warp = null)
        {
            var placed = new List<Placed>();
            float width = Layout(text, tracking, placed);
            if (placed.Count == 0) return Field.Of(w, h, p => 1f);

            // Visual horizontal extent (bearing of the first glyph to the ink end of the last).
            float inkL = placed[0].x + placed[0].g.metrics.horizontalBearingX;
            var last = placed[placed.Count - 1];
            float inkR = last.x + last.g.metrics.horizontalBearingX + last.g.metrics.width;
            float inkW = Mathf.Max(1f, inkR - inkL);

            float scale = Mathf.Min(box.height * capFraction / CapHeight, box.width * 0.96f / inkW); // canvas units per font unit
            Vector2 origin = new Vector2(box.center.x - (inkL + inkW * 0.5f) * scale, box.center.y - CapHeight * 0.5f * scale);

            float spread = _padding; // font units
            var f = new Field(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Vector2 p = new Vector2((x + 0.5f) / h, (y + 0.5f) / h);
                    if (warp != null) p = warp(p);
                    Vector2 q = (p - origin) / scale; // font units, baseline at y = 0
                    float best = spread;
                    foreach (var pg in placed)
                    {
                        var m = pg.g.metrics;
                        var r = pg.g.glyphRect;
                        float gx = q.x - (pg.x + m.horizontalBearingX); // 0..width inside the glyph box
                        float gy = q.y - (m.horizontalBearingY - m.height);
                        if (gx < -spread || gy < -spread || gx > m.width + spread || gy > m.height + spread) continue;
                        float a = AtlasSample(r.x + gx * (r.width / Mathf.Max(1f, m.width)), r.y + gy * (r.height / Mathf.Max(1f, m.height)));
                        float d = (0.5f - a) * 2f * spread;
                        if (d < best) best = d;
                    }
                    f.D[y * w + x] = best * scale;
                }
            return f.Rebuild();
        }
    }
}
