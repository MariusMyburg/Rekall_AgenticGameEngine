using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.World;
using Rekall.Age.Rendering.Abstractions;
using System.Diagnostics;

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
        var source = await CreateFrameSourceAsync(plan, cancellationToken).ConfigureAwait(false);
        return source.BuildCurrentFrame();
    }

    public async ValueTask<RekallAgeOpenXrPerspectiveSceneFrameSource> CreateFrameSourceAsync(
        RekallAgeOpenXrHeadsetSoftwareSceneSubmitPlan plan,
        CancellationToken cancellationToken,
        Silk.NET.Vulkan.Format colorFormat = Silk.NET.Vulkan.Format.R8G8B8A8Srgb)
    {
        var sceneStore = new RekallAgeSceneStore();
        var worldBuilder = new RekallAgeRuntimeWorldBuilder();
        var executionLoop = RekallAgeRuntimeExecutionLoop.CreateDefault(plan.ProjectRoot);
        var scene = await sceneStore.LoadAsync(plan.ProjectRoot, plan.SceneName, cancellationToken)
            .ConfigureAwait(false);
        var world = worldBuilder.Build(scene);
        if (plan.SimulationStartFrame > 0)
        {
            world = (await executionLoop.RunAsync(
                    world,
                    plan.SimulationStartFrame,
                    cancellationToken)
                .ConfigureAwait(false)).World;
        }

        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(
            world,
            plan.RenderWidth,
            plan.RenderHeight,
            plan.DebugOverlay).ForHeadsetOutput();
        var assets = await new RekallAgeRuntimeViewportAssetResolver()
            .ResolveAsync(plan.ProjectRoot, frame, cancellationToken)
            .ConfigureAwait(false);
        return new RekallAgeOpenXrPerspectiveSceneFrameSource(
            world,
            executionLoop,
            assets,
            plan.RenderWidth,
            plan.RenderHeight,
            plan.DebugOverlay,
            colorFormat);
    }

    internal static IReadOnlyDictionary<string, RekallAgeRgbaImage> BuildTextureLookup(
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

public sealed class RekallAgeOpenXrPerspectiveSceneFrameSource
{
    private RekallAgeRuntimeWorld _world;
    private readonly RekallAgeRuntimeExecutionLoop _executionLoop;
    private readonly RekallAgeRuntimeSimulationClock _simulationClock;
    private readonly RekallAgeRuntimeViewportAssetSet _assets;
    private readonly int _width;
    private readonly int _height;
    private readonly bool _debugOverlay;
    private readonly Silk.NET.Vulkan.Format _colorFormat;
    private readonly Func<TimeSpan> _clock;
    private readonly Stopwatch? _ownedClock;

    public RekallAgeOpenXrPerspectiveSceneFrameSource(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeExecutionLoop executionLoop,
        RekallAgeRuntimeViewportAssetSet assets,
        int width,
        int height,
        bool debugOverlay,
        Silk.NET.Vulkan.Format colorFormat = Silk.NET.Vulkan.Format.R8G8B8A8Srgb,
        Func<TimeSpan>? clock = null)
    {
        _world = world;
        _executionLoop = executionLoop;
        _assets = assets;
        _width = width;
        _height = height;
        _debugOverlay = debugOverlay;
        _colorFormat = colorFormat;
        _ownedClock = clock is null ? Stopwatch.StartNew() : null;
        _clock = clock ?? (() => _ownedClock!.Elapsed);
        _simulationClock = new RekallAgeRuntimeSimulationClock(_executionLoop, _clock());
    }

    public RekallAgeOpenXrPerspectiveSceneFrame BuildCurrentFrame()
    {
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(
            _world,
            _width,
            _height,
            _debugOverlay).ForHeadsetOutput();
        var meshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame, _assets);
        var target = RekallAgeVulkanSceneRenderTarget.OpenXrStereoSwapchain(
            checked((uint)_width),
            checked((uint)_height),
            2,
            _colorFormat,
            Silk.NET.Vulkan.Format.D32Sfloat);
        var preparedFrame = RekallAgeVulkanScenePreparedFrameBuilder.Build(frame, meshes, target);
        return new RekallAgeOpenXrPerspectiveSceneFrame(
            frame,
            preparedFrame.Batch,
            meshes.Count,
            RekallAgeOpenXrSoftwareSceneFrameRenderer.BuildTextureLookup(meshes),
            preparedFrame);
    }

    public async ValueTask<RekallAgeOpenXrPerspectiveSceneFrame> AdvanceAsync(
        CancellationToken cancellationToken,
        Func<int, RekallAgeRuntimeInputState>? inputForStep = null)
    {
        var result = await _simulationClock.AdvanceToAsync(_world, _clock(), cancellationToken, inputForStep)
            .ConfigureAwait(false);
        _world = result.World;
        return BuildCurrentFrame();
    }

    public async ValueTask<RekallAgeOpenXrPerspectiveSceneFrame> AdvanceByAsync(
        TimeSpan elapsedSinceLastHeadsetFrame,
        CancellationToken cancellationToken,
        Func<int, RekallAgeRuntimeInputState>? inputForStep = null)
    {
        var result = await _simulationClock.AdvanceByAsync(_world, elapsedSinceLastHeadsetFrame, cancellationToken, inputForStep)
            .ConfigureAwait(false);
        _world = result.World;
        return BuildCurrentFrame();
    }

    public async ValueTask<RekallAgeOpenXrPerspectiveSceneFrame> ApplyInputFrameAsync(
        RekallAgeRuntimeInputState input,
        CancellationToken cancellationToken)
    {
        var result = await _executionLoop.RunAsync(_world, 1, cancellationToken, input)
            .ConfigureAwait(false);
        _world = result.World;
        _simulationClock.Reset(_clock());
        return BuildCurrentFrame();
    }
}

public sealed record RekallAgeOpenXrPerspectiveSceneFrame(
    RekallAgeRuntimeViewportFrame Frame,
    RekallAgeVulkanSceneBatch Batch,
    int MeshCount,
    IReadOnlyDictionary<string, RekallAgeRgbaImage> Textures,
    RekallAgeVulkanScenePreparedFrame PreparedFrame);
