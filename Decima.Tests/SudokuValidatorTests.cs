namespace Decima.Tests;

using Decima.Data;

public class SudokuValidatorTests
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

    // A valid puzzle with empty cells
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

    #region IsValid Tests

    [Fact]
    public void IsValid_ValidCompleteSolution_ReturnsTrue()
    {
        var grid = SudokuGrid.Parse(ValidSolution);

        var result = SudokuValidator.IsValid(grid);

        Assert.True(result);
    }

    [Fact]
    public void IsValid_ValidPuzzleWithEmptyCells_ReturnsTrue()
    {
        var grid = SudokuGrid.Parse(ValidPuzzle);

        var result = SudokuValidator.IsValid(grid);

        Assert.True(result);
    }

    [Fact]
    public void IsValid_DuplicateInRow_ReturnsFalse()
    {
        // Create grid with duplicate 5 in first row
        var invalidGrid =
            "553678912" + // Two 5s in first row
            "672195348" +
            "198342567" +
            "859761423" +
            "426853791" +
            "713924856" +
            "961537284" +
            "287419635" +
            "345286179";

        var grid = SudokuGrid.Parse(invalidGrid);

        var result = SudokuValidator.IsValid(grid);

        Assert.False(result);
    }

    [Fact]
    public void IsValid_DuplicateInColumn_ReturnsFalse()
    {
        // Create grid with duplicate in column
        var invalidGrid =
            "534678912" +
            "572195348" + // 5 at [1,0] conflicts with 5 at [0,0]
            "198342567" +
            "859761423" +
            "426853791" +
            "713924856" +
            "961537284" +
            "287419635" +
            "345286179";

        var grid = SudokuGrid.Parse(invalidGrid);

        var result = SudokuValidator.IsValid(grid);

        Assert.False(result);
    }

    [Fact]
    public void IsValid_DuplicateInBox_ReturnsFalse()
    {
        // Create grid with duplicate in 3x3 box
        var invalidGrid =
            "534678912" +
            "675195348" + // 5 at [1,2] conflicts with 5 at [0,0] in same box
            "198342567" +
            "859761423" +
            "426853791" +
            "713924856" +
            "961537284" +
            "287419635" +
            "345286179";

        var grid = SudokuGrid.Parse(invalidGrid);

        var result = SudokuValidator.IsValid(grid);

        Assert.False(result);
    }

    [Fact]
    public void IsValid_EmptyGrid_ReturnsTrue()
    {
        var grid = SudokuGrid.Empty;

        var result = SudokuValidator.IsValid(grid);

        Assert.True(result);
    }

    #endregion

    #region IsRowValid Tests

    [Fact]
    public void IsRowValid_ValidRow_ReturnsTrue()
    {
        var grid = SudokuGrid.Parse(ValidSolution);

        var result = SudokuValidator.IsRowValid(grid, 0);

        Assert.True(result);
    }

    [Fact]
    public void IsRowValid_RowWithDuplicates_ReturnsFalse()
    {
        var invalidGrid =
            "553678912" + // Duplicate 5
            "672195348" +
            "198342567" +
            "859761423" +
            "426853791" +
            "713924856" +
            "961537284" +
            "287419635" +
            "345286179";

        var grid = SudokuGrid.Parse(invalidGrid);

        var result = SudokuValidator.IsRowValid(grid, 0);

        Assert.False(result);
    }

    [Fact]
    public void IsRowValid_RowWithEmptyCells_ReturnsTrue()
    {
        var grid = SudokuGrid.Parse(ValidPuzzle);

        var result = SudokuValidator.IsRowValid(grid, 0);

        Assert.True(result);
    }

    #endregion

    #region IsColumnValid Tests

    [Fact]
    public void IsColumnValid_ValidColumn_ReturnsTrue()
    {
        var grid = SudokuGrid.Parse(ValidSolution);

        var result = SudokuValidator.IsColumnValid(grid, 0);

        Assert.True(result);
    }

    [Fact]
    public void IsColumnValid_ColumnWithDuplicates_ReturnsFalse()
    {
        var invalidGrid =
            "534678912" +
            "574195348" + // 5 at column 0 duplicates
            "198342567" +
            "859761423" +
            "426853791" +
            "713924856" +
            "961537284" +
            "287419635" +
            "345286179";

        var grid = SudokuGrid.Parse(invalidGrid);

        var result = SudokuValidator.IsColumnValid(grid, 0);

        Assert.False(result);
    }

    #endregion

    #region IsBoxValid Tests

    [Fact]
    public void IsBoxValid_ValidBox_ReturnsTrue()
    {
        var grid = SudokuGrid.Parse(ValidSolution);

        var result = SudokuValidator.IsBoxValid(grid, 0, 0);

        Assert.True(result);
    }

    [Fact]
    public void IsBoxValid_AllBoxesValid_ReturnsTrue()
    {
        var grid = SudokuGrid.Parse(ValidSolution);

        for (var boxRow = 0; boxRow < 3; boxRow++)
        {
            for (var boxCol = 0; boxCol < 3; boxCol++)
            {
                var result = SudokuValidator.IsBoxValid(grid, boxRow * 3, boxCol * 3);
                Assert.True(result, $"Box at ({boxRow * 3}, {boxCol * 3}) should be valid");
            }
        }
    }

    #endregion

    #region CanPlace Tests

    [Fact]
    public void CanPlace_ValidPlacement_ReturnsTrue()
    {
        var grid = SudokuGrid.Parse(ValidPuzzle);

        // Find an empty cell and check if a valid number can be placed
        // Position (0, 2) is empty in ValidPuzzle
        var result = SudokuValidator.CanPlace(grid, 0, 2, 4);

        Assert.True(result);
    }

    [Fact]
    public void CanPlace_ConflictsWithRow_ReturnsFalse()
    {
        var grid = SudokuGrid.Parse(ValidPuzzle);

        // Try to place 5 at (0, 2) - but 5 already exists in row 0 at (0, 0)
        var result = SudokuValidator.CanPlace(grid, 0, 2, 5);

        Assert.False(result);
    }

    [Fact]
    public void CanPlace_ConflictsWithColumn_ReturnsFalse()
    {
        var grid = SudokuGrid.Parse(ValidPuzzle);

        // Try to place 6 at (0, 2) - column 2 row 1 has value from solution
        // Looking at ValidPuzzle: column 0 has 5,6,.,8,4,7,.,.,. 
        // So placing 6 at row 2, col 0 should conflict with 6 at row 1, col 0
        var result = SudokuValidator.CanPlace(grid, 2, 0, 6);

        Assert.False(result);
    }

    [Fact]
    public void CanPlace_ConflictsWithBox_ReturnsFalse()
    {
        var grid = SudokuGrid.Parse(ValidPuzzle);

        // Try to place 9 at (0, 2) - 9 exists in the top-left box at (2, 1)
        var result = SudokuValidator.CanPlace(grid, 0, 2, 9);

        Assert.False(result);
    }

    [Fact]
    public void CanPlace_InvalidValue_ReturnsFalse()
    {
        var grid = SudokuGrid.Parse(ValidPuzzle);

        Assert.False(SudokuValidator.CanPlace(grid, 0, 0, 0));
        Assert.False(SudokuValidator.CanPlace(grid, 0, 0, 10));
        Assert.False(SudokuValidator.CanPlace(grid, 0, 0, -1));
    }

    #endregion

    #region GetPossibleValues Tests

    [Fact]
    public void GetPossibleValues_EmptyCell_ReturnsValidOptions()
    {
        var grid = SudokuGrid.Parse(ValidPuzzle);

        var possibleValues = SudokuValidator.GetPossibleValues(grid, 0, 2);

        Assert.NotEmpty(possibleValues);
        // All returned values should be placeable
        foreach (var value in possibleValues)
        {
            Assert.True(SudokuValidator.CanPlace(grid, 0, 2, value));
        }
    }

    [Fact]
    public void GetPossibleValues_CellWithValue_ReturnsEmpty()
    {
        var grid = SudokuGrid.Parse(ValidSolution);

        // Cell (0,0) = 5 in the solution
        var possibleValues = SudokuValidator.GetPossibleValues(grid, 0, 0);

        // The method might return empty or include the current value - check behavior
        // Based on typical implementation, filled cells return empty
        Assert.Empty(possibleValues);
    }

    #endregion

    #region GetConflicts Tests

    [Fact]
    public void GetConflicts_ValidGrid_ReturnsEmpty()
    {
        var grid = SudokuGrid.Parse(ValidSolution);

        var conflicts = SudokuValidator.GetConflicts(grid, 0, 0);

        Assert.Empty(conflicts);
    }

    [Fact]
    public void GetConflicts_InvalidGrid_ReturnsConflictingCells()
    {
        // Create a grid with a conflict
        var grid = SudokuGrid.Parse(ValidSolution);
        // Modify to create conflict: put 5 at (0,1) which conflicts with 5 at (0,0)
        grid = grid.WithCell(0, 1, 5);

        var conflicts = SudokuValidator.GetConflicts(grid, 0, 0);

        Assert.NotEmpty(conflicts);
    }

    #endregion

    #region GetAllConflicts Tests

    [Fact]
    public void GetAllConflicts_ValidGrid_ReturnsEmpty()
    {
        var grid = SudokuGrid.Parse(ValidSolution);

        var conflicts = SudokuValidator.GetAllConflicts(grid);

        Assert.Empty(conflicts);
    }

    [Fact]
    public void GetAllConflicts_ValidPuzzle_ReturnsEmpty()
    {
        var grid = SudokuGrid.Parse(ValidPuzzle);

        var conflicts = SudokuValidator.GetAllConflicts(grid);

        Assert.Empty(conflicts);
    }

    #endregion
}
