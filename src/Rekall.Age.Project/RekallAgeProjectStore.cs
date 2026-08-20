using System.Text.Json;
using Rekall.Age.Core.Compatibility;
using Rekall.Age.Core.Product;
using Rekall.Age.Core.Persistence;

namespace Rekall.Age.Project;

public sealed class RekallAgeProjectStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = RekallAgeDocumentSchemaProbe.MaximumDocumentDepth
    };

    public const string ManifestFileName = "rekall.project.json";

    public string GetRecoveryPath(string projectRoot) =>
        RekallAgeDocumentRecoveryStore.GetPreviousPath(
            projectRoot,
            Path.Combine(projectRoot, ManifestFileName));

    public string GetQuarantineDirectory(string projectRoot) =>
        RekallAgeDocumentRecoveryStore.GetQuarantineDirectory(
            projectRoot,
            Path.Combine(projectRoot, ManifestFileName));

    public async ValueTask SaveAsync(
        string projectRoot,
        RekallAgeProjectManifest manifest,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(projectRoot);
        var path = Path.Combine(projectRoot, ManifestFileName);
        await RekallAgeAtomicFile.WriteAllTextAsync(
            path,
            Serialize(manifest),
            RekallAgeDocumentSchemaProbe.MaximumDocumentBytes,
            cancellationToken);
    }

    public async ValueTask<string> SaveIfRevisionAsync(
        string projectRoot,
        RekallAgeProjectManifest manifest,
        string expectedRevision,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(projectRoot);
        return await RekallAgeAtomicFile.WriteAllTextIfRevisionAsync(
            Path.Combine(projectRoot, ManifestFileName),
            Serialize(manifest),
            RekallAgeDocumentSchemaProbe.MaximumDocumentBytes,
            expectedRevision,
            GetRecoveryPath(projectRoot),
            cancellationToken);
    }

    public ValueTask<RekallAgeDocumentRecoveryInspection> InspectRecoveryAsync(
        string projectRoot,
        CancellationToken cancellationToken) =>
        RekallAgeDocumentRecoveryStore.InspectAsync(
            projectRoot,
            Path.Combine(projectRoot, ManifestFileName),
            "project",
            RekallAgeProductInfo.Current.ProjectSchemaVersion,
            ValidateSnapshot,
            cancellationToken);

    public async ValueTask<RekallAgeVersionedDocument<RekallAgeProjectManifest>> RestorePreviousAsync(
        string projectRoot,
        string expectedRevision,
        CancellationToken cancellationToken)
    {
        await RekallAgeDocumentRecoveryStore.RestorePreviousAsync(
            projectRoot,
            Path.Combine(projectRoot, ManifestFileName),
            "project",
            RekallAgeProductInfo.Current.ProjectSchemaVersion,
            expectedRevision,
            ValidateSnapshot,
            cancellationToken).ConfigureAwait(false);
        return await LoadVersionedAsync(projectRoot, cancellationToken).ConfigureAwait(false);
    }

    public string Serialize(RekallAgeProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var current = manifest with { SchemaVersion = RekallAgeProductInfo.Current.ProjectSchemaVersion };
        return JsonSerializer.Serialize(current, JsonOptions) + Environment.NewLine;
    }

    public async ValueTask<RekallAgeProjectManifest> LoadAsync(
        string projectRoot,
        CancellationToken cancellationToken) =>
        (await LoadVersionedAsync(projectRoot, cancellationToken).ConfigureAwait(false)).Value;

    public async ValueTask<RekallAgeVersionedDocument<RekallAgeProjectManifest>> LoadVersionedAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(projectRoot, ManifestFileName);
        var snapshot = await RekallAgeDocumentSchemaProbe.ReadSnapshotAsync(
            path,
            "project",
            RekallAgeProductInfo.Current.ProjectSchemaVersion,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var manifest = snapshot.Deserialize<RekallAgeProjectManifest>(JsonOptions);
        return new RekallAgeVersionedDocument<RekallAgeProjectManifest>(manifest with
        {
            SchemaVersion = RekallAgeProductInfo.Current.ProjectSchemaVersion
        }, snapshot.File.Revision);
    }

    private static void ValidateSnapshot(RekallAgeDocumentSnapshot snapshot)
    {
        var manifest = snapshot.Deserialize<RekallAgeProjectManifest>(JsonOptions);
        if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.Capabilities is null)
        {
            throw new InvalidDataException($"Project document '{snapshot.File.Path}' has an invalid required shape.");
        }
    }
}
