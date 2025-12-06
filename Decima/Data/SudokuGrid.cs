namespace Decima.Data;

using System.Text;

using TorchSharp;

/// <summary>
/// Represents a 9x9 Sudoku grid.
/// Values 1-9 represent filled cells, 0 represents empty cells.
/// </summary>
public readonly record struct SudokuGrid
{
    private readonly int[] _cells;

    /// <summary>
    /// Size of the Sudoku grid (9x9).
    /// </summary>
    public const int Size = 9;

    /// <summary>
    /// Size of each box (3x3).
    /// </summary>
    public const int BoxSize = 3;

    /// <summary>
    /// Total number of cells in the grid.
    /// </summary>
    public const int TotalCells = Size * Size;

    /// <summary>
    /// Creates a new empty Sudoku grid.
    /// </summary>
    public SudokuGrid()
    {
        _cells = new int[TotalCells];
    }

    /// <summary>
    /// Creates a Sudoku grid from an existing array.
    /// </summary>
    private SudokuGrid(int[] cells)
    {
        _cells = cells;
    }

    /// <summary>
    /// Gets or sets the value at the specified row and column.
    /// </summary>
    public int this[int row, int col]
    {
        get => _cells[row * Size + col];
        init => _cells[row * Size + col] = value;
    }

    /// <summary>
    /// Gets the value at the specified linear index.
    /// </summary>
    public int this[int index] => _cells[index];

    /// <summary>
    /// Returns true if all cells are filled (no zeros).
    /// </summary>
    public bool IsComplete => !_cells.Contains(0);

    /// <summary>
    /// Returns the number of empty cells.
    /// </summary>
    public int EmptyCellCount => _cells.Count(c => c == 0);

    /// <summary>
    /// Parses a Sudoku grid from a string representation.
    /// Accepts 81 characters where 1-9 are values and 0/./space are empty cells.
    /// </summary>
    public static SudokuGrid Parse(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var cells = new int[TotalCells];
        var index = 0;

        foreach (var c in input)
        {
            if (index >= TotalCells)
            {
                break;
            }

            if (c is >= '1' and <= '9')
            {
                cells[index++] = c - '0';
            }
            else if (c is '0' or '.' or ' ')
            {
                cells[index++] = 0;
            }
            // Skip other characters (newlines, separators, etc.)
        }

        if (index != TotalCells)
        {
            throw new ArgumentException($"Input must contain exactly {TotalCells} cell values, got {index}.", nameof(input));
        }

        return new SudokuGrid(cells);
    }

    /// <summary>
    /// Attempts to parse a Sudoku grid from a string.
    /// </summary>
    public static bool TryParse(string? input, out SudokuGrid grid)
    {
        grid = default;

        if (string.IsNullOrEmpty(input))
        {
            return false;
        }

        try
        {
            grid = Parse(input);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Validates the current grid state.
    /// Returns true if no constraints are violated (may still have empty cells).
    /// </summary>
    public bool IsValid() => SudokuValidator.IsValid(this);

    /// <summary>
    /// Creates a copy of this grid with the specified cell set to a new value.
    /// </summary>
    public SudokuGrid WithCell(int row, int col, int value)
    {
        var newCells = (int[])_cells.Clone();
        newCells[row * Size + col] = value;
        return new SudokuGrid(newCells);
    }

    /// <summary>
    /// Creates a SudokuGrid from a 2D array.
    /// </summary>
    public static SudokuGrid FromArray(int[,] cells)
    {
        if (cells.GetLength(0) != Size || cells.GetLength(1) != Size)
            throw new ArgumentException($"Array must be {Size}x{Size}", nameof(cells));

        var flat = new int[TotalCells];
        for (var row = 0; row < Size; row++)
        {
            for (var col = 0; col < Size; col++)
            {
                flat[row * Size + col] = cells[row, col];
            }
        }

        return new SudokuGrid(flat);
    }

    /// <summary>
    /// Creates a deep copy of this grid.
    /// </summary>
    public SudokuGrid Copy() => new((int[])_cells.Clone());

    /// <summary>
    /// Converts the grid to a one-hot encoded tensor for ML processing.
    /// Shape: [1, 10, 9, 9] where channel 0 = empty, channels 1-9 = digits.
    /// </summary>
    public torch.Tensor ToTensor(torch.Device? device = null)
    {
        device ??= torch.CPU;

        // Create tensor with shape [1, 10, 9, 9]
        var tensor = torch.zeros([1, 10, Size, Size], dtype: torch.float32, device: device);

        for (var row = 0; row < Size; row++)
        {
            for (var col = 0; col < Size; col++)
            {
                var value = this[row, col];
                // Channel 0 for empty (value=0), channels 1-9 for digits
                tensor[0, value, row, col] = 1.0f;
            }
        }

        return tensor;
    }

    /// <summary>
    /// Creates a grid from model output tensor.
    /// Expects shape [1, 9, 9, 9] with probabilities for each digit (1-9) per cell.
    /// </summary>
    public static SudokuGrid FromOutputTensor(torch.Tensor output)
    {
        var cells = new int[TotalCells];

        // Output shape: [1, 9, 9, 9] - batch, digits, rows, cols
        // Take argmax over digit dimension and add 1 (since digits are 1-9)
        using var predictions = output.argmax(dim: 1) + 1; // [1, 9, 9]
        using var squeezed = predictions.squeeze(0); // [9, 9]
        var data = squeezed.data<long>().ToArray();

        for (var i = 0; i < TotalCells; i++)
        {
            cells[i] = (int)data[i];
        }

        return new SudokuGrid(cells);
    }

    /// <summary>
    /// Merges model predictions with original puzzle (keeping given cells).
    /// </summary>
    public SudokuGrid MergeWithPredictions(SudokuGrid predictions)
    {
        var cells = new int[TotalCells];

        for (var i = 0; i < TotalCells; i++)
        {
            // Keep original value if not empty, otherwise use prediction
            cells[i] = _cells[i] != 0 ? _cells[i] : predictions._cells[i];
        }

        return new SudokuGrid(cells);
    }

    /// <summary>
    /// Returns a compact string representation (81 characters).
    /// </summary>
    public override string ToString()
    {
        var sb = new StringBuilder(TotalCells);
        foreach (var cell in _cells)
        {
            sb.Append(cell == 0 ? '.' : (char)('0' + cell));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Returns a formatted multi-line string representation with box separators.
    /// </summary>
    public string ToFormattedString()
    {
        var sb = new StringBuilder();
        sb.AppendLine("┌───────┬───────┬───────┐");

        for (var row = 0; row < Size; row++)
        {
            sb.Append("│ ");
            for (var col = 0; col < Size; col++)
            {
                var value = this[row, col];
                sb.Append(value == 0 ? "." : value.ToString());
                sb.Append(' ');

                if ((col + 1) % BoxSize == 0)
                {
                    sb.Append("│ ");
                }
            }
            sb.AppendLine();

            if ((row + 1) % BoxSize == 0 && row < Size - 1)
            {
                sb.AppendLine("├───────┼───────┼───────┤");
            }
        }

        sb.AppendLine("└───────┴───────┴───────┘");
        return sb.ToString();
    }

    /// <summary>
    /// Gets the cells as a read-only span.
    /// </summary>
    public ReadOnlySpan<int> AsSpan() => _cells.AsSpan();

    /// <summary>
    /// Returns an empty grid.
    /// </summary>
    public static SudokuGrid Empty => new();
}
