using System;
using System.Collections.Generic;

// Pure C# (no UnityEngine) so the game math can be simulated outside Unity.
// Every spin is played from a seed: the whole outcome, bonus features
// included, is worked out up front and the machine then animates it. The
// same seed on another player's machine plays the exact same spin.
namespace VideoSlots
{
    public sealed class SlotRandom
    {
        readonly Random _r;
        public SlotRandom(int seed) { _r = new Random(seed); }
        public int Next(int max) => _r.Next(max);
        public int Range(int min, int maxExclusive) => _r.Next(min, maxExclusive);
        public double NextDouble() => _r.NextDouble();
        public bool Chance(double p) => _r.NextDouble() < p;

        public int Weighted(int[] weights)
        {
            int total = 0;
            for (int i = 0; i < weights.Length; i++) total += weights[i];
            int roll = _r.Next(total);
            for (int i = 0; i < weights.Length; i++)
            {
                roll -= weights[i];
                if (roll < 0) return i;
            }
            return weights.Length - 1;
        }
    }

    public static class SlotGrid
    {
        public const int Reels = 5, Rows = 3, Cells = Reels * Rows;
        // Cells are numbered reel-major: cell = reel * 3 + row, row 0 = top.
        public static int Cell(int reel, int row) => reel * Rows + row;
        public static int ReelOf(int cell) => cell / Rows;
        public static int RowOf(int cell) => cell % Rows;
    }

    // One winning combination, for the win presentation.
    public sealed class SlotWin
    {
        public int symbol;
        public int count;          // reels from the left
        public int line = -1;      // payline index (line games), -1 for ways/scatter
        public int ways = 1;       // ways games: number of ways
        public int multiplier = 1; // wild multipliers (ways games)
        public double pay;         // in units of the total bet
        public readonly List<int> cells = new List<int>();
    }
}
