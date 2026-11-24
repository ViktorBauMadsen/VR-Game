using System.Collections;
using TMPro;
using UnityEngine;
using VideoSlots;
using M = VideoSlots.FruitCocktailMath;

// FRUIT COCKTAIL (remastered) - 5x3, 10 lines of classic fruit.
// Cocktail WILDs substitute for every fruit. DIAMONDs pay anywhere and 3+
// start 10 FREE SPINS: first a SPECIAL symbol is picked, and whenever it
// lands on enough reels it expands over them and pays on all 10 lines.
// 3+ diamonds in the free spins add 10 more.
public class FruitCocktailSlot : VideoSlotMachine
{
    protected override string SymbolTextureName => "FruitCocktail_Symbols";
    protected override string UiTextureName => "FruitCocktail_UI";
    protected override UiRegion BigWinUi => FruitCocktailArt.BigWin;
    protected override UiRegion MegaWinUi => FruitCocktailArt.MegaWin;
    protected override UiRegion EpicWinUi => FruitCocktailArt.EpicWin;
    protected override UiRegion TotalWinUi => FruitCocktailArt.TotalWin;
    protected override Color TrimColor => _free ? Aqua : Pink;
    protected override Color CabinetTint => new Color(0.95f, 0.55f, 0.8f);
    protected override Color AccentColor => _free ? Aqua : Juice;
    protected override Color MeterPanelTop => new Color(0.22f, 0.03f, 0.22f, 0.95f);
    protected override Color MeterPanelBottom => new Color(0.06f, 0.01f, 0.07f, 0.95f);
    protected override Color ValueTop => new Color(1f, 0.98f, 0.85f);
    protected override Color ValueBottom => new Color(1f, 0.55f, 0.18f);
    protected override Color LabelColor => new Color(1f, 0.72f, 0.92f);

    static readonly Color Pink = new Color(1f, 0.2f, 0.6f), Juice = new Color(1f, 0.55f, 0.12f), Aqua = new Color(0.2f, 0.92f, 1f),
        Lime = new Color(0.55f, 1f, 0.25f), Gold = new Color(1f, 0.83f, 0.3f), Ice = new Color(0.62f, 0.93f, 1f);

    const float ExpandTime = 0.45f;

    enum Mode { Base, Intro, Free }
    Mode _mode = Mode.Base;

    readonly SlotRandom _cosmetic = new SlotRandom(System.Environment.TickCount ^ 0x5f3a);
    M.Board _board;
    bool _free;
    int _special = -1, _freeIndex, _freeTotal, _scattersLanded, _specialReels;
    long _freeWin, _bonusTotal;
    float _scatterShowUntil = -10f, _idleSince, _retriggerAt = -10f, _totalUntil = -10f;
    TextMeshPro _info, _infoSide, _specialLabel;

    // Special symbol picker.
    bool _picking;
    int _pickShown = -1;
    float _pickStart, _pickChanged, _pickLanded = -10f;

    // Expanding reels.
    readonly float[] _expandAt = new float[Reels];
    readonly bool[] _expandDone = new bool[Reels];
    WinShow _expandShow;

    struct Bubble { public Vector2 p; public float r, speed, phase, wobble; }
    Bubble[] _bubbles;

    static readonly Vector2[] Corners =
    {
        new Vector2(ReelWindow.xMin, ReelWindow.yMax), new Vector2(ReelWindow.xMax, ReelWindow.yMax),
        new Vector2(ReelWindow.xMin, ReelWindow.yMin), new Vector2(ReelWindow.xMax, ReelWindow.yMin),
    };

