namespace Decima.Tests;

using Decima.Data;

public class HybridSolverTests
{
    private const string EasyPuzzle =
        "53..7...." +
        "6..195..." +
        ".98....6." +
        "8...6...3" +
        "4..8.3..1" +
        "7...2...6" +
        ".6....28." +
        "...419..5" +
        "....8..79";

    [Fact]
    public void SolveGuided_WithUniformProbabilities_SolvesPuzzle()
    {
        var puzzle = SudokuGrid.Parse(EasyPuzzle);
        var probs = new float[9, 9, 9];

        // Fill with uniform probabilities
        for (var d = 0; d < 9; d++)
        {
            for (var r = 0; r < 9; r++)
            {
                for (var c = 0; c < 9; c++)
                {
                    probs[d, r, c] = 1.0f / 9.0f;
                }
            }
        }

        var solution = SudokuGenerator.SolveGuided(puzzle, probs);

        Assert.NotNull(solution);
        Assert.True(solution.Value.IsComplete);
        Assert.True(SudokuValidator.IsValid(solution.Value));
    }

    [Fact]
    public void SolveGuided_WithPerfectProbabilities_SolvesPuzzleFast()
    {
        var (puzzle, solution) = SudokuGenerator.GeneratePuzzle(30);
        var probs = new float[9, 9, 9];

        // Fill with perfect probabilities (1.0 for correct digit, 0.0 otherwise)
        for (var r = 0; r < 9; r++)
        {
            for (var c = 0; c < 9; c++)
            {
                var correctDigit = solution[r, c]; // 1-9
                probs[correctDigit - 1, r, c] = 1.0f;
            }
        }

        var result = SudokuGenerator.SolveGuided(puzzle, probs);

        Assert.NotNull(result);
        Assert.Equal(solution.ToString(), result.Value.ToString());
    }
}
