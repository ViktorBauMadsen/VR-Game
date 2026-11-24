using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using VideoSlots;

// GOLDEN INFERNO - 5x3, 20 lines, Hold & Spin with four jackpots.
// Fire orbs show a credit prize. 6+ orbs trigger HOLD & SPIN: the orbs lock,
// the rest of the screen becomes 15 single spots, 3 respins that reset every
// time a new orb lands. MINI / MINOR / MAJOR orbs can land in the respins;
// fill all 15 spots for the GRAND. Then every orb is collected into the win.
public class GoldenInfernoSlot : VideoSlotMachine
{
    protected override string SymbolTextureName => "Inferno_Symbols";
    protected override string UiTextureName => "Inferno_UI";
    protected override UiRegion BigWinUi => GoldenInfernoArt.BigWin;
    protected override UiRegion MegaWinUi => GoldenInfernoArt.MegaWin;
    protected override UiRegion EpicWinUi => GoldenInfernoArt.EpicWin;
    protected override UiRegion TotalWinUi => GoldenInfernoArt.TotalWin;
    protected override Color TrimColor => _mode == Mode.Base ? new Color(1f, 0.32f, 0.06f) : new Color(1f, 0.12f, 0.02f);
    protected override Color CabinetTint => new Color(1f, 0.6f, 0.5f);
    protected override Color AccentColor => new Color(1f, 0.55f, 0.12f);

    static readonly Color Fire = new Color(1f, 0.45f, 0.08f), Ember = new Color(1f, 0.25f, 0.03f), GoldHi = new Color(1f, 0.97f, 0.75f), GoldLo = new Color(1f, 0.6f, 0.1f);
    static readonly string[] JackpotNames = { "MINI", "MINOR", "MAJOR", "GRAND" };

    enum Mode { Base, Feature, Collect }
    Mode _mode = Mode.Base;

    readonly SlotRandom _cosmetic = new SlotRandom(System.Environment.TickCount);
    InfernoMath.Result _result;
    readonly ReelStrip[] _cells = new ReelStrip[SlotGrid.Cells];
    readonly bool[] _locked = new bool[SlotGrid.Cells];
    readonly Sym[] _lockedSym = new Sym[SlotGrid.Cells];
    readonly float[] _lockTime = new float[SlotGrid.Cells];
    readonly bool[] _collected = new bool[SlotGrid.Cells];
    readonly Sym[] _cellTarget = new Sym[SlotGrid.Cells];
    int _respinsLeft;
    float _respinsChanged = -10f, _triggerUntil = -10f, _featureStart;
    int _collectCell = -1;
    float _collectStart;
    int _orbsThisSpin;
    readonly float[] _jackpotFlash = new float[4];
    float _idleSince;

    // Big jackpot announcement (collect).
    int _announce = -1;
    long _announceAmount;
    float _announceStart;

    struct Comet { public Vector2 from, to; public float start, duration; public Color color; }
    readonly List<Comet> _comets = new List<Comet>();

    protected override void OnBuilt()
    {
        for (int c = 0; c < SlotGrid.Cells; c++)
        {
            int cell = c;
            var strip = new ReelStrip(1, CellRect(c), CellFiller) { spinSpeed = 16f };
            strip.Landed += () => OnCellLanded(cell);
            strip.Ticked += () => OnReelTick(0);
            _cells[c] = strip;
        }
        // A friendly first screen.
        var start = new Sym[Rows];
        for (int r = 0; r < Reels; r++)
        {
            for (int row = 0; row < Rows; row++) start[row] = Filler(r);
            reels[r].Set(start);
        }
        _idleSince = Time.time;
    }

    static Rect CellRect(int cell)
    {
        int reel = SlotGrid.ReelOf(cell), row = SlotGrid.RowOf(cell);
        return new Rect(ReelWindow.xMin + reel * CellW, ReelWindow.yMax - (row + 1) * CellH, CellW, CellH);
    }

    static Sym ToSym(int id, int value) => new Sym(id, value);

    protected override Sym Filler(int reel)
    {
        int s = InfernoMath.FillerSymbol(_cosmetic, reel);
        return new Sym(s, s == InfernoMath.Orb ? InfernoMath.RandomOrbValue(_cosmetic) : 0);
    }

    Sym CellFiller()
    {
        if (_cosmetic.Chance(0.22)) return new Sym(InfernoMath.Orb, InfernoMath.RandomOrbValue(_cosmetic));
        if (_cosmetic.Chance(0.03)) return new Sym(InfernoMath.OrbMini + _cosmetic.Next(3), 0);
        return new Sym(InfernoMath.Empty);
    }

    // ================================================================ play
    protected override bool IsDebugBonusSeed(int seed, int variant)
    {
        var r = InfernoMath.Spin(seed);
        return variant == 1 ? r.feature && r.grand : r.feature;
    }

