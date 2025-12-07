namespace Decima.Models;

using Decima.Data;

using static TorchSharp.torch;

/// <summary>
/// Iterative refinement solver that fills one cell at a time with re-inference.
/// This approach re-evaluates the neural network after each cell is filled,
/// allowing the model to make better predictions based on the updated grid state.
/// </summary>
public sealed class IterativeRefinementSolver
{
    private readonly SudokuNetwork _model;
    private readonly Device _device;
    private readonly int _maxBacktracks;
    private readonly double _confidenceThreshold;

    /// <summary>
    /// Creates a new iterative refinement solver.
    /// </summary>
    /// <param name="model">The trained neural network model.</param>
    /// <param name="device">The device to run inference on.</param>
    /// <param name="maxBacktracks">Maximum number of backtracks allowed (0 = no backtracking).</param>
    /// <param name="confidenceThreshold">Minimum confidence to accept a prediction (0.0 - 1.0).</param>
    public IterativeRefinementSolver(
        SudokuNetwork model, 
        Device device, 
        int maxBacktracks = 100,
        double confidenceThreshold = 0.01)
    {
        _model = model;
        _device = device;
        _maxBacktracks = maxBacktracks;
        _confidenceThreshold = confidenceThreshold;
    }

    /// <summary>
    /// Solves a puzzle using iterative refinement with optional backtracking.
    /// </summary>
    public SudokuGrid Solve(SudokuGrid puzzle)
    {
        _model.eval();

        using var _ = no_grad();

        if (_maxBacktracks > 0)
        {
            return SolveWithBacktracking(puzzle);
        }
        else
        {
            return SolveGreedy(puzzle);
        }
    }

    /// <summary>
    /// Greedy solving: fill highest-confidence valid cell repeatedly.
    /// No backtracking - if stuck, returns partial solution.
    /// </summary>
    private SudokuGrid SolveGreedy(SudokuGrid puzzle)
    {
        var current = puzzle;

        while (!current.IsComplete)
        {
            var (row, col, value, confidence) = GetBestMove(current);

            if (row < 0 || confidence < _confidenceThreshold)
            {
                // No valid move found
                break;
            }

            current = current.WithCell(row, col, value);
        }

        return current;
    }

    /// <summary>
    /// Solving with backtracking: if stuck, undo last move and try alternatives.
    /// </summary>
    private SudokuGrid SolveWithBacktracking(SudokuGrid puzzle)
    {
        var stack = new Stack<(SudokuGrid Grid, List<(int Row, int Col, int Value, float Confidence)> Alternatives)>();
        var current = puzzle;
        var backtracks = 0;

        while (!current.IsComplete && backtracks < _maxBacktracks)
        {
            var moves = GetAllValidMoves(current);

            if (moves.Count == 0)
            {
                // No valid moves - need to backtrack
                if (stack.Count == 0)
                {
                    // Cannot backtrack further
                    break;
                }

                var (prevGrid, alternatives) = stack.Pop();
                backtracks++;

                if (alternatives.Count > 0)
                {
                    // Try the next best alternative
                    var alt = alternatives[0];
                    alternatives.RemoveAt(0);
                    current = prevGrid.WithCell(alt.Row, alt.Col, alt.Value);
                    stack.Push((prevGrid, alternatives));
                }
                else
                {
                    // No alternatives at this level, continue backtracking
                    current = prevGrid;
                }

                continue;
            }

            // Sort by confidence (descending)
            moves.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));

            // Take the best move
            var best = moves[0];
            moves.RemoveAt(0);

            // Store alternatives for potential backtracking
            stack.Push((current, moves));

