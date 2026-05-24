using System.Text.Json;

namespace Rekall.Age.Assets;

public sealed class RekallAgeAssetCatalogStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
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

        await using var stream = File.OpenRead(path);
        var catalog = await JsonSerializer.DeserializeAsync<RekallAgeAssetCatalogDocument>(
            stream,
            JsonOptions,
            cancellationToken);
        return catalog ?? RekallAgeAssetCatalogDocument.Empty;
    }

    public async ValueTask SaveAsync(
        string projectRoot,
        RekallAgeAssetCatalogDocument catalog,
        CancellationToken cancellationToken)
    {
        var assetsRoot = Path.Combine(projectRoot, "Assets");
        Directory.CreateDirectory(assetsRoot);
        var json = JsonSerializer.Serialize(catalog, JsonOptions);
        await File.WriteAllTextAsync(GetCatalogPath(projectRoot), json + Environment.NewLine, cancellationToken);
    }
}
