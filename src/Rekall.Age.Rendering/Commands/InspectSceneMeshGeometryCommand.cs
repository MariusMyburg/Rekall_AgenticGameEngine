using System.Numerics;
using Rekall.Age.Core.Commands;
using Rekall.Age.Runtime;

namespace Rekall.Age.Rendering.Commands;

public sealed record InspectSceneMeshGeometryRequest(
    string ProjectRoot,
    string SceneName,
    int Frames = 0,
    int Width = 320,
    int Height = 180);

public sealed record InspectSceneMeshGeometryResult(
    string SceneName,
    int FrameIndex,
    int MeshCount,
    long VertexCount,
    long IndexCount,
    long TriangleCount,
    IReadOnlyList<InspectSceneMeshGeometryItem> Meshes,
    bool Truncated,
    IReadOnlyList<RekallAgeRuntimeViewportAssetIssue> AssetIssues,
    bool AssetIssuesTruncated);

public sealed record InspectSceneMeshGeometryItem(
    string EntityId,
    string EntityName,
    int MeshIndex,
    string Primitive,
    int VertexCount,
    int IndexCount,
    int TriangleCount,
    Vector3 Minimum,
    Vector3 Maximum,
    int MorphTargetCount,
    string MorphWeightSource);

public sealed class InspectSceneMeshGeometryCommand
    : IRekallAgeCommand<InspectSceneMeshGeometryRequest, InspectSceneMeshGeometryResult>
{
    private const int MaximumMeshes = 256;

    public string Name => "rekall.render.inspect_scene_mesh_geometry";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Inspects bounded post-morph, post-skin scene mesh geometry using the same CPU mesh preparation path as Vulkan; reports counts and finite bounds without dumping vertices.",
        typeof(InspectSceneMeshGeometryRequest).FullName!,
        typeof(InspectSceneMeshGeometryResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<InspectSceneMeshGeometryResult>> ExecuteAsync(
        InspectSceneMeshGeometryRequest request,
        RekallAgeCommandContext context)
    {
        if (request.Frames < 0 || request.Width <= 0 || request.Height <= 0)
        {
            return RekallAgeCommandResult<InspectSceneMeshGeometryResult>.Failure(
                Empty(request),
                "Mesh geometry inspection requires non-negative frames and positive dimensions.",
                [new("REKALL_RENDER_MESH_INSPECTION_INVALID", "Frames must be non-negative and dimensions positive.", request.SceneName)]);
        }
        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            request.ProjectRoot, request.SceneName, request.Frames, context.CancellationToken);
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, request.Width, request.Height, false);
        var assets = await new RekallAgeRuntimeViewportAssetResolver().ResolveAsync(
            request.ProjectRoot, frame, context.CancellationToken);
        var meshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame, assets);
        var summaries = meshes.Select((mesh, index) => Summarize(mesh, index))
            .OrderBy(item => item.EntityName, StringComparer.Ordinal)
            .ThenBy(item => item.EntityId, StringComparer.Ordinal)
            .ThenBy(item => item.MeshIndex)
            .Take(MaximumMeshes)
            .ToArray();
        var result = new InspectSceneMeshGeometryResult(
            frame.SceneName,
            frame.FrameIndex,
            meshes.Count,
            meshes.Sum(mesh => (long)mesh.Vertices.Count),
            meshes.Sum(mesh => (long)mesh.Indices.Count),
            meshes.Sum(mesh => (long)mesh.Indices.Count / 3),
            summaries,
            meshes.Count > MaximumMeshes,
            assets.Issues.Take(MaximumMeshes).ToArray(),
            assets.Issues.Count > MaximumMeshes);
        return RekallAgeCommandResult<InspectSceneMeshGeometryResult>.Success(
            result,
            $"Scene '{request.SceneName}' prepared {result.MeshCount} mesh(es) with {result.VertexCount} vertices.");
    }

    private static InspectSceneMeshGeometryItem Summarize(RekallAgeVulkanSceneMesh mesh, int index)
    {
        var minimum = mesh.Vertices.Count == 0 ? Vector3.Zero : new Vector3(
            mesh.Vertices.Min(vertex => vertex.X), mesh.Vertices.Min(vertex => vertex.Y), mesh.Vertices.Min(vertex => vertex.Z));
        var maximum = mesh.Vertices.Count == 0 ? Vector3.Zero : new Vector3(
            mesh.Vertices.Max(vertex => vertex.X), mesh.Vertices.Max(vertex => vertex.Y), mesh.Vertices.Max(vertex => vertex.Z));
        return new InspectSceneMeshGeometryItem(
            mesh.EntityId, mesh.EntityName, index, mesh.Primitive,
            mesh.Vertices.Count, mesh.Indices.Count, mesh.Indices.Count / 3,
            minimum, maximum, mesh.MorphTargets.Count, mesh.MorphWeightSource);
    }

    private static InspectSceneMeshGeometryResult Empty(InspectSceneMeshGeometryRequest request) =>
        new(request.SceneName, 0, 0, 0, 0, 0, [], false, [], false);
}
