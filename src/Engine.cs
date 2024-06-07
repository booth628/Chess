using Microsoft.VisualBasic;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Security.Principal;
using System.Threading.Tasks.Sources;

namespace Chess
{
    // TYPE ORDER: K, Q, R, B, N, P
    //             2, 3, 4, 5, 6, 7
    public class Engine
    {
        private struct MoveLog
        {
            public char CapturedType;
            public char MovingType;
            public int StartingSquare;
            public int EndingSquare;
            //'k' -> kingside castle, 'q' -> queenside castle, 'e' -> en passant, 'p' -> promotion
            public char SpecialCaseFlag;
            public char PromotionFlag;
            public StatePackage StartingState;
            public MoveLog()
            {
                StartingState = new();
            }
        };
        public struct StatePackage
        {
            public ulong[] Moves;
            public bool[][] CastlingRights;
            public int ActiveColor;
            public int PieceCount;
            public int Clock;
            public int FullMoves;
            public int      EPTarget;
            public int[]? CheckingPieces;
            public StatePackage()
            {
                ActiveColor = 0;
                CastlingRights = new bool[][]
                {
                    //kingside, queenside
                    new bool[2], //w
                    new bool[2] //b
                };
                Moves = new ulong[64];
            }
        }
        public struct MoveResult
        {
            public int Score, PrevIndex, NextIndex;
            public override string ToString()
            {
                return Board.ToAlgNot(PrevIndex) + " to " + Board.ToAlgNot(NextIndex);
            }
        }

        public StatePackage State = new();
        private readonly Stack<MoveLog> History = new();
        public readonly Board[] Players = new Board[2];
        public readonly char[] PieceKeys = new char[] { 'Q', 'R', 'K', 'N', 'B', 'P' }; //ordered left to right by search priority

