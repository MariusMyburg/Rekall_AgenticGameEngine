using System.Collections.Concurrent;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Rendering;

public sealed record RekallAgeResolvedCompiledMesh(
    string FileRevision,
    RekallAgeCompiledMeshSnapshot Snapshot,
    RekallAgeRuntimeViewportGeometryMesh Geometry);

public sealed record RekallAgeCompiledMeshResolution(
    RekallAgeResolvedCompiledMesh? Mesh,
    string? IssueCode = null,
    string? IssueMessage = null);

public sealed class RekallAgeCompiledMeshResolver
{
    private readonly RekallAgeMeshAssetStore _store = new();
    private readonly RekallAgeMeshCompiler _compiler = new();
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    public RekallAgeCompiledMeshResolution Resolve(
        string? projectRoot,
        RekallAgeRuntimeComponent? reference)
    {
        if (reference is null)
        {
            return new(null);
        }
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return new(null, "REKALL_MESH_PROJECT_ROOT_MISSING", "Mesh asset resolution requires the runtime project root.");
        }
        var assetId = ReadString(reference, "assetId");
        if (string.IsNullOrWhiteSpace(assetId))
        {
            return new(null, "REKALL_MESH_ASSET_ID_MISSING", "Mesh asset reference must provide assetId.");
        }
        var path = _store.GetMeshPath(projectRoot, assetId);
        if (!File.Exists(path))
        {
            return new(null, "REKALL_MESH_ASSET_NOT_FOUND", $"Editable mesh asset '{assetId}' was not found.");
        }
        try
        {
            var info = new FileInfo(path);
            var key = Path.GetFullPath(path);
            if (!_cache.TryGetValue(key, out var cached)
                || cached.Length != info.Length
                || cached.LastWriteUtc != info.LastWriteTimeUtc)
            {
                var loaded = _store.LoadVersionedAsync(projectRoot, assetId, CancellationToken.None)
                    .AsTask().GetAwaiter().GetResult();
                var snapshot = _compiler.Compile(loaded.Value);
                info.Refresh();
                cached = new CacheEntry(
                    info.Length,
                    info.LastWriteTimeUtc,
                    new(loaded.Revision, snapshot, ToGeometry(snapshot)));
                _cache[key] = cached;
            }
            var expectedRevision = ReadString(reference, "expectedRevision");
            if (!string.IsNullOrWhiteSpace(expectedRevision)
                && !expectedRevision.Equals(cached.Mesh.FileRevision, StringComparison.OrdinalIgnoreCase))
            {
                return new(
                    null,
                    "REKALL_MESH_REVISION_MISMATCH",
                    $"Editable mesh asset '{assetId}' revision does not match expectedRevision.");
            }
            return new(cached.Mesh);
        }
        catch (RekallAgeMeshCompileException exception)
        {
            return new(null, exception.Code, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return new(null, "REKALL_MESH_ASSET_LOAD_FAILED", $"Editable mesh asset '{assetId}' could not be loaded: {exception.Message}");
        }
    }

    private static RekallAgeRuntimeViewportGeometryMesh ToGeometry(RekallAgeCompiledMeshSnapshot snapshot) =>
        new(
            snapshot.Vertices.Select(vertex => new RekallAgeRuntimeViewportGeometryVertex(
                vertex.Position.X,
                vertex.Position.Y,
                vertex.Position.Z,
                vertex.Normal.X,
                vertex.Normal.Y,
                vertex.Normal.Z,
                vertex.Color.X,
                vertex.Color.Y,
                vertex.Color.Z,
                vertex.Color.W,
                vertex.Uv.X,
                vertex.Uv.Y)).ToArray(),
            snapshot.Indices,
            snapshot.Triangles.Select(triangle => new RekallAgeRuntimeViewportTriangleProvenance(
                triangle.TriangleIndex,
                triangle.SourceFaceId,
                triangle.SourceCornerIds,
                triangle.SourcePointIds,
                triangle.SurfaceIndex)).ToArray());

    private static string? ReadString(RekallAgeRuntimeComponent component, string name)
    {
        var node = component.Properties.FirstOrDefault(property =>
            property.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
        return node is System.Text.Json.Nodes.JsonValue value && value.TryGetValue<string>(out var text)
            ? text?.Trim()
            : null;
    }

    private sealed record CacheEntry(
        long Length,
        DateTime LastWriteUtc,
        RekallAgeResolvedCompiledMesh Mesh);
}
