namespace Decima.Tests;

using Decima.Data;

public class SudokuAugmentationTests
{
    private const string ValidSolution =
        "534678912" +
        "672195348" +
        "198342567" +
        "859761423" +
        "426853791" +
        "713924856" +
        "961537284" +
        "287419635" +
        "345286179";

    private const string ValidPuzzle =
        "53..7...." +
        "6..195..." +
        ".98....6." +
        "8...6...3" +
        "4..8.3..1" +
        "7...2...6" +
        ".6....28." +
        "...419..5" +
        "....8..79";

    #region Augment Tests

    [Fact]
    public void Augment_PreservesValidity()
    {
        var puzzle = SudokuGrid.Parse(ValidPuzzle);
        var solution = SudokuGrid.Parse(ValidSolution);

        var (augPuzzle, augSolution) = SudokuAugmentation.Augment(puzzle, solution);

        Assert.True(SudokuValidator.IsValid(augPuzzle), "Augmented puzzle is invalid");
        Assert.True(SudokuValidator.IsValid(augSolution), "Augmented solution is invalid");
    }

    [Fact]
    public void Augment_SolutionRemainsComplete()
    {
        var puzzle = SudokuGrid.Parse(ValidPuzzle);
        var solution = SudokuGrid.Parse(ValidSolution);

        var (_, augSolution) = SudokuAugmentation.Augment(puzzle, solution);

        Assert.True(augSolution.IsComplete);
    }

    [Fact]
    public void Augment_PreservesEmptyCellCount()
    {
        var puzzle = SudokuGrid.Parse(ValidPuzzle);
        var solution = SudokuGrid.Parse(ValidSolution);
        var originalEmptyCount = puzzle.EmptyCellCount;

        var (augPuzzle, _) = SudokuAugmentation.Augment(puzzle, solution);

        Assert.Equal(originalEmptyCount, augPuzzle.EmptyCellCount);
    }

    [Fact]
    public void Augment_PuzzleAndSolutionRemainConsistent()
    {
        var puzzle = SudokuGrid.Parse(ValidPuzzle);
        var solution = SudokuGrid.Parse(ValidSolution);

        var (augPuzzle, augSolution) = SudokuAugmentation.Augment(puzzle, solution);

        // Every filled cell in puzzle should match solution
        for (var i = 0; i < 81; i++)
        {
            if (augPuzzle[i] != 0)
            {
                Assert.Equal(augSolution[i], augPuzzle[i]);
            }
        }
    }

    [Fact]
    public void Augment_MultipleCallsAllValid()
    {
        var puzzle = SudokuGrid.Parse(ValidPuzzle);
        var solution = SudokuGrid.Parse(ValidSolution);

        for (var i = 0; i < 20; i++)
        {
            var (augPuzzle, augSolution) = SudokuAugmentation.Augment(puzzle, solution);

            Assert.True(SudokuValidator.IsValid(augPuzzle), $"Augmented puzzle {i} is invalid");
            Assert.True(SudokuValidator.IsValid(augSolution), $"Augmented solution {i} is invalid");
            Assert.True(augSolution.IsComplete, $"Augmented solution {i} is not complete");
        }
    }

    #endregion

    #region Rotate90 Tests

    [Fact]
    public void Rotate90_PreservesValidity()
    {
        var grid = SudokuGrid.Parse(ValidSolution);

        var rotated = SudokuAugmentation.Rotate90(grid);

        Assert.True(SudokuValidator.IsValid(rotated));
    }

    [Fact]
    public void Rotate90_FourRotationsReturnToOriginal()
    {
        var grid = SudokuGrid.Parse(ValidSolution);

        var rotated = grid;
        for (var i = 0; i < 4; i++)
        {
            rotated = SudokuAugmentation.Rotate90(rotated);
        }

        Assert.Equal(grid.ToString(), rotated.ToString());
    }

    [Fact]
    public void Rotate90_ChangesGrid()
    {
        var grid = SudokuGrid.Parse(ValidSolution);

        var rotated = SudokuAugmentation.Rotate90(grid);

        Assert.NotEqual(grid.ToString(), rotated.ToString());
    }

