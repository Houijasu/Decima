namespace Decima.Solvers;

/// <summary>
/// Configuration options for the genetic algorithm solver.
/// </summary>
public sealed record GeneticSolverOptions
{
    /// <summary>
    /// Population size (number of chromosomes).
    /// Larger populations explore more solutions but use more memory.
    /// </summary>
    public int PopulationSize { get; init; } = 1000;

    /// <summary>
    /// Maximum number of generations before giving up.
    /// </summary>
    public int MaxGenerations { get; init; } = 1000;

    /// <summary>
    /// Base probability of mutation per chromosome (0.0 to 1.0).
    /// This will be adaptively increased when stuck.
    /// </summary>
    public double MutationRate { get; init; } = 0.1;

    /// <summary>
    /// Maximum mutation rate when adaptive mutation kicks in.
    /// </summary>
    public double MaxMutationRate { get; init; } = 0.5;

    /// <summary>
    /// Probability of crossover between parents (0.0 to 1.0).
    /// </summary>
    public double CrossoverRate { get; init; } = 0.8;

    /// <summary>
    /// Number of best individuals kept unchanged between generations.
    /// </summary>
    public int EliteCount { get; init; } = 2;

    /// <summary>
    /// Tournament size for selection (higher = more selection pressure).
    /// </summary>
    public int TournamentSize { get; init; } = 5;

    /// <summary>
    /// Number of islands for parallel evolution.
    /// Each island evolves independently with periodic migration.
    /// </summary>
    public int IslandCount { get; init; } = 4;

    /// <summary>
    /// Number of generations between island migrations.
    /// </summary>
    public int MigrationInterval { get; init; } = 50;

    /// <summary>
    /// Fraction of population exchanged during migration (0.0 to 1.0).
    /// </summary>
    public double MigrationRate { get; init; } = 0.1;

    /// <summary>
    /// Whether to use GPU acceleration for fitness evaluation.
    /// </summary>
    public bool UseGpu { get; init; } = true;

    /// <summary>
    /// Whether to show progress output during solving.
    /// </summary>
    public bool Verbose { get; init; } = true;

    /// <summary>
    /// Maximum degree of parallelism for CPU operations.
    /// -1 means use all available processors.
    /// </summary>
    public int MaxParallelism { get; init; } = -1;

    /// <summary>
    /// Number of generations without improvement before triggering restart.
    /// </summary>
    public int StagnationThreshold { get; init; } = 100;

    /// <summary>
    /// Maximum number of restarts before giving up.
    /// </summary>
    public int MaxRestarts { get; init; } = 5;

    /// <summary>
    /// Whether to use constraint propagation to pre-fill forced cells.
    /// </summary>
    public bool UseConstraintPropagation { get; init; } = true;

    /// <summary>
    /// Gets the effective parallelism level.
    /// </summary>
    public int EffectiveParallelism => MaxParallelism <= 0
        ? Environment.ProcessorCount
        : MaxParallelism;

    /// <summary>
    /// Gets the population size per island.
    /// </summary>
    public int PopulationPerIsland => PopulationSize / IslandCount;
}
