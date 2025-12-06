namespace Decima.Solvers;

using Decima.Data;
using Decima.Models;

using Spectre.Console;

/// <summary>
/// Island model genetic algorithm with multiple sub-populations evolving in parallel.
/// Periodic migration exchanges best individuals between islands.
/// </summary>
public sealed class IslandModel : IDisposable
{
    private readonly GeneticSolverOptions _options;
    private readonly GpuFitnessEvaluator _fitnessEvaluator;
    private readonly SudokuTrainer? _mlModel;
    private bool _disposed;

    /// <summary>
    /// Creates a new island model solver.
    /// </summary>
    /// <param name="options">GA configuration options.</param>
    /// <param name="mlModelPath">Optional path to ML model for hybrid mode.</param>
    public IslandModel(GeneticSolverOptions? options = null, string? mlModelPath = null)
    {
        _options = options ?? new GeneticSolverOptions();
        _fitnessEvaluator = new GpuFitnessEvaluator(_options.UseGpu);

        if (!string.IsNullOrEmpty(mlModelPath) && File.Exists(mlModelPath))
        {
            _mlModel = new SudokuTrainer(inferenceOnly: true);
            _mlModel.Verbose = false;
            _mlModel.Load(mlModelPath);
        }
    }

    /// <summary>
    /// Gets whether hybrid mode is enabled.
    /// </summary>
    public bool IsHybridMode => _mlModel != null;

    /// <summary>
    /// Solves the puzzle using the island model.
    /// </summary>
    public GeneticSolverResult Solve(SudokuGrid puzzle, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        // Initialize islands
        var islands = InitializeIslands(puzzle);

        // Evaluate all islands
        foreach (var island in islands)
        {
            _fitnessEvaluator.EvaluatePopulation(island);
        }

        var globalBestFitness = islands.SelectMany(i => i).Min(c => c.Fitness ?? int.MaxValue);
        var totalGenerations = 0;

        if (_options.Verbose)
        {
            AnsiConsole.MarkupLine($"[dim]Island Model:[/] {_options.IslandCount} islands, {_options.PopulationPerIsland} each");
            AnsiConsole.MarkupLine($"[dim]Initial best fitness:[/] {globalBestFitness}");
            AnsiConsole.MarkupLine($"[dim]Device:[/] {_fitnessEvaluator.Device}");
            if (IsHybridMode)
            {
                AnsiConsole.MarkupLine("[dim]Mode:[/] Hybrid (ML + GA)");
            }
        }

        // Evolution loop
        for (var generation = 0; generation < _options.MaxGenerations; generation++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            totalGenerations = generation + 1;

            // Check for solution across all islands
            var solution = islands.SelectMany(i => i).FirstOrDefault(c => c.IsSolution);
            if (solution != null)
            {
                if (_options.Verbose)
                {
                    AnsiConsole.MarkupLine($"[green]Solution found at generation {generation}![/]");
                }

                return new GeneticSolverResult
                {
                    Solution = solution.ToGrid(),
                    Fitness = 0,
                    IsSolved = true,
                    Generations = totalGenerations,
                    ElapsedTime = DateTime.UtcNow - startTime,
                    PopulationSize = _options.PopulationSize
                };
            }

            // Evolve islands in parallel
            Parallel.For(0, _options.IslandCount, new ParallelOptions
            {
                MaxDegreeOfParallelism = _options.EffectiveParallelism
            }, i =>
            {
                islands[i] = EvolveIsland(islands[i], puzzle);
            });

            // Evaluate all islands
            foreach (var island in islands)
            {
                _fitnessEvaluator.EvaluatePopulation(island);
            }

            // Migration
            if ((generation + 1) % _options.MigrationInterval == 0)
            {
                PerformMigration(islands);

                if (_options.Verbose)
                {
                    AnsiConsole.MarkupLine($"[dim]Gen {generation}: Migration performed[/]");
                }
            }

            // Track progress
            var currentBest = islands.SelectMany(i => i).Min(c => c.Fitness ?? int.MaxValue);
            if (currentBest < globalBestFitness)
            {
                globalBestFitness = currentBest;

                if (_options.Verbose && generation % 20 == 0)
                {
                    AnsiConsole.MarkupLine($"[dim]Gen {generation}:[/] fitness = {globalBestFitness}");
                }
            }
        }

        // Get best result across all islands
        var best = islands.SelectMany(i => i).OrderBy(c => c.Fitness ?? int.MaxValue).First();

        return new GeneticSolverResult
        {
            Solution = best.ToGrid(),
            Fitness = best.Fitness ?? int.MaxValue,
            IsSolved = best.IsSolution,
            Generations = totalGenerations,
            ElapsedTime = DateTime.UtcNow - startTime,
            PopulationSize = _options.PopulationSize
        };
    }

