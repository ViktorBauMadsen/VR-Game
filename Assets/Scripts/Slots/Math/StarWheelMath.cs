using System.Collections.Generic;

// STAR WHEEL - 5x3, 243 ways, wheel bonus + free spins with multiplier wilds.
// Wins pay for matching symbols on adjacent reels from the left, anywhere on
// the reels (every combination of positions is a "way"). WHEEL scatters land
// on reels 1, 3 and 5; all three spin the big wheel on top of the cabinet.
// The wheel pays a credit prize, the jackpot, or FREE SPINS: in the free
// spins wilds land more often and carry x2 / x3, and multipliers on the same
// way multiply together.
namespace VideoSlots
{
    public static class StarWheelMath
    {
        public const int Scatter = 0, Wild = 1, Planet = 2, Rocket = 3, Ufo = 4, Moon = 5,
            A = 6, K = 7, Q = 8, J = 9, Ten = 10;
        public const int SymbolCount = 11;
        // Art-only ids (the atlas has these after the real symbols).
        public const int WildX2Art = 11, WildX3Art = 12, ArtCount = 13;

        public static readonly string[] Names = { "Wheel", "Wild", "Planet", "Rocket", "UFO", "Moon", "A", "K", "Q", "J", "10" };

        public const int FreeSpinsAwarded = 8, FreeSpinsRetrigger = 5, MaxFreeSpins = 50;

        // Ways pays for 3 / 4 / 5 reels, per way, in units of the total bet.
        static readonly double[][] Pays = BuildPays();
        static double[][] BuildPays()
        {
            var p = new double[SymbolCount][];
            for (int i = 0; i < SymbolCount; i++) p[i] = new double[6];
            void Set(int s, double three, double four, double five) { p[s][3] = three; p[s][4] = four; p[s][5] = five; }
            Set(Planet, 1.1, 3.6, 11);
            Set(Rocket, 0.9, 2.8, 8);
            Set(Ufo, 0.75, 2.2, 5.5);
            Set(Moon, 0.55, 1.6, 4);
            Set(A, 0.28, 0.75, 2.2);
            Set(K, 0.28, 0.75, 2.2);
            Set(Q, 0.22, 0.55, 1.5);
            Set(J, 0.14, 0.45, 1.1);
            Set(Ten, 0.14, 0.45, 1.1);
            return p;
        }
        public static double WaysPay(int symbol, int count) => count >= 3 && count <= 5 ? Pays[symbol][count] : 0;

        public static readonly int[] ReelSymbols = { Scatter, Wild, Planet, Rocket, Ufo, Moon, A, K, Q, J, Ten };
        static readonly int[][] BaseWeights =
        {
            //        Sc Wild Pl Ro Uf Mo  A  K  Q  J 10
            new[] { 7,  0, 4, 5, 6, 7, 10, 10, 11, 12, 12 },
            new[] { 0,  4, 4, 5, 6, 7, 10, 10, 11, 12, 12 },
            new[] { 7,  4, 4, 5, 6, 7, 10, 10, 11, 12, 12 },
            new[] { 0,  4, 4, 5, 6, 7, 10, 10, 11, 12, 12 },
            new[] { 7,  0, 4, 5, 6, 7, 10, 10, 11, 12, 12 },
        };
        static readonly int[][] FreeWeights =
        {
            new[] { 5,  0, 5, 6, 6, 7, 10, 10, 11, 12, 12 },
            new[] { 0, 10, 5, 6, 6, 7, 10, 10, 11, 12, 12 },
            new[] { 5, 10, 5, 6, 6, 7, 10, 10, 11, 12, 12 },
            new[] { 0, 10, 5, 6, 6, 7, 10, 10, 11, 12, 12 },
            new[] { 5,  0, 5, 6, 6, 7, 10, 10, 11, 12, 12 },
        };
        static readonly int[] FreeWildMultipliers = { 2, 3 };
        static readonly int[] FreeWildMultiplierWeights = { 70, 30 };

        // ------------------------------------------------------------ wheel
        public enum WedgeKind { Credits, FreeSpins, Jackpot }
        public sealed class Wedge
        {
            public WedgeKind kind;
            public int multiplier; // x bet (credits / jackpot)
            public int weight;
        }
        // In order around the wheel, clockwise from the top.
        public static readonly Wedge[] Wheel =
        {
            W(WedgeKind.Jackpot, 500, 1),
            W(WedgeKind.Credits, 20, 120),
            W(WedgeKind.Credits, 50, 40),
            W(WedgeKind.FreeSpins, 0, 160),
            W(WedgeKind.Credits, 15, 130),
            W(WedgeKind.Credits, 100, 14),
            W(WedgeKind.Credits, 25, 100),
            W(WedgeKind.Credits, 40, 60),
            W(WedgeKind.Credits, 250, 4),
            W(WedgeKind.Credits, 15, 130),
            W(WedgeKind.FreeSpins, 0, 160),
            W(WedgeKind.Credits, 30, 90),
            W(WedgeKind.Credits, 75, 22),
            W(WedgeKind.Credits, 20, 120),
            W(WedgeKind.Credits, 150, 8),
            W(WedgeKind.FreeSpins, 0, 160),
        };
        static Wedge W(WedgeKind k, int m, int w) => new Wedge { kind = k, multiplier = m, weight = w };
        static int[] WheelWeights()
        {
            var w = new int[Wheel.Length];
            for (int i = 0; i < w.Length; i++) w[i] = Wheel[i].weight;
            return w;
        }
        static readonly int[] _wheelWeights = WheelWeights();

