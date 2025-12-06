namespace Decima.Commands;

using System.ComponentModel;
using System.Diagnostics;

using Decima.Data;

using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

public sealed class PlayCommand : Command<PlayCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-d|--difficulty <LEVEL>")]
        [Description("Difficulty level (easy, medium, hard, expert, extreme)")]
        [DefaultValue(Difficulty.Medium)]
        public Difficulty Difficulty { get; init; }

        [CommandOption("-p|--puzzle <PUZZLE>")]
        [Description("Custom puzzle string (81 chars)")]
        public string? Puzzle { get; init; }

        [CommandOption("-m|--model <PATH>")]
        [Description("Path to trained model for hints")]
        [DefaultValue("sudoku_model.bin")]
        public string ModelPath { get; init; } = "sudoku_model.bin";

        [CommandOption("--no-timer")]
        [Description("Disable the timer")]
        [DefaultValue(false)]
        public bool NoTimer { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        // Get or generate puzzle
        SudokuGrid puzzle;
        SudokuGrid solution;

        if (!string.IsNullOrWhiteSpace(settings.Puzzle))
        {
            if (!SudokuGrid.TryParse(settings.Puzzle, out puzzle))
            {
                AnsiConsole.MarkupLine("[red]Invalid puzzle format![/]");
                return 1;
            }

            var solved = SudokuGenerator.Solve(puzzle);
            if (solved == null)
            {
                AnsiConsole.MarkupLine("[red]Puzzle has no solution![/]");
                return 1;
            }

            solution = solved.Value;
        }
        else
        {
            AnsiConsole.MarkupLine($"[dim]Generating {settings.Difficulty} puzzle...[/]");
            (puzzle, solution) = SudokuGenerator.GenerateWithDifficulty(settings.Difficulty);
        }

        // Initialize game state
        var game = new GameState(puzzle, solution, settings.ModelPath, !settings.NoTimer);

        // Run interactive game loop
        return game.Run();
    }

    private sealed class GameState
    {
        private SudokuGrid _current;
        private readonly SudokuGrid _original;
        private readonly SudokuGrid _solution;
        private readonly string _modelPath;
        private readonly bool _showTimer;
        private readonly Stopwatch _stopwatch;
        private readonly Stack<SudokuGrid> _undoStack;
        private readonly Stack<SudokuGrid> _redoStack;

        private int _cursorRow;
        private int _cursorCol;
        private int _hintsUsed;
        private bool _showHelp;
        private string? _message;
        private DateTime _messageTime;

        public GameState(SudokuGrid puzzle, SudokuGrid solution, string modelPath, bool showTimer)
        {
            _original = puzzle;
            _current = puzzle.Copy();
            _solution = solution;
            _modelPath = modelPath;
            _showTimer = showTimer;
            _stopwatch = new Stopwatch();
            _undoStack = new Stack<SudokuGrid>();
            _redoStack = new Stack<SudokuGrid>();
            _cursorRow = 0;
            _cursorCol = 0;
        }

        public int Run()
        {
            Console.CursorVisible = false;
            _stopwatch.Start();

            try
            {
                while (true)
                {
                    Render();

                    // Check for win
                    if (_current.IsComplete && _current.IsValid())
                    {
                        _stopwatch.Stop();
                        RenderWin();
                        return 0;
                    }

                    // Handle input
                    var key = Console.ReadKey(true);

                    if (!HandleInput(key))
                    {
                        return 0; // User quit
                    }
                }
            }
            finally
            {
                Console.CursorVisible = true;
            }
        }

        private void Render()
        {
            Console.Clear();

            // Title
            AnsiConsole.MarkupLine("[bold cyan]Sudoku[/]");
            AnsiConsole.WriteLine();

            // Timer and stats
            if (_showTimer)
            {
                var elapsed = _stopwatch.Elapsed;
                AnsiConsole.MarkupLine($"[dim]Time:[/] [white]{elapsed:mm\\:ss}[/]  [dim]Hints:[/] [white]{_hintsUsed}[/]  [dim]Empty:[/] [white]{_current.EmptyCellCount}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[dim]Hints:[/] [white]{_hintsUsed}[/]  [dim]Empty:[/] [white]{_current.EmptyCellCount}[/]");
            }

            AnsiConsole.WriteLine();

            // Grid
            var conflicts = SudokuValidator.GetAllConflicts(_current);
            AnsiConsole.Write(RenderInteractiveGrid(_current, _original, (_cursorRow, _cursorCol), conflicts));

            AnsiConsole.WriteLine();

            // Message
            if (!string.IsNullOrEmpty(_message) && (DateTime.Now - _messageTime).TotalSeconds < 3)
            {
                AnsiConsole.MarkupLine(_message);
            }
            else
            {
                AnsiConsole.WriteLine();
            }

            // Help
            if (_showHelp)
            {
                RenderHelp();
            }
            else
            {
                AnsiConsole.MarkupLine("[dim]Press [white]?[/] for help, [white]Q[/] to quit[/]");
            }
        }

        private static IRenderable RenderInteractiveGrid(
            SudokuGrid grid,
            SudokuGrid original,
            (int Row, int Col) cursor,
            HashSet<(int Row, int Col)> conflicts)
        {
            var rows = new List<IRenderable>();

            rows.Add(new Markup("[grey]╔═══════╤═══════╤═══════╗[/]"));

            for (var row = 0; row < SudokuGrid.Size; row++)
            {
                var rowText = new System.Text.StringBuilder();
                rowText.Append("[grey]║[/] ");

                for (var col = 0; col < SudokuGrid.Size; col++)
                {
                    var value = grid[row, col];
                    var text = value == 0 ? "·" : value.ToString();

                    var color = GetCellColor(row, col, value, original, cursor, conflicts);
                    rowText.Append($"[{color}]{text}[/] ");

                    if ((col + 1) % SudokuGrid.BoxSize == 0 && col < SudokuGrid.Size - 1)
                    {
                        rowText.Append("[grey]│[/] ");
                    }
                }

                rowText.Append("[grey]║[/]");
                rows.Add(new Markup(rowText.ToString()));

                if ((row + 1) % SudokuGrid.BoxSize == 0 && row < SudokuGrid.Size - 1)
                {
                    rows.Add(new Markup("[grey]╟───────┼───────┼───────╢[/]"));
                }
            }

            rows.Add(new Markup("[grey]╚═══════╧═══════╧═══════╝[/]"));

            return new Rows(rows);
        }

        private static string GetCellColor(
            int row, int col, int value,
            SudokuGrid original,
            (int Row, int Col) cursor,
            HashSet<(int Row, int Col)> conflicts)
        {
            var isCursor = cursor.Row == row && cursor.Col == col;
            var isConflict = conflicts.Contains((row, col));
            var isOriginal = original[row, col] != 0;

            if (isCursor)
            {
                if (isConflict)
                {
                    return "red bold on grey23";
                }

                if (isOriginal)
                {
                    return "white bold on grey23";
                }

                return value == 0 ? "yellow on grey23" : "green bold on grey23";
            }

            if (isConflict)
            {
                return "red bold";
            }

            if (isOriginal)
            {
                return "white bold";
            }

            return value == 0 ? "grey42" : "green";
        }

        private bool HandleInput(ConsoleKeyInfo key)
        {
            switch (key.Key)
            {
                // Navigation
                case ConsoleKey.UpArrow:
                case ConsoleKey.W:
                case ConsoleKey.K:
                    _cursorRow = (_cursorRow - 1 + 9) % 9;
                    break;

                case ConsoleKey.DownArrow:
                case ConsoleKey.S:
                case ConsoleKey.J:
                    _cursorRow = (_cursorRow + 1) % 9;
                    break;

                case ConsoleKey.LeftArrow:
                case ConsoleKey.A:
                    _cursorCol = (_cursorCol - 1 + 9) % 9;
                    break;

                case ConsoleKey.RightArrow:
                case ConsoleKey.D:
                case ConsoleKey.L:
                    _cursorCol = (_cursorCol + 1) % 9;
                    break;

                // Number input
                case ConsoleKey.D1:
                case ConsoleKey.D2:
                case ConsoleKey.D3:
                case ConsoleKey.D4:
                case ConsoleKey.D5:
                case ConsoleKey.D6:
                case ConsoleKey.D7:
                case ConsoleKey.D8:
                case ConsoleKey.D9:
                case ConsoleKey.NumPad1:
                case ConsoleKey.NumPad2:
                case ConsoleKey.NumPad3:
                case ConsoleKey.NumPad4:
                case ConsoleKey.NumPad5:
                case ConsoleKey.NumPad6:
                case ConsoleKey.NumPad7:
                case ConsoleKey.NumPad8:
                case ConsoleKey.NumPad9:
                    var digit = key.Key >= ConsoleKey.NumPad1
                        ? key.Key - ConsoleKey.NumPad1 + 1
                        : key.Key - ConsoleKey.D1 + 1;
                    PlaceNumber(digit);
                    break;

                // Clear cell
                case ConsoleKey.D0:
                case ConsoleKey.NumPad0:
                case ConsoleKey.Delete:
                case ConsoleKey.Backspace:
                case ConsoleKey.Spacebar:
                    ClearCell();
                    break;

                // Undo/Redo
                case ConsoleKey.U:
                    Undo();
                    break;

                case ConsoleKey.R:
                    if (key.Modifiers.HasFlag(ConsoleModifiers.Control))
                    {
                        Redo();
                    }
                    else
                    {
                        Reset();
                    }
                    break;

                // Hint
                case ConsoleKey.H:
                    GiveHint();
                    break;

                // Help
                case ConsoleKey.Oem2: // ? key
                case ConsoleKey.F1:
                    _showHelp = !_showHelp;
                    break;

                // Quit
                case ConsoleKey.Q:
                case ConsoleKey.Escape:
                    return ConfirmQuit();
            }

            return true;
        }

        private void PlaceNumber(int digit)
        {
            // Can't modify original cells
            if (_original[_cursorRow, _cursorCol] != 0)
            {
                ShowMessage("[yellow]Cannot modify given cells![/]");
                return;
            }

            // Save for undo
            _undoStack.Push(_current.Copy());
            _redoStack.Clear();

            _current = _current.WithCell(_cursorRow, _cursorCol, digit);
        }

        private void ClearCell()
        {
            if (_original[_cursorRow, _cursorCol] != 0)
            {
                ShowMessage("[yellow]Cannot modify given cells![/]");
                return;
            }

            if (_current[_cursorRow, _cursorCol] == 0)
            {
                return;
            }

            _undoStack.Push(_current.Copy());
            _redoStack.Clear();

            _current = _current.WithCell(_cursorRow, _cursorCol, 0);
        }

        private void Undo()
        {
            if (_undoStack.Count == 0)
            {
                ShowMessage("[yellow]Nothing to undo![/]");
                return;
            }

            _redoStack.Push(_current.Copy());
            _current = _undoStack.Pop();
            ShowMessage("[dim]Undo[/]");
        }

        private void Redo()
        {
            if (_redoStack.Count == 0)
            {
                ShowMessage("[yellow]Nothing to redo![/]");
                return;
            }

            _undoStack.Push(_current.Copy());
            _current = _redoStack.Pop();
            ShowMessage("[dim]Redo[/]");
        }

        private void Reset()
        {
            _undoStack.Push(_current.Copy());
            _redoStack.Clear();
            _current = _original.Copy();
            ShowMessage("[yellow]Puzzle reset![/]");
        }

        private void GiveHint()
        {
            // If current cell is empty or incorrect, give hint for it
            if (_original[_cursorRow, _cursorCol] == 0)
            {
                var currentValue = _current[_cursorRow, _cursorCol];
                var correctValue = _solution[_cursorRow, _cursorCol];

                if (currentValue != correctValue)
                {
                    _undoStack.Push(_current.Copy());
                    _redoStack.Clear();

                    _current = _current.WithCell(_cursorRow, _cursorCol, correctValue);
                    _hintsUsed++;

                    if (currentValue == 0)
                    {
                        ShowMessage($"[cyan]Hint: {correctValue}[/]");
                    }
                    else
                    {
                        ShowMessage($"[cyan]Hint: {currentValue} → {correctValue}[/]");
                    }
                    return;
                }
            }

            // Current cell is correct or given - find first empty/incorrect cell
            for (var row = 0; row < 9; row++)
            {
                for (var col = 0; col < 9; col++)
                {
                    if (_original[row, col] == 0 && _current[row, col] != _solution[row, col])
                    {
                        _undoStack.Push(_current.Copy());
                        _redoStack.Clear();

                        var correctValue = _solution[row, col];
                        _current = _current.WithCell(row, col, correctValue);
                        _cursorRow = row;
                        _cursorCol = col;
                        _hintsUsed++;

                        ShowMessage($"[cyan]Hint: Cell ({row + 1},{col + 1}) = {correctValue}[/]");
                        return;
                    }
                }
            }

            ShowMessage("[green]Puzzle already complete![/]");
        }

        private bool ConfirmQuit()
        {
            Console.Clear();
            return !AnsiConsole.Confirm("[yellow]Are you sure you want to quit?[/]", defaultValue: false);
        }

        private void ShowMessage(string message)
        {
            _message = message;
            _messageTime = DateTime.Now;
        }

        private void RenderWin()
        {
            Console.Clear();

            AnsiConsole.Write(new FigletText("You Win!").Centered().Color(Color.Green));
            AnsiConsole.WriteLine();

            AnsiConsole.Write(RenderInteractiveGrid(_current, _original, (-1, -1), []));

            AnsiConsole.WriteLine();

            var elapsed = _stopwatch.Elapsed;
            AnsiConsole.MarkupLine($"[bold green]Congratulations![/]");
            AnsiConsole.MarkupLine($"[dim]Time:[/] [white]{elapsed:mm\\:ss}[/]");
            AnsiConsole.MarkupLine($"[dim]Hints used:[/] [white]{_hintsUsed}[/]");

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Press any key to exit...[/]");
            Console.ReadKey(true);
        }

        private static void RenderHelp()
        {
            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .Title("[cyan]Controls[/]");

            table.AddColumn("[white]Key[/]");
            table.AddColumn("[white]Action[/]");

            table.AddRow("[yellow]↑↓←→[/] / [yellow]WASD[/] / [yellow]JKL[/]", "Move cursor");
            table.AddRow("[yellow]1-9[/]", "Place number");
            table.AddRow("[yellow]0[/] / [yellow]Del[/] / [yellow]Space[/]", "Clear cell");
            table.AddRow("[yellow]U[/]", "Undo");
            table.AddRow("[yellow]Ctrl+R[/]", "Redo");
            table.AddRow("[yellow]R[/]", "Reset puzzle");
            table.AddRow("[yellow]H[/]", "Get hint (current cell)");
            table.AddRow("[yellow]?[/] / [yellow]F1[/]", "Toggle help");
            table.AddRow("[yellow]Q[/] / [yellow]Esc[/]", "Quit");

            AnsiConsole.Write(table);
        }
    }
}