    [Fact]
    public void Rotate90_CornerCellsRotateCorrectly()
    {
        var grid = SudokuGrid.Parse(ValidSolution);

        var rotated = SudokuAugmentation.Rotate90(grid);

        // Top-left (0,0) -> Top-right (0,8)
        Assert.Equal(grid[0, 0], rotated[0, 8]);
        // Top-right (0,8) -> Bottom-right (8,8)
        Assert.Equal(grid[0, 8], rotated[8, 8]);
        // Bottom-right (8,8) -> Bottom-left (8,0)
        Assert.Equal(grid[8, 8], rotated[8, 0]);
        // Bottom-left (8,0) -> Top-left (0,0)
        Assert.Equal(grid[8, 0], rotated[0, 0]);
    }

    #endregion

    #region ReflectHorizontal Tests

    [Fact]
    public void ReflectHorizontal_PreservesValidity()
    {
        var grid = SudokuGrid.Parse(ValidSolution);

        var reflected = SudokuAugmentation.ReflectHorizontal(grid);

        Assert.True(SudokuValidator.IsValid(reflected));
    }

    [Fact]
    public void ReflectHorizontal_TwiceReturnToOriginal()
    {
        var grid = SudokuGrid.Parse(ValidSolution);

        var reflected = SudokuAugmentation.ReflectHorizontal(grid);
        reflected = SudokuAugmentation.ReflectHorizontal(reflected);

        Assert.Equal(grid.ToString(), reflected.ToString());
    }

    [Fact]
    public void ReflectHorizontal_FlipsLeftAndRight()
    {
        var grid = SudokuGrid.Parse(ValidSolution);

        var reflected = SudokuAugmentation.ReflectHorizontal(grid);

        // First column becomes last column
        for (var row = 0; row < 9; row++)
        {
            Assert.Equal(grid[row, 0], reflected[row, 8]);
            Assert.Equal(grid[row, 8], reflected[row, 0]);
        }
    }

    #endregion

    #region ReflectVertical Tests

    [Fact]
    public void ReflectVertical_PreservesValidity()
    {
        var grid = SudokuGrid.Parse(ValidSolution);

        var reflected = SudokuAugmentation.ReflectVertical(grid);

        Assert.True(SudokuValidator.IsValid(reflected));
    }

    [Fact]
    public void ReflectVertical_TwiceReturnToOriginal()
    {
        var grid = SudokuGrid.Parse(ValidSolution);

        var reflected = SudokuAugmentation.ReflectVertical(grid);
        reflected = SudokuAugmentation.ReflectVertical(reflected);

        Assert.Equal(grid.ToString(), reflected.ToString());
    }

    [Fact]
    public void ReflectVertical_FlipsTopAndBottom()
    {
        var grid = SudokuGrid.Parse(ValidSolution);

        var reflected = SudokuAugmentation.ReflectVertical(grid);

        // First row becomes last row
        for (var col = 0; col < 9; col++)
        {
            Assert.Equal(grid[0, col], reflected[8, col]);
            Assert.Equal(grid[8, col], reflected[0, col]);
        }
    }

    #endregion

    #region ApplyDigitPermutation Tests

    [Fact]
    public void ApplyDigitPermutation_PreservesValidity()
    {
        var grid = SudokuGrid.Parse(ValidSolution);

        // Create a simple permutation: swap 1 and 2
        var perm = new int[] { 0, 2, 1, 3, 4, 5, 6, 7, 8, 9 };
        var permuted = SudokuAugmentation.ApplyDigitPermutation(grid, perm);

        Assert.True(SudokuValidator.IsValid(permuted));
    }

    [Fact]
    public void ApplyDigitPermutation_PreservesStructure()
    {
        var grid = SudokuGrid.Parse(ValidSolution);

        // Identity permutation should return same grid
        var perm = new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        var permuted = SudokuAugmentation.ApplyDigitPermutation(grid, perm);

        Assert.Equal(grid.ToString(), permuted.ToString());
    }

    [Fact]
    public void ApplyDigitPermutation_PreservesEmptyCells()
    {
        var grid = SudokuGrid.Parse(ValidPuzzle);
        var originalEmptyCount = grid.EmptyCellCount;

        var perm = new int[] { 0, 2, 1, 3, 4, 5, 6, 7, 8, 9 };
        var permuted = SudokuAugmentation.ApplyDigitPermutation(grid, perm);

        Assert.Equal(originalEmptyCount, permuted.EmptyCellCount);
    }

    #endregion

    #region SwapRowsWithinBands Tests

