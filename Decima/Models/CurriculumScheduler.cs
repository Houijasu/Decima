namespace Decima.Models;

/// <summary>
/// Curriculum learning scheduler for progressive difficulty training.
/// Starts with easy puzzles and gradually increases difficulty.
/// </summary>
public sealed class CurriculumScheduler
{
    private readonly int _minEmptyCells;
    private readonly int _maxEmptyCells;
    private readonly int _totalEpochs;
    private readonly CurriculumStrategy _strategy;

    /// <summary>
    /// Gets the current difficulty level (0.0 = easiest, 1.0 = hardest).
    /// </summary>
    public double CurrentDifficulty { get; private set; }

    /// <summary>
    /// Gets the current number of empty cells for puzzle generation.
    /// </summary>
    public int CurrentEmptyCells { get; private set; }

    public CurriculumScheduler(
        int minEmptyCells = 20,
        int maxEmptyCells = 55,
        int totalEpochs = 100,
        CurriculumStrategy strategy = CurriculumStrategy.Linear)
    {
        _minEmptyCells = minEmptyCells;
        _maxEmptyCells = maxEmptyCells;
        _totalEpochs = totalEpochs;
        _strategy = strategy;

        CurrentEmptyCells = _minEmptyCells;
        CurrentDifficulty = 0.0;
    }

    /// <summary>
    /// Updates the curriculum based on the current epoch.
    /// </summary>
    /// <param name="epoch">Current epoch (0-indexed).</param>
    public void Update(int epoch)
    {
        var progress = Math.Clamp((double)epoch / _totalEpochs, 0.0, 1.0);

        CurrentDifficulty = _strategy switch
        {
            CurriculumStrategy.Linear => progress,
            CurriculumStrategy.Exponential => Math.Pow(progress, 2),
            CurriculumStrategy.Logarithmic => Math.Log(1 + progress * (Math.E - 1)),
            CurriculumStrategy.Step => GetStepDifficulty(progress),
            CurriculumStrategy.Cosine => 0.5 * (1 - Math.Cos(Math.PI * progress)),
            CurriculumStrategy.HardFocus => GetHardFocusDifficulty(progress),
            _ => progress
        };

        CurrentEmptyCells = (int)Math.Round(_minEmptyCells + CurrentDifficulty * (_maxEmptyCells - _minEmptyCells));
    }

    /// <summary>
    /// Updates curriculum based on validation performance.
    /// Increases difficulty if accuracy is above threshold.
    /// </summary>
    /// <param name="accuracy">Current validation accuracy.</param>
    /// <param name="threshold">Accuracy threshold to advance difficulty.</param>
    public void UpdateBasedOnPerformance(double accuracy, double threshold = 0.95)
    {
        if (accuracy >= threshold && CurrentEmptyCells < _maxEmptyCells)
        {
            CurrentEmptyCells = Math.Min(CurrentEmptyCells + 2, _maxEmptyCells);
            CurrentDifficulty = (double)(CurrentEmptyCells - _minEmptyCells) / (_maxEmptyCells - _minEmptyCells);
        }
    }

    private static double GetStepDifficulty(double progress)
    {
        // 5 difficulty stages
        return progress switch
        {
            < 0.2 => 0.0,
            < 0.4 => 0.25,
            < 0.6 => 0.5,
            < 0.8 => 0.75,
            _ => 1.0
        };
    }

    private static double GetHardFocusDifficulty(double progress)
    {
        // First 20% of training: quickly ramp from 0 to 0.6 (easy to medium-hard)
        // Remaining 80%: slowly progress from 0.6 to 1.0 (hard to expert)
        // This ensures most training time is spent on hard puzzles
        if (progress < 0.2)
        {
            // Linear ramp: 0 -> 0.6 in first 20%
            return progress * 3.0; // 0.2 * 3 = 0.6
        }
        else
        {
            // Slow progression: 0.6 -> 1.0 in remaining 80%
            var hardProgress = (progress - 0.2) / 0.8; // normalize to 0-1
            return 0.6 + hardProgress * 0.4;
        }
    }

    /// <summary>
    /// Gets a random number of empty cells with some variance around the current target.
    /// This adds diversity to training samples within each difficulty level.
    /// </summary>
    /// <param name="variance">Maximum variance in empty cells.</param>
    public int GetRandomEmptyCells(int variance = 3)
    {
        var random = Random.Shared;
        var min = Math.Max(_minEmptyCells, CurrentEmptyCells - variance);
        var max = Math.Min(_maxEmptyCells, CurrentEmptyCells + variance);
        return random.Next(min, max + 1);
    }
}

/// <summary>
/// Strategy for curriculum difficulty progression.
/// </summary>
public enum CurriculumStrategy
{
    /// <summary>
    /// Linear progression from easy to hard.
    /// </summary>
    Linear,

    /// <summary>
    /// Exponential progression - stays easy longer, ramps up quickly at the end.
    /// </summary>
    Exponential,

    /// <summary>
    /// Logarithmic progression - quickly increases then plateaus.
    /// </summary>
    Logarithmic,

    /// <summary>
    /// Step-wise progression - discrete difficulty levels.
    /// </summary>
    Step,

    /// <summary>
    /// Cosine progression - smooth S-curve.
    /// </summary>
    Cosine,

    /// <summary>
    /// Hard-focused progression - quickly reaches hard difficulty and stays there longer.
    /// First 20% of epochs: easy to medium, remaining 80%: hard to expert.
    /// </summary>
    HardFocus
}