    protected override IEnumerator PlaySpin(int seed, int bet)
    {
        var r = InfernoMath.Spin(seed);
        _result = r;
        _mode = Mode.Base;
        _orbsThisSpin = 0;
        _announce = -1;
        SetHeader("");

        yield return SpinReels((reel, row) => ToSym(r.grid[SlotGrid.Cell(reel, row)], r.orbValue[SlotGrid.Cell(reel, row)]), r.Anticipate);

        long lineCredits = Credits(r.lineWin, bet);
        if (r.wins.Count > 0)
        {
            int i = 0;
            foreach (var w in r.wins)
            {
                var show = new WinShow
                {
                    line = w.line,
                    amount = Credits(w.pay, bet),
                    label = $"LINE {w.line + 1}  <color=#FFFFFF>{w.count}x {InfernoMath.Names[w.symbol].ToUpper()}</color>  {Digits.Format(Credits(w.pay, bet))}",
                    color = LineColor(w.line),
                };
                show.cells.AddRange(w.cells);
                winShows.Add(show);
                i++;
            }
            BeginWinCycle($"{r.wins.Count} WINNING LINE{(r.wins.Count > 1 ? "S" : "")}  <color=#FFFFFF>{Digits.Format(lineCredits)}</color>");
            Sfx(winClip, 0.75f);
            FlashTrim(new Color(1f, 0.7f, 0.2f), 1.2f);
            if (!r.feature && WinTier(lineCredits, bet) >= 1) yield return BigWin(lineCredits, bet);
            else yield return RollUp(0, lineCredits, Mathf.Clamp(0.5f + (float)(lineCredits / (double)bet) * 0.15f, 0.6f, 2.2f));
        }

        if (r.feature)
        {
            yield return Feature(r, bet, lineCredits);
        }
        else spinWin = lineCredits;
        _idleSince = Time.time;
    }

    IEnumerator Feature(InfernoMath.Result r, int bet, long lineCredits)
    {
        slam = false;
        // Trigger: orbs flare up.
        yield return new WaitForSeconds(0.35f);
        _triggerUntil = Time.time + 2.4f;
        Sfx(bonusChimeClip, 0.9f);
        Sfx(SlotSynth.FeatureFanfare(), 0.85f);
        FlashTrim(new Color(1f, 0.4f, 0.05f), 2.5f);
        ShowBanner(reelScreen, GoldenInfernoArt.HoldSpin, new Vector2(0f, 0.01f), 0.5f, 2.4f, Fire, Layer.OverlayUi);
        for (int c = 0; c < SlotGrid.Cells; c++)
            if (r.grid[c] == InfernoMath.Orb) Burst(reelScreen, CellCenter(c), 14, Fire, 0.3f, 0.03f, FxAtlas.Flame);
        yield return new WaitForSeconds(2.5f);

        // Into the respins: each spot becomes its own little reel.
        ClearWins();
        _mode = Mode.Feature;
        _featureStart = Time.time;
        for (int c = 0; c < SlotGrid.Cells; c++)
        {
            bool orb = r.grid[c] == InfernoMath.Orb;
            _locked[c] = orb;
            _collected[c] = false;
            _lockedSym[c] = orb ? new Sym(InfernoMath.Orb, r.orbValue[c]) : new Sym(InfernoMath.Empty);
            _lockTime[c] = -10f;
            _cells[c].Set(new[] { orb ? _lockedSym[c] : new Sym(InfernoMath.Empty) });
        }
        _respinsLeft = InfernoMath.RespinCount;
        _respinsChanged = Time.time;
        StartLoop(Loop.Bed, SlotSynth.InfernoBed(), 0.5f);
        yield return new WaitForSeconds(0.8f);

        foreach (var step in r.respins)
        {
            int open = 0;
            for (int c = 0; c < SlotGrid.Cells; c++) if (!_locked[c]) open++;
            foreach (int c in OpenCells())
            {
                _cellTarget[c] = new Sym(InfernoMath.Empty);
                _cells[c].Begin(Random.Range(0f, 0.12f));
            }
            foreach (var land in step.landings) _cellTarget[land.cell] = new Sym(land.symbol, land.value);
            Sfx(SlotSynth.Whoosh(), 0.35f, 1.2f);
            yield return new WaitForSeconds(0.55f);

            // Stop left to right, top to bottom; the last few open spots get a suspense spin.
            var order = OpenCells();
            for (int i = 0; i < order.Count; i++)
            {
                int c = order[i];
                bool suspense = open <= 3;
                if (suspense)
                {
                    StartLoop(Loop.Fx, SlotSynth.Anticipation(), 0.6f, false);
                    _cells[c].spinSpeed = 24f;
                    yield return new WaitForSeconds(0.9f);
                }
                _cells[c].Stop(new[] { _cellTarget[c] });
                yield return new WaitForSeconds(suspense ? 0.35f : 0.075f);
                _cells[c].spinSpeed = 16f;
            }
            while (AnyCellMoving()) yield return null;
            StopLoop(Loop.Fx, 4f);

            if (step.landings.Count > 0 && step.respinsLeft > 0)
            {
                Sfx(SlotSynth.RespinReset(), 0.8f);
                if (_respinsLeft < InfernoMath.RespinCount) Sfx(ascendingChimeClip, 0.5f);
            }
            _respinsLeft = step.respinsLeft;
            _respinsChanged = Time.time;
            yield return new WaitForSeconds(0.5f);
        }

        // All 15: the GRAND.
        if (r.grand)
        {
            yield return Announce(3, Credits(InfernoMath.GrandBase, bet));
        }

        // Collect every orb into the win meter.
        _mode = Mode.Collect;
        StopLoop(Loop.Bed, 0.6f);
        yield return new WaitForSeconds(0.6f);
        long total = lineCredits;
        winMeter = total;
        for (int c = 0; c < SlotGrid.Cells; c++)
        {
            int c2 = SlotGrid.Cell(c / Rows, c % Rows); // reel-major order
            var s = _lockedSym[c2];
            if (!_locked[c2]) continue;
            _collectCell = c2;
            _collectStart = Time.time;
            long add;
            int jp = s.id - InfernoMath.OrbMini;
            if (s.id == InfernoMath.Orb) add = Credits(s.value, bet);
            else add = Credits(JackpotMultiplier(jp), bet);

            Sfx(SlotSynth.Collect(), 0.7f, 1f + Mathf.Min(0.5f, c * 0.03f));
            _comets.Add(new Comet { from = CellCenter(c2), to = MeterBox(2).center, start = Time.time, duration = 0.32f, color = s.id == InfernoMath.Orb ? Fire : GoldenInfernoArt.JackpotColors[Mathf.Clamp(jp, 0, 3)] });
            yield return new WaitForSeconds(0.32f);
            if (s.id != InfernoMath.Orb)
            {
                yield return Announce(jp, add);
            }
            long from = total;
            total += add;
            yield return RollUp(from, total, s.id == InfernoMath.Orb ? 0.22f : 0.8f);
            _collected[c2] = true;
            yield return new WaitForSeconds(0.05f);
        }
        if (r.grand)
        {
            long add = Credits(InfernoMath.GrandBase, bet);
            Sfx(SlotSynth.Collect(), 0.8f, 0.8f);
            long from = total;
            total += add;
            yield return RollUp(from, total, 1.2f);
        }
        _collectCell = -1;
        yield return new WaitForSeconds(0.4f);

        spinWin = total;
        if (WinTier(total, bet) >= 1) yield return BigWin(total, bet);
        else
        {
            ShowBanner(reelScreen, GoldenInfernoArt.TotalWin, new Vector2(0f, 0.03f), 0.42f, 2.2f, Fire, Layer.OverlayUi);
            Sfx(winClip, 0.8f);
            yield return new WaitForSeconds(2.2f);
        }

        // Back to the base game, showing the spin that triggered it.
        _mode = Mode.Base;
        for (int reel = 0; reel < Reels; reel++)
        {
            var col = new Sym[Rows];
            for (int row = 0; row < Rows; row++)
            {
                int cell = SlotGrid.Cell(reel, row);
                col[row] = new Sym(r.grid[cell], r.orbValue[cell]);
            }
            reels[reel].Set(col);
        }
        winMeter = total;
    }

