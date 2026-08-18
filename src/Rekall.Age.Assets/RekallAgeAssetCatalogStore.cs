using System.Text.Json;
using Rekall.Age.Core.Persistence;

namespace Rekall.Age.Assets;

public sealed class RekallAgeAssetCatalogStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = RekallAgePersistedJson.MaximumDocumentDepth
    };

    public string GetCatalogPath(string projectRoot)
    {
        return Path.Combine(projectRoot, "Assets", "assets.age.catalog.json");
    }

    public async ValueTask<RekallAgeAssetCatalogDocument> LoadAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var path = GetCatalogPath(projectRoot);
        if (!File.Exists(path))
        {
            return RekallAgeAssetCatalogDocument.Empty;
        }

        return await RekallAgePersistedJson.ReadAsync<RekallAgeAssetCatalogDocument>(
            path,
            JsonOptions,
            cancellationToken);
    }

    public async ValueTask SaveAsync(
        string projectRoot,
        RekallAgeAssetCatalogDocument catalog,
        CancellationToken cancellationToken)
    {
        var assetsRoot = Path.Combine(projectRoot, "Assets");
        Directory.CreateDirectory(assetsRoot);
        var json = JsonSerializer.Serialize(catalog, JsonOptions);
        await RekallAgePersistedJson.WriteAllTextAsync(
            GetCatalogPath(projectRoot),
            json + Environment.NewLine,
            cancellationToken);
    }
}
