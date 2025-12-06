namespace Decima.Solvers;

using TorchSharp;

using static TorchSharp.torch;

/// <summary>
/// GPU-accelerated fitness evaluator using TorchSharp.
/// Evaluates entire population in parallel on GPU.
/// </summary>
public sealed class GpuFitnessEvaluator : IDisposable
{
    private readonly Device _device;
    private bool _disposed;

    public GpuFitnessEvaluator(bool useGpu = true)
    {
        _device = useGpu && cuda.is_available() ? CUDA : CPU;
    }

    /// <summary>
    /// Gets the device being used (CPU or CUDA).
    /// </summary>
    public Device Device => _device;

    /// <summary>
    /// Evaluates fitness for all chromosomes in the population.
    /// Fitness = number of constraint violations (lower is better, 0 = solved).
    /// </summary>
    public void EvaluatePopulation(IList<Chromosome> population)
    {
        if (population.Count == 0) return;

        using var scope = NewDisposeScope();

        // Convert population to tensor [PopSize, 81]
        var popSize = population.Count;
        var data = new long[popSize * 81];

        for (var i = 0; i < popSize; i++)
        {
            var genes = population[i].GetGenesArray();
            for (var j = 0; j < 81; j++)
            {
                data[i * 81 + j] = genes[j];
            }
        }

        // Create tensor and move to device
        var popTensor = tensor(data, [popSize, 9, 9], dtype: int64, device: CPU).to(_device);

        // Calculate fitness for all chromosomes
        var fitness = CalculateFitness(popTensor);

        // Transfer results back to CPU and update chromosomes
        var fitnessArray = fitness.to(CPU).data<long>().ToArray();
        for (var i = 0; i < popSize; i++)
        {
            population[i].SetFitness((int)fitnessArray[i]);
        }
    }

    /// <summary>
    /// Calculates fitness (constraint violations) for a batch of grids.
    /// </summary>
    private Tensor CalculateFitness(Tensor population)
    {
        // population: [PopSize, 9, 9] with values 1-9

        var rowViolations = CountRowViolations(population);
        var colViolations = CountColumnViolations(population);
        var boxViolations = CountBoxViolations(population);

        return rowViolations + colViolations + boxViolations;
    }

    /// <summary>
    /// Counts duplicate digits in each row.
    /// </summary>
    private Tensor CountRowViolations(Tensor population)
    {
        // population: [PopSize, 9, 9]
        // For each row, count how many times each digit appears, then sum (count - 1) for duplicates

        var popSize = population.shape[0];
        var violations = zeros([popSize], dtype: int64, device: _device);

        // One-hot encode: [PopSize, 9, 9] -> [PopSize, 9, 9, 9] where last dim is digit (0-8 for 1-9)
        var oneHot = nn.functional.one_hot(population - 1, 9); // [PopSize, 9, 9, 9]

        // Sum across columns for each row and digit: [PopSize, 9, 9]
        var digitCountsPerRow = oneHot.sum(dim: 2); // [PopSize, 9, 9] = [PopSize, row, digit]

        // Count violations: sum of (count - 1) where count > 1
        var duplicates = (digitCountsPerRow - 1).clamp_min(0);
        violations = duplicates.sum(dim: [1, 2]);

        return violations;
    }

    /// <summary>
    /// Counts duplicate digits in each column.
    /// </summary>
    private Tensor CountColumnViolations(Tensor population)
    {
        // Transpose to make columns into rows, then use same logic
        var transposed = population.permute([0, 2, 1]); // [PopSize, 9, 9] with cols as rows

        var oneHot = nn.functional.one_hot(transposed - 1, 9); // [PopSize, 9, 9, 9]
        var digitCountsPerCol = oneHot.sum(dim: 2); // [PopSize, col, digit]

        var duplicates = (digitCountsPerCol - 1).clamp_min(0);
        return duplicates.sum(dim: [1, 2]);
    }

    /// <summary>
    /// Counts duplicate digits in each 3x3 box.
    /// </summary>
    private Tensor CountBoxViolations(Tensor population)
    {
        var popSize = population.shape[0];

        // Reshape to extract 3x3 boxes: [PopSize, 3, 3, 3, 3]
        var reshaped = population.reshape([popSize, 3, 3, 3, 3]);

        // Permute to group box elements: [PopSize, 3, 3, 3, 3] -> [PopSize, 3, 3, 9]
        var boxes = reshaped.permute([0, 1, 3, 2, 4]).reshape([popSize, 9, 9]);
        // Now boxes[i, box_idx, element_idx] where box_idx is 0-8 and element_idx is 0-8

        var oneHot = nn.functional.one_hot(boxes - 1, 9); // [PopSize, 9, 9, 9]
        var digitCountsPerBox = oneHot.sum(dim: 2); // [PopSize, box, digit]

        var duplicates = (digitCountsPerBox - 1).clamp_min(0);
        return duplicates.sum(dim: [1, 2]);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}
