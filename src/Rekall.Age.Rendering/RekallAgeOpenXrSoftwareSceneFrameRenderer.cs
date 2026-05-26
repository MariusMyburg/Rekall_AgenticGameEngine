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
            camera.ClearColor,
            scene.Textures);
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
        return new RekallAgeOpenXrPerspectiveSceneFrame(frame, batch, meshes.Count, BuildTextureLookup(meshes));
    }

    private static IReadOnlyDictionary<string, RekallAgeRgbaImage> BuildTextureLookup(
        IReadOnlyList<RekallAgeVulkanSceneMesh> meshes)
    {
        var textures = new Dictionary<string, RekallAgeRgbaImage>(StringComparer.Ordinal);
        foreach (var mesh in meshes)
        {
            AddTexture(mesh.BaseColorTexture, textures);
        }

        return textures;
    }

    private static void AddTexture(
        RekallAgeVulkanSceneTexture? texture,
        Dictionary<string, RekallAgeRgbaImage> textures)
    {
        if (texture is null || textures.ContainsKey(texture.Id))
        {
            return;
        }

        if (texture.Rgba.Length == texture.Width * texture.Height * 4)
        {
            textures[texture.Id] = new RekallAgeRgbaImage(texture.Width, texture.Height, texture.Rgba);
            return;
        }

        if (texture.RuntimeTexture is { } runtimeTexture
            && RekallAgeBlockCompressedTextureDecoder.TryDecodeTopLevel(runtimeTexture) is { } decoded)
        {
            textures[texture.Id] = decoded;
        }
    }
}

public sealed record RekallAgeOpenXrPerspectiveSceneFrame(
    RekallAgeRuntimeViewportFrame Frame,
    RekallAgeVulkanSceneBatch Batch,
    int MeshCount,
    IReadOnlyDictionary<string, RekallAgeRgbaImage> Textures);