        // ------------------------------------------------------------ result
        public sealed class Board
        {
            public readonly int[] grid = new int[SlotGrid.Cells];
            public readonly int[] mult = new int[SlotGrid.Cells]; // wild multiplier (1 = none)
            public readonly List<SlotWin> wins = new List<SlotWin>();
            public double win; // x bet
            public int scatters;
            public readonly List<int> scatterCells = new List<int>();
            public int retrigger; // free spins added by this spin

            public bool Anticipate(int reel)
            {
                // Two WHEEL scatters already down and the last scatter reel is still spinning.
                if (reel != 4) return false;
                return scatters - (HasScatterOn(4) ? 1 : 0) >= 2;
            }
            bool HasScatterOn(int reel)
            {
                foreach (int c in scatterCells) if (SlotGrid.ReelOf(c) == reel) return true;
                return false;
            }
        }

        public sealed class Result
        {
            public Board baseSpin;
            public bool wheel;
            public int wedge = -1;
            public readonly List<Board> freeSpins = new List<Board>();
            public double wheelWin; // x bet (credits / jackpot wedge)
            public double freeSpinWin; // x bet
            public double Total => baseSpin.win + wheelWin + freeSpinWin;
        }

        public static int FillerSymbol(SlotRandom rng, int reel, bool free)
        {
            var w = (free ? FreeWeights : BaseWeights)[reel];
            int s = ReelSymbols[rng.Weighted(w)];
            return s == Scatter && rng.Chance(0.5) ? ReelSymbols[rng.Weighted(w)] : s;
        }

        public static Result Spin(int seed)
        {
            var rng = new SlotRandom(seed);
            var r = new Result { baseSpin = DealBoard(rng, false) };

            if (r.baseSpin.scatters >= 3)
            {
                r.wheel = true;
                r.wedge = rng.Weighted(_wheelWeights);
                var wedge = Wheel[r.wedge];
                if (wedge.kind == WedgeKind.FreeSpins)
                {
                    int left = FreeSpinsAwarded, played = 0;
                    while (left > 0 && played < MaxFreeSpins)
                    {
                        var board = DealBoard(rng, true);
                        left--; played++;
                        if (board.scatters >= 3)
                        {
                            board.retrigger = System.Math.Min(FreeSpinsRetrigger, MaxFreeSpins - played - left);
                            left += board.retrigger;
                        }
                        r.freeSpins.Add(board);
                        r.freeSpinWin += board.win;
                    }
                }
                else
                {
                    r.wheelWin = wedge.multiplier;
                }
            }
            return r;
        }

        static Board DealBoard(SlotRandom rng, bool free)
        {
            var b = new Board();
            var weights = free ? FreeWeights : BaseWeights;
            for (int reel = 0; reel < SlotGrid.Reels; reel++)
            {
                bool scatterUsed = false;
                for (int row = 0; row < SlotGrid.Rows; row++)
                {
                    int cell = SlotGrid.Cell(reel, row);
                    int s = ReelSymbols[rng.Weighted(weights[reel])];
                    // At most one scatter per reel.
                    while (s == Scatter && scatterUsed) s = ReelSymbols[rng.Weighted(weights[reel])];
                    if (s == Scatter) { scatterUsed = true; b.scatters++; b.scatterCells.Add(cell); }
                    b.grid[cell] = s;
                    b.mult[cell] = s == Wild && free ? FreeWildMultipliers[rng.Weighted(FreeWildMultiplierWeights)] : 1;
                }
            }
            EvaluateWays(b);
            return b;
        }

        static void EvaluateWays(Board b)
        {
            for (int s = Planet; s < SymbolCount; s++)
            {
                long ways = 1;
                long multWays = 1; // sum over ways of the product of wild multipliers
                int count = 0;
                for (int reel = 0; reel < SlotGrid.Reels; reel++)
                {
                    int matches = 0, multSum = 0;
                    for (int row = 0; row < SlotGrid.Rows; row++)
                    {
                        int cell = SlotGrid.Cell(reel, row);
                        int g = b.grid[cell];
                        if (g == s || g == Wild) { matches++; multSum += b.mult[cell]; }
                    }
                    if (matches == 0) break;
                    ways *= matches;
                    multWays *= multSum;
                    count++;
                }
                double pay = WaysPay(s, count);
                if (pay <= 0) continue;

                var win = new SlotWin
                {
                    symbol = s, count = count, ways = (int)ways,
                    multiplier = ways > 0 ? (int)System.Math.Max(1, multWays / ways) : 1,
                    pay = pay * multWays,
                };
                for (int reel = 0; reel < count; reel++)
                    for (int row = 0; row < SlotGrid.Rows; row++)
                    {
                        int cell = SlotGrid.Cell(reel, row);
                        if (b.grid[cell] == s || b.grid[cell] == Wild) win.cells.Add(cell);
                    }
                b.wins.Add(win);
                b.win += win.pay;
            }
        }
    }
}
