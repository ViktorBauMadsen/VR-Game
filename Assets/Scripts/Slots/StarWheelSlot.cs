using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using VideoSlots;

// STAR WHEEL - 5x3, 243 ways, a big bonus wheel on top of the cabinet.
// Three WHEEL symbols (reels 1, 3, 5) light the wheel: pull the lever to
// spin it. It pays a credit prize, the JACKPOT, or 8 FREE SPINS where wilds
// land more often carrying x2 / x3 (multipliers on the same way multiply).
// Three WHEEL symbols in the free spins add 5 more.
public class StarWheelSlot : VideoSlotMachine
{
    [Header("Wheel (machine-local)")]
    [Tooltip("Lowest point of the wheel's rim.")]
    [SerializeField] Vector3 wheelBottom = new Vector3(0f, 2.39f, 0.3f);
    [SerializeField] float wheelRadius = 0.3f;
    [Tooltip("Degrees the wheel's face leans toward the player.")]
    [SerializeField] float wheelTilt = 18f;
    [SerializeField] float pullTimeout = 8f;
    [Tooltip("Pointer clacks (the roulette ball bounces).")]
    [SerializeField] AudioClip[] wheelClackClips;
    [Tooltip("Loop while the wheel turns (the roulette wheel spin).")]
    [SerializeField] AudioClip wheelSpinLoopClip;
    [Tooltip("When the wheel comes to rest (the roulette ball landing).")]
    [SerializeField] AudioClip wheelStopClip;

    protected override string SymbolTextureName => "StarWheel_Symbols";
    protected override string UiTextureName => "StarWheel_UI";
    protected override UiRegion BigWinUi => StarWheelArt.BigWin;
    protected override UiRegion MegaWinUi => StarWheelArt.MegaWin;
    protected override UiRegion EpicWinUi => StarWheelArt.EpicWin;
    protected override UiRegion TotalWinUi => StarWheelArt.TotalWin;
    protected override Color TrimColor => _free ? new Color(1f, 0.2f, 0.85f) : new Color(0.55f, 0.2f, 1f);
    protected override Color CabinetTint => new Color(0.62f, 0.55f, 0.95f);
    protected override Color AccentColor => _free ? Pink : Cyan;
    protected override Color MeterPanelTop => new Color(0.1f, 0.04f, 0.22f, 0.95f);
    protected override Color MeterPanelBottom => new Color(0.02f, 0.01f, 0.07f, 0.95f);
    protected override Color ValueTop => new Color(0.9f, 1f, 1f);
    protected override Color ValueBottom => new Color(0.25f, 0.85f, 1f);
    protected override Color LabelColor => new Color(0.6f, 0.95f, 1f);

    static readonly Color Pink = new Color(1f, 0.24f, 0.95f), Cyan = new Color(0.24f, 0.96f, 1f), Gold = new Color(1f, 0.83f, 0.29f);

    enum Mode { Base, WheelWait, WheelSpin, Free }
    Mode _mode = Mode.Base;

    readonly SlotRandom _cosmetic = new SlotRandom(System.Environment.TickCount ^ 0x3c3c);
    StarWheelMath.Result _result;
    StarWheelMath.Board _board;
    bool _free;
    int _freeIndex, _freeTotal;
    long _freeWin, _bonusTotal;
    int _scattersLanded;
    float _scatterShowUntil = -10f, _idleSince, _retriggerAt = -10f;
    TextMeshPro _info;

    // Wheel.
    ScreenSurface _wheel;
    Transform _face;
    QuadBatch _faceBatch;
    readonly TextMeshPro[] _labels = new TextMeshPro[16];
    float _R, _phi, _wheelSpeed, _pointer, _prevPeg, _lastClack = -1f;
    bool _awaitPull, _pulled;
    float _awaitStart, _awaitTimeout;
    int _wonWedge = -1;
    float _wonTime;
    int _labelBet = -1;

    struct Star { public Vector2 p; public float size, phase, speed; }
    Star[] _stars;
    float _shootStart = -10f;
    Vector2 _shootFrom, _shootDir;

    protected override void OnBuilt()
    {
        var rng = new System.Random(42);
        _stars = new Star[70];
        for (int i = 0; i < _stars.Length; i++)
            _stars[i] = new Star
            {
                p = new Vector2((float)rng.NextDouble() * 0.58f - 0.29f, (float)rng.NextDouble() * 0.52f - 0.26f),
                size = 0.004f + (float)rng.NextDouble() * 0.01f,
                phase = (float)rng.NextDouble() * 10f,
                speed = 1f + (float)rng.NextDouble() * 3f,
            };

        var col = new Sym[Rows];
        for (int r = 0; r < Reels; r++)
        {
            for (int row = 0; row < Rows; row++) { col[row] = Filler(r); if (col[row].id == StarWheelMath.Scatter) col[row] = new Sym(StarWheelMath.Planet); }
            reels[r].Set(col);
        }
        _info = midScreen.Text("Info", new Rect(-0.27f, -0.115f, 0.54f, 0.035f), LabelColor);
        BuildWheel();
        _idleSince = Time.time;
    }

    static int ArtFor(Sym s)
    {
        if (s.id == StarWheelMath.Wild && s.value == 2) return StarWheelMath.WildX2Art;
        if (s.id == StarWheelMath.Wild && s.value == 3) return StarWheelMath.WildX3Art;
        return s.id;
    }

    protected override Sym Filler(int reel)
    {
        int s = StarWheelMath.FillerSymbol(_cosmetic, reel, _free);
        return new Sym(s, s == StarWheelMath.Wild && _free ? (_cosmetic.Chance(0.3) ? 3 : 2) : 0);
    }

    static Sym BoardSym(StarWheelMath.Board b, int reel, int row)
    {
        int c = SlotGrid.Cell(reel, row);
        return new Sym(b.grid[c], b.mult[c] > 1 ? b.mult[c] : 0);
    }

