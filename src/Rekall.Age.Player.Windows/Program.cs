using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime;
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
        await using var player = await RekallAgeVeldridPlayer.CreateAsync(
            Path.GetFullPath(args[0]),
            args[1],
            syncToVerticalBlank,
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
    private const double FixedSimulationStepSeconds = 1.0 / 60.0;
    private const double MaximumAccumulatedSimulationSeconds = 0.25;
    private const int MaximumSimulationStepsPerRender = 8;

    private readonly string _sceneName;
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
    private readonly RekallAgeRuntimeRenderFrameBuilder _frameBuilder = new();
    private readonly RekallAgeRuntimeViewportAssetSet _assets;
    private readonly int _entityCount;
    private readonly Dictionary<string, TextureBinding> _textures;
    private readonly Dictionary<MaterialKey, ResourceSet> _materialSets = new();
    private readonly TextureBinding _whiteTexture;
    private readonly TextureBinding _flatNormalTexture;
    private readonly TextureBinding _defaultMetallicRoughnessTexture;
    private readonly TextureBinding _hudTexture;
    private readonly ResourceSet _hudTextureSet;
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
    private int _frameIndex;
    private Rekall.Age.Runtime.Abstractions.RekallAgeRuntimeWorld _runtimeWorld;
    private double _lastSimulationClockSeconds;
    private double _simulationAccumulatorSeconds;
    private int _lastFpsFrame;
    private double _lastFpsTime;
    private int _fps;
    private int _cachedWidth;
    private int _cachedHeight;
    private RenderPacket? _cachedStaticPacket;
    private bool _hudDirty = true;

    private RekallAgeVeldridPlayer(
        string sceneName,
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
        TextureBinding hudTexture)
    {
        _sceneName = sceneName;
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
        _runtimeWorld = runtimeWorld;
        _runtimeLoop = runtimeLoop;
        _assets = assets;
        _entityCount = entityCount;
        _textures = textures;
        _whiteTexture = whiteTexture;
        _flatNormalTexture = flatNormalTexture;
        _defaultMetallicRoughnessTexture = defaultMetallicRoughnessTexture;
        _hudTexture = hudTexture;
        _hudTextureSet = _factory.CreateResourceSet(new ResourceSetDescription(_hudTextureLayout, _hudTexture.Texture, _hudTexture.Sampler));
        _sceneTarget = CreateSceneRenderTarget(_factory, InitialWidth, InitialHeight, _presentTextureLayout);
        _lastSimulationClockSeconds = _clock.Elapsed.TotalSeconds;
    }

    public static async ValueTask<RekallAgeVeldridPlayer> CreateAsync(
        string projectRoot,
        string sceneName,
        bool syncToVerticalBlank,
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

        var windowInfo = new WindowCreateInfo(
            100,
            100,
            InitialWidth,
            InitialHeight,
            WindowState.Normal,
            $"Rekall AGE Player - {sceneName} | Vulkan swapchain");
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
            new ResourceLayoutElementDescription("DrawUniform", ResourceKind.UniformBuffer, ShaderStages.Vertex)));
        var materialLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("BaseColorTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("BaseColorSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("NormalTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("NormalSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("MetallicRoughnessTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("MetallicRoughnessSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("OcclusionTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("OcclusionSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
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
        var drawUniformBuffer = factory.CreateBuffer(new BufferDescription(
            checked((uint)Marshal.SizeOf<DrawUniform>()),
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
            sceneName,
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
            hudTexture);
        PlayerLog.Write("Player initialization complete.");
        return player;
    }

    public void Run()
    {
        while (_window.Exists)
        {
            _window.PumpEvents();
            if (!_window.Exists)
            {
                break;
            }

            RenderFrame();
        }

        _device.WaitForIdle();
    }

    public ValueTask DisposeAsync()
    {
        _device.WaitForIdle();
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
        return ValueTask.CompletedTask;
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
        _cachedStaticPacket = null;
        PlayerLog.Write($"Recreated supersampled scene target {_sceneTarget.Width}x{_sceneTarget.Height} for window {displayWidth}x{displayHeight}.");
    }

    private void RenderFrame()
    {
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
        var packet = GetRenderPacket(sceneFrame, useStaticGeometryCache: false, out var verticesChanged);

        if (verticesChanged && packet.Vertices.Length > 0)
        {
            EnsureVertexBufferCapacity(packet.Vertices);
            _device.UpdateBuffer(_vertexBuffer, 0, packet.Vertices);
            EnsureIndexBufferCapacity(packet.Indices);
            _device.UpdateBuffer(_indexBuffer, 0, packet.Indices);
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
            _device.UpdateBuffer(_frameUniformBuffer, 0, packet.FrameUniform);
            _commands.SetPipeline(_scenePipeline);
            _commands.SetVertexBuffer(0, _vertexBuffer);
            _commands.SetIndexBuffer(_indexBuffer, IndexFormat.UInt32);
            _commands.SetGraphicsResourceSet(0, _frameSet);
            _commands.SetGraphicsResourceSet(1, _drawSet);
            foreach (var draw in packet.Draws)
            {
                _device.UpdateBuffer(_drawUniformBuffer, 0, new DrawUniform(draw.Model, draw.MaterialFactors));
                _commands.SetGraphicsResourceSet(2, ResolveMaterialSet(draw));
                _commands.DrawIndexed(draw.IndexCount, 1, draw.FirstIndex, draw.VertexOffset, 0);
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

    private void AdvanceSimulationToWallClock()
    {
        var now = _clock.Elapsed.TotalSeconds;
        var delta = Math.Clamp(now - _lastSimulationClockSeconds, 0.0, MaximumAccumulatedSimulationSeconds);
        _lastSimulationClockSeconds = now;
        _simulationAccumulatorSeconds = Math.Min(
            MaximumAccumulatedSimulationSeconds,
            _simulationAccumulatorSeconds + delta);

        var steps = 0;
        while (_simulationAccumulatorSeconds >= FixedSimulationStepSeconds
            && steps < MaximumSimulationStepsPerRender)
        {
            var runResult = _runtimeLoop.RunAsync(_runtimeWorld, 1, CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            _runtimeWorld = runResult.World;
            _simulationAccumulatorSeconds -= FixedSimulationStepSeconds;
            steps++;
        }
    }

    private RenderPacket GetRenderPacket(
        Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportFrame frame,
        bool useStaticGeometryCache,
        out bool changed)
    {
        if (useStaticGeometryCache
            && _cachedStaticPacket is not null
            && _cachedWidth == frame.Width
            && _cachedHeight == frame.Height)
        {
            changed = false;
            return _cachedStaticPacket;
        }

        var vertices = BuildRenderPacket(frame);
        if (useStaticGeometryCache)
        {
            _cachedStaticPacket = vertices;
            _cachedWidth = frame.Width;
            _cachedHeight = frame.Height;
        }

        changed = true;
        return vertices;
    }

    private bool ShouldUseStaticGeometryCache(Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportFrame frame)
    {
        return frame.Renderables.Any(renderable =>
            renderable.Kind.Equals("mesh", StringComparison.Ordinal)
            && renderable.AssetId is not null
            && _assets.Models.ContainsKey(renderable.AssetId));
    }

    private RenderPacket BuildRenderPacket(Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportFrame frame)
    {
        var meshes = _meshBuilder.BuildMeshes(frame, _assets);
        if (meshes.Count == 0)
        {
            return new RenderPacket([], [], [], default, 0, 0, 0);
        }

        var batch = _batchBuilder.Build(frame, meshes);
        var vertices = batch.Vertices
            .Select(vertex => new GpuVertex(
                new Vector3(vertex.X, vertex.Y, vertex.Z),
                new Vector3(vertex.NormalX, vertex.NormalY, vertex.NormalZ),
                new Vector4(vertex.R, vertex.G, vertex.B, vertex.A),
                new Vector2(vertex.U, vertex.V)))
            .ToArray();
        var draws = batch.Draws
            .Where(draw => draw.IndexCount > 0)
            .Select(draw => new GpuDraw(
                draw.FirstIndex,
                draw.IndexCount,
                draw.VertexOffset,
                draw.Model,
                draw.TextureId,
                draw.MetallicRoughnessTextureId,
                draw.NormalTextureId,
                draw.OcclusionTextureId,
                draw.MaterialFactors))
            .ToArray();

        var textureCount = meshes
            .SelectMany(mesh => new[]
            {
                mesh.BaseColorTexture?.Id,
                mesh.MetallicRoughnessTexture?.Id,
                mesh.NormalTexture?.Id,
                mesh.OcclusionTexture?.Id
            })
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Count();
        return new RenderPacket(
            vertices,
            batch.Indices.ToArray(),
            draws,
            new FrameUniform(
                batch.Frame.ViewProjection,
                new Vector4(batch.Frame.LightDirection, 0),
                batch.Frame.LightColor),
            meshes.Count,
            meshes.Sum(mesh => mesh.Indices.Count / 3),
            textureCount);
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
            $"{_device.BackendType} {SceneSupersampleFactor}xSSAA");
        return RekallAgeSceneDebugHud.FormatLines(stats);
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

    private ResourceSet ResolveMaterialSet(GpuDraw draw)
    {
        var key = new MaterialKey(
            draw.TextureId,
            draw.NormalTextureId,
            draw.MetallicRoughnessTextureId,
            draw.OcclusionTextureId);
        if (_materialSets.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var baseColor = ResolveTexture(draw.TextureId, _whiteTexture);
        var normal = ResolveTexture(draw.NormalTextureId, _flatNormalTexture);
        var metallicRoughness = ResolveTexture(draw.MetallicRoughnessTextureId, _defaultMetallicRoughnessTexture);
        var occlusion = ResolveTexture(draw.OcclusionTextureId, _whiteTexture);
        var resourceSet = _factory.CreateResourceSet(new ResourceSetDescription(
            _materialLayout,
            baseColor.Texture,
            baseColor.Sampler,
            normal.Texture,
            normal.Sampler,
            metallicRoughness.Texture,
            metallicRoughness.Sampler,
            occlusion.Texture,
            occlusion.Sampler));
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
                mesh.OcclusionTexture
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
        } Frame;

        layout(set = 1, binding = 0) uniform DrawUniformBuffer
        {
            mat4 Model;
            vec4 MaterialFactors;
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
        } Frame;

        layout(set = 1, binding = 0) uniform DrawUniformBuffer
        {
            mat4 Model;
            vec4 MaterialFactors;
        } Draw;

        layout(set = 2, binding = 0) uniform texture2D BaseColorTexture;
        layout(set = 2, binding = 1) uniform sampler BaseColorSampler;
        layout(set = 2, binding = 2) uniform texture2D NormalTexture;
        layout(set = 2, binding = 3) uniform sampler NormalSampler;
        layout(set = 2, binding = 4) uniform texture2D MetallicRoughnessTexture;
        layout(set = 2, binding = 5) uniform sampler MetallicRoughnessSampler;
        layout(set = 2, binding = 6) uniform texture2D OcclusionTexture;
        layout(set = 2, binding = 7) uniform sampler OcclusionSampler;

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
            vec3 light = normalize(-Frame.LightDirection.xyz);
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
            vec3 color = ambient + (diffuse + specular) * Frame.LightColor.rgb * ndotl * 2.4;
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
    private readonly record struct FrameUniform(Matrix4x4 ViewProjection, Vector4 LightDirection, Vector4 LightColor);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct DrawUniform(Matrix4x4 Model, Vector4 MaterialFactors);

    private sealed record RenderPacket(
        GpuVertex[] Vertices,
        uint[] Indices,
        GpuDraw[] Draws,
        FrameUniform FrameUniform,
        int MeshCount = 0,
        int TriangleCount = 0,
        int TextureCount = 0);

    private readonly record struct GpuDraw(
        uint FirstIndex,
        uint IndexCount,
        int VertexOffset,
        Matrix4x4 Model,
        string? TextureId,
        string? MetallicRoughnessTextureId,
        string? NormalTextureId,
        string? OcclusionTextureId,
        Vector4 MaterialFactors);

    private readonly record struct MaterialKey(
        string? BaseColorTextureId,
        string? NormalTextureId,
        string? MetallicRoughnessTextureId,
        string? OcclusionTextureId);

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