            current = current.WithCell(best.Row, best.Col, best.Value);
        }

        return current;
    }

    /// <summary>
    /// Gets the single best valid move for the current grid state.
    /// </summary>
    private (int Row, int Col, int Value, float Confidence) GetBestMove(SudokuGrid grid)
    {
        var probs = GetProbabilities(grid);

        var bestRow = -1;
        var bestCol = -1;
        var bestValue = 0;
        var bestConfidence = 0.0f;

        for (var row = 0; row < 9; row++)
        {
            for (var col = 0; col < 9; col++)
            {
                if (grid[row, col] != 0)
                {
                    continue;
                }

                var validDigits = GetValidDigits(grid, row, col);

                for (var digit = 1; digit <= 9; digit++)
                {
                    if (!validDigits.Contains(digit))
                    {
                        continue;
                    }

                    var confidence = probs[digit - 1, row, col];
                    if (confidence > bestConfidence)
                    {
                        bestConfidence = confidence;
                        bestRow = row;
                        bestCol = col;
                        bestValue = digit;
                    }
                }
            }
        }

        return (bestRow, bestCol, bestValue, bestConfidence);
    }

    /// <summary>
    /// Gets all valid moves sorted by confidence.
    /// Returns list of (Row, Col, Value, Confidence) tuples.
    /// </summary>
    private List<(int Row, int Col, int Value, float Confidence)> GetAllValidMoves(SudokuGrid grid)
    {
        var probs = GetProbabilities(grid);
        var moves = new List<(int Row, int Col, int Value, float Confidence)>();

        // Find cell with minimum remaining values (MRV heuristic)
        var minValidCount = 10;
        var mrvRow = -1;
        var mrvCol = -1;
        HashSet<int>? mrvValidDigits = null;

        for (var row = 0; row < 9; row++)
        {
            for (var col = 0; col < 9; col++)
            {
                if (grid[row, col] != 0)
                {
                    continue;
                }

                var validDigits = GetValidDigits(grid, row, col);
                if (validDigits.Count < minValidCount)
                {
                    minValidCount = validDigits.Count;
                    mrvRow = row;
                    mrvCol = col;
                    mrvValidDigits = validDigits;
                }
            }
        }

        if (mrvRow < 0 || mrvValidDigits == null)
        {
            return moves;
        }

        // Return valid moves for the MRV cell, sorted by probability
        foreach (var digit in mrvValidDigits)
        {
            var confidence = probs[digit - 1, mrvRow, mrvCol];
            if (confidence >= _confidenceThreshold)
            {
                moves.Add((mrvRow, mrvCol, digit, confidence));
            }
        }

        // Sort by confidence descending
        moves.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));

        return moves;
    }

    /// <summary>
    /// Gets prediction probabilities from the neural network.
    /// </summary>
    private float[,,] GetProbabilities(SudokuGrid grid)
    {
        var input = grid.ToTensor(_device);
        var output = _model.ForwardWithProbabilities(input);
        var probs = output.cpu().squeeze(0); // [9, 9, 9] - [digit, row, col]

        var result = new float[9, 9, 9];
        var flatProbs = probs.data<float>().ToArray();

        for (var d = 0; d < 9; d++)
        {
            for (var r = 0; r < 9; r++)
            {
                for (var c = 0; c < 9; c++)
                {
                    result[d, r, c] = flatProbs[d * 81 + r * 9 + c];
                }
            }
        }

        input.Dispose();
        output.Dispose();
        probs.Dispose();

        return result;
    }

    /// <summary>
    /// Gets valid digits for a cell based on Sudoku constraints.
    /// </summary>
    private static HashSet<int> GetValidDigits(SudokuGrid grid, int row, int col)
    {
        var valid = new HashSet<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9 };

        // Remove digits in same row
        for (var c = 0; c < 9; c++)
        {
            valid.Remove(grid[row, c]);
        }

        // Remove digits in same column
        for (var r = 0; r < 9; r++)
        {
            valid.Remove(grid[r, col]);
        }

        // Remove digits in same box
        var boxRow = (row / 3) * 3;
        var boxCol = (col / 3) * 3;
        for (var r = boxRow; r < boxRow + 3; r++)
        {
            for (var c = boxCol; c < boxCol + 3; c++)
            {
                valid.Remove(grid[r, c]);
            }
        }

        return valid;
    }

    /// <summary>
    /// Solves a puzzle and returns intermediate steps for visualization.
    /// </summary>
    public IEnumerable<(SudokuGrid Grid, int Row, int Col, int Value, float Confidence)> SolveWithSteps(SudokuGrid puzzle)
    {
        _model.eval();

        using var _ = no_grad();

        var current = puzzle;

        while (!current.IsComplete)
        {
            var (row, col, value, confidence) = GetBestMove(current);

            if (row < 0 || confidence < _confidenceThreshold)
            {
                break;
            }

            current = current.WithCell(row, col, value);
            yield return (current, row, col, value, confidence);
        }
    }
}
