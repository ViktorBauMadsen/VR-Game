using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using K = VideoSlots.SlotArtKit;

namespace VideoSlots
{
    // One dynamic mesh of textured quads, rebuilt every frame. All the video
    // slot screen art is drawn through these: cheap (one draw call per
    // layer), and clipping to the reel window is done on the CPU so the
    // shader stays a plain keyword-free unlit one.
    // Colors are premultiplied here, so the same material draws normal
    // (alpha) and additive (glow) quads: additive = alpha 0.
    public sealed class QuadBatch
    {
        readonly Mesh _mesh;
        readonly List<Vector3> _v = new List<Vector3>(512);
        readonly List<Vector2> _uv = new List<Vector2>(512);
        readonly List<Color> _c = new List<Color>(512);
        readonly List<int> _t = new List<int>(768);
        public readonly GameObject go;
        public readonly MeshRenderer renderer;
        static int _linear = -1;

        public QuadBatch(Transform parent, string name, Material material, float layerZ)
        {
            go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, -layerZ);
            _mesh = new Mesh { name = name };
            _mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        public int Count => _v.Count / 4;

        public void Clear()
        {
            _v.Clear(); _uv.Clear(); _c.Clear(); _t.Clear();
        }

        public static Color Prep(Color c, bool additive)
        {
            if (_linear < 0) _linear = QualitySettings.activeColorSpace == ColorSpace.Linear ? 1 : 0;
            float a = Mathf.Clamp01(c.a);
            if (_linear == 1) c = new Color(Lin(c.r), Lin(c.g), Lin(c.b));
            return new Color(c.r * a, c.g * a, c.b * a, additive ? 0f : a);
        }

        static float Lin(float v) => v <= 0f ? 0f : (v <= 1f ? Mathf.GammaToLinearSpace(v) : Mathf.Pow(v, 2.2f));

        static bool Visible(Color c) => c.a > 0.002f || c.r + c.g + c.b > 0.002f;

        void Push(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Color ca, Color cb, Color cc, Color cd)
        {
            int i = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
            _uv.Add(ua); _uv.Add(ub); _uv.Add(uc); _uv.Add(ud);
            _c.Add(ca); _c.Add(cb); _c.Add(cc); _c.Add(cd);
            _t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
            _t.Add(i); _t.Add(i + 2); _t.Add(i + 3);
        }

        // Centered quad, optionally rotated (degrees, counter-clockwise).
        public void Quad(Vector2 center, Vector2 size, Rect uv, Color color, float angle = 0f, bool additive = false)
        {
            Color c = Prep(color, additive);
            if (!Visible(c) || size.x <= 0f || size.y <= 0f) return;
            Vector2 hx = new Vector2(size.x * 0.5f, 0f), hy = new Vector2(0f, size.y * 0.5f);
            if (angle != 0f)
            {
                float r = angle * Mathf.Deg2Rad, cs = Mathf.Cos(r), sn = Mathf.Sin(r);
                hx = new Vector2(hx.x * cs, hx.x * sn);
                hy = new Vector2(-hy.y * sn, hy.y * cs);
            }
            Push(center - hx - hy, center - hx + hy, center + hx + hy, center + hx - hy,
                new Vector2(uv.xMin, uv.yMin), new Vector2(uv.xMin, uv.yMax), new Vector2(uv.xMax, uv.yMax), new Vector2(uv.xMax, uv.yMin),
                c, c, c, c);
        }

