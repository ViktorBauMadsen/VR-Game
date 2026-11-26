using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using VideoSlots;

// Base for the video slots (GoldenInfernoSlot, StarWheelSlot) that replace
// the classic reels on some of the cabinets. Builds the screens over the
// cabinet's own screen areas at runtime (reel panel + the two sections of
// the upper screen), runs the 5x3 reels, and has the shared presentation:
// meters, win cycling, banners, particles, the BIG/MEGA/EPIC WIN overlay,
// sound and the cabinet's light trim.
// Every spin is played from a seed (VideoSlots.*Math), so another player's
// spin replays exactly from (seed, bet).
public abstract class VideoSlotMachine : SlotMachineBase
{
    public static event Action<VideoSlotMachine, int, int> LocalSpinStarted;

    [Header("Cabinet screen (machine-local, meters)")]
    [SerializeField] protected Vector3 reelPanelBottom = new Vector3(0f, 1.187f, 0.158f);
    [SerializeField] protected Vector3 reelPanelTop = new Vector3(0f, 1.701f, 0.079f);
    [SerializeField] protected Vector3 upperPanelBottom = new Vector3(0f, 1.701f, 0.079f);
    [SerializeField] protected Vector3 upperPanelTop = new Vector3(0f, 2.354f, 0.216f);
    [Tooltip("Heights (machine y) of the chrome bars on the upper screen: top of the lower bar, bottom and top of the middle bar.")]
    [SerializeField] protected Vector3 upperBars = new Vector3(1.732f, 2.04f, 2.10f);
    [SerializeField] protected float screenWidth = 0.584f;
    [Tooltip("How far the reel screen sits in front of the old screen surface (clears the old reel dividers).")]
    [SerializeField] protected float screenLift = 0.012f;
    [Tooltip("The old classic-slot screen pieces, hidden at runtime (filled in by the convert tool).")]
    [SerializeField] protected GameObject[] hideOnStart;
    [SerializeField] protected Renderer cabinetRenderer;

    [Header("Sound")]
    [SerializeField] protected AudioSource audioSource;
    [SerializeField] protected AudioClip reelTickClip;
    [SerializeField] protected AudioClip reelStopClip;
    [SerializeField] protected AudioClip winClip;
    [SerializeField] protected AudioClip bigWinClip;
    [Tooltip("Feature trigger chime (the casino 'Win Bonus Chimes').")]
    [SerializeField] protected AudioClip bonusChimeClip;
    [Tooltip("Rising chime (the casino 'Ascending Win Chime').")]
    [SerializeField] protected AudioClip ascendingChimeClip;
    [SerializeField, Range(0f, 1f)] protected float volume = 0.9f;

    // ------------------------------------------------------------- layout
    // Reel screen, in its own plane (meters, origin at the panel center).
    // The cabinet's chrome bars cover the panel's top and bottom edge.
    protected static readonly Rect ReelWindow = new Rect(-0.272f, -0.166f, 0.544f, 0.333f);
    protected static readonly Rect HeaderRect = new Rect(-0.272f, 0.172f, 0.544f, 0.05f);
    protected static readonly Rect MeterRect = new Rect(-0.286f, -0.233f, 0.572f, 0.059f);
    protected const int Reels = 5, Rows = 3;
    protected static float CellW => ReelWindow.width / Reels;
    protected static float CellH => ReelWindow.height / Rows;
    protected static Vector2 CellCenter(int reel, int row) => new Vector2(ReelWindow.xMin + (reel + 0.5f) * CellW, ReelWindow.yMax - (row + 0.5f) * CellH);
    protected static Vector2 CellCenter(int cell) => CellCenter(SlotGrid.ReelOf(cell), SlotGrid.RowOf(cell));
    protected static Rect ColumnRect(int reel) => new Rect(ReelWindow.xMin + reel * CellW, ReelWindow.yMin, CellW, ReelWindow.height);

    // ------------------------------------------------------------- state
    protected Transform screenRoot;
    protected ScreenSurface reelScreen, midScreen, topScreen;
    protected readonly List<ScreenSurface> surfaces = new List<ScreenSurface>();
    protected ReelStrip[] reels;
    protected Texture symbolTex, uiTex, fxTex, digitTex;
    protected int curBet;
    protected long winMeter, spinWin;
    protected bool slam, isLocalSpin;
    protected int anticipatingReel = -1;
    protected float anticipationStart;
    protected bool built;
    bool _busy;

    TextMeshPro _header;
    readonly TextMeshPro[] _meterLabels = new TextMeshPro[3];
    string _headerText = "";
    float _lastTick = -1f;

    readonly List<Particle> _particles = new List<Particle>();
    readonly List<Banner> _banners = new List<Banner>();

