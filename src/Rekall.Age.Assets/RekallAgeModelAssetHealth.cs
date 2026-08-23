namespace Rekall.Age.Assets;

public sealed record RekallAgeModelAssetInspection(
    RekallAgeModelAssetDocument? Asset,
    string ModelFileRevision,
    RekallAgeModelBuildState BuildState,
    string? CurrentSourceFileRevision,
    long? CurrentSourceLogicalRevision,
    bool CompiledOutputExists,
    string? ActualCompiledContentHash,
    IReadOnlyList<RekallAgeModelBuildDiagnostic> Diagnostics);

public interface IRekallAgeModelAssetHealthInspector
{
    ValueTask<RekallAgeModelAssetInspection> InspectAsync(
        string projectRoot,
        string assetId,
        CancellationToken cancellationToken);
}