    static int JackpotMultiplier(int jp) => jp == 0 ? InfernoMath.MiniMultiplier : jp == 1 ? InfernoMath.MinorMultiplier : jp == 2 ? InfernoMath.MajorBase : InfernoMath.GrandBase;

    IEnumerator Announce(int jackpot, long amount)
    {
        _announce = jackpot;
        _announceAmount = amount;
        _announceStart = Time.time;
        _jackpotFlash[jackpot] = Time.time + 4f;
        Sfx(SlotSynth.JackpotFanfare(), 1f);
        Sfx(bonusChimeClip, 0.6f);
        FlashTrim(GoldenInfernoArt.JackpotColors[jackpot], 4f);
        Burst(reelScreen, new Vector2(0f, 0.02f), 60, new Color(1f, 0.85f, 0.4f), 0.6f, 0.05f, FxAtlas.Sparkle, Layer.OverlayTop);
        yield return new WaitForSeconds(jackpot >= 2 ? 3.6f : 2.6f);
        _announce = -1;
    }

    List<int> OpenCells()
    {
        var list = new List<int>();
        for (int reel = 0; reel < Reels; reel++)
            for (int row = 0; row < Rows; row++)
            {
                int c = SlotGrid.Cell(reel, row);
                if (!_locked[c]) list.Add(c);
            }
        return list;
    }

    bool AnyCellMoving()
    {
        foreach (var c in _cells) if (c.Moving) return true;
        return false;
    }

    void OnCellLanded(int cell)
    {
        var s = _cellTarget[cell];
        Sfx(reelStopClip, 0.35f, 1.2f);
        if (!InfernoMath.IsOrb(s.id)) return;
        _locked[cell] = true;
        _lockedSym[cell] = s;
        _lockTime[cell] = Time.time;
        int count = 0;
        foreach (bool l in _locked) if (l) count++;
        Sfx(SlotSynth.OrbLand(), 0.9f, 0.9f + count * 0.025f);
        Burst(reelScreen, CellCenter(cell), 18, Fire, 0.35f, 0.03f, FxAtlas.Flame);
        Burst(reelScreen, CellCenter(cell), 8, Color.white, 0.25f, 0.03f);
        if (s.id != InfernoMath.Orb)
        {
            int jp = s.id - InfernoMath.OrbMini;
            _jackpotFlash[jp] = Time.time + 2.5f;
            Sfx(ascendingChimeClip, 0.7f);
        }
    }

