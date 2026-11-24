using System.Collections.Generic;

// GOLDEN INFERNO - 5x3, 20 paylines, Hold & Spin.
// Fire orbs carry credit prizes. Land 6 or more anywhere and they lock in
// place for INFERNO RESPINS: 3 respins, every new orb sticks and resets the
// count to 3. Orbs in the respins can also carry the MINI / MINOR / MAJOR
// jackpots, and filling all 15 spots wins the GRAND.
namespace VideoSlots
{
    public static class InfernoMath
    {
        public const int Orb = 0, OrbMini = 1, OrbMinor = 2, OrbMajor = 3, OrbGrand = 4,
            Wild = 5, Crown = 6, Chest = 7, Ruby = 8, Bars = 9,
            A = 10, K = 11, Q = 12, J = 13, Ten = 14, Empty = 15;
        public const int SymbolCount = 16;

        public static readonly string[] Names =
        {
            "Orb", "Mini", "Minor", "Major", "Grand", "Wild", "Crown", "Chest", "Ruby", "Bars",
            "A", "K", "Q", "J", "10", "Empty",
        };

        public static bool IsOrb(int s) => s <= OrbGrand;

        public const int MiniMultiplier = 20, MinorMultiplier = 50;
        public const int MajorBase = 200, GrandBase = 1000;
        public const int OrbsToTrigger = 6;
        public const int RespinCount = 3;

        public static readonly int[][] Lines =
        {
            new[] { 1, 1, 1, 1, 1 }, new[] { 0, 0, 0, 0, 0 }, new[] { 2, 2, 2, 2, 2 },
            new[] { 0, 1, 2, 1, 0 }, new[] { 2, 1, 0, 1, 2 }, new[] { 0, 0, 1, 2, 2 },
            new[] { 2, 2, 1, 0, 0 }, new[] { 1, 0, 0, 0, 1 }, new[] { 1, 2, 2, 2, 1 },
            new[] { 1, 0, 1, 2, 1 }, new[] { 1, 2, 1, 0, 1 }, new[] { 0, 1, 1, 1, 0 },
            new[] { 2, 1, 1, 1, 2 }, new[] { 0, 1, 0, 1, 0 }, new[] { 2, 1, 2, 1, 2 },
            new[] { 1, 1, 0, 1, 1 }, new[] { 1, 1, 2, 1, 1 }, new[] { 0, 0, 2, 0, 0 },
            new[] { 2, 2, 0, 2, 2 }, new[] { 0, 2, 2, 2, 0 },
        };

        // Line pays for 3 / 4 / 5 of a kind, in line bets (total bet / 20).
        static readonly double[][] Pays = BuildPays();
        static double[][] BuildPays()
        {
            var p = new double[SymbolCount][];
            for (int i = 0; i < SymbolCount; i++) p[i] = new double[6];
            void Set(int s, double three, double four, double five) { p[s][3] = three; p[s][4] = four; p[s][5] = five; }
            Set(Wild, 120, 600, 2500);
            Set(Crown, 75, 300, 1200);
            Set(Chest, 60, 220, 850);
            Set(Ruby, 50, 170, 600);
            Set(Bars, 36, 120, 480);
            Set(A, 18, 60, 240);
            Set(K, 18, 60, 240);
            Set(Q, 12, 42, 180);
            Set(J, 12, 36, 145);
            Set(Ten, 12, 36, 145);
            return p;
        }
        public static double LinePay(int symbol, int count) => count >= 3 && count <= 5 ? Pays[symbol][count] : 0;

        // Symbols that can land in the base game, and how often per reel.
        public static readonly int[] BaseSymbols = { Orb, Wild, Crown, Chest, Ruby, Bars, A, K, Q, J, Ten };
        static readonly int[][] ReelWeights =
        {
            //          Orb Wild Crown Chest Ruby Bars  A   K   Q   J  10
            new[] {  14,   0,   4,    5,    6,   7, 12, 12, 14, 15, 15 },
            new[] {  14,   4,   4,    5,    6,   7, 12, 12, 14, 15, 15 },
            new[] {  14,   4,   4,    5,    6,   7, 12, 12, 14, 15, 15 },
            new[] {  14,   4,   4,    5,    6,   7, 12, 12, 14, 15, 15 },
            new[] {  14,   4,   4,    5,    6,   7, 12, 12, 14, 15, 15 },
        };

        // Credit orbs: multiples of the total bet.
        public static readonly int[] OrbValues = { 1, 2, 3, 4, 5, 8, 10, 15, 25 };
        static readonly int[] OrbValueWeights = { 30, 26, 18, 10, 7, 4, 3, 1, 1 };

        // Respins: chance per empty spot per respin, and what a new orb is.
        public const double RespinLandChance = 0.068;
        static readonly int[] RespinOrbKinds = { Orb, OrbMini, OrbMinor, OrbMajor };
        static readonly int[] RespinOrbWeights = { 880, 85, 30, 5 };

        public sealed class RespinLanding { public int cell, symbol, value; }
        public sealed class RespinStep
        {
            public readonly List<RespinLanding> landings = new List<RespinLanding>();
            public int respinsLeft; // after this respin
        }

