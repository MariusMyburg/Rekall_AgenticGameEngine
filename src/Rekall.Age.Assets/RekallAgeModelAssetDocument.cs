namespace Rekall.Age.Assets;

public enum RekallAgeModelSourceKind
{
    Mesh
}

public enum RekallAgeModelBuildState
{
    Current,
    Stale,
    Failed,
    Frozen
}

public sealed record RekallAgeModelSourceReference(
    RekallAgeModelSourceKind Kind,
    string AssetId,
    string? OutputName = null);

public sealed record RekallAgeModelBuildDiagnostic(
    string Code,
    string Severity,
    string Message,
    string? Target = null);

public sealed record RekallAgeModelBuildManifest(
    string SourceFileRevision,
    long SourceLogicalRevision,
    string CompiledMeshPath,
    string CompiledContentHash,
    string CompilerVersion,
    DateTimeOffset BuiltAtUtc,
    IReadOnlyList<RekallAgeModelBuildDiagnostic> Diagnostics)
{
    public const string CurrentCompilerVersion = "rekall-age-model-compiler-v1";

    public static RekallAgeModelBuildManifest Success(
        string sourceFileRevision,
        long sourceLogicalRevision,
        string compiledMeshPath,
        string compiledContentHash,
        string compilerVersion) =>
        new(
            sourceFileRevision,
            sourceLogicalRevision,
            compiledMeshPath,
            compiledContentHash,
            compilerVersion,
            DateTimeOffset.UtcNow,
            []);

    public bool Equals(RekallAgeModelBuildManifest? other) =>
        other is not null
        && string.Equals(SourceFileRevision, other.SourceFileRevision, StringComparison.Ordinal)
        && SourceLogicalRevision == other.SourceLogicalRevision
        && string.Equals(CompiledMeshPath, other.CompiledMeshPath, StringComparison.Ordinal)
        && string.Equals(CompiledContentHash, other.CompiledContentHash, StringComparison.Ordinal)
        && string.Equals(CompilerVersion, other.CompilerVersion, StringComparison.Ordinal)
        && BuiltAtUtc == other.BuiltAtUtc
        && Diagnostics.SequenceEqual(other.Diagnostics);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SourceFileRevision, StringComparer.Ordinal);
        hash.Add(SourceLogicalRevision);
        hash.Add(CompiledMeshPath, StringComparer.Ordinal);
        hash.Add(CompiledContentHash, StringComparer.Ordinal);
        hash.Add(CompilerVersion, StringComparer.Ordinal);
        hash.Add(BuiltAtUtc);
        foreach (var diagnostic in Diagnostics)
        {
            hash.Add(diagnostic);
        }

        return hash.ToHashCode();
    }
}

public sealed record RekallAgeModelAssetDocument(
    int SchemaVersion,
    string AssetId,
    string DisplayName,
    long Revision,
    RekallAgeModelSourceReference Source,
    RekallAgeModelBuildState BuildState,
    RekallAgeModelBuildManifest? LastSuccessfulBuild,
    bool Frozen)
{
    public const int CurrentSchemaVersion = 1;

    public static RekallAgeModelAssetDocument Create(
        string assetId,
        string displayName,
        RekallAgeModelSourceReference source,
        RekallAgeModelBuildManifest successfulBuild) =>
        new(
            CurrentSchemaVersion,
            assetId,
            displayName,
            1,
            source,
            RekallAgeModelBuildState.Current,
            successfulBuild,
            Frozen: false);
}