    protected override void OnReelLanded(int reel)
    {
        base.OnReelLanded(reel);
        if (_result == null || _mode != Mode.Base) return;
        bool any = false;
        for (int row = 0; row < Rows; row++)
            if (InfernoMath.IsOrb(_result.grid[SlotGrid.Cell(reel, row)])) { any = true; _orbsThisSpin++; Burst(reelScreen, CellCenter(reel, row), 10, Fire, 0.25f, 0.028f, FxAtlas.Flame); }
        if (any) Sfx(SlotSynth.OrbLand(), 0.75f, 0.85f + _orbsThisSpin * 0.05f);
        if (reels[reel].Visible(0).id == InfernoMath.Wild || reels[reel].Visible(1).id == InfernoMath.Wild || reels[reel].Visible(2).id == InfernoMath.Wild)
            Sfx(SlotSynth.WildLand(), 0.45f);
    }

    protected override void UpdateReels(float dt)
    {
        base.UpdateReels(dt);
        if (_mode != Mode.Base)
            foreach (var c in _cells) c.Update(dt);
    }

    static Color LineColor(int line) => Color.HSVToRGB(Mathf.Repeat(line * 0.137f + 0.05f, 1f), 0.75f, 1f);

    // ================================================================ draw
    protected override void DrawScreens(float dt)
    {
        DrawBackground(dt);
        if (_mode == Mode.Base) DrawBaseReels();
        else DrawFeatureCells();
        DrawReelFrame();
        DrawHeader();
        DrawComets();
        DrawAnnounce();
        DrawJackpotMeters();
        DrawTopper(dt);
    }

