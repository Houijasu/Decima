namespace Decima.Solvers;

using Decima.Data;
using Decima.Models;

using Spectre.Console;

/// <summary>
/// Main genetic algorithm solver for Sudoku puzzles.
/// Supports both pure GA and hybrid ML+GA modes.
/// </summary>
public sealed class GeneticSolver : IDisposable
{
    private readonly GeneticSolverOptions _options;
    private readonly GpuFitnessEvaluator _fitnessEvaluator;
    private readonly SudokuTrainer? _mlModel;
    private bool _disposed;

    /// <summary>
    /// Creates a new genetic solver with the specified options.
    /// </summary>
    /// <param name="options">GA configuration options.</param>
    /// <param name="mlModelPath">Optional path to ML model for hybrid mode.</param>
    public GeneticSolver(GeneticSolverOptions? options = null, string? mlModelPath = null)
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
    /// Gets the current options.
    /// </summary>
    public GeneticSolverOptions Options => _options;

    /// <summary>
    /// Gets whether hybrid mode is enabled.
    /// </summary>
    public bool IsHybridMode => _mlModel != null;

    /// <summary>
    /// Solves the puzzle using the genetic algorithm.
    /// </summary>
    /// <param name="puzzle">The puzzle to solve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The best solution found and statistics.</returns>
    public GeneticSolverResult Solve(SudokuGrid puzzle, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        // Initialize population
        var population = InitializePopulation(puzzle);

        // Initial evaluation
        _fitnessEvaluator.EvaluatePopulation(population);

        var bestFitness = population.Min(c => c.Fitness ?? int.MaxValue);
        var generationsWithoutImprovement = 0;
        var totalGenerations = 0;

        Chromosome? bestSolution = null;

        if (_options.Verbose)
        {
            AnsiConsole.MarkupLine($"[dim]Initial best fitness:[/] {bestFitness}");
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

            // Check for solution
            bestSolution = population.FirstOrDefault(c => c.IsSolution);
            if (bestSolution != null)
            {
                if (_options.Verbose)
                {
                    AnsiConsole.MarkupLine($"[green]Solution found at generation {generation}![/]");
                }
                break;
            }

            // Evolve population
            population = EvolveGeneration(population, puzzle);

            // Evaluate new population
            _fitnessEvaluator.EvaluatePopulation(population);

            // Track progress
            var currentBest = population.Min(c => c.Fitness ?? int.MaxValue);
            if (currentBest < bestFitness)
            {
                bestFitness = currentBest;
                generationsWithoutImprovement = 0;

                if (_options.Verbose && generation % 10 == 0)
                {
                    AnsiConsole.MarkupLine($"[dim]Gen {generation}:[/] fitness = {bestFitness}");
                }
            }
            else
            {
                generationsWithoutImprovement++;
            }

            // Adaptive mutation rate increase if stuck
            if (generationsWithoutImprovement > 50)
            {
                // Inject fresh random individuals
                InjectDiversity(population, puzzle, 0.1);
                _fitnessEvaluator.EvaluatePopulation(population);
                generationsWithoutImprovement = 0;

                if (_options.Verbose)
                {
                    AnsiConsole.MarkupLine("[dim]Injecting diversity...[/]");
                }
            }
        }

        // Get best result
        bestSolution ??= population.OrderBy(c => c.Fitness ?? int.MaxValue).First();

        var elapsed = DateTime.UtcNow - startTime;

        return new GeneticSolverResult
        {
            Solution = bestSolution.ToGrid(),
            Fitness = bestSolution.Fitness ?? int.MaxValue,
            IsSolved = bestSolution.IsSolution,
            Generations = totalGenerations,
            ElapsedTime = elapsed,
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
                var task = ctx.AddTask("[cyan]Evolving...[/]", maxValue: _options.MaxGenerations);

                // Initialize population
                var population = InitializePopulation(puzzle);
                _fitnessEvaluator.EvaluatePopulation(population);

                var bestFitness = population.Min(c => c.Fitness ?? int.MaxValue);
                var generationsWithoutImprovement = 0;

                for (var generation = 0; generation < _options.MaxGenerations; generation++)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    // Check for solution
                    var solution = population.FirstOrDefault(c => c.IsSolution);
                    if (solution != null)
                    {
                        task.Value = _options.MaxGenerations;
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

                    // Evolve
                    population = EvolveGeneration(population, puzzle);
                    _fitnessEvaluator.EvaluatePopulation(population);

                    // Track progress
                    var currentBest = population.Min(c => c.Fitness ?? int.MaxValue);
                    if (currentBest < bestFitness)
                    {
                        bestFitness = currentBest;
                        generationsWithoutImprovement = 0;
                    }
                    else
                    {
                        generationsWithoutImprovement++;
                    }

                    // Diversity injection
                    if (generationsWithoutImprovement > 50)
                    {
                        InjectDiversity(population, puzzle, 0.1);
                        _fitnessEvaluator.EvaluatePopulation(population);
                        generationsWithoutImprovement = 0;
                    }

                    task.Value = generation + 1;
                    task.Description = $"[cyan]Gen {generation + 1}[/] [dim]fitness={bestFitness}[/]";
                }

                // Get best result
                var best = population.OrderBy(c => c.Fitness ?? int.MaxValue).First();
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
    /// Initializes the population, optionally seeding from ML predictions.
    /// </summary>
    private List<Chromosome> InitializePopulation(SudokuGrid puzzle)
    {
        var population = new List<Chromosome>(_options.PopulationSize);

        if (_mlModel != null)
        {
            // Hybrid mode: seed some individuals from ML predictions
            var mlSeeded = (int)(_options.PopulationSize * 0.2); // 20% from ML

            // Get ML prediction
            var mlPrediction = _mlModel.Solve(puzzle);

            // Create variations of ML prediction
            var random = new Random();
            for (var i = 0; i < mlSeeded; i++)
            {
                var chromosome = Chromosome.FromPrediction(puzzle, mlPrediction);

                // Add some random mutations to create diversity
                if (i > 0)
                {
                    for (var m = 0; m < random.Next(1, 5); m++)
                    {
                        chromosome = GeneticOperators.SwapMutation(chromosome, puzzle);
                    }
                }

                population.Add(chromosome);
            }

            // Fill rest with random individuals
            var remaining = GeneticOperators.InitializePopulation(puzzle, _options.PopulationSize - mlSeeded);
            population.AddRange(remaining);

            if (_options.Verbose)
            {
                AnsiConsole.MarkupLine($"[dim]Seeded {mlSeeded} individuals from ML prediction[/]");
            }
        }
        else
        {
            // Pure GA: all random initialization
            population = GeneticOperators.InitializePopulation(puzzle, _options.PopulationSize);
        }

        return population;
    }

    /// <summary>
    /// Evolves a single generation.
    /// </summary>
    private List<Chromosome> EvolveGeneration(List<Chromosome> population, SudokuGrid puzzle)
    {
        var newPopulation = new List<Chromosome>(_options.PopulationSize);

        // Elitism: preserve best individuals
        var elites = GeneticOperators.GetElites(population, _options.EliteCount);
        newPopulation.AddRange(elites);

        // Create offspring
        var random = new Random();
        var offspring = new Chromosome[_options.PopulationSize - _options.EliteCount];

        Parallel.For(0, offspring.Length / 2, new ParallelOptions
        {
            MaxDegreeOfParallelism = _options.EffectiveParallelism
        }, i =>
        {
            var parent1 = GeneticOperators.TournamentSelect(population, _options.TournamentSize);
            var parent2 = GeneticOperators.TournamentSelect(population, _options.TournamentSize);

            Chromosome child1, child2;

            // Crossover
            if (random.NextDouble() < _options.CrossoverRate)
            {
                // Alternate between row and box crossover
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
                // Use smart mutation 50% of the time
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

            var idx = i * 2;
            offspring[idx] = child1;
            if (idx + 1 < offspring.Length)
            {
                offspring[idx + 1] = child2;
            }
        });

        newPopulation.AddRange(offspring.Where(c => c != null));

        // Ensure population size is correct
        while (newPopulation.Count < _options.PopulationSize)
        {
            newPopulation.Add(Chromosome.FromPuzzle(puzzle, random));
        }

        return newPopulation;
    }

    /// <summary>
    /// Injects fresh random individuals to increase diversity.
    /// </summary>
    private void InjectDiversity(List<Chromosome> population, SudokuGrid puzzle, double fraction)
    {
        var count = (int)(population.Count * fraction);
        var random = new Random();

        // Replace worst individuals
        var sorted = population.OrderByDescending(c => c.Fitness ?? int.MaxValue).ToList();

        for (var i = 0; i < count && i < sorted.Count; i++)
        {
            var idx = population.IndexOf(sorted[i]);
            if (idx >= 0)
            {
                population[idx] = Chromosome.FromPuzzle(puzzle, random);
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

/// <summary>
/// Result of a genetic algorithm solve operation.
/// </summary>
public sealed record GeneticSolverResult
{
    /// <summary>
    /// The best solution found.
    /// </summary>
    public required SudokuGrid Solution { get; init; }

    /// <summary>
    /// Fitness score of the solution (0 = solved).
    /// </summary>
    public required int Fitness { get; init; }

    /// <summary>
    /// Whether the puzzle was fully solved.
    /// </summary>
    public required bool IsSolved { get; init; }

    /// <summary>
    /// Number of generations evolved.
    /// </summary>
    public required int Generations { get; init; }

    /// <summary>
    /// Time taken to solve.
    /// </summary>
    public required TimeSpan ElapsedTime { get; init; }

    /// <summary>
    /// Population size used.
    /// </summary>
    public required int PopulationSize { get; init; }
}
