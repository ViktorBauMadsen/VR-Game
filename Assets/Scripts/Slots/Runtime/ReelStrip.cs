using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace VideoSlots
{
    // A symbol on a reel: id plus a number some symbols carry (orb credit
    // value, wild multiplier).
    public struct Sym
    {
        public int id, value;
        public Sym(int id, int value = 0) { this.id = id; this.value = value; }
    }

    // One reel column (or, with rows = 1, one Hold & Spin cell). Symbols run
    // top to bottom on a tape: syms[0] is just above the window, syms[1..rows]
    // are the visible rows, syms[rows + 1] just below. offset (in cells) is
    // how far the tape has moved down from rest.
    // Spin: a small wind-up, accelerate, run; Stop(targets) feeds the result
    // in at the top, decelerates so it lands exactly on the rows, overshoots
    // a little and springs back.
    public sealed class ReelStrip
    {
        public enum State { Idle, WindUp, Spinning, Stopping, Bounce }

        public readonly int rows;
        public Rect window;
        public float spinSpeed = 22f; // cells per second
        public Func<Sym> filler;
        public readonly Sym[] syms;
        public float offset, velocity;
        public State state = State.Idle;
        public float landTime = -10f;
        public event Action Ticked, Landed;

        readonly Queue<Sym> _queue = new Queue<Sym>();
        float _t, _delay, _remain, _decelD, _decelT, _decelPrev, _bounceFrom;
        bool _stopping;

        const float Overshoot = 0.2f, BounceTime = 0.2f, WindUpDepth = 0.18f, WindUpTime = 0.12f, AccelTime = 0.16f, DecelDistance = 1.3f;

        public ReelStrip(int rows, Rect window, Func<Sym> filler)
        {
            this.rows = rows;
            this.window = window;
            this.filler = filler;
            syms = new Sym[rows + 2];
            for (int i = 0; i < syms.Length; i++) syms[i] = filler();
        }

        public float CellH => window.height / rows;
        public bool Moving => state != State.Idle;
        public bool StopRequested => _stopping;
        public bool Blurred => Mathf.Abs(velocity) > 10f;
        public Sym Visible(int row) => syms[row + 1];

        // Center y of tape slot k (0 = above the window) right now.
        public float SlotY(int k) => window.yMax - (k - 0.5f) * CellH - offset * CellH;

        public void Set(IList<Sym> topToBottom)
        {
            syms[0] = filler();
            for (int r = 0; r < rows; r++) syms[r + 1] = topToBottom[r];
            syms[rows + 1] = filler();
            offset = velocity = 0f;
            state = State.Idle;
            _stopping = false;
            _queue.Clear();
        }

        public void SetRow(int row, Sym s) => syms[row + 1] = s;

        public void Begin(float delay)
        {
            state = State.WindUp;
            _t = 0f;
            _delay = delay;
            _stopping = false;
            _queue.Clear();
            velocity = 0f;
        }

        public void Stop(IList<Sym> topToBottom)
        {
            if (_stopping || state == State.Idle) return;
            _queue.Clear();
            for (int r = rows - 1; r >= 0; r--) _queue.Enqueue(topToBottom[r]);
            _queue.Enqueue(filler());
            _remain = _queue.Count - offset;
            _stopping = true;
        }

        public void Update(float dt)
        {
            switch (state)
            {
                case State.Idle:
                    return;

                case State.WindUp:
                {
                    _t += dt;
                    if (_t < _delay) return;
                    float u = (_t - _delay) / WindUpTime;
                    if (u < 1f)
                    {
                        Move(-WindUpDepth * Mathf.Sin(u * Mathf.PI * 0.5f) - offset);
                        return;
                    }
                    state = State.Spinning;
                    velocity = 0f;
                    return;
                }

                case State.Spinning:
                {
                    velocity = Mathf.Min(spinSpeed, velocity + spinSpeed / AccelTime * dt);
                    float step = velocity * dt;
                    if (_stopping && _remain <= DecelDistance + step)
                    {
                        state = State.Stopping;
                        _t = 0f;
                        _decelPrev = 0f;
                        _decelD = _remain + Overshoot;
                        _decelT = Mathf.Clamp(2f * _decelD / Mathf.Max(velocity, 1f), 0.07f, 0.45f);
                        return;
                    }
                    Move(step);
                    return;
                }

                case State.Stopping:
                {
                    _t += dt;
                    float u = Mathf.Min(1f, _t / _decelT);
                    float traveled = _decelD * (1f - (1f - u) * (1f - u));
                    velocity = 2f * _decelD / _decelT * (1f - u);
                    Move(traveled - _decelPrev);
                    _decelPrev = traveled;
                    if (u >= 1f)
                    {
                        state = State.Bounce;
                        _t = 0f;
                        _bounceFrom = offset;
                        velocity = 0f;
                        landTime = Time.time;
                        Landed?.Invoke();
                    }
                    return;
                }

                case State.Bounce:
                {
                    _t += dt;
                    float u = Mathf.Min(1f, _t / BounceTime);
                    offset = _bounceFrom * (1f - SlotEase.InOut(u));
                    if (u >= 1f)
                    {
                        offset = 0f;
                        state = State.Idle;
                        _stopping = false;
                    }
                    return;
                }
            }
        }

        void Move(float delta)
        {
            offset += delta;
            _remain -= delta;
            while (offset >= 1f)
            {
                offset -= 1f;
                for (int k = syms.Length - 1; k > 0; k--) syms[k] = syms[k - 1];
                syms[0] = _queue.Count > 0 ? _queue.Dequeue() : filler();
                Ticked?.Invoke();
            }
        }
    }

    public enum Layer { Bg, ReelBg, Symbols, Lines, SymbolsTop, FxTop, Digits, Ui, OverlayFx, OverlayUi, OverlayDigits, OverlayTop, Count }

    // One flat display on the cabinet: a transform whose local XY plane is
    // the screen (x = player's right, y = up, -z = toward the player), with
    // one quad batch per layer.
    public sealed class ScreenSurface
    {
        public readonly Transform root;
        public readonly Vector2 size;
        readonly QuadBatch[] _b = new QuadBatch[(int)Layer.Count];
        readonly Func<Layer, Texture> _tex;
        static readonly Dictionary<(Texture, int), Material> _mats = new Dictionary<(Texture, int), Material>();

        public ScreenSurface(string name, Transform parent, Vector3 localPos, Quaternion localRot, Vector2 size, Func<Layer, Texture> tex)
        {
            root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.localPosition = localPos;
            root.localRotation = localRot;
            this.size = size;
            _tex = tex;
        }

        public Rect Bounds => new Rect(-size.x * 0.5f, -size.y * 0.5f, size.x, size.y);

        public QuadBatch this[Layer l]
        {
            get
            {
                int i = (int)l;
                if (_b[i] != null) return _b[i];
                var tex = _tex(l);
                if (!_mats.TryGetValue((tex, i), out var m) || m == null) _mats[(tex, i)] = m = SlotMaterials.Create(tex, i);
                return _b[i] = new QuadBatch(root, l.ToString(), m, i * 0.0004f);
            }
        }

        public void Begin()
        {
            foreach (var b in _b) b?.Clear();
        }

        public void End()
        {
            foreach (var b in _b) b?.Apply();
        }

        // World-space TextMeshPro in a rect of this screen (drawn above the quads).
        public TextMeshPro Text(string name, Rect rect, Color color, FontStyles style = FontStyles.Bold,
            TextAlignmentOptions align = TextAlignmentOptions.Center, float outline = 0.22f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(rect.center.x, rect.center.y, -0.0055f);
            var t = go.AddComponent<TextMeshPro>();
            t.rectTransform.sizeDelta = rect.size;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.enableAutoSizing = true;
            t.fontSizeMin = 0.02f;
            t.fontSizeMax = rect.height * 8.5f;
            t.alignment = align;
            t.fontStyle = style;
            t.color = color;
            t.text = "";
            if (outline > 0f)
            {
                t.outlineWidth = outline;
                t.outlineColor = new Color32(10, 4, 2, 255);
            }
            t.fontMaterial.renderQueue = SlotMaterials.QueueBase + (int)Layer.Count + 1;
            var r = t.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return t;
        }
    }

    // Simple screen-space particles (embers, sparkles, coins, stars).
    public struct Particle
    {
        public ScreenSurface surface;
        public Layer layer;
        public int fx;
        public Vector2 pos, vel;
        public float size, life, age, rot, spin, gravity, drag, flip;
        public Color color;
        public bool additive;
        public Rect clip;
    }
}
