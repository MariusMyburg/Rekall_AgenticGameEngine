using System.Text.Json;
using Rekall.Age.Core.Compatibility;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed class RekallAgeCurveAssetStore
{
    private const string FileSuffix = ".age.curve.json";
    private static readonly JsonSerializerOptions JsonOptions = new(RekallAgeModelingJson.Options)
    {
        MaxDepth = RekallAgeDocumentSchemaProbe.MaximumDocumentDepth
    };
    private readonly RekallAgeCurveValidator _validator = new();

    public string GetCurvePath(string projectRoot, string assetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ValidateAssetId(assetId);
        return Path.Combine(Path.GetFullPath(projectRoot), "Modeling", "Curves", assetId + FileSuffix);
    }

    public string GetRecoveryPath(string projectRoot, string assetId) =>
        RekallAgeDocumentRecoveryStore.GetPreviousPath(projectRoot, GetCurvePath(projectRoot, assetId));

    public async ValueTask SaveAsync(string projectRoot, RekallAgeCurveAsset curve, CancellationToken cancellationToken)
    {
        ValidateForPersistence(curve, curve.AssetId);
        await RekallAgeAtomicFile.WriteAllTextAsync(GetCurvePath(projectRoot, curve.AssetId), Serialize(curve), RekallAgeDocumentSchemaProbe.MaximumDocumentBytes, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<string> SaveIfRevisionAsync(string projectRoot, RekallAgeCurveAsset curve, string expectedRevision, CancellationToken cancellationToken)
    {
        ValidateForPersistence(curve, curve.AssetId);
        var path = GetCurvePath(projectRoot, curve.AssetId);
        if (File.Exists(path))
        {
            var current = await LoadVersionedAsync(projectRoot, curve.AssetId, cancellationToken).ConfigureAwait(false);
            if (string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal) && curve.Revision != current.Value.Revision + 1)
                throw new InvalidDataException($"REKALL_CURVE_LOGICAL_REVISION_INVALID: Curve revision must advance from {current.Value.Revision} to {current.Value.Revision + 1}.");
        }
        else if (curve.Revision != 1)
            throw new InvalidDataException("REKALL_CURVE_LOGICAL_REVISION_INVALID: A new curve asset must start at revision 1.");
        return await RekallAgeAtomicFile.WriteAllTextIfRevisionAsync(path, Serialize(curve), RekallAgeDocumentSchemaProbe.MaximumDocumentBytes, expectedRevision, GetRecoveryPath(projectRoot, curve.AssetId), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<RekallAgeCurveAsset> LoadAsync(string projectRoot, string assetId, CancellationToken cancellationToken) =>
        (await LoadVersionedAsync(projectRoot, assetId, cancellationToken).ConfigureAwait(false)).Value;

    public async ValueTask<RekallAgeVersionedDocument<RekallAgeCurveAsset>> LoadVersionedAsync(string projectRoot, string assetId, CancellationToken cancellationToken)
    {
        var snapshot = await RekallAgeDocumentSchemaProbe.ReadSnapshotAsync(GetCurvePath(projectRoot, assetId), "curve asset", RekallAgeCurveAsset.CurrentSchemaVersion, cancellationToken).ConfigureAwait(false);
        var curve = snapshot.Deserialize<RekallAgeCurveAsset>(JsonOptions);
        ValidateForPersistence(curve, assetId);
        return new(curve with { SchemaVersion = RekallAgeCurveAsset.CurrentSchemaVersion }, snapshot.File.Revision);
    }

    public IReadOnlyList<string> ListAssetIds(string projectRoot)
    {
        var directory = Path.Combine(Path.GetFullPath(projectRoot), "Modeling", "Curves");
        return !Directory.Exists(directory) ? [] : Directory.EnumerateFiles(directory, "*" + FileSuffix).Select(Path.GetFileName).Where(name => name is not null).Select(name => name![..^FileSuffix.Length]).Order(StringComparer.Ordinal).ToArray();
    }

    public string Serialize(RekallAgeCurveAsset curve)
    {
        ArgumentNullException.ThrowIfNull(curve);
        return JsonSerializer.Serialize(curve with { SchemaVersion = RekallAgeCurveAsset.CurrentSchemaVersion }, JsonOptions) + Environment.NewLine;
    }

    private void ValidateForPersistence(RekallAgeCurveAsset curve, string expectedAssetId)
    {
        ArgumentNullException.ThrowIfNull(curve);
        ValidateAssetId(curve.AssetId);
        if (!string.Equals(curve.AssetId, expectedAssetId, StringComparison.Ordinal))
            throw new InvalidDataException($"REKALL_CURVE_ASSET_ID_MISMATCH: Document asset ID '{curve.AssetId}' does not match requested ID '{expectedAssetId}'.");
        var report = _validator.Validate(curve);
        if (!report.IsValid) throw new InvalidDataException("Curve asset failed strict validation: " + string.Join(", ", report.Diagnostics.Where(item => item.Severity == RekallAgeCurveDiagnosticSeverity.Error).Select(item => item.Code).Distinct(StringComparer.Ordinal)));
    }

    private static void ValidateAssetId(string assetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        if (assetId.Length > 128 || assetId is "." or ".." || !char.IsAsciiLetterOrDigit(assetId[0]) || assetId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
            throw new ArgumentException("Curve asset ID must be a safe 1-128 character logical identifier using ASCII letters, digits, '.', '-', or '_'.", nameof(assetId));
    }
}
