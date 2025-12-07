namespace Decima.Models;

using Decima.Data;

using Spectre.Console;

using TorchSharp;

using static TorchSharp.torch;

/// <summary>
/// Handles training, evaluation, and inference for the Sudoku neural network.
/// </summary>
public sealed class SudokuTrainer : IDisposable
{
    private SudokuNetwork _model;
    private optim.Optimizer _optimizer;
    private readonly Device _device;
    private readonly double _learningRate;
    private bool _disposed;

    /// <summary>
    /// Gets the device being used (CPU or CUDA).
    /// </summary>
    public Device Device => _device;

    /// <summary>
    /// Gets or sets whether to show detailed progress output.
    /// </summary>
    public bool Verbose { get; set; } = true;

    /// <summary>
    /// Creates a new trainer instance.
    /// </summary>
    /// <param name="learningRate">Learning rate for the optimizer.</param>
    /// <param name="useCuda">Whether to use CUDA if available.</param>
    public SudokuTrainer(
        double learningRate = 0.001, 
        bool inferenceOnly = false,
        int hiddenChannels = SudokuNetwork.DefaultHiddenChannels,
        int numResBlocks = SudokuNetwork.DefaultNumResBlocks)
    {
        if (!inferenceOnly && !cuda.is_available())
        {
            throw new InvalidOperationException("CUDA is not available. Training requires a GPU.");
        }

        _device = cuda.is_available() ? CUDA : CPU;
        _learningRate = learningRate;

        if (Verbose)
        {
            AnsiConsole.MarkupLine($"[dim]Using device:[/] [cyan]{_device}[/]");
        }

        _model = new SudokuNetwork(hiddenChannels, numResBlocks);
        _model.to(_device);

        _optimizer = optim.Adam(_model.parameters(), lr: learningRate);
    }

    /// <summary>
    /// Trains the model on generated Sudoku puzzles.
    /// </summary>
    public bool Train(int epochs, int batchSize, int samplesPerEpoch, int emptyCells)
    {
        // Use legacy training without curriculum
        return TrainWithCurriculum(epochs, batchSize, samplesPerEpoch, emptyCells, emptyCells, useAugmentation: false, CurriculumStrategy.Linear);
    }