        // Axis-aligned quad with a vertical color gradient, cropped to clip.
        public void Rect(Rect rect, Rect uv, Color top, Color bottom, Rect clip, bool additive = false)
        {
            float x0 = Mathf.Max(rect.xMin, clip.xMin), x1 = Mathf.Min(rect.xMax, clip.xMax);
            float y0 = Mathf.Max(rect.yMin, clip.yMin), y1 = Mathf.Min(rect.yMax, clip.yMax);
            if (x1 <= x0 || y1 <= y0) return;
            float u0 = Mathf.Lerp(uv.xMin, uv.xMax, (x0 - rect.xMin) / rect.width);
            float u1 = Mathf.Lerp(uv.xMin, uv.xMax, (x1 - rect.xMin) / rect.width);
            float tb = (y0 - rect.yMin) / rect.height, tt = (y1 - rect.yMin) / rect.height;
            float v0 = Mathf.Lerp(uv.yMin, uv.yMax, tb), v1 = Mathf.Lerp(uv.yMin, uv.yMax, tt);
            Color cb = Prep(Color.LerpUnclamped(bottom, top, tb), additive), ct = Prep(Color.LerpUnclamped(bottom, top, tt), additive);
            if (!Visible(cb) && !Visible(ct)) return;
            Push(new Vector2(x0, y0), new Vector2(x0, y1), new Vector2(x1, y1), new Vector2(x1, y0),
                new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0),
                cb, ct, ct, cb);
        }

        public void Rect(Rect rect, Rect uv, Color color, bool additive = false) => Rect(rect, uv, color, color, Unclipped, additive);
        public void Rect(Rect rect, Rect uv, Color top, Color bottom, bool additive = false) => Rect(rect, uv, top, bottom, Unclipped, additive);
        public static readonly Rect Unclipped = new Rect(-1000f, -1000f, 2000f, 2000f);

