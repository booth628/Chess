
using System.Numerics;

namespace Chess
{
    public class Board
    {
        public ulong Squares = 0;
        public char Type;
        public Vec OccupiedSquares;
        public int PieceCount
        {
            get { return OccupiedSquares.Count; }
        }
        public static implicit operator ulong(Board b) => b.Squares;
        private class NotationTranslator
        {
            public Dictionary<string, int> Table = new();
            public NotationTranslator()
            {
                string[] files = { "a", "b", "c", "d", "e", "f", "g", "h" };
                string[] ranks = { "1", "2", "3", "4", "5", "6", "7", "8" };
                int index = 0;
                for (int i = 0; i < 8; i++)
                {
                    for (int j = 0; j < 8; j++)
                    {
                        Table[files[j] + ranks[i]] = index;
                        index++;
                    }
                }
            }
        }
        private static readonly NotationTranslator _Translator = new();
        public static int ToIndex(string algnot)
        {
            if (_Translator.Table.ContainsKey(algnot.ToLower()) == false) return -1;
            return _Translator.Table[algnot.ToLower()];
        }
        public static string ToAlgNot(int index)
        {
            foreach (string s in _Translator.Table.Keys)
            {
                if (ToIndex(s) == index) return s;
            }
            return "-";
        }
        public static void PrintSquares (ulong squares)
        {
            string[] lines = new string[8];
            string output = "";
            int bitIndex = 0;

            for (int i = 0; i < 8; i++)
            {
                for (int j = 0; j < 8; j++)
                {
                    bool bit = Board.Contains(squares, bitIndex);
                    lines[i] += (bit ? "1" : "0") + " ";
                    bitIndex++;
                }
                output = lines[i] + "\n" + output;
            }
            Console.WriteLine(output);
        }
        public Board (char type, ulong positions)
        {
            Type = type;
            Squares = positions;
            int size = BitOperations.PopCount(positions);
            OccupiedSquares = type switch
            {
                'K' => new Vec(size),
                'P' => new Vec(size),
                _ => new Vec(int.Min(size + 16, 64)) //allow for 16 promotions to each type, max 64 for weird FEN codes (e.g. "BBBBBBB..."
            };
            for (int i = 0; i < 64; i++)
            {
                if (Contains(i))
                {
                    OccupiedSquares.Add((byte)i);
                }
            }
        }
        public void Update (int prev, int next)
        {
            ulong newSquares = (Squares & ~(Game.UL1 << prev));
            Squares = (newSquares | (Game.UL1 << next));
            for (int i = 0; i < PieceCount; i++)
            {
                if (prev == OccupiedSquares[i])
                {
                    OccupiedSquares[i] = (byte)next;
                }
            }
        }
        public void Insert (int square)
        {
            ulong mask = Game.UL1 << square;
            if (0 != (Squares & mask)) return;
            Squares |= mask;
            OccupiedSquares.Add((byte)square);
        }
        public void Remove(int square)
        {
            Squares &= ~(Game.UL1 << square);
            OccupiedSquares.Remove((byte)square);
        }
        public static bool Contains (ulong squares, int index)
        {
            return 0 != (squares & (Game.UL1 << index));
        }
        public bool Contains (int index)
        {
            return 0 != (Squares & (Game.UL1 << index));
        }
    }
}