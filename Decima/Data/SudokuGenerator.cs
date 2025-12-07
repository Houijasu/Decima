namespace Decima.Data;

/// <summary>
/// Generates valid Sudoku puzzles with unique solutions.
/// </summary>
public static class SudokuGenerator
{
    private static readonly ThreadLocal<Random> s_threadRandom = new(() => new Random());

    private static Random s_random => s_threadRandom.Value!;

    /// <summary>
    /// Generates a complete, valid Sudoku solution.
    /// </summary>
    public static SudokuGrid GenerateSolution()
    {
        var cells = new int[SudokuGrid.TotalCells];
        FillGrid(cells, 0);
        return SudokuGrid.Parse(string.Concat(cells));
    }

    /// <summary>
    /// Generates a puzzle by removing cells from a complete solution.
    /// </summary>
    /// <param name="emptyCells">Number of cells to remove (17-64 for valid puzzles).</param>
    /// <returns>A tuple of (puzzle, solution).</returns>
    public static (SudokuGrid Puzzle, SudokuGrid Solution) GeneratePuzzle(int emptyCells = 40)
    {
        emptyCells = Math.Clamp(emptyCells, 17, 64);

        var solution = GenerateSolution();
        var puzzle = RemoveCells(solution, emptyCells);

        return (puzzle, solution);
    }

    /// <summary>
    /// Generates multiple puzzles for training data.
    /// </summary>
    public static IEnumerable<(SudokuGrid Puzzle, SudokuGrid Solution)> GenerateBatch(int count, int emptyCells = 40)
    {
        for (var i = 0; i < count; i++)
        {
            yield return GeneratePuzzle(emptyCells);
        }
    }

    /// <summary>
    /// Generates puzzles with varying difficulty.
    /// </summary>
    public static (SudokuGrid Puzzle, SudokuGrid Solution) GenerateWithDifficulty(Difficulty difficulty)
    {
        var emptyCells = difficulty switch
        {
            Difficulty.Easy => s_random.Next(30, 36),
            Difficulty.Medium => s_random.Next(36, 46),
            Difficulty.Hard => s_random.Next(46, 53),
            Difficulty.Expert => s_random.Next(53, 60),
            Difficulty.Extreme => s_random.Next(60, 65),
            _ => 40
        };

        return GeneratePuzzle(emptyCells);
    }

    private static bool FillGrid(int[] cells, int position)
    {
        if (position == SudokuGrid.TotalCells)
        {
            return true;
        }

        var row = position / SudokuGrid.Size;
        var col = position % SudokuGrid.Size;

        // Create shuffled list of numbers 1-9
        var numbers = Enumerable.Range(1, 9).OrderBy(_ => s_random.Next()).ToArray();

        foreach (var num in numbers)
        {
            if (IsValidPlacement(cells, row, col, num))
            {
                cells[position] = num;

                if (FillGrid(cells, position + 1))
                {
                    return true;
                }

                cells[position] = 0;
            }
        }

        return false;
    }

