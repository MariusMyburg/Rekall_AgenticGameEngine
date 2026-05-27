using Rekall.Age.Core.Commands;
using Rekall.Age.Runtime;

namespace Rekall.Age.Rendering.Commands;

public sealed record InspectVirtualGeometrySceneRequest(
    string ProjectRoot,
    string SceneName,
    int Frames = 0,
    int Width = 1920,
    int Height = 1080,
    bool DebugOverlay = false);

public sealed record InspectVirtualGeometrySceneResult(
    string SceneName,
    int FrameIndex,
    int RenderableCount,
    int VirtualGeometryRenderableCount,
    int SourceTriangles,
    int SelectedTriangles,
    int ReducedTriangles,
    IReadOnlyList<InspectVirtualGeometryRenderable> Renderables,
    IReadOnlyList<string> Recommendations);

public sealed record InspectVirtualGeometryRenderable(
    string EntityId,
    string EntityName,
    string? AssetId,
    bool Enabled,
    double TargetPixelError,
    int ClusterTriangleCount,
    int MaxSelectedTriangles,
    int MaxLodLevel,
    string DebugMode,
    int MeshCount,
    int SourceTriangles,
    int SelectedTriangles,
    int ReducedTriangles,
    int SelectedLodLevel);

public sealed class InspectVirtualGeometrySceneCommand
    : IRekallAgeCommand<InspectVirtualGeometrySceneRequest, InspectVirtualGeometrySceneResult>
{
    public string Name => "rekall.render.virtual_geometry.inspect_scene";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Inspects virtual geometry selection for scene mesh renderables and reports source, selected, and reduced triangle counts.",
        typeof(InspectVirtualGeometrySceneRequest).FullName!,
        typeof(InspectVirtualGeometrySceneResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<InspectVirtualGeometrySceneResult>> ExecuteAsync(
        InspectVirtualGeometrySceneRequest request,
        RekallAgeCommandContext context)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return RekallAgeCommandResult<InspectVirtualGeometrySceneResult>.Failure(
                Empty(request),
                "Virtual geometry scene inspection requires non-negative frames and positive dimensions.",
                errors);
        }

        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            request.ProjectRoot,
            request.SceneName,
            request.Frames,
            context.CancellationToken).ConfigureAwait(false);
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(
            world,
            request.Width,
            request.Height,
            request.DebugOverlay).ForHeadsetOutput();
        var assets = await new RekallAgeRuntimeViewportAssetResolver().ResolveAsync(
            request.ProjectRoot,
            frame,
            context.CancellationToken).ConfigureAwait(false);
        var meshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame, assets);
        var result = BuildResult(frame, meshes);
        return RekallAgeCommandResult<InspectVirtualGeometrySceneResult>.Success(
            result,
            $"Scene '{request.SceneName}' virtual geometry selected {result.SelectedTriangles} of {result.SourceTriangles} source triangle(s).");
    }

    private static InspectVirtualGeometrySceneResult BuildResult(
        Rendering.Abstractions.RekallAgeRuntimeViewportFrame frame,
        IReadOnlyList<RekallAgeVulkanSceneMesh> meshes)
    {
        var meshesByEntityId = meshes
            .GroupBy(mesh => mesh.EntityId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var renderables = frame.Renderables
            .Where(renderable => renderable.VirtualGeometry is not null)
            .OrderBy(renderable => renderable.EntityName, StringComparer.Ordinal)
            .ThenBy(renderable => renderable.EntityId, StringComparer.Ordinal)
            .Select(renderable =>
            {
                meshesByEntityId.TryGetValue(renderable.EntityId, out var renderableMeshes);
                renderableMeshes ??= [];
                var sourceTriangles = renderableMeshes.Sum(mesh => mesh.VirtualGeometrySourceTriangleCount ?? mesh.Indices.Count / 3);
                var selectedTriangles = renderableMeshes.Sum(mesh => mesh.Indices.Count / 3);
                var settings = renderable.VirtualGeometry!;
                return new InspectVirtualGeometryRenderable(
                    renderable.EntityId,
                    renderable.EntityName,
                    renderable.AssetId,
                    settings.Enabled,
                    settings.TargetPixelError,
                    settings.ClusterTriangleCount,
                    settings.MaxSelectedTriangles,
                    settings.MaxLodLevel,
                    settings.DebugMode,
                    renderableMeshes.Length,
                    sourceTriangles,
                    selectedTriangles,
                    Math.Max(0, sourceTriangles - selectedTriangles),
                    renderableMeshes.Length == 0 ? 0 : renderableMeshes.Max(mesh => mesh.VirtualGeometryLodLevel));
            })
            .ToArray();
        var source = renderables.Sum(renderable => renderable.SourceTriangles);
        var selected = renderables.Sum(renderable => renderable.SelectedTriangles);
        var reduced = Math.Max(0, source - selected);
        return new InspectVirtualGeometrySceneResult(
            frame.SceneName,
            frame.FrameIndex,
            frame.Renderables.Count,
            renderables.Count(renderable => renderable.Enabled),
            source,
            selected,
            reduced,
            renderables,
            BuildRecommendations(renderables, source, selected, reduced));
    }

    private static IReadOnlyList<string> BuildRecommendations(
        IReadOnlyList<InspectVirtualGeometryRenderable> renderables,
        int sourceTriangles,
        int selectedTriangles,
        int reducedTriangles)
    {
        if (renderables.Count == 0)
        {
            return ["No Rekall.VirtualGeometry renderables were found. Add the component to dense mesh entities that need CPU-side triangle selection."];
        }

        var recommendations = new List<string>
        {
            $"Virtual geometry selected {selectedTriangles} of {sourceTriangles} source triangle(s), reducing {reducedTriangles} triangle(s)."
        };
        if (renderables.Any(renderable => renderable.Enabled && renderable.ReducedTriangles == 0 && renderable.SourceTriangles > 0))
        {
            recommendations.Add("Some enabled virtual geometry renderables were not reduced; lower maxSelectedTriangles or increase targetPixelError for more aggressive selection.");
        }

        if (renderables.Any(renderable => !renderable.Enabled))
        {
            recommendations.Add("Some Rekall.VirtualGeometry components are disabled and do not affect selected triangle counts.");
        }

        return recommendations;
    }

    private static IReadOnlyList<RekallAgeCommandError> Validate(InspectVirtualGeometrySceneRequest request)
    {
        var errors = new List<RekallAgeCommandError>();
        if (request.Frames < 0)
        {
            errors.Add(new RekallAgeCommandError(
                "REKALL_VIRTUAL_GEOMETRY_INVALID_REQUEST",
                "Frame count cannot be negative.",
                request.SceneName));
        }

        if (request.Width <= 0 || request.Height <= 0)
        {
            errors.Add(new RekallAgeCommandError(
                "REKALL_VIRTUAL_GEOMETRY_INVALID_REQUEST",
                "Virtual geometry inspection dimensions must be positive.",
                $"{request.Width}x{request.Height}"));
        }

        return errors;
    }

    private static InspectVirtualGeometrySceneResult Empty(InspectVirtualGeometrySceneRequest request)
    {
        return new InspectVirtualGeometrySceneResult(
            request.SceneName,
            Math.Max(0, request.Frames),
            0,
            0,
            0,
            0,
            0,
            [],
            []);
    }
}
