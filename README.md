# Decima

A GPU-accelerated Sudoku solver powered by deep learning, featuring a convolutional neural network with Squeeze-and-Excitation blocks, curriculum learning, and beam search inference.

## Features

- **Deep Learning Solver**: CNN with residual blocks and SE attention for accurate predictions
- **Curriculum Learning**: Progressive difficulty training from easy to extreme puzzles
- **Data Augmentation**: Digit permutations, rotations, reflections for robust training
- **Beam Search**: Explores multiple solution paths for higher accuracy
- **Genetic Algorithm**: Alternative solver with GPU-accelerated fitness evaluation
- **Hybrid Mode**: Combines ML predictions with genetic algorithm refinement
- **ML-Guided Backtracking**: Uses neural network to prioritize search order
- **Interactive Play**: Terminal-based Sudoku game with real-time validation
- **Beautiful UI**: Spectre.Console-powered terminal interface

## Requirements

- .NET 10.0 SDK
- NVIDIA GPU with CUDA support (for training)
- Linux (CUDA binaries included for linux-x64)

## Installation

```bash
git clone https://github.com/yourusername/Decima.git
cd Decima
dotnet build
```

## Usage

### Interactive Menu

Run without arguments to launch the interactive menu:

```bash
dotnet run --project Decima
```

### Training

Train a new model with curriculum learning:

```bash
# Standard training (good balance of speed and accuracy)
dotnet run --project Decima train --epochs 30 --samples 50000

# High-accuracy training (larger model, more data)
dotnet run --project Decima train \
  --epochs 50 \
  --hidden 512 \
  --blocks 15 \
  --strategy HardFocus \
  --samples 100000 \
  --min-empty 20 \
  --max-empty 60
```

#### Training Options

| Option | Default | Description |
|--------|---------|-------------|
| `--epochs` | 10 | Number of training epochs |
| `--batch-size` | 256 | Batch size (larger = faster, more GPU memory) |
| `--samples` | 50000 | Samples per epoch |
| `--learning-rate` | 0.002 | Learning rate |
| `--hidden` | 256 | Hidden channels (256=standard, 512=large) |
| `--blocks` | 10 | Residual blocks (10=standard, 15-20=large) |
| `--min-empty` | 20 | Minimum empty cells (curriculum start) |
| `--max-empty` | 55 | Maximum empty cells (curriculum end) |
| `--strategy` | Cosine | Curriculum strategy (see below) |
| `--augment` | true | Enable data augmentation |
| `--resume` | false | Resume from existing model |

#### Curriculum Strategies

- **Linear**: Steady progression from easy to hard
- **Cosine**: Smooth S-curve progression
- **HardFocus**: 20% warmup, 80% on hard puzzles (recommended)
- **Exponential**: Stays easy longer, ramps up quickly
- **Step**: Discrete difficulty levels

### Solving Puzzles

```bash
# Solve a specific puzzle (use . or 0 for empty cells)
dotnet run --project Decima solve "53..7....6..195....98....6.8...6...34..8.3..17...2...6.6....28....419..5....8..79"

# Generate and solve a random puzzle
dotnet run --project Decima solve --generate --difficulty Hard

# Use genetic algorithm solver
dotnet run --project Decima solve --generate --ga

# Hybrid ML + GA solver
dotnet run --project Decima solve --generate --hybrid

# ML-guided backtracking (fast with ML prioritization)
dotnet run --project Decima solve --generate --guided
```

#### Solve Options

| Option | Default | Description |
|--------|---------|-------------|
| `--model` | sudoku_model.bin | Path to trained model |
| `--generate` | false | Generate random puzzle |
| `--difficulty` | Medium | Easy, Medium, Hard, Expert, Extreme |
| `--beam-width` | 2 | Beam search width (1=greedy) |
| `--animate` | false | Show solving animation |
| `--compare` | false | Compare with backtracking solver |
| `--ga` | false | Use genetic algorithm |
| `--hybrid` | false | ML + GA hybrid solver |
| `--guided` | false | ML-guided backtracking solver |

### Playing Sudoku

```bash
dotnet run --project Decima play
dotnet run --project Decima play --difficulty Hard
```

## Architecture

### Neural Network (v3)

```
Input [batch, 10, 9, 9]     # One-hot encoded (0=empty, 1-9=digits)
    │
    ▼
Conv2d (10 → hidden, 3×3) + BatchNorm + ReLU
    │
    ▼
┌─────────────────────────────┐
│   Residual Block × N        │
│   ┌─────────────────────┐   │
│   │ Conv2d + BN + ReLU  │   │
│   │ Conv2d + BN         │   │
│   │ SE Block (attention)│   │
│   │ + Skip Connection   │   │
│   │ ReLU                │   │
│   └─────────────────────┘   │
└─────────────────────────────┘
    │
    ▼
Conv2d (hidden → 9, 1×1)
    │
    ▼
Output [batch, 9, 9, 9]     # Logits for digits 1-9 at each cell
```

