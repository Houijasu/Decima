namespace Decima.Models;

using TorchSharp;
using TorchSharp.Modules;

using static TorchSharp.torch;
using static TorchSharp.torch.nn;

/// <summary>
/// Convolutional neural network for Sudoku solving.
/// Architecture: Input (10 channels) -> Conv blocks with residual connections -> Output (9 channels)
/// </summary>
public sealed class SudokuNetwork : Module<Tensor, Tensor>
{
    private readonly Conv2d _inputConv;
    private readonly BatchNorm2d _inputBn;
    private readonly ModuleList<Module<Tensor, Tensor>> _resBlocks;
    private readonly Conv2d _outputConv;

    public const int InputChannels = 10; // 0=empty, 1-9=digits
    public const int OutputChannels = 9; // predictions for digits 1-9

    // Default values (can be overridden via constructor)
    public const int DefaultHiddenChannels = 256;
    public const int DefaultNumResBlocks = 10;

    // Instance values (for metadata)
    public int HiddenChannels { get; }
    public int NumResBlocks { get; }

    /// <summary>
    /// Gets the model metadata for this instance.
    /// </summary>
    public ModelMetadata Metadata => new(
        Version: 3, // Bump version for SE blocks
        InputChannels: InputChannels,
        HiddenChannels: HiddenChannels,
        OutputChannels: OutputChannels,
        NumResBlocks: NumResBlocks
    );

    /// <summary>
    /// Creates a new SudokuNetwork with configurable capacity.
    /// </summary>
    /// <param name="hiddenChannels">Number of hidden channels (default: 256, use 512 for higher accuracy)</param>
    /// <param name="numResBlocks">Number of residual blocks (default: 10, use 15-20 for higher accuracy)</param>
    public SudokuNetwork(int hiddenChannels = DefaultHiddenChannels, int numResBlocks = DefaultNumResBlocks) 
        : base("SudokuNetwork")
    {
        HiddenChannels = hiddenChannels;
        NumResBlocks = numResBlocks;

        _inputConv = Conv2d(InputChannels, hiddenChannels, 3, padding: 1);
        _inputBn = BatchNorm2d(hiddenChannels);

        _resBlocks = new ModuleList<Module<Tensor, Tensor>>();
        for (var i = 0; i < numResBlocks; i++)
        {
            _resBlocks.Add(new ResidualBlock(hiddenChannels));
        }

        _outputConv = Conv2d(hiddenChannels, OutputChannels, 1);

        RegisterComponents();
    }

    public override Tensor forward(Tensor input)
    {
        // Input shape: [batch, 10, 9, 9]
        var x = _inputConv.forward(input);
        x = _inputBn.forward(x);
        x = functional.relu(x);

        // Residual blocks
        foreach (var block in _resBlocks)
        {
            x = block.forward(x);
        }

        // Output: [batch, 9, 9, 9]
        x = _outputConv.forward(x);

        return x;
    }

    /// <summary>
    /// Forward pass with softmax applied per cell.
    /// Returns probabilities for each digit (1-9) at each cell.
    /// </summary>
    public Tensor ForwardWithProbabilities(Tensor input)
    {
        var logits = forward(input);
        // Apply softmax over the channel dimension (digit predictions)
        return functional.softmax(logits, dim: 1);
    }
}

/// <summary>
/// Residual block with two convolutional layers and Squeeze-and-Excitation.
/// </summary>
internal sealed class ResidualBlock : Module<Tensor, Tensor>
{
    private readonly Conv2d _conv1;
    private readonly BatchNorm2d _bn1;
    private readonly Conv2d _conv2;
    private readonly BatchNorm2d _bn2;
    private readonly SEBlock _se;

    public ResidualBlock(int channels) : base("ResidualBlock")
    {
        _conv1 = Conv2d(channels, channels, 3, padding: 1);
        _bn1 = BatchNorm2d(channels);
        _conv2 = Conv2d(channels, channels, 3, padding: 1);
        _bn2 = BatchNorm2d(channels);
        _se = new SEBlock(channels);

        RegisterComponents();
    }

    public override Tensor forward(Tensor input)
    {
        var residual = input;

        var x = _conv1.forward(input);
        x = _bn1.forward(x);
        x = functional.relu(x);

        x = _conv2.forward(x);
        x = _bn2.forward(x);
        
        // Apply Squeeze-and-Excitation
        x = _se.forward(x);

        x = x + residual;
        x = functional.relu(x);

        return x;
    }
}

/// <summary>
/// Squeeze-and-Excitation Block.
/// Adaptively recalibrates channel-wise feature responses by explicitly modelling interdependencies between channels.
/// </summary>
internal sealed class SEBlock : Module<Tensor, Tensor>
{
    private readonly Linear _fc1;
    private readonly Linear _fc2;
    private readonly int _channels;

    public SEBlock(int channels, int reduction = 16) : base("SEBlock")
    {
        _channels = channels;
        // Ensure hidden size is at least 1
        var hidden = Math.Max(1, channels / reduction);
        
        _fc1 = Linear(channels, hidden);
        _fc2 = Linear(hidden, channels);

        RegisterComponents();
    }

    public override Tensor forward(Tensor input)
    {
        // input: [batch, channels, height, width]
        var batch = input.shape[0];
        var channels = input.shape[1];
        
        // Squeeze: Global Average Pooling -> [batch, channels]
        var y = input.mean([2, 3]);
        
        // Excitation: FC -> ReLU -> FC -> Sigmoid
        y = _fc1.forward(y);
        y = functional.relu(y);
        y = _fc2.forward(y);
        y = functional.sigmoid(y);
        
        // Scale: Reshape to [batch, channels, 1, 1] and multiply input
        y = y.view(batch, channels, 1, 1);
        
        return input * y;
    }
}
