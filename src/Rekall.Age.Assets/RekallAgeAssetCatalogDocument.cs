namespace Rekall.Age.Assets;

public sealed record RekallAgeAssetCatalogDocument(IReadOnlyList<RekallAgeAssetDocument> Assets)
{
    public static RekallAgeAssetCatalogDocument Empty { get; } = new(Array.Empty<RekallAgeAssetDocument>());

    public RekallAgeAssetCatalogDocument AddOrReplace(RekallAgeAssetDocument asset)
    {
        return this with
        {
            Assets = Assets
                .Where(existing => !existing.Id.Equals(asset.Id, StringComparison.Ordinal))
                .Append(asset)
                .OrderBy(item => item.Kind, StringComparer.Ordinal)
                .ThenBy(item => item.Name, StringComparer.Ordinal)
                .ToArray()
        };
    }
}