    AudioSource[] _sfx;
    int _sfxNext;
    protected enum Loop { Bed, Fx, Fx2 }
    readonly AudioSource[] _loops = new AudioSource[3];
    readonly float[] _loopTarget = new float[3];

    Material _trimMat, _bodyMat;
    float _trimFlashUntil;
    Color _trimFlashColor;

    // ------------------------------------------------------------- theme
    protected abstract string SymbolTextureName { get; }
    protected abstract string UiTextureName { get; }
    protected abstract UiRegion BigWinUi { get; }
    protected abstract UiRegion MegaWinUi { get; }
    protected abstract UiRegion EpicWinUi { get; }
    protected abstract UiRegion TotalWinUi { get; }
    protected abstract Color TrimColor { get; }
    protected abstract Color CabinetTint { get; }
    protected abstract Color AccentColor { get; }
    protected virtual Color MeterPanelTop => new Color(0.16f, 0.05f, 0.05f, 0.95f);
    protected virtual Color MeterPanelBottom => new Color(0.05f, 0.01f, 0.01f, 0.95f);
    protected virtual Color ValueTop => new Color(1f, 0.97f, 0.75f);
    protected virtual Color ValueBottom => new Color(1f, 0.62f, 0.12f);
    protected virtual Color LabelColor => new Color(1f, 0.85f, 0.45f);

    protected abstract Sym Filler(int reel);
    protected abstract IEnumerator PlaySpin(int seed, int bet);
    protected abstract void DrawScreens(float dt);
    protected virtual void OnBuilt() { }

    public override bool IsSpinning => _busy;

    // ================================================================ setup
    protected virtual void Awake()
    {
        betAmount = ClampBet(betAmount);
        curBet = betAmount;
        if (reelTickClip == null) reelTickClip = Resources.Load<AudioClip>("SlotSounds/SlotReelTick");
        if (reelStopClip == null) reelStopClip = Resources.Load<AudioClip>("SlotSounds/SlotReelStop");
        if (winClip == null) winClip = Resources.Load<AudioClip>("SlotSounds/SlotWin");
        if (bigWinClip == null) bigWinClip = Resources.Load<AudioClip>("SlotSounds/SlotBigWin");
    }

    protected virtual void Start()
    {
        if (hideOnStart != null)
            foreach (var g in hideOnStart)
                if (g != null) g.SetActive(false);

        BuildScreens();
        SetupCabinet();
        SetupAudio();
        BuildBetPanelIfWanted();
        built = true;
        OnBuilt();
    }

    protected virtual void OnDestroy()
    {
        if (screenRoot != null) Destroy(screenRoot.gameObject);
    }

    protected virtual void OnEnable() { if (screenRoot != null) screenRoot.gameObject.SetActive(true); }
    protected virtual void OnDisable() { if (screenRoot != null) screenRoot.gameObject.SetActive(false); }

    Texture TexFor(Layer l)
    {
        switch (l)
        {
            case Layer.Symbols: case Layer.SymbolsTop: return symbolTex;
            case Layer.Digits: case Layer.OverlayDigits: return digitTex;
            case Layer.Ui: case Layer.OverlayUi: return uiTex;
            default: return fxTex;
        }
    }

    void BuildScreens()
    {
        symbolTex = SlotTextures.Get(SymbolTextureName);
        uiTex = SlotTextures.Get(UiTextureName);
        fxTex = SlotTextures.Get("Fx");
        digitTex = SlotTextures.Get("Digits");

        // Unparented (like the bet panel): a scaled cabinet would skew a tilted child.
        screenRoot = new GameObject(name + " VideoSlot (runtime)").transform;
        screenRoot.SetPositionAndRotation(transform.position, transform.rotation);
        Vector3 ls = transform.lossyScale;
        screenRoot.localScale = Vector3.one * ((Mathf.Abs(ls.x) + Mathf.Abs(ls.y) + Mathf.Abs(ls.z)) / 3f);

        reelScreen = MakeSurface("Reel Screen", reelPanelBottom, reelPanelTop, screenLift);
        midScreen = MakeSurface("Info Screen", UpperAt(upperBars.x), UpperAt(upperBars.y), 0.006f);
        topScreen = MakeSurface("Topper Screen", UpperAt(upperBars.z), upperPanelTop, 0.006f);

        reels = new ReelStrip[Reels];
        for (int r = 0; r < Reels; r++)
        {
            int reel = r;
            var strip = new ReelStrip(Rows, ColumnRect(r), () => Filler(reel));
            strip.Ticked += () => OnReelTick(reel);
            strip.Landed += () => OnReelLanded(reel);
            reels[r] = strip;
        }

        _header = reelScreen.Text("Header", HeaderRect, LabelColor);
        string[] labels = { "CREDIT", "BET", "WIN" };
        for (int i = 0; i < 3; i++)
        {
            Rect box = MeterBox(i);
            _meterLabels[i] = reelScreen.Text(labels[i], new Rect(box.x, box.yMax - 0.019f, box.width, 0.014f), LabelColor, FontStyles.Bold, TextAlignmentOptions.Center, 0.15f);
            _meterLabels[i].text = labels[i];
            _meterLabels[i].characterSpacing = 8f;
        }
    }

