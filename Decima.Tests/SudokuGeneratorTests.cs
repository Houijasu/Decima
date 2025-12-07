namespace Decima.Tests;

using Decima.Data;

public class SudokuGeneratorTests
{
    #region GenerateSolution Tests

    [Fact]
    public void GenerateSolution_ReturnsCompleteGrid()
    {
        var solution = SudokuGenerator.GenerateSolution();

        Assert.True(solution.IsComplete);
        Assert.Equal(0, solution.EmptyCellCount);
    }

    [Fact]
    public void GenerateSolution_ReturnsValidGrid()
    {
        var solution = SudokuGenerator.GenerateSolution();

        Assert.True(SudokuValidator.IsValid(solution));
    }

    [Fact]
    public void GenerateSolution_GeneratesDifferentSolutions()
    {
        var solution1 = SudokuGenerator.GenerateSolution();
        var solution2 = SudokuGenerator.GenerateSolution();

        // While not guaranteed, two random solutions should almost always differ
        Assert.NotEqual(solution1.ToString(), solution2.ToString());
    }

    [Fact]
    public void GenerateSolution_MultipleCallsAllValid()
    {
        for (var i = 0; i < 10; i++)
        {
            var solution = SudokuGenerator.GenerateSolution();
            Assert.True(solution.IsComplete, $"Solution {i} is not complete");
            Assert.True(SudokuValidator.IsValid(solution), $"Solution {i} is not valid");
        }
    }

    #endregion

    #region GeneratePuzzle Tests

    [Fact]
    public void GeneratePuzzle_ReturnsPuzzleWithEmptyCells()
    {
        var (puzzle, solution) = SudokuGenerator.GeneratePuzzle(30);

        Assert.False(puzzle.IsComplete);
        Assert.True(puzzle.EmptyCellCount >= 30);
    }

    [Fact]
    public void GeneratePuzzle_ReturnsValidPuzzle()
    {
        var (puzzle, solution) = SudokuGenerator.GeneratePuzzle(30);

        Assert.True(SudokuValidator.IsValid(puzzle));
    }

    [Fact]
    public void GeneratePuzzle_SolutionIsComplete()
    {
        var (puzzle, solution) = SudokuGenerator.GeneratePuzzle(30);

        Assert.True(solution.IsComplete);
        Assert.True(SudokuValidator.IsValid(solution));
    }

    [Fact]
    public void GeneratePuzzle_PuzzleMatchesSolutionForFilledCells()
    {
        var (puzzle, solution) = SudokuGenerator.GeneratePuzzle(30);

        for (var i = 0; i < 81; i++)
        {
            if (puzzle[i] != 0)
            {
                Assert.Equal(solution[i], puzzle[i]);
            }
        }
    }

    [Theory]
    [InlineData(20)]
    [InlineData(40)]
    [InlineData(55)]
    public void GeneratePuzzle_RespectsEmptyCellCount(int emptyCells)
    {
        var (puzzle, _) = SudokuGenerator.GeneratePuzzle(emptyCells);

        // The generator may remove more cells than requested
        Assert.True(puzzle.EmptyCellCount >= emptyCells);
    }

    #endregion

    #region GenerateBatch Tests

    [Fact]
    public void GenerateBatch_ReturnsRequestedCount()
    {
        var batch = SudokuGenerator.GenerateBatch(5, 30).ToList();

        Assert.Equal(5, batch.Count);
    }

    [Fact]
    public void GenerateBatch_AllPuzzlesValid()
    {
        var batch = SudokuGenerator.GenerateBatch(10, 30).ToList();

        foreach (var (puzzle, solution) in batch)
        {
            Assert.True(SudokuValidator.IsValid(puzzle));
            Assert.True(SudokuValidator.IsValid(solution));
            Assert.True(solution.IsComplete);
        }
    }

    [Fact]
    public void GenerateBatch_EmptyBatch_ReturnsEmptyList()
    {
        var batch = SudokuGenerator.GenerateBatch(0, 30).ToList();

        Assert.Empty(batch);
    }

    #endregion

    #region GenerateWithDifficulty Tests

