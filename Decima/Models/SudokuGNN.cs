namespace Decima.Models;

using TorchSharp;
using TorchSharp.Modules;

using static TorchSharp.torch;
using static TorchSharp.torch.nn;

/// <summary>
/// Graph Neural Network for Sudoku solving.
/// Models the Sudoku grid as a graph where:
/// - 81 nodes (one per cell)
/// - Edges connect cells that share constraints (same row, column, or box)
/// - Message passing allows global constraint reasoning
/// </summary>
public sealed class SudokuGNN : Module<Tensor, Tensor>
{
    private readonly Linear _inputProj;
    private readonly ModuleList<GraphConvLayer> _convLayers;
    private readonly Linear _outputProj;
    private readonly Tensor _edgeIndex;
    private readonly int _hiddenDim;
    private readonly int _numLayers;

    public const int InputChannels = 10; // 0=empty, 1-9=digits
    public const int OutputChannels = 9; // predictions for digits 1-9
    public const int NumNodes = 81;

    // Instance values for metadata
    public int HiddenDim { get; }
    public int NumLayers { get; }

    /// <summary>
    /// Gets the model metadata for this instance.
    /// </summary>
    public ModelMetadata Metadata => new(
        Version: 4, // GNN version
        InputChannels: InputChannels,
        HiddenChannels: HiddenDim,
        OutputChannels: OutputChannels,
        NumResBlocks: NumLayers
    );

    /// <summary>
    /// Creates a new SudokuGNN.
    /// </summary>
    /// <param name="hiddenDim">Hidden dimension for node embeddings (default: 128).</param>
    /// <param name="numLayers">Number of message passing layers (default: 8).</param>
    public SudokuGNN(int hiddenDim = 128, int numLayers = 8) : base("SudokuGNN")
    {
        _hiddenDim = hiddenDim;
        _numLayers = numLayers;
        HiddenDim = hiddenDim;
        NumLayers = numLayers;

        // Input projection: one-hot encoded value + positional features
        // 10 (value) + 9 (row one-hot) + 9 (col one-hot) + 9 (box one-hot) = 37
        _inputProj = Linear(37, hiddenDim);

        // Graph convolution layers with residual connections
        _convLayers = new ModuleList<GraphConvLayer>();
        for (var i = 0; i < numLayers; i++)
        {
            _convLayers.Add(new GraphConvLayer(hiddenDim));
        }

        // Output projection to digit logits
        _outputProj = Linear(hiddenDim, OutputChannels);

        // Build edge index (static, same for all puzzles)
        _edgeIndex = BuildEdgeIndex();

        RegisterComponents();
    }

    public override Tensor forward(Tensor input)
    {
        // Input shape: [batch, 10, 9, 9]
        var batchSize = input.shape[0];

        // Reshape to node features: [batch, 81, 10]
        var nodeFeatures = input.permute(0, 2, 3, 1).reshape([batchSize, NumNodes, InputChannels]);

        // Add positional features (row, col, box one-hot encodings)
        var posFeatures = GetPositionalFeatures(batchSize, input.device);
        nodeFeatures = cat([nodeFeatures, posFeatures], dim: 2); // [batch, 81, 37]

        // Project to hidden dimension
        var x = _inputProj.forward(nodeFeatures); // [batch, 81, hidden]

        // Get edge index on the same device
        var edges = _edgeIndex.to(input.device);

        // Message passing layers
        foreach (var layer in _convLayers)
        {
            x = layer.forward(x, edges);
        }

        // Output projection
        var logits = _outputProj.forward(x); // [batch, 81, 9]

        // Reshape to expected output format: [batch, 9, 9, 9]
        logits = logits.reshape([batchSize, 9, 9, OutputChannels]);
        logits = logits.permute(0, 3, 1, 2); // [batch, 9, 9, 9] -> [batch, digit, row, col]

        return logits;
    }

    /// <summary>
    /// Forward pass with softmax applied per cell.
    /// </summary>
    public Tensor ForwardWithProbabilities(Tensor input)
    {
        var logits = forward(input);
        return functional.softmax(logits, dim: 1);
    }

    /// <summary>
    /// Builds the edge index tensor for the Sudoku constraint graph.
    /// Each cell is connected to all other cells in the same row, column, and box.
    /// </summary>
    private static Tensor BuildEdgeIndex()
    {
        var edges = new List<(int, int)>();

        for (var node = 0; node < NumNodes; node++)
        {
            var row = node / 9;
            var col = node % 9;
            var boxRow = (row / 3) * 3;
            var boxCol = (col / 3) * 3;

            // Add edges to all cells in same row
            for (var c = 0; c < 9; c++)
            {
                if (c != col)
                {
                    edges.Add((node, row * 9 + c));
                }
            }

            // Add edges to all cells in same column
            for (var r = 0; r < 9; r++)
            {
                if (r != row)
                {
                    edges.Add((node, r * 9 + col));
                }
            }

            // Add edges to all cells in same box (excluding already added row/col)
            for (var r = boxRow; r < boxRow + 3; r++)
            {
                for (var c = boxCol; c < boxCol + 3; c++)
                {
                    if (r != row && c != col)
                    {
                        edges.Add((node, r * 9 + c));
                    }
                }
            }
        }

        // Convert to tensor: [2, num_edges]
        var numEdges = edges.Count;
        var edgeData = new long[2 * numEdges];

        for (var i = 0; i < numEdges; i++)
        {
            edgeData[i] = edges[i].Item1;
            edgeData[numEdges + i] = edges[i].Item2;
        }

        return tensor(edgeData, [2, numEdges], dtype: int64);
    }

