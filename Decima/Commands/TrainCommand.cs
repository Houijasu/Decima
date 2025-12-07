namespace Decima.Commands;

using System.ComponentModel;

using Decima.Models;

using Spectre.Console;
using Spectre.Console.Cli;

using static TorchSharp.torch;

public sealed class TrainCommand : Command<TrainCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-e|--epochs <EPOCHS>")]
        [Description("Number of training epochs")]
        [DefaultValue(10)]
        public int Epochs { get; init; }

        [CommandOption("-b|--batch-size <SIZE>")]
        [Description("Batch size for training (larger = faster but more GPU memory)")]
        [DefaultValue(256)]
        public int BatchSize { get; init; }

        [CommandOption("-s|--samples <SAMPLES>")]
        [Description("Samples per epoch")]
        [DefaultValue(50000)]
        public int SamplesPerEpoch { get; init; }

        [CommandOption("-l|--learning-rate <RATE>")]
        [Description("Learning rate")]
        [DefaultValue(0.002)]
        public double LearningRate { get; init; }

        [CommandOption("--empty-cells <COUNT>")]
        [Description("Number of empty cells in puzzles (legacy mode, use --min/--max for curriculum)")]
        [DefaultValue(40)]
        public int EmptyCells { get; init; }

        [CommandOption("--min-empty <COUNT>")]
        [Description("Minimum empty cells for curriculum learning (easiest)")]
        [DefaultValue(20)]
        public int MinEmptyCells { get; init; }

        [CommandOption("--max-empty <COUNT>")]
        [Description("Maximum empty cells for curriculum learning (hardest, max 64 = 17 clues)")]
        [DefaultValue(55)]
        public int MaxEmptyCells { get; init; }

        [CommandOption("-o|--output <PATH>")]
        [Description("Output path for trained model")]
        [DefaultValue("sudoku_model.bin")]
        public string OutputPath { get; init; } = "sudoku_model.bin";

        [CommandOption("--eval-samples <COUNT>")]
        [Description("Number of samples for evaluation after training")]
        [DefaultValue(100)]
        public int EvalSamples { get; init; }

        [CommandOption("-r|--resume")]
        [Description("Resume training from existing model file")]
        [DefaultValue(false)]
        public bool Resume { get; init; }

        [CommandOption("--curriculum")]
        [Description("Enable curriculum learning (progressive difficulty)")]
        [DefaultValue(true)]
        public bool UseCurriculum { get; init; }

        [CommandOption("--augment")]
        [Description("Enable data augmentation (digit permutations, rotations, etc.)")]
        [DefaultValue(true)]
        public bool UseAugmentation { get; init; }

        [CommandOption("--strategy <STRATEGY>")]
        [Description("Curriculum progression strategy (Linear, Exponential, Logarithmic, Step, Cosine, HardFocus)")]
        [DefaultValue(CurriculumStrategy.Cosine)]
        public CurriculumStrategy Strategy { get; init; }

        [CommandOption("--hidden <CHANNELS>")]
        [Description("Hidden channels in model (256=standard, 512=large for higher accuracy)")]
        [DefaultValue(256)]
        public int HiddenChannels { get; init; }

        [CommandOption("--blocks <COUNT>")]
        [Description("Number of residual blocks (10=standard, 15-20=large for higher accuracy)")]
        [DefaultValue(10)]
        public int NumResBlocks { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        AnsiConsole.MarkupLine("[bold cyan]Sudoku Solver Training[/]");
        AnsiConsole.WriteLine();

        // Require CUDA for training
        if (!cuda.is_available())
        {
            AnsiConsole.MarkupLine("[bold red]Error: CUDA is not available. Training requires a GPU.[/]");
            return 1;
        }

        // Check if resuming from existing model
        var resuming = settings.Resume && File.Exists(settings.OutputPath);
        if (settings.Resume && !File.Exists(settings.OutputPath))
        {
            AnsiConsole.MarkupLine($"[yellow]Warning: --resume specified but model file '{settings.OutputPath}' not found. Starting fresh.[/]");
        }

        // Validate empty cell ranges
        var minEmpty = Math.Clamp(settings.MinEmptyCells, 17, 64);
        var maxEmpty = Math.Clamp(settings.MaxEmptyCells, 17, 64);
        if (minEmpty > maxEmpty)
        {
            AnsiConsole.MarkupLine("[red]Error: --min-empty cannot be greater than --max-empty[/]");
            return 1;
        }
        if (maxEmpty > 60)
        {
            AnsiConsole.MarkupLine($"[yellow]Note: Training with {maxEmpty} empty cells (extreme difficulty). This may require more epochs.[/]");
        }

        // Display configuration
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey);
        table.AddColumn(new TableColumn("[cyan]Parameter[/]").LeftAligned());
        table.AddColumn(new TableColumn("[cyan]Value[/]").RightAligned());
        table.AddRow("Epochs", $"[white]{settings.Epochs}[/]");
        table.AddRow("Batch Size", $"[white]{settings.BatchSize}[/]");
        table.AddRow("Samples/Epoch", $"[white]{settings.SamplesPerEpoch:N0}[/]");
        table.AddRow("Learning Rate", $"[white]{settings.LearningRate:F6}[/]");

        if (settings.UseCurriculum)
        {
            table.AddRow("Curriculum", $"[green]{minEmpty} → {maxEmpty} empty ({settings.Strategy})[/]");
        }
        else
        {
            table.AddRow("Empty Cells", $"[white]{settings.EmptyCells}[/]");
        }

        table.AddRow("Data Augmentation", settings.UseAugmentation ? "[green]Enabled[/]" : "[dim]Disabled[/]");
        table.AddRow("Model Size", $"[white]hidden={settings.HiddenChannels}, blocks={settings.NumResBlocks}[/]");
        table.AddRow("Output Path", $"[white]{settings.OutputPath}[/]");
        table.AddRow("Device", "[green]CUDA (GPU)[/]");
        table.AddRow("Resume", resuming ? "[green]Yes (loading existing model)[/]" : "[dim]No (fresh start)[/]");
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        using var trainer = new SudokuTrainer(
            settings.LearningRate,
            inferenceOnly: false,
            hiddenChannels: settings.HiddenChannels,
            numResBlocks: settings.NumResBlocks);

        if (resuming)
        {
            AnsiConsole.MarkupLine($"[cyan]Loading existing model from:[/] {settings.OutputPath}");

            try
            {
                trainer.Load(settings.OutputPath);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Failed to load existing model:[/] {settings.OutputPath}");
                AnsiConsole.MarkupLine($"[dim]Error: {ex.Message}[/]");
                AnsiConsole.MarkupLine("[yellow]Starting fresh training instead.[/]");
                resuming = false;
            }

            AnsiConsole.WriteLine();
        }

        bool stoppedEarly;

        if (settings.UseCurriculum)
        {
            stoppedEarly = trainer.TrainWithCurriculum(
                settings.Epochs,
                settings.BatchSize,
                settings.SamplesPerEpoch,
                minEmpty,
                maxEmpty,
                settings.UseAugmentation,
                settings.Strategy);
        }
        else
        {
            stoppedEarly = trainer.Train(
                settings.Epochs,
                settings.BatchSize,
                settings.SamplesPerEpoch,
                settings.EmptyCells);
        }

        AnsiConsole.WriteLine();

        if (stoppedEarly)
        {
            AnsiConsole.MarkupLine("[yellow]Training stopped early by user.[/]");
            AnsiConsole.WriteLine();
        }

        if (settings.EvalSamples > 0)
        {
            AnsiConsole.MarkupLine("[bold]Evaluating model...[/]");

            // Evaluate at multiple difficulty levels
            var easyAcc = trainer.Evaluate(settings.EvalSamples, 30);
            var mediumAcc = trainer.Evaluate(settings.EvalSamples, 40);
            var hardAcc = trainer.Evaluate(settings.EvalSamples, 50);
            var extremeAcc = trainer.Evaluate(settings.EvalSamples, 62);

            AnsiConsole.MarkupLine($"  Easy (30 empty):     [{GetAccuracyColor(easyAcc)}]{easyAcc:P1}[/]");
            AnsiConsole.MarkupLine($"  Medium (40 empty):   [{GetAccuracyColor(mediumAcc)}]{mediumAcc:P1}[/]");
            AnsiConsole.MarkupLine($"  Hard (50 empty):     [{GetAccuracyColor(hardAcc)}]{hardAcc:P1}[/]");
            AnsiConsole.MarkupLine($"  Extreme (62 empty):  [{GetAccuracyColor(extremeAcc)}]{extremeAcc:P1}[/]");
            AnsiConsole.WriteLine();
        }

        trainer.Save(settings.OutputPath);

        AnsiConsole.MarkupLine(stoppedEarly
            ? "[bold yellow]✓ Model saved (training interrupted)[/]"
            : "[bold green]✓ Training complete![/]");

        return 0;
    }

    private static string GetAccuracyColor(double accuracy) => accuracy switch
    {
        >= 0.95 => "green",
        >= 0.80 => "yellow",
        _ => "red"
    };
}
