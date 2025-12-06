namespace Decima.Commands;

using System.ComponentModel;

using Decima.Data;
using Decima.Models;
using Decima.Solvers;
using Decima.UI;

using Spectre.Console;
using Spectre.Console.Cli;

public sealed class SolveCommand : Command<SolveCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[PUZZLE]")]
        [Description("Sudoku puzzle string (81 chars, use . or 0 for empty cells)")]
        public string? Puzzle { get; init; }

        [CommandOption("-m|--model <PATH>")]
        [Description("Path to trained model")]
        [DefaultValue("sudoku_model.bin")]
        public string ModelPath { get; init; } = "sudoku_model.bin";

        [CommandOption("-a|--animate")]
        [Description("Animate the solving process")]
        [DefaultValue(false)]
        public bool Animate { get; init; }

        [CommandOption("--delay <MS>")]
        [Description("Delay between animation steps in milliseconds")]
        [DefaultValue(50)]
        public int AnimationDelay { get; init; }

        [CommandOption("--compare")]
        [Description("Compare ML solution with backtracking solver")]
        [DefaultValue(false)]
        public bool Compare { get; init; }

        [CommandOption("-g|--generate")]
        [Description("Generate a random puzzle instead of providing one")]
        [DefaultValue(false)]
        public bool Generate { get; init; }

        [CommandOption("-d|--difficulty <LEVEL>")]
        [Description("Difficulty level for generated puzzles (easy, medium, hard, expert, extreme)")]
        [DefaultValue(Difficulty.Medium)]
        public Difficulty Difficulty { get; init; }

        // Genetic Algorithm options
        [CommandOption("--ga")]
        [Description("Use genetic algorithm solver instead of ML")]
        [DefaultValue(false)]
        public bool UseGeneticAlgorithm { get; init; }

        [CommandOption("--hybrid")]
        [Description("Use hybrid ML + GA solver (ML seeds GA population)")]
        [DefaultValue(false)]
        public bool UseHybrid { get; init; }

        [CommandOption("--islands")]
        [Description("Use island model for GA (parallel sub-populations)")]
        [DefaultValue(false)]
        public bool UseIslands { get; init; }

        [CommandOption("--population <SIZE>")]
        [Description("GA population size")]
        [DefaultValue(1000)]
        public int PopulationSize { get; init; }

        [CommandOption("--generations <COUNT>")]
        [Description("Maximum GA generations")]
        [DefaultValue(1000)]
        public int MaxGenerations { get; init; }

        [CommandOption("--mutation <RATE>")]
        [Description("GA mutation rate (0.0 to 1.0)")]
        [DefaultValue(0.1)]
        public double MutationRate { get; init; }

        [CommandOption("--island-count <COUNT>")]
        [Description("Number of islands for island model")]
        [DefaultValue(4)]
        public int IslandCount { get; init; }

        // Beam search options
        [CommandOption("--beam-width <WIDTH>")]
        [Description("Beam width for inference (higher = more accurate but slower, 1 = greedy/no beam search)")]
        [DefaultValue(2)]
        public int BeamWidth { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        // Get or generate puzzle
        SudokuGrid puzzle;
        SudokuGrid? knownSolution = null;

        if (settings.Generate)
        {
            AnsiConsole.MarkupLine($"[dim]Generating {settings.Difficulty} puzzle...[/]");
            var (p, s) = SudokuGenerator.GenerateWithDifficulty(settings.Difficulty);
            puzzle = p;
            knownSolution = s;
        }
        else if (!string.IsNullOrWhiteSpace(settings.Puzzle))
        {
            if (!SudokuGrid.TryParse(settings.Puzzle, out puzzle))
            {
                AnsiConsole.MarkupLine("[red]Invalid puzzle format![/]");
                AnsiConsole.MarkupLine("[dim]Provide 81 characters where 1-9 are values and 0/./space are empty cells.[/]");
                return 1;
            }
        }
        else
        {
            // Prompt for puzzle
            var puzzleString = AnsiConsole.Prompt(
                new TextPrompt<string>("[cyan]Enter puzzle[/] (81 chars, . for empty):")
                    .Validate(p =>
                    {
                        var cleaned = p.Where(c => char.IsDigit(c) || c == '.').Count();
                        return cleaned >= 81
                            ? ValidationResult.Success()
                            : ValidationResult.Error("Puzzle must have at least 81 cell values");
                    }));

            if (!SudokuGrid.TryParse(puzzleString, out puzzle))
            {
                AnsiConsole.MarkupLine("[red]Invalid puzzle format![/]");
                return 1;
            }
        }

        // Validate input puzzle
        if (!puzzle.IsValid())
        {
            AnsiConsole.MarkupLine("[red]Invalid puzzle - contains conflicting values![/]");
            var conflicts = SudokuValidator.GetAllConflicts(puzzle);
            AnsiConsole.Write(SudokuGridRenderer.RenderWithBoxes(puzzle, conflicts: conflicts, title: "Invalid Puzzle"));
            return 1;
        }

        // Display input puzzle
        AnsiConsole.WriteLine();
        AnsiConsole.Write(SudokuGridRenderer.RenderWithBoxes(puzzle, title: "Input Puzzle"));
        AnsiConsole.WriteLine();

        // Route to appropriate solver
        if (settings.UseGeneticAlgorithm || settings.UseHybrid)
        {
            return SolveWithGeneticAlgorithm(puzzle, knownSolution, settings, cancellationToken);
        }
        else
        {
            return SolveWithML(puzzle, knownSolution, settings, cancellationToken);
        }
    }

    private int SolveWithML(SudokuGrid puzzle, SudokuGrid? knownSolution, Settings settings, CancellationToken cancellationToken)
    {
        // Check model exists
        if (!File.Exists(settings.ModelPath))
        {
            AnsiConsole.MarkupLine($"[red]Model not found:[/] {settings.ModelPath}");
            AnsiConsole.MarkupLine("[dim]Train a model first with:[/] [cyan]decima train[/]");
            return 1;
        }

        // Load model and solve
        using var trainer = new SudokuTrainer(inferenceOnly: true);
        trainer.Verbose = false;
        trainer.Load(settings.ModelPath);

        SudokuGrid solution = default;

        if (settings.Animate && settings.BeamWidth == 1)
        {
            // Animation only works with greedy (non-beam) search
            solution = SolveAnimation.AnimateSolve(trainer, puzzle, settings.AnimationDelay);
        }
        else
        {
            // Beam search (default) - more accurate
            var statusMessage = settings.BeamWidth > 1
                ? $"Solving with beam search (width={settings.BeamWidth})..."
                : "Solving...";

            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("cyan"))
                .Start(statusMessage, ctx =>
                {
                    solution = settings.BeamWidth > 1
                        ? trainer.SolveWithBeamSearch(puzzle, settings.BeamWidth)
                        : trainer.Solve(puzzle);
                });
        }

        // Display and validate result
        DisplayResult(puzzle, solution, knownSolution, settings.Compare);

        return 0;
    }

    private int SolveWithGeneticAlgorithm(SudokuGrid puzzle, SudokuGrid? knownSolution, Settings settings, CancellationToken cancellationToken)
    {
        var options = new GeneticSolverOptions
        {
            PopulationSize = settings.PopulationSize,
            MaxGenerations = settings.MaxGenerations,
            MutationRate = settings.MutationRate,
            IslandCount = settings.IslandCount,
            UseGpu = true,
            Verbose = false
        };

        // Determine model path for hybrid mode
        string? modelPath = settings.UseHybrid && File.Exists(settings.ModelPath) ? settings.ModelPath : null;

        if (settings.UseHybrid && modelPath == null)
        {
            AnsiConsole.MarkupLine("[yellow]Warning:[/] Hybrid mode requested but model not found. Using pure GA.");
        }

        GeneticSolverResult result;

        if (settings.UseIslands)
        {
            AnsiConsole.MarkupLine($"[dim]Using Island Model GA ({options.IslandCount} islands, {options.PopulationPerIsland} each)[/]");

            using var solver = new IslandModel(options, modelPath);
            result = solver.SolveWithProgress(puzzle, cancellationToken);
        }
        else
        {
            AnsiConsole.MarkupLine($"[dim]Using Genetic Algorithm (pop={options.PopulationSize}, gen={options.MaxGenerations})[/]");
            if (modelPath != null)
            {
                AnsiConsole.MarkupLine("[dim]Hybrid mode: seeding from ML predictions[/]");
            }

            using var solver = new GeneticSolver(options, modelPath);
            result = solver.SolveWithProgress(puzzle, cancellationToken);
        }

        // Display result
        AnsiConsole.WriteLine();
        AnsiConsole.Write(SudokuGridRenderer.RenderWithBoxes(result.Solution, puzzle, title: "GA Solution"));
        AnsiConsole.WriteLine();

        // Statistics
        AnsiConsole.MarkupLine($"[dim]Generations:[/] {result.Generations}");
        AnsiConsole.MarkupLine($"[dim]Time:[/] {result.ElapsedTime.TotalSeconds:F2}s");
        AnsiConsole.MarkupLine($"[dim]Final fitness:[/] {result.Fitness}");

        // Validation message
        if (result.IsSolved)
        {
            AnsiConsole.MarkupLine("[bold green]✓ Valid solution![/]");
        }
        else if (!result.Solution.IsComplete)
        {
            var empty = result.Solution.EmptyCellCount;
            AnsiConsole.MarkupLine($"[bold yellow]⚠ Incomplete: {empty} cells remaining[/]");
        }
        else
        {
            var conflicts = SudokuValidator.GetAllConflicts(result.Solution);
            AnsiConsole.MarkupLine($"[bold red]✗ Invalid: {conflicts.Count} conflicting cells (fitness={result.Fitness})[/]");
        }

        // Compare with known solution
        if (knownSolution.HasValue)
        {
            var correct = 0;
            var total = 0;

            for (var row = 0; row < 9; row++)
            {
                for (var col = 0; col < 9; col++)
                {
                    if (puzzle[row, col] == 0)
                    {
                        total++;
                        if (result.Solution[row, col] == knownSolution.Value[row, col])
                        {
                            correct++;
                        }
                    }
                }
            }

            AnsiConsole.MarkupLine($"[dim]Accuracy vs generated solution:[/] {correct}/{total} ({(double)correct / total:P1})");
        }

        // Compare with backtracking solver
        if (settings.Compare)
        {
            AnsiConsole.WriteLine();
            SolveAnimation.CompareWithBacktracking(puzzle, result.Solution);
        }

        return result.IsSolved ? 0 : 1;
    }

    private void DisplayResult(SudokuGrid puzzle, SudokuGrid solution, SudokuGrid? knownSolution, bool compare)
    {
        // Display result
        AnsiConsole.WriteLine();
        AnsiConsole.Write(SudokuGridRenderer.RenderWithBoxes(solution, puzzle, title: "Solution"));
        AnsiConsole.WriteLine();

        // Validation message
        if (solution.IsComplete && solution.IsValid())
        {
            AnsiConsole.MarkupLine("[bold green]✓ Valid solution![/]");
        }
        else if (!solution.IsComplete)
        {
            var empty = solution.EmptyCellCount;
            AnsiConsole.MarkupLine($"[bold yellow]⚠ Incomplete: {empty} cells remaining[/]");
        }
        else
        {
            var conflicts = SudokuValidator.GetAllConflicts(solution);
            AnsiConsole.MarkupLine($"[bold red]✗ Invalid: {conflicts.Count} conflicting cells[/]");
        }

        // Compare with known solution if available
        if (knownSolution.HasValue)
        {
            var correct = 0;
            var total = 0;

            for (var row = 0; row < 9; row++)
            {
                for (var col = 0; col < 9; col++)
                {
                    if (puzzle[row, col] == 0)
                    {
                        total++;
                        if (solution[row, col] == knownSolution.Value[row, col])
                        {
                            correct++;
                        }
                    }
                }
            }

            AnsiConsole.MarkupLine($"[dim]Accuracy vs generated solution:[/] {correct}/{total} ({(double)correct / total:P1})");
        }

        // Compare with backtracking solver
        if (compare)
        {
            AnsiConsole.WriteLine();
            SolveAnimation.CompareWithBacktracking(puzzle, solution);
        }
    }
}