    // ================================================================ play
    protected override bool IsDebugBonusSeed(int seed, int variant)
    {
        var r = StarWheelMath.Spin(seed);
        if (!r.wheel) return false;
        bool free = StarWheelMath.Wheel[r.wedge].kind == StarWheelMath.WedgeKind.FreeSpins;
        return variant == 1 ? free : !free;
    }

    protected override IEnumerator PlaySpin(int seed, int bet)
    {
        var r = StarWheelMath.Spin(seed);
        _result = r;
        _free = false;
        _mode = Mode.Base;
        _wonWedge = -1;
        RefreshLabels(bet);
        _board = r.baseSpin;
        _scattersLanded = 0;
        SetHeader("");

        yield return SpinReels((reel, row) => BoardSym(r.baseSpin, reel, row), r.baseSpin.Anticipate);

        long baseCredits = Credits(r.baseSpin.win, bet);
        long total = baseCredits;
        if (r.baseSpin.wins.Count > 0)
        {
            ShowWays(r.baseSpin, bet);
            Sfx(winClip, 0.75f);
            FlashTrim(Cyan, 1.2f);
            if (!r.wheel && WinTier(baseCredits, bet) >= 1) yield return BigWin(baseCredits, bet);
            else yield return RollUp(0, baseCredits, Mathf.Clamp(0.5f + (float)(baseCredits / (double)bet) * 0.15f, 0.6f, 2.2f));
        }

        if (r.wheel)
        {
            yield return WheelBonus(r, bet, seed, baseCredits);
            total = _bonusTotal;
        }
        spinWin = total;
        winMeter = total;
        _idleSince = Time.time;
    }

    void ShowWays(StarWheelMath.Board b, int bet)
    {
        ClearWins();
        long sum = 0;
        foreach (var w in b.wins)
        {
            long credits = Credits(w.pay, bet);
            sum += credits;
            string mult = w.multiplier > 1 ? $"  <color=#FF5AE8>x{w.multiplier}</color>" : "";
            var show = new WinShow
            {
                amount = credits,
                color = SymbolColor(w.symbol),
                label = $"{w.count}x {StarWheelMath.Names[w.symbol].ToUpper()}  {w.ways} WAY{(w.ways > 1 ? "S" : "")}{mult}  <color=#FFFFFF>{Digits.Format(credits)}</color>",
            };
            show.cells.AddRange(w.cells);
            winShows.Add(show);
        }
        BeginWinCycle($"{b.wins.Count} WIN{(b.wins.Count > 1 ? "S" : "")}  <color=#FFFFFF>{Digits.Format(sum)}</color>");
    }

    static Color SymbolColor(int s)
    {
        switch (s)
        {
            case StarWheelMath.Planet: return new Color(1f, 0.55f, 0.25f);
            case StarWheelMath.Rocket: return new Color(1f, 0.25f, 0.35f);
            case StarWheelMath.Ufo: return new Color(0.45f, 1f, 0.5f);
            case StarWheelMath.Moon: return Gold;
            case StarWheelMath.A: return new Color(1f, 0.25f, 0.55f);
            case StarWheelMath.K: return Cyan;
            case StarWheelMath.Q: return new Color(0.45f, 1f, 0.35f);
            case StarWheelMath.J: return new Color(1f, 0.65f, 0.25f);
            default: return new Color(0.7f, 0.45f, 1f);
        }
    }

    IEnumerator WheelBonus(StarWheelMath.Result r, int bet, int seed, long baseCredits)
    {
        slam = false;
        yield return new WaitForSeconds(0.4f);
        _scatterShowUntil = Time.time + 2.8f;
        Sfx(bonusChimeClip, 0.9f);
        Sfx(SlotSynth.FeatureFanfare(), 0.85f);
        FlashTrim(Pink, 3f);
        ShowBanner(reelScreen, StarWheelArt.WheelBonus, new Vector2(0f, 0.01f), 0.5f, 2.6f, Pink, Layer.OverlayUi);
        foreach (int c in r.baseSpin.scatterCells) Burst(reelScreen, CellCenter(c), 20, Gold, 0.35f, 0.035f);
        yield return new WaitForSeconds(2.7f);

        // Waits for the lever (or spins by itself after a while).
        _mode = Mode.WheelWait;
        _awaitPull = true;
        _pulled = false;
        _awaitStart = Time.time;
        _awaitTimeout = isLocalSpin ? pullTimeout : 2.5f;
        Sfx(ascendingChimeClip, 0.6f);
        while (!_pulled && Time.time - _awaitStart < _awaitTimeout) yield return null;
        _awaitPull = false;

        _mode = Mode.WheelSpin;
        Sfx(SlotSynth.Whoosh(), 0.7f, 0.8f);
        yield return SpinWheel(r.wedge, seed);

        var wedge = StarWheelMath.Wheel[r.wedge];
        _wonWedge = r.wedge;
        _wonTime = Time.time;
        Burst(_wheel, new Vector2(0f, _R * 0.62f), 40, Gold, 0.35f, 0.04f);
        long total = baseCredits;

        if (wedge.kind == StarWheelMath.WedgeKind.FreeSpins)
        {
            Sfx(bonusChimeClip, 0.9f);
            FlashTrim(Pink, 2.5f);
            yield return new WaitForSeconds(1f);
            yield return FreeSpins(r, bet, baseCredits);
            total = _bonusTotal;
        }
        else
        {
            long add = Credits(r.wheelWin, bet);
            total += add;
            if (wedge.kind == StarWheelMath.WedgeKind.Jackpot)
            {
                Sfx(SlotSynth.JackpotFanfare(), 1f);
                FlashTrim(Gold, 4f);
                ShowBanner(reelScreen, StarWheelArt.Jackpot, new Vector2(0f, 0.02f), 0.5f, 3f, Gold, Layer.OverlayUi);
                yield return new WaitForSeconds(3f);
            }
            else
            {
                Sfx(ascendingChimeClip, 0.8f);
                yield return new WaitForSeconds(1.2f);
            }
            yield return BigWin(total, bet);
        }
        _bonusTotal = total;
        _mode = _free ? Mode.Free : Mode.Base;
    }

