namespace Decima.Data;

/// <summary>
/// Data augmentation for Sudoku puzzles using symmetry transformations.
/// Sudoku has many symmetries that preserve validity:
/// - Digit permutations (9! = 362,880 variations)
/// - Rotations (4 variations: 0°, 90°, 180°, 270°)
/// - Reflections (2 variations: horizontal, vertical)
/// - Band/stack swaps (swapping rows/columns within bands)
/// </summary>
public static class SudokuAugmentation
{
    private static readonly ThreadLocal<Random> ThreadRandom = new(() => new Random());
    private static Random Random => ThreadRandom.Value!;

    /// <summary>
    /// Applies a random augmentation to a puzzle-solution pair.
    /// </summary>
    public static (SudokuGrid Puzzle, SudokuGrid Solution) Augment(SudokuGrid puzzle, SudokuGrid solution)
    {
        var augPuzzle = puzzle;
        var augSolution = solution;

        // Apply random digit permutation (most impactful - 9! variations)
        if (Random.NextDouble() < 0.8)
        {
            var perm = GenerateDigitPermutation();
            augPuzzle = ApplyDigitPermutation(augPuzzle, perm);
            augSolution = ApplyDigitPermutation(augSolution, perm);
        }

        // Apply random rotation (4 variations)
        if (Random.NextDouble() < 0.5)
        {
            var rotations = Random.Next(1, 4); // 1, 2, or 3 rotations of 90°
            for (var i = 0; i < rotations; i++)
            {
                augPuzzle = Rotate90(augPuzzle);
                augSolution = Rotate90(augSolution);
            }
        }

        // Apply random reflection (horizontal or vertical)
        if (Random.NextDouble() < 0.5)
        {
            if (Random.NextDouble() < 0.5)
            {
                augPuzzle = ReflectHorizontal(augPuzzle);
                augSolution = ReflectHorizontal(augSolution);
            }
            else
            {
                augPuzzle = ReflectVertical(augPuzzle);
                augSolution = ReflectVertical(augSolution);
            }
        }

        // Apply band/stack swaps within bands (preserves Sudoku validity)
        if (Random.NextDouble() < 0.5)
        {
            var (p, s) = SwapRowsWithinBands(augPuzzle, augSolution);
            augPuzzle = p;
            augSolution = s;
        }

        if (Random.NextDouble() < 0.5)
        {
            var (p, s) = SwapColsWithinStacks(augPuzzle, augSolution);
            augPuzzle = p;
            augSolution = s;
        }

        return (augPuzzle, augSolution);
    }

    /// <summary>
    /// Generates a random permutation of digits 1-9.
    /// </summary>
    public static int[] GenerateDigitPermutation()
    {
        var perm = new int[10]; // Index 0 unused, indices 1-9 map old digit to new digit
        var digits = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9 };

        // Fisher-Yates shuffle
        for (var i = digits.Count - 1; i > 0; i--)
        {
            var j = Random.Next(i + 1);
            (digits[i], digits[j]) = (digits[j], digits[i]);
        }

        for (var i = 0; i < 9; i++)
        {
            perm[i + 1] = digits[i];
        }