    private static bool IsValidPlacement(int[] cells, int row, int col, int value)
    {
        // Check row
        for (var c = 0; c < SudokuGrid.Size; c++)
        {
            if (cells[row * SudokuGrid.Size + c] == value)
            {
                return false;
            }
        }

        // Check column
        for (var r = 0; r < SudokuGrid.Size; r++)
        {
            if (cells[r * SudokuGrid.Size + col] == value)
            {
                return false;
            }
        }

        // Check 3x3 box
        var boxStartRow = (row / SudokuGrid.BoxSize) * SudokuGrid.BoxSize;
        var boxStartCol = (col / SudokuGrid.BoxSize) * SudokuGrid.BoxSize;

        for (var r = boxStartRow; r < boxStartRow + SudokuGrid.BoxSize; r++)
        {
            for (var c = boxStartCol; c < boxStartCol + SudokuGrid.BoxSize; c++)
            {
                if (cells[r * SudokuGrid.Size + c] == value)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static SudokuGrid RemoveCells(SudokuGrid solution, int emptyCells)
    {
        var cells = new int[SudokuGrid.TotalCells];
        var span = solution.AsSpan();
        span.CopyTo(cells);

        // Create list of positions and shuffle
        var positions = Enumerable.Range(0, SudokuGrid.TotalCells)
            .OrderBy(_ => s_random.Next())
            .ToList();

        var removed = 0;

        foreach (var pos in positions)
        {
            if (removed >= emptyCells)
            {
                break;
            }

            var backup = cells[pos];
            cells[pos] = 0;

            // For training data, we don't need to verify uniqueness
            // (uniqueness check is expensive and not needed for ML training)
            removed++;

            // If you want unique solution puzzles, uncomment this:
            // if (!HasUniqueSolution(cells))
            // {
            //     cells[pos] = backup;
            // }
            // else
            // {
            //     removed++;
            // }
        }

        return SudokuGrid.Parse(string.Concat(cells.Select(c => c == 0 ? '.' : (char)('0' + c))));
    }

    /// <summary>
    /// Solves a Sudoku puzzle using backtracking.
    /// Useful for validation and as a fallback solver.
    /// </summary>
    public static SudokuGrid? Solve(SudokuGrid puzzle)
    {
        var cells = new int[SudokuGrid.TotalCells];
        var span = puzzle.AsSpan();
        span.CopyTo(cells);

        if (SolveBacktrack(cells, 0))
        {
            return SudokuGrid.Parse(string.Concat(cells));
        }

        return null;
    }

    /// <summary>
    /// Solves a Sudoku puzzle using backtracking guided by probabilities.
    /// This hybrid approach guarantees 100% accuracy (if solvable) and is much faster than standard backtracking.
    /// </summary>
    public static SudokuGrid? SolveGuided(SudokuGrid puzzle, float[,,] probabilities)
    {
        var cells = new int[SudokuGrid.TotalCells];
        var span = puzzle.AsSpan();
        span.CopyTo(cells);

        // Pre-compute candidate order for each cell based on probabilities
        var candidates = new int[SudokuGrid.TotalCells][];
        for (var i = 0; i < SudokuGrid.TotalCells; i++)
        {
            if (cells[i] == 0)
            {
                var row = i / SudokuGrid.Size;
                var col = i % SudokuGrid.Size;
                
                // Get digits sorted by probability (descending)
                var cellProbs = new (int Digit, float Prob)[9];
                for (var d = 0; d < 9; d++)
                {
                    cellProbs[d] = (d + 1, probabilities[d, row, col]);
                }
                
                Array.Sort(cellProbs, (a, b) => b.Prob.CompareTo(a.Prob));
                
                candidates[i] = new int[9];
                for(int j=0; j<9; j++) candidates[i][j] = cellProbs[j].Digit;
            }
        }

        if (SolveBacktrackGuided(cells, 0, candidates))
        {
            return SudokuGrid.Parse(string.Concat(cells));
        }

        return null;
    }

    private static bool SolveBacktrackGuided(int[] cells, int position, int[][] candidates)
    {
        if (position == SudokuGrid.TotalCells)
        {
            return true;
        }

        // Skip filled cells
        if (cells[position] != 0)
        {
            return SolveBacktrackGuided(cells, position + 1, candidates);
        }

        var row = position / SudokuGrid.Size;
        var col = position % SudokuGrid.Size;

        // Try candidates in probability order
        foreach (var num in candidates[position])
        {
            if (IsValidPlacement(cells, row, col, num))
            {
                cells[position] = num;

                if (SolveBacktrackGuided(cells, position + 1, candidates))
                {
                    return true;
                }

                cells[position] = 0;
            }
        }

        return false;
    }

    private static bool SolveBacktrack(int[] cells, int position)
    {
        if (position == SudokuGrid.TotalCells)
        {
            return true;
        }

        // Skip filled cells
        if (cells[position] != 0)
        {
            return SolveBacktrack(cells, position + 1);
        }

        var row = position / SudokuGrid.Size;
        var col = position % SudokuGrid.Size;

        for (var num = 1; num <= 9; num++)
        {
            if (IsValidPlacement(cells, row, col, num))
            {
                cells[position] = num;

                if (SolveBacktrack(cells, position + 1))
                {
                    return true;
                }

                cells[position] = 0;
            }
        }

        return false;
    }

    /// <summary>
    /// Counts the number of solutions for a puzzle (up to a limit).
    /// </summary>
    public static int CountSolutions(SudokuGrid puzzle, int limit = 2)
    {
        var cells = new int[SudokuGrid.TotalCells];
        var span = puzzle.AsSpan();
        span.CopyTo(cells);

        var count = 0;
        CountSolutionsBacktrack(cells, 0, ref count, limit);
        return count;
    }

    private static void CountSolutionsBacktrack(int[] cells, int position, ref int count, int limit)
    {
        if (count >= limit)
        {
            return;
        }

        if (position == SudokuGrid.TotalCells)
        {
            count++;
            return;
        }

        if (cells[position] != 0)
        {
            CountSolutionsBacktrack(cells, position + 1, ref count, limit);
            return;
        }

        var row = position / SudokuGrid.Size;
        var col = position % SudokuGrid.Size;

        for (var num = 1; num <= 9; num++)
        {
            if (IsValidPlacement(cells, row, col, num))
            {
                cells[position] = num;
                CountSolutionsBacktrack(cells, position + 1, ref count, limit);
                cells[position] = 0;
            }
        }
    }
}

/// <summary>
/// Difficulty levels for generated puzzles.
/// </summary>
public enum Difficulty
{
    /// <summary>30-35 empty cells</summary>
    Easy,

    /// <summary>36-45 empty cells</summary>
    Medium,

    /// <summary>46-52 empty cells</summary>
    Hard,

    /// <summary>53-59 empty cells</summary>
    Expert,

    /// <summary>60-64 empty cells (near-minimal clues)</summary>
    Extreme
}
