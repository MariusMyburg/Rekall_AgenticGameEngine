using System.Text.Json;
using Rekall.Age.Core.Compatibility;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed class RekallAgeRigAssetStore
{
    private const string FileSuffix = ".age.rig.json";
    private static readonly JsonSerializerOptions JsonOptions = new(RekallAgeModelingJson.Options)
    {
        MaxDepth = RekallAgeDocumentSchemaProbe.MaximumDocumentDepth
    };
    private readonly RekallAgeRigValidator _validator = new();

    public string GetRigPath(string projectRoot, string assetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ValidateAssetId(assetId);
        return Path.Combine(Path.GetFullPath(projectRoot), "Modeling", "Rigs", assetId + FileSuffix);
    }

    public string GetRecoveryPath(string projectRoot, string assetId) =>
        RekallAgeDocumentRecoveryStore.GetPreviousPath(projectRoot, GetRigPath(projectRoot, assetId));

    public async ValueTask SaveAsync(string projectRoot, RekallAgeRigAsset rig, CancellationToken cancellationToken)
    {
        ValidateForPersistence(rig, rig.AssetId);
        await RekallAgeAtomicFile.WriteAllTextAsync(GetRigPath(projectRoot, rig.AssetId), Serialize(rig), RekallAgeDocumentSchemaProbe.MaximumDocumentBytes, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<string> SaveIfRevisionAsync(
        string projectRoot,
        RekallAgeRigAsset rig,
        string expectedRevision,
        CancellationToken cancellationToken)
    {
        ValidateForPersistence(rig, rig.AssetId);
        var path = GetRigPath(projectRoot, rig.AssetId);
        if (File.Exists(path))
        {
            var current = await LoadVersionedAsync(projectRoot, rig.AssetId, cancellationToken).ConfigureAwait(false);
            if (string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal) && rig.Revision != current.Value.Revision + 1)
                throw new InvalidDataException($"REKALL_RIG_LOGICAL_REVISION_INVALID: Rig revision must advance from {current.Value.Revision} to {current.Value.Revision + 1}.");
        }
        else if (rig.Revision != 1)
            throw new InvalidDataException("REKALL_RIG_LOGICAL_REVISION_INVALID: A new rig asset must start at revision 1.");
        return await RekallAgeAtomicFile.WriteAllTextIfRevisionAsync(
            path, Serialize(rig), RekallAgeDocumentSchemaProbe.MaximumDocumentBytes, expectedRevision,
            GetRecoveryPath(projectRoot, rig.AssetId), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<RekallAgeRigAsset> LoadAsync(string projectRoot, string assetId, CancellationToken cancellationToken)
        => (await LoadVersionedAsync(projectRoot, assetId, cancellationToken).ConfigureAwait(false)).Value;

    public async ValueTask<RekallAgeVersionedDocument<RekallAgeRigAsset>> LoadVersionedAsync(
        string projectRoot,
        string assetId,
        CancellationToken cancellationToken)
    {
        var snapshot = await RekallAgeDocumentSchemaProbe.ReadSnapshotAsync(GetRigPath(projectRoot, assetId), "rig asset", RekallAgeRigAsset.CurrentSchemaVersion, cancellationToken).ConfigureAwait(false);
        var rig = snapshot.Deserialize<RekallAgeRigAsset>(JsonOptions);
        ValidateForPersistence(rig, assetId);
        return new(rig with { SchemaVersion = RekallAgeRigAsset.CurrentSchemaVersion }, snapshot.File.Revision);
    }

    public RekallAgeRigAsset Load(string projectRoot, string assetId)
    {
        var path = GetRigPath(projectRoot, assetId);
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new FileNotFoundException($"Rig asset '{assetId}' does not exist.", path);
        if (info.Length > RekallAgeDocumentSchemaProbe.MaximumDocumentBytes)
            throw new InvalidDataException("REKALL_RIG_DOCUMENT_TOO_LARGE: Rig document exceeds the maximum document size.");
        var rig = JsonSerializer.Deserialize<RekallAgeRigAsset>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("REKALL_RIG_DOCUMENT_INVALID: Rig document is empty.");
        ValidateForPersistence(rig, assetId);
        return rig with { SchemaVersion = RekallAgeRigAsset.CurrentSchemaVersion };
    }

    public IReadOnlyList<string> ListAssetIds(string projectRoot)
    {
        var directory = Path.Combine(Path.GetFullPath(projectRoot), "Modeling", "Rigs");
        return !Directory.Exists(directory) ? [] : Directory.EnumerateFiles(directory, "*" + FileSuffix)
            .Select(Path.GetFileName).Where(name => name is not null).Select(name => name![..^FileSuffix.Length])
            .Order(StringComparer.Ordinal).ToArray();
    }

    public string Serialize(RekallAgeRigAsset rig) =>
        JsonSerializer.Serialize(rig with { SchemaVersion = RekallAgeRigAsset.CurrentSchemaVersion }, JsonOptions) + Environment.NewLine;

    private void ValidateForPersistence(RekallAgeRigAsset rig, string expectedAssetId)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ValidateAssetId(rig.AssetId);
        if (!string.Equals(rig.AssetId, expectedAssetId, StringComparison.Ordinal))
            throw new InvalidDataException($"REKALL_RIG_ASSET_ID_MISMATCH: Document asset ID '{rig.AssetId}' does not match requested ID '{expectedAssetId}'.");
        var report = _validator.Validate(rig);
        if (!report.IsValid)
            throw new InvalidDataException("Rig asset failed strict validation: " + string.Join(", ", report.Diagnostics.Where(item => item.Severity == RekallAgeRigDiagnosticSeverity.Error).Select(item => item.Code).Distinct(StringComparer.Ordinal)));
    }

    private static void ValidateAssetId(string assetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        if (assetId.Length > 128 || assetId is "." or ".." || !char.IsAsciiLetterOrDigit(assetId[0]) || assetId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
            throw new ArgumentException("Rig asset ID must be a safe 1-128 character logical identifier using ASCII letters, digits, '.', '-', or '_'.", nameof(assetId));
    }
}