### Squeeze-and-Excitation Block

SE blocks provide channel-wise attention, allowing the network to focus on relevant features:

```
Input [B, C, H, W]
    │
    ▼
Global Average Pool → [B, C]
    │
    ▼
FC (C → C/16) + ReLU
    │
    ▼
FC (C/16 → C) + Sigmoid
    │
    ▼
Scale Input × Attention
```

### Training Features

- **Focal Loss**: Focuses training on hard examples with γ=2.0
- **Constraint Loss**: Penalizes row/column/box violations
- **Dynamic Weighting**: Constraint weight increases with difficulty
- **Cosine Annealing LR**: Learning rate scheduler for smoother convergence
- **Gradient Clipping**: Prevents exploding gradients

### Data Augmentation

Sudoku has rich symmetries that preserve validity:

| Augmentation | Variations |
|--------------|------------|
| Digit permutations | 362,880 (9!) |
| Rotations | 4 |
| Reflections | 2 |
| Row swaps (within bands) | 216 |
| Column swaps (within stacks) | 216 |

## Project Structure

```
Decima/
├── Commands/
│   ├── MenuCommand.cs      # Interactive menu
│   ├── PlayCommand.cs      # Sudoku game
│   ├── SolveCommand.cs     # Puzzle solver
│   └── TrainCommand.cs     # Model training
├── Data/
│   ├── SudokuAugmentation.cs  # Data augmentation
│   ├── SudokuGenerator.cs     # Puzzle generation
│   ├── SudokuGrid.cs          # Grid data structure
│   └── SudokuValidator.cs     # Validation logic
├── Models/
│   ├── BeamSearchSolver.cs    # Beam search inference
│   ├── CurriculumScheduler.cs # Training curriculum
│   ├── ModelMetadata.cs       # Model versioning
│   ├── SudokuNetwork.cs       # CNN architecture
│   └── SudokuTrainer.cs       # Training loop
├── Solvers/
│   ├── Chromosome.cs          # GA candidate solution
│   ├── GeneticOperators.cs    # Selection, crossover, mutation
│   ├── GeneticSolver.cs       # GA solver
│   ├── GeneticSolverOptions.cs # GA configuration
│   ├── GpuFitnessEvaluator.cs # GPU fitness evaluation
│   └── IslandModel.cs         # Parallel island GA
├── UI/
│   ├── SolveAnimation.cs      # Solving visualization
│   └── SudokuGridRenderer.cs  # Grid rendering
├── ConfiguratorExtensions.cs  # Command auto-discovery
└── Program.cs

Decima.Tests/                   # 105 unit tests
├── HybridSolverTests.cs
├── SudokuAugmentationTests.cs
├── SudokuGeneratorTests.cs
├── SudokuGridTests.cs
└── SudokuValidatorTests.cs
```

## Performance

Expected accuracy after training with recommended settings:

| Difficulty | Empty Cells | Target Accuracy |
|------------|-------------|-----------------|
| Easy | 30-35 | 98%+ |
| Medium | 36-45 | 95%+ |
| Hard | 46-52 | 85%+ |
| Expert | 53-59 | 70%+ |
| Extreme | 60-64 | 50%+ |

*Note: Extreme puzzles are near the theoretical minimum of 17 clues and are challenging even for specialized solvers.*

## Dependencies

- [TorchSharp](https://github.com/dotnet/TorchSharp) - .NET bindings for PyTorch
- [Spectre.Console](https://spectreconsole.net/) - Beautiful console UI
- [Dunet](https://github.com/domn1995/dunet) - Discriminated unions for C#

## Future Improvements

- [ ] CNN + GNN hybrid for better constraint reasoning
- [ ] Transformer/attention-based architecture
- [ ] Active learning (train on failed puzzles)
- [ ] Unique-solution puzzle generation for extreme difficulty
- [ ] Model quantization for faster inference
- [ ] ONNX export for cross-platform deployment

## License

MIT

## Acknowledgments

- Inspired by research on neural network approaches to constraint satisfaction problems
- SE block architecture from ["Squeeze-and-Excitation Networks"](https://arxiv.org/abs/1709.01507)
- Focal loss from ["Focal Loss for Dense Object Detection"](https://arxiv.org/abs/1708.02002)
