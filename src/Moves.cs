using System.Numerics;
using System.Runtime.Intrinsics.X86;
namespace Chess
{
    public static class MoveManager
    {
        private static ulong[][] RayGen()
        {
            ulong[][] rays = new[]
            {
                new ulong[64], 
                new ulong[64], 
                new ulong[64], 
                new ulong[64], 
                new ulong[64], 
                new ulong[64], 
                new ulong[64], 
                new ulong[64]
            };

            //Populate rays
            for (int i = 0; i < 64; i++)
            {
                rays[0][i] = 0;
                int j = 1;
                //stop once top border is reached
                while ((i + (8 * j)) < 64)
                {
                    //add a positive bit upward one row at a time
                    rays[0][i] |= Game.UL1 << (i + (8 * j));
                    j++;
                }

                rays[4][i] = 0;
                j = 1;
                //stop once bottom border is reached
                while ((i - (8 * j)) >= 0)
                {
                    //add a positive bit downward
                    rays[4][i] |= Game.UL1 << (i - (8 * j));
                    j++;
                }


                rays[2][i] = 0;
                j = 1;
                //stop once right border is reached
                while ((i + j) % 8 != 0)
                {
                    //add a positive bit to the right
                    rays[2][i] |= Game.UL1 << (i + j);
                    j++;
                }

                rays[6][i] = 0;
                j = 1;
                //stop once left border is reached
                while ((i - j + 1) % 8 != 0)
                {
                    //add a positive bit to the left
                    rays[6][i] |= Game.UL1 << (i - j);
                    j++;
                }

                rays[1][i] = 0;
                j = 1;
                while ((i + (9 * j)) < 64 && (i + j) % 8 != 0)
                {
                    //add a positive bit upward and one to the right
                    rays[1][i] |= Game.UL1 << (i + (9 * j));
                    j++;
                }

                rays[3][i] = 0;
                j = 1;
                while ((i - (7 * j)) >= 0 && (i + j) % 8 != 0)
                {
                    //add a positive bit downward and to the right
                    rays[3][i] |= Game.UL1 << (i - (7 * j));
                    j++;
                }

                rays[5][i] = 0;
                j = 1;
                while ((i - (9 * j)) >= 0 && (i - j + 1) % 8 != 0)
                {
                    //add a positive bit downward and to the left
                    rays[5][i] |= Game.UL1 << (i - (9 * j));
                    j++;
                }

                rays[7][i] = 0;
                j = 1;
                while ((i + (7 * j)) < 64 && (i - j + 1) % 8 != 0)
                {
                    //add a positive bit upward and to the left
                    rays[7][i] |= Game.UL1 << (i + (7 * j));
                    j++;
                }
            }
            return rays;
        }
        private static (ulong[], ulong[]) PawnGen()
        {
            //parallel with $dicts; range of starting squares for forward and backwards pawns
            int[][] starts = new[] { new[] { 8, 15 }, new[] { 48, 55 } };
            //int[][] ends = new[] { new[] { 56, 63 }, new[] { 0, 7 } };
            int[] direction = new[] { 1, -1 };
            ulong[][] moves = new ulong[2][]
            {
                new ulong[64],
                new ulong[64]
            };
            for (int i = 0; i < 2; i++)
            {
                for (int j = 0; j < 64; j++)
                {
                    //if (ends[i][0] <= j && j <= ends[i][1]) continue;
                    moves[i][j] = 0;
                    if (starts[i][0] <= j && j <= starts[i][1])
                    {
                        moves[i][j] |= Game.UL1 << (j + 2 * 8 * direction[i]);
                    }
                    moves[i][j] |= Game.UL1 << (j + 8 * direction[i]);
                }
            }
            return (moves[0], moves[1]);
        }