    [Fact]
    public void SwapRowsWithinBands_PreservesValidity()
    {
        var puzzle = SudokuGrid.Parse(ValidPuzzle);
        var solution = SudokuGrid.Parse(ValidSolution);

        var (swappedPuzzle, swappedSolution) = SudokuAugmentation.SwapRowsWithinBands(puzzle, solution);

        Assert.True(SudokuValidator.IsValid(swappedPuzzle), "Swapped puzzle is invalid");
        Assert.True(SudokuValidator.IsValid(swappedSolution), "Swapped solution is invalid");
    }

    [Fact]
    public void SwapRowsWithinBands_PreservesCompleteness()
    {
        var puzzle = SudokuGrid.Parse(ValidPuzzle);
        var solution = SudokuGrid.Parse(ValidSolution);

        var (_, swappedSolution) = SudokuAugmentation.SwapRowsWithinBands(puzzle, solution);

        Assert.True(swappedSolution.IsComplete);
    }

    #endregion

    #region SwapColsWithinStacks Tests

    [Fact]
    public void SwapColsWithinStacks_PreservesValidity()
    {
        var puzzle = SudokuGrid.Parse(ValidPuzzle);
        var solution = SudokuGrid.Parse(ValidSolution);

        var (swappedPuzzle, swappedSolution) = SudokuAugmentation.SwapColsWithinStacks(puzzle, solution);

        Assert.True(SudokuValidator.IsValid(swappedPuzzle), "Swapped puzzle is invalid");
        Assert.True(SudokuValidator.IsValid(swappedSolution), "Swapped solution is invalid");
    }

    #endregion

    #region SwapBands Tests

    [Fact]
    public void SwapBands_PreservesValidity()
    {
        var puzzle = SudokuGrid.Parse(ValidPuzzle);
        var solution = SudokuGrid.Parse(ValidSolution);

        var (swappedPuzzle, swappedSolution) = SudokuAugmentation.SwapBands(puzzle, solution, 0, 2);

        Assert.True(SudokuValidator.IsValid(swappedPuzzle), "Swapped puzzle is invalid");
        Assert.True(SudokuValidator.IsValid(swappedSolution), "Swapped solution is invalid");
    }

    [Fact]
    public void SwapBands_SameBand_ReturnsUnchanged()
    {
        var puzzle = SudokuGrid.Parse(ValidPuzzle);
        var solution = SudokuGrid.Parse(ValidSolution);

        var (swappedPuzzle, swappedSolution) = SudokuAugmentation.SwapBands(puzzle, solution, 1, 1);

        Assert.Equal(puzzle.ToString(), swappedPuzzle.ToString());
        Assert.Equal(solution.ToString(), swappedSolution.ToString());
    }

    #endregion

    #region SwapStacks Tests

    [Fact]
    public void SwapStacks_PreservesValidity()
    {
        var puzzle = SudokuGrid.Parse(ValidPuzzle);
        var solution = SudokuGrid.Parse(ValidSolution);

        var (swappedPuzzle, swappedSolution) = SudokuAugmentation.SwapStacks(puzzle, solution, 0, 2);

        Assert.True(SudokuValidator.IsValid(swappedPuzzle), "Swapped puzzle is invalid");
        Assert.True(SudokuValidator.IsValid(swappedSolution), "Swapped solution is invalid");
    }

    [Fact]
    public void SwapStacks_SameStack_ReturnsUnchanged()
    {
        var puzzle = SudokuGrid.Parse(ValidPuzzle);
        var solution = SudokuGrid.Parse(ValidSolution);

        var (swappedPuzzle, swappedSolution) = SudokuAugmentation.SwapStacks(puzzle, solution, 1, 1);

        Assert.Equal(puzzle.ToString(), swappedPuzzle.ToString());
        Assert.Equal(solution.ToString(), swappedSolution.ToString());
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public void Augment_IsThreadSafe()
    {
        var puzzle = SudokuGrid.Parse(ValidPuzzle);
        var solution = SudokuGrid.Parse(ValidSolution);
        var results = new List<(SudokuGrid puzzle, SudokuGrid solution)>();

        Parallel.For(0, 50, _ =>
        {
            var (augPuzzle, augSolution) = SudokuAugmentation.Augment(puzzle, solution);
            lock (results)
            {
                results.Add((augPuzzle, augSolution));
            }
        });

        Assert.Equal(50, results.Count);
        foreach (var (augPuzzle, augSolution) in results)
        {
            Assert.True(SudokuValidator.IsValid(augPuzzle), "Thread-augmented puzzle is invalid");
            Assert.True(SudokuValidator.IsValid(augSolution), "Thread-augmented solution is invalid");
        }
    }

    #endregion
}
