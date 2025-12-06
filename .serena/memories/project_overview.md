# Decima Project Overview

## Purpose
Decima is a Sudoku solver application that uses machine learning (neural networks via TorchSharp/PyTorch) to solve Sudoku puzzles. It provides training capabilities and inference for puzzle solving.

## Tech Stack
- **Framework:** .NET 10.0 (preview)
- **Language:** C# with latest language features
- **ML Framework:** TorchSharp 0.105.1 with CUDA support (Linux)
- **CLI Framework:** Spectre.Console.Cli for command-line interface
- **UI:** Spectre.Console for rich terminal output (colors, tables, figlet text)
- **Utilities:** Dunet 1.11.3 for discriminated unions

## Project Structure
```
/
├── Decima.slnx              # Solution file
├── .editorconfig            # Code style configuration
└── Decima/
    ├── Decima.csproj        # Project file
    ├── Program.cs           # Application entry point
    ├── ConfiguratorExtensions.cs  # CLI command auto-discovery
    ├── Commands/
    │   ├── PlayCommand.cs   # Interactive play mode
    │   ├── TrainCommand.cs  # Model training command
    │   └── SolveCommand.cs  # Puzzle solving command
    ├── Data/
    │   ├── SudokuGrid.cs    # Core 9x9 grid data structure
    │   ├── SudokuValidator.cs # Constraint validation
    │   └── SudokuGenerator.cs # Puzzle generation
    ├── Models/
    │   ├── SudokuNetwork.cs # CNN neural network model
    │   └── SudokuTrainer.cs # Training and inference
    └── UI/
        ├── SudokuGridRenderer.cs # Grid display with Spectre.Console
        └── SolveAnimation.cs     # Animated solving visualization
```

## Application Commands
1. **train** - Train the neural network model
   - Options: epochs, batch-size, samples, learning-rate, empty-cells, output, cpu, eval-samples
2. **solve** - Solve a Sudoku puzzle using trained model
   - Args: puzzle string (81 chars)
   - Options: model path, cpu
3. **play** - Interactive play mode (currently a stub)

## Development Status
- The project references `Decima.Models.SudokuTrainer` and `Decima.Data.SudokuGrid` which need to be implemented
- Basic CLI structure is in place with auto-discovery of commands
