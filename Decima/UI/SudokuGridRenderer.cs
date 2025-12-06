namespace Decima.UI;

using Decima.Data;

using Spectre.Console;
using Spectre.Console.Rendering;

/// <summary>
/// Renders Sudoku grids as Spectre.Console tables with styling.
/// </summary>
public static class SudokuGridRenderer
{
    /// <summary>
    /// Renders a Sudoku grid as a styled table.
    /// </summary>
    /// <param name="grid">The grid to render.</param>
    /// <param name="originalPuzzle">Optional original puzzle to highlight given vs solved cells.</param>
    /// <param name="highlightCell">Optional cell to highlight (e.g., cursor position).</param>
    /// <param name="conflicts">Optional set of conflicting cells to mark in red.</param>
    /// <param name="title">Optional title for the panel.</param>
    public static IRenderable Render(
        SudokuGrid grid,
        SudokuGrid? originalPuzzle = null,
        (int Row, int Col)? highlightCell = null,
        HashSet<(int Row, int Col)>? conflicts = null,
        string? title = null)
    {
        var table = new Table()
            .Border(TableBorder.Heavy)
            .BorderColor(Color.Grey)
            .HideHeaders();

        // Add 9 columns
        for (var col = 0; col < SudokuGrid.Size; col++)
        {
            table.AddColumn(new TableColumn(string.Empty).Centered().Width(3));
        }

        // Add rows
        for (var row = 0; row < SudokuGrid.Size; row++)
        {
            var cells = new List<IRenderable>();

            for (var col = 0; col < SudokuGrid.Size; col++)
            {
                var value = grid[row, col];
                var text = value == 0 ? "·" : value.ToString();

                var style = GetCellStyle(
                    row, col, value,
                    originalPuzzle,
                    highlightCell,
                    conflicts);

                cells.Add(new Markup(text, style));
            }

            table.AddRow(cells.ToArray());

            // Add thicker separator after every 3rd row (except last)
            if ((row + 1) % SudokuGrid.BoxSize == 0 && row < SudokuGrid.Size - 1)
            {
                // We'll handle this with border styling
            }
        }

        // Wrap in panel if title is provided
        if (!string.IsNullOrEmpty(title))
        {
            var panel = new Panel(table)
                .Header(title, Justify.Center)
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Cyan1);

            return panel;
        }