    protected override void OnBuilt()
    {
        var rng = new System.Random(11);
        _bubbles = new Bubble[48];
        for (int i = 0; i < _bubbles.Length; i++)
            _bubbles[i] = new Bubble
            {
                p = new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble()),
                r = 0.003f + (float)rng.NextDouble() * 0.008f,
                speed = 0.03f + (float)rng.NextDouble() * 0.07f,
                phase = (float)rng.NextDouble() * 10f,
                wobble = 0.003f + (float)rng.NextDouble() * 0.008f,
            };
        ClearExpand();
        var col = new Sym[Rows];
        for (int r = 0; r < Reels; r++)
        {
            for (int row = 0; row < Rows; row++)
            {
                col[row] = Filler(r);
                if (col[row].id == M.Diamond) col[row] = new Sym(M.Cherry);
            }
            reels[r].Set(col);
        }
        _info = midScreen.Text("Info", new Rect(-0.27f, -0.118f, 0.54f, 0.032f), LabelColor);
        _infoSide = midScreen.Text("Info Side", new Rect(-0.06f, -0.032f, 0.32f, 0.045f), Color.white);
        _specialLabel = midScreen.Text("Special Label", new Rect(-0.27f, -0.004f, 0.13f, 0.022f), Gold);
        _idleSince = Time.time;
    }

    protected override Sym Filler(int reel) => new Sym(M.FillerSymbol(_cosmetic, reel));

    static Sym BoardSym(M.Board b, int reel, int row) => new Sym(b.grid[SlotGrid.Cell(reel, row)]);

    static string Name(int s) => s >= 0 ? M.Names[s].ToUpper() : "";

    static Color LineColor(int line) => Color.HSVToRGB(Mathf.Repeat(line * 0.137f + 0.9f, 1f), 0.7f, 1f);

    static void Set(TextMeshPro t, string s)
    {
        if (t != null && t.text != s) t.text = s;
    }

    void ClearExpand()
    {
        for (int r = 0; r < Reels; r++)
        {
            _expandAt[r] = -1f;
            _expandDone[r] = false;
        }
        _expandShow = null;
    }

    // ================================================================ play
    // Variant 0: any free spins; variant 1: free spins with SEVEN as the special symbol.
    protected override bool IsDebugBonusSeed(int seed, int variant)
    {
        var r = M.Spin(seed);
        return r.feature && (variant != 1 || r.special == M.Seven);
    }

    protected override IEnumerator PlaySpin(int seed, int bet)
    {
        var r = M.Spin(seed);
        _free = false;
        _mode = Mode.Base;
        _special = -1;
        ClearExpand();
        _board = r.baseSpin;
        _scattersLanded = 0;
        SetHeader("");

        yield return SpinReels((reel, row) => BoardSym(r.baseSpin, reel, row), r.baseSpin.Anticipate);

        long baseCredits = Credits(r.baseSpin.win, bet);
        if (r.baseSpin.wins.Count > 0)
        {
            ShowWins(r.baseSpin, bet, true, false);
            Sfx(winClip, 0.75f);
            FlashTrim(Juice, 1.2f);
            if (!r.feature && WinTier(baseCredits, bet) >= 1) yield return BigWin(baseCredits, bet);
            else yield return RollUp(0, baseCredits, Mathf.Clamp(0.5f + (float)(baseCredits / (double)bet) * 0.15f, 0.6f, 2.2f));
        }

        long total = baseCredits;
        if (r.feature)
        {
            yield return Bonus(r, bet, baseCredits);
            total = _bonusTotal;
        }
        spinWin = total;
        winMeter = total;
        _idleSince = Time.time;
    }

    void ShowWins(M.Board b, int bet, bool lines, bool expand)
    {
        ClearWins();
        _expandShow = null;
        long sum = 0;
        if (lines)
            foreach (var w in b.wins)
            {
                long credits = Credits(w.pay, bet);
                sum += credits;
                var show = new WinShow { amount = credits, line = w.line };
                if (w.line >= 0)
                {
                    show.color = LineColor(w.line);
                    show.label = $"LINE {w.line + 1}  <color=#FFFFFF>{w.count}x {Name(w.symbol)}</color>  {Digits.Format(credits)}";
                }
                else
                {
                    show.color = Ice;
                    show.label = $"<color=#9FEFFF>{w.count} DIAMONDS</color> PAY ANYWHERE  <color=#FFFFFF>{Digits.Format(credits)}</color>";
                }
                show.cells.AddRange(w.cells);
                winShows.Add(show);
            }
        if (expand && b.expandWin > 0)
        {
            long credits = Credits(b.expandWin, bet);
            sum += credits;
            _expandShow = new WinShow
            {
                amount = credits,
                color = Gold,
                label = $"<color=#FFD34A>{Name(b.special)}</color> ON {b.expandReels.Count} REELS  •  ALL 10 LINES  <color=#FFFFFF>{Digits.Format(credits)}</color>",
            };
            foreach (int reel in b.expandReels)
                for (int row = 0; row < Rows; row++) _expandShow.cells.Add(SlotGrid.Cell(reel, row));
            winShows.Add(_expandShow);
        }
        if (winShows.Count > 0) BeginWinCycle($"{winShows.Count} WINS  <color=#FFFFFF>{Digits.Format(sum)}</color>");
    }

    IEnumerator Bonus(M.Result r, int bet, long baseCredits)
    {
        slam = false;
        yield return new WaitForSeconds(0.4f);

        // Trigger: the diamonds flare up.
        _mode = Mode.Intro;
        _freeTotal = M.FreeSpinsAwarded;
        _freeIndex = 0;
        _freeWin = 0;
        _scatterShowUntil = Time.time + 2.9f;
        Sfx(bonusChimeClip, 0.9f);
        Sfx(SlotSynth.FeatureFanfare(), 0.85f);
        FlashTrim(Aqua, 3f);
        foreach (int c in r.baseSpin.scatterCells)
        {
            Burst(reelScreen, CellCenter(c), 24, Ice, 0.4f, 0.035f);
            Burst(reelScreen, CellCenter(c), 8, Color.white, 0.3f, 0.025f, FxAtlas.Diamond);
        }
        ShowBanner(reelScreen, FruitCocktailArt.FreeSpins, new Vector2(0f, 0.01f), 0.5f, 2.6f, Pink, Layer.OverlayUi);
        yield return new WaitForSeconds(2.9f);
        ClearWins();

        yield return PickSpecial(r.special);

        // Free spins.
        _free = true;
        _mode = Mode.Free;
        StartLoop(Loop.Bed, SlotSynth.CocktailBed(), 0.45f);
        long total = baseCredits;
        winMeter = total;
        foreach (var board in r.freeSpins)
        {
            slam = false;
            _freeIndex++;
            _board = board;
            _scattersLanded = 0;
            _specialReels = 0;
            ClearWins();
            ClearExpand();
            yield return SpinReels((reel, row) => BoardSym(board, reel, row), board.Anticipate, 0.55f, 0.16f);

            long lineCredits = Credits(board.lineWin + board.scatterWin, bet);
            if (lineCredits > 0)
            {
                ShowWins(board, bet, true, false);
                Sfx(winClip, 0.6f);
                FlashTrim(Pink, 1f);
                if (WinTier(lineCredits, bet) >= 1) yield return BigWin(lineCredits, bet);
                else yield return RollUp(total, total + lineCredits, Mathf.Clamp(0.4f + (float)(lineCredits / (double)bet) * 0.1f, 0.5f, 1.6f));
                total += lineCredits;
                _freeWin += lineCredits;
                winMeter = total;
                yield return Wait(board.expandWin > 0 ? 0.6f : 0.9f);
            }

            if (board.expandWin > 0)
            {
                yield return Expand(board);
                long ew = Credits(board.expandWin, bet);
                ShowWins(board, bet, false, true);
                Sfx(winClip, 0.85f);
                Sfx(ascendingChimeClip, 0.6f);
                FlashTrim(Gold, 1.6f);
                if (WinTier(ew, bet) >= 1) yield return BigWin(ew, bet);
                else yield return RollUp(total, total + ew, Mathf.Clamp(0.6f + (float)(ew / (double)bet) * 0.12f, 0.8f, 2f));
                total += ew;
                _freeWin += ew;
                winMeter = total;
                yield return Wait(1.2f);
            }

            if (board.retrigger > 0)
            {
                _freeTotal += board.retrigger;
                _retriggerAt = Time.time;
                _scatterShowUntil = Time.time + 2f;
                Sfx(bonusChimeClip, 0.9f);
                Sfx(SlotSynth.Fizz(), 0.6f);
                ShowBanner(reelScreen, FruitCocktailArt.PlusSpins, new Vector2(0f, 0.01f), 0.36f, 2f, Pink, Layer.OverlayUi);
                foreach (int c in board.scatterCells) Burst(reelScreen, CellCenter(c), 18, Ice, 0.35f, 0.03f);
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
            ShowBanner(reelScreen, FruitCocktailArt.TotalWin, new Vector2(0f, 0.04f), 0.42f, 2.4f, Juice, Layer.OverlayUi);
            _totalUntil = Time.time + 2.4f;
            Sfx(winClip, 0.8f);
            yield return new WaitForSeconds(2.4f);
        }
        winMeter = total;
        _bonusTotal = total;
        _free = false;
        _mode = Mode.Base;
        _special = -1;
        ClearExpand();
        // Back to the spin that triggered it.
        for (int reel = 0; reel < Reels; reel++)
        {
            var col = new Sym[Rows];
            for (int row = 0; row < Rows; row++) col[row] = BoardSym(r.baseSpin, reel, row);
            reels[reel].Set(col);
        }
        _board = r.baseSpin;
    }

    // The fruit roll in a big frame, slowing down until the special symbol lands.
    IEnumerator PickSpecial(int special)
    {
        var order = M.SpecialSymbols;
        int si = System.Array.IndexOf(order, special);
        _picking = true;
        _pickStart = Time.time;
        _pickLanded = -10f;
        _pickShown = order[(si + 3) % order.Length];
        _pickChanged = Time.time;
        Sfx(SlotSynth.Fizz(), 0.7f);
        Sfx(SlotSynth.Whoosh(), 0.5f);
        yield return new WaitForSeconds(0.7f);

        const int steps = 26;
        for (int i = 0; i < steps; i++)
        {
            float u = i / (float)(steps - 1);
            _pickShown = order[((si - (steps - 1 - i)) % order.Length + order.Length) % order.Length];
            _pickChanged = Time.time;
            Sfx(SlotSynth.PickTick(), 0.5f, 0.9f + 0.5f * u);
            yield return new WaitForSeconds(Mathf.Lerp(0.05f, 0.32f, u * u));
        }
        _pickLanded = Time.time;
        _special = special;
        Sfx(SlotSynth.WildLand(), 0.8f);
        Sfx(bonusChimeClip, 0.8f);
        FlashTrim(Gold, 2f);
        Burst(reelScreen, new Vector2(0f, 0.025f), 40, Gold, 0.5f, 0.04f, FxAtlas.Sparkle, Layer.OverlayTop);
        Burst(reelScreen, new Vector2(0f, 0.025f), 16, Pink, 0.35f, 0.03f, FxAtlas.Star, Layer.OverlayTop);
        yield return new WaitForSeconds(2.4f);
        _picking = false;
    }

    // The special symbol fills each reel it landed on, one reel at a time.
    IEnumerator Expand(M.Board b)
    {
        ClearWins();
        yield return Wait(0.2f);
        ShowBanner(reelScreen, FruitCocktailArt.Expanding, new Vector2(0f, 0.115f), 0.3f, 1.2f + 0.55f * b.expandReels.Count, Juice, Layer.OverlayUi);
        Sfx(SlotSynth.Fizz(), 0.55f);
        int n = 0;
        foreach (int reel in b.expandReels)
        {
            _expandAt[reel] = Time.time;
            Sfx(SlotSynth.Expand(), 0.75f, 1f + 0.06f * n++);
            Burst(reelScreen, ColumnRect(reel).center, 26, Gold, 0.45f, 0.035f);
            yield return new WaitForSeconds(ExpandTime + 0.02f);
            for (int row = 0; row < Rows; row++) reels[reel].SetRow(row, new Sym(b.special));
            _expandDone[reel] = true;
            yield return new WaitForSeconds(0.08f);
        }
        yield return new WaitForSeconds(0.3f);
    }

    protected override void OnReelLanded(int reel)
    {
        base.OnReelLanded(reel);
        if (_board == null) return;
        bool special = false;
        for (int row = 0; row < Rows; row++)
        {
            int c = SlotGrid.Cell(reel, row);
            int s = _board.grid[c];
            if (s == M.Diamond)
            {
                _scattersLanded++;
                Sfx(SlotSynth.DiamondLand(), 0.85f, 1f + (_scattersLanded - 1) * 0.1f);
                Burst(reelScreen, CellCenter(c), 16, Ice, 0.32f, 0.03f);
                Burst(reelScreen, CellCenter(c), 6, Color.white, 0.25f, 0.02f, FxAtlas.Diamond);
            }
            else if (s == M.Wild)
            {
                Sfx(SlotSynth.WildLand(), 0.45f);
                Burst(reelScreen, CellCenter(c), 10, Pink, 0.3f, 0.03f);
            }
            if (_free && s == _special)
            {
                special = true;
                Burst(reelScreen, CellCenter(c), 8, Gold, 0.22f, 0.025f);
            }
        }
        if (special)
        {
            _specialReels++;
            Sfx(SlotSynth.PickTick(), 0.55f, 1.1f + _specialReels * 0.12f);
        }
    }

    // ================================================================ draw
    protected override void DrawScreens(float dt)
    {
        DrawLounge(reelScreen, true);
        DrawReels(dt);
        DrawFrame();
        DrawHeader();
        DrawPicker();
        DrawTotal();
        DrawInfo();
        DrawTopper(dt);
    }

    // Neon cocktail-bar backdrop with rising bubbles.
    void DrawLounge(ScreenSurface s, bool reelsBehind)
    {
        var bg = s[Layer.Bg];
        Rect full = s.Bounds;
        float t = Time.time;
        Color top = _free ? new Color(0.03f, 0.24f, 0.32f) : new Color(0.34f, 0.04f, 0.3f);
        Color bottom = _free ? new Color(0.01f, 0.03f, 0.09f) : new Color(0.07f, 0.01f, 0.09f);
        bg.Rect(full, White, top, bottom);
        Rect glow = FxAtlas.Uv(FxAtlas.Glow);
        float sy = full.height;
        bg.Quad(new Vector2(-0.14f + 0.06f * Mathf.Sin(t * 0.23f), sy * 0.15f), new Vector2(0.48f, sy * 0.9f), glow, Pal.A(_free ? Aqua : Pink, 0.28f), 0f, true);
        bg.Quad(new Vector2(0.15f + 0.05f * Mathf.Cos(t * 0.19f), -sy * 0.15f), new Vector2(0.42f, sy * 0.8f), glow, Pal.A(_free ? Lime : Juice, 0.18f), 0f, true);

        Rect ring = FxAtlas.Uv(FxAtlas.Ring), disc = FxAtlas.Uv(FxAtlas.Disc);
        float speedK = _mode == Mode.Base ? 1f : 1.8f;
        foreach (var b in _bubbles)
        {
            float v = Mathf.Repeat(b.p.y + t * b.speed * speedK / Mathf.Max(0.1f, sy), 1f);
            Vector2 p = new Vector2(b.p.x * full.width + Mathf.Sin(t * 1.7f + b.phase) * b.wobble, full.yMin + v * full.height);
            float a = Mathf.Clamp01(Mathf.Min(v, 1f - v) * 8f) * 0.55f;
            bg.Quad(p, Vector2.one * b.r * 2.2f, ring, new Color(0.85f, 0.95f, 1f, a), 0f, true);
            bg.Quad(p + new Vector2(-0.3f, 0.3f) * b.r, Vector2.one * b.r * 0.7f, disc, new Color(1f, 1f, 1f, a * 0.8f), 0f, true);
        }
        if (!reelsBehind) return;

        var rb = s[Layer.ReelBg];
        Color panelTop = _free ? new Color(0.02f, 0.13f, 0.19f, 0.86f) : new Color(0.15f, 0.02f, 0.17f, 0.86f);
        for (int r = 0; r < Reels; r++)
        {
            Rect col = ColumnRect(r);
            rb.Rect(Grow(col, -0.0015f), White, panelTop, new Color(0.03f, 0.005f, 0.05f, 0.9f));
            rb.Quad(col.center, new Vector2(col.width * 1.2f, col.height * 0.9f), glow, Pal.A(_free ? Aqua : Pink, 0.1f), 0f, true);
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
        Border(b, r, 0.0028f, Color.Lerp(c, Color.white, 0.55f), Color.Lerp(c, Color.white, 0.2f));
    }

    void Panel(ScreenSurface s, Rect r, Color neon)
    {
        s[Layer.ReelBg].Rect(r, White, new Color(0.18f, 0.03f, 0.2f, 0.92f), new Color(0.04f, 0.01f, 0.06f, 0.92f));
        NeonFrame(s[Layer.FxTop], r, neon, 0.25f);
    }

    void DrawLine(QuadBatch ln, int line, Color color, float alpha)
    {
        if (line < 0) return;
        var rows = M.Lines[line];
        var pts = new Vector2[Reels + 2];
        pts[0] = new Vector2(ReelWindow.xMin - 0.004f, CellCenter(0, rows[0]).y);
        for (int r = 0; r < Reels; r++) pts[r + 1] = CellCenter(r, rows[r]);
        pts[Reels + 1] = new Vector2(ReelWindow.xMax + 0.004f, CellCenter(Reels - 1, rows[Reels - 1]).y);
        Rect uv = FxAtlas.Uv(FxAtlas.Line);
        for (int i = 0; i < pts.Length - 1; i++)
        {
            ln.Line(pts[i], pts[i + 1], 0.022f, uv, Pal.A(color, 0.55f * alpha), true);
            ln.Line(pts[i], pts[i + 1], 0.008f, uv, Pal.A(Color.Lerp(color, Color.white, 0.6f), alpha));
        }
    }

    void DrawReels(float dt)
    {
        var bs = reelScreen[Layer.Symbols];
        var bt = reelScreen[Layer.SymbolsTop];
        var ln = reelScreen[Layer.Lines];
        var fx = reelScreen[Layer.FxTop];
        var win = CurrentWin(out float phase, out _);
        bool scatterShow = Time.time < _scatterShowUntil;
        float size = Mathf.Min(CellW, CellH) * 1.04f;
        float t = Time.time;
        Rect glow = FxAtlas.Uv(FxAtlas.Glow);

        if (win != null)
        {
            if (win == _expandShow)
                for (int l = 0; l < M.Lines.Length; l++) DrawLine(ln, l, Color.Lerp(Gold, LineColor(l), 0.35f), 0.5f);
            else if (win == allWins)
                foreach (var w in winShows) DrawLine(ln, w.line, w.color, 0.75f);
            else DrawLine(ln, win.line, win.color, 1f);
        }

        for (int r = 0; r < Reels; r++)
        {
            var strip = reels[r];
            if (_expandAt[r] >= 0f)
            {
                float ek = Mathf.Clamp01((t - _expandAt[r]) / ExpandTime);
                DrawExpandGlow(r, ek);
                if (!_expandDone[r])
                {
                    DrawExpanding(r, ek, size);
                    continue;
                }
            }
            bool blur = strip.Blurred;
            for (int k = 0; k < strip.syms.Length; k++)
            {
                float y = strip.SlotY(k);
                if (y - CellH > ReelWindow.yMax || y + CellH < ReelWindow.yMin) continue;
                var s = strip.syms[k];
                Vector2 c = new Vector2(CellCenter(r, 0).x, y);
                int row = k - 1;
                int cell = !strip.Moving && row >= 0 && row < Rows ? SlotGrid.Cell(r, row) : -1;
                bool scatter = s.id == M.Diamond;

                bool hl = win != null && cell >= 0 && win.cells.Contains(cell);
                bool glowScatter = scatterShow && scatter && cell >= 0;
                bool dim = !_picking && ((win != null && !hl) || (scatterShow && !glowScatter));
                float scale = 1f;
                float since = t - strip.landTime;
                if (cell >= 0 && since < 0.35f && (scatter || s.id == M.Wild || (_free && s.id == _special)))
                    scale = 1f + 0.18f * Mathf.Sin(since / 0.35f * Mathf.PI);
                if (hl) scale = 1f + 0.07f * Mathf.Sin(phase * Mathf.PI * 4f);
                if (glowScatter) scale = 1.08f + 0.07f * Mathf.Sin(t * 10f + cell);
                Color tint = dim ? new Color(0.34f, 0.3f, 0.38f) : Color.white;

                bool top = hl || glowScatter;
                if (top)
                {
                    Color g = glowScatter ? Ice : win.color;
                    fx.Quad(c, new Vector2(CellW, CellH) * 1.02f, FxAtlas.Uv(FxAtlas.Frame), Pal.A(g, 0.95f), 0f, true);
                    ln.Quad(c, Vector2.one * size * 1.7f, glow, Pal.A(g, 0.45f + 0.3f * SlotEase.Pulse(10f, cell)), 0f, true);
                    if (glowScatter) ln.Quad(c, Vector2.one * size * 1.5f, FxAtlas.Uv(FxAtlas.Rays), Pal.A(Ice, 0.6f), t * 60f, true);
                }
                else if (!blur && !dim && cell >= 0)
                {
                    // Diamonds twinkle; the special symbol glows in the free spins.
                    if (scatter)
                    {
                        ln.Quad(c, Vector2.one * size * 1.3f, glow, Pal.A(Aqua, 0.18f + 0.12f * SlotEase.Pulse(3f, cell)), 0f, true);
                        if (Random.value < dt * 2.5f)
                            Emit(reelScreen, Layer.FxTop, FxAtlas.Sparkle, c + Random.insideUnitCircle * size * 0.3f, Vector2.zero, Random.Range(0.012f, 0.024f), 0.5f, Color.white, true, 0f, 0f, 120f);
                    }
                    else if (_free && s.id == _special)
                        ln.Quad(c, Vector2.one * size * 1.45f, glow, Pal.A(Gold, 0.25f + 0.15f * SlotEase.Pulse(6f, r)), 0f, true);
                }
                DrawSymbol(top ? bt : bs, s.id, c, size * scale, blur, tint, top ? QuadBatch.Unclipped : ReelWindow);
            }
        }

        // Anticipation: a fizzing column.
        if (anticipatingReel >= 0)
        {
            Rect col = ColumnRect(anticipatingReel);
            float k = Mathf.Clamp01((t - anticipationStart) / 0.3f);
            float p = SlotEase.Pulse(16f);
            Color ac = _free ? Gold : Aqua;
            fx.Quad(col.center, new Vector2(col.width * 1.25f, col.height * 1.08f), FxAtlas.Uv(FxAtlas.Frame), Pal.A(Color.Lerp(ac, Color.white, 0.3f * p), k), 0f, true);
            fx.Quad(col.center, new Vector2(col.width * 2.2f, col.height * 1.5f), glow, Pal.A(ac, 0.4f * k), 0f, true);
            if (Random.value < 0.6f)
                Emit(reelScreen, Layer.FxTop, FxAtlas.Ring, new Vector2(col.center.x + Random.Range(-0.045f, 0.045f), col.yMin + 0.01f),
                    new Vector2(Random.Range(-0.01f, 0.01f), Random.Range(0.12f, 0.3f)), Random.Range(0.008f, 0.02f), Random.Range(0.6f, 1.1f), new Color(0.8f, 0.97f, 1f), true);
            if (Random.value < 0.4f)
                Emit(reelScreen, Layer.FxTop, FxAtlas.Diamond, new Vector2(col.center.x + Random.Range(-0.05f, 0.05f), col.center.y + Random.Range(-0.15f, 0.15f)),
                    Random.insideUnitCircle * 0.12f, Random.Range(0.01f, 0.02f), Random.Range(0.4f, 0.7f), Color.white, true, 0f, 1f, 200f);
        }
    }

    void DrawExpandGlow(int reel, float k)
    {
        Rect col = ColumnRect(reel);
        float p = SlotEase.Pulse(6f, reel);
        reelScreen[Layer.ReelBg].Rect(Grow(col, -0.0015f), White, new Color(1f, 0.55f, 0.15f, 0.4f * k), new Color(0.85f, 0.1f, 0.45f, 0.4f * k));
        reelScreen[Layer.Lines].Quad(col.center, new Vector2(col.width * 1.6f, col.height * 1.15f), FxAtlas.Uv(FxAtlas.Glow), Pal.A(Gold, (0.3f + 0.2f * p) * k), 0f, true);
        reelScreen[Layer.FxTop].Quad(col.center, new Vector2(col.width * 1.06f, col.height * 1.03f), FxAtlas.Uv(FxAtlas.Frame), Pal.A(Gold, 0.9f * k), 0f, true);
        float since = Time.time - _expandAt[reel];
        if (since < 0.5f)
            reelScreen[Layer.FxTop].Quad(col.center, Vector2.one * (0.05f + since * 0.7f), FxAtlas.Uv(FxAtlas.Ring), new Color(1f, 0.9f, 0.6f, 1f - since / 0.5f), 0f, true);
        if (Random.value < 0.25f)
            Emit(reelScreen, Layer.FxTop, FxAtlas.Sparkle, new Vector2(col.center.x + Random.Range(-0.045f, 0.045f), Random.Range(col.yMin, col.yMax)),
                new Vector2(0f, 0.05f), Random.Range(0.01f, 0.022f), 0.6f, Color.white, true, 0f, 0f, 120f);
    }

    // Mid-expansion: the old symbols fade while copies of the special slide out to fill the reel.
    void DrawExpanding(int reel, float k, float size)
    {
        var strip = reels[reel];
        var bs = reelScreen[Layer.Symbols];
        var bt = reelScreen[Layer.SymbolsTop];
        float fade = 1f - SlotEase.InOut(Mathf.Clamp01(k * 2.2f));
        float slide = SlotEase.OutBack(k, 1.8f);
        for (int row = 0; row < Rows; row++)
        {
            Vector2 target = CellCenter(reel, row);
            var s = strip.Visible(row);
            if (s.id == _special)
            {
                DrawSymbol(bt, _special, target, size * (1f + 0.15f * Mathf.Sin(k * Mathf.PI)), false, Color.white, QuadBatch.Unclipped);
                continue;
            }
            if (fade > 0f) DrawSymbol(bs, s.id, target, size * (0.7f + 0.3f * fade), false, new Color(1f, 1f, 1f, fade), ReelWindow);
            int src = -1;
            for (int d = 1; d < Rows && src < 0; d++)
            {
                if (row - d >= 0 && strip.Visible(row - d).id == _special) src = row - d;
                else if (row + d < Rows && strip.Visible(row + d).id == _special) src = row + d;
            }
            Vector2 from = src >= 0 ? CellCenter(reel, src) : target;
            DrawSymbol(bt, _special, Vector2.LerpUnclamped(from, target, slide), size * Mathf.Lerp(0.75f, 1f, Mathf.Clamp01(k * 1.6f)), false, Color.white, QuadBatch.Unclipped);
        }
    }

    void DrawFrame()
    {
        var fx = reelScreen[Layer.FxTop];
        Color neon = _free ? Aqua : Pink;
        NeonFrame(fx, Grow(ReelWindow, 0.008f), neon, 0.4f + 0.15f * SlotEase.Pulse(2f));
        GoldFrame(fx, ReelWindow, 0.0045f);
        for (int r = 1; r < Reels; r++)
        {
            float x = ReelWindow.xMin + r * CellW;
            fx.Rect(new Rect(x - 0.0008f, ReelWindow.yMin, 0.0016f, ReelWindow.height), White, Pal.A(Color.Lerp(neon, Color.white, 0.4f), 0.55f), Pal.A(neon, 0.25f));
        }
        for (int i = 0; i < Corners.Length; i++)
        {
            fx.Quad(Corners[i], Vector2.one * 0.034f, FxAtlas.Uv(FxAtlas.Glow), Pal.A(Ice, 0.45f + 0.3f * SlotEase.Pulse(3f, i)), 0f, true);
            fx.Quad(Corners[i], Vector2.one * 0.017f, FxAtlas.Uv(FxAtlas.Diamond), new Color(0.8f, 0.97f, 1f), 0f, true);
        }
    }

    void DrawHeader()
    {
        var rb = reelScreen[Layer.ReelBg];
        rb.Rect(HeaderRect, White, _free ? new Color(0.02f, 0.16f, 0.22f, 0.95f) : new Color(0.22f, 0.03f, 0.2f, 0.95f), new Color(0.04f, 0.01f, 0.06f, 0.95f));
        NeonFrame(reelScreen[Layer.FxTop], HeaderRect, _free ? Aqua : Pink, 0.3f);

        if (Time.time - _retriggerAt < 2.2f)
        {
            SetHeader($"<color=#FF5AB0>+{M.FreeSpinsRetrigger} FREE SPINS!</color>");
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
            case Mode.Intro:
                if (_picking && _pickLanded > 0f) SetHeader($"<color=#FFD34A>{Name(_special)}</color> EXPANDS & PAYS ALL 10 LINES!");
                else if (_picking) SetHeader("PICKING THE <color=#7FE8FF>SPECIAL SYMBOL</color>...");
                else SetHeader($"<color=#7FE8FF>DIAMOND BONUS!</color>  {M.FreeSpinsAwarded} FREE SPINS");
                return;
            case Mode.Free:
                SetHeader($"FREE SPIN <color=#FFFFFF>{_freeIndex}</color> OF <color=#FFFFFF>{_freeTotal}</color>   SPECIAL <color=#FFD34A>{Name(_special)}</color>");
                return;
        }
        if (IsSpinning)
        {
            SetHeader("GOOD LUCK!");
            return;
        }
        string[] idle =
        {
            $"3 <color=#7FE8FF>DIAMONDS</color> TRIGGER {M.FreeSpinsAwarded} FREE SPINS",
            "FREE SPINS: A <color=#FFD34A>SPECIAL SYMBOL</color> EXPANDS & PAYS ALL LINES",
            "<color=#FF5AB0>COCKTAIL WILD</color> SUBSTITUTES FOR ALL FRUIT",
            "10 LINES  •  PULL THE LEVER TO SPIN",
        };
        SetHeader(idle[(int)((Time.time - _idleSince) / 3.5f) % idle.Length]);
    }

    void DrawPicker()
    {
        if (!_picking) return;
        var s = reelScreen;
        float t = Time.time - _pickStart;
        float a = Mathf.Clamp01(t / 0.3f);
        bool landed = _pickLanded > 0f;
        float lt = Time.time - _pickLanded;
        Color accent = landed ? Gold : Aqua;
        var ln = s[Layer.Lines];
        var bt = s[Layer.SymbolsTop];
        var fx = s[Layer.FxTop];
        var ui = s[Layer.Ui];
        Rect glow = FxAtlas.Uv(FxAtlas.Glow);

        ln.Rect(ReelWindow, White, new Color(0.03f, 0f, 0.06f, 0.8f * a), new Color(0.03f, 0f, 0.06f, 0.8f * a));
        Vector2 c = new Vector2(0f, 0.025f);
        ln.Quad(c, Vector2.one * 0.42f, FxAtlas.Uv(FxAtlas.Rays), Pal.A(accent, (landed ? 0.65f : 0.3f) * a), Time.time * (landed ? 60f : 25f), true);
        ln.Quad(c, Vector2.one * 0.26f, glow, Pal.A(landed ? Gold : Pink, 0.55f * a), 0f, true);

        var reg = FruitCocktailArt.Special;
        float bw = 0.36f * SlotEase.OutBack(t / 0.45f, 2f);
        fx.Quad(new Vector2(0f, 0.135f), new Vector2(bw * 1.2f, 0.06f), glow, Pal.A(Aqua, 0.4f * a), 0f, true);
        ui.Quad(new Vector2(0f, 0.135f), new Vector2(bw, bw / reg.Aspect), reg.Uv, new Color(1f, 1f, 1f, a));

        float sinceTick = Time.time - _pickChanged;
        float pop = landed ? 1f + 0.35f * Mathf.Exp(-lt * 5f) * Mathf.Cos(lt * 18f) : 1f + 0.1f * Mathf.Clamp01(1f - sinceTick / 0.1f);
        fx.Quad(c, Vector2.one * 0.165f * pop, FxAtlas.Uv(FxAtlas.Frame), Pal.A(accent, a), 0f, true);
        DrawSymbol(bt, _pickShown, c, 0.14f * pop, false, new Color(1f, 1f, 1f, a), QuadBatch.Unclipped);

        for (int i = 0; i < M.SpecialSymbols.Length; i++)
        {
            int sym = M.SpecialSymbols[i];
            Vector2 p = new Vector2(-0.21f + i * 0.06f, -0.112f);
            bool on = sym == _pickShown;
            if (on)
            {
                fx.Quad(p, Vector2.one * 0.06f, FxAtlas.Uv(FxAtlas.Frame), Pal.A(accent, a), 0f, true);
                ln.Quad(p, Vector2.one * 0.1f, glow, Pal.A(accent, 0.6f * a), 0f, true);
            }
            DrawSymbol(bt, sym, p, on ? 0.056f : 0.046f, false, on ? new Color(1f, 1f, 1f, a) : new Color(0.5f, 0.48f, 0.55f, a), QuadBatch.Unclipped);
        }
        if (landed && Random.value < 0.5f)
            Emit(s, Layer.OverlayTop, FxAtlas.Sparkle, c + Random.insideUnitCircle * 0.09f, Vector2.zero, Random.Range(0.015f, 0.03f), 0.5f, Color.white, true, 0f, 0f, 90f);
    }

    // TOTAL WIN amount under its banner.
    void DrawTotal()
    {
        float left = _totalUntil - Time.time;
        if (left <= 0f) return;
        float a = Mathf.Clamp01(left / 0.3f) * Mathf.Clamp01((2.4f - left) / 0.3f);
        var s = reelScreen;
        s[Layer.OverlayFx].Rect(ReelWindow, White, new Color(0.03f, 0f, 0.06f, 0.6f * a), new Color(0.03f, 0f, 0.06f, 0.6f * a));
        Digits.Draw(s[Layer.OverlayDigits], Digits.Format(_freeWin), new Vector2(0f, -0.05f), 0.042f, new Color(1f, 0.98f, 0.8f), Juice, 0.5f, null, a);
    }

    // Upper screen, lower section: the bonus / free spins panel.
    void DrawInfo()
    {
        var s = midScreen;
        DrawLounge(s, false);
        Rect full = s.Bounds;
        float t = Time.time;
        var ui = s[Layer.Ui];
        var fx = s[Layer.FxTop];
        var dg = s[Layer.Digits];
        var bt = s[Layer.SymbolsTop];
        var ln = s[Layer.Lines];
        Rect glow = FxAtlas.Uv(FxAtlas.Glow);

        bool free = _free || (_mode == Mode.Intro && _special >= 0);
        var banner = free ? FruitCocktailArt.FreeSpins : FruitCocktailArt.Bonus;
        float bw = (free ? 0.42f : 0.3f) * (1f + 0.015f * Mathf.Sin(t * 2f));
        Vector2 bp = new Vector2(0f, full.yMax - 0.05f);
        fx.Quad(bp, new Vector2(bw * 1.15f, 0.12f), glow, Pal.A(free ? Pink : Aqua, 0.35f), 0f, true);
        ui.Quad(bp, new Vector2(bw, bw / banner.Aspect), banner.Uv, Color.white);

        if (free)
        {
            Set(_infoSide, "");
            Set(_info, "");
            Rect tile = new Rect(-0.27f, -0.145f, 0.13f, 0.17f);
            Panel(s, tile, Gold);
            Vector2 tc = tile.center + new Vector2(0f, -0.012f);
            ln.Quad(tc, Vector2.one * 0.17f, glow, Pal.A(Juice, 0.35f + 0.2f * SlotEase.Pulse(4f)), 0f, true);
            ln.Quad(tc, Vector2.one * 0.15f, FxAtlas.Uv(FxAtlas.Rays), Pal.A(Gold, 0.3f), t * 30f, true);
            DrawSymbol(bt, _special, tc, 0.12f * (1f + 0.04f * Mathf.Sin(t * 3f)), false, Color.white, QuadBatch.Unclipped);
            Set(_specialLabel, "SPECIAL");

            Rect row1 = new Rect(-0.12f, -0.04f, 0.39f, 0.065f);
            Panel(s, row1, Pink);
            ui.Quad(new Vector2(-0.05f, row1.center.y), new Vector2(0.1f, 0.05f), FruitCocktailArt.Spins.Uv, Color.white);
            Digits.Draw(dg, Mathf.Max(0, _freeTotal - _freeIndex).ToString(), new Vector2(0.13f, row1.center.y), 0.03f, Color.white, Aqua);
            Rect row2 = new Rect(-0.12f, -0.135f, 0.39f, 0.065f);
            Panel(s, row2, Aqua);
            ui.Quad(new Vector2(-0.05f, row2.center.y), new Vector2(0.1f, 0.05f), FruitCocktailArt.Win.Uv, Color.white);
            Digits.Draw(dg, Digits.Format(_freeWin), new Vector2(0.13f, row2.center.y), 0.028f, new Color(1f, 0.97f, 0.8f), Juice);
        }
        else
        {
            Set(_specialLabel, "");
            Rect row = new Rect(-0.27f, -0.045f, 0.54f, 0.07f);
            Panel(s, row, Aqua);
            for (int i = 0; i < 3; i++)
            {
                Vector2 p = new Vector2(-0.22f + i * 0.055f, row.center.y);
                ln.Quad(p, Vector2.one * 0.08f, glow, Pal.A(Aqua, 0.3f + 0.25f * SlotEase.Pulse(5f, i)), 0f, true);
                DrawSymbol(bt, M.Diamond, p, 0.064f * (1f + 0.05f * Mathf.Sin(t * 4f + i)), false, Color.white, QuadBatch.Unclipped);
            }
            Set(_infoSide, $"= <color=#FFFFFF>{M.FreeSpinsAwarded} FREE SPINS</color>");
            string[] msgs =
            {
                "+ A SPECIAL SYMBOL THAT EXPANDS ON ANY REEL",
                "DIAMONDS PAY ANYWHERE: 3 = 5x  4 = 25x  5 = 200x BET",
                $"DIAMONDS DURING FREE SPINS ADD +{M.FreeSpinsRetrigger} SPINS",
            };
            Set(_info, msgs[(int)(t / 3.5f + 1.5f) % msgs.Length]);
        }
        float sweep = Mathf.Repeat(t * 0.3f, 1.6f) - 0.3f;
        s[Layer.OverlayFx].QuadClipped(new Vector2(-0.35f + sweep * 0.7f, 0f), new Vector2(0.06f, full.height * 1.1f), FxAtlas.Uv(FxAtlas.SoftRect), new Color(1f, 0.9f, 1f, 0.08f), 12f, s.Bounds, true);
    }

    // Upper screen, top section: logo with marquee bulbs.
    void DrawTopper(float dt)
    {
        var s = topScreen;
        DrawLounge(s, false);
        Rect full = s.Bounds;
        float t = Time.time;
        bool hot = _mode != Mode.Base || BigWinActive;
        var logo = FruitCocktailArt.Logo;
        float w = 0.5f * (1f + 0.012f * Mathf.Sin(t * 1.4f));
        s[Layer.FxTop].Quad(Vector2.zero, new Vector2(w * 1.1f, w / logo.Aspect * 1.8f), FxAtlas.Uv(FxAtlas.Glow), Pal.A(_free ? Aqua : Pink, hot ? 0.75f : 0.4f), 0f, true);
        s[Layer.Ui].Quad(Vector2.zero, new Vector2(w, w / logo.Aspect), logo.Uv, Color.white);
        float sweep = Mathf.Repeat(t * 0.25f, 1.5f) - 0.25f;
        s[Layer.OverlayFx].QuadClipped(new Vector2(-0.35f + sweep * 0.7f, 0f), new Vector2(0.07f, 0.2f), FxAtlas.Uv(FxAtlas.SoftRect), new Color(1f, 0.95f, 0.9f, 0.16f), 14f, s.Bounds, true);
        if (Random.value < dt * (hot ? 14f : 4f))
            Emit(s, Layer.OverlayFx, FxAtlas.Sparkle, new Vector2(Random.Range(-0.25f, 0.25f), Random.Range(-0.07f, 0.07f)), Vector2.zero, Random.Range(0.012f, 0.03f), 0.6f, Color.white, true, 0f, 0f, 90f);
        Bulbs(s, Grow(full, -0.014f), hot);
    }

    void Bulbs(ScreenSurface s, Rect r, bool hot)
    {
        var fx = s[Layer.FxTop];
        const float spacing = 0.03f;
        int total = Mathf.Max(8, Mathf.RoundToInt(2f * (r.width + r.height) / spacing));
        int step = (int)(Time.time * (hot ? 14f : 5f));
        Rect disc = FxAtlas.Uv(FxAtlas.Disc), bulb = FxAtlas.Uv(FxAtlas.Bulb);
        for (int i = 0; i < total; i++)
        {
            Vector2 p = Perimeter(r, i / (float)total);
            bool lit = hot ? (i + step) % 2 == 0 : (i + step) % 4 == 0;
            Color c = i % 2 == 0 ? new Color(1f, 0.85f, 0.4f) : (_free ? Aqua : Pink);
            fx.Quad(p, Vector2.one * 0.013f, disc, new Color(0.25f, 0.12f, 0.05f));
            fx.Quad(p, Vector2.one * 0.0095f, disc, lit ? Color.Lerp(c, Color.white, 0.5f) : Pal.Mul(c, 0.4f));
            if (lit) fx.Quad(p, Vector2.one * 0.035f, bulb, c, 0f, true);
        }
    }

    static Vector2 Perimeter(Rect r, float u)
    {
        float d = u * 2f * (r.width + r.height);
        if (d < r.width) return new Vector2(r.xMin + d, r.yMax);
        d -= r.width;
        if (d < r.height) return new Vector2(r.xMax, r.yMax - d);
        d -= r.height;
        if (d < r.width) return new Vector2(r.xMax - d, r.yMin);
        d -= r.width;
        return new Vector2(r.xMin, r.yMin + d);
    }
}
