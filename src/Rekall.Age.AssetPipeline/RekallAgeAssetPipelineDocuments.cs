using Rekall.Age.Assets;

namespace Rekall.Age.AssetPipeline;

public sealed record RekallAgeAssetPipelineDocument(
    IReadOnlyList<RekallAgeAssetSourceRecord> Sources,
    IReadOnlyList<RekallAgeImportedAssetRecord> Imported,
    IReadOnlyList<RekallAgeCookedAssetRecord> CookedArtifacts,
    IReadOnlyList<RekallAgeAssetDependencyRecord> Dependencies)
{
    public static RekallAgeAssetPipelineDocument Empty { get; } = new(
        Array.Empty<RekallAgeAssetSourceRecord>(),
        Array.Empty<RekallAgeImportedAssetRecord>(),
        Array.Empty<RekallAgeCookedAssetRecord>(),
        Array.Empty<RekallAgeAssetDependencyRecord>());

    public RekallAgeAssetPipelineDocument AddImport(
        RekallAgeAssetDocument asset,
        string sourcePath,
        string kind)
    {
        var source = new RekallAgeAssetSourceRecord(asset.Id, sourcePath, kind, asset.ContentHash);
        var imported = new RekallAgeImportedAssetRecord(asset.Id, asset.ImportedPath, kind, asset.ContentHash);
        var cooked = new RekallAgeCookedAssetRecord(asset.Id, asset.ImportedPath, "raw-copy", asset.ContentHash);
        return this with
        {
            Sources = Replace(Sources, source, item => item.AssetId),
            Imported = Replace(Imported, imported, item => item.AssetId),
            CookedArtifacts = Replace(CookedArtifacts, cooked, item => item.AssetId)
        };
    }

    private static IReadOnlyList<T> Replace<T>(
        IEnumerable<T> existing,
        T value,
        Func<T, string> key)
    {
        var valueKey = key(value);
        return existing
            .Where(item => !key(item).Equals(valueKey, StringComparison.Ordinal))
            .Append(value)
            .OrderBy(key, StringComparer.Ordinal)
            .ToArray();
    }
}

public sealed record RekallAgeAssetSourceRecord(
    string AssetId,
    string SourcePath,
    string Kind,
    string ContentHash);

public sealed record RekallAgeImportedAssetRecord(
    string AssetId,
    string ImportedPath,
    string Kind,
    string ContentHash);

public sealed record RekallAgeCookedAssetRecord(
    string AssetId,
    string ArtifactPath,
    string ArtifactKind,
    string ContentHash);

public sealed record RekallAgeAssetDependencyRecord(
    string AssetId,
    string DependsOnAssetId,
    string Reason);

public sealed record RekallAgeAssetImportReport(
    bool Imported,
    string AssetId,
    string Kind,
    string SourcePath,
    string ImportedPath,
    IReadOnlyList<string> Diagnostics);