        public readonly Dictionary<char, Board> Pieces = new();
        public ulong Blockers
        {
            get { return Players[0].Squares | Players[1].Squares; }
        }
        public int ActiveKingSquare
        {
            get { return BitOperations.TrailingZeroCount(Pieces['K'].Squares & Players[State.ActiveColor].Squares); }
        }
        public int InactiveColor
        {
            get { return (State.ActiveColor + 1) % 2; }
        }
        public Engine (string FEN = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")
        {
            string[] sections = FEN.Split(' ');
            string[] lines = sections[0].Split('/');
            int i = 0;
            Dictionary<char, ulong> starts = new()
            {
                ['K'] = 0,
                ['Q'] = 0,
                ['R'] = 0,
                ['N'] = 0,
                ['B'] = 0,
                ['P'] = 0
            };
            ulong[] playerStarts = new ulong[] { 0, 0 };
            foreach (string line in lines.Reverse())
            {
                foreach (char c in line)
                {
                    if (char.IsNumber(c))
                    {
                        i += (c - '0');
                        continue;
                    }
                    int playerIndex = char.IsUpper(c) ? 0 : 1;
                    char type = char.ToUpper(c);
                    starts[type] |= Game.UL1 << i;
                    playerStarts[playerIndex] |= Game.UL1 << i;
                    State.PieceCount++;
                    i++;
                }
            }
            Players[0] = new Board('w', playerStarts[0]);
            Players[1] = new Board('b', playerStarts[1]);
            foreach (char c in starts.Keys)
            {
                Pieces[c] = new Board(c, starts[c]);
            }
            State.ActiveColor = (sections[1] == "b") ? 1 : 0;

            //{kingside, queenside} castling for players 1 and 0
            foreach (char c in sections[2])
            {
                if (c == 'K') State.CastlingRights[0][0] = true;
                if (c == 'Q') State.CastlingRights[0][1] = true;
                if (c == 'k') State.CastlingRights[1][0] = true;
                if (c == 'q') State.CastlingRights[1][1] = true;
            }

            int temp = Board.ToIndex(sections[3]);
            if (temp != -1) State.EPTarget = temp;
            if (Pieces['K'].PieceCount != 2) throw new Exception();
            GeneratePseudos();
            if (sections.Length < 5) return;
            State.Clock = int.Parse(sections[4]);
            State.FullMoves = int.Parse(sections[5]);
        }
        public void GeneratePseudos(int code = 0)
        {
            ulong[] moves = State.Moves;
            Array.Clear(moves);
            ulong kingSquares = Pieces['K'].Squares;
            ulong friendlies;
            int square;
            bool down;
            foreach (char type in PieceKeys)
            {
                Board b = Pieces[type];
                for (int i = 0; i < b.PieceCount; i++)
                {
                    square = b.OccupiedSquares[i];
                    if (code != 1 && !Players[State.ActiveColor].Contains(square)) continue; //typically 'code' will be the depth at which this function was called; generate moves for both sides at depth 1 for attack tables to be used in quiescence.
                    down = Players[1].Contains(square);
                    friendlies = down ? Players[1].Squares : Players[0].Squares;

                    moves[square] = MoveGen(b.Type, square, down) & ~(kingSquares | friendlies);
                    
                    if (b.Type == 'K')
                    {
                        //kingside castling
                        if (State.CastlingRights[State.ActiveColor][0])
                        {
                            if (!Board.Contains(Blockers, square + 2) && !Board.Contains(Blockers, square + 1))
                            {
                                if (!IsSquareThreatened(square + 1)) moves[square] |= Game.UL1 << square + 2;
                            }
                        }

                        //queenside castling
                        if (State.CastlingRights[State.ActiveColor][1])
                        {
                            if (!Board.Contains(Blockers, square - 3) && !Board.Contains(Blockers, square - 2) && !Board.Contains(Blockers, square - 1))
                            {
                                if (!IsSquareThreatened(square - 1)) moves[square] |= Game.UL1 << square - 2;
                            }
                        }
                    }
                }
            }
        }
        public ulong MoveGen (char type, int square, bool down = true)
        {
            switch (type)
            {
                case 'P':
                    ulong moves;
                    int direction;
                    if (down)
                    {
                        direction = -1;
                        if (Board.Contains(Blockers, square - 8)) moves = 0;
                        else moves = MoveManager.Pawns.Down[square] & ~Blockers;
                    }
                    else
                    {
                        direction = 1;
                        if (Board.Contains(Blockers, square + 8)) moves = 0;
                        else moves = MoveManager.Pawns.Up[square] & ~Blockers;
                    }

                    //attacks, en passant
                    ulong options = Blockers;
                    if (State.EPTarget != -1) options |= (ulong)(Game.UL1 << State.EPTarget);
                    if (square % 8 != 0) moves |= options & (Game.UL1 << (-1 + square + 8 * direction));
                    if (square % 8 != 7) moves |= options & (Game.UL1 << (1 + square + 8 * direction));
                    return moves;

                case 'N':
                    return MoveManager.Knights[square];

                case 'K':
                    return MoveManager.Kings[square];

                default:
                    return MoveManager.SlideGen(type, square, Blockers);
            }
        }

        /// <summary>
        /// Wrapper for alpha-beta search
        /// </summary>
        /// <param name="depth"></param>
        /// <returns></returns>
        public MoveResult? Search (int depth)
        {
            MoveResult result = new()
            {
                Score = int.MinValue + 1
            };
            bool mate = true;
            foreach (char type in PieceKeys)
            {
                Board b = Pieces[type];
                for (int i = 0; i < b.PieceCount; i++)
                {
                    int square = b.OccupiedSquares[i];
                    ulong targetSquares = State.Moves[square];
                    if (targetSquares == 0) continue;
                    for (int j = 0; j < 63; j++)
                    {
                        if (!Board.Contains(targetSquares, j)) continue;
                        if (Make(square, j))
                        {
                            mate = false;
                            int score = -AlphaBeta(int.MinValue + 1, -result.Score, depth - 1);
                            UnMake();
                            if (score > result.Score)
                            {
                                result.PrevIndex = square;
                                result.NextIndex = j;
                                result.Score = score;
                                
                            }
                        }
                    }
                }
            }
            if (mate) return null;
            return result;
        }
        private int AlphaBeta(int alpha, int beta, int depth)
        {
            if (depth == 0)
            {
                return Eval();
            }
            bool mate = true;
            foreach (char type in PieceKeys)
            {
                Board b = Pieces[type];
                for (int i = 0; i < b.PieceCount; i++)
                {
                    int square = b.OccupiedSquares[i];
                    ulong targetSquares = State.Moves[square];
                    if (targetSquares == 0) continue;
                    for (int j = 0; j < 63; j++)
                    {
                        if (!Board.Contains(targetSquares, j)) continue;

                        if (Make(square, j, depth))
                        {
                            mate = false;
                            int tempScore = -AlphaBeta(-beta, -alpha, depth - 1);
                            UnMake();
                            if (tempScore >= beta)
                            {
                                return beta;
                            }
                            if (tempScore > alpha)
                            {
                                alpha = tempScore;
                            }
                        }
                    }
                }
            }
            if (mate)
            {
                return IsPlayerChecked() ? int.MinValue + 2 : 0; //eval to 0 in stalemate
            }
            return alpha;
        }
       
        // Symmetric
        public int Eval ()
        {
            int square;
            int[] material = new int[2];
            int index;
            foreach (char type in PieceKeys)
            {
                Board b = Pieces[type];
                for (int i = 0; i < b.PieceCount; i++)
                {
                    square = b.OccupiedSquares[i];

                    switch (type)
                    {
                        case 'K':
                            if (Players[0].Contains(square)) material[0] += PositionalWeights.King[square];
                            else material[1] += PositionalWeights.King[square];
                            break;
                        case 'P':
                            index = Players[0].Contains(square) ? 0 : 1;
                            material[index] += PositionalWeights.Pawn[index][square] + 15 * BitOperations.PopCount(State.Moves[square]);
                            break;
                        case 'N':
                            index = Players[0].Contains(square) ? 0 : 1;
                            material[index] += PositionalWeights.Knight[square] + 10 * BitOperations.PopCount(State.Moves[square]);
                            break;
                        case 'B':
                            index = Players[0].Contains(square) ? 0 : 1;
                            material[index] += PositionalWeights.Bishop[square] + 10 * BitOperations.PopCount(State.Moves[square]);
                            break;
                        case 'R':
                            index = Players[0].Contains(square) ? 0 : 1;
                            material[index] += PositionalWeights.Rook[square] + 10 * BitOperations.PopCount(State.Moves[square]);
                            break;
                        case 'Q':
                            index = Players[0].Contains(square) ? 0 : 1;
                            material[index] += PositionalWeights.Queen[square] + 10 * BitOperations.PopCount(State.Moves[square]);
                            break;
                    }
                }
            }
            return material[State.ActiveColor] - material[InactiveColor]; ;
        }
        /// <summary>
        /// Make move;
        /// Will call UnMake() and return false if the move was illegal.
        /// </summary>
        /// <param name="prev"></param>
        /// <param name="next"></param>
        /// <returns></returns>
        public bool Make (int prev, int next, int genCode = 0, char promotionType = 'Q')
        {
            if (!Board.Contains(State.Moves[prev], next))
            {
                return false;
            }
            Board player = Players[State.ActiveColor]; 
            Board opponent = Players[InactiveColor];
            char capturedType, specialCaseFlag, promotionFlag;
            char type = capturedType = specialCaseFlag = promotionFlag = '\0';

            foreach (char t in PieceKeys)
            {
                Board b = Pieces[t];
                if (b.Contains(next))
                {
                    //always call Remove() before Update() to account for same-piece captures when updating OccupiedSquares
                    b.Remove(next);
                    opponent.Remove(next);
                    capturedType = b.Type;
                }
                if (b.Contains(prev))
                {
                    b.Update(prev, next);
                    type = b.Type;
                }
            }
            player.Update(prev, next);

            //en passant capture, promotions
            if (type == 'P')
            {
                int opponentDirection = ((State.ActiveColor == 0) ? -1 : 1);

                if (next == State.EPTarget)
                {
                    capturedType = 'P';
                    specialCaseFlag = 'e';
                    int square = next + (8 * opponentDirection);
                    Pieces['P'].Remove(square);
                    opponent.Remove(square);
                }
                //white pawns will never be on the first rank, black never on last. promote to queen on either end row
                if (next > 55 || next < 8)
                {
                    Pieces['P'].Remove(next);
                    Pieces[promotionType].Insert(next);
                    promotionFlag = promotionType;
                    specialCaseFlag = 'p';
                }
            }
            
            //castling
            if (type == 'K')
            {
                //kingside
                if (next - prev == 2)
                {
                    Pieces['R'].Update(next + 1, prev + 1);
                    player.Update(next + 1, prev + 1);
                    specialCaseFlag = 'k';
                }
                //queenside
                if (prev - next == 2)
                {
                    Pieces['R'].Update(next - 2, prev - 1);
                    player.Update(next - 2, prev - 1);
                    specialCaseFlag = 'q';
                }
            }
            
            Log(prev, next, type, capturedType, specialCaseFlag, promotionFlag);

            if (IsPlayerChecked())
            {
                UnMake();
                return false;
            }

            if (type == 'K') Array.Clear(State.CastlingRights[State.ActiveColor]);
            if (type == 'R')
            {
                
                if (prev % 8 == 0) State.CastlingRights[State.ActiveColor][0] = false;
                //if the rook on the a-file has moved, remove the player's ability to castle kingside.
                
                if (prev % 8 == 7) State.CastlingRights[State.ActiveColor][1] = false;
                //queenside
            }
            if (type == 'P')
            {
                State.Clock = -1;

                //set en passant target
                int opponentDirection = State.ActiveColor == 0 ? -1 : 1;
                if ((next - prev == 16) || (prev - next == 16))
                {
                    State.EPTarget = (prev - (8 * opponentDirection));
                }
                else State.EPTarget = -1;
            }
            else State.EPTarget = -1;

            if (capturedType != '\0')
            {
                State.PieceCount--;
                State.Clock = -1;
            }

            State.FullMoves += 1 * State.ActiveColor;
            State.Clock += 1;
            State.ActiveColor = InactiveColor; //InactiveColor is a property which returns (State.ActiveColor + 1) % 2

            GeneratePseudos(genCode);

            return true;
        }

        public void Log (int prev, int next, char movingType, char targetType, char flag, char promotionType)
        {

            MoveLog log = new()
            {
                MovingType = movingType,
                StartingSquare = prev,
                EndingSquare = next,
                CapturedType = targetType,
                SpecialCaseFlag = flag,
                PromotionFlag = promotionType
            };

            log.StartingState.ActiveColor = State.ActiveColor;
            log.StartingState.EPTarget = State.EPTarget;
            log.StartingState.CastlingRights = State.CastlingRights;
            log.StartingState.PieceCount = State.PieceCount;

            Array.Copy(State.Moves, log.StartingState.Moves, 64);
            Array.Copy(State.CastlingRights[0], log.StartingState.CastlingRights[0], 2);
            Array.Copy(State.CastlingRights[1], log.StartingState.CastlingRights[1], 2);
            History.Push(log);
        }

        /// <summary>
        /// Undo the most recent move made by Make()
        /// </summary>
        public void UnMake ()
        {
            
            MoveLog log = History.Pop();
            
            Board player = Players[log.StartingState.ActiveColor];
            Board opponent = Players[(log.StartingState.ActiveColor + 1) % 2];
            Board board = Pieces[log.MovingType];
            switch (log.SpecialCaseFlag)
            {
                //kingside castle
                case 'k':
                    board.Update(log.EndingSquare, log.StartingSquare);
                    player.Update(log.EndingSquare, log.StartingSquare);
                    Pieces['R'].Update(log.StartingSquare + 1, log.EndingSquare + 1);
                    player.Update(log.StartingSquare + 1, log.EndingSquare + 1);
                    break;
                //queenside castle
                case 'q':
                    board.Update(log.EndingSquare, log.StartingSquare);
                    player.Update(log.EndingSquare, log.StartingSquare);
                    Pieces['R'].Update(log.StartingSquare - 1, log.EndingSquare - 2);
                    player.Update(log.StartingSquare - 1, log.EndingSquare - 2);
                    break;
                //en passant
                case 'e':
                    board.Update(log.EndingSquare, log.StartingSquare);
                    player.Update(log.EndingSquare, log.StartingSquare);
                    int opponentDirection = log.StartingState.ActiveColor == 0 ? -1 : 1;
                    opponent.Insert(log.EndingSquare + (8 * opponentDirection));
                    Pieces['P'].Insert(log.EndingSquare + (8 * opponentDirection));
                    break;
                //promotion
                case 'p':
                    player.Remove(log.EndingSquare);
                    Pieces[log.PromotionFlag].Remove(log.EndingSquare);

                    player.Insert(log.StartingSquare);
                    Pieces['P'].Insert(log.StartingSquare);
                    break;
                default:
                    board.Update(log.EndingSquare, log.StartingSquare);
                    player.Update(log.EndingSquare, log.StartingSquare);

                    if (log.CapturedType != '\0')
                    {
                        Pieces[log.CapturedType].Insert(log.EndingSquare);
                        opponent.Insert(log.EndingSquare);
                    }
                    break;
            }
            State = log.StartingState;
        }
        public bool IsPlayerChecked ()
        {
            int playerIndex = State.ActiveColor;
            ulong friendlies = Players[(playerIndex)].Squares;
            ulong enemies, squares;
            int square = ActiveKingSquare;

            foreach (char type in Pieces.Keys)
            {
                enemies = Pieces[type].Squares & ~friendlies;
                squares = 0;
                if (type == 'P')
                {
                    int direction = (playerIndex == 0) ? 1 : -1;

                    if (square % 8 != 0) squares |= (Game.UL1 << (-1 + square + 8 * direction));
                    if (square % 8 != 7) squares |= (Game.UL1 << (1 + square + 8 * direction));
                }
                else squares = MoveGen(type, square);
                if (0 != (squares & enemies)) return true;
            }
            return false;
        }
        public bool IsSquareThreatened (int square, int targetColor = -1)
        {
            if (targetColor == -1) targetColor = State.ActiveColor;
            ulong friendlies = Players[targetColor].Squares;
            ulong enemies, squares;
            //generate moves of each piece type from the square and compare with enemies of that type
            foreach (char type in PieceKeys)
            {
                enemies = Pieces[type].Squares & ~friendlies;
                squares = 0;
                //pawn attacks from MoveGen will not be in the right direction
                if (type == 'P')
                {
                    int direction = (targetColor == 0) ? 1 : -1;

                    if (square % 8 != 0) squares |= (Game.UL1 << (-1 + square + 8 * direction));
                    if (square % 8 != 7) squares |= (Game.UL1 << (1 + square + 8 * direction));
                }
                else squares = MoveGen(type, square) & ~friendlies;
                if (0 != (squares & enemies)) return true;
            }
            return false;
        }

        public bool SearchMake (int depth)
        {
            MoveResult? move = Search(depth);
            if (move == null) return false;
            Make((MoveResult)move);
            Console.WriteLine("The computer plays " + move);
            return true;
        }

        // MISCELLANEOUS FUNCTIONS FOR DEBUGGING AND EASY I/O //
        public void Print (bool flipped = false)
        {
            string line;
            string output = "";
            int bitIndex = 0;
            if (!flipped)
            {
                for (int i = 1; i <= 8; i++)
                {
                    line = "";
                    for (int j = 0; j < 8; j++)
                    {
                        bool bit = Board.Contains(Blockers, bitIndex);
                        if (bit)
                        {
                            foreach (char type in PieceKeys)
                            {
                                string piece = char.ToString(type);
                                if (0 != (Players[1].Squares & (Game.UL1 << bitIndex))) piece = piece.ToLower();
                                if (0 != (Pieces[type].Squares & (Game.UL1 << bitIndex))) line += piece + " ";
                            }
                        }
                        else line += "-" + " ";
                        bitIndex++;
                    }
                    line += i;
                    output = line + "\n" + output;
                }
                output += "a b c d e f g h";
                Console.WriteLine(output);
                return;
            }

            bitIndex = 63;
            for (int i = 8; i > 0; i--)
            {
                line = "";
                for (int j = 0; j < 8; j++)
                {
                    bool bit = Board.Contains(Blockers, bitIndex);
                    if (bit)
                    {
                        foreach (char type in PieceKeys)
                        {
                            string piece = char.ToString(type);
                            if (0 != (Players[1].Squares & (Game.UL1 << bitIndex))) piece = piece.ToLower();
                            if (0 != (Pieces[type].Squares & (Game.UL1 << bitIndex))) line += piece + " ";
                        }
                    }
                    else line += "-" + " ";
                    bitIndex--;
                }
                line += i;
                output = line + "\n" + output;
            }
            output += "h g f e d c b a";
            Console.WriteLine(output);
        }
        public void Perft (int depth)
        {
            Stopwatch watch = new();
            watch.Start();
            long nodes = PerftRecursive(depth);
            watch.Stop();
            Console.WriteLine(nodes.ToString() + " nodes");

            Console.WriteLine(watch.ElapsedMilliseconds.ToString() + " ms");

            if ((watch.ElapsedMilliseconds) != 0) Console.WriteLine((nodes / (long)watch.ElapsedMilliseconds).ToString() + " Knps");

            Console.WriteLine((long)watch.ElapsedTicks / nodes + " ticks/node");
        }
        /// <summary>
        /// Verbose Perft
        /// </summary>
        /// <param name="depth"></param>
        public void VPerft (int depth)
        {
            Stopwatch watch = new();
            watch.Start();
            long nodes = 0;

            for (int i = 0; i < 64; i++)
            {
                if (State.Moves[i] == 0) continue;
                for (int j = 0; j < 64; j++)
                {
                    if (0 == (State.Moves[i] & (Game.UL1 << j))) continue;

                    if (Make(i, j))
                    {
                        Console.Write(Board.ToAlgNot(i) + Board.ToAlgNot(j) + " ");
                        long count = PerftRecursive(depth - 1);
                        nodes += count;
                        Console.WriteLine(count);
                        UnMake();
                    }
                }
            }
            watch.Stop();
            Console.WriteLine();
            Console.WriteLine(nodes.ToString() + " nodes");

            Console.WriteLine(watch.ElapsedMilliseconds.ToString() + " ms");

            if ((watch.ElapsedMilliseconds) != 0) Console.WriteLine((nodes / (long)watch.ElapsedMilliseconds).ToString() + " Knps");

            Console.WriteLine((long)watch.ElapsedTicks / nodes + " ticks/node");
        }
        private long PerftRecursive (int depth)
        {
            long nodes = 0;
            if (depth == 0) return (long)1;
            for (int i = 0; i < 64; i++)
            {
                //if (0 == (Players[State.ActiveColor].Squares & (Game.UL1 << i))) continue;
                if (State.Moves[i] == 0) continue;
                for (int j = 0; j < 64; j++)
                {
                    if (!Board.Contains(State.Moves[i], j)) continue;

                    if (Make(i, j))
                    {
                        nodes += PerftRecursive(depth - 1);
                        UnMake();
                    }
                }
            }
            return nodes;
        }
        public string GenerateFEN ()
        {
            string line;
            string output = "";
            int emptyCount;
            int bitIndex = 0;
            for (int i = 1; i <= 8; i++)
            {
                line = "";
                emptyCount = 0;
                for (int j = 0; j < 8; j++)
                {
                    bool bit = Board.Contains(Blockers, bitIndex);
                    if (bit)
                    {
                        if (emptyCount != 0)
                        { 
                            line += emptyCount;
                            emptyCount = 0;
                        }
                        foreach (char type in PieceKeys)
                        {
                            string piece = type.ToString();
                            
                            if (Players[1].Contains(bitIndex)) piece = piece.ToLower();
                            if (Pieces[type].Contains(bitIndex)) line += piece;
                        }
                    }
                    else emptyCount++;
                    bitIndex++;
                }
                if (emptyCount != 0) line += emptyCount;
                output = line + "/" + output;
            }

            output = string.Concat(output.AsSpan(0, output.Length - 1), " ", State.ActiveColor == 0 ? "w" : "b", " "); ///*equivalent to:*/ output = output.Substring(0, output.Length - 1) + " " + (State.ActiveColor == 0 ? "w" : "b") + " ";
            bool[] w = State.CastlingRights[State.ActiveColor];
            bool[] b = State.CastlingRights[InactiveColor];
            
            if (w[0]) output += "K";
            if (w[1]) output += "Q";
            if (b[0]) output += "k";
            if (b[1]) output += "q";

            if (!w[0] && !w[1] && !b[0] && !b[1]) output += "-";
            
            if (State.EPTarget != -1) output += " " + Board.ToAlgNot((int)State.EPTarget) + " ";
            else output += " - ";
            output += State.Clock + " " + State.FullMoves;
            return output;
        }
        public bool Make (string prev, string next)
        {
            Console.WriteLine("hi");
            return Make(Board.ToIndex(prev), Board.ToIndex(next));
        }
        public bool Make (MoveResult move)
        {
            return Make(move.PrevIndex, move.NextIndex);
        }
        public void DumpInfo()
        {
            foreach (char type in PieceKeys)
            {
                Console.WriteLine(type);
                Board.PrintSquares(Pieces[type]);
            }
            Console.WriteLine("P1");
            Board.PrintSquares(Players[0]);
            Console.WriteLine("P2");
            Board.PrintSquares(Players[1]);
            Print();
            Console.WriteLine(GenerateFEN());
        }
    }
}