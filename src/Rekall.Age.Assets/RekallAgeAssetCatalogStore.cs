using System.Diagnostics;
using System.Text.Json;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Persistence;

namespace Rekall.Age.Assets;

public sealed class RekallAgeAssetCatalogBusyException : RekallAgeCodedBoundaryException
{
    public const string ErrorCode = "REKALL_ASSET_CATALOG_BUSY";

    public RekallAgeAssetCatalogBusyException(
        string path,
        int attempts,
        Exception innerException)
        : base(
            ErrorCode,
            $"Asset catalog '{path}' remained contended for {attempts} mutation attempts. Retry the semantic mutation against fresh catalog state.",
            path,
            innerException)
    {
        Path = path;
        Attempts = attempts;
    }

    public string Path { get; }

    public int Attempts { get; }
}

public sealed class RekallAgeAssetCatalogStore
{
    public const int MaximumMutationAttempts = 16;
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
        CancellationToken cancellationToken) =>
        (await LoadVersionedAsync(projectRoot, cancellationToken).ConfigureAwait(false)).Value;

    public async ValueTask<RekallAgeVersionedDocument<RekallAgeAssetCatalogDocument>> LoadVersionedAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var path = GetCatalogPath(projectRoot);
        if (!File.Exists(path))
        {
            return new(RekallAgeAssetCatalogDocument.Empty, RekallAgeDocumentRevision.Missing);
        }

        var snapshot = await RekallAgeBoundedFileSnapshot.ReadAsync(
            path,
            RekallAgePersistedJson.MaximumDocumentBytes,
            cancellationToken).ConfigureAwait(false);
        var catalog = JsonSerializer.Deserialize<RekallAgeAssetCatalogDocument>(snapshot.Bytes, JsonOptions)
            ?? throw new InvalidDataException($"Asset catalog '{path}' could not be deserialized.");
        return new(catalog, snapshot.Revision);
    }

    public async ValueTask SaveAsync(
        string projectRoot,
        RekallAgeAssetCatalogDocument catalog,
        CancellationToken cancellationToken)
    {
        var assetsRoot = Path.Combine(projectRoot, "Assets");
        Directory.CreateDirectory(assetsRoot);
        await RekallAgePersistedJson.WriteAllTextAsync(
            GetCatalogPath(projectRoot),
            Serialize(catalog),
            cancellationToken);
    }

    public async ValueTask<string> SaveIfRevisionAsync(
        string projectRoot,
        RekallAgeAssetCatalogDocument catalog,
        string expectedRevision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return await RekallAgeAtomicFile.WriteAllTextIfRevisionAsync(
            GetCatalogPath(projectRoot),
            Serialize(catalog),
            RekallAgePersistedJson.MaximumDocumentBytes,
            expectedRevision,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Replays a pure semantic catalog transform against fresh state after document revision
    /// conflicts. The transform must be deterministic and side-effect free because it may run
    /// up to <see cref="MaximumMutationAttempts"/> times.
    /// </summary>
    public async ValueTask<RekallAgeVersionedDocument<RekallAgeAssetCatalogDocument>> MutateAsync(
        string projectRoot,
        Func<RekallAgeAssetCatalogDocument, RekallAgeAssetCatalogDocument> mutation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        for (var attempt = 1; attempt <= MaximumMutationAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var loaded = await LoadVersionedAsync(projectRoot, cancellationToken).ConfigureAwait(false);
            var updated = mutation(loaded.Value)
                ?? throw new InvalidOperationException("Asset catalog mutation returned null.");
            try
            {
                var revision = await SaveIfRevisionAsync(
                    projectRoot,
                    updated,
                    loaded.Revision,
                    cancellationToken).ConfigureAwait(false);
                return new(updated, revision);
            }
            catch (RekallAgeDocumentRevisionException error) when (
                error.Code == "REKALL_DOCUMENT_REVISION_CONFLICT")
            {
                if (attempt == MaximumMutationAttempts)
                {
                    throw new RekallAgeAssetCatalogBusyException(
                        GetCatalogPath(projectRoot),
                        MaximumMutationAttempts,
                        error);
                }

                // Reload and replay the pure semantic mutation against the winner.
            }
        }

        throw new UnreachableException();
    }

    public ValueTask<RekallAgeVersionedDocument<RekallAgeAssetCatalogDocument>> AddOrReplaceAsync(
        string projectRoot,
        RekallAgeAssetDocument asset,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return MutateAsync(
            projectRoot,
            catalog => catalog.AddOrReplace(asset),
            cancellationToken);
    }

    private static string Serialize(RekallAgeAssetCatalogDocument catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return JsonSerializer.Serialize(catalog, JsonOptions) + Environment.NewLine;
    }
}
