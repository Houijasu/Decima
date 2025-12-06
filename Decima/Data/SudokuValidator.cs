namespace Decima.Data;

/// <summary>
/// Provides validation methods for Sudoku grids.
/// </summary>
public static class SudokuValidator
{
    /// <summary>
    /// Validates that the grid has no constraint violations.
    /// Empty cells (0) are allowed.
    /// </summary>
    public static bool IsValid(SudokuGrid grid)
    {
        // Check all rows
        for (var row = 0; row < SudokuGrid.Size; row++)
        {
            if (!IsRowValid(grid, row))
            {
                return false;
            }
        }

        // Check all columns
        for (var col = 0; col < SudokuGrid.Size; col++)
        {
            if (!IsColumnValid(grid, col))
            {
                return false;
            }
        }

        // Check all 3x3 boxes
        for (var boxRow = 0; boxRow < SudokuGrid.BoxSize; boxRow++)
        {
            for (var boxCol = 0; boxCol < SudokuGrid.BoxSize; boxCol++)
            {
                if (!IsBoxValid(grid, boxRow * SudokuGrid.BoxSize, boxCol * SudokuGrid.BoxSize))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Checks if a specific row has no duplicate values (ignoring zeros).
    /// </summary>
    public static bool IsRowValid(SudokuGrid grid, int row)
    {
        Span<bool> seen = stackalloc bool[SudokuGrid.Size + 1];

        for (var col = 0; col < SudokuGrid.Size; col++)
        {
            var value = grid[row, col];
            if (value != 0)
            {
                if (seen[value])
                {
                    return false;
                }

                seen[value] = true;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks if a specific column has no duplicate values (ignoring zeros).
    /// </summary>
    public static bool IsColumnValid(SudokuGrid grid, int col)
    {
        Span<bool> seen = stackalloc bool[SudokuGrid.Size + 1];

        for (var row = 0; row < SudokuGrid.Size; row++)
        {
            var value = grid[row, col];
            if (value != 0)
            {
                if (seen[value])
                {
                    return false;
                }

                seen[value] = true;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks if a 3x3 box has no duplicate values (ignoring zeros).
    /// </summary>
    public static bool IsBoxValid(SudokuGrid grid, int startRow, int startCol)
    {
        Span<bool> seen = stackalloc bool[SudokuGrid.Size + 1];

        for (var row = startRow; row < startRow + SudokuGrid.BoxSize; row++)
        {
            for (var col = startCol; col < startCol + SudokuGrid.BoxSize; col++)
            {
                var value = grid[row, col];
                if (value != 0)
                {
                    if (seen[value])
                    {
                        return false;
                    }

                    seen[value] = true;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Gets all cells that conflict with the given position.
    /// </summary>
    public static List<(int Row, int Col)> GetConflicts(SudokuGrid grid, int row, int col)
    {
        var conflicts = new List<(int Row, int Col)>();
        var value = grid[row, col];

        if (value == 0)
        {
            return conflicts;
        }

        // Check row conflicts
        for (var c = 0; c < SudokuGrid.Size; c++)
        {
            if (c != col && grid[row, c] == value)
            {
                conflicts.Add((row, c));
            }
        }

        // Check column conflicts
        for (var r = 0; r < SudokuGrid.Size; r++)
        {
            if (r != row && grid[r, col] == value)
            {
                conflicts.Add((r, col));
            }
        }

        // Check box conflicts
        var boxStartRow = (row / SudokuGrid.BoxSize) * SudokuGrid.BoxSize;
        var boxStartCol = (col / SudokuGrid.BoxSize) * SudokuGrid.BoxSize;

        for (var r = boxStartRow; r < boxStartRow + SudokuGrid.BoxSize; r++)
        {
            for (var c = boxStartCol; c < boxStartCol + SudokuGrid.BoxSize; c++)
            {
                if ((r != row || c != col) && grid[r, c] == value)
                {
                    // Avoid duplicates (cell might already be added from row/col check)
                    var cell = (r, c);
                    if (!conflicts.Contains(cell))
                    {
                        conflicts.Add(cell);
                    }
                }
            }
        }

        return conflicts;
    }

    /// <summary>
    /// Gets all conflicting cells in the entire grid.
    /// </summary>
    public static HashSet<(int Row, int Col)> GetAllConflicts(SudokuGrid grid)
    {
        var conflicts = new HashSet<(int Row, int Col)>();

        for (var row = 0; row < SudokuGrid.Size; row++)
        {
            for (var col = 0; col < SudokuGrid.Size; col++)
            {
                if (grid[row, col] != 0)
                {
                    var cellConflicts = GetConflicts(grid, row, col);
                    if (cellConflicts.Count > 0)
                    {
                        conflicts.Add((row, col));
                        foreach (var conflict in cellConflicts)
                        {
                            conflicts.Add(conflict);
                        }
                    }
                }
            }
        }

        return conflicts;
    }

    /// <summary>
    /// Checks if placing a value at the given position would be valid.
    /// </summary>
    public static bool CanPlace(SudokuGrid grid, int row, int col, int value)
    {
        if (value < 1 || value > 9)
        {
            return false;
        }

        // Check row
        for (var c = 0; c < SudokuGrid.Size; c++)
        {
            if (grid[row, c] == value)
            {
                return false;
            }
        }

        // Check column
        for (var r = 0; r < SudokuGrid.Size; r++)
        {
            if (grid[r, col] == value)
            {
                return false;
            }
        }

        // Check box
        var boxStartRow = (row / SudokuGrid.BoxSize) * SudokuGrid.BoxSize;
        var boxStartCol = (col / SudokuGrid.BoxSize) * SudokuGrid.BoxSize;

        for (var r = boxStartRow; r < boxStartRow + SudokuGrid.BoxSize; r++)
        {
            for (var c = boxStartCol; c < boxStartCol + SudokuGrid.BoxSize; c++)
            {
                if (grid[r, c] == value)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Gets all valid values that can be placed at the given position.
    /// </summary>
    public static List<int> GetPossibleValues(SudokuGrid grid, int row, int col)
    {
        var possible = new List<int>();

        if (grid[row, col] != 0)
        {
            return possible;
        }

        for (var value = 1; value <= 9; value++)
        {
            if (CanPlace(grid, row, col, value))
            {
                possible.Add(value);
            }
        }

        return possible;
    }
}