        // A thick line segment (uses the Line fx cell: bright core, soft sides).
        public void Line(Vector2 a, Vector2 b, float width, Rect uv, Color color, bool additive = false)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1e-5f) return;
            Vector2 n = new Vector2(-d.y, d.x) / len * (width * 0.5f);
            Vector2 ext = d / len * (width * 0.25f);
            a -= ext; b += ext;
            Color c = Prep(color, additive);
            if (!Visible(c)) return;
            Push(a - n, a + n, b + n, b - n,
                new Vector2(uv.xMin, uv.yMin), new Vector2(uv.xMin, uv.yMax), new Vector2(uv.xMax, uv.yMax), new Vector2(uv.xMax, uv.yMin),
                c, c, c, c);
        }

        public void Apply()
        {
            _mesh.Clear(true);
            _mesh.SetVertices(_v);
            _mesh.SetUVs(0, _uv);
            _mesh.SetColors(_c);
            _mesh.SetTriangles(_t, 0, false);
            _mesh.bounds = new Bounds(Vector3.zero, new Vector3(6f, 6f, 2f));
        }

        public void SetVisible(bool on) { if (go.activeSelf != on) go.SetActive(on); }
    }

    public static class SlotMaterials
    {
        static Material _base;
        public const int QueueBase = 3010;

        public static Material Create(Texture tex, int queueOffset, float brightness = 1f)
        {
            if (_base == null)
            {
                _base = Resources.Load<Material>("VideoSlots/SlotScreen");
                if (_base == null)
                {
                    var shader = Shader.Find("Casino/SlotScreen");
                    if (shader == null) shader = Shader.Find("Sprites/Default");
                    _base = new Material(shader);
                }
            }
            var m = new Material(_base) { name = "SlotScreen " + (tex != null ? tex.name : "white") };
            m.SetTexture("_MainTex", tex != null ? tex : Texture2D.whiteTexture);
            m.SetFloat("_SrcBlend", (float)BlendMode.One);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Brightness", brightness);
            m.renderQueue = QueueBase + queueOffset;
            return m;
        }
    }

    // The baked art (Assets/VideoSlots/Resources/VideoSlots/*.png) - or, if
    // it hasn't been baked yet, the same art generated on the fly.
    public static class SlotTextures
    {
        public sealed class Entry
        {
            public string name;
            public int w, h;
            public Func<SdfFont, Color[]> build;
        }

        public static readonly Entry[] All =
        {
            new Entry { name = "Fx", w = FxAtlas.Size, h = FxAtlas.Size, build = f => K.BuildFxAtlas() },
            new Entry { name = "Digits", w = Digits.Width, h = Digits.Height, build = Digits.Build },
            new Entry { name = "Inferno_Symbols", w = SymbolAtlas.Width, h = SymbolAtlas.Height, build = GoldenInfernoArt.BuildSymbols },
            new Entry { name = "Inferno_UI", w = 1024, h = 1024, build = GoldenInfernoArt.BuildUi },
            new Entry { name = "StarWheel_Symbols", w = SymbolAtlas.Width, h = SymbolAtlas.Height, build = StarWheelArt.BuildSymbols },
            new Entry { name = "StarWheel_UI", w = 1024, h = 1024, build = StarWheelArt.BuildUi },
            new Entry { name = "StarWheel_Wheel", w = 1024, h = 1024, build = f => StarWheelArt.BuildWheelFace(1024) },
            new Entry { name = "FruitCocktail_Symbols", w = SymbolAtlas.Width, h = SymbolAtlas.Height, build = FruitCocktailArt.BuildSymbols },
            new Entry { name = "FruitCocktail_UI", w = 1024, h = 1024, build = FruitCocktailArt.BuildUi },
        };

        static readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>();
        static SdfFont _font;

        public static SdfFont Font
        {
            get
            {
                if (_font != null) return _font;
                var f = TMP_Settings.defaultFontAsset;
                if (f == null) f = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
                _font = new SdfFont(f);
                return _font;
            }
        }

        public static Texture2D Get(string name)
        {
            if (_cache.TryGetValue(name, out var t) && t != null) return t;
            t = Resources.Load<Texture2D>("VideoSlots/" + name);
            if (t == null)
            {
                var e = Array.Find(All, x => x.name == name);
                Debug.LogWarning($"Video slots: '{name}' art isn't baked (Tools > Slot Machines > Bake Video Slot Art) - generating it now.");
                t = FromPixels(e.build(Font), e.w, e.h, name);
            }
            _cache[name] = t;
            return t;
        }

        public static Texture2D FromPixels(Color[] px, int w, int h, string name)
        {
            Premultiply(px);
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true, false)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4,
            };
            t.SetPixels(px);
            t.Apply(true, true);
            return t;
        }

        // Premultiplied in linear space, stored gamma-encoded: an sRGB texture
        // then filters and blends exactly right (no dark fringes, glows stay glows).
        public static void Premultiply(Color[] px)
        {
            for (int i = 0; i < px.Length; i++)
            {
                Color c = px[i];
                float a = Mathf.Clamp01(c.a);
                if (a <= 0f) { px[i] = new Color(0f, 0f, 0f, 0f); continue; }
                px[i] = new Color(
                    Mathf.LinearToGammaSpace(Mathf.GammaToLinearSpace(Mathf.Clamp01(c.r)) * a),
                    Mathf.LinearToGammaSpace(Mathf.GammaToLinearSpace(Mathf.Clamp01(c.g)) * a),
                    Mathf.LinearToGammaSpace(Mathf.GammaToLinearSpace(Mathf.Clamp01(c.b)) * a), a);
            }
        }
    }

    // Chunky outlined digits (white, tinted by vertex color: gold, neon...)
    // for every number on the screens. Drawn as quads, so they clip with the
    // reels and need no text components.
    public static class Digits
    {
        public const string Glyphs = "0123456789,.KMx/";
        public const int Cols = 8, CellW = 128, CellH = 256;
        public const int Width = Cols * CellW, Height = CellH * 2;
        const float Baseline = 0.276f, Cap = 0.448f;

        static float Advance(char ch)
        {
            switch (ch)
            {
                case ',': case '.': return 0.2f;
                case 'K': return 0.44f;
                case 'M': return 0.52f;
                case 'x': return 0.36f;
                case '/': return 0.3f;
                case ' ': return 0.2f;
                default: return 0.4f;
            }
        }

        public static Rect Uv(int index)
        {
            int col = index % Cols, row = index / Cols;
            return new Rect((col * CellW + 1f) / Width, ((1 - row) * CellH + 1f) / Height, (CellW - 2f) / Width, (CellH - 2f) / Height);
        }

        public static Color[] Build(SdfFont font)
        {
            var atlas = new Color[Width * Height];
            for (int i = 0; i < Glyphs.Length; i++)
            {
                var c = new PaintCanvas(CellW, CellH);
                float w = (float)CellW / CellH;
                var t = font.Text(c.W, c.H, Glyphs[i].ToString(), new Rect(0.085f, 0.1f, w - 0.17f, 0.8f), 0f, 0.56f).Grow(0.016f);
                c.Shadow(t.Grow(0.04f), new Vector2(0.008f, -0.016f), 0.02f, new Color(0f, 0f, 0f, 0.65f));
                c.Fill(t, K.Ink, 0.04f);
                c.Fill(t, new Color(0.55f, 0.55f, 0.55f), 0.024f);
                c.Bevel(t.Grow(0.024f), 0.016f, 0.7f, K.Light);
                c.Fill(t, K.Ink, 0.008f);
                c.Fill(t, (p, d) => Color.Lerp(new Color(0.78f, 0.78f, 0.78f), Color.white, Mathf.InverseLerp(Baseline, Baseline + Cap, p.y)));
                c.Bevel(t, 0.022f, 0.6f, K.Light);
                int col = i % Cols, row = i / Cols;
                c.CopyTo(atlas, Width, col * CellW, (1 - row) * CellH);
            }
            return atlas;
        }

        public static float Measure(string s, float cap)
        {
            float h = cap / Cap, w = 0f;
            foreach (char ch in s) w += Advance(ch) * h;
            return w;
        }

        // align: 0 = left, 0.5 = centered, 1 = right.
        public static void Draw(QuadBatch b, string s, Vector2 at, float cap, Color top, Color bottom, float align = 0.5f, Rect? clip = null, float alpha = 1f, float scaleX = 1f)
        {
            if (string.IsNullOrEmpty(s)) return;
            float h = cap / Cap, qw = h * CellW / CellH;
            float total = Measure(s, cap) * scaleX;
            float x = at.x - total * align;
            float y0 = at.y - h * 0.5f;
            // Extrapolate the tint so it spans baseline -> cap top.
            Color qb = Color.LerpUnclamped(bottom, top, -Baseline / Cap), qt = Color.LerpUnclamped(bottom, top, (1f - Baseline) / Cap);
            qb = Clamp(qb); qt = Clamp(qt);
            qb.a = qt.a = alpha * top.a;
            Rect cr = clip ?? QuadBatch.Unclipped;
            foreach (char ch in s)
            {
                float adv = Advance(ch) * h * scaleX;
                int gi = Glyphs.IndexOf(ch);
                if (gi >= 0)
                {
                    float cx = x + adv * 0.5f;
                    b.Rect(new Rect(cx - qw * 0.5f * scaleX, y0, qw * scaleX, h), Uv(gi), qt, qb, cr);
                }
                x += adv;
            }
        }

        static Color Clamp(Color c) => new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), c.a);

        // 1234567 -> "1,234,567"; compact = shorten past 6 digits (12.5M).
        public static string Format(long v, bool compact = false)
        {
            if (compact)
            {
                if (v >= 10_000_000) return (v / 1_000_000) + "M";
                if (v >= 1_000_000) return (v / 100_000 / 10f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "M";
                if (v >= 100_000) return (v / 1000) + "K";
            }
            return v.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    // Small helpers shared by the machines.
    public static class SlotEase
    {
        public static float OutCubic(float t) { t = Mathf.Clamp01(t) - 1f; return t * t * t + 1f; }
        public static float InOut(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
        public static float OutBack(float t, float s = 1.70158f) { t = Mathf.Clamp01(t) - 1f; return t * t * ((s + 1f) * t + s) + 1f; }
        public static float OutElastic(float t)
        {
            t = Mathf.Clamp01(t);
            if (t <= 0f || t >= 1f) return t;
            return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * (2f * Mathf.PI / 3f)) + 1f;
        }
        public static float Pulse(float speed, float phase = 0f) => 0.5f + 0.5f * Mathf.Sin(Time.time * speed + phase);
    }
}