    IEnumerator SpinWheel(int wedge, int seed)
    {
        // Where in the wedge it stops is cosmetic, but seeded so every player sees the same.
        var rng = new System.Random(seed ^ 0x5bd1e99);
        float jitter = ((float)rng.NextDouble() * 2f - 1f) * 8f;
        float target = wedge * 22.5f + jitter;
        float start = _phi;
        float distance = 360f * 4f + Mathf.Repeat(start - target, 360f);
        const float duration = 7.2f;
        StartLoop(Loop.Fx, wheelSpinLoopClip, 0.8f);
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duration);
            float prev = _phi;
            _phi = start - distance * (1f - Mathf.Pow(1f - u, 4f));
            _wheelSpeed = Mathf.Abs(_phi - prev) / Mathf.Max(1e-4f, Time.deltaTime);
            float k = Mathf.Clamp01(_wheelSpeed / 700f);
            StartLoop(Loop.Fx, wheelSpinLoopClip, 0.15f + 0.65f * k);
            SetLoopPitch(Loop.Fx, 0.75f + 0.45f * k);
            yield return null;
        }
        _phi = start - distance;
        _wheelSpeed = 0f;
        StopLoop(Loop.Fx, 3f);
        Sfx(wheelStopClip, 1f);
        Sfx(winClip, 0.7f);
        // Pointer nudges it back a hair.
        float settle = _phi;
        for (float s = 0f; s < 0.35f; s += Time.deltaTime)
        {
            _phi = settle + 1.2f * Mathf.Sin(Mathf.Clamp01(s / 0.35f) * Mathf.PI * 0.5f);
            yield return null;
        }
    }

    IEnumerator FreeSpins(StarWheelMath.Result r, int bet, long baseCredits)
    {
        _free = true;
        _mode = Mode.Free;
        _freeTotal = StarWheelMath.FreeSpinsAwarded;
        _freeIndex = 0;
        _freeWin = 0;
        ClearWins();
        ShowBanner(reelScreen, StarWheelArt.FreeSpins, new Vector2(0f, 0.01f), 0.5f, 2.4f, Cyan, Layer.OverlayUi);
        StartLoop(Loop.Bed, SlotSynth.StarBed(), 0.45f);
        yield return new WaitForSeconds(2.4f);

        long total = baseCredits;
        winMeter = total;
        foreach (var board in r.freeSpins)
        {
            slam = false;
            _freeIndex++;
            _board = board;
            _scattersLanded = 0;
            ClearWins();
            yield return SpinReels((reel, row) => BoardSym(board, reel, row), board.Anticipate, 0.55f, 0.16f);

            if (board.wins.Count > 0)
            {
                long w = Credits(board.win, bet);
                ShowWays(board, bet);
                bool mult = false;
                foreach (var win in board.wins) if (win.multiplier > 1) mult = true;
                if (mult) Sfx(SlotSynth.MultiplierHit(), 0.8f);
                Sfx(winClip, 0.6f);
                FlashTrim(Pink, 1f);
                if (WinTier(w, bet) >= 1)
                {
                    long before = total;
                    yield return BigWin(w, bet);
                    winMeter = before + w;
                }
                else yield return RollUp(total, total + w, Mathf.Clamp(0.4f + (float)(w / (double)bet) * 0.1f, 0.5f, 1.6f));
                total += w;
                _freeWin += w;
                winMeter = total;
                yield return Wait(0.9f);
            }
            if (board.retrigger > 0)
            {
                _freeTotal += board.retrigger;
                _retriggerAt = Time.time;
                _scatterShowUntil = Time.time + 2f;
                Sfx(bonusChimeClip, 0.9f);
                ShowBanner(reelScreen, StarWheelArt.FreeSpins, new Vector2(0f, 0.01f), 0.46f, 2f, Pink, Layer.OverlayUi);
                yield return new WaitForSeconds(2.1f);
            }
            yield return Wait(0.45f);
        }
        StopLoop(Loop.Bed, 0.6f);
        ClearWins();
        yield return new WaitForSeconds(0.4f);
        if (WinTier(_freeWin, bet) >= 1) yield return BigWin(_freeWin, bet);
        else
        {
            ShowBanner(reelScreen, StarWheelArt.TotalWin, new Vector2(0f, 0.03f), 0.42f, 2.2f, Cyan, Layer.OverlayUi);
            Sfx(winClip, 0.8f);
            yield return new WaitForSeconds(2.2f);
        }
        winMeter = total;
        _free = false;
        _bonusTotal = total;
        // Back to the spin that triggered it.
        for (int reel = 0; reel < Reels; reel++)
        {
            var col = new Sym[Rows];
            for (int row = 0; row < Rows; row++) col[row] = BoardSym(r.baseSpin, reel, row);
            reels[reel].Set(col);
        }
        _board = r.baseSpin;
    }

    protected override void OnLeverWhileBusy()
    {
        if (_awaitPull && isLocalSpin)
        {
            _pulled = true;
            return;
        }
        if (_mode == Mode.WheelSpin) return;
        base.OnLeverWhileBusy();
    }

    protected override void OnBetChanged()
    {
        base.OnBetChanged();
        RefreshLabels(betAmount);
    }

    protected override void OnReelLanded(int reel)
    {
        base.OnReelLanded(reel);
        if (_board == null) return;
        for (int row = 0; row < Rows; row++)
        {
            int c = SlotGrid.Cell(reel, row);
            int s = _board.grid[c];
            if (s == StarWheelMath.Scatter)
            {
                _scattersLanded++;
                Sfx(SlotSynth.ScatterLand(), 0.85f, 1f + (_scattersLanded - 1) * 0.12f);
                Burst(reelScreen, CellCenter(c), 14, Gold, 0.3f, 0.03f);
            }
            else if (s == StarWheelMath.Wild)
            {
                Sfx(_board.mult[c] > 1 ? SlotSynth.MultiplierHit() : SlotSynth.WildLand(), 0.5f);
                Burst(reelScreen, CellCenter(c), 10, Pink, 0.3f, 0.03f);
            }
        }
    }

    // ================================================================ wheel
    void BuildWheel()
    {
        _R = wheelRadius;
        // Shrink to fit under a ceiling, if there is one right above.
        Vector3 from = transform.TransformPoint(wheelBottom) + Vector3.up * 0.05f;
        float nearest = float.MaxValue;
        foreach (var hit in Physics.RaycastAll(from, Vector3.up, wheelRadius * 2f + 0.3f, ~0, QueryTriggerInteraction.Ignore))
            if (!hit.collider.transform.IsChildOf(transform) && hit.distance < nearest) nearest = hit.distance;
        if (nearest < float.MaxValue) _R = Mathf.Clamp((nearest + 0.01f) * 0.5f, 0.16f, wheelRadius);

        float tilt = wheelTilt * Mathf.Deg2Rad;
        Vector3 up = new Vector3(0f, Mathf.Cos(tilt), Mathf.Sin(tilt));
        Vector3 normal = new Vector3(0f, -Mathf.Sin(tilt), Mathf.Cos(tilt));
        Vector3 center = wheelBottom + up * _R;
        _wheel = MakeSurface("Bonus Wheel", center, Quaternion.LookRotation(-normal, up), Vector2.one * _R * 2.8f);

        _face = new GameObject("Wheel Face").transform;
        _face.SetParent(_wheel.root, false);
        var faceMat = SlotMaterials.Create(SlotTextures.Get("StarWheel_Wheel"), (int)Layer.ReelBg);
        _faceBatch = new QuadBatch(_face, "Face", faceMat, 0.0008f);
        float q = _R / 0.485f;
        _faceBatch.Quad(Vector2.zero, Vector2.one * q, new Rect(0f, 0f, 1f, 1f), Color.white);
        _faceBatch.Apply();

        for (int i = 0; i < _labels.Length; i++)
        {
            float th = i * 22.5f * Mathf.Deg2Rad;
            Vector2 d = new Vector2(Mathf.Sin(th), Mathf.Cos(th));
            float ang = Mathf.Atan2(-d.y, -d.x) * Mathf.Rad2Deg;
            var go = new GameObject("Label " + i);
            go.transform.SetParent(_face, false);
            go.transform.localPosition = (Vector3)(d * _R * 0.6f) + new Vector3(0f, 0f, -0.003f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, ang);
            var t = go.AddComponent<TextMeshPro>();
            t.rectTransform.sizeDelta = new Vector2(_R * 0.52f, _R * 0.13f);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.enableAutoSizing = true;
            t.fontSizeMin = 0.02f;
            t.fontSizeMax = _R * 0.13f * 8.5f;
            t.alignment = TextAlignmentOptions.Center;
            t.fontStyle = FontStyles.Bold;
            t.outlineWidth = 0.3f;
            t.outlineColor = new Color32(20, 6, 30, 255);
            var w = StarWheelMath.Wheel[i];
            t.color = w.kind == StarWheelMath.WedgeKind.Jackpot ? Gold
                : w.kind == StarWheelMath.WedgeKind.FreeSpins ? new Color(1f, 0.55f, 0.95f) : Color.white;
            t.fontMaterial.renderQueue = SlotMaterials.QueueBase + (int)Layer.Count + 1;
            var rend = t.GetComponent<Renderer>();
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            _labels[i] = t;
        }
        RefreshLabels(betAmount);
        _phi = 0f;
        _prevPeg = PegOffset();
    }

    void RefreshLabels(int bet)
    {
        if (_labels[0] == null || bet == _labelBet) return;
        _labelBet = bet;
        for (int i = 0; i < _labels.Length; i++)
        {
            var w = StarWheelMath.Wheel[i];
            _labels[i].text = w.kind == StarWheelMath.WedgeKind.FreeSpins ? "FREE SPINS"
                : w.kind == StarWheelMath.WedgeKind.Jackpot ? "JACKPOT"
                : Digits.Format((long)w.multiplier * bet, true);
        }
    }

    static Rect Centered(float size) => new Rect(-size * 0.5f, -size * 0.5f, size, size);

    // Signed angle (degrees) of the nearest peg from the pointer; pegs sit between wedges.
    // (Peg j sits at j * 22.5 - 11.25 degrees clockwise on the face; the face is turned _phi counter-clockwise.)
    float PegOffset() => Mathf.Repeat(-_phi, 22.5f) - 11.25f;

    void DrawWheel(float dt)
    {
        var s = _wheel;
        float R = _R, t = Time.time;
        _face.localRotation = Quaternion.Euler(0f, 0f, _phi);

        // Pointer: pegs passing push it aside; it springs back. Clack on each peg.
        float peg = PegOffset();
        float push = peg > -6f && peg <= 0f ? (peg + 6f) / 6f * 26f : 0f;
        if (push > _pointer) _pointer = push;
        else _pointer = Mathf.MoveTowards(_pointer, 0f, 420f * dt);
        if (_prevPeg <= 0f && peg > 0f && _wheelSpeed > 5f && Time.time - _lastClack > 0.03f)
        {
            _lastClack = Time.time;
            var clip = wheelClackClips != null && wheelClackClips.Length > 0 ? wheelClackClips[Random.Range(0, wheelClackClips.Length)] : SlotSynth.Clack();
            Sfx(clip, Mathf.Clamp(0.25f + _wheelSpeed / 500f, 0.25f, 0.9f), Random.Range(0.95f, 1.1f));
        }
        _prevPeg = peg;

        bool active = _mode == Mode.WheelWait || _mode == Mode.WheelSpin || _wonWedge >= 0 && t - _wonTime < 4f;
        var bg = s[Layer.Bg];
        bg.Quad(Vector2.zero, Vector2.one * R * 3.1f, FxAtlas.Uv(FxAtlas.Glow), new Color(0.03f, 0f, 0.08f, 0.85f));
        if (active) bg.Quad(Vector2.zero, Vector2.one * R * (3.6f + 0.2f * Mathf.Sin(t * 6f)), FxAtlas.Uv(FxAtlas.Glow), new Color(1f, 0.25f, 0.9f, 0.55f), 0f, true);
        // Bevelled gold marquee band (Disc sprite radius = 0.47 of the quad).
        Rect disc = FxAtlas.Uv(FxAtlas.Disc), ru = QuadBatch.Unclipped;
        float bandR = R * 1.24f;
        bg.Rect(Centered(bandR / 0.47f), disc, new Color(1f, 0.88f, 0.5f), new Color(0.45f, 0.26f, 0.06f), ru);
        bg.Rect(Centered((bandR - R * 0.03f) / 0.47f), disc, new Color(0.62f, 0.4f, 0.12f), new Color(0.95f, 0.75f, 0.35f), ru);
        bg.Rect(Centered((R * 1.02f) / 0.47f), disc, new Color(0.35f, 0.2f, 0.04f), new Color(0.2f, 0.1f, 0.02f), ru);
        var fx = s[Layer.FxTop];
        if (active) fx.Quad(Vector2.zero, Vector2.one * bandR / 0.38f, FxAtlas.Uv(FxAtlas.Ring), new Color(1f, 0.4f, 0.9f, 0.6f + 0.3f * SlotEase.Pulse(4f)), 0f, true);

        // Chasing bulbs around the band.
        const int bulbs = 24;
        float rate = _mode == Mode.WheelSpin ? 16f : _mode == Mode.WheelWait ? 10f : 3f;
        int step = (int)(t * rate);
        bool winBlink = _wonWedge >= 0 && t - _wonTime < 4f;
        for (int i = 0; i < bulbs; i++)
        {
            float a = (i + 0.5f) / bulbs * Mathf.PI * 2f;
            Vector2 p = new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * R * 1.125f;
            bool lit = winBlink ? (i + (int)(t * 8f)) % 2 == 0 : (i + step) % 3 == 0;
            Color c = i % 2 == 0 ? new Color(1f, 0.9f, 0.55f) : new Color(1f, 0.45f, 0.95f);
            fx.Quad(p, Vector2.one * R * 0.115f, disc, new Color(0.3f, 0.17f, 0.04f));
            fx.Quad(p, Vector2.one * R * 0.09f, disc, lit ? Color.Lerp(c, Color.white, 0.55f) : new Color(0.55f, 0.42f, 0.3f));
            fx.Quad(p + new Vector2(-0.012f, 0.012f) * R, Vector2.one * R * 0.03f, disc, new Color(1f, 1f, 1f, lit ? 0.9f : 0.45f));
            if (lit) fx.Quad(p, Vector2.one * R * 0.3f, FxAtlas.Uv(FxAtlas.Bulb), c, 0f, true);
        }

        // The winning wedge shines.
        if (_wonWedge >= 0 && t - _wonTime < 6f)
        {
            float p = SlotEase.Pulse(10f);
            fx.Quad(new Vector2(0f, R * 0.62f), new Vector2(R * 0.5f, R * 0.85f), FxAtlas.Uv(FxAtlas.Glow), new Color(1f, 0.95f, 0.6f, 0.35f + 0.35f * p), 0f, true);
            if (Random.value < dt * 20f)
                Emit(s, Layer.OverlayFx, FxAtlas.Sparkle, new Vector2(Random.Range(-0.12f, 0.12f) * R, R * Random.Range(0.3f, 0.95f)), Vector2.zero, R * Random.Range(0.06f, 0.14f), 0.6f, Color.white, true, 0f, 0f, 90f);
        }

        // Pointer, hinged at its top.
        var ptr = StarWheelArt.Pointer;
        float size = R * 0.3f;
        Vector2 pivot = new Vector2(0f, R * 1.13f);
        float ang = _pointer * Mathf.Deg2Rad;
        Vector2 down = new Vector2(Mathf.Sin(ang), -Mathf.Cos(ang)) * size * 0.28f;
        s[Layer.Ui].Quad(pivot + down, Vector2.one * size, ptr.Uv, Color.white, _pointer);
        var hub = StarWheelArt.Hub;
        s[Layer.Ui].Quad(Vector2.zero, Vector2.one * R * 0.26f, hub.Uv, Color.white, Mathf.Sin(t) * 4f);
    }

    // ================================================================ draw
    protected override void DrawScreens(float dt)
    {
        DrawSpace(reelScreen, dt, true);
        DrawReelsLayer();
        DrawHeader();
        DrawPullPrompt();
        DrawInfo(dt);
        DrawTopper(dt);
        DrawWheel(dt);
    }

    void DrawSpace(ScreenSurface s, float dt, bool reelsBehind)
    {
        var bg = s[Layer.Bg];
        Rect full = s.Bounds;
        Color top = _free ? new Color(0.28f, 0.03f, 0.3f) : new Color(0.12f, 0.03f, 0.3f);
        bg.Rect(full, White, top, new Color(0.01f, 0f, 0.06f));
        float t = Time.time;
        bg.Quad(new Vector2(-0.12f + 0.05f * Mathf.Sin(t * 0.2f), 0.06f), new Vector2(0.5f, 0.4f), FxAtlas.Uv(FxAtlas.Smoke), new Color(1f, 0.2f, 0.85f, _free ? 0.35f : 0.22f), t * 4f, true);
        bg.Quad(new Vector2(0.14f, -0.08f + 0.04f * Mathf.Cos(t * 0.17f)), new Vector2(0.45f, 0.38f), FxAtlas.Uv(FxAtlas.Smoke), new Color(0.2f, 0.7f, 1f, 0.2f), -t * 3f, true);
        foreach (var st in _stars)
        {
            Vector2 p = new Vector2(st.p.x, st.p.y * full.height / 0.52f);
            float tw = 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin(t * st.speed + st.phase));
            bg.Quad(p, Vector2.one * st.size * 2.2f, FxAtlas.Uv(FxAtlas.Sparkle), new Color(0.8f, 0.9f, 1f, tw), 0f, true);
        }
        if (!reelsBehind) return;

        // Shooting star now and then.
        if (t - _shootStart > 5f && Random.value < dt * 0.4f)
        {
            _shootStart = t;
            _shootFrom = new Vector2(Random.Range(-0.25f, 0.1f), 0.22f);
            _shootDir = new Vector2(Random.Range(0.6f, 1f), -Random.Range(0.3f, 0.6f)).normalized;
        }
        float st2 = t - _shootStart;
        if (st2 < 0.7f)
        {
            Vector2 head = _shootFrom + _shootDir * st2 * 0.6f;
            bg.Line(head - _shootDir * 0.08f, head, 0.006f, FxAtlas.Uv(FxAtlas.Line), new Color(0.8f, 0.95f, 1f, 1f - st2 / 0.7f), true);
        }

        var rb = s[Layer.ReelBg];
        for (int r = 0; r < Reels; r++)
        {
            Rect col = ColumnRect(r);
            rb.Rect(Grow(col, -0.0015f), White, new Color(0.08f, 0.03f, 0.2f, 0.82f), new Color(0.02f, 0.01f, 0.07f, 0.88f));
            rb.Quad(col.center, new Vector2(col.width * 1.2f, col.height * 0.9f), FxAtlas.Uv(FxAtlas.Glow), new Color(0.35f, 0.1f, 0.7f, 0.15f), 0f, true);
        }
        rb.Rect(new Rect(ReelWindow.xMin, ReelWindow.yMax - 0.022f, ReelWindow.width, 0.022f), White, new Color(0f, 0f, 0f, 0.5f), new Color(0f, 0f, 0f, 0f));
        rb.Rect(new Rect(ReelWindow.xMin, ReelWindow.yMin, ReelWindow.width, 0.022f), White, new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0.5f));
    }

    void NeonFrame(QuadBatch b, Rect r, Color c, float glow)
    {
        var uv = FxAtlas.Uv(FxAtlas.Line);
        Vector2 a = new Vector2(r.xMin, r.yMax), bb = new Vector2(r.xMax, r.yMax), cc = new Vector2(r.xMax, r.yMin), d = new Vector2(r.xMin, r.yMin);
        Color g = Pal.A(c, glow);
        b.Line(a, bb, 0.024f, uv, g, true); b.Line(bb, cc, 0.024f, uv, g, true); b.Line(cc, d, 0.024f, uv, g, true); b.Line(d, a, 0.024f, uv, g, true);
        Color core = Color.Lerp(c, Color.white, 0.55f);
        Border(b, r, 0.0028f, core, Color.Lerp(c, Color.white, 0.2f));
    }

    void DrawReelsLayer()
    {
        var bs = reelScreen[Layer.Symbols];
        var bt = reelScreen[Layer.SymbolsTop];
        var ln = reelScreen[Layer.Lines];
        var fx = reelScreen[Layer.FxTop];
        var win = CurrentWin(out float phase, out bool first);
        bool scatterShow = Time.time < _scatterShowUntil;
        float size = Mathf.Min(CellW, CellH) * 1.04f;

        for (int r = 0; r < Reels; r++)
        {
            var strip = reels[r];
            bool blur = strip.Blurred;
            for (int k = 0; k < strip.syms.Length; k++)
            {
                float y = strip.SlotY(k);
                if (y - CellH > ReelWindow.yMax || y + CellH < ReelWindow.yMin) continue;
                var s = strip.syms[k];
                Vector2 c = new Vector2(CellCenter(r, 0).x, y);
                int row = k - 1;
                int cell = !strip.Moving && row >= 0 && row < Rows ? SlotGrid.Cell(r, row) : -1;
                bool scatter = s.id == StarWheelMath.Scatter;

                bool hl = win != null && cell >= 0 && win.cells.Contains(cell);
                bool glowScatter = scatterShow && scatter && cell >= 0;
                bool dim = (win != null && !hl) || (scatterShow && !glowScatter);
                float scale = 1f;
                float since = Time.time - strip.landTime;
                if ((scatter || s.id == StarWheelMath.Wild) && cell >= 0 && since < 0.35f) scale = 1f + 0.18f * Mathf.Sin(since / 0.35f * Mathf.PI);
                if (hl) scale = 1f + 0.07f * Mathf.Sin(phase * Mathf.PI * 4f);
                if (glowScatter) scale = 1.08f + 0.07f * Mathf.Sin(Time.time * 10f + cell);
                Color tint = dim ? new Color(0.32f, 0.3f, 0.4f) : Color.white;

                bool top = hl || glowScatter;
                if (top)
                {
                    Color g = glowScatter ? Gold : win.color;
                    fx.Quad(c, new Vector2(CellW, CellH) * 1.02f, FxAtlas.Uv(FxAtlas.Frame), Pal.A(g, 0.95f), 0f, true);
                    ln.Quad(c, Vector2.one * size * 1.7f, FxAtlas.Uv(FxAtlas.Glow), Pal.A(g, 0.45f + 0.3f * SlotEase.Pulse(10f, cell)), 0f, true);
                    if (glowScatter) ln.Quad(c, Vector2.one * size * 1.5f, FxAtlas.Uv(FxAtlas.Rays), Pal.A(Gold, 0.6f), Time.time * 60f, true);
                }
                // Multiplier wilds glow pink.
                if (s.id == StarWheelMath.Wild && s.value > 1 && !blur && !dim)
                    ln.Quad(c, Vector2.one * size * 1.45f, FxAtlas.Uv(FxAtlas.Glow), Pal.A(Pink, 0.35f + 0.2f * SlotEase.Pulse(7f, r)), 0f, true);
                DrawSymbol(top ? bt : bs, ArtFor(s), c, size * scale, blur, tint, top ? QuadBatch.Unclipped : ReelWindow);
            }
        }

        // Neon frame + suspense on the last reel.
        Color frame = _free ? Pink : Cyan;
        NeonFrame(fx, Grow(ReelWindow, 0.004f), frame, 0.45f + 0.15f * SlotEase.Pulse(2f));
        for (int r = 1; r < Reels; r++)
        {
            float x = ReelWindow.xMin + r * CellW;
            fx.Rect(new Rect(x - 0.0007f, ReelWindow.yMin, 0.0014f, ReelWindow.height), White, Pal.A(frame, 0.55f), Pal.A(frame, 0.2f));
        }
        if (anticipatingReel >= 0)
        {
            Rect col = ColumnRect(anticipatingReel);
            float k = Mathf.Clamp01((Time.time - anticipationStart) / 0.3f);
            float p = SlotEase.Pulse(16f);
            fx.Quad(col.center, new Vector2(col.width * 1.25f, col.height * 1.08f), FxAtlas.Uv(FxAtlas.Frame), new Color(1f, 0.8f + 0.2f * p, 0.3f, k), 0f, true);
            fx.Quad(col.center, new Vector2(col.width * 2.2f, col.height * 1.5f), FxAtlas.Uv(FxAtlas.Glow), Pal.A(Gold, 0.4f * k), 0f, true);
            if (Random.value < 0.6f)
                Emit(reelScreen, Layer.FxTop, FxAtlas.Star, new Vector2(col.center.x + Random.Range(-0.05f, 0.05f), col.center.y + Random.Range(-0.15f, 0.15f)),
                    Random.insideUnitCircle * 0.15f, Random.Range(0.012f, 0.025f), Random.Range(0.4f, 0.7f), Color.white, false, 0f, 1f, 200f);
        }
    }

    void DrawHeader()
    {
        var rb = reelScreen[Layer.ReelBg];
        rb.Rect(HeaderRect, White, new Color(0.12f, 0.04f, 0.28f, 0.95f), new Color(0.03f, 0.01f, 0.09f, 0.95f));
        NeonFrame(reelScreen[Layer.FxTop], HeaderRect, _free ? Pink : Cyan, 0.3f);

        if (Time.time - _retriggerAt < 2.2f)
        {
            SetHeader($"<color=#FF5AE8>+{StarWheelMath.FreeSpinsRetrigger} FREE SPINS!</color>");
            return;
        }
        var win = CurrentWin(out _, out _);
        if (win != null)
        {
            SetHeader(win.label);
            return;
        }
        switch (_mode)
        {
            case Mode.WheelWait: SetHeader("<color=#FFD34A>PULL THE LEVER TO SPIN THE WHEEL!</color>"); return;
            case Mode.WheelSpin: SetHeader("<color=#FFD34A>GOOD LUCK!</color>"); return;
            case Mode.Free: SetHeader($"FREE SPIN <color=#FFFFFF>{_freeIndex}</color> OF <color=#FFFFFF>{_freeTotal}</color>"); return;
        }
        if (IsSpinning)
        {
            SetHeader("GOOD LUCK!");
            return;
        }
        string[] idle =
        {
            "3 <color=#FFD34A>WHEEL</color> SYMBOLS SPIN THE BIG WHEEL",
            "FREE SPINS: WILDS CARRY <color=#FF5AE8>x2</color> AND <color=#FF5AE8>x3</color>",
            "243 WAYS  •  PULL THE LEVER TO SPIN",
        };
        SetHeader(idle[(int)((Time.time - _idleSince) / 3.5f) % idle.Length]);
    }

    void DrawPullPrompt()
    {
        if (!_awaitPull) return;
        var s = reelScreen;
        float t = Time.time - _awaitStart;
        float a = Mathf.Clamp01(t / 0.3f);
        s[Layer.OverlayFx].Rect(s.Bounds, White, new Color(0.02f, 0f, 0.06f, 0.55f * a), new Color(0.02f, 0f, 0.06f, 0.55f * a));
        s[Layer.OverlayFx].Quad(new Vector2(0f, 0.02f), Vector2.one * 0.55f, FxAtlas.Uv(FxAtlas.Rays), Pal.A(Pink, 0.45f * a), t * 30f, true);
        var reg = StarWheelArt.PullToSpin;
        float w = 0.48f * (1f + 0.05f * Mathf.Sin(t * 8f)) * SlotEase.OutBack(t / 0.4f, 2.5f);
        s[Layer.OverlayUi].Quad(new Vector2(0f, 0.035f), new Vector2(w, w / reg.Aspect), reg.Uv, new Color(1f, 1f, 1f, a));
        if (isLocalSpin)
        {
            int left = Mathf.CeilToInt(Mathf.Max(0f, _awaitTimeout - t));
            Digits.Draw(s[Layer.OverlayDigits], left.ToString(), new Vector2(0f, -0.06f), 0.03f, Color.white, Cyan, 0.5f, null, a);
        }
        // Point up at the wheel.
        float bob = Mathf.Sin(t * 6f) * 0.012f;
        s[Layer.OverlayFx].Quad(new Vector2(-0.2f, 0.12f + bob), new Vector2(0.03f, 0.03f), FxAtlas.Uv(FxAtlas.Diamond), Pal.A(Gold, a), 0f, true);
        s[Layer.OverlayFx].Quad(new Vector2(0.2f, 0.12f + bob), new Vector2(0.03f, 0.03f), FxAtlas.Uv(FxAtlas.Diamond), Pal.A(Gold, a), 0f, true);
    }

    // Upper screen, lower section.
    void DrawInfo(float dt)
    {
        var s = midScreen;
        DrawSpace(s, dt, false);
        Rect full = s.Bounds;
        var banner = _free ? StarWheelArt.FreeSpins : StarWheelArt.WheelBonus;
        float bw = 0.46f * (1f + 0.015f * Mathf.Sin(Time.time * 2f));
        s[Layer.FxTop].Quad(new Vector2(0f, full.yMax - 0.05f), new Vector2(bw * 1.1f, 0.12f), FxAtlas.Uv(FxAtlas.Glow), Pal.A(_free ? Cyan : Pink, 0.35f), 0f, true);
        s[Layer.Ui].Quad(new Vector2(0f, full.yMax - 0.05f), new Vector2(bw, bw / banner.Aspect), banner.Uv, Color.white);

        var rb = s[Layer.ReelBg];
        var dg = s[Layer.Digits];
        Rect row1 = new Rect(-0.27f, -0.035f, 0.54f, 0.06f);
        rb.Rect(row1, White, new Color(0.1f, 0.03f, 0.24f, 0.9f), new Color(0.02f, 0.01f, 0.08f, 0.9f));
        NeonFrame(s[Layer.FxTop], row1, _free ? Pink : Gold, 0.25f);
        if (_free)
        {
            var spins = StarWheelArt.SpinsLeft;
            s[Layer.Ui].Quad(new Vector2(-0.17f, row1.center.y), new Vector2(0.14f, 0.07f), spins.Uv, Color.white);
            Digits.Draw(dg, $"{_freeIndex}/{_freeTotal}", new Vector2(0.08f, row1.center.y), 0.03f, Color.white, Cyan);
            _info.text = "";
            Rect row2 = new Rect(-0.27f, -0.13f, 0.54f, 0.06f);
            rb.Rect(row2, White, new Color(0.1f, 0.03f, 0.24f, 0.9f), new Color(0.02f, 0.01f, 0.08f, 0.9f));
            NeonFrame(s[Layer.FxTop], row2, Cyan, 0.25f);
            var tw = StarWheelArt.TotalWin;
            s[Layer.Ui].Quad(new Vector2(-0.16f, row2.center.y), new Vector2(0.17f, 0.17f / tw.Aspect), tw.Uv, Color.white);
            Digits.Draw(dg, Digits.Format(_freeWin), new Vector2(0.08f, row2.center.y), 0.028f, new Color(1f, 0.97f, 0.8f), new Color(1f, 0.65f, 0.15f));
        }
        else
        {
            var jp = StarWheelArt.Jackpot;
            s[Layer.Ui].Quad(new Vector2(-0.15f, row1.center.y), new Vector2(0.2f, 0.05f), jp.Uv, Color.white);
            long amount = (long)StarWheelMath.Wheel[0].multiplier * curBet;
            Digits.Draw(dg, Digits.Format(amount), new Vector2(0.1f, row1.center.y), 0.03f, new Color(1f, 0.97f, 0.8f), new Color(1f, 0.65f, 0.15f));
            string msg = _mode == Mode.WheelWait ? "<color=#FFD34A>PULL THE LEVER!</color>" : "3 WHEEL SYMBOLS SPIN THE WHEEL";
            if (_info.text != msg) _info.text = msg;
        }
        float sweep = Mathf.Repeat(Time.time * 0.3f, 1.6f) - 0.3f;
        s[Layer.OverlayFx].QuadClipped(new Vector2(-0.35f + sweep * 0.7f, 0f), new Vector2(0.06f, full.height * 1.1f), FxAtlas.Uv(FxAtlas.SoftRect), new Color(0.8f, 0.9f, 1f, 0.1f), 12f, s.Bounds, true);
    }

    // Upper screen, top section: logo.
    void DrawTopper(float dt)
    {
        var s = topScreen;
        DrawSpace(s, dt, false);
        float t = Time.time;
        var logo = StarWheelArt.Logo;
        float w = 0.56f * (1f + 0.012f * Mathf.Sin(t * 1.4f));
        bool hot = _mode != Mode.Base || BigWinActive;
        s[Layer.FxTop].Quad(new Vector2(0f, 0f), new Vector2(w * 1.05f, w / logo.Aspect * 1.7f), FxAtlas.Uv(FxAtlas.Glow), Pal.A(_free ? Pink : new Color(0.5f, 0.3f, 1f), hot ? 0.75f : 0.4f), 0f, true);
        s[Layer.Ui].Quad(Vector2.zero, new Vector2(w, w / logo.Aspect), logo.Uv, Color.white);
        float sweep = Mathf.Repeat(t * 0.25f, 1.5f) - 0.25f;
        s[Layer.OverlayFx].QuadClipped(new Vector2(-0.35f + sweep * 0.7f, 0f), new Vector2(0.07f, 0.2f), FxAtlas.Uv(FxAtlas.SoftRect), new Color(0.9f, 0.95f, 1f, 0.16f), 14f, s.Bounds, true);
        if (Random.value < dt * (hot ? 14f : 4f))
            Emit(s, Layer.OverlayFx, FxAtlas.Sparkle, new Vector2(Random.Range(-0.27f, 0.27f), Random.Range(-0.08f, 0.08f)), Vector2.zero, Random.Range(0.012f, 0.03f), 0.6f, Color.white, true, 0f, 0f, 90f);
    }
}
