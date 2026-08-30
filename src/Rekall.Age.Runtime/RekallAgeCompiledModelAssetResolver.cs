using System.Collections.Concurrent;
using System.Text.Json;
using Rekall.Age.Assets;
using Rekall.Age.AssetPipeline;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Runtime;

/// <summary>
/// Resolves a <c>Rekall.ModelAssetReference</c>'s <c>assetId</c> to the compiled mesh geometry a
/// published Model Asset's last successful build wrote, for the runtime systems (physics, rendering)
/// that need actual triangle data rather than the asset's own bookkeeping document. Mirrors
/// <see cref="Rekall.Age.Modeling.RekallAgeCompiledMeshAssetResolver"/>'s synchronous, cached shape so
/// callers (<see cref="RekallAgeBepuPhysicsSystem"/>, <see cref="Rekall.Age.Rendering.RekallAgeRuntimeRenderFrameBuilder"/>)
/// can resolve either kind of asset reference the same way. Before this resolver existed, an entity
/// carrying only <c>Rekall.ModelAssetReference</c> (the placement shape <c>rekall.scene.instantiate_asset</c>
/// actually produces) rendered as an unresolved fallback shape and was invisible to
/// <c>Rekall.MeshCollider</c> physics -- confirmed empirically by publishing and placing a real Model
/// Asset and capturing the resulting frame (Asset-backed: 0, Fallback: 1) rather than assumed from
/// source inspection alone.
/// </summary>
public sealed record RekallAgeCompiledModelAssetResolution(
    string? Revision,
    RekallAgeCompiledMeshSnapshot? Snapshot,
    string? IssueCode = null,
    string? IssueMessage = null);

public sealed class RekallAgeCompiledModelAssetResolver
{
    private readonly RekallAgeModelAssetStore _assetStore = new();
    private readonly RekallAgePublishedModelOutputStore _outputStore = new();
    private readonly RekallAgeMeshAssetStore _meshStore = new();
    private readonly RekallAgeMeshCompiler _meshCompiler = new();
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    public RekallAgeCompiledModelAssetResolution Resolve(string? projectRoot, string? modelAssetId)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return new(null, null, "REKALL_MODEL_PROJECT_ROOT_MISSING", "Model Asset resolution requires the runtime project root.");
        }
        if (string.IsNullOrWhiteSpace(modelAssetId))
        {
            return new(null, null, "REKALL_MODEL_ASSET_ID_MISSING", "Model Asset reference must provide assetId.");
        }

        var normalizedAssetId = modelAssetId.Trim();
        string path;
        try
        {
            path = _assetStore.GetModelPath(projectRoot, normalizedAssetId);
        }
        catch (ArgumentException exception)
        {
            return new(null, null, "REKALL_MODEL_ASSET_ID_INVALID", exception.Message);
        }

        if (!File.Exists(path))
        {
            return new(null, null, "REKALL_MODEL_ASSET_NOT_FOUND", $"Model Asset '{normalizedAssetId}' was not found.");
        }

        try
        {
            var info = new FileInfo(path);
            var key = Path.GetFullPath(path);
            if (!_cache.TryGetValue(key, out var cached)
                || cached.Length != info.Length
                || cached.LastWriteUtc != info.LastWriteTimeUtc)
            {
                var loaded = _assetStore.LoadVersionedAsync(projectRoot, normalizedAssetId, CancellationToken.None)
                    .AsTask().GetAwaiter().GetResult();
                var build = loaded.Value.LastSuccessfulBuild;
                if (build is null)
                {
                    return new(loaded.Revision, null, "REKALL_MODEL_ASSET_NOT_BUILT", $"Model Asset '{normalizedAssetId}' has no successful build to resolve.");
                }

                RekallAgeCompiledMeshSnapshot snapshot;
                try
                {
                    snapshot = _outputStore.LoadAsync(
                            projectRoot, normalizedAssetId, build.CompiledContentHash, CancellationToken.None)
                        .AsTask().GetAwaiter().GetResult();
                }
                catch (FileNotFoundException)
                {
                    var source = _meshStore.LoadVersionedAsync(
                            projectRoot, loaded.Value.Source.AssetId, CancellationToken.None)
                        .AsTask().GetAwaiter().GetResult();
                    if (!source.Revision.Equals(build.SourceFileRevision, StringComparison.Ordinal)
                        || source.Value.Revision != build.SourceLogicalRevision)
                    {
                        return new(
                            loaded.Revision,
                            null,
                            "REKALL_MODEL_ASSET_REBUILD_REQUIRED",
                            $"Model Asset '{normalizedAssetId}' has no compiled output and its editable source has changed; rebuild the Model Asset.");
                    }

                    snapshot = _meshCompiler.Compile(source.Value);
                }
                info.Refresh();
                cached = new CacheEntry(info.Length, info.LastWriteTimeUtc, loaded.Revision, snapshot);
                _cache[key] = cached;
            }

            return new(cached.Revision, cached.Snapshot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or JsonException or InvalidOperationException or InvalidDataException)
        {
            return new(null, null, "REKALL_MODEL_ASSET_LOAD_FAILED", $"Model Asset '{normalizedAssetId}' could not be loaded: {exception.Message}");
        }
    }

    private sealed record CacheEntry(
        long Length,
        DateTime LastWriteUtc,
        string Revision,
        RekallAgeCompiledMeshSnapshot Snapshot);
}
