namespace Decima.Models;

using Decima.Data;

using static TorchSharp.torch;

/// <summary>
/// Beam search solver for Sudoku using the neural network.
/// Explores multiple candidates in parallel and prunes invalid ones.
/// </summary>
public sealed class BeamSearchSolver
{
    private readonly SudokuNetwork _model;
    private readonly Device _device;
    private readonly int _beamWidth;
    private readonly bool _useConstraintPropagation;

    public BeamSearchSolver(SudokuNetwork model, Device device, int beamWidth = 5, bool useConstraintPropagation = true)
    {
        _model = model;
        _device = device;
        _beamWidth = beamWidth;
        _useConstraintPropagation = useConstraintPropagation;
    }

    /// <summary>
    /// Solves a puzzle using beam search.
    /// </summary>
    public SudokuGrid Solve(SudokuGrid puzzle)
    {
        _model.eval();

        using var _ = no_grad();

        // Initialize beam with the puzzle
        var beam = new List<BeamCandidate>
        {
            new(puzzle, 0.0)
        };

        while (true)
        {
            // Check for complete solutions
            var solutions = beam.Where(c => c.Grid.IsComplete).ToList();
            if (solutions.Count > 0)
            {
                // Return the best valid solution, or best complete one if none valid
                var validSolutions = solutions.Where(c => c.Grid.IsValid()).ToList();
                if (validSolutions.Count > 0)
                {
                    return validSolutions.OrderBy(c => c.Score).First().Grid;
                }

                // No valid complete solution - return best complete one
                return solutions.OrderBy(c => c.Score).First().Grid;
            }

            // Expand all candidates
            var newCandidates = new List<BeamCandidate>();

            foreach (var candidate in beam)
            {
                var expanded = ExpandCandidate(candidate);
                newCandidates.AddRange(expanded);
            }

            if (newCandidates.Count == 0)
            {
                // No valid expansions - return best current candidate
                return beam.OrderBy(c => c.Score).First().Grid;
            }

            // Keep top candidates (lower score is better)
            beam = newCandidates
                .OrderBy(c => c.Score)
                .Take(_beamWidth)
                .ToList();
        }
    }

    /// <summary>
    /// Expands a candidate by filling the most confident cell with top-k values.
    /// </summary>
    private List<BeamCandidate> ExpandCandidate(BeamCandidate candidate)
    {
        var results = new List<BeamCandidate>();

        // Get model predictions
        var input = candidate.Grid.ToTensor(_device);
        var output = _model.ForwardWithProbabilities(input);
        var probs = output.cpu().squeeze(0); // [9, 9, 9] - [digit, row, col]

        // Find empty cell with highest confidence
        var bestRow = -1;
        var bestCol = -1;
        var bestConfidence = 0.0f;
        var bestProbs = new float[9];

        for (var row = 0; row < 9; row++)
        {
            for (var col = 0; col < 9; col++)
            {
                if (candidate.Grid[row, col] == 0)
                {
                    // Get max probability for this cell
                    var maxProb = 0.0f;
                    var cellProbs = new float[9];

                    for (var digit = 0; digit < 9; digit++)
                    {
                        var p = probs[digit, row, col].item<float>();
                        cellProbs[digit] = p;
                        if (p > maxProb) maxProb = p;
                    }

                    if (maxProb > bestConfidence)
                    {
                        bestConfidence = maxProb;
                        bestRow = row;
                        bestCol = col;
                        bestProbs = cellProbs;
                    }
                }
            }
        }

        input.Dispose();
        output.Dispose();
        probs.Dispose();

        if (bestRow < 0)
        {
            return results;
        }

        // Get valid digits for this cell (constraint propagation)
        var validDigits = _useConstraintPropagation
            ? GetValidDigits(candidate.Grid, bestRow, bestCol)
            : Enumerable.Range(1, 9).ToHashSet();

        // Create candidates for top-k valid predictions
        var topK = bestProbs
            .Select((p, i) => (Digit: i + 1, Prob: p))
            .Where(x => validDigits.Contains(x.Digit))
            .OrderByDescending(x => x.Prob)
            .Take(_beamWidth);

        foreach (var (digit, prob) in topK)
        {
            if (prob < 0.001f) continue; // Skip very low probability

            var newGrid = candidate.Grid.WithCell(bestRow, bestCol, digit);

            // Score is negative log probability (lower is better)
            var newScore = candidate.Score - Math.Log(prob + 1e-10);

            // Add penalty for constraint violations
            if (_useConstraintPropagation)
            {
                var violations = CountViolations(newGrid);
                newScore += violations * 10.0; // Heavy penalty for violations
            }

            results.Add(new BeamCandidate(newGrid, newScore));
        }

        return results;
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
    /// Counts constraint violations in a grid.
    /// </summary>
    private static int CountViolations(SudokuGrid grid)
    {
        var violations = 0;

        // Check rows
        for (var row = 0; row < 9; row++)
        {
            var seen = new HashSet<int>();
            for (var col = 0; col < 9; col++)
            {
                var val = grid[row, col];
                if (val != 0 && !seen.Add(val))
                {
                    violations++;
                }
            }
        }

        // Check columns
        for (var col = 0; col < 9; col++)
        {
            var seen = new HashSet<int>();
            for (var row = 0; row < 9; row++)
            {
                var val = grid[row, col];
                if (val != 0 && !seen.Add(val))
                {
                    violations++;
                }
            }
        }

        // Check boxes
        for (var boxRow = 0; boxRow < 3; boxRow++)
        {
            for (var boxCol = 0; boxCol < 3; boxCol++)
            {
                var seen = new HashSet<int>();
                for (var r = 0; r < 3; r++)
                {
                    for (var c = 0; c < 3; c++)
                    {
                        var val = grid[boxRow * 3 + r, boxCol * 3 + c];
                        if (val != 0 && !seen.Add(val))
                        {
                            violations++;
                        }
                    }
                }
            }
        }

        return violations;
    }

    /// <summary>
    /// Represents a candidate solution in the beam.
    /// </summary>
    private sealed record BeamCandidate(SudokuGrid Grid, double Score);
}
