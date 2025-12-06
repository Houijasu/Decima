namespace Decima.Solvers;

using Decima.Data;
using Decima.Models;

using Spectre.Console;

/// <summary>
/// Main genetic algorithm solver for Sudoku puzzles.
/// Supports both pure GA and hybrid ML+GA modes.
/// Features: adaptive mutation, restarts, constraint propagation.
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

        // Apply constraint propagation if enabled
        var workingPuzzle = puzzle;
        if (_options.UseConstraintPropagation)
        {
            workingPuzzle = ApplyConstraintPropagation(puzzle);
            if (_options.Verbose && !workingPuzzle.Equals(puzzle))
            {
                var filled = puzzle.EmptyCellCount - workingPuzzle.EmptyCellCount;
                AnsiConsole.MarkupLine($"[dim]Constraint propagation filled {filled} cells[/]");
            }
        }

        // Initialize population
        var population = InitializePopulation(workingPuzzle);

        // Initial evaluation
        _fitnessEvaluator.EvaluatePopulation(population);

        var bestFitness = population.Min(c => c.Fitness ?? int.MaxValue);
        var globalBestFitness = bestFitness;
        var generationsWithoutImprovement = 0;
        var totalGenerations = 0;
        var restartCount = 0;
        var currentMutationRate = _options.MutationRate;

        Chromosome? bestSolution = null;
        Chromosome? globalBestSolution = population.OrderBy(c => c.Fitness ?? int.MaxValue).First();

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

            // Evolve population with current mutation rate
            population = EvolveGeneration(population, workingPuzzle, currentMutationRate);

            // Evaluate new population
            _fitnessEvaluator.EvaluatePopulation(population);

            // Track progress
            var currentBest = population.Min(c => c.Fitness ?? int.MaxValue);
            var currentBestChromosome = population.First(c => c.Fitness == currentBest);

            // Track local population best
            if (currentBest < bestFitness)
            {
                bestFitness = currentBest;
                
                if (_options.Verbose && generation % 50 == 0)
                {
                    AnsiConsole.MarkupLine($"[dim]Gen {generation}:[/] fitness = {bestFitness}");
                }
            }

            // Track global best - only reset stagnation counter on GLOBAL improvement
            if (currentBest < globalBestFitness)
            {
                globalBestFitness = currentBest;
                globalBestSolution = currentBestChromosome.Copy();
                generationsWithoutImprovement = 0;
                currentMutationRate = _options.MutationRate;
            }
            else
            {
                generationsWithoutImprovement++;

                // Adaptive mutation: increase mutation rate when stuck
                if (generationsWithoutImprovement > 20)
                {
                    currentMutationRate = Math.Min(
                        _options.MaxMutationRate,
                        _options.MutationRate + (generationsWithoutImprovement - 20) * 0.01);
                }
            }

            // Diversity injection if moderately stuck
            if (generationsWithoutImprovement > 30 && generationsWithoutImprovement % 30 == 0)
            {
                InjectDiversity(population, workingPuzzle, 0.2);
                _fitnessEvaluator.EvaluatePopulation(population);

                if (_options.Verbose)
                {
                    AnsiConsole.MarkupLine($"[dim]Injecting diversity (mutation rate: {currentMutationRate:P0})...[/]");
                }
            }

            // Restart if severely stuck
            if (generationsWithoutImprovement >= _options.StagnationThreshold)
            {
                if (restartCount < _options.MaxRestarts)
                {
                    restartCount++;
                    if (_options.Verbose)
                    {
                        AnsiConsole.MarkupLine($"[yellow]Restart {restartCount}/{_options.MaxRestarts} (stuck at fitness {bestFitness})[/]");
                    }

                    // Keep best solution and reinitialize rest
                    population = InitializePopulation(workingPuzzle);
                    
                    // Seed with global best
                    if (globalBestSolution != null)
                    {
                        population[0] = globalBestSolution.Copy();
                        // Add mutations of global best
                        for (var i = 1; i < Math.Min(10, population.Count); i++)
                        {
                            var mutated = globalBestSolution.Copy();
                            for (var m = 0; m < i; m++)
                            {
                                mutated = GeneticOperators.SmartMutation(mutated, workingPuzzle);
                            }
                            population[i] = mutated;
                        }
                    }

                    _fitnessEvaluator.EvaluatePopulation(population);
                    bestFitness = population.Min(c => c.Fitness ?? int.MaxValue);
                    generationsWithoutImprovement = 0;
                    currentMutationRate = _options.MutationRate;
                }
            }
        }

        // Get best result
        bestSolution ??= globalBestSolution ?? population.OrderBy(c => c.Fitness ?? int.MaxValue).First();

        var elapsed = DateTime.UtcNow - startTime;

        return new GeneticSolverResult
        {
            Solution = bestSolution.ToGrid(),
            Fitness = bestSolution.Fitness ?? int.MaxValue,
            IsSolved = bestSolution.IsSolution,
            Generations = totalGenerations,
            ElapsedTime = elapsed,
            PopulationSize = _options.PopulationSize,
            Restarts = restartCount
        };
    }

    /// <summary>
    /// Solves the puzzle with live progress display.
    /// </summary>
    public GeneticSolverResult SolveWithProgress(SudokuGrid puzzle, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        GeneticSolverResult? result = null;

        // Apply constraint propagation if enabled
        var workingPuzzle = puzzle;
        if (_options.UseConstraintPropagation)
        {
            workingPuzzle = ApplyConstraintPropagation(puzzle);
            if (!workingPuzzle.Equals(puzzle))
            {
                var filled = puzzle.EmptyCellCount - workingPuzzle.EmptyCellCount;
                AnsiConsole.MarkupLine($"[dim]Constraint propagation filled {filled} cells[/]");
            }
        }

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
                var population = InitializePopulation(workingPuzzle);
                _fitnessEvaluator.EvaluatePopulation(population);

                var bestFitness = population.Min(c => c.Fitness ?? int.MaxValue);
                var globalBestFitness = bestFitness;
                var generationsWithoutImprovement = 0;
                var restartCount = 0;
                var currentMutationRate = _options.MutationRate;

                Chromosome? globalBestSolution = population.OrderBy(c => c.Fitness ?? int.MaxValue).First();

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
                            PopulationSize = _options.PopulationSize,
                            Restarts = restartCount
                        };
                        return;
                    }

                    // Evolve with adaptive mutation
                    population = EvolveGeneration(population, workingPuzzle, currentMutationRate);
                    _fitnessEvaluator.EvaluatePopulation(population);

                    // Track progress
                    var currentBest = population.Min(c => c.Fitness ?? int.MaxValue);
                    var currentBestChromosome = population.First(c => c.Fitness == currentBest);

                    // Track local population best
                    if (currentBest < bestFitness)
                    {
                        bestFitness = currentBest;
                    }

                    // Track global best - only reset stagnation counter on GLOBAL improvement
                    if (currentBest < globalBestFitness)
                    {
                        globalBestFitness = currentBest;
                        globalBestSolution = currentBestChromosome.Copy();
                        generationsWithoutImprovement = 0;
                        currentMutationRate = _options.MutationRate;
                    }
                    else
                    {
                        generationsWithoutImprovement++;

                        // Adaptive mutation
                        if (generationsWithoutImprovement > 20)
                        {
                            currentMutationRate = Math.Min(
                                _options.MaxMutationRate,
                                _options.MutationRate + (generationsWithoutImprovement - 20) * 0.01);
                        }
                    }

                    // Diversity injection
                    if (generationsWithoutImprovement > 30 && generationsWithoutImprovement % 30 == 0)
                    {
                        InjectDiversity(population, workingPuzzle, 0.2);
                        _fitnessEvaluator.EvaluatePopulation(population);
                    }

                    // Restart if stuck
                    if (generationsWithoutImprovement >= _options.StagnationThreshold)
                    {
                        if (restartCount < _options.MaxRestarts)
                        {
                            restartCount++;
                            task.Description = $"[yellow]Restart {restartCount}[/] [dim]fitness={bestFitness}[/]";

                            population = InitializePopulation(workingPuzzle);
                            if (globalBestSolution != null)
                            {
                                population[0] = globalBestSolution.Copy();
                                for (var i = 1; i < Math.Min(10, population.Count); i++)
                                {
                                    var mutated = globalBestSolution.Copy();
                                    for (var m = 0; m < i; m++)
                                    {
                                        mutated = GeneticOperators.SmartMutation(mutated, workingPuzzle);
                                    }
                                    population[i] = mutated;
                                }
                            }

                            _fitnessEvaluator.EvaluatePopulation(population);
                            bestFitness = population.Min(c => c.Fitness ?? int.MaxValue);
                            generationsWithoutImprovement = 0;
                            currentMutationRate = _options.MutationRate;
                        }
                    }

                    task.Value = generation + 1;
                    var restartInfo = restartCount > 0 ? $" R{restartCount}" : "";
                    task.Description = $"[cyan]Gen {generation + 1}{restartInfo}[/] [dim]fitness={globalBestFitness} mut={currentMutationRate:P0}[/]";
                }

                // Get best result
                var best = globalBestSolution ?? population.OrderBy(c => c.Fitness ?? int.MaxValue).First();
                result = new GeneticSolverResult
                {
                    Solution = best.ToGrid(),
                    Fitness = best.Fitness ?? int.MaxValue,
                    IsSolved = best.IsSolution,
                    Generations = _options.MaxGenerations,
                    ElapsedTime = DateTime.UtcNow - startTime,
                    PopulationSize = _options.PopulationSize,
                    Restarts = restartCount
                };
            });

        return result!;
    }

    /// <summary>
    /// Applies constraint propagation to fill in forced cells.
    /// Uses naked singles and hidden singles strategies.
    /// </summary>
    private static SudokuGrid ApplyConstraintPropagation(SudokuGrid puzzle)
    {
        var cells = new int[9, 9];
        for (var row = 0; row < 9; row++)
        {
            for (var col = 0; col < 9; col++)
            {
                cells[row, col] = puzzle[row, col];
            }
        }

        var changed = true;
        while (changed)
        {
            changed = false;

            for (var row = 0; row < 9; row++)
            {
                for (var col = 0; col < 9; col++)
                {
                    if (cells[row, col] != 0) continue;

                    var candidates = GetCandidates(cells, row, col);

                    // Naked single: only one candidate
                    if (candidates.Count == 1)
                    {
                        cells[row, col] = candidates.First();
                        changed = true;
                        continue;
                    }

                    // Hidden single in row
                    foreach (var digit in candidates)
                    {
                        var isHiddenSingle = true;
                        for (var c = 0; c < 9; c++)
                        {
                            if (c != col && cells[row, c] == 0)
                            {
                                var otherCandidates = GetCandidates(cells, row, c);
                                if (otherCandidates.Contains(digit))
                                {
                                    isHiddenSingle = false;
                                    break;
                                }
                            }
                        }
                        if (isHiddenSingle)
                        {
                            cells[row, col] = digit;
                            changed = true;
                            break;
                        }
                    }

                    if (cells[row, col] != 0) continue;

                    // Hidden single in column
                    foreach (var digit in candidates)
                    {
                        var isHiddenSingle = true;
                        for (var r = 0; r < 9; r++)
                        {
                            if (r != row && cells[r, col] == 0)
                            {
                                var otherCandidates = GetCandidates(cells, r, col);
                                if (otherCandidates.Contains(digit))
                                {
                                    isHiddenSingle = false;
                                    break;
                                }
                            }
                        }
                        if (isHiddenSingle)
                        {
                            cells[row, col] = digit;
                            changed = true;
                            break;
                        }
                    }

                    if (cells[row, col] != 0) continue;

                    // Hidden single in box
                    var boxRow = (row / 3) * 3;
                    var boxCol = (col / 3) * 3;
                    foreach (var digit in candidates)
                    {
                        var isHiddenSingle = true;
                        for (var r = boxRow; r < boxRow + 3 && isHiddenSingle; r++)
                        {
                            for (var c = boxCol; c < boxCol + 3; c++)
                            {
                                if ((r != row || c != col) && cells[r, c] == 0)
                                {
                                    var otherCandidates = GetCandidates(cells, r, c);
                                    if (otherCandidates.Contains(digit))
                                    {
                                        isHiddenSingle = false;
                                        break;
                                    }
                                }
                            }
                        }
                        if (isHiddenSingle)
                        {
                            cells[row, col] = digit;
                            changed = true;
                            break;
                        }
                    }
                }
            }
        }

        return SudokuGrid.FromArray(cells);
    }

    /// <summary>
    /// Gets candidate values for a cell.
    /// </summary>
    private static HashSet<int> GetCandidates(int[,] cells, int row, int col)
    {
        var candidates = new HashSet<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9 };

        // Remove values in same row
        for (var c = 0; c < 9; c++)
        {
            candidates.Remove(cells[row, c]);
        }

        // Remove values in same column
        for (var r = 0; r < 9; r++)
        {
            candidates.Remove(cells[r, col]);
        }

        // Remove values in same box
        var boxRow = (row / 3) * 3;
        var boxCol = (col / 3) * 3;
        for (var r = boxRow; r < boxRow + 3; r++)
        {
            for (var c = boxCol; c < boxCol + 3; c++)
            {
                candidates.Remove(cells[r, c]);
            }
        }

        return candidates;
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
    /// Evolves a single generation with configurable mutation rate.
    /// </summary>
    private List<Chromosome> EvolveGeneration(List<Chromosome> population, SudokuGrid puzzle, double mutationRate)
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
            var localRandom = Random.Shared;
            var parent1 = GeneticOperators.TournamentSelect(population, _options.TournamentSize);
            var parent2 = GeneticOperators.TournamentSelect(population, _options.TournamentSize);

            Chromosome child1, child2;

            // Crossover
            if (localRandom.NextDouble() < _options.CrossoverRate)
            {
                // Alternate between row and box crossover
                if (localRandom.NextDouble() < 0.5)
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

            // Mutation with adaptive rate
            if (localRandom.NextDouble() < mutationRate)
            {
                // Use smart mutation more often when stuck (higher mutation rate)
                var smartProbability = Math.Min(0.8, 0.5 + (mutationRate - _options.MutationRate) * 2);
                child1 = localRandom.NextDouble() < smartProbability
                    ? GeneticOperators.SmartMutation(child1, puzzle)
                    : GeneticOperators.SwapMutation(child1, puzzle);

                // Apply multiple mutations when mutation rate is high
                if (mutationRate > 0.3)
                {
                    var extraMutations = (int)((mutationRate - 0.3) * 10);
                    for (var m = 0; m < extraMutations; m++)
                    {
                        child1 = GeneticOperators.SmartMutation(child1, puzzle);
                    }
                }
            }

            if (localRandom.NextDouble() < mutationRate)
            {
                var smartProbability = Math.Min(0.8, 0.5 + (mutationRate - _options.MutationRate) * 2);
                child2 = localRandom.NextDouble() < smartProbability
                    ? GeneticOperators.SmartMutation(child2, puzzle)
                    : GeneticOperators.SwapMutation(child2, puzzle);

                if (mutationRate > 0.3)
                {
                    var extraMutations = (int)((mutationRate - 0.3) * 10);
                    for (var m = 0; m < extraMutations; m++)
                    {
                        child2 = GeneticOperators.SmartMutation(child2, puzzle);
                    }
                }
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

    /// <summary>
    /// Number of restarts performed.
    /// </summary>
    public int Restarts { get; init; }
}