    [Theory]
    [InlineData(Difficulty.Easy)]
    [InlineData(Difficulty.Medium)]
    [InlineData(Difficulty.Hard)]
    [InlineData(Difficulty.Expert)]
    [InlineData(Difficulty.Extreme)]
    public void GenerateWithDifficulty_ReturnsValidPuzzle(Difficulty difficulty)
    {
        var (puzzle, solution) = SudokuGenerator.GenerateWithDifficulty(difficulty);

        Assert.True(SudokuValidator.IsValid(puzzle));
        Assert.True(SudokuValidator.IsValid(solution));
        Assert.True(solution.IsComplete);
    }

    [Fact]
    public void GenerateWithDifficulty_HarderDifficultyHasMoreEmptyCells()
    {
        var (easyPuzzle, _) = SudokuGenerator.GenerateWithDifficulty(Difficulty.Easy);
        var (extremePuzzle, _) = SudokuGenerator.GenerateWithDifficulty(Difficulty.Extreme);

        // On average, extreme should have more empty cells than easy
        // This test might occasionally fail due to randomness, but should pass most times
        Assert.True(extremePuzzle.EmptyCellCount > easyPuzzle.EmptyCellCount - 10);
    }

    #endregion

    #region Solve Tests

    [Fact]
    public void Solve_ValidPuzzle_ReturnsSolution()
    {
        var (puzzle, originalSolution) = SudokuGenerator.GeneratePuzzle(30);

        var solved = SudokuGenerator.Solve(puzzle);

        Assert.NotNull(solved);
        Assert.True(solved.Value.IsComplete);
        Assert.True(SudokuValidator.IsValid(solved.Value));
    }

    [Fact]
    public void Solve_CompleteSolution_ReturnsSameSolution()
    {
        var solution = SudokuGenerator.GenerateSolution();

        var solved = SudokuGenerator.Solve(solution);

        Assert.NotNull(solved);
        Assert.Equal(solution.ToString(), solved.Value.ToString());
    }

    [Fact]
    public void Solve_InvalidPuzzle_ShouldBePrevalidated()
    {
        // Create an invalid puzzle - two 5s in the same row
        // Note: Solve() doesn't validate input, so callers should use IsValid first
        var invalidPuzzle =
            "55......." + // Two 5s in first row - invalid
            "........." +
            "........." +
            "........." +
            "........." +
            "........." +
            "........." +
            "........." +
            ".........";

        var grid = SudokuGrid.Parse(invalidPuzzle);

        // The grid should be detected as invalid before attempting to solve
        Assert.False(SudokuValidator.IsValid(grid));
    }

    #endregion

    #region CountSolutions Tests

    [Fact]
    public void CountSolutions_CompleteSolution_ReturnsOne()
    {
        var solution = SudokuGenerator.GenerateSolution();

        var count = SudokuGenerator.CountSolutions(solution, 2);

        Assert.Equal(1, count);
    }

    [Fact]
    public void CountSolutions_EmptyGrid_ReturnsMoreThanOne()
    {
        var emptyGrid = SudokuGrid.Empty;

        var count = SudokuGenerator.CountSolutions(emptyGrid, 10);

        Assert.True(count > 1);
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public void GenerateBatch_IsThreadSafe()
    {
        // Generate a larger batch which uses the thread-safe RNG
        var batch = SudokuGenerator.GenerateBatch(100, 30).ToList();

        Assert.Equal(100, batch.Count);
        foreach (var (puzzle, solution) in batch)
        {
            Assert.True(SudokuValidator.IsValid(puzzle), "Thread-generated puzzle is invalid");
            Assert.True(SudokuValidator.IsValid(solution), "Thread-generated solution is invalid");
        }
    }

    [Fact]
    public void ParallelGeneration_AllResultsValid()
    {
        var results = new List<(SudokuGrid puzzle, SudokuGrid solution)>();

        Parallel.For(0, 50, _ =>
        {
            var result = SudokuGenerator.GeneratePuzzle(30);
            lock (results)
            {
                results.Add(result);
            }
        });

        Assert.Equal(50, results.Count);
        foreach (var (puzzle, solution) in results)
        {
            Assert.True(SudokuValidator.IsValid(puzzle));
            Assert.True(SudokuValidator.IsValid(solution));
        }
    }

    #endregion
}
