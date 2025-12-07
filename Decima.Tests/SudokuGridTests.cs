namespace Decima.Tests;

using Decima.Data;

public class SudokuGridTests
{
    // A valid complete Sudoku solution
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

    // A valid puzzle with empty cells (represented by dots)
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

    #region Parse Tests

    [Fact]
    public void Parse_ValidSolution_ReturnsGrid()
    {
        var grid = SudokuGrid.Parse(ValidSolution);

        Assert.Equal(5, grid[0, 0]);
        Assert.Equal(9, grid[8, 8]);
        Assert.True(grid.IsComplete);
    }

    [Fact]
    public void Parse_ValidPuzzleWithDots_ReturnsGridWithZeros()
    {
        var grid = SudokuGrid.Parse(ValidPuzzle);

        Assert.Equal(5, grid[0, 0]);
        Assert.Equal(0, grid[0, 2]); // dot becomes 0
        Assert.False(grid.IsComplete);
    }

    [Fact]
    public void Parse_ValidPuzzleWithZeros_ReturnsGridWithZeros()
    {
        var input = ValidPuzzle.Replace('.', '0');
        var grid = SudokuGrid.Parse(input);

        Assert.Equal(5, grid[0, 0]);
        Assert.Equal(0, grid[0, 2]);
    }

    [Fact]
    public void Parse_InputWithNewlines_IgnoresNewlines()
    {
        var input = "534678912\n672195348\n198342567\n859761423\n426853791\n713924856\n961537284\n287419635\n345286179";
        var grid = SudokuGrid.Parse(input);

        Assert.True(grid.IsComplete);
        Assert.Equal(5, grid[0, 0]);
    }

    [Fact]
    public void Parse_TooFewCells_ThrowsArgumentException()
    {
        var input = "12345678"; // Only 8 cells

        var ex = Assert.Throws<ArgumentException>(() => SudokuGrid.Parse(input));
        Assert.Contains("81", ex.Message);
    }

