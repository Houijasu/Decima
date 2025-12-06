namespace Decima.Models;

using System.Text.Json;

/// <summary>
/// Metadata for model versioning and compatibility checks.
/// </summary>
public readonly record struct ModelMetadata(
    int Version,
    int InputChannels,
    int HiddenChannels,
    int OutputChannels,
    int NumResBlocks)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Saves metadata to a JSON file alongside the model.
    /// </summary>
    public void Save(string modelPath)
    {
        var metadataPath = GetMetadataPath(modelPath);
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(metadataPath, json);
    }

    /// <summary>
    /// Loads metadata from a JSON file alongside the model.
    /// Returns null if metadata file doesn't exist.
    /// </summary>
    public static ModelMetadata? Load(string modelPath)
    {
        var metadataPath = GetMetadataPath(modelPath);
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        var json = File.ReadAllText(metadataPath);
        return JsonSerializer.Deserialize<ModelMetadata>(json);
    }

    /// <summary>
    /// Checks if this metadata is compatible with the current network architecture.
    /// </summary>
    public bool IsCompatibleWith(ModelMetadata current)
    {
        // Version check is strict because architecture might change (e.g. SE blocks added in v3)
        return Version == current.Version
            && InputChannels == current.InputChannels
            && HiddenChannels == current.HiddenChannels
            && OutputChannels == current.OutputChannels
            && NumResBlocks == current.NumResBlocks;
    }

    /// <summary>
    /// Gets the metadata file path for a given model path.
    /// </summary>
    public static string GetMetadataPath(string modelPath)
    {
        return Path.ChangeExtension(modelPath, ".meta.json");
    }

    public override string ToString()
    {
        return $"v{Version} (in={InputChannels}, hidden={HiddenChannels}, out={OutputChannels}, blocks={NumResBlocks})";
    }
}
