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
    private readonly RekallAgeCompiledMeshAssetResolver _resolver = new();

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
            : new(new(resolved.FileRevision!, resolved.Snapshot, ToGeometry(resolved.Snapshot)));
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
}