    protected ScreenSurface MakeSurface(string n, Vector3 bottom, Vector3 top, float lift)
    {
        Vector3 up = (top - bottom).normalized;
        Vector3 normal = new Vector3(0f, -up.z, up.y);
        var rot = Quaternion.LookRotation(-normal, up);
        Vector3 c = (bottom + top) * 0.5f + normal * lift;
        var s = new ScreenSurface(n, screenRoot, c, rot, new Vector2(screenWidth, (top - bottom).magnitude), TexFor);
        surfaces.Add(s);
        return s;
    }

    protected ScreenSurface MakeSurface(string n, Vector3 localPos, Quaternion localRot, Vector2 size)
    {
        var s = new ScreenSurface(n, screenRoot, localPos, localRot, size, TexFor);
        surfaces.Add(s);
        return s;
    }

    protected Vector3 UpperAt(float y)
    {
        float u = Mathf.InverseLerp(upperPanelBottom.y, upperPanelTop.y, y);
        return Vector3.LerpUnclamped(upperPanelBottom, upperPanelTop, u);
    }

    void SetupCabinet()
    {
        if (cabinetRenderer == null) cabinetRenderer = GetComponent<MeshRenderer>();
        if (cabinetRenderer == null) return;
        var mats = cabinetRenderer.materials;
        foreach (var m in mats)
        {
            if (m.name.StartsWith("Emission")) _trimMat = m;
            else if (m.name.StartsWith("Color")) _bodyMat = m;
        }
        if (_bodyMat != null && _bodyMat.HasProperty("_BaseColor")) _bodyMat.SetColor("_BaseColor", CabinetTint);
        if (_trimMat != null) _trimMat.EnableKeyword("_EMISSION");
        cabinetRenderer.materials = mats;
    }

    void SetupAudio()
    {
        SpatialAudio.Setup(audioSource);
        _sfx = new AudioSource[8];
        for (int i = 0; i < _sfx.Length; i++) _sfx[i] = NewSource();
        for (int i = 0; i < _loops.Length; i++)
        {
            _loops[i] = NewSource();
            _loops[i].loop = true;
        }
    }