    void DrawBackground(float dt)
    {
        var bg = reelScreen[Layer.Bg];
        Rect full = reelScreen.Bounds;
        bool feature = _mode != Mode.Base;
        Color top = feature ? new Color(0.45f, 0.05f, 0.02f) : new Color(0.3f, 0.04f, 0.02f);
        bg.Rect(full, White, top, new Color(0.04f, 0f, 0f));
        float t = Time.time;
        bg.Quad(new Vector2(Mathf.Sin(t * 0.3f) * 0.12f, -0.12f), new Vector2(0.7f, 0.35f), FxAtlas.Uv(FxAtlas.Glow), new Color(1f, 0.3f, 0.05f, feature ? 0.45f : 0.28f), 0f, true);
        bg.Quad(new Vector2(-Mathf.Sin(t * 0.23f) * 0.15f, 0.1f), new Vector2(0.5f, 0.3f), FxAtlas.Uv(FxAtlas.Glow), new Color(0.8f, 0.1f, 0.02f, 0.25f), 0f, true);
        if (Random.value < dt * (feature ? 40f : 16f))
            Emit(reelScreen, Layer.Bg, FxAtlas.Glow, new Vector2(Random.Range(-0.29f, 0.29f), -0.26f), new Vector2(Random.Range(-0.02f, 0.02f), Random.Range(0.06f, 0.16f)),
                Random.Range(0.006f, 0.016f), Random.Range(2f, 4f), Color.Lerp(Ember, new Color(1f, 0.8f, 0.3f), Random.value), true);

        // Reel panels.
        var rb = reelScreen[Layer.ReelBg];
        for (int r = 0; r < Reels; r++)
        {
            Rect col = ColumnRect(r);
            rb.Rect(col, White, new Color(0.13f, 0.02f, 0.02f, 0.9f), new Color(0.03f, 0f, 0f, 0.92f));
            rb.Quad(col.center, new Vector2(col.width * 1.3f, col.height * 0.9f), FxAtlas.Uv(FxAtlas.Glow), new Color(0.6f, 0.08f, 0.02f, 0.18f), 0f, true);
            if (r > 0) rb.Rect(new Rect(col.xMin - 0.0008f, col.yMin, 0.0016f, col.height), White, new Color(1f, 0.8f, 0.4f, 0.7f), new Color(0.6f, 0.3f, 0.05f, 0.7f));
        }
        // Top/bottom inner shadow.
        rb.Rect(new Rect(ReelWindow.xMin, ReelWindow.yMax - 0.025f, ReelWindow.width, 0.025f), White, new Color(0f, 0f, 0f, 0.55f), new Color(0f, 0f, 0f, 0f));
        rb.Rect(new Rect(ReelWindow.xMin, ReelWindow.yMin, ReelWindow.width, 0.025f), White, new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0.55f));
    }

    void DrawReelFrame()
    {
        var fx = reelScreen[Layer.FxTop];
        GoldFrame(fx, ReelWindow, 0.006f);
        // Anticipation: the reel still spinning is on fire.
        if (anticipatingReel >= 0)
        {
            Rect col = ColumnRect(anticipatingReel);
            float p = SlotEase.Pulse(14f);
            float k = Mathf.Clamp01((Time.time - anticipationStart) / 0.3f);
            fx.Quad(col.center, new Vector2(col.width * 1.25f, col.height * 1.08f), FxAtlas.Uv(FxAtlas.Frame), new Color(1f, 0.5f + 0.3f * p, 0.1f, 0.9f * k), 0f, true);
            fx.Quad(col.center, new Vector2(col.width * 2f, col.height * 1.4f), FxAtlas.Uv(FxAtlas.Glow), new Color(1f, 0.3f, 0.05f, 0.35f * k), 0f, true);
            if (Random.value < 0.7f)
                Emit(reelScreen, Layer.FxTop, FxAtlas.Flame, new Vector2(col.center.x + Random.Range(-col.width * 0.5f, col.width * 0.5f), col.yMin + 0.01f),
                    new Vector2(0f, Random.Range(0.15f, 0.35f)), Random.Range(0.025f, 0.05f), Random.Range(0.4f, 0.8f), new Color(1f, Random.Range(0.25f, 0.6f), 0.05f), true);
        }
    }

    void DrawBaseReels()
    {
        var bs = reelScreen[Layer.Symbols];
        var bt = reelScreen[Layer.SymbolsTop];
        var dg = reelScreen[Layer.Digits];
        var ln = reelScreen[Layer.Lines];
        var fx = reelScreen[Layer.FxTop];
        var win = CurrentWin(out float phase, out bool first);
        bool trigger = Time.time < _triggerUntil;
        float size = Mathf.Min(CellW, CellH) * 1.04f;

        // Win lines under the highlighted symbols.
        if (win != null)
        {
            if (win == allWins)
            {
                foreach (var w in winShows) DrawLine(ln, w.line, w.color, 0.75f);
            }
            else DrawLine(ln, win.line, win.color, 1f);
        }

        for (int r = 0; r < Reels; r++)
        {
            var strip = reels[r];
            bool blur = strip.Blurred;
            for (int k = 0; k < strip.syms.Length; k++)
            {
                float y = strip.SlotY(k);
                if (y - CellH > ReelWindow.yMax || y + CellH < ReelWindow.yMin) continue;
                var s = strip.syms[k];
                if (s.id == InfernoMath.Empty) continue;
                Vector2 c = new Vector2(CellCenter(r, 0).x, y);
                int row = k - 1;
                int cell = !strip.Moving && row >= 0 && row < Rows ? SlotGrid.Cell(r, row) : -1;
                bool isOrb = InfernoMath.IsOrb(s.id);

                bool hl = win != null && cell >= 0 && win.cells.Contains(cell);
                bool dim = win != null && !hl && !trigger;
                bool glowOrb = trigger && isOrb && cell >= 0;
                float scale = 1f;
                float since = Time.time - strip.landTime;
                if (isOrb && since < 0.35f && cell >= 0) scale = 1f + 0.18f * Mathf.Sin(since / 0.35f * Mathf.PI);
                if (hl) scale = 1f + 0.07f * Mathf.Sin(phase * Mathf.PI * 4f);
                if (glowOrb) scale = 1.06f + 0.06f * Mathf.Sin(Time.time * 12f + cell);
                Color tint = dim ? new Color(0.35f, 0.3f, 0.3f) : Color.white;

                var target = hl || glowOrb ? bt : bs;
                if (hl || glowOrb)
                {
                    Color g = glowOrb ? Fire : win.color;
                    fx.Quad(c, new Vector2(CellW, CellH) * 1.02f, FxAtlas.Uv(FxAtlas.Frame), Pal.A(g, 0.9f), 0f, true);
                    ln.Quad(c, Vector2.one * size * 1.7f, FxAtlas.Uv(FxAtlas.Glow), Pal.A(g, 0.5f + 0.3f * SlotEase.Pulse(10f, cell)), 0f, true);
                }
                DrawSymbol(target, s.id, c, size * scale, blur, tint, hl || glowOrb ? QuadBatch.Unclipped : ReelWindow);
                if (s.id == InfernoMath.Orb && s.value > 0)
                    DrawOrbValue(dg, s.value, c, scale, dim ? 0.4f : 1f, ReelWindow);
            }
        }
    }

    void DrawOrbValue(QuadBatch dg, int value, Vector2 c, float scale, float alpha, Rect clip)
    {
        string txt = Digits.Format((long)value * curBet, true);
        float cap = 0.019f * scale;
        float w = Digits.Measure(txt, cap);
        float maxW = CellW * 0.82f;
        float sx = w > maxW ? maxW / w : 1f;
        Digits.Draw(dg, txt, c + new Vector2(0f, -0.004f), cap, GoldHi, GoldLo, 0.5f, clip, alpha, sx);
    }

    void DrawLine(QuadBatch ln, int line, Color color, float alpha)
    {
        if (line < 0) return;
        var rows = InfernoMath.Lines[line];
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

    void DrawFeatureCells()
    {
        var rb = reelScreen[Layer.ReelBg];
        var bs = reelScreen[Layer.Symbols];
        var bt = reelScreen[Layer.SymbolsTop];
        var dg = reelScreen[Layer.Digits];
        var ln = reelScreen[Layer.Lines];
        var fx = reelScreen[Layer.FxTop];
        float size = Mathf.Min(CellW, CellH) * 1.0f;
        float intro = Mathf.Clamp01((Time.time - _featureStart) / 0.5f);

        for (int c = 0; c < SlotGrid.Cells; c++)
        {
            Rect cr = CellRect(c);
            Rect tile = Grow(cr, -0.0025f);
            Vector2 center = cr.center;
            rb.Rect(tile, White, new Color(0.2f, 0.03f, 0.02f, intro), new Color(0.05f, 0f, 0f, intro));
            Border(rb, Grow(tile, -0.0012f), 0.0012f, new Color(1f, 0.55f, 0.2f, 0.6f * intro), new Color(0.5f, 0.1f, 0.02f, 0.6f * intro));

            if (_locked[c])
            {
                var s = _lockedSym[c];
                float since = Time.time - _lockTime[c];
                float scale = since < 0.45f ? SlotEase.OutBack(since / 0.45f, 3f) : 1f;
                bool collecting = c == _collectCell;
                if (collecting) scale *= 1.15f + 0.05f * Mathf.Sin(Time.time * 30f);
                float dimK = _mode == Mode.Collect && _collected[c] && !collecting ? 0.45f : 1f;
                Color g = s.id == InfernoMath.Orb ? Fire : GoldenInfernoArt.JackpotColors[s.id - InfernoMath.OrbMini];
                ln.Quad(center, Vector2.one * size * (1.6f + 0.15f * SlotEase.Pulse(6f, c)), FxAtlas.Uv(FxAtlas.Glow), Pal.A(g, (collecting ? 0.95f : 0.45f) * dimK), 0f, true);
                if (since < 0.6f) fx.Quad(center, Vector2.one * size * (1f + since * 3f), FxAtlas.Uv(FxAtlas.Ring), Pal.A(g, 1f - since / 0.6f), 0f, true);
                DrawSymbol(bt, s.id, center, size * scale, false, new Color(dimK, dimK, dimK), QuadBatch.Unclipped);
                if (s.id == InfernoMath.Orb) DrawOrbValue(dg, s.value, center, scale, dimK, QuadBatch.Unclipped);
                if (collecting) fx.Quad(center, new Vector2(CellW, CellH), FxAtlas.Uv(FxAtlas.Frame), new Color(1f, 0.9f, 0.5f, 1f), 0f, true);
                continue;
            }

            var strip = _cells[c];
            for (int k = 0; k < strip.syms.Length; k++)
            {
                var s = strip.syms[k];
                if (s.id == InfernoMath.Empty) continue;
                Vector2 p = new Vector2(center.x, strip.SlotY(k));
                DrawSymbol(bs, s.id, p, size * 0.92f, strip.Blurred, new Color(0.85f, 0.85f, 0.85f), tile);
                if (s.id == InfernoMath.Orb) DrawOrbValue(dg, s.value, p, 0.92f, 0.85f, tile);
            }
            if (_mode == Mode.Collect)
                rb.Rect(tile, White, new Color(0f, 0f, 0f, 0.4f), new Color(0f, 0f, 0f, 0.4f));
        }
    }

    void DrawHeader()
    {
        var rb = reelScreen[Layer.ReelBg];
        rb.Rect(HeaderRect, White, new Color(0.18f, 0.03f, 0.02f, 0.95f), new Color(0.06f, 0f, 0f, 0.95f));
        GoldFrame(reelScreen[Layer.FxTop], HeaderRect, 0.0025f);

        if (_mode == Mode.Feature)
        {
            SetHeader("");
            var ui = reelScreen[Layer.Ui];
            var reg = GoldenInfernoArt.Respins;
            float w = 0.17f;
            Vector2 bannerPos = new Vector2(-0.06f, HeaderRect.center.y);
            ui.Quad(bannerPos, new Vector2(w, w / reg.Aspect), reg.Uv, Color.white);
            float since = Time.time - _respinsChanged;
            var fx = reelScreen[Layer.FxTop];
            for (int i = 0; i < InfernoMath.RespinCount; i++)
            {
                Vector2 at = new Vector2(0.075f + i * 0.04f, HeaderRect.center.y);
                bool lit = i < _respinsLeft;
                float pop = lit && since < 0.4f ? 1f + 0.5f * (1f - since / 0.4f) : 1f;
                fx.Quad(at, Vector2.one * 0.034f, FxAtlas.Uv(FxAtlas.Disc), new Color(0.12f, 0.02f, 0.01f, 1f));
                if (lit)
                {
                    fx.Quad(at, Vector2.one * 0.06f * pop, FxAtlas.Uv(FxAtlas.Glow), new Color(1f, 0.5f, 0.1f, 0.8f), 0f, true);
                    fx.Quad(at, Vector2.one * 0.026f, FxAtlas.Uv(FxAtlas.Disc), new Color(1f, 0.75f + 0.25f * SlotEase.Pulse(6f, i), 0.3f));
                }
                Digits.Draw(reelScreen[Layer.Digits], (i + 1).ToString(), at, 0.012f, lit ? new Color(0.4f, 0.1f, 0f) : new Color(0.5f, 0.3f, 0.2f), lit ? new Color(0.3f, 0.05f, 0f) : new Color(0.3f, 0.15f, 0.1f));
            }
            return;
        }

        var win = CurrentWin(out _, out bool first);
        if (win != null)
        {
            SetHeader(win.label);
            return;
        }
        if (IsSpinning)
        {
            SetHeader(_mode == Mode.Collect ? "COLLECTING FIRE ORBS" : "GOOD LUCK!");
            return;
        }
        string[] idle =
        {
            "6+ FIRE ORBS TRIGGER <color=#FFFFFF>HOLD & SPIN</color>",
            "FILL ALL 15 SPOTS TO WIN THE <color=#FF5A4A>GRAND</color>",
            "20 LINES  •  PULL THE LEVER TO SPIN",
        };
        SetHeader(idle[(int)((Time.time - _idleSince) / 3.5f) % idle.Length]);
    }

    void DrawComets()
    {
        var fx = reelScreen[Layer.OverlayTop];
        for (int i = _comets.Count - 1; i >= 0; i--)
        {
            var cm = _comets[i];
            float u = (Time.time - cm.start) / cm.duration;
            if (u >= 1f)
            {
                Burst(reelScreen, cm.to, 12, cm.color, 0.2f, 0.025f, FxAtlas.Sparkle, Layer.OverlayTop);
                _comets.RemoveAt(i);
                continue;
            }
            float e = u * u;
            Vector2 p = Vector2.Lerp(cm.from, cm.to, e) + new Vector2(0f, Mathf.Sin(u * Mathf.PI) * 0.05f);
            fx.Quad(p, Vector2.one * 0.06f, FxAtlas.Uv(FxAtlas.Glow), Pal.A(cm.color, 0.9f), 0f, true);
            fx.Quad(p, Vector2.one * 0.03f, FxAtlas.Uv(FxAtlas.Sparkle), Color.white, Time.time * 400f, true);
            Emit(reelScreen, Layer.OverlayTop, FxAtlas.Glow, p, Vector2.zero, 0.025f, 0.25f, cm.color, true);
        }
    }

    void DrawAnnounce()
    {
        if (_announce < 0) return;
        float t = Time.time - _announceStart;
        float a = Mathf.Clamp01(t / 0.25f);
        var s = reelScreen;
        Color jc = GoldenInfernoArt.JackpotColors[_announce];
        s[Layer.OverlayFx].Rect(s.Bounds, White, new Color(0f, 0f, 0f, 0.6f * a), new Color(0f, 0f, 0f, 0.6f * a));
        s[Layer.OverlayFx].Quad(new Vector2(0f, 0.03f), Vector2.one * 0.7f, FxAtlas.Uv(FxAtlas.Rays), Pal.A(jc, 0.6f * a), t * 30f, true);
        s[Layer.OverlayFx].Quad(new Vector2(0f, 0.03f), new Vector2(0.6f, 0.35f), FxAtlas.Uv(FxAtlas.Glow), Pal.A(jc, 0.6f * a), 0f, true);
        float pop = SlotEase.OutBack(t / 0.45f, 2.5f);
        var jp = GoldenInfernoArt.Jackpot;
        s[Layer.OverlayUi].Quad(new Vector2(0f, 0.105f), new Vector2(0.36f, 0.09f) * pop, jp.Uv, new Color(1f, 1f, 1f, a));
        var badge = JackpotBadge(_announce);
        s[Layer.OverlayUi].Quad(new Vector2(0f, 0.025f), new Vector2(0.3f, 0.075f) * pop, badge.Uv, new Color(1f, 1f, 1f, a));
        Digits.Draw(s[Layer.OverlayDigits], Digits.Format(_announceAmount), new Vector2(0f, -0.07f), 0.045f * pop, GoldHi, GoldLo, 0.5f, null, a);
        if (Random.value < 0.5f) Emit(s, Layer.OverlayTop, FxAtlas.Sparkle, new Vector2(Random.Range(-0.27f, 0.27f), Random.Range(-0.15f, 0.18f)), Vector2.zero, Random.Range(0.02f, 0.045f), 0.6f, Color.white, true, 0f, 0f, 120f);
    }

    static UiRegion JackpotBadge(int jp) => jp == 0 ? GoldenInfernoArt.Mini : jp == 1 ? GoldenInfernoArt.Minor : jp == 2 ? GoldenInfernoArt.Major : GoldenInfernoArt.Grand;

    // Upper screen, lower section: the four jackpot meters.
    void DrawJackpotMeters()
    {
        var s = midScreen;
        Rect full = s.Bounds;
        s[Layer.Bg].Rect(full, White, new Color(0.22f, 0.03f, 0.02f), new Color(0.05f, 0f, 0f));
        s[Layer.Bg].Quad(new Vector2(0f, 0f), new Vector2(0.8f, 0.4f), FxAtlas.Uv(FxAtlas.Glow), new Color(1f, 0.25f, 0.05f, 0.22f), 0f, true);

        float rowH = 0.07f, top = full.yMax - 0.045f;
        for (int i = 0; i < 4; i++)
        {
            int jp = 3 - i; // GRAND first
            float y = top - i * (rowH + 0.004f);
            bool flash = Time.time < _jackpotFlash[jp];
            Color jc = GoldenInfernoArt.JackpotColors[jp];
            float big = jp == 3 ? 1.12f : 1f;
            Rect panel = new Rect(-0.045f, y - 0.025f * big, 0.31f, 0.05f * big);
            s[Layer.ReelBg].Rect(panel, White, new Color(0.08f, 0.02f, 0.02f, 0.95f), new Color(0.02f, 0f, 0f, 0.95f));
            Border(s[Layer.FxTop], panel, 0.003f, Color.Lerp(jc, Color.white, 0.4f), Pal.Mul(jc, 0.6f));
            if (flash)
            {
                float p = SlotEase.Pulse(20f);
                s[Layer.FxTop].Quad(panel.center, new Vector2(panel.width * 1.3f, panel.height * 2.4f), FxAtlas.Uv(FxAtlas.Glow), Pal.A(jc, 0.5f + 0.4f * p), 0f, true);
            }
            var badge = JackpotBadge(jp);
            float bw = 0.2f * big;
            s[Layer.Ui].Quad(new Vector2(-0.165f, y), new Vector2(bw, bw / badge.Aspect), badge.Uv, Color.white);
            long amount = (long)JackpotMultiplier(jp) * curBet;
            Color vt = flash ? Color.white : GoldHi;
            Digits.Draw(s[Layer.Digits], Digits.Format(amount), new Vector2(panel.center.x, y), 0.026f * big, vt, GoldLo);
        }
        // Sweep of light across the meters.
        float sweep = Mathf.Repeat(Time.time * 0.35f, 1.6f) - 0.3f;
        s[Layer.OverlayFx].Quad(new Vector2(-0.35f + sweep * 0.7f, 0f), new Vector2(0.06f, full.height * 1.1f), FxAtlas.Uv(FxAtlas.SoftRect), new Color(1f, 0.9f, 0.6f, 0.12f), 12f, true);
    }

    // Upper screen, top section: the logo in flames.
    void DrawTopper(float dt)
    {
        var s = topScreen;
        Rect full = s.Bounds;
        s[Layer.Bg].Rect(full, White, new Color(0.08f, 0f, 0f), new Color(0.35f, 0.05f, 0.01f));
        float t = Time.time;
        s[Layer.Bg].Quad(new Vector2(0f, full.yMin + 0.03f), new Vector2(0.9f, 0.25f), FxAtlas.Uv(FxAtlas.Glow), new Color(1f, 0.35f, 0.05f, 0.5f + 0.1f * Mathf.Sin(t * 5f)), 0f, true);
        if (Random.value < dt * 30f)
            Emit(s, Layer.Bg, FxAtlas.Flame, new Vector2(Random.Range(-0.3f, 0.3f), full.yMin - 0.01f), new Vector2(Random.Range(-0.01f, 0.01f), Random.Range(0.08f, 0.16f)),
                Random.Range(0.04f, 0.09f), Random.Range(0.8f, 1.5f), new Color(1f, Random.Range(0.2f, 0.5f), 0.03f, 0.7f), true);
        if (Random.value < dt * 10f)
            Emit(s, Layer.FxTop, FxAtlas.Glow, new Vector2(Random.Range(-0.28f, 0.28f), full.yMin), new Vector2(Random.Range(-0.03f, 0.03f), Random.Range(0.05f, 0.12f)),
                Random.Range(0.006f, 0.012f), Random.Range(1.5f, 3f), new Color(1f, 0.75f, 0.3f), true);

        var logo = GoldenInfernoArt.Logo;
        float w = 0.56f * (1f + 0.012f * Mathf.Sin(t * 1.3f));
        bool hot = _mode != Mode.Base || BigWinActive;
        s[Layer.FxTop].Quad(new Vector2(0f, 0.005f), new Vector2(w * 1.05f, w / logo.Aspect * 1.6f), FxAtlas.Uv(FxAtlas.Glow), new Color(1f, 0.4f, 0.05f, hot ? 0.7f : 0.35f), 0f, true);
        s[Layer.Ui].Quad(new Vector2(0f, 0.005f), new Vector2(w, w / logo.Aspect), logo.Uv, Color.white);
        float sweep = Mathf.Repeat(t * 0.25f, 1.5f) - 0.25f;
        s[Layer.OverlayFx].Quad(new Vector2(-0.35f + sweep * 0.7f, 0.005f), new Vector2(0.07f, 0.2f), FxAtlas.Uv(FxAtlas.SoftRect), new Color(1f, 0.95f, 0.7f, 0.18f), 14f, true);
        if (hot && Random.value < dt * 12f)
            Emit(s, Layer.OverlayFx, FxAtlas.Sparkle, new Vector2(Random.Range(-0.26f, 0.26f), Random.Range(-0.06f, 0.07f)), Vector2.zero, Random.Range(0.015f, 0.035f), 0.5f, Color.white, true, 0f, 0f, 90f);
    }
}
