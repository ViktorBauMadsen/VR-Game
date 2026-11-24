using System.Collections.Generic;

// FRUIT COCKTAIL (remastered) - 5x3, 10 paylines, classic fruits.
// Cocktail WILDs substitute for every fruit. DIAMONDs are scatters: they pay
// anywhere, and 3 or more start COCKTAIL FREE SPINS. Before the free spins
// one fruit is picked as the SPECIAL symbol: whenever it lands on enough
// reels to pay, it expands to fill those reels and pays on all 10 lines -
// the reels don't have to be next to each other. 3+ diamonds in the free
// spins add 10 more.
namespace VideoSlots
{
    public static class FruitCocktailMath
    {
        public const int Diamond = 0, Wild = 1, Seven = 2, Bar = 3, Bell = 4, Melon = 5, Grapes = 6, Orange = 7, Lemon = 8, Cherry = 9;
        public const int SymbolCount = 10;

        public static readonly string[] Names = { "Diamond", "Wild", "Seven", "BAR", "Bell", "Melon", "Grapes", "Orange", "Lemon", "Cherry" };

        public const int FreeSpinsAwarded = 10, FreeSpinsRetrigger = 10, MaxFreeSpins = 60;

        public static readonly int[][] Lines =
        {
            new[] { 1, 1, 1, 1, 1 }, new[] { 0, 0, 0, 0, 0 }, new[] { 2, 2, 2, 2, 2 },
            new[] { 0, 1, 2, 1, 0 }, new[] { 2, 1, 0, 1, 2 }, new[] { 1, 0, 0, 0, 1 },
            new[] { 1, 2, 2, 2, 1 }, new[] { 0, 0, 1, 2, 2 }, new[] { 2, 2, 1, 0, 0 },
            new[] { 1, 2, 1, 0, 1 },
        };

        // Line pays for 2..5 of a kind, in line bets (total bet / 10).
        static readonly double[][] Pays = BuildPays();
        static double[][] BuildPays()
        {
            var p = new double[SymbolCount][];
            for (int i = 0; i < SymbolCount; i++) p[i] = new double[6];
            void Set(int s, double two, double three, double four, double five) { p[s][2] = two; p[s][3] = three; p[s][4] = four; p[s][5] = five; }
            Set(Wild, 0, 50, 250, 1000);
            Set(Seven, 2, 35, 175, 1000);
            Set(Bar, 0, 30, 120, 600);
            Set(Bell, 0, 25, 75, 300);
            Set(Melon, 0, 20, 50, 175);
            Set(Grapes, 0, 12, 35, 150);
            Set(Orange, 0, 6, 25, 120);
            Set(Lemon, 0, 6, 25, 120);
            Set(Cherry, 1, 6, 25, 120);
            return p;
        }
        public static double LinePay(int symbol, int count) => count >= 2 && count <= 5 ? Pays[symbol][count] : 0;

        // Expanded special symbol on n reels (x total bet: the line pay on all 10 lines).
        // The top three already expand on just 2 reels.
        public static double ExpandPay(int symbol, int reels)
        {
            if (reels == 2)
                switch (symbol)
                {
                    case Seven: return 10;
                    case Bar: return 5;
                    case Bell: return 3;
                }
            return LinePay(symbol, reels);
        }
        public static int ExpandMin(int symbol) => ExpandPay(symbol, 2) > 0 ? 2 : 3;

        // Diamonds anywhere: x total bet.
        public static readonly double[] ScatterPays = { 0, 0, 0, 5, 25, 200 };

        public static readonly int[] ReelSymbols = { Diamond, Wild, Seven, Bar, Bell, Melon, Grapes, Orange, Lemon, Cherry };
        static readonly int[][] ReelWeights =
        {
            //          Dia Wild  7  BAR Bell Melon Grape Orange Lemon Cherry
            new[] {  7,   8,  12,  16,  20,   28,   32,   40,    40,   44 },
            new[] {  7,   8,  12,  16,  20,   28,   32,   40,    40,   44 },
            new[] {  7,   8,  12,  16,  20,   28,   32,   40,    40,   44 },
            new[] {  7,   8,  12,  16,  20,   28,   32,   40,    40,   44 },
            new[] {  7,   8,  12,  16,  20,   28,   32,   40,    40,   44 },
        };

        // Which fruit becomes the expanding SPECIAL symbol.
        public static readonly int[] SpecialSymbols = { Seven, Bar, Bell, Melon, Grapes, Orange, Lemon, Cherry };
        static readonly int[] SpecialWeights = { 5, 8, 10, 13, 15, 16, 16, 17 };

        public sealed class Board
        {
            public readonly int[] grid = new int[SlotGrid.Cells];
            public readonly List<SlotWin> wins = new List<SlotWin>();
            public double lineWin;           // x bet
            public int scatters;
            public readonly List<int> scatterCells = new List<int>();
            public double scatterWin;        // x bet
            // Free spins: the expanding special symbol.
            public int special = -1;
            public readonly List<int> expandReels = new List<int>();
            public double expandWin;         // x bet
            public int retrigger;
            public double win;               // x bet, everything on this board