    AudioSource NewSource()
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.playOnAwake = false;
        SpatialAudio.Setup(s); // only heard near the machine
        if (audioSource != null) s.outputAudioMixerGroup = audioSource.outputAudioMixerGroup;
        return s;
    }

    // ================================================================ spin
    public override void Spin()
    {
        if (!built) return;
        if (_busy)
        {
            OnLeverWhileBusy();
            return;
        }
        if (PlayerBalance.Instance == null)
        {
            Debug.LogWarning($"{name}: no PlayerBalance found in the scene.");
            return;
        }
        int bet = betAmount;
        if (!PlayerBalance.Instance.TrySpend(bet))
        {
            RaiseSpinRejected();
            return;
        }
        int seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        if (_debugVariant >= 0)
        {
            for (int i = 0; i < 200000 && !IsDebugBonusSeed(seed, _debugVariant); i++)
                seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            _debugVariant = -1;
        }
        LocalSpinStarted?.Invoke(this, seed, bet);
        StartCoroutine(Run(seed, bet, true));
    }

    // Another player spun this machine: same spin, no money changes here.
    public void PlayRemoteSpin(int seed, int bet)
    {
        if (!built || _busy) return;
        StartCoroutine(Run(seed, bet, false));
    }

    IEnumerator Run(int seed, int bet, bool local)
    {
        _busy = true;
        slam = false;
        isLocalSpin = local;
        curBet = bet;
        RaiseSpinStateChanged();
        ClearWins();
        winMeter = 0;
        spinWin = 0;

        yield return PlaySpin(seed, bet);

        if (local && spinWin > 0 && PlayerBalance.Instance != null)
            PlayerBalance.Instance.Payout((int)Math.Min(int.MaxValue, spinWin), bet);
        curBet = betAmount;
        slam = false;
        _busy = false;
        RaiseSpinStateChanged();
    }

    // Lever pulled while busy: skip the big win count-up, or slam-stop the reels.
    protected virtual void OnLeverWhileBusy()
    {
        if (_big != null)
        {
            _big.skip = true;
            return;
        }
        if (isLocalSpin) slam = true;
    }

    protected override void OnBetChanged()
    {
        curBet = betAmount;
        Sfx(reelTickClip, 0.8f, 1.1f);
    }

    protected long Credits(double timesBet, int bet) => (long)Math.Round(timesBet * bet);

    // Waits, cut short by a slam (lever pulled again).
    protected IEnumerator Wait(float seconds, bool skippable = true)
    {
        float end = Time.time + seconds;
        while (Time.time < end && !(skippable && slam)) yield return null;
    }

    // Spins all 5 reels and lands them on target(reel, row). anticipate(reel):
    // the reels before it promise a feature - spin it longer, with the
    // suspense effects.
    protected IEnumerator SpinReels(Func<int, int, Sym> target, Func<int, bool> anticipate, float baseTime = 0.65f, float stagger = 0.2f)
    {
        for (int r = 0; r < Reels; r++)
        {
            reels[r].spinSpeed = 22f;
            reels[r].Begin(r * 0.05f);
        }
        float start = Time.time;
        var col = new Sym[Rows];
        for (int r = 0; r < Reels; r++)
        {
            float due = start + baseTime + r * stagger;
            bool ant = !slam && anticipate != null && anticipate(r);
            if (ant)
            {
                // Earlier reels first, then the long suspense spin.
                while (r > 0 && reels[r - 1].Moving && !slam) yield return null;
                anticipatingReel = r;
                anticipationStart = Time.time;
                reels[r].spinSpeed = 30f;
                StartLoop(Loop.Fx, SlotSynth.Anticipation(), 0.75f, false);
                due = Mathf.Max(due, Time.time) + 1.9f;
            }
            while (Time.time < due && !slam) yield return null;
            for (int row = 0; row < Rows; row++) col[row] = target(r, row);
            reels[r].Stop(col);
            if (ant)
            {
                while (reels[r].state != ReelStrip.State.Bounce && reels[r].state != ReelStrip.State.Idle) yield return null;
                anticipatingReel = -1;
                StopLoop(Loop.Fx, 6f);
            }
        }
        anticipatingReel = -1;
        StopLoop(Loop.Fx, 6f);
        while (AnyReelMoving()) yield return null;
    }

    protected bool AnyReelMoving()
    {
        foreach (var r in reels) if (r.Moving) return true;
        return false;
    }

    protected virtual void OnReelTick(int reel)
    {
        if (Time.time - _lastTick < 0.045f) return;
        _lastTick = Time.time;
        Sfx(reelTickClip, 0.18f, UnityEngine.Random.Range(0.95f, 1.05f));
    }

    protected virtual void OnReelLanded(int reel)
    {
        Sfx(reelStopClip, 0.75f, 1f + reel * 0.05f);
    }

    // ================================================================ sound
    protected void Sfx(AudioClip clip, float vol = 1f, float pitch = 1f)
    {
        if (clip == null || _sfx == null) return;
        var s = _sfx[_sfxNext++ % _sfx.Length];
        s.pitch = pitch;
        s.PlayOneShot(clip, vol * volume);
    }

    protected void StartLoop(Loop slot, AudioClip clip, float vol, bool loop = true)
    {
        var s = _loops[(int)slot];
        if (s == null || clip == null) return;
        if (s.clip != clip || !s.isPlaying)
        {
            s.clip = clip;
            s.loop = loop;
            s.volume = vol * volume;
            s.pitch = 1f;
            s.Play();
        }
        _loopTarget[(int)slot] = vol * volume;
    }

    protected void StopLoop(Loop slot, float fadeSpeed = 2f)
    {
        _loopTarget[(int)slot] = -fadeSpeed; // negative = fading out at this speed
    }

    protected void SetLoopPitch(Loop slot, float pitch)
    {
        var s = _loops[(int)slot];
        if (s != null) s.pitch = pitch;
    }

    void UpdateLoops(float dt)
    {
        for (int i = 0; i < _loops.Length; i++)
        {
            var s = _loops[i];
            if (s == null || !s.isPlaying) continue;
            float target = _loopTarget[i];
            if (target < 0f)
            {
                s.volume = Mathf.MoveTowards(s.volume, 0f, -target * dt);
                if (s.volume <= 0.001f) s.Stop();
            }
            else s.volume = Mathf.MoveTowards(s.volume, target, dt * 2f);
        }
    }

    // ================================================================ frame
    protected virtual void LateUpdate()
    {
        if (!built) return;
        float dt = Time.deltaTime;
        UpdateReels(dt);

        foreach (var s in surfaces) s.Begin();
        DrawScreens(dt);
        DrawMeters();
        DrawBanners();
        DrawBigWin(dt);
        UpdateParticles(dt);
        foreach (var s in surfaces) s.End();

        UpdateTrim();
        UpdateLoops(dt);
    }

    protected virtual void UpdateReels(float dt)
    {
        foreach (var r in reels) r.Update(dt);
    }

    protected void SetHeader(string text)
    {
        if (_header == null || text == _headerText) return;
        _headerText = text;
        _header.text = text;
    }

    void UpdateTrim()
    {
        if (_trimMat == null) return;
        float pulse = 0.8f + 0.2f * Mathf.Sin(Time.time * 1.7f);
        Color c = TrimColor * (1.4f * pulse);
        if (Time.time < _trimFlashUntil)
        {
            float k = Mathf.Clamp01((_trimFlashUntil - Time.time) * 2f);
            float strobe = 0.5f + 0.5f * Mathf.Sin(Time.time * 18f);
            c = Color.Lerp(c, _trimFlashColor * (1.5f + 2.5f * strobe), k);
        }
        _trimMat.SetColor("_EmissionColor", c);
    }

    protected void FlashTrim(Color color, float seconds)
    {
        _trimFlashColor = color;
        _trimFlashUntil = Mathf.Max(_trimFlashUntil, Time.time + seconds);
    }

    // ================================================================ drawing helpers
    protected static Rect White => FxAtlas.Uv(FxAtlas.White);

    protected static void DrawSymbol(QuadBatch b, int art, Vector2 c, float size, bool blurred, Color tint, Rect clip)
    {
        if (art < 0) return;
        b.Rect(new Rect(c.x - size * 0.5f, c.y - size * 0.5f, size, size), SymbolAtlas.Uv(art, blurred), tint, tint, clip);
    }

    // Metallic border: dark rim, gradient body, light inner edge.
    protected static void Border(QuadBatch b, Rect r, float t, Color top, Color bottom)
    {
        Rect uv = White;
        float h = r.height + 2f * t;
        Color TopAt(float y) => Color.Lerp(bottom, top, Mathf.InverseLerp(r.yMin - t, r.yMax + t, y));
        b.Rect(new Rect(r.xMin - t, r.yMax, r.width + 2f * t, t), uv, top, TopAt(r.yMax));
        b.Rect(new Rect(r.xMin - t, r.yMin - t, r.width + 2f * t, t), uv, TopAt(r.yMin), bottom);
        b.Rect(new Rect(r.xMin - t, r.yMin, t, r.height), uv, TopAt(r.yMax), TopAt(r.yMin));
        b.Rect(new Rect(r.xMax, r.yMin, t, r.height), uv, TopAt(r.yMax), TopAt(r.yMin));
    }

    protected static void GoldFrame(QuadBatch b, Rect r, float t)
    {
        Border(b, Grow(r, t), 0.0025f, new Color(0.2f, 0.08f, 0.02f), new Color(0.1f, 0.03f, 0.01f));
        Border(b, r, t, new Color(1f, 0.93f, 0.6f), new Color(0.72f, 0.4f, 0.06f));
        Border(b, Grow(r, -0.0015f), 0.0015f, new Color(1f, 1f, 0.85f, 0.9f), new Color(0.55f, 0.3f, 0.05f, 0.9f));
    }

    protected static Rect Grow(Rect r, float d) => new Rect(r.xMin - d, r.yMin - d, r.width + 2f * d, r.height + 2f * d);

    protected static Rect MeterBox(int i)
    {
        float gap = 0.008f, w = (MeterRect.width - gap * 2f) / 3f;
        return new Rect(MeterRect.xMin + i * (w + gap), MeterRect.yMin, w, MeterRect.height);
    }

    void DrawMeters()
    {
        var bg = reelScreen[Layer.ReelBg];
        var dg = reelScreen[Layer.Digits];
        long credit = PlayerBalance.Instance != null ? PlayerBalance.Instance.Balance : 0;
        long[] values = { credit, curBet, winMeter };
        for (int i = 0; i < 3; i++)
        {
            Rect box = MeterBox(i);
            bg.Rect(box, White, MeterPanelTop, MeterPanelBottom);
            bg.Rect(new Rect(box.x, box.yMax - 0.0015f, box.width, 0.0015f), White, Pal.A(AccentColor, 0.8f), Pal.A(AccentColor, 0.8f));
            bool hot = i == 2 && winMeter > 0;
            Color top = ValueTop, bottom = ValueBottom;
            if (hot)
            {
                float p = SlotEase.Pulse(8f);
                top = Color.Lerp(top, Color.white, p * 0.5f);
                reelScreen[Layer.FxTop].Quad(new Vector2(box.center.x, box.yMin + 0.017f), new Vector2(box.width * 1.1f, 0.05f), FxAtlas.Uv(FxAtlas.Glow), Pal.A(AccentColor, 0.25f + 0.2f * p), 0f, true);
            }
            Digits.Draw(dg, Digits.Format(values[i], i == 0), new Vector2(box.center.x, box.yMin + 0.0175f), 0.019f, top, bottom);
        }
    }

    // ================================================================ particles
    protected void Emit(ScreenSurface s, Layer layer, int fx, Vector2 pos, Vector2 vel, float size, float life, Color color,
        bool additive = true, float gravity = 0f, float drag = 0f, float spin = 0f, float flip = 0f, Rect? clip = null)
    {
        if (_particles.Count > 600) return;
        _particles.Add(new Particle
        {
            surface = s, layer = layer, fx = fx, pos = pos, vel = vel, size = size, life = life, color = color,
            additive = additive, gravity = gravity, drag = drag, spin = spin, flip = flip,
            rot = UnityEngine.Random.Range(0f, 360f), clip = clip ?? s.Bounds,
        });
    }

    protected void Burst(ScreenSurface s, Vector2 at, int count, Color color, float speed = 0.25f, float size = 0.03f, int fx = FxAtlas.Sparkle, Layer layer = Layer.FxTop)
    {
        for (int i = 0; i < count; i++)
        {
            Vector2 dir = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(0.3f, 1f) * speed;
            Emit(s, layer, fx, at, dir, size * UnityEngine.Random.Range(0.5f, 1.2f), UnityEngine.Random.Range(0.4f, 0.9f), color, true, 0.15f, 2.5f, UnityEngine.Random.Range(-200f, 200f));
        }
    }

    void UpdateParticles(float dt)
    {
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.age += dt;
            if (p.age >= p.life)
            {
                _particles[i] = _particles[_particles.Count - 1];
                _particles.RemoveAt(_particles.Count - 1);
                continue;
            }
            p.vel.y -= p.gravity * dt;
            p.vel /= 1f + p.drag * dt;
            p.pos += p.vel * dt;
            p.rot += p.spin * dt;
            _particles[i] = p;
            if (!p.clip.Contains(p.pos)) continue;
            float u = p.age / p.life;
            float a = Mathf.Min(1f, p.age / 0.08f) * (1f - u * u);
            Color c = p.color;
            c.a *= a;
            float w = p.size;
            if (p.flip > 0f) w *= Mathf.Abs(Mathf.Cos(p.age * p.flip)) * 0.85f + 0.15f;
            p.surface[p.layer].Quad(p.pos, new Vector2(w, p.size), FxAtlas.Uv(p.fx), c, p.flip > 0f ? 0f : p.rot, p.additive);
        }
    }

    // ================================================================ banners
    struct Banner
    {
        public ScreenSurface s;
        public Layer layer;
        public UiRegion region;
        public Vector2 pos;
        public float width, start, duration;
        public Color glow;
    }

    protected void ShowBanner(ScreenSurface s, UiRegion region, Vector2 pos, float width, float duration, Color glow, Layer layer = Layer.Ui)
    {
        _banners.Add(new Banner { s = s, layer = layer, region = region, pos = pos, width = width, start = Time.time, duration = duration, glow = glow });
    }

    protected void ClearBanners() => _banners.Clear();

    void DrawBanners()
    {
        for (int i = _banners.Count - 1; i >= 0; i--)
        {
            var b = _banners[i];
            float t = Time.time - b.start;
            if (t > b.duration) { _banners.RemoveAt(i); continue; }
            float pop = SlotEase.OutBack(t / 0.4f, 2.2f);
            float fade = Mathf.Clamp01((b.duration - t) / 0.3f);
            float w = b.width * pop * (1f + 0.025f * Mathf.Sin(t * 7f));
            float h = w / b.region.Aspect;
            var fxLayer = b.layer == Layer.OverlayUi ? Layer.OverlayFx : Layer.FxTop;
            b.s[fxLayer].Quad(b.pos, new Vector2(w * 1.25f, h * 2.2f), FxAtlas.Uv(FxAtlas.Glow), Pal.A(b.glow, 0.55f * fade), 0f, true);
            b.s[b.layer].Quad(b.pos, new Vector2(w, h), b.region.Uv, new Color(1f, 1f, 1f, fade));
            if (t < 0.25f)
            {
                float flash = 1f - t / 0.25f;
                b.s[fxLayer].Quad(b.pos, new Vector2(w * 1.6f, h * 3f), FxAtlas.Uv(FxAtlas.Glow), new Color(1f, 1f, 1f, flash * 0.6f), 0f, true);
            }
        }
    }

    // ================================================================ wins
    protected sealed class WinShow
    {
        public readonly List<int> cells = new List<int>();
        public int line = -1;
        public string label;
        public long amount;
        public Color color = Color.white;
    }

    protected readonly List<WinShow> winShows = new List<WinShow>();
    protected readonly WinShow allWins = new WinShow();
    protected float winShowStart = -1f;
    const float WinShowTime = 1.8f;

    protected void ClearWins()
    {
        winShows.Clear();
        allWins.cells.Clear();
        winShowStart = -1f;
    }

    protected void BeginWinCycle(string allLabel)
    {
        allWins.cells.Clear();
        allWins.label = allLabel;
        foreach (var w in winShows)
            foreach (int c in w.cells)
                if (!allWins.cells.Contains(c)) allWins.cells.Add(c);
        winShowStart = Time.time;
    }

    // The win being shown now (all wins first, then each in turn).
    protected WinShow CurrentWin(out float phase, out bool first)
    {
        phase = 0f;
        first = false;
        if (winShows.Count == 0 || winShowStart < 0f) return null;
        float t = Time.time - winShowStart;
        int n = winShows.Count == 1 ? 1 : winShows.Count + 1;
        int idx = (int)(t / WinShowTime) % n;
        phase = (t % WinShowTime) / WinShowTime;
        if (winShows.Count == 1) { first = t < WinShowTime; return winShows[0]; }
        first = idx == 0 && t < WinShowTime;
        return idx == 0 ? allWins : winShows[idx - 1];
    }

    // Count the win meter up, with coin ticks.
    protected IEnumerator RollUp(long from, long to, float seconds)
    {
        float t = 0f, nextTick = 0f;
        while (t < seconds && !(slam && isLocalSpin))
        {
            t += Time.deltaTime;
            winMeter = from + (long)((to - from) * SlotEase.OutCubic(t / seconds));
            if (t >= nextTick)
            {
                nextTick = t + 0.07f;
                Sfx(SlotSynth.CoinTick(), 0.35f, 0.9f + 0.4f * (t / seconds));
            }
            yield return null;
        }
        winMeter = to;
    }

    protected int WinTier(long credits, int bet)
    {
        double x = (double)credits / Math.Max(1, bet);
        return x >= 50 ? 3 : x >= 25 ? 2 : x >= 10 ? 1 : 0;
    }

    // ================================================================ big win
    sealed class BigWinState
    {
        public long amount, shown;
        public int bet, tier;
        public float start, tierChanged, alpha = 1f;
        public bool skip;
    }
    BigWinState _big;

    protected bool BigWinActive => _big != null;

    // BIG / MEGA / EPIC WIN: dims the reels, counts the amount up (upgrading
    // the banner as it passes each tier), coin shower. Lever skips ahead.
    protected IEnumerator BigWin(long amount, int bet)
    {
        int finalTier = Math.Max(1, WinTier(amount, bet));
        var b = new BigWinState { amount = amount, bet = bet, tier = 1, start = Time.time, tierChanged = Time.time };
        _big = b;
        float roll = 2.6f + finalTier * 1.6f;
        StartLoop(Loop.Fx2, SlotSynth.CoinShower(), 0.55f);
        Sfx(SlotSynth.BigWinSting(), 0.9f);
        Sfx(bigWinClip, 0.8f);
        FlashTrim(new Color(1f, 0.8f, 0.3f), roll + 2.5f);

        float t = 0f, nextTick = 0f, nextCoin = 0f;
        while (t < roll && !b.skip)
        {
            t += Time.deltaTime;
            float u = t / roll;
            b.shown = (long)(amount * (1f - (1f - u) * (1f - u)));
            winMeter = b.shown;
            int tier = Math.Max(1, WinTier(b.shown, bet));
            if (tier > b.tier)
            {
                b.tier = tier;
                b.tierChanged = Time.time;
                Sfx(SlotSynth.BigWinSting(), 1f, 1f + tier * 0.06f);
                Burst(reelScreen, new Vector2(0f, 0.06f), 40, new Color(1f, 0.85f, 0.4f), 0.5f, 0.05f, FxAtlas.Sparkle, Layer.OverlayTop);
            }
            if (t >= nextTick)
            {
                nextTick = t + 0.06f;
                Sfx(SlotSynth.CoinTick(), 0.3f, 0.9f + 0.5f * u);
            }
            if (t >= nextCoin)
            {
                nextCoin = t + 0.035f;
                SpawnCoin();
            }
            yield return null;
        }
        b.shown = amount;
        winMeter = amount;
        b.tier = finalTier;
        b.skip = false;
        Sfx(winClip, 0.9f);
        float hold = 0f;
        while (hold < 2.2f && !b.skip)
        {
            hold += Time.deltaTime;
            if (hold < 1.2f && Time.frameCount % 2 == 0) SpawnCoin();
            yield return null;
        }
        StopLoop(Loop.Fx2, 1.2f);
        for (float f = 0f; f < 0.35f; f += Time.deltaTime)
        {
            b.alpha = 1f - f / 0.35f;
            yield return null;
        }
        _big = null;
    }

    void SpawnCoin()
    {
        var s = reelScreen;
        Vector2 pos = new Vector2(UnityEngine.Random.Range(-0.27f, 0.27f), 0.24f);
        Vector2 vel = new Vector2(UnityEngine.Random.Range(-0.08f, 0.08f), UnityEngine.Random.Range(-0.05f, 0.1f));
        Emit(s, Layer.OverlayTop, FxAtlas.Coin, pos, vel, UnityEngine.Random.Range(0.022f, 0.034f), 1.6f, Color.white, false, 0.5f, 0.3f, 0f, UnityEngine.Random.Range(6f, 12f));
    }

    void DrawBigWin(float dt)
    {
        var b = _big;
        if (b == null) return;
        var s = reelScreen;
        float t = Time.time - b.start;
        float a = Mathf.Clamp01(t / 0.3f) * b.alpha;
        Color tierCol = b.tier >= 3 ? new Color(0.3f, 0.8f, 1f) : b.tier == 2 ? new Color(1f, 0.35f, 0.8f) : new Color(1f, 0.75f, 0.25f);
        s[Layer.OverlayFx].Rect(s.Bounds, White, new Color(0.02f, 0f, 0.03f, 0.75f * a), new Color(0.02f, 0f, 0.03f, 0.75f * a));
        Vector2 c = new Vector2(0f, 0.045f);
        s[Layer.OverlayFx].Quad(c, Vector2.one * 0.75f, FxAtlas.Uv(FxAtlas.Rays), Pal.A(tierCol, 0.55f * a), t * 25f, true);
        s[Layer.OverlayFx].Quad(c, Vector2.one * 0.6f, FxAtlas.Uv(FxAtlas.Rays), Pal.A(Color.white, 0.25f * a), -t * 40f, true);
        s[Layer.OverlayFx].Quad(c, new Vector2(0.7f, 0.32f), FxAtlas.Uv(FxAtlas.Glow), Pal.A(tierCol, 0.6f * a), 0f, true);

        var region = b.tier >= 3 ? EpicWinUi : b.tier == 2 ? MegaWinUi : BigWinUi;
        float since = Time.time - b.tierChanged;
        float pop = since < 0.5f ? SlotEase.OutBack(since / 0.5f, 2.5f) : 1f + 0.03f * Mathf.Sin(t * 6f);
        float w = 0.48f * pop;
        s[Layer.OverlayUi].Quad(c, new Vector2(w, w / region.Aspect), region.Uv, new Color(1f, 1f, 1f, a));
        float amtPulse = 1f + 0.04f * Mathf.Sin(t * 14f);
        Digits.Draw(s[Layer.OverlayDigits], Digits.Format(b.shown), new Vector2(0f, -0.075f), 0.05f * amtPulse, new Color(1f, 0.98f, 0.8f), new Color(1f, 0.6f, 0.1f), 0.5f, null, a);
        if (Time.frameCount % 3 == 0 && b.alpha >= 1f)
            Emit(s, Layer.OverlayTop, FxAtlas.Sparkle, c + new Vector2(UnityEngine.Random.Range(-0.24f, 0.24f), UnityEngine.Random.Range(-0.06f, 0.06f)), Vector2.zero, UnityEngine.Random.Range(0.02f, 0.045f), 0.6f, Color.white, true, 0f, 0f, 90f);
    }

    // ================================================================ bet panel
    // Below the reel screen, facing the player.
    public override bool TryGetBetPanelPose(Vector3 offset, out Vector3 position, out Quaternion facing)
    {
        Vector3 up = (reelPanelTop - reelPanelBottom).normalized;
        Vector3 normal = new Vector3(0f, -up.z, up.y);
        Vector3 center = (reelPanelBottom + reelPanelTop) * 0.5f + normal * screenLift;
        Vector3 local = center + up * (MeterRect.yMin - 0.05f) + normal * 0.015f;
        facing = transform.rotation * Quaternion.LookRotation(-normal, up);
        position = SlotBetPanel.PushClear(this, transform.TransformPoint(local), facing, offset);
        return true;
    }

    // Test without VR: in Play mode, ⋮ menu on this component > Debug Spin.
    [ContextMenu("Debug Spin")]
    void DebugSpin()
    {
        if (!Application.isPlaying)
        {
            Debug.Log($"{name}: Debug Spin only works in Play mode.");
            return;
        }
        Spin();
    }

    // Editor testing: pick a seed that lands the bonus (variant lets a machine offer two bonus flavours).
    int _debugVariant = -1;
    protected virtual bool IsDebugBonusSeed(int seed, int variant) => true;

    [ContextMenu("Debug Spin (Force Bonus)")]
    void DebugSpinBonus() { if (Application.isPlaying && !_busy) { _debugVariant = 0; Spin(); _debugVariant = -1; } }

    [ContextMenu("Debug Spin (Force Bonus Alt)")]
    void DebugSpinBonusAlt() { if (Application.isPlaying && !_busy) { _debugVariant = 1; Spin(); _debugVariant = -1; } }
}
