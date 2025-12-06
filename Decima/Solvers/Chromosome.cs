namespace Decima.Solvers;

using Decima.Data;

/// <summary>
/// Represents a candidate solution (chromosome) in the genetic algorithm.
/// Immutable for thread-safety in parallel operations.
/// </summary>
public sealed class Chromosome
{
    private readonly int[] _genes;
    private readonly bool[] _mutable;
    private int? _cachedFitness;

    /// <summary>
    /// The 9x9 grid values (row-major order, 81 elements).
    /// </summary>
    public ReadOnlySpan<int> Genes => _genes;

    /// <summary>
    /// Gets the cached fitness value, or null if not yet evaluated.
    /// </summary>
    public int? Fitness => _cachedFitness;

    /// <summary>
    /// Gets whether this chromosome has been evaluated.
    /// </summary>
    public bool IsEvaluated => _cachedFitness.HasValue;

    /// <summary>
    /// Gets whether this is a perfect solution (fitness = 0).
    /// </summary>
    public bool IsSolution => _cachedFitness == 0;

    private Chromosome(int[] genes, bool[] mutable)
    {
        _genes = genes;
        _mutable = mutable;
    }

    /// <summary>
    /// Creates a chromosome from a puzzle, filling empty cells randomly.
    /// Uses row-wise initialization to ensure each row contains digits 1-9.
    /// </summary>
    public static Chromosome FromPuzzle(SudokuGrid puzzle, Random random)
    {
        var genes = new int[81];
        var mutable = new bool[81];

        for (var row = 0; row < 9; row++)
        {
            // Collect given values and missing values for this row
            var given = new List<int>();
            var missing = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            var emptyIndices = new List<int>();

            for (var col = 0; col < 9; col++)
            {
                var idx = row * 9 + col;
                var value = puzzle[row, col];

                if (value != 0)
                {
                    genes[idx] = value;
                    mutable[idx] = false;
                    given.Add(value);
                    missing.Remove(value);
                }
                else
                {
                    mutable[idx] = true;
                    emptyIndices.Add(idx);
                }
            }

            // Shuffle missing values and assign to empty cells
            Shuffle(missing, random);
            for (var i = 0; i < emptyIndices.Count; i++)
            {
                genes[emptyIndices[i]] = missing[i];
            }
        }

        return new Chromosome(genes, mutable);
    }

    /// <summary>
    /// Creates a chromosome from ML predictions, keeping given cells fixed.
    /// </summary>
    public static Chromosome FromPrediction(SudokuGrid puzzle, SudokuGrid prediction)
    {
        var genes = new int[81];
        var mutable = new bool[81];

        for (var row = 0; row < 9; row++)
        {
            for (var col = 0; col < 9; col++)
            {
                var idx = row * 9 + col;
                var givenValue = puzzle[row, col];

                if (givenValue != 0)
                {
                    genes[idx] = givenValue;
                    mutable[idx] = false;
                }
                else
                {
                    genes[idx] = prediction[row, col];
                    mutable[idx] = true;
                }
            }
        }

        return new Chromosome(genes, mutable);
    }

    /// <summary>
    /// Gets the value at the specified row and column.
    /// </summary>
    public int this[int row, int col] => _genes[row * 9 + col];

    /// <summary>
    /// Gets whether the cell at the specified position is mutable.
    /// </summary>
    public bool IsMutable(int row, int col) => _mutable[row * 9 + col];

    /// <summary>
    /// Sets the fitness value (called by fitness evaluator).
    /// </summary>
    public void SetFitness(int fitness) => _cachedFitness = fitness;

    /// <summary>
    /// Creates a copy of this chromosome with a mutated gene.
    /// </summary>
    public Chromosome WithSwappedGenes(int row, int col1, int col2)
    {
        var newGenes = (int[])_genes.Clone();
        var idx1 = row * 9 + col1;
        var idx2 = row * 9 + col2;
        (newGenes[idx1], newGenes[idx2]) = (newGenes[idx2], newGenes[idx1]);
        return new Chromosome(newGenes, _mutable);
    }

    /// <summary>
    /// Creates a copy of this chromosome with a new value at the specified position.
    /// </summary>
    public Chromosome WithGene(int row, int col, int value)
    {
        var newGenes = (int[])_genes.Clone();
        newGenes[row * 9 + col] = value;
        return new Chromosome(newGenes, _mutable);
    }

    /// <summary>
    /// Creates a deep copy of this chromosome.
    /// </summary>
    public Chromosome Copy() => new((int[])_genes.Clone(), _mutable);

    /// <summary>
    /// Converts this chromosome to a SudokuGrid.
    /// </summary>
    public SudokuGrid ToGrid()
    {
        var cells = new int[9, 9];
        for (var row = 0; row < 9; row++)
        {
            for (var col = 0; col < 9; col++)
            {
                cells[row, col] = _genes[row * 9 + col];
            }
        }
        return SudokuGrid.FromArray(cells);
    }

    /// <summary>
    /// Gets the raw genes array for GPU operations.
    /// </summary>
    public int[] GetGenesArray() => _genes;

    /// <summary>
    /// Gets the mutability mask for genetic operations.
    /// </summary>
    public bool[] GetMutableMask() => _mutable;

    private static void Shuffle<T>(List<T> list, Random random)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