        return perm;
    }

    /// <summary>
    /// Applies a digit permutation to a grid.
    /// </summary>
    public static SudokuGrid ApplyDigitPermutation(SudokuGrid grid, int[] permutation)
    {
        var cells = new int[9, 9];

        for (var row = 0; row < 9; row++)
        {
            for (var col = 0; col < 9; col++)
            {
                var value = grid[row, col];
                cells[row, col] = value == 0 ? 0 : permutation[value];
            }
        }

        return SudokuGrid.FromArray(cells);
    }

    /// <summary>
    /// Rotates the grid 90 degrees clockwise.
    /// </summary>
    public static SudokuGrid Rotate90(SudokuGrid grid)
    {
        var cells = new int[9, 9];

        for (var row = 0; row < 9; row++)
        {
            for (var col = 0; col < 9; col++)
            {
                // (row, col) -> (col, 8-row)
                cells[col, 8 - row] = grid[row, col];
            }
        }

        return SudokuGrid.FromArray(cells);
    }

    /// <summary>
    /// Reflects the grid horizontally (flip left-right).
    /// </summary>
    public static SudokuGrid ReflectHorizontal(SudokuGrid grid)
    {
        var cells = new int[9, 9];

        for (var row = 0; row < 9; row++)
        {
            for (var col = 0; col < 9; col++)
            {
                cells[row, 8 - col] = grid[row, col];
            }
        }

        return SudokuGrid.FromArray(cells);
    }

    /// <summary>
    /// Reflects the grid vertically (flip top-bottom).
    /// </summary>
    public static SudokuGrid ReflectVertical(SudokuGrid grid)
    {
        var cells = new int[9, 9];

        for (var row = 0; row < 9; row++)
        {
            for (var col = 0; col < 9; col++)
            {
                cells[8 - row, col] = grid[row, col];
            }
        }

        return SudokuGrid.FromArray(cells);
    }

    /// <summary>
    /// Randomly swaps rows within each horizontal band (groups of 3 rows).
    /// This preserves Sudoku validity.
    /// </summary>
    public static (SudokuGrid Puzzle, SudokuGrid Solution) SwapRowsWithinBands(SudokuGrid puzzle, SudokuGrid solution)
    {
        var puzzleCells = new int[9, 9];
        var solutionCells = new int[9, 9];

        // Copy original
        for (var row = 0; row < 9; row++)
        {
            for (var col = 0; col < 9; col++)
            {
                puzzleCells[row, col] = puzzle[row, col];
                solutionCells[row, col] = solution[row, col];
            }
        }

        // For each band (0-2, 3-5, 6-8), randomly shuffle the rows
        for (var band = 0; band < 3; band++)
        {
            var bandStart = band * 3;
            var rowOrder = new[] { 0, 1, 2 };

            // Fisher-Yates shuffle
            for (var i = 2; i > 0; i--)
            {
                var j = Random.Next(i + 1);
                (rowOrder[i], rowOrder[j]) = (rowOrder[j], rowOrder[i]);
            }

            // Apply shuffle
            var tempPuzzle = new int[3, 9];
            var tempSolution = new int[3, 9];

            for (var r = 0; r < 3; r++)
            {
                for (var col = 0; col < 9; col++)
                {
                    tempPuzzle[r, col] = puzzleCells[bandStart + rowOrder[r], col];
                    tempSolution[r, col] = solutionCells[bandStart + rowOrder[r], col];
                }
            }

            for (var r = 0; r < 3; r++)
            {
                for (var col = 0; col < 9; col++)
                {
                    puzzleCells[bandStart + r, col] = tempPuzzle[r, col];
                    solutionCells[bandStart + r, col] = tempSolution[r, col];
                }
            }
        }

        return (SudokuGrid.FromArray(puzzleCells), SudokuGrid.FromArray(solutionCells));
    }

    /// <summary>
    /// Randomly swaps columns within each vertical stack (groups of 3 columns).
    /// This preserves Sudoku validity.
    /// </summary>
    public static (SudokuGrid Puzzle, SudokuGrid Solution) SwapColsWithinStacks(SudokuGrid puzzle, SudokuGrid solution)
    {
        var puzzleCells = new int[9, 9];
        var solutionCells = new int[9, 9];

        // Copy original
        for (var row = 0; row < 9; row++)
        {
            for (var col = 0; col < 9; col++)
            {
                puzzleCells[row, col] = puzzle[row, col];
                solutionCells[row, col] = solution[row, col];
            }
        }

        // For each stack (0-2, 3-5, 6-8), randomly shuffle the columns
        for (var stack = 0; stack < 3; stack++)
        {
            var stackStart = stack * 3;
            var colOrder = new[] { 0, 1, 2 };

            // Fisher-Yates shuffle
            for (var i = 2; i > 0; i--)
            {
                var j = Random.Next(i + 1);
                (colOrder[i], colOrder[j]) = (colOrder[j], colOrder[i]);
            }

            // Apply shuffle
            var tempPuzzle = new int[9, 3];
            var tempSolution = new int[9, 3];

            for (var row = 0; row < 9; row++)
            {
                for (var c = 0; c < 3; c++)
                {
                    tempPuzzle[row, c] = puzzleCells[row, stackStart + colOrder[c]];
                    tempSolution[row, c] = solutionCells[row, stackStart + colOrder[c]];
                }
            }

            for (var row = 0; row < 9; row++)
            {
                for (var c = 0; c < 3; c++)
                {
                    puzzleCells[row, stackStart + c] = tempPuzzle[row, c];
                    solutionCells[row, stackStart + c] = tempSolution[row, c];
                }
            }
        }

        return (SudokuGrid.FromArray(puzzleCells), SudokuGrid.FromArray(solutionCells));
    }

    /// <summary>
    /// Swaps two horizontal bands (groups of 3 rows).
    /// </summary>
    public static (SudokuGrid Puzzle, SudokuGrid Solution) SwapBands(SudokuGrid puzzle, SudokuGrid solution, int band1, int band2)
    {
        if (band1 == band2 || band1 < 0 || band1 > 2 || band2 < 0 || band2 > 2)
            return (puzzle, solution);

        var puzzleCells = new int[9, 9];
        var solutionCells = new int[9, 9];

        for (var row = 0; row < 9; row++)
        {
            var newRow = row;
            var band = row / 3;

            if (band == band1) newRow = band2 * 3 + (row % 3);
            else if (band == band2) newRow = band1 * 3 + (row % 3);

            for (var col = 0; col < 9; col++)
            {
                puzzleCells[newRow, col] = puzzle[row, col];
                solutionCells[newRow, col] = solution[row, col];
            }
        }

        return (SudokuGrid.FromArray(puzzleCells), SudokuGrid.FromArray(solutionCells));
    }

    /// <summary>
    /// Swaps two vertical stacks (groups of 3 columns).
    /// </summary>
    public static (SudokuGrid Puzzle, SudokuGrid Solution) SwapStacks(SudokuGrid puzzle, SudokuGrid solution, int stack1, int stack2)
    {
        if (stack1 == stack2 || stack1 < 0 || stack1 > 2 || stack2 < 0 || stack2 > 2)
            return (puzzle, solution);

        var puzzleCells = new int[9, 9];
        var solutionCells = new int[9, 9];

        for (var col = 0; col < 9; col++)
        {
            var newCol = col;
            var stack = col / 3;

            if (stack == stack1) newCol = stack2 * 3 + (col % 3);
            else if (stack == stack2) newCol = stack1 * 3 + (col % 3);

            for (var row = 0; row < 9; row++)
            {
                puzzleCells[row, newCol] = puzzle[row, col];
                solutionCells[row, newCol] = solution[row, col];
            }
        }

        return (SudokuGrid.FromArray(puzzleCells), SudokuGrid.FromArray(solutionCells));
    }
}
