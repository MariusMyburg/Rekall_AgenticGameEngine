using System.Collections.Concurrent;
using System.Text.Json;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed record RekallAgeCompiledMeshAssetResolution(
    string? FileRevision,
    RekallAgeCompiledMeshSnapshot? Snapshot,
    string? IssueCode = null,
    string? IssueMessage = null);

public sealed class RekallAgeCompiledMeshAssetResolver
{
    private readonly RekallAgeMeshAssetStore _store = new();
    private readonly RekallAgeMeshCompiler _compiler = new();
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    public RekallAgeCompiledMeshAssetResolution Resolve(
        string? projectRoot,
        string? assetId,
        string? expectedRevision = null)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return new(null, null, "REKALL_MESH_PROJECT_ROOT_MISSING", "Mesh asset resolution requires the runtime project root.");
        }
        if (string.IsNullOrWhiteSpace(assetId))
        {
            return new(null, null, "REKALL_MESH_ASSET_ID_MISSING", "Mesh asset reference must provide assetId.");
        }
        var normalizedAssetId = assetId.Trim();
        var path = _store.GetMeshPath(projectRoot, normalizedAssetId);
        if (!File.Exists(path))
        {
            return new(null, null, "REKALL_MESH_ASSET_NOT_FOUND", $"Editable mesh asset '{normalizedAssetId}' was not found.");
        }
        try
        {
            var info = new FileInfo(path);
            var key = Path.GetFullPath(path);
            if (!_cache.TryGetValue(key, out var cached)
                || cached.Length != info.Length
                || cached.LastWriteUtc != info.LastWriteTimeUtc)
            {
                var loaded = _store.LoadVersionedAsync(projectRoot, normalizedAssetId, CancellationToken.None)
                    .AsTask().GetAwaiter().GetResult();
                var snapshot = _compiler.Compile(loaded.Value);
                info.Refresh();
                cached = new CacheEntry(info.Length, info.LastWriteTimeUtc, loaded.Revision, snapshot);
                _cache[key] = cached;
            }
            if (!string.IsNullOrWhiteSpace(expectedRevision)
                && !expectedRevision.Equals(cached.FileRevision, StringComparison.OrdinalIgnoreCase))
            {
                return new(
                    cached.FileRevision,
                    null,
                    "REKALL_MESH_REVISION_MISMATCH",
                    $"Editable mesh asset '{normalizedAssetId}' revision does not match expectedRevision.");
            }
            return new(cached.FileRevision, cached.Snapshot);
        }
        catch (RekallAgeMeshCompileException exception)
        {
            return new(null, null, exception.Code, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return new(null, null, "REKALL_MESH_ASSET_LOAD_FAILED", $"Editable mesh asset '{normalizedAssetId}' could not be loaded: {exception.Message}");
        }
    }

    private sealed record CacheEntry(
        long Length,
        DateTime LastWriteUtc,
        string FileRevision,
        RekallAgeCompiledMeshSnapshot Snapshot);
}