    /// <summary>
    /// Trains the model with curriculum learning and data augmentation.
    /// </summary>
    /// <param name="epochs">Number of training epochs.</param>
    /// <param name="batchSize">Batch size for training.</param>
    /// <param name="samplesPerEpoch">Number of samples per epoch.</param>
    /// <param name="minEmptyCells">Minimum empty cells (easiest difficulty).</param>
    /// <param name="maxEmptyCells">Maximum empty cells (hardest difficulty).</param>
    /// <param name="useAugmentation">Whether to use data augmentation.</param>
    /// <param name="strategy">Curriculum progression strategy.</param>
    public bool TrainWithCurriculum(
        int epochs,
        int batchSize,
        int samplesPerEpoch,
        int minEmptyCells,
        int maxEmptyCells,
        bool useAugmentation = true,
        CurriculumStrategy strategy = CurriculumStrategy.Cosine)
    {
        _model.train();

        var batchesPerEpoch = samplesPerEpoch / batchSize;
        var stoppedEarly = false;

        var curriculum = new CurriculumScheduler(minEmptyCells, maxEmptyCells, epochs, strategy);

        // Cosine annealing learning rate scheduler: decays LR over epochs down to 10% of the initial rate
        var scheduler = optim.lr_scheduler.CosineAnnealingLR(_optimizer, T_max: epochs, eta_min: _learningRate * 0.1);

        AnsiConsole.MarkupLine("[dim]Press [yellow]Escape[/] to stop training and save the model.[/]");
        if (useAugmentation)
        {
            AnsiConsole.MarkupLine("[dim]Data augmentation:[/] [green]enabled[/]");
        }
        AnsiConsole.MarkupLine($"[dim]Curriculum:[/] [cyan]{minEmptyCells}[/] → [cyan]{maxEmptyCells}[/] empty cells ({strategy})");
        AnsiConsole.MarkupLine($"[dim]LR schedule:[/] Cosine annealing to {(_learningRate * 0.1):F6}");
        AnsiConsole.WriteLine();

        AnsiConsole.Progress()
            .AutoRefresh(true)
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(
            [
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn()
            ])
            .Start(ctx =>
            {
                var epochTask = ctx.AddTask("[cyan]Training[/]", maxValue: epochs);

                for (var epoch = 0; epoch < epochs; epoch++)
                {
                    // Update curriculum difficulty
                    curriculum.Update(epoch);
                    var currentEmpty = curriculum.CurrentEmptyCells;

                    var epochLoss = 0.0;
                    var epochCorrect = 0L;
                    var epochTotal = 0L;

                    var batchTask = ctx.AddTask($"[dim]Epoch {epoch + 1} (empty={currentEmpty})[/]", maxValue: batchesPerEpoch);

                    for (var batch = 0; batch < batchesPerEpoch; batch++)
                    {
                        // Check for Escape key press (only if console is available)
                        try
                        {
                            if (Console.KeyAvailable)
                            {
                                var key = Console.ReadKey(intercept: true);
                                if (key.Key == ConsoleKey.Escape)
                                {
                                    stoppedEarly = true;
                                    batchTask.StopTask();
                                    epochTask.Description = $"[yellow]Training stopped at epoch {epoch + 1}[/]";
                                    return;
                                }
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            // Console not available (e.g., running in non-interactive mode)
                        }

                        // Ramp up constraint weight from 0.1 to 0.5 over training
                        var constraintWeight = 0.1 + (0.4 * curriculum.CurrentDifficulty);
                        
                        var (loss, correct, total) = TrainBatchWithAugmentation(batchSize, curriculum, useAugmentation, constraintWeight);
                        epochLoss += loss;
                        epochCorrect += correct;
                        epochTotal += total;

                        batchTask.Increment(1);
                    }

                    // Step LR scheduler once per epoch
                    scheduler.step();

                    batchTask.StopTask();
                    ctx.Refresh();

                    var avgLoss = epochLoss / batchesPerEpoch;
                    var accuracy = (double)epochCorrect / epochTotal;

                    // Report current LR from the first param group
                    var currentLr = _optimizer.ParamGroups.First().LearningRate;

                    epochTask.Description = $"[cyan]Epoch {epoch + 1}/{epochs}[/] Loss: {avgLoss:F4} Acc: {accuracy:P1} Empty: {currentEmpty} LR: {currentLr:F6}";
                    epochTask.Increment(1);
                }
            });

        return stoppedEarly;
    }

    private (double Loss, long Correct, long Total) TrainBatch(int batchSize, int emptyCells)
    {
        using var _ = NewDisposeScope();

        // Generate batch data
        var (inputs, targets, masks) = GenerateBatchTensors(batchSize, emptyCells);

        _optimizer.zero_grad();

        // Forward pass
        var outputs = _model.forward(inputs);

        // Compute loss only on empty cells
        // outputs: [batch, 9, 9, 9], targets: [batch, 9, 9]
        var loss = ComputeMaskedLoss(outputs, targets, masks);

        // Backward pass
        loss.backward();
        _optimizer.step();

        // Calculate accuracy
        var (correct, total) = ComputeAccuracy(outputs, targets, masks);

        return (loss.item<float>(), correct, total);
    }

    private (double Loss, long Correct, long Total) TrainBatchWithAugmentation(
        int batchSize, 
        CurriculumScheduler curriculum, 
        bool useAugmentation,
        double constraintWeight = 0.1)
    {
        using var _ = NewDisposeScope();

        // Generate batch data with augmentation
        var (inputs, targets, masks) = GenerateBatchTensorsWithAugmentation(batchSize, curriculum, useAugmentation);

        _optimizer.zero_grad();

        // Forward pass
        var outputs = _model.forward(inputs);

        // Compute loss only on empty cells with focal loss and dynamic constraint weight
        var loss = ComputeMaskedLoss(outputs, targets, masks, constraintWeight);

        // Backward pass
        loss.backward();

        // Gradient clipping for stability
        nn.utils.clip_grad_norm_(_model.parameters(), 1.0);

        _optimizer.step();

        // Calculate accuracy
        var (correct, total) = ComputeAccuracy(outputs, targets, masks);

        return (loss.item<float>(), correct, total);
    }

    private (Tensor Inputs, Tensor Targets, Tensor Masks) GenerateBatchTensors(int batchSize, int emptyCells)
    {
        // Generate data on CPU first, then transfer to GPU in one operation
        var inputsData = new float[batchSize * 10 * 9 * 9];
        var targetsData = new long[batchSize * 9 * 9];
        var masksData = new float[batchSize * 9 * 9];

        Parallel.For(0, batchSize, i =>
        {
            var (puzzle, solution) = SudokuGenerator.GeneratePuzzle(emptyCells);

            var inputOffset = i * 10 * 81;
            var targetOffset = i * 81;

            for (var row = 0; row < 9; row++)
            {
                for (var col = 0; col < 9; col++)
                {
                    var cellIdx = row * 9 + col;
                    var puzzleValue = puzzle[row, col];
                    var solutionValue = solution[row, col];

                    // One-hot encode input: [batch, channel, row, col] -> flat index
                    inputsData[inputOffset + puzzleValue * 81 + cellIdx] = 1.0f;

                    // Target is the solution digit (0-indexed for loss function)
                    targetsData[targetOffset + cellIdx] = solutionValue - 1;

                    // Mask: 1 for empty cells (cells we want to predict)
                    if (puzzleValue == 0)
                    {
                        masksData[targetOffset + cellIdx] = 1.0f;
                    }
                }
            }
        });

        // Create tensors on CPU and transfer to GPU in bulk
        var inputs = tensor(inputsData, [batchSize, 10, 9, 9], dtype: float32, device: CPU).to(_device);
        var targets = tensor(targetsData, [batchSize, 9, 9], dtype: int64, device: CPU).to(_device);
        var masks = tensor(masksData, [batchSize, 9, 9], dtype: float32, device: CPU).to(_device);

        return (inputs, targets, masks);
    }

    private (Tensor Inputs, Tensor Targets, Tensor Masks) GenerateBatchTensorsWithAugmentation(
        int batchSize, CurriculumScheduler curriculum, bool useAugmentation)
    {
        // Generate data on CPU first, then transfer to GPU in one operation
        var inputsData = new float[batchSize * 10 * 9 * 9];
        var targetsData = new long[batchSize * 9 * 9];
        var masksData = new float[batchSize * 9 * 9];

        Parallel.For(0, batchSize, i =>
        {
            // Get empty cells with some variance for diversity
            var emptyCells = curriculum.GetRandomEmptyCells(3);

            var (puzzle, solution) = SudokuGenerator.GeneratePuzzle(emptyCells);

            // Apply data augmentation
            if (useAugmentation)
            {
                (puzzle, solution) = SudokuAugmentation.Augment(puzzle, solution);
            }

            var inputOffset = i * 10 * 81;
            var targetOffset = i * 81;

            for (var row = 0; row < 9; row++)
            {
                for (var col = 0; col < 9; col++)
                {
                    var cellIdx = row * 9 + col;
                    var puzzleValue = puzzle[row, col];
                    var solutionValue = solution[row, col];

                    // One-hot encode input: [batch, channel, row, col] -> flat index
                    inputsData[inputOffset + puzzleValue * 81 + cellIdx] = 1.0f;

                    // Target is the solution digit (0-indexed for loss function)
                    targetsData[targetOffset + cellIdx] = solutionValue - 1;

                    // Mask: 1 for empty cells (cells we want to predict)
                    if (puzzleValue == 0)
                    {
                        masksData[targetOffset + cellIdx] = 1.0f;
                    }
                }
            }
        });

        // Create tensors on CPU and transfer to GPU in bulk
        var inputs = tensor(inputsData, [batchSize, 10, 9, 9], dtype: float32, device: CPU).to(_device);
        var targets = tensor(targetsData, [batchSize, 9, 9], dtype: int64, device: CPU).to(_device);
        var masks = tensor(masksData, [batchSize, 9, 9], dtype: float32, device: CPU).to(_device);

        return (inputs, targets, masks);
    }

    private static Tensor ComputeMaskedLoss(Tensor outputs, Tensor targets, Tensor masks, double constraintWeight = 0.1)
    {
        // outputs: [batch, 9, 9, 9] - logits for each digit at each cell
        // targets: [batch, 9, 9] - correct digit index (0-8)
        // masks: [batch, 9, 9] - 1 for cells to predict, 0 for given cells

        // Reshape for loss calculation: [batch*81, 9] and [batch*81]
        var outputsFlat = outputs.permute(0, 2, 3, 1).reshape([-1, 9]);
        var targetsFlat = targets.reshape([-1]);
        var masksFlat = masks.reshape([-1]);

        // Compute Focal Loss instead of standard Cross Entropy
        // FL(p_t) = -alpha * (1 - p_t)^gamma * log(p_t)
        // We use gamma=2.0 and alpha=1.0 (implicit)
        
        var logProbs = nn.functional.log_softmax(outputsFlat, dim: 1);
        var probs = logProbs.exp();
        
        // Gather probabilities of target class
        var targetProbs = probs.gather(1, targetsFlat.unsqueeze(1)).squeeze(1);
        var targetLogProbs = logProbs.gather(1, targetsFlat.unsqueeze(1)).squeeze(1);
        
        // Calculate Focal Loss term: (1 - p)^gamma * -log(p)
        var gamma = 2.0;
        var focalTerm = (1.0 - targetProbs).pow(gamma);
        var loss = focalTerm * -targetLogProbs;

        // Apply mask and compute mean
        var maskedLoss = loss * masksFlat;
        var numMasked = masksFlat.sum();

        // Focal loss mean
        var focalLoss = maskedLoss.sum() / (numMasked + 1e-8f);

        // Constraint-aware loss: penalize duplicate predictions in rows, columns, and boxes
        var constraintLoss = ComputeConstraintLoss(outputs);

        // Combine losses
        return focalLoss + constraintWeight * constraintLoss;
    }


    private static Tensor ComputeConstraintLoss(Tensor outputs)
    {
        // outputs: [batch, 9, 9, 9] - logits for each digit at each cell
        // Convert to probabilities
        var probs = nn.functional.softmax(outputs, dim: 1); // [batch, 9, 9, 9] - dim 1 is digits

        // For valid Sudoku, each digit should appear exactly once per row/col/box
        // Sum of probabilities for each digit in a row/col/box should be close to 1

        var loss = zeros(1, device: probs.device);

        // Row constraint: sum probabilities for each digit across columns
        // probs: [batch, 9, 9, 9] -> [batch, digit, row, col]
        // Sum over columns (dim 3) for each digit in each row
        var rowSums = probs.sum(dim: 3); // [batch, 9, 9] - for each digit, sum across cols in each row
        // Each digit should appear once per row, so sum should be 1
        // Penalize deviation from 1
        loss = loss + ((rowSums - 1.0f).pow(2)).mean();

        // Column constraint: sum probabilities for each digit across rows
        var colSums = probs.sum(dim: 2); // [batch, 9, 9] - for each digit, sum across rows in each col
        loss = loss + ((colSums - 1.0f).pow(2)).mean();

        // Box constraint: sum probabilities for each digit in each 3x3 box
        // Reshape to [batch, 9, 3, 3, 3, 3] then sum within boxes
        var probsReshaped = probs.reshape([-1, 9, 3, 3, 3, 3]);
        var boxSums = probsReshaped.sum(dim: [2, 4]); // [batch, 9, 3, 3] - sum within each 3x3 box
        loss = loss + ((boxSums - 1.0f).pow(2)).mean();

        return loss;
    }

    private static (long Correct, long Total) ComputeAccuracy(Tensor outputs, Tensor targets, Tensor masks)
    {
        using var _ = no_grad();

        // Get predictions
        var predictions = outputs.argmax(dim: 1); // [batch, 9, 9]

        // Compare with targets where mask is 1
        var correct = ((predictions == targets) & (masks > 0.5f)).sum().item<long>();
        var total = (masks > 0.5f).sum().item<long>();

        return (correct, total);
    }

    /// <summary>
    /// Evaluates the model on generated puzzles.
    /// </summary>
    /// <returns>Cell-level accuracy on empty cells.</returns>
    public double Evaluate(int samples, int emptyCells)
    {
        _model.eval();

        using var _ = no_grad();
        using var scope = NewDisposeScope();

        long totalCorrect = 0;
        long totalCells = 0;

        for (var i = 0; i < samples; i++)
        {
            var (puzzle, solution) = SudokuGenerator.GeneratePuzzle(emptyCells);

            var input = puzzle.ToTensor(_device);
            var output = _model.forward(input);
            var predictions = output.argmax(dim: 1).squeeze(0); // [9, 9]

            for (var row = 0; row < 9; row++)
            {
                for (var col = 0; col < 9; col++)
                {
                    if (puzzle[row, col] == 0)
                    {
                        var predicted = predictions[row, col].item<long>() + 1;
                        var actual = solution[row, col];

                        if (predicted == actual)
                        {
                            totalCorrect++;
                        }

                        totalCells++;
                    }
                }
            }

            input.Dispose();
            output.Dispose();
            predictions.Dispose();
        }

        _model.train();
        return (double)totalCorrect / totalCells;
    }

    /// <summary>
    /// Solves a Sudoku puzzle using the trained model.
    /// </summary>
    public SudokuGrid Solve(SudokuGrid puzzle)
    {
        _model.eval();

        using var _ = no_grad();

        var input = puzzle.ToTensor(_device);
        var output = _model.forward(input);
        var predictions = SudokuGrid.FromOutputTensor(output.cpu());

        // Merge predictions with original puzzle (keep given cells)
        var result = puzzle.MergeWithPredictions(predictions);

        input.Dispose();
        output.Dispose();

        _model.train();
        return result;
    }

    /// <summary>
    /// Solves a puzzle using a hybrid ML-guided backtracking approach.
    /// This guarantees 100% accuracy for solvable puzzles.
    /// </summary>
    public SudokuGrid SolveHybrid(SudokuGrid puzzle)
    {
        _model.eval();
        using var _ = no_grad();

        var input = puzzle.ToTensor(_device);
        var output = _model.forward(input);
        
        // Get probabilities: [1, 9, 9, 9] -> [9, 9, 9]
        var probsTensor = nn.functional.softmax(output, dim: 1).cpu().squeeze(0);
        
        // Convert to 3D array
        var flatProbs = probsTensor.data<float>().ToArray();
        var probabilities = new float[9, 9, 9];
        
        // Tensor layout is [digit, row, col]
        for (var d = 0; d < 9; d++)
        {
            for (var r = 0; r < 9; r++)
            {
                for (var c = 0; c < 9; c++)
                {
                    probabilities[d, r, c] = flatProbs[d * 81 + r * 9 + c];
                }
            }
        }

        input.Dispose();
        output.Dispose();
        probsTensor.Dispose();

        _model.train();

        var result = SudokuGenerator.SolveGuided(puzzle, probabilities);
        return result ?? puzzle;
    }



    /// <summary>
    /// Solves a puzzle using beam search for higher accuracy.
    /// </summary>
    /// <param name="puzzle">The puzzle to solve.</param>
    /// <param name="beamWidth">Number of candidates to explore in parallel.</param>
    public SudokuGrid SolveWithBeamSearch(SudokuGrid puzzle, int beamWidth = 5)
    {
        var solver = new BeamSearchSolver(_model, _device, beamWidth);
        return solver.Solve(puzzle);
    }

    /// <summary>
    /// Solves a puzzle iteratively, filling one cell at a time based on confidence.
    /// </summary>
    public IEnumerable<(SudokuGrid Grid, int Row, int Col, int Value, double Confidence)> SolveIteratively(SudokuGrid puzzle)
    {
        _model.eval();

        var current = puzzle.Copy();

        using var _ = no_grad();

        while (!current.IsComplete)
        {
            var input = current.ToTensor(_device);
            var output = _model.ForwardWithProbabilities(input);

            // Find empty cell with highest confidence prediction
            var bestRow = -1;
            var bestCol = -1;
            var bestValue = 0;
            var bestConfidence = 0.0;

            var probs = output.cpu().squeeze(0); // [9, 9, 9]

            for (var row = 0; row < 9; row++)
            {
                for (var col = 0; col < 9; col++)
                {
                    if (current[row, col] == 0)
                    {
                        for (var digit = 0; digit < 9; digit++)
                        {
                            var confidence = probs[digit, row, col].item<float>();
                            if (confidence > bestConfidence)
                            {
                                bestConfidence = confidence;
                                bestRow = row;
                                bestCol = col;
                                bestValue = digit + 1;
                            }
                        }
                    }
                }
            }

            input.Dispose();
            output.Dispose();
            probs.Dispose();

            if (bestRow < 0)
            {
                break;
            }

            current = current.WithCell(bestRow, bestCol, bestValue);
            yield return (current, bestRow, bestCol, bestValue, bestConfidence);
        }

        _model.train();
    }

    /// <summary>
    /// Saves the model to a file.
    /// </summary>
    public void Save(string path)
    {
        _model.save(path);
        _model.Metadata.Save(path);

        if (Verbose)
        {
            AnsiConsole.MarkupLine($"[green]Model saved to:[/] {path}");
            AnsiConsole.MarkupLine($"[dim]Metadata:[/] {_model.Metadata}");
        }
    }

    /// <summary>
    /// Loads the model from a file.
    /// </summary>
    public void Load(string path)
    {
        // Load metadata to get model architecture
        var savedMetadata = ModelMetadata.Load(path);

        if (savedMetadata.HasValue)
        {
            var meta = savedMetadata.Value;
            
            // Check if we need to recreate the model with different architecture
            if (meta.HiddenChannels != _model.HiddenChannels || meta.NumResBlocks != _model.NumResBlocks)
            {
                // Dispose old model and create new one with correct architecture
                _model.Dispose();
                _optimizer.Dispose();
                
                // Use reflection to set the new model (since _model is readonly we need to work around it)
                var newModel = new SudokuNetwork(meta.HiddenChannels, meta.NumResBlocks);
                
                // Load weights into new model
                newModel.load(path);
                newModel.to(_device);
                
                // Swap in the new model
                _model = newModel;
                _optimizer = optim.Adam(_model.parameters(), lr: _learningRate);
                
                if (Verbose)
                {
                    AnsiConsole.MarkupLine($"[dim]Model metadata:[/] {meta}");
                    AnsiConsole.MarkupLine($"[dim]Recreated model with architecture:[/] hidden={meta.HiddenChannels}, blocks={meta.NumResBlocks}");
                }
            }
            else
            {
                _model.load(path);
                _model.to(_device);
                
                if (Verbose)
                {
                    AnsiConsole.MarkupLine($"[dim]Model metadata:[/] {meta}");
                }
            }
        }
        else
        {
            if (Verbose)
            {
                AnsiConsole.MarkupLine("[yellow]Warning: No metadata file found. Assuming default architecture.[/]");
            }
            
            _model.load(path);
            _model.to(_device);
        }

        // Recreate optimizer with model parameters after loading
        _optimizer.Dispose();
        _optimizer = optim.Adam(_model.parameters(), lr: _learningRate);

        if (Verbose)
        {
            AnsiConsole.MarkupLine($"[green]Model loaded from:[/] {path}");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _model.Dispose();
            _optimizer.Dispose();
            _disposed = true;
        }
    }
}
