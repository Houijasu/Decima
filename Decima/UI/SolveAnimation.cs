namespace Decima.UI;

using Decima.Data;
using Decima.Models;

using Spectre.Console;

/// <summary>
/// Provides animated visualization for Sudoku solving.
/// </summary>
public static class SolveAnimation
{
    /// <summary>
    /// Animates the solving process step by step.
    /// </summary>
    public static SudokuGrid AnimateSolve(
        SudokuTrainer trainer,
        SudokuGrid puzzle,
        int delayMs = 100,
        bool showConfidence = true)
    {
        var result = puzzle;

        AnsiConsole.Live(SudokuGridRenderer.RenderWithBoxes(puzzle, puzzle, title: "Solving..."))
            .AutoClear(false)
            .Overflow(VerticalOverflow.Ellipsis)
            .Start(ctx =>
            {
                foreach (var (grid, row, col, value, confidence) in trainer.SolveIteratively(puzzle))
                {
                    result = grid;

                    var title = showConfidence
                        ? $"Solving... [{row + 1},{col + 1}]={value} ({confidence:P0})"
                        : $"Solving... [{row + 1},{col + 1}]={value}";

                    ctx.UpdateTarget(SudokuGridRenderer.RenderWithBoxes(
                        grid,
                        puzzle,
                        highlightCell: (row, col),
                        title: title));

                    ctx.Refresh();
                    Thread.Sleep(delayMs);
                }

                // Final result without highlight
                ctx.UpdateTarget(SudokuGridRenderer.RenderWithBoxes(
                    result,
                    puzzle,
                    title: result.IsComplete && result.IsValid() ? "Solved!" : "Complete"));
            });

        return result;
    }

    /// <summary>
    /// Shows solving progress with a status spinner.
    /// </summary>
    public static SudokuGrid SolveWithStatus(SudokuTrainer trainer, SudokuGrid puzzle)
    {
        SudokuGrid result = default;

        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan"))
            .Start("Solving puzzle...", ctx =>
            {
                result = trainer.Solve(puzzle);
            });

        return result;
    }

    /// <summary>
    /// Displays the solving result with validation.
    /// </summary>
    public static void DisplayResult(SudokuGrid puzzle, SudokuGrid solution)
    {
        AnsiConsole.WriteLine();

        var comparison = SudokuGridRenderer.RenderComparison(puzzle, solution, "Input", "Solution");
        AnsiConsole.Write(comparison);

        AnsiConsole.WriteLine();

        // Validation
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
    }

    /// <summary>
    /// Compares ML solution with backtracking solution.
    /// </summary>
    public static void CompareWithBacktracking(SudokuGrid puzzle, SudokuGrid mlSolution)
    {
        var backtrackSolution = SudokuGenerator.Solve(puzzle);

        if (backtrackSolution == null)
        {
            AnsiConsole.MarkupLine("[yellow]Backtracking solver could not find a solution.[/]");
            return;
        }

        var mlCorrect = 0;
        var mlWrong = 0;

        for (var row = 0; row < 9; row++)
        {
            for (var col = 0; col < 9; col++)
            {
                if (puzzle[row, col] == 0)
                {
                    if (mlSolution[row, col] == backtrackSolution.Value[row, col])
                    {
                        mlCorrect++;
                    }
                    else
                    {
                        mlWrong++;
                    }
                }
            }
        }

        var total = mlCorrect + mlWrong;
        var accuracy = total > 0 ? (double)mlCorrect / total : 0;

        AnsiConsole.MarkupLine($"[dim]ML vs Backtracking:[/] {mlCorrect}/{total} cells correct ({accuracy:P1})");
    }
}
