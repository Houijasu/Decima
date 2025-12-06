namespace Decima.Solvers;

using Decima.Data;

/// <summary>
/// Genetic operators for selection, crossover, and mutation.
/// All operations are thread-safe using ThreadLocal random instances.
/// </summary>
public static class GeneticOperators
{
    private static readonly ThreadLocal<Random> ThreadRandom = new(() => new Random());

    private static Random Random => ThreadRandom.Value!;

    #region Selection

    /// <summary>
    /// Tournament selection: picks the best individual from a random subset.
    /// </summary>
    public static Chromosome TournamentSelect(IList<Chromosome> population, int tournamentSize)
    {
        Chromosome? best = null;

        for (var i = 0; i < tournamentSize; i++)
        {
            var candidate = population[Random.Next(population.Count)];
            if (best == null || (candidate.Fitness ?? int.MaxValue) < (best.Fitness ?? int.MaxValue))
            {
                best = candidate;
            }
        }

        return best!;
    }

    /// <summary>
    /// Selects parents for the next generation using tournament selection.
    /// </summary>
    public static List<Chromosome> SelectParents(IList<Chromosome> population, int count, int tournamentSize)
    {
        var parents = new List<Chromosome>(count);
        for (var i = 0; i < count; i++)
        {
            parents.Add(TournamentSelect(population, tournamentSize));
        }
        return parents;
    }

    #endregion

    #region Crossover

    /// <summary>
    /// Row-wise crossover: creates offspring by combining rows from two parents.
    /// Preserves row validity (each row still contains 1-9).
    /// </summary>
    public static (Chromosome Child1, Chromosome Child2) RowCrossover(
        Chromosome parent1,
        Chromosome parent2,
        SudokuGrid puzzle)
    {
        var genes1 = (int[])parent1.GetGenesArray().Clone();
        var genes2 = (int[])parent2.GetGenesArray().Clone();

        // Single-point crossover at row level
        var crossoverPoint = Random.Next(1, 9);

        for (var row = crossoverPoint; row < 9; row++)
        {
            for (var col = 0; col < 9; col++)
            {
                var idx = row * 9 + col;
                if (parent1.IsMutable(row, col))
                {
                    (genes1[idx], genes2[idx]) = (genes2[idx], genes1[idx]);
                }
            }
        }

        return (
            CreateChromosome(genes1, puzzle),
            CreateChromosome(genes2, puzzle)
        );
    }

    /// <summary>
    /// Box-wise crossover: swaps 3x3 boxes between parents.
    /// </summary>
    public static (Chromosome Child1, Chromosome Child2) BoxCrossover(
        Chromosome parent1,
        Chromosome parent2,
        SudokuGrid puzzle)
    {
        var genes1 = (int[])parent1.GetGenesArray().Clone();
        var genes2 = (int[])parent2.GetGenesArray().Clone();

        // Randomly select boxes to swap (0-8)
        var boxesToSwap = Random.Next(1, 9);

        for (var box = 0; box < 9; box++)
        {
            if ((boxesToSwap & (1 << box)) != 0)
            {
                var boxRow = (box / 3) * 3;
                var boxCol = (box % 3) * 3;

                for (var r = 0; r < 3; r++)
                {
                    for (var c = 0; c < 3; c++)
                    {
                        var row = boxRow + r;
                        var col = boxCol + c;
                        var idx = row * 9 + col;

                        if (parent1.IsMutable(row, col))
                        {
                            (genes1[idx], genes2[idx]) = (genes2[idx], genes1[idx]);
                        }
                    }
                }
            }
        }

        return (
            CreateChromosome(genes1, puzzle),
            CreateChromosome(genes2, puzzle)
        );
    }

    #endregion

    #region Mutation

