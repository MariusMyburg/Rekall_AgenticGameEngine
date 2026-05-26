using System.Diagnostics;
using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.Runtime.Live;
using Rekall.Age.World;
using Rekall.Age.World.Commands;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.SPIRV;
using Veldrid.StartupUtilities;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingColor = System.Drawing.Color;
using DrawingFont = System.Drawing.Font;
using DrawingFontStyle = System.Drawing.FontStyle;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingGraphicsUnit = System.Drawing.GraphicsUnit;
using DrawingImageLockMode = System.Drawing.Imaging.ImageLockMode;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingSolidBrush = System.Drawing.SolidBrush;

namespace Rekall.Age.Player.Windows;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        PlayerLog.Write("Player process starting.");
        if (args.Length < 2)
        {
            PlayerLog.Write("Player process exiting: missing arguments.");
            return 2;
        }

        var backend = ReadOption(args, "--backend") ?? "vulkan";
        if (!backend.Equals("vulkan", StringComparison.OrdinalIgnoreCase))
        {
            backend = "vulkan";
        }

        var syncToVerticalBlank = !HasOption(args, "--no-vsync");
        var openXrRequested = HasOption(args, "--xr") || HasOption(args, "--vr");
        var simulateXrInput = HasOption(args, "--simulate-xr") || HasOption(args, "--xr-sim");
        var probeOpenXrCompositor = HasOption(args, "--openxr-compositor-probe");
        await using var player = await RekallAgeVeldridPlayer.CreateAsync(
            Path.GetFullPath(args[0]),
            args[1],
            syncToVerticalBlank,
            openXrRequested,
            simulateXrInput,
            probeOpenXrCompositor,
            CancellationToken.None);
        PlayerLog.Write("Player entering render loop.");
        player.Run();
        PlayerLog.Write("Player process exiting normally.");
        return 0;
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (var i = 2; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static bool HasOption(string[] args, string name)
    {
        return args.Skip(2).Any(arg => arg.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed class RekallAgeVeldridPlayer : IAsyncDisposable
{
    private const int InitialWidth = 1280;
    private const int InitialHeight = 720;
    private const int HudWidth = 360;
    private const int HudHeight = 224;
    private const int HudMargin = 16;
    private const int SceneSupersampleFactor = 2;

    private static readonly JsonSerializerOptions LiveJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _projectRoot;
    private readonly string _sceneName;
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private readonly string _livePipeName;
    private readonly ConcurrentQueue<LiveEditWorkItem> _liveEditQueue = new();
    private readonly RekallAgeLivePlayerNamedPipeServer _liveServer;
    private FileSystemWatcher? _assetWatcher;
    private readonly Sdl2Window _window;
    private readonly GraphicsDevice _device;
    private readonly ResourceFactory _factory;
    private readonly CommandList _commands;
    private readonly Pipeline _scenePipeline;
    private readonly Pipeline _presentPipeline;
    private readonly Pipeline _hudPipeline;
    private readonly ResourceLayout _frameLayout;
    private readonly ResourceLayout _drawLayout;
    private readonly ResourceLayout _materialLayout;
    private readonly ResourceLayout _presentTextureLayout;
    private readonly ResourceLayout _hudTextureLayout;
    private readonly ResourceSet _frameSet;
    private ResourceSet _drawSet;
    private readonly RekallAgeRuntimeExecutionLoop _runtimeLoop;
    private readonly RekallAgeRuntimeSimulationClock _simulationClock;
    private readonly RekallAgeRuntimeRenderFrameBuilder _frameBuilder = new();
    private RekallAgeRuntimeViewportAssetSet _assets;
    private int _entityCount;
    private readonly Dictionary<string, TextureBinding> _textures;
    private readonly Dictionary<MaterialKey, ResourceSet> _materialSets = new();
    private readonly TextureBinding _whiteTexture;
    private readonly TextureBinding _flatNormalTexture;
    private readonly TextureBinding _defaultMetallicRoughnessTexture;
    private readonly TextureBinding _hudTexture;
    private readonly ResourceSet _hudTextureSet;
    private readonly RekallAgeOpenXrSessionBootstrapResult? _openXrStatus;
    private readonly RekallAgeOpenXrVulkanInteropInspection? _openXrVulkanInterop;
    private readonly RekallAgeOpenXrCompositorSessionBootstrapResult? _openXrCompositorSession;
    private readonly bool _simulateXrInput;
    private SceneRenderTarget _sceneTarget;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly RekallAgeVulkanSceneMeshBuilder _meshBuilder = new();
    private readonly RekallAgeVulkanSceneBatchBuilder _batchBuilder = new();

    private DeviceBuffer _vertexBuffer;
    private DeviceBuffer _indexBuffer;
    private DeviceBuffer _hudVertexBuffer;
    private DeviceBuffer _frameUniformBuffer;
    private DeviceBuffer _drawUniformBuffer;
    private uint _vertexBufferCapacityBytes;
    private uint _indexBufferCapacityBytes;
    private uint _hudVertexBufferCapacityBytes;
    private readonly uint _drawUniformStrideBytes;
    private uint _drawUniformBufferCapacityBytes;
    private int _frameIndex;
    private Rekall.Age.Runtime.Abstractions.RekallAgeRuntimeWorld _runtimeWorld;
    private double _pendingMouseWheelDelta;
    private Vector2 _lastMousePosition;
    private Vector2 _previousMousePosition;
    private readonly HashSet<string> _pressedKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pressedKeysThisFrame = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _releasedKeysThisFrame = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pressedButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pressedButtonsThisFrame = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _releasedButtonsThisFrame = new(StringComparer.OrdinalIgnoreCase);
    private readonly uint[] _drawUniformDynamicOffsets = new uint[1];
    private int _lastFpsFrame;
    private double _lastFpsTime;
    private int _fps;
    private int _sceneRevision = 1;
    private int _assetRevision = 1;
    private int _assetHotReloadPending;
    private long _lastAssetHotReloadRequestTicks;
    private CachedRenderGeometry? _cachedStaticGeometry;
    private bool _hudDirty = true;
    private RekallAgeSceneDocument _sceneDocument;

    private RekallAgeVeldridPlayer(
        string projectRoot,
        string sceneName,
        RekallAgeSceneDocument sceneDocument,
        Sdl2Window window,
        GraphicsDevice device,
        CommandList commands,
        Pipeline scenePipeline,
        Pipeline presentPipeline,
        Pipeline hudPipeline,
        ResourceLayout frameLayout,
        ResourceLayout drawLayout,
        ResourceLayout materialLayout,
        ResourceLayout presentTextureLayout,
        ResourceLayout hudTextureLayout,
        ResourceSet frameSet,
        ResourceSet drawSet,
        DeviceBuffer vertexBuffer,
        DeviceBuffer indexBuffer,
        DeviceBuffer hudVertexBuffer,
        DeviceBuffer frameUniformBuffer,
        DeviceBuffer drawUniformBuffer,
        Rekall.Age.Runtime.Abstractions.RekallAgeRuntimeWorld runtimeWorld,
        RekallAgeRuntimeExecutionLoop runtimeLoop,
        RekallAgeRuntimeViewportAssetSet assets,
        int entityCount,
        Dictionary<string, TextureBinding> textures,
        TextureBinding whiteTexture,
        TextureBinding flatNormalTexture,
        TextureBinding defaultMetallicRoughnessTexture,
        TextureBinding hudTexture,
        RekallAgeOpenXrSessionBootstrapResult? openXrStatus,
        RekallAgeOpenXrVulkanInteropInspection? openXrVulkanInterop,
        RekallAgeOpenXrCompositorSessionBootstrapResult? openXrCompositorSession,
        bool simulateXrInput)
    {
        _projectRoot = projectRoot;
        _sceneName = sceneName;
        _sceneDocument = sceneDocument;
        _livePipeName = RekallAgeLivePlayerEndpoint.ResolvePipeName(projectRoot, sceneName);
        _liveServer = new RekallAgeLivePlayerNamedPipeServer(_livePipeName, EnqueueLiveEditAsync);
        _window = window;
        _device = device;
        _factory = device.ResourceFactory;
        _commands = commands;
        _scenePipeline = scenePipeline;
        _presentPipeline = presentPipeline;
        _hudPipeline = hudPipeline;
        _frameLayout = frameLayout;
        _drawLayout = drawLayout;
        _materialLayout = materialLayout;
        _presentTextureLayout = presentTextureLayout;
        _hudTextureLayout = hudTextureLayout;
        _frameSet = frameSet;
        _drawSet = drawSet;
        _vertexBuffer = vertexBuffer;
        _indexBuffer = indexBuffer;
        _hudVertexBuffer = hudVertexBuffer;
        _frameUniformBuffer = frameUniformBuffer;
        _drawUniformBuffer = drawUniformBuffer;
        _vertexBufferCapacityBytes = vertexBuffer.SizeInBytes;
        _indexBufferCapacityBytes = indexBuffer.SizeInBytes;
        _hudVertexBufferCapacityBytes = hudVertexBuffer.SizeInBytes;
        _drawUniformStrideBytes = AlignTo(
            checked((uint)Marshal.SizeOf<DrawUniform>()),
            Math.Max(1, _device.UniformBufferMinOffsetAlignment));
        _drawUniformBufferCapacityBytes = drawUniformBuffer.SizeInBytes;
        _runtimeWorld = runtimeWorld;
        _runtimeLoop = runtimeLoop;
        _simulationClock = new RekallAgeRuntimeSimulationClock(_runtimeLoop, _clock.Elapsed);
        _assets = assets;
        _entityCount = entityCount;
        _textures = textures;
        _whiteTexture = whiteTexture;
        _flatNormalTexture = flatNormalTexture;
        _defaultMetallicRoughnessTexture = defaultMetallicRoughnessTexture;
        _hudTexture = hudTexture;
        _hudTextureSet = _factory.CreateResourceSet(new ResourceSetDescription(_hudTextureLayout, _hudTexture.Texture, _hudTexture.Sampler));
        _openXrStatus = openXrStatus;
        _openXrVulkanInterop = openXrVulkanInterop;
        _openXrCompositorSession = openXrCompositorSession;
        _simulateXrInput = simulateXrInput;
        _sceneTarget = CreateSceneRenderTarget(_factory, InitialWidth, InitialHeight, _presentTextureLayout);
    }

    public static async ValueTask<RekallAgeVeldridPlayer> CreateAsync(
        string projectRoot,
        string sceneName,
        bool syncToVerticalBlank,
        bool openXrRequested,
        bool simulateXrInput,
        bool probeOpenXrCompositor,
        CancellationToken cancellationToken)
    {
        PlayerLog.Write("Loading runtime scene.");
        var scene = await new Rekall.Age.World.RekallAgeSceneStore()
            .LoadAsync(projectRoot, sceneName, cancellationToken);
        var initialWorld = new RekallAgeRuntimeWorldBuilder().Build(scene);
        var runtimeLoop = RekallAgeRuntimeExecutionLoop.CreateDefault(projectRoot);
        var runResult = await runtimeLoop.RunAsync(initialWorld, 1, cancellationToken);
        var world = runResult.World;
        var baseFrame = new RekallAgeRuntimeRenderFrameBuilder()
            .Build(world, InitialWidth, InitialHeight, debugOverlay: true);
        var entityCount = world.Entities.Count;
        PlayerLog.Write($"Loaded runtime scene renderables={baseFrame.Renderables.Count}.");
        PlayerLog.Write("Resolving viewport assets.");
        var assets = await new RekallAgeRuntimeViewportAssetResolver()
            .ResolveAsync(projectRoot, baseFrame, cancellationToken);
        PlayerLog.Write($"Resolved viewport assets images={assets.Images.Count} textures={assets.Textures.Count} models={assets.Models.Count} issues={assets.Issues.Count}.");
        foreach (var issue in assets.Issues)
        {
            PlayerLog.Write($"Asset issue asset={issue.AssetId} code={issue.Code} message={issue.Message}");
        }

        RekallAgeOpenXrSessionBootstrapResult? openXrStatus = null;
        if (openXrRequested)
        {
            PlayerLog.Write("Bootstrapping OpenXR headset readiness.");
            openXrStatus = await new RekallAgeNativeOpenXrSessionBootstrap()
                .BootstrapAsync(cancellationToken)
                .ConfigureAwait(false);
            PlayerLog.Write(
                $"OpenXR status ready={openXrStatus.HeadsetSessionReady} hmd={openXrStatus.HmdSystemAvailable} vulkanRequirements={openXrStatus.VulkanGraphicsRequirementsReady} stereoViews={openXrStatus.PrimaryStereoViews.Count}.");
            foreach (var error in openXrStatus.Errors)
            {
                PlayerLog.Write($"OpenXR error: {error}");
            }
        }

        if (simulateXrInput)
        {
            PlayerLog.Write("XR input simulator enabled.");
        }

        var windowInfo = new WindowCreateInfo(
            100,
            100,
            InitialWidth,
            InitialHeight,
            WindowState.Normal,
            BuildWindowTitle(sceneName, openXrRequested, simulateXrInput));
        PlayerLog.Write("Creating SDL window.");
        var window = VeldridStartup.CreateWindow(ref windowInfo);
        var options = new GraphicsDeviceOptions(
            debug: false,
            swapchainDepthFormat: PixelFormat.D24_UNorm_S8_UInt,
            syncToVerticalBlank: syncToVerticalBlank,
            resourceBindingModel: ResourceBindingModel.Improved,
            preferDepthRangeZeroToOne: true,
            preferStandardClipSpaceYDirection: true);
        PlayerLog.Write("Creating Vulkan graphics device.");
        var device = VeldridStartup.CreateGraphicsDevice(window, options, GraphicsBackend.Vulkan);
        var factory = device.ResourceFactory;
        PlayerLog.Write($"Created graphics device backend={device.BackendType} vsync={syncToVerticalBlank} anisotropy={device.Features.SamplerAnisotropy}.");
        var openXrVulkanInterop = InspectOpenXrVulkanInterop(device, openXrStatus);
        RekallAgeOpenXrCompositorSessionBootstrapResult? openXrCompositorSession = null;
        if (probeOpenXrCompositor)
        {
            PlayerLog.Write("OpenXR compositor probe enabled.");
            openXrCompositorSession = await BootstrapOpenXrCompositorSessionAsync(
                    device,
                    openXrVulkanInterop,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else if (openXrRequested)
        {
            PlayerLog.Write("OpenXR compositor probe skipped; running headset-ready stereo mirror until XR-created Vulkan swapchain submission is enabled.");
        }

        var commands = factory.CreateCommandList();
        PlayerLog.Write("Compiling SPIR-V shaders.");
        var sceneShaders = factory.CreateFromSpirv(
            new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(SceneVertexShader), "main"),
            new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(SceneFragmentShader), "main"));
        var presentShaders = factory.CreateFromSpirv(
            new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(PresentVertexShader), "main"),
            new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(PresentFragmentShader), "main"));
        var hudShaders = factory.CreateFromSpirv(
            new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(HudVertexShader), "main"),
            new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(HudFragmentShader), "main"));
        var sceneVertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4),
            new VertexElementDescription("UV", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2));
        var hudVertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4),
            new VertexElementDescription("UV", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2));
        var frameLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("FrameUniform", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment)));
        var drawLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription(
                "DrawUniform",
                ResourceKind.UniformBuffer,
                ShaderStages.Vertex | ShaderStages.Fragment,
                ResourceLayoutElementOptions.DynamicBinding)));
        var materialLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("BaseColorTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("BaseColorSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("NormalTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("NormalSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("MetallicRoughnessTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("MetallicRoughnessSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("OcclusionTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("OcclusionSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("EmissiveTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("EmissiveSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
        var presentTextureLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("SceneTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("SceneSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
        var hudTextureLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("SurfaceTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("SurfaceSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
        using var initialSceneTarget = CreateSceneRenderTarget(factory, InitialWidth, InitialHeight, presentTextureLayout);
        var sceneShaderSet = new ShaderSetDescription([sceneVertexLayout], sceneShaders);
        var scenePipelineDescription = new GraphicsPipelineDescription(
            BlendStateDescription.SingleOverrideBlend,
            DepthStencilStateDescription.DepthOnlyLessEqual,
            RasterizerStateDescription.CullNone,
            PrimitiveTopology.TriangleList,
            sceneShaderSet,
            [frameLayout, drawLayout, materialLayout],
            initialSceneTarget.Framebuffer.OutputDescription);
        var presentShaderSet = new ShaderSetDescription([], presentShaders);
        var presentPipelineDescription = new GraphicsPipelineDescription(
            BlendStateDescription.SingleOverrideBlend,
            DepthStencilStateDescription.Disabled,
            RasterizerStateDescription.CullNone,
            PrimitiveTopology.TriangleList,
            presentShaderSet,
            [presentTextureLayout],
            device.SwapchainFramebuffer.OutputDescription);
        var hudShaderSet = new ShaderSetDescription([hudVertexLayout], hudShaders);
        var hudPipelineDescription = new GraphicsPipelineDescription(
            BlendStateDescription.SingleOverrideBlend,
            DepthStencilStateDescription.Disabled,
            RasterizerStateDescription.CullNone,
            PrimitiveTopology.TriangleList,
            hudShaderSet,
            [hudTextureLayout],
            device.SwapchainFramebuffer.OutputDescription);
        PlayerLog.Write("Creating graphics pipelines.");
        var scenePipeline = factory.CreateGraphicsPipeline(scenePipelineDescription);
        var presentPipeline = factory.CreateGraphicsPipeline(presentPipelineDescription);
        var hudPipeline = factory.CreateGraphicsPipeline(hudPipelineDescription);
        foreach (var shader in sceneShaders.Concat(presentShaders).Concat(hudShaders))
        {
            shader.Dispose();
        }

        PlayerLog.Write("Creating GPU buffers.");
        var vertexBuffer = factory.CreateBuffer(new BufferDescription(
            4 * 1024 * 1024,
            BufferUsage.VertexBuffer | BufferUsage.Dynamic));
        var indexBuffer = factory.CreateBuffer(new BufferDescription(
            4 * 1024 * 1024,
            BufferUsage.IndexBuffer | BufferUsage.Dynamic));
        var hudVertexBuffer = factory.CreateBuffer(new BufferDescription(
            64 * 1024,
            BufferUsage.VertexBuffer | BufferUsage.Dynamic));
        var frameUniformBuffer = factory.CreateBuffer(new BufferDescription(
            checked((uint)Marshal.SizeOf<FrameUniform>()),
            BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        var drawUniformStrideBytes = AlignTo(
            checked((uint)Marshal.SizeOf<DrawUniform>()),
            Math.Max(1, device.UniformBufferMinOffsetAlignment));
        var drawUniformBuffer = factory.CreateBuffer(new BufferDescription(
            checked(drawUniformStrideBytes * 256),
            BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        var frameSet = factory.CreateResourceSet(new ResourceSetDescription(frameLayout, frameUniformBuffer));
        var drawSet = factory.CreateResourceSet(new ResourceSetDescription(drawLayout, drawUniformBuffer));
        PlayerLog.Write("Creating texture resources.");
        var whiteTexture = CreateTextureBinding(
            device,
            factory,
            new RekallAgeVulkanSceneTexture(
                "__rekall_white",
                1,
                1,
                [255, 255, 255, 255],
                new RekallAgeVulkanSceneSampler(
                    RekallAgeVulkanSceneFilter.Linear,
                    RekallAgeVulkanSceneFilter.Linear,
                    RekallAgeVulkanSceneWrapMode.Repeat,
                    RekallAgeVulkanSceneWrapMode.Repeat)),
            hudTextureLayout);
        var flatNormalTexture = CreateTextureBinding(
            device,
            factory,
            new RekallAgeVulkanSceneTexture(
                "__rekall_flat_normal",
                1,
                1,
                [128, 128, 255, 255],
                new RekallAgeVulkanSceneSampler(
                    RekallAgeVulkanSceneFilter.Linear,
                    RekallAgeVulkanSceneFilter.Linear,
                    RekallAgeVulkanSceneWrapMode.Repeat,
                    RekallAgeVulkanSceneWrapMode.Repeat)),
            hudTextureLayout);
        var defaultMetallicRoughnessTexture = CreateTextureBinding(
            device,
            factory,
            new RekallAgeVulkanSceneTexture(
                "__rekall_default_metallic_roughness",
                1,
                1,
                [0, 255, 0, 255],
                new RekallAgeVulkanSceneSampler(
                    RekallAgeVulkanSceneFilter.Linear,
                    RekallAgeVulkanSceneFilter.Linear,
                    RekallAgeVulkanSceneWrapMode.Repeat,
                    RekallAgeVulkanSceneWrapMode.Repeat)),
            hudTextureLayout);
        var textures = CreateTextureBindings(device, factory, hudTextureLayout, assets);
        var hudTexture = CreateTextureBinding(
            device,
            factory,
            new RekallAgeVulkanSceneTexture(
                "__rekall_hud",
                HudWidth,
                HudHeight,
                new byte[HudWidth * HudHeight * 4],
                new RekallAgeVulkanSceneSampler(
                    RekallAgeVulkanSceneFilter.Linear,
                    RekallAgeVulkanSceneFilter.Linear,
                    RekallAgeVulkanSceneWrapMode.ClampToEdge,
                    RekallAgeVulkanSceneWrapMode.ClampToEdge)),
            hudTextureLayout);
        var player = new RekallAgeVeldridPlayer(
            projectRoot,
            sceneName,
            scene,
            window,
            device,
            commands,
            scenePipeline,
            presentPipeline,
            hudPipeline,
            frameLayout,
            drawLayout,
            materialLayout,
            presentTextureLayout,
            hudTextureLayout,
            frameSet,
            drawSet,
            vertexBuffer,
            indexBuffer,
            hudVertexBuffer,
            frameUniformBuffer,
            drawUniformBuffer,
            world,
            runtimeLoop,
            assets,
            entityCount,
            textures,
            whiteTexture,
            flatNormalTexture,
            defaultMetallicRoughnessTexture,
            hudTexture,
            openXrStatus,
            openXrVulkanInterop,
            openXrCompositorSession,
            simulateXrInput);
        player.StartLiveEditServer();
        player.StartAssetHotReloadWatcher();
        PlayerLog.Write("Player initialization complete.");
        return player;
    }

    private static RekallAgeOpenXrVulkanInteropInspection? InspectOpenXrVulkanInterop(
        GraphicsDevice device,
        RekallAgeOpenXrSessionBootstrapResult? openXrStatus)
    {
        if (openXrStatus is null)
        {
            return null;
        }

        RekallAgeOpenXrVulkanDeviceInteropInfo? vulkan = null;
        if (device.GetVulkanInfo(out var info))
        {
            vulkan = new RekallAgeOpenXrVulkanDeviceInteropInfo(
                device.BackendType.ToString(),
                unchecked((ulong)info.Instance),
                unchecked((ulong)info.PhysicalDevice),
                unchecked((ulong)info.Device),
                unchecked((ulong)info.GraphicsQueue),
                info.GraphicsQueueFamilyIndex,
                ExternalTextureWrappingSupported: true,
                info.DriverName,
                info.DriverInfo);
        }

        var inspection = RekallAgeOpenXrVulkanInteropInspector.Inspect(openXrStatus, vulkan);
        PlayerLog.Write(
            $"OpenXR Vulkan interop status={inspection.Status} graphicsBinding={inspection.ReadyForXrGraphicsBinding} swapchainWrapping={inspection.ReadyForXrSwapchainWrapping} compositor={inspection.ReadyForCompositorSession} eye={inspection.RecommendedEyeWidth}x{inspection.RecommendedEyeHeight} layers={inspection.SwapchainArrayLayers}.");
        foreach (var capability in inspection.Capabilities)
        {
            PlayerLog.Write($"OpenXR Vulkan capability: {capability}");
        }

        foreach (var blocker in inspection.Blockers)
        {
            PlayerLog.Write($"OpenXR Vulkan blocker: {blocker}");
        }

        return inspection;
    }

    private static async ValueTask<RekallAgeOpenXrCompositorSessionBootstrapResult?> BootstrapOpenXrCompositorSessionAsync(
        GraphicsDevice device,
        RekallAgeOpenXrVulkanInteropInspection? inspection,
        CancellationToken cancellationToken)
    {
        if (inspection is not { ReadyForXrGraphicsBinding: true }
            || !device.GetVulkanInfo(out var info))
        {
            return null;
        }

        var vulkan = new RekallAgeOpenXrVulkanDeviceInteropInfo(
            device.BackendType.ToString(),
            unchecked((ulong)info.Instance),
            unchecked((ulong)info.PhysicalDevice),
            unchecked((ulong)info.Device),
            unchecked((ulong)info.GraphicsQueue),
            info.GraphicsQueueFamilyIndex,
            ExternalTextureWrappingSupported: true,
            info.DriverName,
            info.DriverInfo,
            inspection.RecommendedEyeWidth,
            inspection.RecommendedEyeHeight);
        var session = await new RekallAgeNativeOpenXrCompositorSessionBootstrap()
            .BootstrapAsync(vulkan, cancellationToken)
            .ConfigureAwait(false);
        PlayerLog.Write(
            $"OpenXR compositor session ready={session.ReadyForFrameSubmission} frameLoop={session.FrameLoopReady} sessionReadyEvent={session.SessionReadyEventObserved} lastState={RekallAgeNativeOpenXrCompositorSessionBootstrap.DescribeOpenXrSessionState(session.LastSessionState)} sessionCreated={session.SessionCreated} formats={session.SwapchainFormats.Count} preferredColor={session.PreferredColorFormat?.ToString(CultureInfo.InvariantCulture) ?? "<none>"} preferredDepth={session.PreferredDepthFormat?.ToString(CultureInfo.InvariantCulture) ?? "<none>"} colorImages={session.ColorSwapchainImageCount} depthImages={session.DepthSwapchainImageCount} frameWaited={session.FrameWaited} frameEnded={session.FrameEnded}.");
        foreach (var error in session.Errors)
        {
            PlayerLog.Write($"OpenXR compositor session error: {error}");
        }

        return session;
    }

    private void StartLiveEditServer()
    {
        _liveServer.Start();
        PlayerLog.Write($"Live-edit server listening pipe={_livePipeName} session={_sessionId}.");
    }

    private void StartAssetHotReloadWatcher()
    {
        var assetsRoot = Path.Combine(_projectRoot, "Assets");
        Directory.CreateDirectory(assetsRoot);
        _assetWatcher = new FileSystemWatcher(assetsRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.LastWrite
                | NotifyFilters.Size
                | NotifyFilters.CreationTime
        };
        _assetWatcher.Changed += (_, _) => MarkAssetHotReloadPending();
        _assetWatcher.Created += (_, _) => MarkAssetHotReloadPending();
        _assetWatcher.Deleted += (_, _) => MarkAssetHotReloadPending();
        _assetWatcher.Renamed += (_, _) => MarkAssetHotReloadPending();
        _assetWatcher.EnableRaisingEvents = true;
        PlayerLog.Write($"Asset hot reload watching {assetsRoot}.");
    }

    public void Run()
    {
        while (_window.Exists)
        {
            CaptureInput(_window.PumpEvents());
            if (!_window.Exists)
            {
                break;
            }

            RenderFrame();
        }

        _device.WaitForIdle();
    }

    private void CaptureInput(InputSnapshot snapshot)
    {
        _previousMousePosition = _lastMousePosition;
        _lastMousePosition = snapshot.MousePosition;
        foreach (var keyEvent in snapshot.KeyEvents)
        {
            var key = keyEvent.Key.ToString();
            if (keyEvent.Down)
            {
                if (_pressedKeys.Add(key))
                {
                    _pressedKeysThisFrame.Add(key);
                }
            }
            else if (_pressedKeys.Remove(key))
            {
                _releasedKeysThisFrame.Add(key);
            }
        }

        foreach (var mouseEvent in snapshot.MouseEvents)
        {
            var button = mouseEvent.MouseButton.ToString();
            if (mouseEvent.Down)
            {
                if (_pressedButtons.Add(button))
                {
                    _pressedButtonsThisFrame.Add(button);
                }
            }
            else if (_pressedButtons.Remove(button))
            {
                _releasedButtonsThisFrame.Add(button);
            }
        }

        if (Math.Abs(snapshot.WheelDelta) <= 0.000001f)
        {
            return;
        }

        _pendingMouseWheelDelta += snapshot.WheelDelta;
        _cachedStaticGeometry = null;
    }

    private static string BuildWindowTitle(
        string sceneName,
        bool openXrRequested,
        bool simulateXrInput)
    {
        var suffixes = new List<string> { "Vulkan swapchain" };
        if (openXrRequested)
        {
            suffixes.Add("OpenXR mirror");
        }

        if (simulateXrInput)
        {
            suffixes.Add("XR sim");
        }

        return $"Rekall AGE Player - {sceneName} | {string.Join(" | ", suffixes)}";
    }

    public async ValueTask DisposeAsync()
    {
        _device.WaitForIdle();
        _assetWatcher?.Dispose();
        await _liveServer.DisposeAsync();
        _sceneTarget.Dispose();
        foreach (var materialSet in _materialSets.Values)
        {
            materialSet.Dispose();
        }

        _hudTextureSet.Dispose();
        _frameSet.Dispose();
        _drawSet.Dispose();
        _vertexBuffer.Dispose();
        _indexBuffer.Dispose();
        _hudVertexBuffer.Dispose();
        _frameUniformBuffer.Dispose();
        _drawUniformBuffer.Dispose();
        foreach (var texture in _textures.Values)
        {
            texture.Dispose();
        }

        _whiteTexture.Dispose();
        _flatNormalTexture.Dispose();
        _defaultMetallicRoughnessTexture.Dispose();
        _hudTexture.Dispose();
        _scenePipeline.Dispose();
        _presentPipeline.Dispose();
        _hudPipeline.Dispose();
        _frameLayout.Dispose();
        _drawLayout.Dispose();
        _materialLayout.Dispose();
        _presentTextureLayout.Dispose();
        _hudTextureLayout.Dispose();
        _commands.Dispose();
        _device.Dispose();
    }

    private void EnsureSceneRenderTarget(int displayWidth, int displayHeight)
    {
        displayWidth = Math.Max(1, displayWidth);
        displayHeight = Math.Max(1, displayHeight);
        if (_sceneTarget.DisplayWidth == displayWidth
            && _sceneTarget.DisplayHeight == displayHeight)
        {
            return;
        }

        _device.WaitForIdle();
        _sceneTarget.Dispose();
        _sceneTarget = CreateSceneRenderTarget(_factory, displayWidth, displayHeight, _presentTextureLayout);
        _cachedStaticGeometry = null;
        PlayerLog.Write($"Recreated supersampled scene target {_sceneTarget.Width}x{_sceneTarget.Height} for window {displayWidth}x{displayHeight}.");
    }

    private void MarkAssetHotReloadPending()
    {
        Interlocked.Exchange(ref _lastAssetHotReloadRequestTicks, Stopwatch.GetTimestamp());
        Interlocked.Exchange(ref _assetHotReloadPending, 1);
    }

    private void ProcessAssetHotReload()
    {
        if (Volatile.Read(ref _assetHotReloadPending) == 0)
        {
            return;
        }

        var elapsedSeconds = (Stopwatch.GetTimestamp() - Volatile.Read(ref _lastAssetHotReloadRequestTicks))
            / (double)Stopwatch.Frequency;
        if (elapsedSeconds < 0.5)
        {
            return;
        }

        if (Interlocked.Exchange(ref _assetHotReloadPending, 0) == 0)
        {
            return;
        }

        try
        {
            ReloadAssetsForCurrentWorld("Hot-reloaded runtime viewport assets after asset filesystem change.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            PlayerLog.Write($"Asset hot reload failed; retrying after debounce. error={ex.Message}");
            MarkAssetHotReloadPending();
        }
    }

    private ValueTask<JsonObject> EnqueueLiveEditAsync(
        RekallAgeLivePlayerRequestEnvelope request,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _liveEditQueue.Enqueue(new LiveEditWorkItem(request, completion));
        return new ValueTask<JsonObject>(completion.Task.WaitAsync(cancellationToken));
    }

    private void ProcessLiveEditQueue()
    {
        while (_liveEditQueue.TryDequeue(out var item))
        {
            try
            {
                item.Completion.SetResult(ApplyLiveEdit(item.Request));
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or JsonException or ArgumentException)
            {
                item.Completion.SetException(ex);
                PlayerLog.Write($"Live-edit request failed operation={item.Request.Operation} error={ex.Message}");
            }
        }
    }

    private JsonObject ApplyLiveEdit(RekallAgeLivePlayerRequestEnvelope request)
    {
        PlayerLog.Write($"Live-edit request operation={request.Operation} request={request.RequestId}.");
        return request.Operation switch
        {
            "status" => CreateLiveStatus("status", false, "Live player is running."),
            "reload_scene" => ReloadSceneFromDisk(ReadBoolean(request.Payload, "reloadAssets", true)),
            "reload_assets" => ReloadAssetsForCurrentWorld("Reloaded runtime viewport assets."),
            "apply_scene_blueprint" => ApplySceneBlueprintLive(request.Payload),
            "apply_scene_diff" => ApplySceneDiffLive(request.Payload),
            _ => throw new InvalidOperationException($"Live-edit operation '{request.Operation}' is not supported.")
        };
    }

    private JsonObject ReloadSceneFromDisk(bool reloadAssets)
    {
        var scene = new RekallAgeSceneStore()
            .LoadAsync(_projectRoot, _sceneName, CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        ApplySceneDocument(scene);
        if (reloadAssets)
        {
            ReloadAssetsForCurrentWorld("Reloaded scene and assets.");
        }

        return CreateLiveStatus("reload_scene", true, reloadAssets ? "Reloaded scene and assets." : "Reloaded scene.");
    }

    private JsonObject ApplySceneBlueprintLive(JsonObject? payload)
    {
        var request = payload.Deserialize<LiveApplySceneBlueprintPayload>(LiveJsonOptions)
            ?? throw new JsonException("Live scene blueprint payload was null.");
        if (request.Entities.Count == 0)
        {
            throw new InvalidOperationException("Live scene blueprint must contain at least one entity.");
        }

        var updated = ApplySceneDelta(
            _sceneDocument,
            request.Entities,
            [],
            [],
            request.ClearExisting,
            out var upsertedCount,
            out var removedCount);
        if (request.PersistToProject)
        {
            new RekallAgeSceneStore()
                .SaveAsync(_projectRoot, updated, CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        ApplySceneDocument(updated);
        if (request.ReloadAssets)
        {
            ReloadAssetsForCurrentWorld("Applied live scene blueprint and reloaded assets.");
        }

        var status = CreateLiveStatus(
            "apply_scene_blueprint",
            true,
            request.PersistToProject
                ? "Applied live scene blueprint and persisted it to project scene storage."
                : "Applied live scene blueprint to the running player.");
        status["upsertedCount"] = upsertedCount;
        status["removedCount"] = removedCount;
        return status;
    }

    private JsonObject ApplySceneDiffLive(JsonObject? payload)
    {
        var request = payload.Deserialize<LiveApplySceneDiffPayload>(LiveJsonOptions)
            ?? throw new JsonException("Live scene diff payload was null.");
        var upserts = request.UpsertEntities ?? [];
        var deleteIds = request.DeleteEntityIds ?? [];
        var deleteNames = request.DeleteEntityNames ?? [];
        if (!request.ClearExisting && upserts.Count == 0 && deleteIds.Count == 0 && deleteNames.Count == 0)
        {
            throw new InvalidOperationException("Live scene diff must contain an upsert, delete, or clear operation.");
        }

        var updated = ApplySceneDelta(
            _sceneDocument,
            upserts,
            deleteIds,
            deleteNames,
            request.ClearExisting,
            out var upsertedCount,
            out var removedCount);
        if (request.PersistToProject)
        {
            new RekallAgeSceneStore()
                .SaveAsync(_projectRoot, updated, CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        ApplySceneDocument(updated);
        if (request.ReloadAssets)
        {
            ReloadAssetsForCurrentWorld("Applied live scene diff and reloaded assets.");
        }

        var status = CreateLiveStatus(
            "apply_scene_diff",
            true,
            request.PersistToProject
                ? "Applied live scene diff and persisted it to project scene storage."
                : "Applied live scene diff to the running player.");
        status["upsertedCount"] = upsertedCount;
        status["removedCount"] = removedCount;
        return status;
    }

    private void ApplySceneDocument(RekallAgeSceneDocument scene)
    {
        var initialWorld = new RekallAgeRuntimeWorldBuilder().Build(scene);
        var runResult = _runtimeLoop.RunAsync(initialWorld, 1, CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        _sceneDocument = scene;
        _runtimeWorld = runResult.World;
        _entityCount = _runtimeWorld.Entities.Count;
        _sceneRevision++;
        _simulationClock.Reset(_clock.Elapsed);
        _cachedStaticGeometry = null;
        _hudDirty = true;
    }

    private JsonObject ReloadAssetsForCurrentWorld(string message)
    {
        var frame = _frameBuilder.Build(
            _runtimeWorld,
            Math.Max(1, _window.Width),
            Math.Max(1, _window.Height),
            debugOverlay: true);
        var assets = new RekallAgeRuntimeViewportAssetResolver()
            .ResolveAsync(_projectRoot, frame, CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();

        _device.WaitForIdle();
        foreach (var materialSet in _materialSets.Values)
        {
            materialSet.Dispose();
        }

        _materialSets.Clear();
        foreach (var texture in _textures.Values)
        {
            texture.Dispose();
        }

        _textures.Clear();
        foreach (var item in CreateTextureBindings(_device, _factory, _hudTextureLayout, assets))
        {
            _textures[item.Key] = item.Value;
        }

        _assets = assets;
        _assetRevision++;
        _cachedStaticGeometry = null;
        _hudDirty = true;
        PlayerLog.Write($"Live assets reloaded images={assets.Images.Count} textures={assets.Textures.Count} models={assets.Models.Count} issues={assets.Issues.Count}.");
        return CreateLiveStatus("reload_assets", true, message);
    }

    private JsonObject CreateLiveStatus(string operation, bool applied, string message)
    {
        var frame = _frameBuilder.Build(
            _runtimeWorld,
            Math.Max(1, _window.Width),
            Math.Max(1, _window.Height),
            debugOverlay: true);
        return new JsonObject
        {
            ["sessionId"] = _sessionId,
            ["pipeName"] = _livePipeName,
            ["operation"] = operation,
            ["applied"] = applied,
            ["frameIndex"] = _frameIndex,
            ["entityCount"] = _entityCount,
            ["renderableCount"] = frame.Renderables.Count,
            ["sceneRevision"] = _sceneRevision,
            ["assetRevision"] = _assetRevision,
            ["message"] = message
        };
    }

    private static RekallAgeSceneDocument ApplySceneDelta(
        RekallAgeSceneDocument scene,
        IReadOnlyList<RekallAgeSceneBlueprintEntity> upserts,
        IReadOnlyList<string> deleteEntityIds,
        IReadOnlyList<string> deleteEntityNames,
        bool clearExisting,
        out int upsertedCount,
        out int removedCount)
    {
        var existing = clearExisting ? [] : scene.Entities.ToList();
        removedCount = clearExisting ? scene.Entities.Count : 0;
        upsertedCount = 0;

        if (!clearExisting)
        {
            var deleteIds = ToTrimmedSet(deleteEntityIds);
            var deleteNames = ToTrimmedSet(deleteEntityNames);
            if (deleteIds.Count > 0 || deleteNames.Count > 0)
            {
                var before = existing.Count;
                existing = existing
                    .Where(entity => !deleteIds.Contains(entity.Id) && !deleteNames.Contains(entity.Name))
                    .ToList();
                removedCount += before - existing.Count;
            }
        }

        foreach (var blueprint in upserts)
        {
            var entity = CreateEntity(blueprint);
            var replacementIndex = FindReplacementIndex(existing, blueprint);
            if (replacementIndex < 0)
            {
                existing.Add(entity);
            }
            else
            {
                existing[replacementIndex] = entity;
            }

            upsertedCount++;
        }

        return scene with
        {
            Entities = existing
                .OrderBy(entity => entity.Name, StringComparer.Ordinal)
                .ThenBy(entity => entity.Id, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static HashSet<string> ToTrimmedSet(IReadOnlyList<string> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToHashSet(StringComparer.Ordinal);
    }

    private static int FindReplacementIndex(List<RekallAgeEntityDocument> existing, RekallAgeSceneBlueprintEntity blueprint)
    {
        if (!string.IsNullOrWhiteSpace(blueprint.Id))
        {
            var byId = existing.FindIndex(entity => entity.Id.Equals(blueprint.Id, StringComparison.Ordinal));
            if (byId >= 0)
            {
                return byId;
            }
        }

        var nameMatches = existing
            .Select((entity, index) => (entity, index))
            .Where(item => item.entity.Name.Equals(blueprint.Name.Trim(), StringComparison.Ordinal))
            .ToArray();
        return nameMatches.Length == 1 ? nameMatches[0].index : -1;
    }

    private static RekallAgeEntityDocument CreateEntity(RekallAgeSceneBlueprintEntity blueprint)
    {
        var entity = RekallAgeEntityDocument.Create(blueprint.Name, blueprint.Tags ?? []);
        if (!string.IsNullOrWhiteSpace(blueprint.Id))
        {
            entity = entity with { Id = blueprint.Id.Trim() };
        }

        entity = entity with
        {
            ParentId = string.IsNullOrWhiteSpace(blueprint.ParentId) ? null : blueprint.ParentId.Trim(),
            Visible = blueprint.Visible ?? true,
            Locked = blueprint.Locked ?? false
        };

        foreach (var component in blueprint.Components ?? [])
        {
            entity = entity.AddComponent(RekallAgeComponentDocument.Create(component.Type, component.Properties));
        }

        return entity;
    }

    private static bool ReadBoolean(JsonObject? payload, string name, bool fallback)
    {
        return payload is not null
            && payload.TryGetPropertyValue(name, out var node)
            && node is JsonValue value
            && value.TryGetValue<bool>(out var boolean)
            ? boolean
            : fallback;
    }

    private void RenderFrame()
    {
        ProcessLiveEditQueue();
        ProcessAssetHotReload();
        var frameNumber = Interlocked.Increment(ref _frameIndex);
        AdvanceSimulationToWallClock();
        var frame = _frameBuilder.Build(
            _runtimeWorld,
            Math.Max(1, _window.Width),
            Math.Max(1, _window.Height),
            debugOverlay: true);
        EnsureSceneRenderTarget(frame.Width, frame.Height);
        var sceneFrame = frame with
        {
            Width = _sceneTarget.Width,
            Height = _sceneTarget.Height
        };
        var packet = GetRenderPacket(
            sceneFrame,
            useStaticGeometryCache: ShouldUseStaticGeometryCache(sceneFrame),
            out var verticesChanged);

        if (verticesChanged && packet.Vertices.Length > 0)
        {
            EnsureVertexBufferCapacity(packet.Vertices);
            _device.UpdateBuffer(_vertexBuffer, 0, packet.Vertices);
            EnsureIndexBufferCapacity(packet.Indices);
            _device.UpdateBuffer(_indexBuffer, 0, packet.Indices);
        }

        if (packet.Draws.Length > 0)
        {
            EnsureDrawUniformBufferCapacity(packet.Draws.Length);
            for (var i = 0; i < packet.Draws.Length; i++)
            {
                var draw = packet.Draws[i];
                _device.UpdateBuffer(
                    _drawUniformBuffer,
                    checked(_drawUniformStrideBytes * (uint)i),
                    new DrawUniform(draw.Model, draw.MaterialFactors, draw.EmissiveFactors));
            }
        }

        UpdateTitle(frameNumber, _clock.Elapsed.TotalSeconds, packet.Vertices.Length);
        var hudVertices = BuildHudVertices(frame.Width, frame.Height);
        if (_hudDirty)
        {
            UpdateHudTexture(BuildHudLines(frame, packet));
            _hudDirty = false;
        }

        if (hudVertices.Length > 0)
        {
            EnsureHudVertexBufferCapacity(hudVertices);
            _device.UpdateBuffer(_hudVertexBuffer, 0, hudVertices);
        }

        _commands.Begin();
        _commands.SetFramebuffer(_sceneTarget.Framebuffer);
        _commands.SetFullViewports();
        _commands.SetFullScissorRects();
        _commands.ClearColorTarget(0, new RgbaFloat(0.08f, 0.10f, 0.14f, 1f));
        _commands.ClearDepthStencil(1f);
        if (packet.Vertices.Length > 0)
        {
            _commands.SetPipeline(_scenePipeline);
            _commands.SetVertexBuffer(0, _vertexBuffer);
            _commands.SetIndexBuffer(_indexBuffer, IndexFormat.UInt32);
            _commands.SetGraphicsResourceSet(0, _frameSet);
            if (packet.StereoFrameUniforms.Count >= 2)
            {
                foreach (var stereoUniform in packet.StereoFrameUniforms)
                {
                    _device.UpdateBuffer(_frameUniformBuffer, 0, stereoUniform.Uniform);
                    _commands.SetViewport(0, new Viewport(
                        stereoUniform.Viewport.X,
                        stereoUniform.Viewport.Y,
                        Math.Max(1, stereoUniform.Viewport.Z),
                        Math.Max(1, stereoUniform.Viewport.W),
                        0,
                        1));
                    DrawScenePacket(packet);
                }
            }
            else
            {
                _device.UpdateBuffer(_frameUniformBuffer, 0, packet.FrameUniform);
                DrawScenePacket(packet);
            }
        }

        _commands.SetFramebuffer(_device.SwapchainFramebuffer);
        _commands.SetFullViewports();
        _commands.SetFullScissorRects();
        _commands.ClearColorTarget(0, new RgbaFloat(0.08f, 0.10f, 0.14f, 1f));
        _commands.SetPipeline(_presentPipeline);
        _commands.SetGraphicsResourceSet(0, _sceneTarget.ResourceSet);
        _commands.Draw(3);

        if (hudVertices.Length > 0)
        {
            _commands.SetPipeline(_hudPipeline);
            _commands.SetVertexBuffer(0, _hudVertexBuffer);
            _commands.SetGraphicsResourceSet(0, _hudTextureSet);
            _commands.Draw((uint)hudVertices.Length);
        }

        _commands.End();
        _device.SubmitCommands(_commands);
        _device.SwapBuffers();
    }

    private void DrawScenePacket(RenderPacket packet)
    {
        for (var i = 0; i < packet.Draws.Length; i++)
        {
            var draw = packet.Draws[i];
            var drawUniformOffset = checked(_drawUniformStrideBytes * (uint)i);
            _drawUniformDynamicOffsets[0] = drawUniformOffset;
            _commands.SetGraphicsResourceSet(1, _drawSet, _drawUniformDynamicOffsets);
            _commands.SetGraphicsResourceSet(2, ResolveMaterialSet(draw));
            _commands.DrawIndexed(draw.IndexCount, 1, draw.FirstIndex, draw.VertexOffset, 0);
        }
    }

    private void AdvanceSimulationToWallClock()
    {
        var result = _simulationClock.AdvanceToAsync(
                _runtimeWorld,
                _clock.Elapsed,
                CancellationToken.None,
                step => step == 0 ? ConsumeRuntimeInput() : RekallAgeRuntimeInputState.Empty)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        _runtimeWorld = result.World;
    }

    private RekallAgeRuntimeInputState ConsumeRuntimeInput()
    {
        var wheelDelta = _pendingMouseWheelDelta;
        var mouseDeltaX = _lastMousePosition.X - _previousMousePosition.X;
        var mouseDeltaY = _lastMousePosition.Y - _previousMousePosition.Y;
        if (wheelDelta == 0
            && mouseDeltaX == 0
            && mouseDeltaY == 0
            && _pressedKeys.Count == 0
            && _pressedButtons.Count == 0
            && _pressedKeysThisFrame.Count == 0
            && _releasedKeysThisFrame.Count == 0
            && _pressedButtonsThisFrame.Count == 0
            && _releasedButtonsThisFrame.Count == 0)
        {
            return _simulateXrInput
                ? RekallAgeXrInputSimulator.CreateFrame(RekallAgeRuntimeInputState.Empty, _clock.Elapsed)
                : RekallAgeRuntimeInputState.Empty;
        }

        var pressedKeysThisFrame = SnapshotSetOrNull(_pressedKeysThisFrame);
        var releasedKeysThisFrame = SnapshotSetOrNull(_releasedKeysThisFrame);
        var pressedButtonsThisFrame = SnapshotSetOrNull(_pressedButtonsThisFrame);
        var releasedButtonsThisFrame = SnapshotSetOrNull(_releasedButtonsThisFrame);
        var pressedKeys = SnapshotSetOrNull(_pressedKeys);
        var pressedButtons = SnapshotSetOrNull(_pressedButtons);
        _pendingMouseWheelDelta = 0;
        _pressedKeysThisFrame.Clear();
        _releasedKeysThisFrame.Clear();
        _pressedButtonsThisFrame.Clear();
        _releasedButtonsThisFrame.Clear();
        var input = new RekallAgeRuntimeInputState(
            MouseX: _lastMousePosition.X,
            MouseY: _lastMousePosition.Y,
            MouseDeltaX: mouseDeltaX,
            MouseDeltaY: mouseDeltaY,
            MouseWheelDelta: wheelDelta,
            PressedKeys: pressedKeys,
            PressedKeysThisFrame: pressedKeysThisFrame,
            ReleasedKeysThisFrame: releasedKeysThisFrame,
            PressedButtons: pressedButtons,
            PressedButtonsThisFrame: pressedButtonsThisFrame,
            ReleasedButtonsThisFrame: releasedButtonsThisFrame);
        return _simulateXrInput
            ? RekallAgeXrInputSimulator.CreateFrame(input, _clock.Elapsed)
            : input;
    }

    private static IReadOnlySet<string>? SnapshotSetOrNull(HashSet<string> source)
    {
        return source.Count == 0
            ? null
            : source.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private RenderPacket GetRenderPacket(
        Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportFrame frame,
        bool useStaticGeometryCache,
        out bool changed)
    {
        if (useStaticGeometryCache
            && _cachedStaticGeometry is not null
            && _cachedStaticGeometry.Key.Equals(CreateGeometryCacheKey(frame)))
        {
            var packet = BuildRenderPacket(frame, _cachedStaticGeometry, out _);
            changed = false;
            return packet;
        }

        var result = BuildRenderPacket(frame, null, out var geometry);
        if (useStaticGeometryCache && geometry is not null)
        {
            _cachedStaticGeometry = geometry with { Key = CreateGeometryCacheKey(frame) };
        }

        changed = true;
        return result;
    }

    private bool ShouldUseStaticGeometryCache(Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportFrame frame)
    {
        foreach (var renderable in frame.Renderables)
        {
            if (renderable.Kind.Equals("mesh", StringComparison.Ordinal)
                && (renderable.AssetId is not null
                    || renderable.Variant is not null
                    || renderable.GeometryMesh is not null
                    || renderable.LineSegments is not null))
            {
                return true;
            }
        }

        return false;
    }

    private RenderPacket BuildRenderPacket(
        Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportFrame frame,
        CachedRenderGeometry? cachedGeometry,
        out CachedRenderGeometry? builtGeometry)
    {
        builtGeometry = null;
        var meshes = cachedGeometry?.Meshes ?? _meshBuilder.BuildMeshes(frame, _assets);
        if (meshes.Count == 0)
        {
            return new RenderPacket([], [], [], default, [], 0, 0, 0);
        }

        var batch = _batchBuilder.Build(frame, meshes);
        var vertices = cachedGeometry?.Vertices;
        if (vertices is null)
        {
            vertices = new GpuVertex[batch.Vertices.Count];
            for (var i = 0; i < batch.Vertices.Count; i++)
            {
                var vertex = batch.Vertices[i];
                vertices[i] = new GpuVertex(
                    new Vector3(vertex.X, vertex.Y, vertex.Z),
                    new Vector3(vertex.NormalX, vertex.NormalY, vertex.NormalZ),
                    new Vector4(vertex.R, vertex.G, vertex.B, vertex.A),
                    new Vector2(vertex.U, vertex.V));
            }
        }

        var drawList = new List<GpuDraw>(batch.Draws.Count);
        for (var i = 0; i < batch.Draws.Count; i++)
        {
            var draw = batch.Draws[i];
            if (draw.IndexCount == 0)
            {
                continue;
            }

            drawList.Add(new GpuDraw(
                draw.FirstIndex,
                draw.IndexCount,
                draw.VertexOffset,
                draw.Model,
                draw.TextureId,
                draw.MetallicRoughnessTextureId,
                draw.NormalTextureId,
                draw.OcclusionTextureId,
                draw.EmissiveTextureId,
                draw.MaterialFactors,
                draw.EmissiveFactors));
        }

        var indices = cachedGeometry?.Indices;
        if (indices is null)
        {
            indices = new uint[batch.Indices.Count];
            for (var i = 0; i < batch.Indices.Count; i++)
            {
                indices[i] = batch.Indices[i];
            }
        }

        var meshCount = cachedGeometry?.MeshCount ?? meshes.Count;
        var triangleCount = cachedGeometry?.TriangleCount;
        var textureCount = cachedGeometry?.TextureCount;
        if (triangleCount is null || textureCount is null)
        {
            var textureIds = new HashSet<string>(StringComparer.Ordinal);
            var triangles = 0;
            foreach (var mesh in meshes)
            {
                triangles += mesh.Indices.Count / 3;
                AddTextureId(textureIds, mesh.BaseColorTexture?.Id);
                AddTextureId(textureIds, mesh.MetallicRoughnessTexture?.Id);
                AddTextureId(textureIds, mesh.NormalTexture?.Id);
                AddTextureId(textureIds, mesh.OcclusionTexture?.Id);
                AddTextureId(textureIds, mesh.EmissiveTexture?.Id);
            }

            triangleCount = triangles;
            textureCount = textureIds.Count;
        }

        if (cachedGeometry is null)
        {
            builtGeometry = new CachedRenderGeometry(
                default,
                meshes,
                vertices,
                indices,
                meshCount,
                triangleCount.Value,
                textureCount.Value);
        }

        return new RenderPacket(
            vertices,
            indices,
            drawList.ToArray(),
            new FrameUniform(
                batch.Frame.ViewProjection,
                new Vector4(batch.Frame.LightDirection, 0),
                batch.Frame.LightColor,
                batch.Frame.LightPosition),
            BuildStereoUniforms(batch),
            meshCount,
            triangleCount.Value,
            textureCount.Value);
    }

    private GeometryCacheKey CreateGeometryCacheKey(Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportFrame frame)
    {
        var hash = new HashCode();
        var meshRenderableCount = 0;
        foreach (var renderable in frame.Renderables)
        {
            if (!renderable.Kind.Equals("mesh", StringComparison.Ordinal))
            {
                continue;
            }

            meshRenderableCount++;
            hash.Add(renderable.EntityId, StringComparer.Ordinal);
            hash.Add(renderable.AssetId, StringComparer.Ordinal);
            hash.Add(renderable.Variant, StringComparer.Ordinal);
            hash.Add(renderable.MaterialColor, StringComparer.Ordinal);
            hash.Add(renderable.TextureAssetId, StringComparer.Ordinal);
            hash.Add(renderable.MetallicRoughnessTextureAssetId, StringComparer.Ordinal);
            hash.Add(renderable.NormalTextureAssetId, StringComparer.Ordinal);
            hash.Add(renderable.OcclusionTextureAssetId, StringComparer.Ordinal);
            hash.Add(renderable.EmissiveColor, StringComparer.Ordinal);
            hash.Add(renderable.EmissiveTextureAssetId, StringComparer.Ordinal);
            hash.Add(renderable.MetallicFactor);
            hash.Add(renderable.RoughnessFactor);
            hash.Add(renderable.NormalScale);
            hash.Add(renderable.OcclusionStrength);
            hash.Add(renderable.EmissiveStrength);
            hash.Add(renderable.GeometryMesh?.Vertices.Count ?? 0);
            hash.Add(renderable.GeometryMesh?.Indices.Count ?? 0);
            hash.Add(renderable.GeometryMesh is null ? 0 : RuntimeHelpers.GetHashCode(renderable.GeometryMesh));
            hash.Add(renderable.LineSegments?.Segments.Count ?? 0);
            hash.Add(renderable.LineSegments?.Thickness ?? 0);
            hash.Add(renderable.LineSegments is null ? 0 : RuntimeHelpers.GetHashCode(renderable.LineSegments));
        }

        return new GeometryCacheKey(
            _sceneRevision,
            _assetRevision,
            frame.Width,
            frame.Height,
            meshRenderableCount,
            hash.ToHashCode());
    }

    private static IReadOnlyList<StereoFrameUniform> BuildStereoUniforms(RekallAgeVulkanSceneBatch batch)
    {
        if (batch.Stereo is not { Enabled: true } stereo || stereo.Views.Count < 2)
        {
            return [];
        }

        var uniforms = new StereoFrameUniform[stereo.Views.Count];
        for (var i = 0; i < stereo.Views.Count; i++)
        {
            var view = stereo.Views[i];
            uniforms[i] = new StereoFrameUniform(
                view.Name,
                view.Index,
                new FrameUniform(
                    view.ViewProjection,
                    new Vector4(batch.Frame.LightDirection, 0),
                    batch.Frame.LightColor,
                    batch.Frame.LightPosition),
                view.Viewport);
        }

        return uniforms;
    }

    private static void AddTextureId(HashSet<string> textureIds, string? textureId)
    {
        if (!string.IsNullOrWhiteSpace(textureId))
        {
            textureIds.Add(textureId);
        }
    }

    private void EnsureVertexBufferCapacity(IReadOnlyCollection<GpuVertex> vertices)
    {
        var requiredBytes = checked((uint)(vertices.Count * Marshal.SizeOf<GpuVertex>()));
        if (requiredBytes <= _vertexBufferCapacityBytes)
        {
            return;
        }

        var newCapacity = _vertexBufferCapacityBytes;
        while (newCapacity < requiredBytes)
        {
            newCapacity = checked(newCapacity * 2);
        }

        _device.WaitForIdle();
        _vertexBuffer.Dispose();
        _vertexBuffer = _factory.CreateBuffer(new BufferDescription(
            newCapacity,
            BufferUsage.VertexBuffer | BufferUsage.Dynamic));
        _vertexBufferCapacityBytes = newCapacity;
        PlayerLog.Write($"Resized dynamic vertex buffer to {newCapacity} bytes for {vertices.Count} vertices.");
    }

    private void EnsureIndexBufferCapacity(IReadOnlyCollection<uint> indices)
    {
        var requiredBytes = checked((uint)(indices.Count * sizeof(uint)));
        if (requiredBytes <= _indexBufferCapacityBytes)
        {
            return;
        }

        var newCapacity = _indexBufferCapacityBytes;
        while (newCapacity < requiredBytes)
        {
            newCapacity = checked(newCapacity * 2);
        }

        _device.WaitForIdle();
        _indexBuffer.Dispose();
        _indexBuffer = _factory.CreateBuffer(new BufferDescription(
            newCapacity,
            BufferUsage.IndexBuffer | BufferUsage.Dynamic));
        _indexBufferCapacityBytes = newCapacity;
        PlayerLog.Write($"Resized dynamic index buffer to {newCapacity} bytes for {indices.Count} indices.");
    }

    private void EnsureHudVertexBufferCapacity(IReadOnlyCollection<HudVertex> vertices)
    {
        var requiredBytes = checked((uint)(vertices.Count * Marshal.SizeOf<HudVertex>()));
        if (requiredBytes <= _hudVertexBufferCapacityBytes)
        {
            return;
        }

        var newCapacity = _hudVertexBufferCapacityBytes;
        while (newCapacity < requiredBytes)
        {
            newCapacity = checked(newCapacity * 2);
        }

        _device.WaitForIdle();
        _hudVertexBuffer.Dispose();
        _hudVertexBuffer = _factory.CreateBuffer(new BufferDescription(
            newCapacity,
            BufferUsage.VertexBuffer | BufferUsage.Dynamic));
        _hudVertexBufferCapacityBytes = newCapacity;
    }

    private void EnsureDrawUniformBufferCapacity(int drawCount)
    {
        var requiredBytes = checked(_drawUniformStrideBytes * (uint)Math.Max(1, drawCount));
        if (requiredBytes <= _drawUniformBufferCapacityBytes)
        {
            return;
        }

        var newCapacity = _drawUniformBufferCapacityBytes;
        while (newCapacity < requiredBytes)
        {
            newCapacity = checked(newCapacity * 2);
        }

        _device.WaitForIdle();
        _drawSet.Dispose();
        _drawUniformBuffer.Dispose();
        _drawUniformBuffer = _factory.CreateBuffer(new BufferDescription(
            newCapacity,
            BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        _drawSet = _factory.CreateResourceSet(new ResourceSetDescription(_drawLayout, _drawUniformBuffer));
        _drawUniformBufferCapacityBytes = newCapacity;
        PlayerLog.Write($"Resized dynamic draw uniform buffer to {newCapacity} bytes for {drawCount} draw(s).");
    }

    private IReadOnlyList<string> BuildHudLines(
        Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportFrame frame,
        RenderPacket packet)
    {
        var stats = new RekallAgeSceneDebugHudStats(
            frame.SceneName,
            _entityCount,
            frame.Renderables.Count,
            frame.Renderables.Count(renderable => renderable.EntityId.EndsWith(":collider", StringComparison.Ordinal)),
            packet.MeshCount,
            packet.TriangleCount,
            packet.TextureCount,
            packet.Draws.Length,
            packet.Vertices.Length,
            _fps,
            BuildBackendHudLine());
        return RekallAgeSceneDebugHud.FormatLines(stats);
    }

    private string BuildBackendHudLine()
    {
        var baseLine = $"{_device.BackendType} {SceneSupersampleFactor}xSSAA";
        var suffixes = new List<string>();
        if (_simulateXrInput)
        {
            suffixes.Add("XR SIM");
        }

        if (_openXrStatus is not null)
        {
            suffixes.Add(_openXrStatus.HeadsetSessionReady
                ? "OXR READY"
                : "OXR WAIT");
        }

        if (_openXrVulkanInterop is not null)
        {
            suffixes.Add(_openXrVulkanInterop.ReadyForCompositorSession
                ? "CMP READY"
                : "CMP WAIT");
        }

        if (_openXrCompositorSession is not null)
        {
            suffixes.Add(_openXrCompositorSession.FrameLoopReady
                ? "SES READY"
                : "SES WAIT");
        }

        if (suffixes.Count == 0)
        {
            return baseLine;
        }

        return $"{baseLine} {string.Join(' ', suffixes)}";
    }

    private void UpdateHudTexture(IReadOnlyList<string> lines)
    {
        using var bitmap = new DrawingBitmap(HudWidth, HudHeight, DrawingPixelFormat.Format32bppArgb);
        using (var graphics = DrawingGraphics.FromImage(bitmap))
        using (var font = new DrawingFont("Consolas", 10.5f, DrawingFontStyle.Regular, DrawingGraphicsUnit.Point))
        using (var brush = new DrawingSolidBrush(DrawingColor.FromArgb(232, 238, 244, 252)))
        using (var background = new DrawingSolidBrush(DrawingColor.FromArgb(172, 8, 12, 18)))
        using (var accent = new DrawingSolidBrush(DrawingColor.FromArgb(218, 80, 170, 255)))
        {
            graphics.Clear(DrawingColor.Transparent);
            graphics.FillRectangle(background, 0, 0, HudWidth, HudHeight);
            graphics.FillRectangle(accent, 0, 0, 3, HudHeight);
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            var y = 10f;
            foreach (var line in lines)
            {
                graphics.DrawString(line, font, brush, 12, y);
                y += 19f;
            }
        }

        var rgba = ReadBitmapRgba(bitmap);
        _device.UpdateTexture(
            _hudTexture.Texture,
            rgba,
            0,
            0,
            0,
            HudWidth,
            HudHeight,
            1,
            0,
            0);
    }

    private static byte[] ReadBitmapRgba(DrawingBitmap bitmap)
    {
        var data = bitmap.LockBits(
            new DrawingRectangle(0, 0, bitmap.Width, bitmap.Height),
            DrawingImageLockMode.ReadOnly,
            DrawingPixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[checked(data.Stride * data.Height)];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var rgba = new byte[checked(bitmap.Width * bitmap.Height * 4)];
            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var source = y * data.Stride + x * 4;
                    var target = (y * bitmap.Width + x) * 4;
                    rgba[target] = bytes[source + 2];
                    rgba[target + 1] = bytes[source + 1];
                    rgba[target + 2] = bytes[source];
                    rgba[target + 3] = bytes[source + 3];
                }
            }

            return rgba;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static HudVertex[] BuildHudVertices(int width, int height)
    {
        if (width <= HudMargin * 2 || height <= HudMargin * 2)
        {
            return [];
        }

        var x0 = Math.Max(HudMargin, width - HudWidth - HudMargin);
        var y0 = HudMargin;
        var x1 = Math.Min(width - HudMargin, x0 + HudWidth);
        var y1 = Math.Min(height - HudMargin, y0 + HudHeight);
        var left = ToClipX(x0, width);
        var right = ToClipX(x1, width);
        var top = ToClipY(y0, height);
        var bottom = ToClipY(y1, height);
        var color = Vector4.One;
        return
        [
            new HudVertex(new Vector3(left, top, 0), color, new Vector2(0, 0)),
            new HudVertex(new Vector3(right, top, 0), color, new Vector2(1, 0)),
            new HudVertex(new Vector3(right, bottom, 0), color, new Vector2(1, 1)),
            new HudVertex(new Vector3(left, top, 0), color, new Vector2(0, 0)),
            new HudVertex(new Vector3(right, bottom, 0), color, new Vector2(1, 1)),
            new HudVertex(new Vector3(left, bottom, 0), color, new Vector2(0, 1))
        ];
    }

    private static float ToClipX(float x, int width)
    {
        return x / Math.Max(1, width) * 2f - 1f;
    }

    private static float ToClipY(float y, int height)
    {
        return 1f - y / Math.Max(1, height) * 2f;
    }

    private static uint AlignTo(uint value, uint alignment)
    {
        if (alignment <= 1)
        {
            return value;
        }

        var remainder = value % alignment;
        return remainder == 0
            ? value
            : checked(value + alignment - remainder);
    }

    private ResourceSet ResolveMaterialSet(GpuDraw draw)
    {
        var key = new MaterialKey(
            draw.TextureId,
            draw.NormalTextureId,
            draw.MetallicRoughnessTextureId,
            draw.OcclusionTextureId,
            draw.EmissiveTextureId);
        if (_materialSets.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var baseColor = ResolveTexture(draw.TextureId, _whiteTexture);
        var normal = ResolveTexture(draw.NormalTextureId, _flatNormalTexture);
        var metallicRoughness = ResolveTexture(draw.MetallicRoughnessTextureId, _defaultMetallicRoughnessTexture);
        var occlusion = ResolveTexture(draw.OcclusionTextureId, _whiteTexture);
        var emissive = ResolveTexture(draw.EmissiveTextureId, _whiteTexture);
        var resourceSet = _factory.CreateResourceSet(new ResourceSetDescription(
            _materialLayout,
            baseColor.Texture,
            baseColor.Sampler,
            normal.Texture,
            normal.Sampler,
            metallicRoughness.Texture,
            metallicRoughness.Sampler,
            occlusion.Texture,
            occlusion.Sampler,
            emissive.Texture,
            emissive.Sampler));
        _materialSets[key] = resourceSet;
        return resourceSet;
    }

    private TextureBinding ResolveTexture(string? textureId, TextureBinding fallback)
    {
        return textureId is not null && _textures.TryGetValue(textureId, out var texture)
            ? texture
            : fallback;
    }

    private static SceneRenderTarget CreateSceneRenderTarget(
        ResourceFactory factory,
        int displayWidth,
        int displayHeight,
        ResourceLayout presentTextureLayout)
    {
        var width = checked((uint)Math.Max(1, displayWidth * SceneSupersampleFactor));
        var height = checked((uint)Math.Max(1, displayHeight * SceneSupersampleFactor));
        var color = factory.CreateTexture(TextureDescription.Texture2D(
            width,
            height,
            mipLevels: 1,
            arrayLayers: 1,
            PixelFormat.R8_G8_B8_A8_UNorm,
            TextureUsage.RenderTarget | TextureUsage.Sampled));
        var depth = factory.CreateTexture(TextureDescription.Texture2D(
            width,
            height,
            mipLevels: 1,
            arrayLayers: 1,
            PixelFormat.D24_UNorm_S8_UInt,
            TextureUsage.DepthStencil));
        var framebuffer = factory.CreateFramebuffer(new FramebufferDescription(depth, color));
        var sampler = factory.CreateSampler(new SamplerDescription(
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            SamplerFilter.MinLinear_MagLinear_MipLinear,
            ComparisonKind.Never,
            maximumAnisotropy: 1,
            minimumLod: 0,
            maximumLod: 0,
            lodBias: 0,
            borderColor: SamplerBorderColor.TransparentBlack));
        var resourceSet = factory.CreateResourceSet(new ResourceSetDescription(presentTextureLayout, color, sampler));
        return new SceneRenderTarget(
            displayWidth,
            displayHeight,
            checked((int)width),
            checked((int)height),
            color,
            depth,
            framebuffer,
            sampler,
            resourceSet);
    }

    private static Dictionary<string, TextureBinding> CreateTextureBindings(
        GraphicsDevice device,
        ResourceFactory factory,
        ResourceLayout layout,
        RekallAgeRuntimeViewportAssetSet assets)
    {
        var textures = new Dictionary<string, TextureBinding>(StringComparer.Ordinal);
        foreach (var image in assets.Images)
        {
            textures[image.Key] = CreateTextureBinding(
                device,
                factory,
                new RekallAgeVulkanSceneTexture(
                    image.Key,
                    image.Value.Width,
                    image.Value.Height,
                    image.Value.Rgba,
                    DefaultTextureSampler()),
                layout);
        }

        foreach (var runtimeTexture in assets.Textures)
        {
            var decoded = RekallAgeBlockCompressedTextureDecoder.TryDecodeTopLevel(runtimeTexture.Value);
            if (decoded is not null)
            {
                textures[runtimeTexture.Key] = CreateTextureBinding(
                    device,
                    factory,
                    new RekallAgeVulkanSceneTexture(
                        runtimeTexture.Key,
                        decoded.Width,
                        decoded.Height,
                        decoded.Rgba,
                        DefaultTextureSampler()),
                    layout);
                PlayerLog.Write($"Decoded runtime texture id={runtimeTexture.Key} format={runtimeTexture.Value.Format} size={decoded.Width}x{decoded.Height} to RGBA upload.");
                continue;
            }

            textures[runtimeTexture.Key] = CreateTextureBinding(
                device,
                factory,
                new RekallAgeVulkanSceneTexture(
                    runtimeTexture.Key,
                    runtimeTexture.Value.Width,
                    runtimeTexture.Value.Height,
                    [],
                    DefaultTextureSampler(),
                    runtimeTexture.Value),
                layout);
        }

        foreach (var texture in assets.Models.Values
            .SelectMany(meshes => meshes)
            .SelectMany(mesh => new[]
            {
                mesh.BaseColorTexture,
                mesh.MetallicRoughnessTexture,
                mesh.NormalTexture,
                mesh.OcclusionTexture,
                mesh.EmissiveTexture
            })
            .OfType<RekallAgeVulkanSceneTexture>()
            .GroupBy(texture => texture.Id, StringComparer.Ordinal)
            .Select(group => group.First()))
        {
            if (!textures.ContainsKey(texture.Id))
            {
                textures[texture.Id] = CreateTextureBinding(device, factory, texture, layout);
            }
        }

        PlayerLog.Write($"Created texture resources count={textures.Count}.");
        return textures;
    }

    private static RekallAgeVulkanSceneSampler DefaultTextureSampler()
    {
        return new RekallAgeVulkanSceneSampler(
            RekallAgeVulkanSceneFilter.Linear,
            RekallAgeVulkanSceneFilter.Linear,
            RekallAgeVulkanSceneWrapMode.Repeat,
            RekallAgeVulkanSceneWrapMode.Repeat);
    }

    private static TextureBinding CreateTextureBinding(
        GraphicsDevice device,
        ResourceFactory factory,
        RekallAgeVulkanSceneTexture texture,
        ResourceLayout layout)
    {
        if (texture.RuntimeTexture is { } runtimeTexture
            && TryGetTexturePixelFormat(runtimeTexture.Format, out var runtimeFormat)
            && runtimeTexture.MipLevels.Count > 0)
        {
            return CreateRuntimeTextureBinding(device, factory, texture, runtimeTexture, runtimeFormat, layout);
        }

        var mipLevels = CalculateMipLevels(texture.Width, texture.Height);
        var gpuTexture = factory.CreateTexture(TextureDescription.Texture2D(
            checked((uint)texture.Width),
            checked((uint)texture.Height),
            mipLevels: mipLevels,
            arrayLayers: 1,
            PixelFormat.R8_G8_B8_A8_UNorm,
            TextureUsage.Sampled | TextureUsage.GenerateMipmaps));
        device.UpdateTexture(
            gpuTexture,
            texture.Rgba,
            x: 0,
            y: 0,
            z: 0,
            width: checked((uint)texture.Width),
            height: checked((uint)texture.Height),
            depth: 1,
            mipLevel: 0,
            arrayLayer: 0);
        if (mipLevels > 1)
        {
            using var commands = factory.CreateCommandList();
            commands.Begin();
            commands.GenerateMipmaps(gpuTexture);
            commands.End();
            device.SubmitCommands(commands);
            device.WaitForIdle();
        }

        var filter = ToSamplerFilter(texture.Sampler.MinFilter, texture.Sampler.MagFilter, device.Features.SamplerAnisotropy);
        var sampler = factory.CreateSampler(new SamplerDescription(
            ToSamplerAddressMode(texture.Sampler.WrapS),
            ToSamplerAddressMode(texture.Sampler.WrapT),
            SamplerAddressMode.Wrap,
            filter,
            ComparisonKind.Never,
            maximumAnisotropy: filter == SamplerFilter.Anisotropic ? 8u : 1u,
            minimumLod: 0,
            maximumLod: mipLevels - 1,
            lodBias: 0,
            borderColor: SamplerBorderColor.TransparentBlack));
        var resourceSet = factory.CreateResourceSet(new ResourceSetDescription(layout, gpuTexture, sampler));
        return new TextureBinding(gpuTexture, sampler, resourceSet);
    }

    private static TextureBinding CreateRuntimeTextureBinding(
        GraphicsDevice device,
        ResourceFactory factory,
        RekallAgeVulkanSceneTexture texture,
        RekallAgeRuntimeTextureAsset runtimeTexture,
        PixelFormat format,
        ResourceLayout layout)
    {
        var mipLevels = checked((uint)Math.Max(1, runtimeTexture.MipLevels.Count));
        var gpuTexture = factory.CreateTexture(TextureDescription.Texture2D(
            checked((uint)runtimeTexture.Width),
            checked((uint)runtimeTexture.Height),
            mipLevels: mipLevels,
            arrayLayers: 1,
            format,
            TextureUsage.Sampled));
        foreach (var mip in runtimeTexture.MipLevels.OrderBy(mip => mip.Level))
        {
            device.UpdateTexture(
                gpuTexture,
                mip.Bytes,
                x: 0,
                y: 0,
                z: 0,
                width: checked((uint)mip.Width),
                height: checked((uint)mip.Height),
                depth: 1,
                mipLevel: checked((uint)mip.Level),
                arrayLayer: 0);
        }

        var filter = ToSamplerFilter(texture.Sampler.MinFilter, texture.Sampler.MagFilter, device.Features.SamplerAnisotropy);
        var sampler = factory.CreateSampler(new SamplerDescription(
            ToSamplerAddressMode(texture.Sampler.WrapS),
            ToSamplerAddressMode(texture.Sampler.WrapT),
            SamplerAddressMode.Wrap,
            filter,
            ComparisonKind.Never,
            maximumAnisotropy: filter == SamplerFilter.Anisotropic ? 8u : 1u,
            minimumLod: 0,
            maximumLod: mipLevels - 1,
            lodBias: 0,
            borderColor: SamplerBorderColor.TransparentBlack));
        var resourceSet = factory.CreateResourceSet(new ResourceSetDescription(layout, gpuTexture, sampler));
        PlayerLog.Write($"Uploaded runtime texture id={texture.Id} format={runtimeTexture.Format} size={runtimeTexture.Width}x{runtimeTexture.Height} mips={runtimeTexture.MipLevels.Count}.");
        return new TextureBinding(gpuTexture, sampler, resourceSet);
    }

    private static uint CalculateMipLevels(int width, int height)
    {
        var largest = Math.Max(1, Math.Max(width, height));
        var levels = 1u;
        while (largest > 1)
        {
            largest /= 2;
            levels++;
        }

        return levels;
    }

    private static SamplerAddressMode ToSamplerAddressMode(RekallAgeVulkanSceneWrapMode mode)
    {
        return mode switch
        {
            RekallAgeVulkanSceneWrapMode.ClampToEdge => SamplerAddressMode.Clamp,
            RekallAgeVulkanSceneWrapMode.MirroredRepeat => SamplerAddressMode.Mirror,
            _ => SamplerAddressMode.Wrap
        };
    }

    private static SamplerFilter ToSamplerFilter(
        RekallAgeVulkanSceneFilter minFilter,
        RekallAgeVulkanSceneFilter magFilter,
        bool supportsAnisotropy)
    {
        if (minFilter == RekallAgeVulkanSceneFilter.Nearest
            && magFilter == RekallAgeVulkanSceneFilter.Nearest)
        {
            return SamplerFilter.MinPoint_MagPoint_MipPoint;
        }

        return supportsAnisotropy
            ? SamplerFilter.Anisotropic
            : SamplerFilter.MinLinear_MagLinear_MipLinear;
    }

    private static bool TryGetTexturePixelFormat(string? format, out PixelFormat pixelFormat)
    {
        var resolved = format switch
        {
            "BC1_UNorm" or "VK_FORMAT_BC1_RGB_UNORM_BLOCK" or "VK_FORMAT_BC1_RGBA_UNORM_BLOCK" => (PixelFormat?)PixelFormat.BC1_Rgba_UNorm,
            "VK_FORMAT_BC1_RGB_SRGB_BLOCK" or "VK_FORMAT_BC1_RGBA_SRGB_BLOCK" => PixelFormat.BC1_Rgba_UNorm_SRgb,
            "BC2_UNorm" or "VK_FORMAT_BC2_UNORM_BLOCK" => PixelFormat.BC2_UNorm,
            "VK_FORMAT_BC2_SRGB_BLOCK" => PixelFormat.BC2_UNorm_SRgb,
            "BC3_UNorm" or "VK_FORMAT_BC3_UNORM_BLOCK" => PixelFormat.BC3_UNorm,
            "VK_FORMAT_BC3_SRGB_BLOCK" => PixelFormat.BC3_UNorm_SRgb,
            "BC4_UNorm" or "VK_FORMAT_BC4_UNORM_BLOCK" => PixelFormat.BC4_UNorm,
            "VK_FORMAT_BC4_SNORM_BLOCK" => PixelFormat.BC4_SNorm,
            "BC5_UNorm" or "VK_FORMAT_BC5_UNORM_BLOCK" => PixelFormat.BC5_UNorm,
            "VK_FORMAT_BC5_SNORM_BLOCK" => PixelFormat.BC5_SNorm,
            "VK_FORMAT_BC7_UNORM_BLOCK" => PixelFormat.BC7_UNorm,
            "VK_FORMAT_BC7_SRGB_BLOCK" => PixelFormat.BC7_UNorm_SRgb,
            _ => null
        };
        pixelFormat = resolved.GetValueOrDefault();
        return resolved.HasValue;
    }

    private void UpdateTitle(int frameNumber, double elapsedSeconds, int vertexCount)
    {
        if (elapsedSeconds - _lastFpsTime >= 0.5)
        {
            _fps = (int)Math.Round((frameNumber - _lastFpsFrame) / Math.Max(0.001, elapsedSeconds - _lastFpsTime));
            _lastFpsFrame = frameNumber;
            _lastFpsTime = elapsedSeconds;
            _hudDirty = true;
            PlayerLog.Write($"Frame={frameNumber} Fps={_fps} Vertices={vertexCount} Backend={_device.BackendType} Window={_window.Width}x{_window.Height}");
        }
    }

    private const string SceneVertexShader = """
        #version 450

        layout(location = 0) in vec3 Position;
        layout(location = 1) in vec3 Normal;
        layout(location = 2) in vec4 Color;
        layout(location = 3) in vec2 UV;

        layout(set = 0, binding = 0) uniform FrameUniformBuffer
        {
            mat4 ViewProjection;
            vec4 LightDirection;
            vec4 LightColor;
            vec4 LightPosition;
        } Frame;

        layout(set = 1, binding = 0) uniform DrawUniformBuffer
        {
            mat4 Model;
            vec4 MaterialFactors;
            vec4 EmissiveFactors;
        } Draw;

        layout(location = 0) out vec3 fsin_Normal;
        layout(location = 1) out vec4 fsin_Color;
        layout(location = 2) out vec2 fsin_UV;
        layout(location = 3) out vec3 fsin_WorldPosition;

        void main()
        {
            vec4 worldPosition = Draw.Model * vec4(Position, 1.0);
            gl_Position = Frame.ViewProjection * worldPosition;
            fsin_Normal = mat3(Draw.Model) * Normal;
            fsin_Color = Color;
            fsin_UV = UV;
            fsin_WorldPosition = worldPosition.xyz;
        }
        """;

    private const string SceneFragmentShader = """
        #version 450

        layout(location = 0) in vec3 fsin_Normal;
        layout(location = 1) in vec4 fsin_Color;
        layout(location = 2) in vec2 fsin_UV;
        layout(location = 3) in vec3 fsin_WorldPosition;

        layout(set = 0, binding = 0) uniform FrameUniformBuffer
        {
            mat4 ViewProjection;
            vec4 LightDirection;
            vec4 LightColor;
            vec4 LightPosition;
        } Frame;

        layout(set = 1, binding = 0) uniform DrawUniformBuffer
        {
            mat4 Model;
            vec4 MaterialFactors;
            vec4 EmissiveFactors;
        } Draw;

        layout(set = 2, binding = 0) uniform texture2D BaseColorTexture;
        layout(set = 2, binding = 1) uniform sampler BaseColorSampler;
        layout(set = 2, binding = 2) uniform texture2D NormalTexture;
        layout(set = 2, binding = 3) uniform sampler NormalSampler;
        layout(set = 2, binding = 4) uniform texture2D MetallicRoughnessTexture;
        layout(set = 2, binding = 5) uniform sampler MetallicRoughnessSampler;
        layout(set = 2, binding = 6) uniform texture2D OcclusionTexture;
        layout(set = 2, binding = 7) uniform sampler OcclusionSampler;
        layout(set = 2, binding = 8) uniform texture2D EmissiveTexture;
        layout(set = 2, binding = 9) uniform sampler EmissiveSampler;

        layout(location = 0) out vec4 fsout_Color;

        const float PI = 3.14159265359;

        vec3 perturbNormal(vec3 normal)
        {
            vec3 tangentNormal = texture(sampler2D(NormalTexture, NormalSampler), fsin_UV).xyz * 2.0 - 1.0;
            tangentNormal.xy *= Draw.MaterialFactors.z;
            vec3 q1 = dFdx(fsin_WorldPosition);
            vec3 q2 = dFdy(fsin_WorldPosition);
            vec2 st1 = dFdx(fsin_UV);
            vec2 st2 = dFdy(fsin_UV);
            vec3 tangent = normalize(q1 * st2.t - q2 * st1.t);
            vec3 bitangent = normalize(-q1 * st2.s + q2 * st1.s);
            mat3 tbn = mat3(tangent, bitangent, normal);
            return normalize(tbn * tangentNormal);
        }

        float distributionGgx(vec3 normal, vec3 halfVector, float roughness)
        {
            float a = roughness * roughness;
            float a2 = a * a;
            float ndoth = max(dot(normal, halfVector), 0.0);
            float denom = ndoth * ndoth * (a2 - 1.0) + 1.0;
            return a2 / max(PI * denom * denom, 0.0001);
        }

        float geometrySchlickGgx(float ndotv, float roughness)
        {
            float r = roughness + 1.0;
            float k = (r * r) / 8.0;
            return ndotv / max(ndotv * (1.0 - k) + k, 0.0001);
        }

        vec3 fresnelSchlick(float cosTheta, vec3 f0)
        {
            return f0 + (1.0 - f0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
        }

        void main()
        {
            vec4 textureColor = texture(sampler2D(BaseColorTexture, BaseColorSampler), fsin_UV);
            vec3 albedo = pow(max(fsin_Color.rgb * textureColor.rgb, vec3(0.0)), vec3(2.2));
            float metallic = 0.0;
            float roughness = clamp(Draw.MaterialFactors.y, 0.04, 1.0);
            if (Draw.MaterialFactors.x > 0.0001)
            {
                vec4 metalRough = texture(sampler2D(MetallicRoughnessTexture, MetallicRoughnessSampler), fsin_UV);
                metallic = clamp(metalRough.b * Draw.MaterialFactors.x, 0.0, 1.0);
                roughness = clamp(metalRough.g * Draw.MaterialFactors.y, 0.04, 1.0);
            }
            float occlusion = 1.0;
            if (Draw.MaterialFactors.w > 0.0001)
            {
                occlusion = mix(1.0, texture(sampler2D(OcclusionTexture, OcclusionSampler), fsin_UV).r, Draw.MaterialFactors.w);
            }
            vec3 normal = normalize(fsin_Normal);
            vec3 light = Frame.LightPosition.w > 0.5
                ? normalize(Frame.LightPosition.xyz - fsin_WorldPosition)
                : normalize(-Frame.LightDirection.xyz);
            if (Draw.MaterialFactors.z > 0.0001)
            {
                normal = perturbNormal(normal);
            }
            vec3 view = normalize(vec3(0.0, 0.0, 1.0));
            vec3 halfVector = normalize(view + light);
            float ndotl = max(dot(normal, light), 0.0);
            float ndotv = max(dot(normal, view), 0.0);
            vec3 f0 = mix(vec3(0.04), albedo, metallic);
            float d = distributionGgx(normal, halfVector, roughness);
            float g = geometrySchlickGgx(ndotv, roughness) * geometrySchlickGgx(ndotl, roughness);
            vec3 f = fresnelSchlick(max(dot(halfVector, view), 0.0), f0);
            vec3 specular = d * g * f / max(4.0 * ndotv * ndotl, 0.0001);
            vec3 diffuse = (1.0 - f) * (1.0 - metallic) * albedo / PI;
            vec3 ambient = albedo * 0.035 * occlusion;
            vec3 emissive = pow(max(texture(sampler2D(EmissiveTexture, EmissiveSampler), fsin_UV).rgb * Draw.EmissiveFactors.rgb, vec3(0.0)), vec3(2.2)) * Draw.EmissiveFactors.a;
            vec3 color = emissive + ambient + (diffuse + specular) * Frame.LightColor.rgb * ndotl * 2.4;
            vec3 lit = pow(color, vec3(1.0 / 2.2));
            fsout_Color = vec4(lit, fsin_Color.a * textureColor.a);
        }
        """;

    private const string PresentVertexShader = """
        #version 450

        layout(location = 0) out vec2 fsin_UV;

        void main()
        {
            vec2 positions[3] = vec2[](
                vec2(-1.0, -1.0),
                vec2(3.0, -1.0),
                vec2(-1.0, 3.0)
            );
            vec2 position = positions[gl_VertexIndex];
            gl_Position = vec4(position, 0.0, 1.0);
            fsin_UV = position * 0.5 + 0.5;
        }
        """;

    private const string PresentFragmentShader = """
        #version 450

        layout(location = 0) in vec2 fsin_UV;
        layout(set = 0, binding = 0) uniform texture2D SceneTexture;
        layout(set = 0, binding = 1) uniform sampler SceneSampler;

        layout(location = 0) out vec4 fsout_Color;

        void main()
        {
            fsout_Color = texture(sampler2D(SceneTexture, SceneSampler), fsin_UV);
        }
        """;

    private const string HudVertexShader = """
        #version 450

        layout(location = 0) in vec3 Position;
        layout(location = 1) in vec4 Color;
        layout(location = 2) in vec2 UV;

        layout(location = 0) out vec4 fsin_Color;
        layout(location = 1) out vec2 fsin_UV;

        void main()
        {
            gl_Position = vec4(Position, 1.0);
            fsin_Color = Color;
            fsin_UV = UV;
        }
        """;

    private const string HudFragmentShader = """
        #version 450

        layout(location = 0) in vec4 fsin_Color;
        layout(location = 1) in vec2 fsin_UV;
        layout(set = 0, binding = 0) uniform texture2D SurfaceTexture;
        layout(set = 0, binding = 1) uniform sampler SurfaceSampler;

        layout(location = 0) out vec4 fsout_Color;

        void main()
        {
            fsout_Color = fsin_Color * texture(sampler2D(SurfaceTexture, SurfaceSampler), fsin_UV);
        }
        """;

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct GpuVertex(Vector3 Position, Vector3 Normal, Vector4 Color, Vector2 UV);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct HudVertex(Vector3 Position, Vector4 Color, Vector2 UV);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct FrameUniform(Matrix4x4 ViewProjection, Vector4 LightDirection, Vector4 LightColor, Vector4 LightPosition);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct DrawUniform(Matrix4x4 Model, Vector4 MaterialFactors, Vector4 EmissiveFactors);

    private sealed record RenderPacket(
        GpuVertex[] Vertices,
        uint[] Indices,
        GpuDraw[] Draws,
        FrameUniform FrameUniform,
        IReadOnlyList<StereoFrameUniform> StereoFrameUniforms,
        int MeshCount = 0,
        int TriangleCount = 0,
        int TextureCount = 0);

    private sealed record CachedRenderGeometry(
        GeometryCacheKey Key,
        IReadOnlyList<RekallAgeVulkanSceneMesh> Meshes,
        GpuVertex[] Vertices,
        uint[] Indices,
        int MeshCount,
        int TriangleCount,
        int TextureCount);

    private readonly record struct GeometryCacheKey(
        int SceneRevision,
        int AssetRevision,
        int Width,
        int Height,
        int MeshRenderableCount,
        int StructuralHash);

    private sealed record StereoFrameUniform(
        string Name,
        int Index,
        FrameUniform Uniform,
        Vector4 Viewport);

    private sealed record LiveEditWorkItem(
        RekallAgeLivePlayerRequestEnvelope Request,
        TaskCompletionSource<JsonObject> Completion);

    private sealed record LiveApplySceneBlueprintPayload(
        IReadOnlyList<RekallAgeSceneBlueprintEntity> Entities,
        bool ClearExisting,
        bool PersistToProject,
        bool ReloadAssets);

    private sealed record LiveApplySceneDiffPayload(
        IReadOnlyList<RekallAgeSceneBlueprintEntity>? UpsertEntities,
        IReadOnlyList<string>? DeleteEntityIds,
        IReadOnlyList<string>? DeleteEntityNames,
        bool ClearExisting,
        bool PersistToProject,
        bool ReloadAssets);

    private readonly record struct GpuDraw(
        uint FirstIndex,
        uint IndexCount,
        int VertexOffset,
        Matrix4x4 Model,
        string? TextureId,
        string? MetallicRoughnessTextureId,
        string? NormalTextureId,
        string? OcclusionTextureId,
        string? EmissiveTextureId,
        Vector4 MaterialFactors,
        Vector4 EmissiveFactors);

    private readonly record struct MaterialKey(
        string? BaseColorTextureId,
        string? NormalTextureId,
        string? MetallicRoughnessTextureId,
        string? OcclusionTextureId,
        string? EmissiveTextureId);

    private sealed record TextureBinding(Texture Texture, Sampler Sampler, ResourceSet ResourceSet) : IDisposable
    {
        public void Dispose()
        {
            ResourceSet.Dispose();
            Sampler.Dispose();
            Texture.Dispose();
        }
    }

    private sealed record SceneRenderTarget(
        int DisplayWidth,
        int DisplayHeight,
        int Width,
        int Height,
        Texture Color,
        Texture Depth,
        Framebuffer Framebuffer,
        Sampler Sampler,
        ResourceSet ResourceSet) : IDisposable
    {
        public void Dispose()
        {
            ResourceSet.Dispose();
            Sampler.Dispose();
            Framebuffer.Dispose();
            Depth.Dispose();
            Color.Dispose();
        }
    }
}

internal static class PlayerLog
{
    private static readonly object Gate = new();
    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Rekall AGE",
        "Player",
        "Logs",
        $"player-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.log");

    public static void Write(string message)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.AppendAllText(Path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
    }
}