    /// <summary>
    /// Gets positional features for each node (row, col, box one-hot encodings).
    /// </summary>
    private static Tensor GetPositionalFeatures(long batchSize, Device device)
    {
        // Create position features: [81, 27] (9 row + 9 col + 9 box)
        var posData = new float[NumNodes * 27];

        for (var node = 0; node < NumNodes; node++)
        {
            var row = node / 9;
            var col = node % 9;
            var box = (row / 3) * 3 + (col / 3);

            var offset = node * 27;
            posData[offset + row] = 1.0f;          // Row one-hot (0-8)
            posData[offset + 9 + col] = 1.0f;      // Col one-hot (9-17)
            posData[offset + 18 + box] = 1.0f;     // Box one-hot (18-26)
        }

        var posTensor = tensor(posData, [1, NumNodes, 27], dtype: float32, device: device);

        // Expand to batch size
        return posTensor.expand([batchSize, NumNodes, 27]);
    }
}

/// <summary>
/// Graph convolution layer with message passing.
/// Aggregates features from neighboring nodes and updates node representations.
/// </summary>
internal sealed class GraphConvLayer : Module<Tensor, Tensor, Tensor>
{
    private readonly Linear _messageLinear;
    private readonly Linear _updateLinear;
    private readonly LayerNorm _norm;
    private readonly int _hiddenDim;

    public GraphConvLayer(int hiddenDim) : base("GraphConvLayer")
    {
        _hiddenDim = hiddenDim;

        // Message transformation
        _messageLinear = Linear(hiddenDim * 2, hiddenDim);

        // Update transformation (combines self with aggregated messages)
        _updateLinear = Linear(hiddenDim * 2, hiddenDim);

        // Layer normalization for stability
        _norm = LayerNorm(hiddenDim);

        RegisterComponents();
    }

    public override Tensor forward(Tensor nodeFeatures, Tensor edgeIndex)
    {
        // nodeFeatures: [batch, num_nodes, hidden]
        // edgeIndex: [2, num_edges] - source and target node indices

        var batchSize = nodeFeatures.shape[0];
        var numNodes = nodeFeatures.shape[1];
        var numEdges = edgeIndex.shape[1];

        // Get source and target indices
        var srcIdx = edgeIndex[0]; // [num_edges]
        var tgtIdx = edgeIndex[1]; // [num_edges]

        // Gather source and target features for each edge
        // We need to expand for batch dimension
        var srcIdxExpanded = srcIdx.unsqueeze(0).unsqueeze(-1).expand([batchSize, numEdges, _hiddenDim]);
        var tgtIdxExpanded = tgtIdx.unsqueeze(0).unsqueeze(-1).expand([batchSize, numEdges, _hiddenDim]);

        var srcFeatures = nodeFeatures.gather(1, srcIdxExpanded); // [batch, num_edges, hidden]
        var tgtFeatures = nodeFeatures.gather(1, tgtIdxExpanded); // [batch, num_edges, hidden]

        // Compute messages: concat source and target, then transform
        var edgeFeatures = cat([srcFeatures, tgtFeatures], dim: 2); // [batch, num_edges, hidden*2]
        var messages = functional.relu(_messageLinear.forward(edgeFeatures)); // [batch, num_edges, hidden]

        // Aggregate messages at each node (sum aggregation)
        var aggregated = zeros([batchSize, numNodes, _hiddenDim], dtype: nodeFeatures.dtype, device: nodeFeatures.device);

        // Scatter-add messages to target nodes
        var tgtIdxForScatter = tgtIdx.unsqueeze(0).unsqueeze(-1).expand([batchSize, numEdges, _hiddenDim]);
        aggregated = aggregated.scatter_add(1, tgtIdxForScatter, messages);

        // Update node features: combine self with aggregated messages
        var combined = cat([nodeFeatures, aggregated], dim: 2); // [batch, num_nodes, hidden*2]
        var updated = _updateLinear.forward(combined); // [batch, num_nodes, hidden]

        // Residual connection + layer norm
        updated = _norm.forward(updated + nodeFeatures);
        updated = functional.relu(updated);

        return updated;
    }
}