    /// <summary>
    /// Swap mutation: swaps two mutable cells within the same row.
    /// Preserves row validity.
    /// </summary>
    public static Chromosome SwapMutation(Chromosome chromosome, SudokuGrid puzzle)
    {
        var row = Random.Next(9);
        var mutableCols = new List<int>();

        for (var col = 0; col < 9; col++)
        {
            if (chromosome.IsMutable(row, col))
            {
                mutableCols.Add(col);
            }
        }

        if (mutableCols.Count < 2) return chromosome;

        var idx1 = Random.Next(mutableCols.Count);
        var idx2 = Random.Next(mutableCols.Count);
        while (idx2 == idx1 && mutableCols.Count > 1)
        {
            idx2 = Random.Next(mutableCols.Count);
        }

        return chromosome.WithSwappedGenes(row, mutableCols[idx1], mutableCols[idx2]);
    }

    /// <summary>
    /// Smart mutation: mutates cells with the highest conflict contribution.
    /// </summary>
    public static Chromosome SmartMutation(Chromosome chromosome, SudokuGrid puzzle)
    {
        // Find the row with most violations
        var worstRow = -1;
        var worstViolations = -1;

        for (var row = 0; row < 9; row++)
        {
            var violations = CountRowConflicts(chromosome, row);
            if (violations > worstViolations)
            {
                worstViolations = violations;
                worstRow = row;
            }
        }

        if (worstRow < 0 || worstViolations == 0) return chromosome;

        // Find mutable cells in worst row
        var mutableCols = new List<int>();
        for (var col = 0; col < 9; col++)
        {
            if (chromosome.IsMutable(worstRow, col))
            {
                mutableCols.Add(col);
            }
        }

        if (mutableCols.Count < 2) return chromosome;

        // Swap two random mutable cells in the worst row
        var idx1 = Random.Next(mutableCols.Count);
        var idx2 = Random.Next(mutableCols.Count);
        while (idx2 == idx1)
        {
            idx2 = Random.Next(mutableCols.Count);
        }

        return chromosome.WithSwappedGenes(worstRow, mutableCols[idx1], mutableCols[idx2]);
    }

    /// <summary>
    /// Counts column/box conflicts for a specific row (not row conflicts since rows are valid by construction).
    /// </summary>
    private static int CountRowConflicts(Chromosome chromosome, int row)
    {
        var conflicts = 0;

        for (var col = 0; col < 9; col++)
        {
            var value = chromosome[row, col];

            // Check column conflicts
            for (var r = 0; r < 9; r++)
            {
                if (r != row && chromosome[r, col] == value)
                {
                    conflicts++;
                }
            }

            // Check box conflicts
            var boxRow = (row / 3) * 3;
            var boxCol = (col / 3) * 3;
            for (var r = boxRow; r < boxRow + 3; r++)
            {
                for (var c = boxCol; c < boxCol + 3; c++)
                {
                    if ((r != row || c != col) && chromosome[r, c] == value)
                    {
                        conflicts++;
                    }
                }
            }
        }

        return conflicts;
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Creates a new chromosome from genes array, using puzzle for mutability mask.
    /// </summary>
    private static Chromosome CreateChromosome(int[] genes, SudokuGrid puzzle)
    {
        // Reconstruct from genes - need to create via reflection or factory
        // For now, create a grid and use FromPrediction
        var cells = new int[9, 9];
        for (var row = 0; row < 9; row++)
        {
            for (var col = 0; col < 9; col++)
            {
                cells[row, col] = genes[row * 9 + col];
            }
        }

        var grid = SudokuGrid.FromArray(cells);
        return Chromosome.FromPrediction(puzzle, grid);
    }

    /// <summary>
    /// Preserves elite individuals from the current generation.
    /// </summary>
    public static List<Chromosome> GetElites(IList<Chromosome> population, int eliteCount)
    {
        return population
            .Where(c => c.IsEvaluated)
            .OrderBy(c => c.Fitness)
            .Take(eliteCount)
            .Select(c => c.Copy())
            .ToList();
    }

    /// <summary>
    /// Initializes a population of random chromosomes.
    /// </summary>
    public static List<Chromosome> InitializePopulation(SudokuGrid puzzle, int size)
    {
        var population = new List<Chromosome>(size);

        Parallel.For(0, size, _ =>
        {
            var chromosome = Chromosome.FromPuzzle(puzzle, Random);
            lock (population)
            {
                population.Add(chromosome);
            }
        });

        return population;
    }

    #endregion
}
