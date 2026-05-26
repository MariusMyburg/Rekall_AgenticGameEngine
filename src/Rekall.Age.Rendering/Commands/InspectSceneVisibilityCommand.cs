using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Rendering;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Rendering.Commands;

public sealed record InspectSceneVisibilityRequest(
    string ProjectRoot,
    string SceneName,
    int Frames = 0);

public sealed record InspectSceneVisibilityResult(
    string SceneName,
    int FrameIndex,
    int TotalRenderableCount,
    IReadOnlyList<InspectSceneVisibilityCamera> Cameras,
    IReadOnlyList<InspectSceneVisibilityRenderable> UnseenByActiveCameraRenderables,
    IReadOnlyList<InspectSceneVisibilityRenderable> UnseenByAnyCameraRenderables);

public sealed record InspectSceneVisibilityCamera(
    string EntityId,
    string EntityName,
    bool Active,
    string CullingMask,
    int VisibleRenderableCount,
    IReadOnlyList<InspectSceneVisibilityRenderable> VisibleRenderables,
    int CulledRenderableCount,
    IReadOnlyList<InspectSceneVisibilityRenderable> CulledRenderables);

public sealed record InspectSceneVisibilityRenderable(
    string EntityId,
    string EntityName,
    string Kind,
    string Layer,
    string Reason);

public sealed class InspectSceneVisibilityCommand
    : IRekallAgeCommand<InspectSceneVisibilityRequest, InspectSceneVisibilityResult>
{
    public string Name => "rekall.render.visibility.inspect_scene";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Inspects render-layer visibility for every scene camera and reports renderables hidden from active or all cameras.",
        typeof(InspectSceneVisibilityRequest).FullName!,
        typeof(InspectSceneVisibilityResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<InspectSceneVisibilityResult>> ExecuteAsync(
        InspectSceneVisibilityRequest request,
        RekallAgeCommandContext context)
    {
        if (request.Frames < 0)
        {
            return RekallAgeCommandResult<InspectSceneVisibilityResult>.Failure(
                Empty(request),
                "Scene visibility inspection requires a non-negative frame count.",
                [
                    new RekallAgeCommandError(
                        "REKALL_SCENE_VISIBILITY_INVALID_REQUEST",
                        "Frame count cannot be negative.",
                        request.SceneName)
                ]);
        }

        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            request.ProjectRoot,
            request.SceneName,
            request.Frames,
            context.CancellationToken).ConfigureAwait(false);
        var result = BuildResult(world);
        return RekallAgeCommandResult<InspectSceneVisibilityResult>.Success(
            result,
            $"Scene '{request.SceneName}' visibility: {result.TotalRenderableCount} renderables across {result.Cameras.Count} camera(s).");
    }

    private static InspectSceneVisibilityResult BuildResult(RekallAgeRuntimeWorld world)
    {
        var rendering = world.Subsystems.Rendering;
        var renderables = EnumerateRenderables(rendering)
            .OrderBy(renderable => renderable.EntityName, StringComparer.Ordinal)
            .ThenBy(renderable => renderable.EntityId, StringComparer.Ordinal)
            .ToArray();
        var cameras = rendering.Cameras
            .OrderByDescending(camera => camera.Active)
            .ThenBy(camera => camera.EntityName, StringComparer.Ordinal)
            .ThenBy(camera => camera.EntityId, StringComparer.Ordinal)
            .Select(camera => BuildCameraVisibility(camera, renderables))
            .ToArray();
        var activeCameras = cameras.Where(camera => camera.Active).ToArray();
        var activeCameraSet = activeCameras.Length == 0 ? cameras : activeCameras;
        var unseenByActiveCameras = renderables
            .Where(renderable => activeCameraSet.Length > 0
                && !activeCameraSet.Any(camera => camera.VisibleRenderables.Any(visible => visible.EntityId == renderable.EntityId)))
            .Select(renderable => ToRenderable(renderable, "not-visible-to-active-camera"))
            .ToArray();
        var unseenByAnyCamera = renderables
            .Where(renderable => cameras.Length > 0
                && !cameras.Any(camera => camera.VisibleRenderables.Any(visible => visible.EntityId == renderable.EntityId)))
            .Select(renderable => ToRenderable(renderable, "not-visible-to-any-camera"))
            .ToArray();

        return new InspectSceneVisibilityResult(
            world.SceneName,
            world.FrameIndex,
            renderables.Length,
            cameras,
            unseenByActiveCameras,
            unseenByAnyCamera);
    }

    private static InspectSceneVisibilityCamera BuildCameraVisibility(
        RekallAgeRuntimeRenderCamera camera,
        IReadOnlyList<RenderableCandidate> renderables)
    {
        var visible = renderables
            .Where(renderable => RekallAgeRenderLayerMask.IncludesLayer(renderable.Layer, camera.CullingMask))
            .Select(renderable => ToRenderable(renderable, "visible"))
            .ToArray();
        var culled = renderables
            .Where(renderable => !RekallAgeRenderLayerMask.IncludesLayer(renderable.Layer, camera.CullingMask))
            .Select(renderable => ToRenderable(renderable, "camera-culling-mask"))
            .ToArray();

        return new InspectSceneVisibilityCamera(
            camera.EntityId,
            camera.EntityName,
            camera.Active,
            RekallAgeRenderLayerMask.NormalizeCullingMask(camera.CullingMask),
            visible.Length,
            visible,
            culled.Length,
            culled);
    }

    private static IEnumerable<RenderableCandidate> EnumerateRenderables(RekallAgeRuntimeRenderView rendering)
    {
        foreach (var sprite in rendering.Sprites)
        {
            yield return new RenderableCandidate(sprite.EntityId, sprite.EntityName, "sprite", sprite.Layer);
        }

        foreach (var mesh in rendering.Meshes)
        {
            yield return new RenderableCandidate(mesh.EntityId, mesh.EntityName, "mesh", mesh.Layer);
        }

        foreach (var light in rendering.Lights)
        {
            yield return new RenderableCandidate(light.EntityId, light.EntityName, "light", light.Layer);
        }

        foreach (var uiLayer in rendering.UiLayers)
        {
            yield return new RenderableCandidate(uiLayer.EntityId, uiLayer.EntityName, "ui", "default");
        }
    }

    private static InspectSceneVisibilityRenderable ToRenderable(RenderableCandidate renderable, string reason)
    {
        return new InspectSceneVisibilityRenderable(
            renderable.EntityId,
            renderable.EntityName,
            renderable.Kind,
            RekallAgeRenderLayerMask.NormalizeLayer(renderable.Layer),
            reason);
    }

    private static InspectSceneVisibilityResult Empty(InspectSceneVisibilityRequest request)
    {
        return new InspectSceneVisibilityResult(
            request.SceneName,
            Math.Max(0, request.Frames),
            0,
            [],
            [],
            []);
    }

    private sealed record RenderableCandidate(
        string EntityId,
        string EntityName,
        string Kind,
        string Layer);
}
