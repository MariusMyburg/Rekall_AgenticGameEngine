using Rekall.Age.Runtime;
using Rekall.Age.World;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

public sealed class RekallAgeOpenXrSoftwareSceneFrameRenderer
{
    public async ValueTask<RekallAgeRuntimeViewportRgbaFrame> RenderAsync(
        RekallAgeOpenXrHeadsetSoftwareSceneSubmitPlan plan,
        CancellationToken cancellationToken)
    {
        var scene = await BuildSceneAsync(plan, cancellationToken).ConfigureAwait(false);
        var camera = scene.Frame.ActiveCamera
            ?? throw new InvalidOperationException("OpenXR scene rendering requires an active camera.");
        var renderer = new RekallAgePerspectiveSoftwareSceneRenderer();
        var viewProjection = renderer.CreateCameraViewProjection(
            camera,
            plan.RenderWidth,
            plan.RenderHeight,
            System.Numerics.Quaternion.Identity,
            System.Numerics.Vector3.Zero);
        var pixels = renderer.Render(
            scene.Batch,
            plan.RenderWidth,
            plan.RenderHeight,
            viewProjection,
            camera.ClearColor);
        return new RekallAgeRuntimeViewportRgbaFrame(
            plan.RenderWidth,
            plan.RenderHeight,
            pixels,
            scene.Frame.FrameIndex,
            camera.EntityName,
            scene.Frame.Renderables.Count,
            scene.MeshCount,
            0,
            0,
            0,
            pixels.Any(value => value != 0));
    }

    public async ValueTask<RekallAgeOpenXrPerspectiveSceneFrame> BuildSceneAsync(
        RekallAgeOpenXrHeadsetSoftwareSceneSubmitPlan plan,
        CancellationToken cancellationToken)
    {
        var sceneStore = new RekallAgeSceneStore();
        var worldBuilder = new RekallAgeRuntimeWorldBuilder();
        var executionLoop = RekallAgeRuntimeExecutionLoop.CreateDefault(plan.ProjectRoot);
        var world = await new RekallAgeRuntimeSnapshotService(sceneStore, worldBuilder, executionLoop)
            .InspectSceneAsync(
                plan.ProjectRoot,
                plan.SceneName,
                plan.SimulationStartFrame,
                cancellationToken)
            .ConfigureAwait(false);
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(
            world,
            plan.RenderWidth,
            plan.RenderHeight,
            plan.DebugOverlay).ForHeadsetOutput();
        var assets = await new RekallAgeRuntimeViewportAssetResolver()
            .ResolveAsync(plan.ProjectRoot, frame, cancellationToken)
            .ConfigureAwait(false);
        var meshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame, assets);
        var batch = new RekallAgeVulkanSceneBatchBuilder().Build(frame, meshes);
        return new RekallAgeOpenXrPerspectiveSceneFrame(frame, batch, meshes.Count);
    }
}

public sealed record RekallAgeOpenXrPerspectiveSceneFrame(
    RekallAgeRuntimeViewportFrame Frame,
    RekallAgeVulkanSceneBatch Batch,
    int MeshCount);
