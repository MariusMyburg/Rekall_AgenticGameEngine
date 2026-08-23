using System.Text.Json;
using Rekall.Age.Core.Compatibility;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed class RekallAgeMeshAssetStore
{
    private const string FileSuffix = ".age.mesh.json";
    private static readonly JsonSerializerOptions JsonOptions = new(RekallAgeModelingJson.Options)
    {
        MaxDepth = RekallAgeDocumentSchemaProbe.MaximumDocumentDepth
    };
    private readonly RekallAgeMeshValidator _validator = new();

    public string GetMeshPath(string projectRoot, string assetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ValidateAssetId(assetId);
        return Path.Combine(projectRoot, "Modeling", "Meshes", assetId + FileSuffix);
    }

    public string GetRecoveryPath(string projectRoot, string assetId) =>
        RekallAgeDocumentRecoveryStore.GetPreviousPath(projectRoot, GetMeshPath(projectRoot, assetId));

    public async ValueTask SaveAsync(
        string projectRoot,
        RekallAgeMeshAsset mesh,
        CancellationToken cancellationToken)
    {
        ValidateForPersistence(mesh, expectedAssetId: mesh.AssetId);
        await RekallAgeAtomicFile.WriteAllTextAsync(
            GetMeshPath(projectRoot, mesh.AssetId),
            Serialize(mesh),
            RekallAgeDocumentSchemaProbe.MaximumDocumentBytes,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<string> SaveIfRevisionAsync(
        string projectRoot,
        RekallAgeMeshAsset mesh,
        string expectedRevision,
        CancellationToken cancellationToken)
    {
        ValidateForPersistence(mesh, expectedAssetId: mesh.AssetId);
        var path = GetMeshPath(projectRoot, mesh.AssetId);
        if (File.Exists(path))
        {
            var current = await LoadVersionedAsync(projectRoot, mesh.AssetId, cancellationToken).ConfigureAwait(false);
            if (string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal)
                && mesh.Revision != current.Value.Revision + 1)
            {
                throw new InvalidDataException(
                    $"REKALL_MESH_LOGICAL_REVISION_INVALID: Mesh revision must advance from {current.Value.Revision} to {current.Value.Revision + 1}.");
            }
        }
        else if (mesh.Revision != 1)
        {
            throw new InvalidDataException("REKALL_MESH_LOGICAL_REVISION_INVALID: A new mesh asset must start at revision 1.");
        }

        return await RekallAgeAtomicFile.WriteAllTextIfRevisionAsync(
            path,
            Serialize(mesh),
            RekallAgeDocumentSchemaProbe.MaximumDocumentBytes,
            expectedRevision,
            GetRecoveryPath(projectRoot, mesh.AssetId),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<RekallAgeMeshAsset> LoadAsync(
        string projectRoot,
        string assetId,
        CancellationToken cancellationToken) =>
        (await LoadVersionedAsync(projectRoot, assetId, cancellationToken).ConfigureAwait(false)).Value;

    public async ValueTask<RekallAgeVersionedDocument<RekallAgeMeshAsset>> LoadVersionedAsync(
        string projectRoot,
        string assetId,
        CancellationToken cancellationToken)
    {
        var snapshot = await RekallAgeDocumentSchemaProbe.ReadSnapshotAsync(
            GetMeshPath(projectRoot, assetId),
            "mesh asset",
            RekallAgeMeshAsset.CurrentSchemaVersion,
            cancellationToken).ConfigureAwait(false);
        var mesh = DeserializeAndValidate(snapshot, assetId);
        return new RekallAgeVersionedDocument<RekallAgeMeshAsset>(mesh, snapshot.File.Revision);
    }

    public IReadOnlyList<string> ListAssetIds(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var directory = Path.Combine(projectRoot, "Modeling", "Meshes");
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "*" + FileSuffix)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name![..^FileSuffix.Length])
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    public string Serialize(RekallAgeMeshAsset mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var current = mesh with { SchemaVersion = RekallAgeMeshAsset.CurrentSchemaVersion };
        return JsonSerializer.Serialize(current, JsonOptions) + Environment.NewLine;
    }

    public ValueTask<RekallAgeDocumentRecoveryInspection> InspectRecoveryAsync(
        string projectRoot,
        string assetId,
        CancellationToken cancellationToken) =>
        RekallAgeDocumentRecoveryStore.InspectAsync(
            projectRoot,
            GetMeshPath(projectRoot, assetId),
            "mesh asset",
            RekallAgeMeshAsset.CurrentSchemaVersion,
            snapshot => _ = DeserializeAndValidate(snapshot, assetId),
            cancellationToken);

    public async ValueTask<RekallAgeVersionedDocument<RekallAgeMeshAsset>> RestorePreviousAsync(
        string projectRoot,
        string assetId,
        string expectedRevision,
        CancellationToken cancellationToken)
    {
        await RekallAgeDocumentRecoveryStore.RestorePreviousAsync(
            projectRoot,
            GetMeshPath(projectRoot, assetId),
            "mesh asset",
            RekallAgeMeshAsset.CurrentSchemaVersion,
            expectedRevision,
            snapshot => _ = DeserializeAndValidate(snapshot, assetId),
            cancellationToken).ConfigureAwait(false);
        return await LoadVersionedAsync(projectRoot, assetId, cancellationToken).ConfigureAwait(false);
    }

    private RekallAgeMeshAsset DeserializeAndValidate(RekallAgeDocumentSnapshot snapshot, string expectedAssetId)
    {
        var mesh = snapshot.Deserialize<RekallAgeMeshAsset>(JsonOptions);
        ValidateForPersistence(mesh, expectedAssetId);
        return mesh with { SchemaVersion = RekallAgeMeshAsset.CurrentSchemaVersion };
    }

    private void ValidateForPersistence(RekallAgeMeshAsset mesh, string expectedAssetId)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ValidateAssetId(mesh.AssetId);
        if (!string.Equals(mesh.AssetId, expectedAssetId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"REKALL_MESH_ASSET_ID_MISMATCH: Document asset ID '{mesh.AssetId}' does not match requested ID '{expectedAssetId}'.");
        }

        var report = _validator.Validate(mesh);
        if (!report.IsValid)
        {
            var errors = report.Diagnostics
                .Where(item => item.Severity == RekallAgeMeshDiagnosticSeverity.Error)
                .Select(item => item.Code)
                .Distinct(StringComparer.Ordinal);
            throw new InvalidDataException("Mesh asset failed strict validation: " + string.Join(", ", errors));
        }
    }

    private static void ValidateAssetId(string assetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        if (assetId.Length > 128
            || assetId is "." or ".."
            || !char.IsAsciiLetterOrDigit(assetId[0])
            || assetId.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '-' and not '_' and not '.'))
        {
            throw new ArgumentException(
                "Mesh asset ID must be a safe 1-128 character logical identifier using ASCII letters, digits, '.', '-', or '_'.",
                nameof(assetId));
        }
    }
}
