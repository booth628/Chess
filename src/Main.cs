using System.ComponentModel.Design;
using System.Diagnostics;
using System.Numerics;

namespace Chess
{
    class Game
    {
        /// <summary>
        /// ulong 1
        /// </summary>
        public static readonly ulong UL1 = 1;
        private static readonly int Depth = 4;
        public static void Main()
        {
            string input;
            bool flipped = new Random().Next() % 2 == 0;
            Game game = new();
            while (true)
            {
                Console.Write("Enter a starting position (FEN) or nothing to use the default starting position: ");
                input = Console.ReadLine();
                /*if (input == "*")
                {
                    Debug();
                    continue;
                }*/
                if (input == "") break;
                try
                {
                    game = new(input);
                }
                catch
                {
                    Console.WriteLine("An error occurred while initializing the game. Please make sure that your FEN code is valid.");
                    continue;
                }
                break;
            }
            Console.Write("Enter 'b' or 'B' to play as black, 'w' or 'W' to play as white, or anything else for a random color: ");
            input = Console.ReadLine().ToLower();
            if (input == "" || (input[0] != 'b' && input[0] != 'w'))
            {
                input = flipped ? "b" : "w";
                Console.WriteLine("You have the " + (flipped ? "black" : "white") + " pieces.");
            }
            switch (input)
            {
                case "b":
                    flipped = true;
                    game.ActivePlayer = game.Engine.State.ActiveColor == 0 ? 1 : 0;
                    break;
                case "w":
                    flipped = false;
                    game.ActivePlayer = game.Engine.State.ActiveColor == 1 ? 1 : 0;
                    break;
            }
            Console.WriteLine("\nType *info for help.");
            while (true)
            {
                Console.WriteLine();

                if (game.ActivePlayer == 1)
                {
                    if (!game.Engine.SearchMake(Depth))
                    {
                        Console.WriteLine("Game over!");
                        break;
                    }
                }
                else
                {
                    game.Engine.Print(flipped);
                    (int prev, int next) move;
                    if (game.Engine.Search(1) == null)
                    {
                        Console.WriteLine("Game over!");
                        break;
                    }
                    while (true)
                    {
                        Console.Write("\nYour move: ");
                        input = Console.ReadLine().Replace("x", null);
                        char promotionType = '\0';
                        if (input == "*info")
                        {
                            Console.WriteLine("Enter moves in algebraic chess notation (e.g. Ra4). Lowercase piece types are not allowed to prevent\n\t moves from being misinterpreted (for example, bxc4 is a pawn capture, Bxc4 is a bishop capture).");
                            Console.WriteLine("When capturing a piece, use of 'x' is allowed but not required (e.g. Bxd4).");
                            Console.WriteLine("You may enter a pawn move using only the ending square (e.g. d4).");
                            Console.WriteLine("To distinguish between 2 pieces of the same type which can move to the same square, use either the rank (1-8)\n\tor file (a-h). For example, with a rook on a1 and one on h1, say either Rad1 or Rhd1 to move one to d1.");
                            Console.WriteLine("To promote a pawn to something other than a queen, append =[type] to the end of your input (e.g. f8=N).");
                            Console.WriteLine("Commands:");
                            Console.WriteLine("*dump: print the game info");
                            Console.WriteLine("*undo: undo your last move");
                            Console.WriteLine("*code: generate a FEN code representing the position");
                            continue;
                        }
                        if (input == "*undo")
                        {
                            try
                            {
                                game.Engine.UnMake();
                                game.Engine.UnMake();
                            }
                            catch
                            {
                                Console.WriteLine("Undo failed.");
                                continue;
                            }
                            Console.WriteLine();
                            game.Engine.Print(flipped);
                            continue;
                        }
                        if (input == "*dump")
                        {
                            game.Engine.DumpInfo();
                            continue;
                        }
                        if (input == "*code")
                        {
                            Console.WriteLine("\n" + game.Engine.GenerateFEN());
                            continue;
                        }
                        if (input[^2] == '=')
                        {
                            promotionType = input[^1];
                            input = input[..^2];
                            if (promotionType == 'K' || promotionType == 'P' || !game.Engine.Pieces.ContainsKey(promotionType))
                            {
                                Console.WriteLine("Invalid promotion, please try again.");
                                continue;
                            }
                        }
                        move = game.ProcessInput(input);
                        if (move.prev == -1 || move.next == -1)
                        {
                            Console.WriteLine("Invalid move, please try again.");
                        }
                        else if (!game.Engine.Make(move.prev, move.next, 0, promotionType))
                        {
                            Console.WriteLine("Invalid move, please try again.");
                        }
                        else break;
                    }
                }
                game.ActivePlayer = game.ActivePlayer == 0 ? 1 : 0;
            }
        }
        /*
        public static void Debug()
        {
            Engine e = new();
            e.Perft(4);
        }*/

        readonly Engine Engine;

        //player = 0, bot = 1
        public int ActivePlayer = 0;

        public Game(string FEN)
        {
            Engine = new(FEN);
        }
        public Game()
        {
            Engine = new();
        }
        public (int startingSquare, int nextSquare) ProcessInput(string input)
        {
            string square;
            int prev, next;
            char type;
            ulong pieces;
            char? distinguisher = null;
            if (input.ToLower() == "o-o") return (Engine.ActiveKingSquare, Engine.ActiveKingSquare + 2);
            if (input.ToLower() == "o-o-o") return (Engine.ActiveKingSquare, Engine.ActiveKingSquare - 2);
            if (input.Length < 2 || input.Length > 4) return (-1, -1);
            if (input.Length == 2)
            {
                type = 'P';
                square = input;
            }
            else if (input.Length == 3)
            {
                type = input[0];
                if (!Engine.PieceKeys.Contains(input[0]))
                {
                    type = 'P';
                    distinguisher = input[0];
                }
                square = input[1..];
            }
            else
            {
                distinguisher = input[1];
                type = char.ToUpper(input[0]);
                square = input[2..];
            }
            if (distinguisher != null)
            {
                prev = 0;
                next = Board.ToIndex(square);
                pieces = Engine.Pieces[type];
                if (distinguisher >= '1' && distinguisher <= '8')
                {
                    int row = (int)distinguisher - '0';
                    while (!Board.Contains(Engine.State.Moves[prev], next) || (prev / 8) + 1 != row)
                    {
                        prev = BitOperations.TrailingZeroCount(pieces);
                        pieces &= ~(UL1 << prev);
                        if (prev == 64) return (-1, -1);
                    }
                    return (prev, next);
                }
                else if (distinguisher >= 'a' && distinguisher <= 'h')
                {
                    int column = (int)distinguisher - 97;
                    while (!Board.Contains(Engine.State.Moves[prev], next) || prev % 8 != column)
                    {
                        prev = BitOperations.TrailingZeroCount(pieces);
                        pieces &= ~(UL1 << prev);
                        if (prev == 64) return (-1, -1);
                    }
                    return (prev, next);
                }
                else return (-1, -1);
            }

            prev = 0;
            next = Board.ToIndex(square);
            pieces = Engine.Pieces[type];
            while (!Board.Contains(Engine.State.Moves[prev], next))
            {
                prev = BitOperations.TrailingZeroCount(pieces);
                pieces &= ~(UL1 << prev);
                if (prev == 64) return (-1, -1);
            }
            return (prev, next);
        }
    }
}