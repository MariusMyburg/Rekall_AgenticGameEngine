using System.Text.Json;

namespace Rekall.Age.AssetPipeline;

public sealed class RekallAgeAssetPipelineStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string GetPath(string projectRoot)
    {
        return Path.Combine(projectRoot, "Assets", "asset-pipeline.age.json");
    }

    public async ValueTask<RekallAgeAssetPipelineDocument> LoadAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var path = GetPath(projectRoot);
        if (!File.Exists(path))
        {
            return RekallAgeAssetPipelineDocument.Empty;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<RekallAgeAssetPipelineDocument>(
            stream,
            JsonOptions,
            cancellationToken) ?? RekallAgeAssetPipelineDocument.Empty;
    }

    public async ValueTask SaveAsync(
        string projectRoot,
        RekallAgeAssetPipelineDocument document,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.Combine(projectRoot, "Assets"));
        var json = JsonSerializer.Serialize(document, JsonOptions);
        await File.WriteAllTextAsync(GetPath(projectRoot), json + Environment.NewLine, cancellationToken);
    }
}
