using System.Text.Json;
using Rekall.Age.Core.Persistence;

namespace Rekall.Age.AssetPipeline;

public sealed class RekallAgeAssetPipelineStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = RekallAgePersistedJson.MaximumDocumentDepth
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

        return await RekallAgePersistedJson.ReadAsync<RekallAgeAssetPipelineDocument>(
            path,
            JsonOptions,
            cancellationToken);
    }

    public async ValueTask SaveAsync(
        string projectRoot,
        RekallAgeAssetPipelineDocument document,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.Combine(projectRoot, "Assets"));
        var json = JsonSerializer.Serialize(document, JsonOptions);
        await RekallAgePersistedJson.WriteAllTextAsync(
            GetPath(projectRoot),
            json + Environment.NewLine,
            cancellationToken);
    }
}
