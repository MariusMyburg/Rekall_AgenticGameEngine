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
        return new(ResolveRuntimePaths(projectRoot, catalog), snapshot.Revision);
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
            Serialize(projectRoot, catalog),
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
            Serialize(projectRoot, catalog),
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

    private static string Serialize(string projectRoot, RekallAgeAssetCatalogDocument catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return JsonSerializer.Serialize(ToPortablePaths(projectRoot, catalog), JsonOptions) + Environment.NewLine;
    }

    private static RekallAgeAssetCatalogDocument ResolveRuntimePaths(
        string projectRoot,
        RekallAgeAssetCatalogDocument catalog) =>
        new(catalog.Assets.Select(asset => asset with
        {
            SourcePath = IsRemoteUri(asset.SourcePath)
                ? asset.SourcePath
                : ResolveLocalPath(projectRoot, asset.SourcePath),
            ImportedPath = ResolveLocalPath(projectRoot, asset.ImportedPath)
        }).ToArray());

    private static RekallAgeAssetCatalogDocument ToPortablePaths(
        string projectRoot,
        RekallAgeAssetCatalogDocument catalog) =>
        new(catalog.Assets.Select(asset =>
        {
            var importedIsPortable = TryProjectRelativePath(
                projectRoot,
                asset.ImportedPath,
                out var relativeImported);
            var importedPath = importedIsPortable
                ? relativeImported
                : asset.ImportedPath;
            var sourcePath = IsRemoteUri(asset.SourcePath)
                ? asset.SourcePath
                : TryProjectRelativePath(projectRoot, asset.SourcePath, out var relativeSource)
                    ? relativeSource
                    : importedIsPortable
                        ? importedPath
                        : asset.SourcePath;
            return asset with { SourcePath = sourcePath, ImportedPath = importedPath };
        }).ToArray());

    private static string ResolveLocalPath(string projectRoot, string storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
        {
            return storedPath;
        }

        try
        {
            if (Path.IsPathFullyQualified(storedPath))
            {
                var fullStoredPath = Path.GetFullPath(storedPath);
                return TryResolveRelocatedPath(projectRoot, fullStoredPath, out var relocatedPath)
                    ? relocatedPath
                    : fullStoredPath;
            }

            var root = NormalizeRoot(projectRoot);
            var resolved = Path.GetFullPath(Path.Combine(root, storedPath));
            return IsInside(root, resolved) ? resolved : storedPath;
        }
        catch (ArgumentException)
        {
            return storedPath;
        }
        catch (NotSupportedException)
        {
            return storedPath;
        }
    }

    private static bool TryResolveRelocatedPath(
        string projectRoot,
        string storedPath,
        out string relocatedPath)
    {
        var root = NormalizeRoot(projectRoot);
        if (IsInside(root, storedPath) && File.Exists(storedPath))
        {
            relocatedPath = storedPath;
            return true;
        }

        var projectName = Path.GetFileName(root);
        var segments = storedPath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        for (var index = segments.Length - 2; index >= 0; index--)
        {
            if (!segments[index].Equals(projectName, PathComparison))
            {
                continue;
            }

            var candidate = Path.GetFullPath(Path.Combine(root, Path.Combine(segments[(index + 1)..])));
            if (IsInside(root, candidate) && File.Exists(candidate))
            {
                relocatedPath = candidate;
                return true;
            }
        }

        relocatedPath = string.Empty;
        return false;
    }

    private static bool TryProjectRelativePath(string projectRoot, string path, out string relative)
    {
        relative = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var root = NormalizeRoot(projectRoot);
            var fullPath = Path.GetFullPath(Path.IsPathFullyQualified(path) ? path : Path.Combine(root, path));
            if (!IsInside(root, fullPath))
            {
                return false;
            }

            relative = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static string NormalizeRoot(string projectRoot) =>
        Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsInside(string root, string path) =>
        path.Equals(root, PathComparison)
        || path.StartsWith(root + Path.DirectorySeparatorChar, PathComparison);

    private static bool IsRemoteUri(string path) =>
        Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile;

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
