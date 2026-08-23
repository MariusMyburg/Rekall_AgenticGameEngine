using System.Text.Json;
using Rekall.Age.Core.Persistence;

namespace Rekall.Age.Assets;

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

    public async ValueTask<RekallAgeVersionedDocument<RekallAgeAssetCatalogDocument>> MutateAsync(
        string projectRoot,
        Func<RekallAgeAssetCatalogDocument, CancellationToken, ValueTask<RekallAgeAssetCatalogDocument>> mutation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        for (var attempt = 1; attempt <= MaximumMutationAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var loaded = await LoadVersionedAsync(projectRoot, cancellationToken).ConfigureAwait(false);
            var updated = await mutation(loaded.Value, cancellationToken).ConfigureAwait(false)
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
            catch (RekallAgeDocumentRevisionException) when (attempt < MaximumMutationAttempts)
            {
                // Reload and replay the semantic mutation against the winner.
            }
        }

        throw new InvalidOperationException(
            $"Asset catalog remained contended for {MaximumMutationAttempts} mutation attempts.");
    }

    public ValueTask<RekallAgeVersionedDocument<RekallAgeAssetCatalogDocument>> AddOrReplaceAsync(
        string projectRoot,
        RekallAgeAssetDocument asset,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return MutateAsync(
            projectRoot,
            (catalog, _) => ValueTask.FromResult(catalog.AddOrReplace(asset)),
            cancellationToken);
    }

    private static string Serialize(RekallAgeAssetCatalogDocument catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return JsonSerializer.Serialize(catalog, JsonOptions) + Environment.NewLine;
    }
}