    [Fact]
    public void Parse_NullInput_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => SudokuGrid.Parse(null!));
    }

    #endregion

    #region TryParse Tests

    [Fact]
    public void TryParse_ValidInput_ReturnsTrue()
    {
        var result = SudokuGrid.TryParse(ValidSolution, out var grid);

        Assert.True(result);
        Assert.Equal(5, grid[0, 0]);
    }

    [Fact]
    public void TryParse_InvalidInput_ReturnsFalse()
    {
        var result = SudokuGrid.TryParse("invalid", out var grid);

        Assert.False(result);
        Assert.Equal(default, grid);
    }

    [Fact]
    public void TryParse_NullInput_ReturnsFalse()
    {
        var result = SudokuGrid.TryParse(null, out var grid);

        Assert.False(result);
    }

    [Fact]
    public void TryParse_EmptyInput_ReturnsFalse()
    {
        var result = SudokuGrid.TryParse(string.Empty, out _);

        Assert.False(result);
    }

    #endregion

    #region ToString Tests

    [Fact]
    public void ToString_CompleteSolution_ReturnsCorrectString()
    {
        var grid = SudokuGrid.Parse(ValidSolution);

        var result = grid.ToString();

        Assert.Equal(ValidSolution, result);
    }

    [Fact]
    public void ToString_PuzzleWithEmptyCells_UsesDotsForZeros()
    {
        var grid = SudokuGrid.Parse(ValidPuzzle);

        var result = grid.ToString();

        Assert.Equal(ValidPuzzle, result);
    }

    [Fact]
    public void ToString_RoundTrip_PreservesData()
    {
        var original = SudokuGrid.Parse(ValidPuzzle);

        var serialized = original.ToString();
        var restored = SudokuGrid.Parse(serialized);

        Assert.Equal(original.ToString(), restored.ToString());
    }

    #endregion

    #region Indexer Tests

    [Fact]
    public void Indexer_RowCol_ReturnsCorrectValue()
    {
        var grid = SudokuGrid.Parse(ValidSolution);

        Assert.Equal(5, grid[0, 0]);
        Assert.Equal(3, grid[0, 1]);
        Assert.Equal(6, grid[1, 0]);
    }

    [Fact]
    public void Indexer_LinearIndex_ReturnsCorrectValue()
    {
        var grid = SudokuGrid.Parse(ValidSolution);

        Assert.Equal(5, grid[0]);  // First cell
        Assert.Equal(3, grid[1]);  // Second cell
        Assert.Equal(6, grid[9]);  // First cell of second row
    }

    #endregion

    #region Property Tests

    [Fact]
    public void IsComplete_CompleteSolution_ReturnsTrue()
    {
        var grid = SudokuGrid.Parse(ValidSolution);

        Assert.True(grid.IsComplete);
    }

    [Fact]
    public void IsComplete_PuzzleWithEmptyCells_ReturnsFalse()
    {
        var grid = SudokuGrid.Parse(ValidPuzzle);

        Assert.False(grid.IsComplete);
    }

    [Fact]
    public void EmptyCellCount_CompleteSolution_ReturnsZero()
    {
        var grid = SudokuGrid.Parse(ValidSolution);

        Assert.Equal(0, grid.EmptyCellCount);
    }

    [Fact]
    public void EmptyCellCount_Puzzle_ReturnsCorrectCount()
    {
        var grid = SudokuGrid.Parse(ValidPuzzle);

        var expectedEmpty = ValidPuzzle.Count(c => c == '.');
        Assert.Equal(expectedEmpty, grid.EmptyCellCount);
    }

    [Fact]
    public void Empty_ReturnsGridWithAllZeros()
    {
        var grid = SudokuGrid.Empty;

        Assert.Equal(81, grid.EmptyCellCount);
        Assert.False(grid.IsComplete);
    }

    #endregion

    #region WithCell Tests

    [Fact]
    public void WithCell_ReturnsNewGridWithUpdatedCell()
    {
        var original = SudokuGrid.Parse(ValidPuzzle);

        var modified = original.WithCell(0, 2, 4);

        Assert.Equal(0, original[0, 2]); // Original unchanged
        Assert.Equal(4, modified[0, 2]); // New grid has the value
    }

    [Fact]
    public void WithCell_PreservesOtherCells()
    {
        var original = SudokuGrid.Parse(ValidPuzzle);

        var modified = original.WithCell(0, 2, 4);

        Assert.Equal(original[0, 0], modified[0, 0]);
        Assert.Equal(original[8, 8], modified[8, 8]);
    }

    #endregion

    #region Copy Tests

    [Fact]
    public void Copy_CreateIndependentCopy()
    {
        var original = SudokuGrid.Parse(ValidPuzzle);

        var copy = original.Copy();

        Assert.Equal(original.ToString(), copy.ToString());
    }

    #endregion

    #region FromArray Tests

    [Fact]
    public void FromArray_ValidArray_ReturnsGrid()
    {
        var cells = new int[9, 9];
        cells[0, 0] = 5;
        cells[8, 8] = 9;

        var grid = SudokuGrid.FromArray(cells);

        Assert.Equal(5, grid[0, 0]);
        Assert.Equal(9, grid[8, 8]);
    }

    [Fact]
    public void FromArray_WrongSize_ThrowsArgumentException()
    {
        var cells = new int[8, 9]; // Wrong size

        Assert.Throws<ArgumentException>(() => SudokuGrid.FromArray(cells));
    }

    #endregion

    #region Constants Tests

    [Fact]
    public void Constants_HaveCorrectValues()
    {
        Assert.Equal(9, SudokuGrid.Size);
        Assert.Equal(3, SudokuGrid.BoxSize);
        Assert.Equal(81, SudokuGrid.TotalCells);
    }

    #endregion
}