    /// <summary>
    /// Solves the puzzle with live progress display.
    /// </summary>
    public GeneticSolverResult SolveWithProgress(SudokuGrid puzzle, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        GeneticSolverResult? result = null;

        AnsiConsole.Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn())
            .Start(ctx =>
            {
                var tasks = new ProgressTask[_options.IslandCount];
                for (var i = 0; i < _options.IslandCount; i++)
                {
                    tasks[i] = ctx.AddTask($"[cyan]Island {i + 1}[/]", maxValue: _options.MaxGenerations);
                }

                // Initialize islands
                var islands = InitializeIslands(puzzle);
                foreach (var island in islands)
                {
                    _fitnessEvaluator.EvaluatePopulation(island);
                }

                var globalBestFitness = islands.SelectMany(i => i).Min(c => c.Fitness ?? int.MaxValue);

                for (var generation = 0; generation < _options.MaxGenerations; generation++)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    // Check for solution
                    var solution = islands.SelectMany(i => i).FirstOrDefault(c => c.IsSolution);
                    if (solution != null)
                    {
                        for (var t = 0; t < tasks.Length; t++)
                        {
                            tasks[t].Value = _options.MaxGenerations;
                        }

                        result = new GeneticSolverResult
                        {
                            Solution = solution.ToGrid(),
                            Fitness = 0,
                            IsSolved = true,
                            Generations = generation + 1,
                            ElapsedTime = DateTime.UtcNow - startTime,
                            PopulationSize = _options.PopulationSize
                        };
                        return;
                    }

                    // Evolve islands in parallel
                    Parallel.For(0, _options.IslandCount, new ParallelOptions
                    {
                        MaxDegreeOfParallelism = _options.EffectiveParallelism
                    }, i =>
                    {
                        islands[i] = EvolveIsland(islands[i], puzzle);
                    });

                    // Evaluate all islands
                    foreach (var island in islands)
                    {
                        _fitnessEvaluator.EvaluatePopulation(island);
                    }

                    // Migration
                    if ((generation + 1) % _options.MigrationInterval == 0)
                    {
                        PerformMigration(islands);
                    }

                    // Update progress
                    var currentBest = islands.SelectMany(i => i).Min(c => c.Fitness ?? int.MaxValue);
                    if (currentBest < globalBestFitness)
                    {
                        globalBestFitness = currentBest;
                    }

                    for (var i = 0; i < _options.IslandCount; i++)
                    {
                        var islandBest = islands[i].Min(c => c.Fitness ?? int.MaxValue);
                        tasks[i].Value = generation + 1;
                        tasks[i].Description = $"[cyan]Island {i + 1}[/] [dim]f={islandBest}[/]";
                    }
                }

                // Get best result
                var best = islands.SelectMany(i => i).OrderBy(c => c.Fitness ?? int.MaxValue).First();
                result = new GeneticSolverResult
                {
                    Solution = best.ToGrid(),
                    Fitness = best.Fitness ?? int.MaxValue,
                    IsSolved = best.IsSolution,
                    Generations = _options.MaxGenerations,
                    ElapsedTime = DateTime.UtcNow - startTime,
                    PopulationSize = _options.PopulationSize
                };
            });

        return result!;
    }

    /// <summary>
    /// Initializes multiple islands (sub-populations).
    /// </summary>
    private List<List<Chromosome>> InitializeIslands(SudokuGrid puzzle)
    {
        var islands = new List<List<Chromosome>>(_options.IslandCount);
        var popPerIsland = _options.PopulationPerIsland;

        // If hybrid mode, seed first island from ML
        var mlSeedCount = 0;
        SudokuGrid? mlPrediction = null;

        if (_mlModel != null)
        {
            mlPrediction = _mlModel.Solve(puzzle);
            mlSeedCount = popPerIsland / 5; // 20% of first island from ML

            if (_options.Verbose)
            {
                AnsiConsole.MarkupLine($"[dim]Seeding {mlSeedCount} individuals from ML prediction[/]");
            }
        }

        for (var i = 0; i < _options.IslandCount; i++)
        {
            var island = new List<Chromosome>(popPerIsland);

            // First island gets ML seeds in hybrid mode
            if (i == 0 && mlPrediction.HasValue)
            {
                var random = new Random();
                for (var j = 0; j < mlSeedCount; j++)
                {
                    var chromosome = Chromosome.FromPrediction(puzzle, mlPrediction.Value);

                    // Add mutations for diversity (except first)
                    if (j > 0)
                    {
                        for (var m = 0; m < random.Next(1, 5); m++)
                        {
                            chromosome = GeneticOperators.SwapMutation(chromosome, puzzle);
                        }
                    }

                    island.Add(chromosome);
                }

                // Fill rest with random
                var remaining = GeneticOperators.InitializePopulation(puzzle, popPerIsland - mlSeedCount);
                island.AddRange(remaining);
            }
            else
            {
                island = GeneticOperators.InitializePopulation(puzzle, popPerIsland);
            }

            islands.Add(island);
        }

        return islands;
    }

    /// <summary>
    /// Evolves a single island for one generation.
    /// </summary>
    private List<Chromosome> EvolveIsland(List<Chromosome> island, SudokuGrid puzzle)
    {
        var newIsland = new List<Chromosome>(_options.PopulationPerIsland);

        // Elitism
        var eliteCount = Math.Max(1, _options.EliteCount / _options.IslandCount);
        var elites = GeneticOperators.GetElites(island, eliteCount);
        newIsland.AddRange(elites);

        // Create offspring
        var random = new Random();
        while (newIsland.Count < _options.PopulationPerIsland)
        {
            var parent1 = GeneticOperators.TournamentSelect(island, _options.TournamentSize);
            var parent2 = GeneticOperators.TournamentSelect(island, _options.TournamentSize);

            Chromosome child1, child2;

            // Crossover
            if (random.NextDouble() < _options.CrossoverRate)
            {
                if (random.NextDouble() < 0.5)
                {
                    (child1, child2) = GeneticOperators.RowCrossover(parent1, parent2, puzzle);
                }
                else
                {
                    (child1, child2) = GeneticOperators.BoxCrossover(parent1, parent2, puzzle);
                }
            }
            else
            {
                child1 = parent1.Copy();
                child2 = parent2.Copy();
            }

            // Mutation
            if (random.NextDouble() < _options.MutationRate)
            {
                child1 = random.NextDouble() < 0.5
                    ? GeneticOperators.SmartMutation(child1, puzzle)
                    : GeneticOperators.SwapMutation(child1, puzzle);
            }

            if (random.NextDouble() < _options.MutationRate)
            {
                child2 = random.NextDouble() < 0.5
                    ? GeneticOperators.SmartMutation(child2, puzzle)
                    : GeneticOperators.SwapMutation(child2, puzzle);
            }

            newIsland.Add(child1);
            if (newIsland.Count < _options.PopulationPerIsland)
            {
                newIsland.Add(child2);
            }
        }

        return newIsland;
    }

    /// <summary>
    /// Performs migration between islands (ring topology).
    /// Best individuals from each island migrate to the next.
    /// </summary>
    private void PerformMigration(List<List<Chromosome>> islands)
    {
        var migrantCount = (int)(_options.PopulationPerIsland * _options.MigrationRate);
        if (migrantCount < 1) migrantCount = 1;

        // Collect migrants from each island (best individuals)
        var migrants = new List<List<Chromosome>>();
        foreach (var island in islands)
        {
            var islandMigrants = island
                .OrderBy(c => c.Fitness ?? int.MaxValue)
                .Take(migrantCount)
                .Select(c => c.Copy())
                .ToList();
            migrants.Add(islandMigrants);
        }

        // Ring migration: island i receives from island (i-1)
        for (var i = 0; i < islands.Count; i++)
        {
            var sourceIdx = (i - 1 + islands.Count) % islands.Count;
            var incomingMigrants = migrants[sourceIdx];

            // Replace worst individuals with migrants
            var sorted = islands[i].OrderByDescending(c => c.Fitness ?? int.MaxValue).ToList();
            for (var j = 0; j < migrantCount && j < sorted.Count && j < incomingMigrants.Count; j++)
            {
                var idx = islands[i].IndexOf(sorted[j]);
                if (idx >= 0)
                {
                    islands[i][idx] = incomingMigrants[j];
                }
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _fitnessEvaluator.Dispose();
            _mlModel?.Dispose();
            _disposed = true;
        }
    }
}
