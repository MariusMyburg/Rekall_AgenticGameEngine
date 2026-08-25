using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime.Abstractions;
using System.Runtime.CompilerServices;

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
    private readonly RekallAgeCompiledMeshAssetResolver _resolver = new();
    private readonly Rekall.Age.Runtime.RekallAgeCompiledModelAssetResolver _modelAssetResolver = new();
    private readonly ConditionalWeakTable<RekallAgeCompiledMeshSnapshot, RekallAgeRuntimeViewportGeometryMesh> _geometryCache = new();

    public RekallAgeCompiledMeshResolution Resolve(
        string? projectRoot,
        RekallAgeRuntimeComponent? reference)
    {
        if (reference is null)
        {
            return new(null);
        }
        var assetId = ReadString(reference, "assetId");
        var resolved = _resolver.Resolve(projectRoot, assetId, ReadString(reference, "expectedRevision"));
        return resolved.Snapshot is null
            ? new(null, resolved.IssueCode, resolved.IssueMessage)
            : new(new(resolved.FileRevision!, resolved.Snapshot, ResolveGeometry(resolved.Snapshot)));
    }

    /// <summary>
    /// Resolves a <c>Rekall.ModelAssetReference</c> (the published Model Asset placement shape
    /// <c>rekall.scene.instantiate_asset</c> produces) the same way <see cref="Resolve"/> resolves the
    /// older <c>Rekall.MeshAssetReference</c>. Before this existed, a placed Model Asset's bare
    /// <c>Rekall.MeshRenderer</c> component had no way to reach the compiled geometry its own sibling
    /// <c>Rekall.ModelAssetReference</c> named, and rendered as an unresolved fallback shape --
    /// confirmed by capturing an actual placed Model Asset before this fix (Asset-backed: 0,
    /// Fallback: 1).
    /// </summary>
    public RekallAgeCompiledMeshResolution ResolveModelAsset(
        string? projectRoot,
        RekallAgeRuntimeComponent? reference)
    {
        if (reference is null)
        {
            return new(null);
        }
        var assetId = ReadString(reference, "assetId");
        var resolved = _modelAssetResolver.Resolve(projectRoot, assetId);
        return resolved.Snapshot is null
            ? new(null, resolved.IssueCode, resolved.IssueMessage)
            : new(new(resolved.Revision!, resolved.Snapshot, ResolveGeometry(resolved.Snapshot)));
    }

    private RekallAgeRuntimeViewportGeometryMesh ResolveGeometry(RekallAgeCompiledMeshSnapshot snapshot) =>
        _geometryCache.GetValue(snapshot, static value => ToGeometry(value));

    private static RekallAgeRuntimeViewportGeometryMesh ToGeometry(RekallAgeCompiledMeshSnapshot snapshot) =>
        new(
            snapshot.Vertices.Select(vertex => new RekallAgeRuntimeViewportGeometryVertex(
                vertex.Position.X,
                vertex.Position.Y,
                vertex.Position.Z,
                vertex.Normal.X,
                vertex.Normal.Y,
                vertex.Normal.Z,
                snapshot.HasVertexColors ? vertex.Color.X : double.NaN,
                snapshot.HasVertexColors ? vertex.Color.Y : double.NaN,
                snapshot.HasVertexColors ? vertex.Color.Z : double.NaN,
                snapshot.HasVertexColors ? vertex.Color.W : double.NaN,
                vertex.Uv.X,
                vertex.Uv.Y)).ToArray(),
            snapshot.Indices,
            snapshot.Triangles.Select(triangle => new RekallAgeRuntimeViewportTriangleProvenance(
                triangle.TriangleIndex,
                triangle.SourceFaceId,
                triangle.SourceCornerIds,
                triangle.SourcePointIds,
                triangle.SurfaceIndex)).ToArray(),
            snapshot.Surfaces.Select(surface => new RekallAgeRuntimeViewportGeometrySurface(
                surface.SurfaceIndex,
                surface.MaterialSlotIndex,
                surface.MaterialAssetId,
                surface.FirstIndex,
                surface.IndexCount,
                surface.SourceFaceIds)).ToArray());

    private static string? ReadString(RekallAgeRuntimeComponent component, string name)
    {
        var node = component.Properties.FirstOrDefault(property =>
            property.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
        return node is System.Text.Json.Nodes.JsonValue value && value.TryGetValue<string>(out var text)
            ? text?.Trim()
            : null;
    }
}