        public static readonly ulong[][] Rays = RayGen();
        public static readonly (ulong[] Up, ulong[] Down) Pawns = PawnGen();
        public static readonly ulong[] Knights = KnightGen();
        public static readonly ulong[] Kings = KingGen();
        private static ulong[] KnightGen()
        {
            ulong[] moves = new ulong[64];
            for (int i = 0; i < 64; i++)
            {
                bool[] keys = new bool[8];
                Array.Fill(keys, true);
                ulong[] values = new ulong[8]
                {
                    (Game.UL1 << (i + 10)),
                    (Game.UL1 << (i + 17)),
                    (Game.UL1 << (i + 15)),
                    (Game.UL1 << (i + 6)),
                    (Game.UL1 << (i - 10)),
                    (Game.UL1 << (i - 17)),
                    (Game.UL1 << (i - 15)),
                    (Game.UL1 << (i - 6)),
                };
                ulong mask = 0;
                if (i < 16)
                {
                    keys[6] = keys[5] = false;
                    if (i < 7) keys[7] = keys[4] = false;
                }
                if (i > 47)
                {
                    keys[1] = keys[2] = false;
                    if (i > 55) keys[0] = keys[3] = false;
                }
                if (i % 8 > 5)
                {
                    keys[0] = keys[7] = false;
                    if (i % 8 == 7) keys[1] = keys[6] = false;
                }
                if (i % 8 < 2)
                {
                    keys[3] = keys[4] = false;
                    if (i % 8 == 0) keys[2] = keys[5] = false;
                }
                for (int j = 0; j < 8; j++)
                {
                    if (keys[j]) mask |= values[j];
                }
                moves[i] = mask;
            }
            return moves;
        }
        private static ulong[] KingGen()
        {
            ulong[] moves = new ulong[64];
            for (int i = 0; i < 64; i++)
            {
                bool[] keys = new bool[8];
                Array.Fill(keys, true);
                ulong[] values = new ulong[8]
                {
                    (Game.UL1 << (i + 8)),
                    (Game.UL1 << (i + 9)),
                    (Game.UL1 << (i + 7)),
                    (Game.UL1 << (i + 1)),
                    (Game.UL1 << (i - 1)),
                    (Game.UL1 << (i - 9)),
                    (Game.UL1 << (i - 7)),
                    (Game.UL1 << (i - 8)),
                };
                ulong mask = 0;

                if (i < 8) keys[7] = keys[6] = keys[5] = false;
                if (i > 55) keys[0] = keys[1] = keys[2] = false;
                if (i % 8 == 0) keys[2] = keys[4] = keys[5] = false;
                if (i % 8 == 7) keys[1] = keys[3] = keys[6] = false;

                for (int j = 0; j < 8; j++)
                {
                    if (keys[j]) mask |= values[j];
                }
                moves[i] = mask;
            }
            return moves;
        }
        private static readonly Dictionary<char, int[]> Directions = new()
        {
            {'R', new [] { 0, 2, 4, 6 } },
            {'B', new [] { 1, 3, 5, 7 } },
            {'Q', new [] { 0, 2, 4, 6, 1, 3, 5, 7 } }
        };
        public static ulong SlideGen(char type, int square, ulong blockers)
        {
            ulong moves = 0;
            foreach (int dir in Directions[type])
            {
                moves |= MaskGen(dir, square, blockers);
            }
            return moves;
        }
        public static ulong MaskGen(int dir, int square, ulong blockers)
        {
            ulong moves = Rays[dir][square];
            ulong relevantBlockers = moves & blockers;
            if ((relevantBlockers) == 0) return moves;
            int MSBlocker;
            if (2 < dir && dir < 7)
            {
                //if this isn't an intrinsic i will never write another line of c#
                //index of most significant positive bit in $relevantBlockers
                MSBlocker = 63 - BitOperations.LeadingZeroCount(relevantBlockers);
            }
            else
            {
                //index of least significant positive bit in $relevantBlockers
                MSBlocker = BitOperations.TrailingZeroCount(relevantBlockers);
            }
            //Console.WriteLine("direction: {0}, most significant blocker: {1}", dir, MSBlocker);
            return moves & (~Rays[dir][MSBlocker]);
        }
        
    }
}