        public sealed class Result
        {
            public readonly int[] grid = new int[SlotGrid.Cells];
            public readonly int[] orbValue = new int[SlotGrid.Cells]; // credit orbs: x bet
            public readonly List<SlotWin> wins = new List<SlotWin>();
            public double lineWin;  // x bet
            public int orbCount;

            public bool feature;
            public readonly List<RespinStep> respins = new List<RespinStep>();
            public readonly int[] finalGrid = new int[SlotGrid.Cells];   // after the respins (Empty where nothing landed)
            public readonly int[] finalValue = new int[SlotGrid.Cells];
            public bool grand;
            public int minis, minors, majors;
            public double featureCredits; // x bet, credit orbs only (jackpots are paid separately)

            public bool Anticipate(int reel)
            {
                // 4+ orbs on the reels before this one: the remaining reels
                // spin longer, it might be the feature.
                int count = 0;
                for (int c = 0; c < reel * SlotGrid.Rows; c++) if (IsOrb(grid[c])) count++;
                return count >= OrbsToTrigger - 2 && count < SlotGrid.Cells;
            }
        }

        public static int RandomOrbValue(SlotRandom rng) => OrbValues[rng.Weighted(OrbValueWeights)];

        // Random reel symbol for the spinning animation (no meaning).
        public static int FillerSymbol(SlotRandom rng, int reel) => BaseSymbols[rng.Weighted(ReelWeights[reel])];

        public static Result Spin(int seed)
        {
            var rng = new SlotRandom(seed);
            var r = new Result();

            for (int reel = 0; reel < SlotGrid.Reels; reel++)
            {
                for (int row = 0; row < SlotGrid.Rows; row++)
                {
                    int cell = SlotGrid.Cell(reel, row);
                    int s = BaseSymbols[rng.Weighted(ReelWeights[reel])];
                    r.grid[cell] = s;
                    if (s == Orb)
                    {
                        r.orbValue[cell] = RandomOrbValue(rng);
                        r.orbCount++;
                    }
                }
            }

            EvaluateLines(r);

            if (r.orbCount >= OrbsToTrigger) PlayFeature(r, rng);
            return r;
        }

        static void EvaluateLines(Result r)
        {
            for (int l = 0; l < Lines.Length; l++)
            {
                var line = Lines[l];
                int first = -1;
                int count = 0;
                // Leading wilds count toward whatever symbol follows them.
                int wildRun = 0;
                for (int reel = 0; reel < SlotGrid.Reels; reel++)
                {
                    int s = r.grid[SlotGrid.Cell(reel, line[reel])];
                    if (IsOrb(s)) break;
                    if (s == Wild) { if (first < 0) wildRun++; count++; continue; }
                    if (first < 0) { first = s; count++; continue; }
                    if (s == first) { count++; continue; }
                    break;
                }

                double best = 0; int bestSymbol = -1, bestCount = 0;
                if (first >= 0 && count >= 3) { best = LinePay(first, count); bestSymbol = first; bestCount = count; }
                if (wildRun >= 3 && LinePay(Wild, wildRun) > best) { best = LinePay(Wild, wildRun); bestSymbol = Wild; bestCount = wildRun; }
                if (best <= 0) continue;

                var win = new SlotWin { symbol = bestSymbol, count = bestCount, line = l, pay = best / Lines.Length };
                for (int reel = 0; reel < bestCount; reel++) win.cells.Add(SlotGrid.Cell(reel, line[reel]));
                r.wins.Add(win);
                r.lineWin += win.pay;
            }
        }

        static void PlayFeature(Result r, SlotRandom rng)
        {
            r.feature = true;
            int locked = 0;
            for (int c = 0; c < SlotGrid.Cells; c++)
            {
                bool orb = r.grid[c] == Orb;
                r.finalGrid[c] = orb ? Orb : Empty;
                r.finalValue[c] = orb ? r.orbValue[c] : 0;
                if (orb) locked++;
            }

            int left = RespinCount;
            while (left > 0 && locked < SlotGrid.Cells)
            {
                var step = new RespinStep();
                for (int c = 0; c < SlotGrid.Cells; c++)
                {
                    if (r.finalGrid[c] != Empty || !rng.Chance(RespinLandChance)) continue;
                    int kind = RespinOrbKinds[rng.Weighted(RespinOrbWeights)];
                    int value = kind == Orb ? RandomOrbValue(rng) : 0;
                    r.finalGrid[c] = kind;
                    r.finalValue[c] = value;
                    step.landings.Add(new RespinLanding { cell = c, symbol = kind, value = value });
                    locked++;
                }
                left = step.landings.Count > 0 ? RespinCount : left - 1;
                if (locked >= SlotGrid.Cells) left = 0;
                step.respinsLeft = left;
                r.respins.Add(step);
            }

            r.grand = locked >= SlotGrid.Cells;
            for (int c = 0; c < SlotGrid.Cells; c++)
            {
                switch (r.finalGrid[c])
                {
                    case Orb: r.featureCredits += r.finalValue[c]; break;
                    case OrbMini: r.minis++; break;
                    case OrbMinor: r.minors++; break;
                    case OrbMajor: r.majors++; break;
                }
            }
        }
    }
}