        return table;
    }

    /// <summary>
    /// Renders a grid with box separators using a custom layout.
    /// </summary>
    public static IRenderable RenderWithBoxes(
        SudokuGrid grid,
        SudokuGrid? originalPuzzle = null,
        (int Row, int Col)? highlightCell = null,
        HashSet<(int Row, int Col)>? conflicts = null,
        string? title = null)
    {
        var rows = new List<IRenderable>();

        // Top border
        rows.Add(new Markup("[grey]╔═══════╤═══════╤═══════╗[/]"));

        for (var row = 0; row < SudokuGrid.Size; row++)
        {
            var rowText = new System.Text.StringBuilder();
            rowText.Append("[grey]║[/] ");

            for (var col = 0; col < SudokuGrid.Size; col++)
            {
                var value = grid[row, col];
                var text = value == 0 ? "·" : value.ToString();

                var color = GetCellColor(row, col, value, originalPuzzle, highlightCell, conflicts);
                rowText.Append($"[{color}]{text}[/] ");

                // Box separator
                if ((col + 1) % SudokuGrid.BoxSize == 0 && col < SudokuGrid.Size - 1)
                {
                    rowText.Append("[grey]│[/] ");
                }
            }

            rowText.Append("[grey]║[/]");
            rows.Add(new Markup(rowText.ToString()));

            // Box separator rows
            if ((row + 1) % SudokuGrid.BoxSize == 0 && row < SudokuGrid.Size - 1)
            {
                rows.Add(new Markup("[grey]╟───────┼───────┼───────╢[/]"));
            }
        }

        // Bottom border
        rows.Add(new Markup("[grey]╚═══════╧═══════╧═══════╝[/]"));

        var content = new Rows(rows);

        if (!string.IsNullOrEmpty(title))
        {
            var panel = new Panel(content)
                .Header($"[cyan]{title}[/]", Justify.Center)
                .Border(BoxBorder.None)
                .Padding(1, 0);

            return panel;
        }

        return content;
    }

    /// <summary>
    /// Renders a compact single-line representation.
    /// </summary>
    public static IRenderable RenderCompact(SudokuGrid grid, SudokuGrid? originalPuzzle = null)
    {
        var text = new System.Text.StringBuilder();

        for (var i = 0; i < SudokuGrid.TotalCells; i++)
        {
            var value = grid[i];
            var row = i / SudokuGrid.Size;
            var col = i % SudokuGrid.Size;

            var isGiven = originalPuzzle.HasValue && originalPuzzle.Value[row, col] != 0;
            var color = isGiven ? "grey" : (value == 0 ? "dim" : "green");

            text.Append($"[{color}]{(value == 0 ? '.' : (char)('0' + value))}[/]");

            if ((col + 1) % SudokuGrid.BoxSize == 0 && col < SudokuGrid.Size - 1)
            {
                text.Append("[grey]|[/]");
            }

            if (col == SudokuGrid.Size - 1 && row < SudokuGrid.Size - 1)
            {
                text.AppendLine();
                if ((row + 1) % SudokuGrid.BoxSize == 0)
                {
                    text.AppendLine("[grey]---+---+---[/]");
                }
            }
        }

        return new Markup(text.ToString());
    }

    /// <summary>
    /// Renders a comparison of puzzle and solution side by side.
    /// </summary>
    public static IRenderable RenderComparison(SudokuGrid puzzle, SudokuGrid solution, string? puzzleTitle = null, string? solutionTitle = null)
    {
        var puzzlePanel = RenderWithBoxes(puzzle, title: puzzleTitle ?? "Puzzle");
        var solutionPanel = RenderWithBoxes(solution, puzzle, title: solutionTitle ?? "Solution");

        return new Columns(puzzlePanel, solutionPanel)
        {
            Expand = false
        };
    }

    private static Style GetCellStyle(
        int row, int col, int value,
        SudokuGrid? originalPuzzle,
        (int Row, int Col)? highlightCell,
        HashSet<(int Row, int Col)>? conflicts)
    {
        // Check for conflicts first (highest priority)
        if (conflicts?.Contains((row, col)) == true)
        {
            return new Style(Color.Red, decoration: Decoration.Bold);
        }

        // Check for highlight (cursor)
        if (highlightCell.HasValue && highlightCell.Value.Row == row && highlightCell.Value.Col == col)
        {
            return new Style(Color.Yellow, Color.Grey23, Decoration.Bold);
        }

        // Empty cell
        if (value == 0)
        {
            return new Style(Color.Grey42);
        }

        // Given cell (from original puzzle)
        if (originalPuzzle.HasValue && originalPuzzle.Value[row, col] != 0)
        {
            return new Style(Color.White, decoration: Decoration.Bold);
        }

        // Solved cell
        return new Style(Color.Green);
    }

    private static string GetCellColor(
        int row, int col, int value,
        SudokuGrid? originalPuzzle,
        (int Row, int Col)? highlightCell,
        HashSet<(int Row, int Col)>? conflicts)
    {
        // Check for conflicts first
        if (conflicts?.Contains((row, col)) == true)
        {
            return "red bold";
        }

        // Check for highlight
        if (highlightCell.HasValue && highlightCell.Value.Row == row && highlightCell.Value.Col == col)
        {
            return "yellow bold on grey23";
        }

        // Empty cell
        if (value == 0)
        {
            return "grey42";
        }

        // Given cell
        if (originalPuzzle.HasValue && originalPuzzle.Value[row, col] != 0)
        {
            return "white bold";
        }

        // Solved cell
        return "green";
    }
}