            public bool HasSymbolOn(int reel, int symbol)
            {
                for (int row = 0; row < SlotGrid.Rows; row++) if (grid[SlotGrid.Cell(reel, row)] == symbol) return true;
                return false;
            }

            public bool Anticipate(int reel)
            {
                // Two diamonds already down: every reel still to come might make it three.
                int dia = 0;
                for (int c = 0; c < reel * SlotGrid.Rows; c++) if (grid[c] == Diamond) dia++;
                if (dia >= 2) return true;
                // Free spins: the special symbol is about to expand big.
                if (special < 0 || reel != SlotGrid.Reels - 1) return false;
                int on = 0;
                for (int r = 0; r < reel; r++) if (HasSymbolOn(r, special)) on++;
                return on >= 3;
            }
        }

        public sealed class Result
        {
            public Board baseSpin;
            public bool feature;
            public int special = -1;
            public readonly List<Board> freeSpins = new List<Board>();
            public double freeSpinWin; // x bet
            public double Total => baseSpin.win + freeSpinWin;
        }

        public static int FillerSymbol(SlotRandom rng, int reel)
        {
            int s = ReelSymbols[rng.Weighted(ReelWeights[reel])];
            return s == Diamond && rng.Chance(0.5) ? ReelSymbols[rng.Weighted(ReelWeights[reel])] : s;
        }

        public static Result Spin(int seed)
        {
            var rng = new SlotRandom(seed);
            var r = new Result { baseSpin = DealBoard(rng, -1) };
            if (r.baseSpin.scatters < 3) return r;

            r.feature = true;
            r.special = SpecialSymbols[rng.Weighted(SpecialWeights)];
            int left = FreeSpinsAwarded, played = 0;
            while (left > 0 && played < MaxFreeSpins)
            {
                left--;
                played++;
                var b = DealBoard(rng, r.special);
                if (b.scatters >= 3 && played + left < MaxFreeSpins)
                {
                    b.retrigger = System.Math.Min(FreeSpinsRetrigger, MaxFreeSpins - played - left);
                    left += b.retrigger;
                }
                r.freeSpins.Add(b);
                r.freeSpinWin += b.win;
            }
            return r;
        }

        static Board DealBoard(SlotRandom rng, int special)
        {
            var b = new Board { special = special };
            for (int reel = 0; reel < SlotGrid.Reels; reel++)
            {
                bool diamond = false;
                for (int row = 0; row < SlotGrid.Rows; row++)
                {
                    int s = ReelSymbols[rng.Weighted(ReelWeights[reel])];
                    while (s == Diamond && diamond) s = ReelSymbols[rng.Weighted(ReelWeights[reel])]; // one per reel
                    if (s == Diamond) diamond = true;
                    b.grid[SlotGrid.Cell(reel, row)] = s;
                }
            }

            EvaluateLines(b);

            for (int c = 0; c < SlotGrid.Cells; c++)
                if (b.grid[c] == Diamond) { b.scatters++; b.scatterCells.Add(c); }
            b.scatterWin = ScatterPays[System.Math.Min(5, b.scatters)];
            if (b.scatterWin > 0)
            {
                var w = new SlotWin { symbol = Diamond, count = b.scatters, pay = b.scatterWin };
                w.cells.AddRange(b.scatterCells);
                b.wins.Add(w);
            }

            if (special >= 0)
            {
                for (int reel = 0; reel < SlotGrid.Reels; reel++)
                    if (b.HasSymbolOn(reel, special)) b.expandReels.Add(reel);
                double pay = ExpandPay(special, b.expandReels.Count);
                if (pay > 0) b.expandWin = pay; // pays on all 10 lines: 10 x (pay / 10) of the bet
                else b.expandReels.Clear();
            }

            b.win = b.lineWin + b.scatterWin + b.expandWin;
            return b;
        }

        static void EvaluateLines(Board b)
        {
            for (int l = 0; l < Lines.Length; l++)
            {
                var line = Lines[l];
                int first = -1, count = 0, wildRun = 0;
                for (int reel = 0; reel < SlotGrid.Reels; reel++)
                {
                    int s = b.grid[SlotGrid.Cell(reel, line[reel])];
                    if (s == Diamond) break;
                    if (s == Wild) { if (first < 0) wildRun++; count++; continue; }
                    if (first < 0) { first = s; count++; continue; }
                    if (s == first) { count++; continue; }
                    break;
                }

                double best = 0; int bestSymbol = -1, bestCount = 0;
                if (first >= 0 && LinePay(first, count) > 0) { best = LinePay(first, count); bestSymbol = first; bestCount = count; }
                if (LinePay(Wild, wildRun) > best) { best = LinePay(Wild, wildRun); bestSymbol = Wild; bestCount = wildRun; }
                if (best <= 0) continue;

                var win = new SlotWin { symbol = bestSymbol, count = bestCount, line = l, pay = best / Lines.Length };
                for (int reel = 0; reel < bestCount; reel++) win.cells.Add(SlotGrid.Cell(reel, line[reel]));
                b.wins.Add(win);
                b.lineWin += win.pay;
            }
        }
    }
}
