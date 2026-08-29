using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime.Abstractions;
using Veldrid;
using Veldrid.SPIRV;
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

namespace Rekall.Age.Rendering.Windows;

/// <summary>
/// Owns one surface-bound Vulkan device and the persistent resources used to present AGE runtime frames.
/// Host applications retain window, simulation, input, audio, persistence, and file-output policy.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RekallAgeVeldridVulkanPresentationSession : IRekallAgeVulkanPresentationSession
{
    private const int HudWidth = 360;
    private const int HudHeight = 224;
    private const int HudMargin = 16;

    private readonly object _gate = new();
    private readonly Action<string> _log;
    private readonly int _sceneSupersampleFactor;
    private readonly bool _debugHudEnabled;
    private readonly GraphicsDevice _device;
    private readonly ResourceFactory _factory;
    private readonly CommandList _commands;
    private readonly Pipeline _scenePipeline;
    private readonly Pipeline _sceneTransparentPipeline;
    private readonly Pipeline _directionalShadowPipeline;
    private readonly RekallAgeVeldridShaderPipelineCache _shaderPipelineCache;
    private readonly Pipeline _presentPipeline;
    private readonly RekallAgeVeldridPresentPassAdapter _presentPassAdapter;
    private readonly RekallAgeVeldridRuntimeGpuWorkloadExecutor _runtimeGpuWorkloadExecutor;
    private readonly Pipeline _hudPipeline;
    private readonly ResourceLayout _frameLayout;
    private readonly ResourceLayout _directionalShadowFrameLayout;
    private readonly ResourceLayout _drawLayout;
    private readonly ResourceLayout _materialLayout;
    private readonly ResourceLayout _presentTextureLayout;
    private readonly ResourceLayout _postProcessLayout;
    private readonly ResourceLayout _hudTextureLayout;
    private ResourceSet _frameSet;
    private readonly ResourceSet _directionalShadowFrameSet;
    private ResourceSet _drawSet;
    private ResourceSet _postProcessSet;
    private RekallAgeRuntimeViewportAssetSet _assets;
    private readonly Dictionary<string, TextureBinding> _textures;
    private readonly Dictionary<MaterialKey, ResourceSet> _materialSets = new();
    private readonly TextureBinding _whiteTexture;
    private readonly TextureBinding _flatNormalTexture;
    private readonly TextureBinding _defaultMetallicRoughnessTexture;
    private TextureBinding _environmentTexture;
    private readonly TextureBinding _hudTexture;
    private readonly ResourceSet _hudTextureSet;
    private TextureBinding _uiTexture;
    private readonly RekallAgeRuntimeSoftwareRenderer _softwareRenderer = new();
    private SceneRenderTarget _sceneTarget;
    private readonly RekallAgeVulkanSceneMeshBuilder _meshBuilder = new();
    private readonly RekallAgeVulkanSceneBatchBuilder _batchBuilder = new();
    private readonly RekallAgeInteractiveQualityFrameResolver _interactiveQualityResolver = new();
    private DeviceBuffer _vertexBuffer;
    private DeviceBuffer _indexBuffer;
    private DeviceBuffer _hudVertexBuffer;
    private DeviceBuffer _frameUniformBuffer;
    private readonly DeviceBuffer _fogUniformBuffer;
    private readonly DeviceBuffer _directionalShadowFrameUniformBuffer;
    private DeviceBuffer _drawUniformBuffer;
    private DeviceBuffer _postProcessUniformBuffer;
    private uint _vertexBufferCapacityBytes;
    private uint _indexBufferCapacityBytes;
    private uint _hudVertexBufferCapacityBytes;
    private readonly uint _drawUniformStrideBytes;
    private uint _drawUniformBufferCapacityBytes;
    private readonly uint[] _drawUniformDynamicOffsets = new uint[1];
    private int _sceneRevision;
    private int _assetRevision;
    private CachedRenderGeometry? _cachedStaticGeometry;
    private bool _hudDirty = true;
    private int? _uiOverlaySignature;
    private string? _lastRuntimeGpuWorkloadStatus;
    private int _profileGeometryCacheHits;
    private int _profileGeometryCacheMisses;
    private DirectionalShadowTarget _directionalShadowTarget;
    private readonly RekallAgeInteractiveShadowFramePlanner _interactiveShadowPlanner = new();
    private readonly RekallAgeInteractiveFogFramePlanner _interactiveFogPlanner = new();
    private readonly RekallAgeInteractiveAmbientOcclusionPlanner _interactiveAmbientOcclusionPlanner = new();
    private readonly RekallAgeInteractiveParticleBridge _interactiveParticleBridge = new();
    private int _lastUiVertexCount;
    private int _lastHudVertexCount;
    private int _surfaceWidth;
    private int _surfaceHeight;
    private int _frameIndex;
    private int _entityCount;
    private int _fps;
    private int _lastFpsFrame;
    private double _lastFpsTime;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private string? _debugBackendText;
    private bool _assetInvalidated;
    private bool _disposed;

    public RekallAgeVeldridVulkanPresentationSession(
        SwapchainSource swapchainSource,
        int pixelWidth,
        int pixelHeight,
        RekallAgeVulkanPresentationOptions options,
        RekallAgeRuntimeViewportFrame initialFrame,
        RekallAgeRuntimeViewportAssetSet initialAssets,
        int initialAssetRevision = 0)
    {
        if (pixelWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        }

        if (pixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelHeight));
        }

        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ProjectRoot);
        ArgumentNullException.ThrowIfNull(initialFrame);
        ArgumentNullException.ThrowIfNull(initialAssets);

        _log = options.Log ?? (_ => { });
        _sceneSupersampleFactor = Math.Clamp(options.SceneSupersampleFactor, 1, 4);
        _debugHudEnabled = options.DebugHudEnabled;
        _surfaceWidth = pixelWidth;
        _surfaceHeight = pixelHeight;
        _assets = initialAssets;
        _assetRevision = initialAssetRevision;

        var deviceOptions = new GraphicsDeviceOptions(
            debug: false,
            swapchainDepthFormat: PixelFormat.D24_UNorm_S8_UInt,
            syncToVerticalBlank: options.SyncToVerticalBlank,
            resourceBindingModel: ResourceBindingModel.Improved,
            preferDepthRangeZeroToOne: true,
            preferStandardClipSpaceYDirection: true);
        var swapchain = new SwapchainDescription(
            swapchainSource,
            checked((uint)pixelWidth),
            checked((uint)pixelHeight),
            PixelFormat.D24_UNorm_S8_UInt,
            options.SyncToVerticalBlank);
        _log("Creating Vulkan graphics device.");
        _device = GraphicsDevice.CreateVulkan(deviceOptions, swapchain);
        _factory = _device.ResourceFactory;
        _commands = _factory.CreateCommandList();
        _log($"Created graphics device backend={_device.BackendType} vsync={options.SyncToVerticalBlank} anisotropy={_device.Features.SamplerAnisotropy}.");

        _log("Compiling SPIR-V shaders.");
        var sceneShaders = _factory.CreateFromSpirv(
            new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(RekallAgeVeldridSceneShaders.SceneVertexShader), "main"),
            new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(RekallAgeVeldridSceneShaders.SceneFragmentShader), "main"));
        var directionalShadowShaders = _factory.CreateFromSpirv(
            new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(RekallAgeVeldridSceneShaders.DirectionalShadowVertexShader), "main"));
        var presentShaders = _factory.CreateFromSpirv(
            new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(RekallAgeVeldridSceneShaders.PresentVertexShader), "main"),
            new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(RekallAgeVeldridSceneShaders.PresentFragmentShader), "main"));
        var hudShaders = _factory.CreateFromSpirv(
            new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(RekallAgeVeldridSceneShaders.HudVertexShader), "main"),
            new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(RekallAgeVeldridSceneShaders.HudFragmentShader), "main"));
        var sceneVertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4),
            new VertexElementDescription("UV", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2));
        var hudVertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4),
            new VertexElementDescription("UV", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2));
        _frameLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("FrameUniform", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment),
            new ResourceLayoutElementDescription("DirectionalShadowAtlas", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("DirectionalShadowSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("InteractiveFogUniform", ResourceKind.UniformBuffer, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("EnvironmentTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("EnvironmentSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
        _directionalShadowFrameLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("DirectionalShadowFrameUniform", ResourceKind.UniformBuffer, ShaderStages.Vertex)));
        _drawLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription(
                "DrawUniform",
                ResourceKind.UniformBuffer,
                ShaderStages.Vertex | ShaderStages.Fragment,
                ResourceLayoutElementOptions.DynamicBinding)));
        _materialLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("BaseColorTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("BaseColorSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("NormalTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("NormalSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("MetallicRoughnessTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("MetallicRoughnessSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("OcclusionTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("OcclusionSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("EmissiveTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("EmissiveSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("CloudShadowTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("CloudShadowSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("SurfaceWaterTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("SurfaceWaterSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
        _presentTextureLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("SceneTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("SceneSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("SceneDepthTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("SceneDepthSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
        _postProcessLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("PostProcessUniform", ResourceKind.UniformBuffer, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("EnvironmentTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("EnvironmentSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
        _hudTextureLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("SurfaceTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("SurfaceSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
        _directionalShadowTarget = CreateDirectionalShadowTarget(_factory, 2048);
        _sceneTarget = CreateSceneRenderTarget(
            _factory,
            pixelWidth,
            pixelHeight,
            _sceneSupersampleFactor,
            _presentTextureLayout);
        var sceneShaderSet = new ShaderSetDescription([sceneVertexLayout], sceneShaders);
        _scenePipeline = _factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleAlphaBlend,
            DepthStencilStateDescription.DepthOnlyLessEqual,
            RasterizerStateDescription.CullNone,
            PrimitiveTopology.TriangleList,
            sceneShaderSet,
            [_frameLayout, _drawLayout, _materialLayout],
            _sceneTarget.Framebuffer.OutputDescription));
        _sceneTransparentPipeline = _factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleAlphaBlend,
            new DepthStencilStateDescription(true, false, ComparisonKind.LessEqual),
            RasterizerStateDescription.CullNone,
            PrimitiveTopology.TriangleList,
            sceneShaderSet,
            [_frameLayout, _drawLayout, _materialLayout],
            _sceneTarget.Framebuffer.OutputDescription));
        _directionalShadowPipeline = _factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.Empty,
            DepthStencilStateDescription.DepthOnlyLessEqual,
            RasterizerStateDescription.CullNone,
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription([sceneVertexLayout], [directionalShadowShaders]),
            [_directionalShadowFrameLayout, _drawLayout],
            _directionalShadowTarget.Framebuffers[0].OutputDescription));
        _presentPipeline = _factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleOverrideBlend,
            DepthStencilStateDescription.Disabled,
            RasterizerStateDescription.CullNone,
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription([], presentShaders),
            [_presentTextureLayout, _postProcessLayout],
            _device.SwapchainFramebuffer.OutputDescription));
        _hudPipeline = _factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleAlphaBlend,
            DepthStencilStateDescription.Disabled,
            RasterizerStateDescription.CullNone,
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription([hudVertexLayout], hudShaders),
            [_hudTextureLayout],
            _device.SwapchainFramebuffer.OutputDescription));
        foreach (var shader in sceneShaders.Concat([directionalShadowShaders]).Concat(presentShaders).Concat(hudShaders))
        {
            shader.Dispose();
        }

        _shaderPipelineCache = new RekallAgeVeldridShaderPipelineCache(
            options.ProjectRoot,
            _factory,
            sceneVertexLayout,
            [_frameLayout, _drawLayout, _materialLayout],
            _sceneTarget.Framebuffer.OutputDescription,
            _device.WaitForIdle,
            _log);
        _presentPassAdapter = new RekallAgeVeldridPresentPassAdapter();
        _runtimeGpuWorkloadExecutor = new RekallAgeVeldridRuntimeGpuWorkloadExecutor(options.ProjectRoot, _device, _commands);

        _log("Creating GPU buffers.");
        _vertexBuffer = _factory.CreateBuffer(new BufferDescription(4 * 1024 * 1024, BufferUsage.VertexBuffer | BufferUsage.Dynamic));
        _indexBuffer = _factory.CreateBuffer(new BufferDescription(4 * 1024 * 1024, BufferUsage.IndexBuffer | BufferUsage.Dynamic));
        _hudVertexBuffer = _factory.CreateBuffer(new BufferDescription(64 * 1024, BufferUsage.VertexBuffer | BufferUsage.Dynamic));
        _frameUniformBuffer = _factory.CreateBuffer(new BufferDescription(
            checked((uint)Marshal.SizeOf<FrameUniform>()), BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        _fogUniformBuffer = _factory.CreateBuffer(new BufferDescription(
            checked((uint)Marshal.SizeOf<InteractiveFogUniform>()), BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        _directionalShadowFrameUniformBuffer = _factory.CreateBuffer(new BufferDescription(
            checked((uint)Marshal.SizeOf<DirectionalShadowFrameUniform>()), BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        _drawUniformStrideBytes = AlignTo(
            checked((uint)Marshal.SizeOf<DrawUniform>()),
            Math.Max(1, _device.UniformBufferMinOffsetAlignment));
        _drawUniformBuffer = _factory.CreateBuffer(new BufferDescription(
            checked(_drawUniformStrideBytes * 256), BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        _postProcessUniformBuffer = _factory.CreateBuffer(new BufferDescription(
            checked((uint)Marshal.SizeOf<PostProcessUniform>()), BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        _vertexBufferCapacityBytes = _vertexBuffer.SizeInBytes;
        _indexBufferCapacityBytes = _indexBuffer.SizeInBytes;
        _hudVertexBufferCapacityBytes = _hudVertexBuffer.SizeInBytes;
        _drawUniformBufferCapacityBytes = _drawUniformBuffer.SizeInBytes;
        _directionalShadowFrameSet = _factory.CreateResourceSet(new ResourceSetDescription(
            _directionalShadowFrameLayout,
            _directionalShadowFrameUniformBuffer));
        _drawSet = _factory.CreateResourceSet(new ResourceSetDescription(_drawLayout, _drawUniformBuffer));

        _log("Creating texture resources.");
        _whiteTexture = CreateTextureBinding(
            _device,
            _factory,
            new RekallAgeVulkanSceneTexture(
                "__rekall_white", 1, 1, [255, 255, 255, 255],
                new RekallAgeVulkanSceneSampler(
                    RekallAgeVulkanSceneFilter.Linear,
                    RekallAgeVulkanSceneFilter.Linear,
                    RekallAgeVulkanSceneWrapMode.Repeat,
                    RekallAgeVulkanSceneWrapMode.Repeat)),
            _hudTextureLayout);
        _flatNormalTexture = CreateTextureBinding(
            _device,
            _factory,
            new RekallAgeVulkanSceneTexture(
                "__rekall_flat_normal", 1, 1, [128, 128, 255, 255],
                new RekallAgeVulkanSceneSampler(
                    RekallAgeVulkanSceneFilter.Linear,
                    RekallAgeVulkanSceneFilter.Linear,
                    RekallAgeVulkanSceneWrapMode.Repeat,
                    RekallAgeVulkanSceneWrapMode.Repeat)),
            _hudTextureLayout);
        _defaultMetallicRoughnessTexture = CreateTextureBinding(
            _device,
            _factory,
            new RekallAgeVulkanSceneTexture(
                "__rekall_default_metallic_roughness", 1, 1, [0, 255, 0, 255],
                new RekallAgeVulkanSceneSampler(
                    RekallAgeVulkanSceneFilter.Linear,
                    RekallAgeVulkanSceneFilter.Linear,
                    RekallAgeVulkanSceneWrapMode.Repeat,
                    RekallAgeVulkanSceneWrapMode.Repeat)),
            _hudTextureLayout);
        var initialQuality = initialFrame.ResolvedQualityPlan
            ?? new RekallAgeRenderQualityProfileResolver().Resolve(
                new RekallAgeRenderQualityIntent(),
                RekallAgeRenderingDeviceCapabilities.DesktopBaseline("veldrid-vulkan"),
                initialFrame.Width,
                initialFrame.Height);
        _textures = CreateTextureBindings(
            _device,
            _factory,
            _hudTextureLayout,
            initialAssets,
            checked((uint)initialQuality.Textures.MaximumAnisotropy));
        _environmentTexture = ResolveEnvironmentTexture(initialFrame);
        _postProcessSet = CreatePostProcessSet(_environmentTexture);
        _frameSet = CreateFrameSet(_environmentTexture);
        _hudTexture = CreateTextureBinding(
            _device,
            _factory,
            new RekallAgeVulkanSceneTexture(
                "__rekall_hud", HudWidth, HudHeight, new byte[HudWidth * HudHeight * 4],
                new RekallAgeVulkanSceneSampler(
                    RekallAgeVulkanSceneFilter.Linear,
                    RekallAgeVulkanSceneFilter.Linear,
                    RekallAgeVulkanSceneWrapMode.ClampToEdge,
                    RekallAgeVulkanSceneWrapMode.ClampToEdge)),
            _hudTextureLayout);
        _hudTextureSet = _factory.CreateResourceSet(new ResourceSetDescription(
            _hudTextureLayout,
            _hudTexture.Texture,
            _hudTexture.Sampler));
        _uiTexture = CreateUiTextureBinding(pixelWidth, pixelHeight);
    }

    public RekallAgeVulkanNativeDeviceInfo DeviceInfo
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_device.GetVulkanInfo(out var info))
                {
                    throw new InvalidOperationException("The Veldrid graphics device did not expose Vulkan interop metadata.");
                }

                return new RekallAgeVulkanNativeDeviceInfo(
                    _device.DeviceName,
                    _device.BackendType.ToString(),
                    unchecked((ulong)info.Instance),
                    unchecked((ulong)info.PhysicalDevice),
                    unchecked((ulong)info.Device),
                    unchecked((ulong)info.GraphicsQueue),
                    info.GraphicsQueueFamilyIndex,
                    info.DriverName,
                    info.DriverInfo);
            }
        }
    }

    public ValueTask<RekallAgeVulkanPresentationFrame> PresentAsync(
        RekallAgeVulkanSceneSubmission submission,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            var frame = PrepareSubmission(submission);
            PresentScene(frame, submission.RuntimeGpuWorkloads);
            var deviceInfo = DeviceInfo;
            return ValueTask.FromResult(
                RekallAgeVulkanPresentationFrame.Presented(frame, deviceInfo.DeviceName)
                    with
                    {
                        VulkanInterop = new RekallAgeVulkanPresentationInteropMetadata
                        {
                            VkInstance = checked((nuint)deviceInfo.Instance),
                            VkPhysicalDevice = checked((nuint)deviceInfo.PhysicalDevice),
                            VkDevice = checked((nuint)deviceInfo.Device),
                            GraphicsQueue = checked((nuint)deviceInfo.GraphicsQueue),
                            GraphicsQueueFamilyIndex = deviceInfo.GraphicsQueueFamilyIndex
                        }
                    });
        }
    }

    public ValueTask<RekallAgeVulkanPresentationFrame> PresentRgbaAsync(
        RekallAgeVulkanPixelSubmission submission,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            var frame = PrepareSubmission(submission.Scene);
            EnsurePixelRenderTarget(submission.Width, submission.Height);
            _device.UpdateTexture(
                _sceneTarget.Color,
                submission.Rgba.Span,
                0,
                0,
                0,
                checked((uint)submission.Width),
                checked((uint)submission.Height),
                1,
                0,
                0);

            var uiVertices = BuildFullScreenOverlayVertices(
                frame.Renderables.Any(renderable => renderable.UiVisual is not null));
            if (uiVertices.Length > 0)
            {
                UpdateUiTexture(frame);
                EnsureHudVertexBufferCapacity(uiVertices);
                _device.UpdateBuffer(_hudVertexBuffer, 0, uiVertices);
            }

            _lastUiVertexCount = uiVertices.Length;
            _lastHudVertexCount = 0;
            _device.UpdateBuffer(_postProcessUniformBuffer, 0, PostProcessUniform.Default);
            _commands.Begin();
            _presentPassAdapter.Record(
                _commands,
                _device.SwapchainFramebuffer,
                _presentPipeline,
                _sceneTarget.ResourceSet,
                _postProcessSet,
                _surfaceWidth,
                _surfaceHeight,
                new RgbaFloat(0.02f, 0.04f, 0.08f, 1f));
            RecordRuntimeGpuWorkloads(submission.Scene.RuntimeGpuWorkloads);
            if (uiVertices.Length > 0)
            {
                _commands.SetPipeline(_hudPipeline);
                _commands.SetVertexBuffer(0, _hudVertexBuffer);
                _commands.SetGraphicsResourceSet(0, _uiTexture.ResourceSet);
                _commands.Draw((uint)uiVertices.Length);
            }

            _commands.End();
            _device.SubmitCommands(_commands);
            _device.SwapBuffers();
            UpdateTitle(++_frameIndex, _clock.Elapsed.TotalSeconds, submission.Width * submission.Height);
            return ValueTask.FromResult(RekallAgeVulkanPresentationFrame.Presented(frame, DeviceInfo.DeviceName));
        }
    }

    public ValueTask<RekallAgeVulkanPresentedPixels> CapturePresentedRgbaAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            _device.WaitForIdle();
            var width = checked((uint)_surfaceWidth);
            var height = checked((uint)_surfaceHeight);
            Texture? color = null;
            Texture? depth = null;
            Framebuffer? framebuffer = null;
            Texture? staging = null;
            try
            {
                var swapchain = _device.SwapchainFramebuffer;
                var colorFormat = swapchain.ColorTargets[0].Target.Format;
                color = _factory.CreateTexture(TextureDescription.Texture2D(
                    width,
                    height,
                    mipLevels: 1,
                    arrayLayers: 1,
                    colorFormat,
                    TextureUsage.RenderTarget | TextureUsage.Sampled));
                if (swapchain.DepthTarget is { } swapchainDepth)
                {
                    depth = _factory.CreateTexture(TextureDescription.Texture2D(
                        width,
                        height,
                        mipLevels: 1,
                        arrayLayers: 1,
                        swapchainDepth.Target.Format,
                        TextureUsage.DepthStencil));
                }

                framebuffer = _factory.CreateFramebuffer(new FramebufferDescription(depth, color));
                staging = _factory.CreateTexture(TextureDescription.Texture2D(
                    width,
                    height,
                    mipLevels: 1,
                    arrayLayers: 1,
                    colorFormat,
                    TextureUsage.Staging));
                using var commands = _factory.CreateCommandList();
                commands.Begin();
                _presentPassAdapter.Record(
                    commands,
                    framebuffer,
                    _presentPipeline,
                    _sceneTarget.ResourceSet,
                    _postProcessSet,
                    (int)width,
                    (int)height,
                    new RgbaFloat(0.08f, 0.10f, 0.14f, 1f));
                if (_lastUiVertexCount > 0 || _lastHudVertexCount > 0)
                {
                    commands.SetPipeline(_hudPipeline);
                    commands.SetVertexBuffer(0, _hudVertexBuffer);
                    if (_lastUiVertexCount > 0)
                    {
                        commands.SetGraphicsResourceSet(0, _uiTexture.ResourceSet);
                        commands.Draw((uint)_lastUiVertexCount);
                    }

                    if (_lastHudVertexCount > 0)
                    {
                        commands.SetGraphicsResourceSet(0, _hudTextureSet);
                        commands.Draw((uint)_lastHudVertexCount, 1, (uint)_lastUiVertexCount, 0);
                    }
                }

                commands.CopyTexture(color, staging);
                commands.End();
                _device.SubmitCommands(commands);
                _device.WaitForIdle();
                var map = _device.Map<byte>(staging, MapMode.Read);
                try
                {
                    var bgra = colorFormat is PixelFormat.B8_G8_R8_A8_UNorm
                        or PixelFormat.B8_G8_R8_A8_UNorm_SRgb;
                    var pixels = new byte[checked((int)(width * height * 4))];
                    for (var i = 0; i < pixels.Length; i += 4)
                    {
                        pixels[i] = bgra ? map[i + 2] : map[i];
                        pixels[i + 1] = map[i + 1];
                        pixels[i + 2] = bgra ? map[i] : map[i + 2];
                        pixels[i + 3] = 255;
                    }

                    return ValueTask.FromResult(
                        new RekallAgeVulkanPresentedPixels((int)width, (int)height, pixels));
                }
                finally
                {
                    _device.Unmap(staging);
                }
            }
            finally
            {
                staging?.Dispose();
                framebuffer?.Dispose();
                depth?.Dispose();
                color?.Dispose();
            }
        }
    }

    public ValueTask InvalidateAssetsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _assetInvalidated = true;
            return ValueTask.CompletedTask;
        }
    }

    public ValueTask InvalidateShadersAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _shaderPipelineCache.InvalidateAll();
            _cachedStaticGeometry = null;
            _log("Project shader pipelines invalidated after debounced filesystem change.");
            return ValueTask.CompletedTask;
        }
    }

    public void Resize(int pixelWidth, int pixelHeight)
    {
        if (pixelWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        }

        if (pixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelHeight));
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ResizeCore(pixelWidth, pixelHeight);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            _device.WaitForIdle();
            _runtimeGpuWorkloadExecutor.Dispose();
            _sceneTarget.Dispose();
            foreach (var materialSet in _materialSets.Values)
            {
                materialSet.Dispose();
            }

            _hudTextureSet.Dispose();
            _uiTexture.Dispose();
            _frameSet.Dispose();
            _directionalShadowFrameSet.Dispose();
            _drawSet.Dispose();
            _postProcessSet.Dispose();
            _vertexBuffer.Dispose();
            _indexBuffer.Dispose();
            _hudVertexBuffer.Dispose();
            _frameUniformBuffer.Dispose();
            _fogUniformBuffer.Dispose();
            _directionalShadowFrameUniformBuffer.Dispose();
            _drawUniformBuffer.Dispose();
            _postProcessUniformBuffer.Dispose();
            foreach (var texture in _textures.Values)
            {
                texture.Dispose();
            }

            _whiteTexture.Dispose();
            _flatNormalTexture.Dispose();
            _defaultMetallicRoughnessTexture.Dispose();
            _hudTexture.Dispose();
            _shaderPipelineCache.Dispose();
            _scenePipeline.Dispose();
            _sceneTransparentPipeline.Dispose();
            _directionalShadowPipeline.Dispose();
            _presentPipeline.Dispose();
            _presentPassAdapter.Dispose();
            _hudPipeline.Dispose();
            _frameLayout.Dispose();
            _directionalShadowFrameLayout.Dispose();
            _drawLayout.Dispose();
            _materialLayout.Dispose();
            _presentTextureLayout.Dispose();
            _postProcessLayout.Dispose();
            _hudTextureLayout.Dispose();
            _directionalShadowTarget.Dispose();
            _commands.Dispose();
            _device.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private RekallAgeRuntimeViewportFrame PrepareSubmission(RekallAgeVulkanSceneSubmission submission)
    {
        var frame = submission.Frame.ResolvedQualityPlan is null
            ? _interactiveQualityResolver.Resolve(
                submission.Frame,
                authoredIntent: null,
                RekallAgeRenderingDeviceCapabilities.DesktopBaseline(
                    $"veldrid-{_device.BackendType.ToString().ToLowerInvariant()}"))
            : submission.Frame;
        if (_surfaceWidth != frame.Width || _surfaceHeight != frame.Height)
        {
            ResizeCore(frame.Width, frame.Height);
        }

        if (_assetInvalidated || submission.AssetRevision != _assetRevision)
        {
            ReplaceAssets(frame, submission.Assets, submission.AssetRevision);
        }
        else
        {
            RefreshEnvironmentTexture(frame);
        }

        _sceneRevision = submission.SceneRevision;
        _debugBackendText = submission.DebugBackendText;
        _entityCount = frame.Renderables
            .Select(renderable => renderable.EntityId)
            .Distinct(StringComparer.Ordinal)
            .Count();
        return frame;
    }

    private void ReplaceAssets(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportAssetSet assets,
        int assetRevision)
    {
        _device.WaitForIdle();
        _frameSet.Dispose();
        _postProcessSet.Dispose();
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
        var anisotropy = checked((uint)(frame.ResolvedQualityPlan?.Textures.MaximumAnisotropy ?? 1));
        foreach (var item in CreateTextureBindings(
                     _device,
                     _factory,
                     _hudTextureLayout,
                     assets,
                     anisotropy))
        {
            _textures[item.Key] = item.Value;
        }

        _assets = assets;
        _assetRevision = assetRevision;
        _assetInvalidated = false;
        _environmentTexture = ResolveEnvironmentTexture(frame);
        _postProcessSet = CreatePostProcessSet(_environmentTexture);
        _frameSet = CreateFrameSet(_environmentTexture);
        _runtimeGpuWorkloadExecutor.InvalidateFrameResources();
        _cachedStaticGeometry = null;
        _hudDirty = true;
        _uiOverlaySignature = null;
        _log($"Viewport assets uploaded revision={assetRevision} images={assets.Images.Count} textures={assets.Textures.Count} models={assets.Models.Count} issues={assets.Issues.Count}.");
    }

    private void RefreshEnvironmentTexture(RekallAgeRuntimeViewportFrame frame)
    {
        var environmentTexture = ResolveEnvironmentTexture(frame);
        if (ReferenceEquals(environmentTexture, _environmentTexture))
        {
            return;
        }

        _device.WaitForIdle();
        _frameSet.Dispose();
        _postProcessSet.Dispose();
        _environmentTexture = environmentTexture;
        _postProcessSet = CreatePostProcessSet(_environmentTexture);
        _frameSet = CreateFrameSet(_environmentTexture);
    }

    private void ResizeCore(int pixelWidth, int pixelHeight)
    {
        if (_surfaceWidth == pixelWidth && _surfaceHeight == pixelHeight)
        {
            return;
        }

        _device.WaitForIdle();
        _device.ResizeMainWindow(checked((uint)pixelWidth), checked((uint)pixelHeight));
        _surfaceWidth = pixelWidth;
        _surfaceHeight = pixelHeight;
        EnsureSceneRenderTarget(pixelWidth, pixelHeight);
        _uiOverlaySignature = null;
    }

    private void EnsureSceneRenderTarget(int displayWidth, int displayHeight)
    {
        if (_sceneTarget.DisplayWidth == displayWidth && _sceneTarget.DisplayHeight == displayHeight)
        {
            return;
        }

        _device.WaitForIdle();
        _runtimeGpuWorkloadExecutor.InvalidateFrameResources();
        _sceneTarget.Dispose();
        _sceneTarget = CreateSceneRenderTarget(
            _factory,
            displayWidth,
            displayHeight,
            _sceneSupersampleFactor,
            _presentTextureLayout);
        _cachedStaticGeometry = null;
        _log($"Recreated supersampled scene target {_sceneTarget.Width}x{_sceneTarget.Height} for surface {displayWidth}x{displayHeight}.");
    }

    private void EnsurePixelRenderTarget(int width, int height)
    {
        if (_sceneTarget.Width == width && _sceneTarget.Height == height)
        {
            return;
        }

        _device.WaitForIdle();
        _runtimeGpuWorkloadExecutor.InvalidateFrameResources();
        _sceneTarget.Dispose();
        _sceneTarget = CreateSceneRenderTarget(
            _factory,
            Math.Max(1, width / _sceneSupersampleFactor),
            Math.Max(1, height / _sceneSupersampleFactor),
            _sceneSupersampleFactor,
            _presentTextureLayout);
        _cachedStaticGeometry = null;
    }

    private void PresentScene(
        RekallAgeRuntimeViewportFrame frame,
        IReadOnlyList<RekallAgeRuntimeGpuWorkload> runtimeGpuWorkloads)
    {
        var frameNumber = ++_frameIndex;
        EnsureSceneRenderTarget(frame.Width, frame.Height);
        var sceneFrame = frame with { Width = _sceneTarget.Width, Height = _sceneTarget.Height };
        var packet = GetRenderPacket(
            sceneFrame,
            useStaticGeometryCache: ShouldUseStaticGeometryCache(sceneFrame),
            out var verticesChanged);
        var highFidelityPlan = new RekallAgeVulkanHighFidelityFrameRenderer().Plan(
            sceneFrame,
            _cachedStaticGeometry?.Meshes);
        var interactiveShadow = _interactiveShadowPlanner.Plan(highFidelityPlan?.ShadowPlan);
        var interactiveFog = _interactiveFogPlanner.Plan(highFidelityPlan?.FogPlan);
        var ambientOcclusion = _interactiveAmbientOcclusionPlanner.Plan(
            sceneFrame.ResolvedQualityPlan,
            !string.Equals(
                Environment.GetEnvironmentVariable("REKALL_INTERACTIVE_AO"),
                "0",
                StringComparison.OrdinalIgnoreCase));
        if (frameNumber == 1)
        {
            var shadowDiagnostics = highFidelityPlan?.ShadowPlan.Diagnostics.Count > 0
                ? string.Join(" | ", highFidelityPlan.ShadowPlan.Diagnostics.Select(item => $"{item.Code}: {item.Message}"))
                : "none";
            _log(
                $"Interactive high fidelity quality={sceneFrame.ResolvedQualityPlan?.ResolvedPreset ?? "none"} " +
                $"post={sceneFrame.PostProcessStack?.Enabled == true} plan={highFidelityPlan is not null} " +
                $"shadows={highFidelityPlan?.ShadowPlan.Enabled == true} cascades={highFidelityPlan?.ShadowPlan.Cascades.Count ?? 0} " +
                $"fog={interactiveFog.Enabled} fogMode={interactiveFog.RequestedMode}->{interactiveFog.ExecutedMode} fogVolumes={interactiveFog.Volumes.Count} " +
                $"ao={ambientOcclusion.Enabled} aoSamples={ambientOcclusion.SampleCount} diagnostics={shadowDiagnostics}.");
            foreach (var diagnostic in interactiveFog.Diagnostics)
            {
                _log($"Interactive fog diagnostic {diagnostic.Code}: {diagnostic.Message}");
            }
        }

        EnsureDirectionalShadowTarget(interactiveShadow.Resolution);
        packet = ApplyInteractiveShadowFrame(packet, interactiveShadow);
        packet = ApplyInteractiveEnvironment(packet, sceneFrame.Environment);
        var interactiveParticles = _interactiveParticleBridge.Build(
            highFidelityPlan?.ParticlePlan,
            sceneFrame.ElapsedSeconds,
            sceneFrame.DeltaSeconds);
        packet = AppendInteractiveParticles(packet, sceneFrame.ActiveCamera, interactiveParticles);
        verticesChanged |= interactiveParticles.ActiveParticleCount > 0;
        if (frameNumber == 1)
        {
            _log(
                $"Interactive particles mode={interactiveParticles.ExecutionMode} " +
                $"emitters={interactiveParticles.EmitterCount} active={interactiveParticles.ActiveParticleCount}.");
        }

        _device.UpdateBuffer(_fogUniformBuffer, 0, BuildInteractiveFogUniform(interactiveFog));
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
                    new DrawUniform(
                        draw.Model,
                        draw.MaterialFactors,
                        draw.EmissiveFactors,
                        draw.AtmosphereFactors0,
                        draw.AtmosphereFactors1,
                        draw.AtmosphereColor0,
                        draw.AtmosphereColor1,
                        draw.AtmosphereColor2,
                        draw.CloudFactors,
                        draw.CloudColor,
                        draw.CloudShadowFactors,
                        draw.SurfaceWaterFactors,
                        new Vector4(
                            draw.ReceiveShadows ? 1 : 0,
                            draw.AlphaCutoff,
                            draw.AlphaMode.Equals("mask", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
                            0)));
            }
        }

        UpdateTitle(frameNumber, _clock.Elapsed.TotalSeconds, packet.Vertices.Length);
        var uiVertices = BuildFullScreenOverlayVertices(
            frame.Renderables.Any(renderable => renderable.UiVisual is not null));
        var hudVertices = _debugHudEnabled ? BuildHudVertices(frame.Width, frame.Height) : [];
        if (_debugHudEnabled && _hudDirty)
        {
            UpdateHudTexture(BuildHudLines(frame, packet));
            _hudDirty = false;
        }

        if (uiVertices.Length > 0)
        {
            UpdateUiTexture(frame);
        }

        var overlayVertices = uiVertices.Concat(hudVertices).ToArray();
        if (overlayVertices.Length > 0)
        {
            EnsureHudVertexBufferCapacity(overlayVertices);
            _device.UpdateBuffer(_hudVertexBuffer, 0, overlayVertices);
        }

        _lastUiVertexCount = uiVertices.Length;
        _lastHudVertexCount = hudVertices.Length;
        _device.UpdateBuffer(
            _postProcessUniformBuffer,
            0,
            BuildPostProcessUniform(
                frame.PostProcessStack,
                ambientOcclusion,
                packet.FrameUniform.ViewProjection,
                packet.FrameUniform.CameraPosition,
                sceneFrame.Environment,
                _sceneTarget.Width,
                _sceneTarget.Height));

        _commands.Begin();
        RecordDirectionalShadowPass(packet, interactiveShadow);
        _commands.SetFramebuffer(_sceneTarget.Framebuffer);
        _commands.SetFullViewports();
        _commands.SetFullScissorRects();
        var background = RekallAgeEnvironmentBackgroundResolver.Resolve(frame);
        _commands.ClearColorTarget(0, new RgbaFloat(background.X, background.Y, background.Z, background.W));
        _commands.ClearDepthStencil(1f);
        if (packet.Vertices.Length > 0)
        {
            _commands.SetVertexBuffer(0, _vertexBuffer);
            _commands.SetIndexBuffer(_indexBuffer, IndexFormat.UInt32);
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

        _commands.GenerateMipmaps(_sceneTarget.Color);
        _presentPassAdapter.Record(
            _commands,
            _device.SwapchainFramebuffer,
            _presentPipeline,
            _sceneTarget.ResourceSet,
            _postProcessSet,
            _surfaceWidth,
            _surfaceHeight,
            new RgbaFloat(0.08f, 0.10f, 0.14f, 1f));
        RecordRuntimeGpuWorkloads(runtimeGpuWorkloads);
        if (overlayVertices.Length > 0)
        {
            _commands.SetPipeline(_hudPipeline);
            _commands.SetVertexBuffer(0, _hudVertexBuffer);
        }

        if (uiVertices.Length > 0)
        {
            _commands.SetGraphicsResourceSet(0, _uiTexture.ResourceSet);
            _commands.Draw((uint)uiVertices.Length);
        }

        if (hudVertices.Length > 0)
        {
            _commands.SetGraphicsResourceSet(0, _hudTextureSet);
            _commands.Draw((uint)hudVertices.Length, 1, (uint)uiVertices.Length, 0);
        }

        _commands.End();
        _device.SubmitCommands(_commands);
        _device.SwapBuffers();
    }

    private void RecordRuntimeGpuWorkloads(IReadOnlyList<RekallAgeRuntimeGpuWorkload> runtimeGpuWorkloads)
    {
        var report = _runtimeGpuWorkloadExecutor.Record(
            runtimeGpuWorkloads,
            _sceneTarget.Color,
            _device.SwapchainFramebuffer);
        var status = report.Diagnostics.Count == 0
            ? $"Runtime GPU workloads enabled={report.EnabledWorkloads} executed={report.ExecutedWorkloads}."
            : $"Runtime GPU workloads enabled={report.EnabledWorkloads} executed={report.ExecutedWorkloads} diagnostics={string.Join(" | ", report.Diagnostics.Select(item => $"{item.Code}: {item.Message}"))}";
        if (status.Equals(_lastRuntimeGpuWorkloadStatus, StringComparison.Ordinal))
        {
            return;
        }

        _lastRuntimeGpuWorkloadStatus = status;
        _log(status);
    }

    private void DrawScenePacket(RenderPacket packet)
    {
        DrawScenePacketPass(packet, transparent: false);
        DrawScenePacketPass(packet, transparent: true);
    }

    private void DrawScenePacketPass(RenderPacket packet, bool transparent)
    {
        for (var i = 0; i < packet.Draws.Length; i++)
        {
            var draw = packet.Draws[i];
            if (draw.Transparent != transparent)
            {
                continue;
            }

            var pipeline = draw.ShaderPipeline is null
                ? transparent ? _sceneTransparentPipeline : _scenePipeline
                : _shaderPipelineCache.Resolve(draw.ShaderPipeline, transparent).Pipeline;
            _commands.SetPipeline(pipeline);
            _commands.SetGraphicsResourceSet(0, _frameSet);
            _drawUniformDynamicOffsets[0] = checked(_drawUniformStrideBytes * (uint)i);
            _commands.SetGraphicsResourceSet(1, _drawSet, _drawUniformDynamicOffsets);
            _commands.SetGraphicsResourceSet(2, ResolveMaterialSet(draw));
            _commands.DrawIndexed(draw.IndexCount, 1, draw.FirstIndex, draw.VertexOffset, 0);
        }
    }

    private TextureBinding ResolveEnvironmentTexture(RekallAgeRuntimeViewportFrame frame) =>
        !string.IsNullOrWhiteSpace(frame.Environment?.SkyAssetId)
        && _textures.TryGetValue(frame.Environment.SkyAssetId, out var authoredEnvironment)
            ? authoredEnvironment
            : _whiteTexture;

    private ResourceSet CreatePostProcessSet(TextureBinding environmentTexture) =>
        _factory.CreateResourceSet(new ResourceSetDescription(
            _postProcessLayout,
            _postProcessUniformBuffer,
            environmentTexture.Texture,
            environmentTexture.Sampler));

    private ResourceSet CreateFrameSet(TextureBinding environmentTexture) =>
        _factory.CreateResourceSet(new ResourceSetDescription(
            _frameLayout,
            _frameUniformBuffer,
            _directionalShadowTarget.View,
            _directionalShadowTarget.Sampler,
            _fogUniformBuffer,
            environmentTexture.Texture,
            environmentTexture.Sampler));

    private RenderPacket GetRenderPacket(
        Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportFrame frame,
        bool useStaticGeometryCache,
        out bool changed)
    {
        if (useStaticGeometryCache
            && _cachedStaticGeometry is not null
            && _cachedStaticGeometry.Key.Equals(CreateGeometryCacheKey(frame)))
        {
            _profileGeometryCacheHits++;
            var packet = BuildRenderPacket(frame, _cachedStaticGeometry, out _);
            changed = false;
            return packet;
        }

        _profileGeometryCacheMisses++;
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

        var batch = cachedGeometry is null
            ? _batchBuilder.Build(frame, meshes)
            : _batchBuilder.BuildDynamic(frame, meshes, cachedGeometry.StableBatch);
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
                draw.CloudShadowTextureId,
                draw.SurfaceWaterTextureId,
                draw.MaterialFactors,
                draw.EmissiveFactors,
                draw.AtmosphereFactors0,
                draw.AtmosphereFactors1,
                draw.AtmosphereColor0,
                draw.AtmosphereColor1,
                draw.AtmosphereColor2,
                draw.CloudFactors,
                draw.CloudColor,
                draw.CloudShadowFactors,
                draw.SurfaceWaterFactors,
                draw.Transparent,
                draw.ShaderPipeline,
                draw.EntityId,
                draw.CastShadows,
                draw.ReceiveShadows,
                draw.AlphaMode,
                draw.AlphaCutoff));
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
                AddTextureId(textureIds, mesh.SurfaceWaterTexture?.Id);
            }

            triangleCount = triangles;
            textureCount = textureIds.Count;
        }

        if (cachedGeometry is null)
        {
            builtGeometry = new CachedRenderGeometry(
                default,
                meshes,
                batch,
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
                batch.Frame.LightPosition,
                batch.Frame.CameraPosition,
                new Vector4(batch.Frame.AdditionalLightDirection, 0),
                batch.Frame.AdditionalLightColor,
                batch.Frame.AdditionalLightPosition,
                batch.Frame.AdditionalLightParameters,
                PointLight(batch.Frame, 1).Color, PointLight(batch.Frame, 1).Position, PointLight(batch.Frame, 1).Parameters,
                PointLight(batch.Frame, 2).Color, PointLight(batch.Frame, 2).Position, PointLight(batch.Frame, 2).Parameters,
                PointLight(batch.Frame, 3).Color, PointLight(batch.Frame, 3).Position, PointLight(batch.Frame, 3).Parameters,
                SpotLight(batch.Frame, 0).Color, SpotLight(batch.Frame, 0).Position, SpotLight(batch.Frame, 0).Direction, SpotLight(batch.Frame, 0).Parameters,
                SpotLight(batch.Frame, 1).Color, SpotLight(batch.Frame, 1).Position, SpotLight(batch.Frame, 1).Direction, SpotLight(batch.Frame, 1).Parameters,
                SpotLight(batch.Frame, 2).Color, SpotLight(batch.Frame, 2).Position, SpotLight(batch.Frame, 2).Direction, SpotLight(batch.Frame, 2).Parameters,
                SpotLight(batch.Frame, 3).Color, SpotLight(batch.Frame, 3).Position, SpotLight(batch.Frame, 3).Direction, SpotLight(batch.Frame, 3).Parameters,
                EnvironmentAmbientSkyColor: batch.Frame.EnvironmentAmbientSkyColor,
                EnvironmentAmbientGroundColor: batch.Frame.EnvironmentAmbientGroundColor),
            BuildStereoUniforms(batch),
            meshCount,
            triangleCount.Value,
            textureCount.Value);
    }

    private static RenderPacket AppendInteractiveParticles(
        RenderPacket packet,
        RekallAgeRuntimeViewportCamera? camera,
        RekallAgeInteractiveParticleFrame particleFrame)
    {
        if (camera is null || particleFrame.Particles.Count == 0)
        {
            return packet;
        }

        var rotation = Matrix4x4.CreateFromYawPitchRoll(
            DegreesToRadians((float)camera.RotationY),
            DegreesToRadians((float)camera.RotationX),
            DegreesToRadians((float)camera.RotationZ));
        var right = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, rotation));
        var up = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, rotation));
        var normal = Vector3.Normalize(Vector3.Cross(right, up));
        var vertices = new List<GpuVertex>(packet.Vertices.Length + particleFrame.Particles.Count * 4);
        vertices.AddRange(packet.Vertices);
        var indices = new List<uint>(packet.Indices.Length + particleFrame.Particles.Count * 6);
        indices.AddRange(packet.Indices);
        var draws = new List<GpuDraw>(packet.Draws.Length + particleFrame.EmitterCount);
        draws.AddRange(packet.Draws);

        foreach (var group in particleFrame.Particles.GroupBy(item => item.EmitterEntityId, StringComparer.Ordinal))
        {
            var firstIndex = checked((uint)indices.Count);
            var vertexOffset = vertices.Count;
            var particleIndex = 0u;
            foreach (var particle in group)
            {
                var halfRight = right * (particle.Size * 0.5f);
                var halfUp = up * (particle.Size * 0.5f);
                vertices.Add(new(particle.Position - halfRight - halfUp, normal, particle.Color, new(0, 1)));
                vertices.Add(new(particle.Position + halfRight - halfUp, normal, particle.Color, new(1, 1)));
                vertices.Add(new(particle.Position + halfRight + halfUp, normal, particle.Color, new(1, 0)));
                vertices.Add(new(particle.Position - halfRight + halfUp, normal, particle.Color, new(0, 0)));
                indices.Add(particleIndex + 0);
                indices.Add(particleIndex + 1);
                indices.Add(particleIndex + 2);
                indices.Add(particleIndex + 0);
                indices.Add(particleIndex + 2);
                indices.Add(particleIndex + 3);
                particleIndex += 4;
            }

            var representative = group.First();
            var emissive = representative.Color;
            draws.Add(new GpuDraw(
                FirstIndex: firstIndex,
                IndexCount: checked((uint)(group.Count() * 6)),
                VertexOffset: vertexOffset,
                Model: Matrix4x4.Identity,
                TextureId: representative.TextureAssetId,
                MetallicRoughnessTextureId: null,
                NormalTextureId: null,
                OcclusionTextureId: null,
                EmissiveTextureId: null,
                CloudShadowTextureId: null,
                SurfaceWaterTextureId: null,
                MaterialFactors: new Vector4(0, 1, 0, 0),
                EmissiveFactors: new Vector4(emissive.X, emissive.Y, emissive.Z, 1.35f),
                AtmosphereFactors0: Vector4.Zero,
                AtmosphereFactors1: Vector4.Zero,
                AtmosphereColor0: Vector4.Zero,
                AtmosphereColor1: Vector4.Zero,
                AtmosphereColor2: Vector4.Zero,
                CloudFactors: Vector4.Zero,
                CloudColor: Vector4.Zero,
                CloudShadowFactors: Vector4.Zero,
                SurfaceWaterFactors: Vector4.Zero,
                Transparent: true,
                ShaderPipeline: null,
                EntityId: representative.EmitterEntityId,
                CastShadows: false,
                ReceiveShadows: false,
                AlphaMode: "blend",
                AlphaCutoff: 0.5f));
        }

        return packet with
        {
            Vertices = vertices.ToArray(),
            Indices = indices.ToArray(),
            Draws = draws.ToArray(),
            TriangleCount = packet.TriangleCount + particleFrame.ActiveParticleCount * 2
        };
    }

    private static float DegreesToRadians(float degrees) => degrees * MathF.PI / 180f;

    private static RenderPacket ApplyInteractiveEnvironment(
        RenderPacket packet,
        RekallAgeRuntimeViewportEnvironment? environment)
    {
        var parameters = environment is null
            ? new Vector4(1, 0, 11.2f, 0)
            : new Vector4(
                (float)Math.Clamp(environment.AmbientEnergy, 0, 16),
                (float)Math.Clamp(environment.Exposure, -8, 8),
                (float)Math.Clamp(environment.WhitePoint, 0.1, 64),
                environment.ToneMapper.Equals("agx", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
        var ambientSkyColor = ParseEnvironmentColor(environment?.AmbientSkyColor);
        ambientSkyColor.W = string.IsNullOrWhiteSpace(environment?.SkyAssetId) ? 0 : 1;
        var ambientGroundColor = ParseEnvironmentColor(environment?.AmbientGroundColor);
        return packet with
        {
            FrameUniform = packet.FrameUniform with
            {
                EnvironmentParameters = parameters,
                EnvironmentAmbientSkyColor = ambientSkyColor,
                EnvironmentAmbientGroundColor = ambientGroundColor
            }
        };
    }

    private static Vector4 ParseEnvironmentColor(string? value)
    {
        if (value is { Length: 7 or 9 } && value[0] == '#'
            && byte.TryParse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red)
            && byte.TryParse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green)
            && byte.TryParse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
        {
            return new Vector4(red / 255f, green / 255f, blue / 255f, 1);
        }

        return Vector4.One;
    }

    private GeometryCacheKey CreateGeometryCacheKey(Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportFrame frame)
    {
        var hash = new HashCode();
        hash.Add(RekallAgeVirtualGeometrySelectionSignature.Compute(frame));
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
            hash.Add(renderable.SurfaceWater?.TextureAssetId, StringComparer.Ordinal);
            hash.Add(renderable.SurfaceWater?.Coverage ?? 0);
            hash.Add(renderable.SurfaceWater?.SpecularStrength ?? 0);
            hash.Add(renderable.SurfaceWater?.Roughness ?? 0);
            hash.Add(renderable.MeshSlices);
            hash.Add(renderable.MeshStacks);
            hash.Add(renderable.MetallicFactor);
            hash.Add(renderable.RoughnessFactor);
            hash.Add(renderable.NormalScale);
            hash.Add(renderable.OcclusionStrength);
            hash.Add(renderable.EmissiveStrength);
            hash.Add(renderable.GeometryMesh?.Vertices.Count ?? 0);
            hash.Add(renderable.GeometryMesh?.Indices.Count ?? 0);
            hash.Add(renderable.GeometryMesh is null ? 0 : RekallAgeRuntimeGeometrySignature.For(renderable.GeometryMesh));
            hash.Add(renderable.LineSegments?.Segments.Count ?? 0);
            hash.Add(renderable.LineSegments?.Thickness ?? 0);
            hash.Add(renderable.LineSegments is null ? 0 : RekallAgeRuntimeGeometrySignature.For(renderable.LineSegments));
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
                    batch.Frame.LightPosition,
                    view.EyePosition,
                    new Vector4(batch.Frame.AdditionalLightDirection, 0),
                    batch.Frame.AdditionalLightColor,
                    batch.Frame.AdditionalLightPosition,
                    batch.Frame.AdditionalLightParameters,
                    PointLight(batch.Frame, 1).Color, PointLight(batch.Frame, 1).Position, PointLight(batch.Frame, 1).Parameters,
                    PointLight(batch.Frame, 2).Color, PointLight(batch.Frame, 2).Position, PointLight(batch.Frame, 2).Parameters,
                    PointLight(batch.Frame, 3).Color, PointLight(batch.Frame, 3).Position, PointLight(batch.Frame, 3).Parameters,
                    SpotLight(batch.Frame, 0).Color, SpotLight(batch.Frame, 0).Position, SpotLight(batch.Frame, 0).Direction, SpotLight(batch.Frame, 0).Parameters,
                    SpotLight(batch.Frame, 1).Color, SpotLight(batch.Frame, 1).Position, SpotLight(batch.Frame, 1).Direction, SpotLight(batch.Frame, 1).Parameters,
                    SpotLight(batch.Frame, 2).Color, SpotLight(batch.Frame, 2).Position, SpotLight(batch.Frame, 2).Direction, SpotLight(batch.Frame, 2).Parameters,
                    SpotLight(batch.Frame, 3).Color, SpotLight(batch.Frame, 3).Position, SpotLight(batch.Frame, 3).Direction, SpotLight(batch.Frame, 3).Parameters),
                view.Viewport);
        }

        return uniforms;
    }

    private static RekallAgeVulkanPointLight PointLight(RekallAgeVulkanSceneFrameUniform frame, int index) =>
        index >= 0 && index < frame.PointLights.Count
            ? frame.PointLights[index]
            : new(string.Empty, Vector4.Zero, Vector4.Zero, Vector4.Zero);

    private static RekallAgeVulkanSpotLight SpotLight(RekallAgeVulkanSceneFrameUniform frame, int index) =>
        index >= 0 && index < frame.SpotLights.Count
            ? frame.SpotLights[index]
            : new(string.Empty, Vector4.Zero, Vector4.Zero, Vector4.Zero, Vector4.Zero);

    private static PostProcessUniform BuildPostProcessUniform(
        RekallAgeRuntimeViewportPostProcessStack? stack,
        RekallAgeInteractiveAmbientOcclusionPlan ambientOcclusion,
        Matrix4x4 viewProjection,
        Vector4 cameraPosition,
        RekallAgeRuntimeViewportEnvironment? environment,
        int width,
        int height)
    {
        var inverseViewProjection = Matrix4x4.Invert(viewProjection, out var inverse)
            ? inverse
            : Matrix4x4.Identity;
        // EnvironmentParameters.w was a colour-grade present/absent flag that no shader ever
        // read. It now carries lens-dirt strength, reusing an already-spare slot rather than
        // growing this uniform - the same approach ShadowFactors.y/.z take in the scene shader.
        var lensDirtStrength = stack is { Enabled: true }
            ? (float)Math.Clamp(
                stack.Passes.FirstOrDefault(pass =>
                    pass.Type.Equals("lensDirt", StringComparison.OrdinalIgnoreCase))?.Intensity ?? 0,
                0,
                4)
            : 0f;
        var environmentParameters = environment is null
            ? new Vector4(0, 11.2f, 0, lensDirtStrength)
            : new Vector4(
                (float)Math.Clamp(environment.Exposure, -8, 8),
                (float)Math.Clamp(environment.WhitePoint, 0.1, 64),
                environment.ToneMapper.Equals("agx", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
                lensDirtStrength);
        var screenParameters = new Vector4(
            Math.Max(1, width),
            Math.Max(1, height),
            1f / Math.Max(1, width),
            1f / Math.Max(1, height));
        var ambientOcclusionParameters = ambientOcclusion.Enabled
            ? new Vector4(
                ambientOcclusion.SampleCount,
                ambientOcclusion.RadiusPixels,
                ambientOcclusion.Strength,
                ambientOcclusion.Bias)
            : Vector4.Zero;

        if (stack is null)
        {
            return PostProcessUniform.Default with
            {
                ScreenParameters = screenParameters,
                AmbientOcclusionParameters = ambientOcclusionParameters,
                InverseViewProjection = inverseViewProjection,
                CameraPosition = cameraPosition,
                EnvironmentParameters = environmentParameters
            };
        }

        if (!stack.Enabled || stack.Passes.Count == 0)
        {
            return PostProcessUniform.Disabled with
            {
                ScreenParameters = screenParameters,
                AmbientOcclusionParameters = ambientOcclusionParameters,
                InverseViewProjection = inverseViewProjection,
                CameraPosition = cameraPosition,
                EnvironmentParameters = environmentParameters
            };
        }

        var threshold = PostProcessUniform.Default.Parameters.X;
        var intensity = PostProcessUniform.Default.Parameters.Y;
        var radius = PostProcessUniform.Default.Parameters.Z;
        var enabled = 0f;
        foreach (var pass in stack.Passes)
        {
            if (pass.Type.Equals("brightExtract", StringComparison.OrdinalIgnoreCase))
            {
                enabled = 1f;
                threshold = (float)Math.Clamp(pass.Threshold, 0, 64);
                if (pass.Scale > 0)
                {
                    radius = (float)Math.Clamp(pass.Scale, 0.05, 32);
                }
            }
            else if (pass.Type.Equals("blur", StringComparison.OrdinalIgnoreCase))
            {
                enabled = 1f;
                radius = (float)Math.Clamp(pass.Radius, 0.05, 32);
            }
            else if (pass.Type.Equals("composite", StringComparison.OrdinalIgnoreCase))
            {
                enabled = 1f;
                intensity = (float)Math.Clamp(pass.Intensity, 0, 16);
            }
        }

        return new PostProcessUniform(
            new Vector4(threshold, intensity, radius, enabled),
            screenParameters,
            ambientOcclusionParameters,
            inverseViewProjection,
            cameraPosition,
            environmentParameters);
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
        _log($"Resized dynamic vertex buffer to {newCapacity} bytes for {vertices.Count} vertices.");
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
        _log($"Resized dynamic index buffer to {newCapacity} bytes for {indices.Count} indices.");
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
        _log($"Resized dynamic draw uniform buffer to {newCapacity} bytes for {drawCount} draw(s).");
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
        var baseLine = $"{_device.BackendType} {_sceneSupersampleFactor}xSSAA";
        return string.IsNullOrWhiteSpace(_debugBackendText)
            ? baseLine
            : $"{baseLine} {_debugBackendText}";
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

    private void UpdateUiTexture(RekallAgeRuntimeViewportFrame frame)
    {
        var signature = RekallAgeRuntimeUiOverlaySignature.Compute(frame);
        if (_uiOverlaySignature == signature)
        {
            return;
        }

        if (_uiTexture.Texture.Width != (uint)frame.Width || _uiTexture.Texture.Height != (uint)frame.Height)
        {
            _device.WaitForIdle();
            _uiTexture.Dispose();
            _uiTexture = CreateUiTextureBinding(frame.Width, frame.Height);
        }

        var rgba = _softwareRenderer.RenderUiOverlayRgba(frame, _assets);
        _device.UpdateTexture(
            _uiTexture.Texture,
            rgba,
            0,
            0,
            0,
            checked((uint)frame.Width),
            checked((uint)frame.Height),
            1,
            0,
            0);
        _uiOverlaySignature = signature;
    }

    private void RecordDirectionalShadowPass(
        RenderPacket packet,
        RekallAgeInteractiveShadowFrame shadow)
    {
        if (!shadow.Enabled || packet.Vertices.Length == 0)
        {
            return;
        }

        _commands.SetPipeline(_directionalShadowPipeline);
        _commands.SetVertexBuffer(0, _vertexBuffer);
        _commands.SetIndexBuffer(_indexBuffer, IndexFormat.UInt32);
        _commands.SetGraphicsResourceSet(0, _directionalShadowFrameSet);
        for (var cascadeIndex = 0; cascadeIndex < shadow.CascadeCount; cascadeIndex++)
        {
            _commands.SetFramebuffer(_directionalShadowTarget.Framebuffers[cascadeIndex]);
            _commands.SetFullViewports();
            _commands.SetFullScissorRects();
            _commands.ClearDepthStencil(1f);
            _device.UpdateBuffer(
                _directionalShadowFrameUniformBuffer,
                0,
                new DirectionalShadowFrameUniform(shadow.ViewProjections[cascadeIndex]));
            for (var drawIndex = 0; drawIndex < packet.Draws.Length; drawIndex++)
            {
                var draw = packet.Draws[drawIndex];
                if (!draw.CastShadows || draw.Transparent)
                {
                    continue;
                }

                _drawUniformDynamicOffsets[0] = checked(_drawUniformStrideBytes * (uint)drawIndex);
                _commands.SetGraphicsResourceSet(1, _drawSet, _drawUniformDynamicOffsets);
                _commands.DrawIndexed(draw.IndexCount, 1, draw.FirstIndex, draw.VertexOffset, 0);
            }
        }
    }

    private TextureBinding CreateUiTextureBinding(int width, int height)
    {
        var texture = _factory.CreateTexture(TextureDescription.Texture2D(
            checked((uint)Math.Max(1, width)),
            checked((uint)Math.Max(1, height)),
            mipLevels: 1,
            arrayLayers: 1,
            PixelFormat.R8_G8_B8_A8_UNorm,
            TextureUsage.Sampled));
        var sampler = _factory.CreateSampler(new SamplerDescription(
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            SamplerFilter.MinLinear_MagLinear_MipPoint,
            ComparisonKind.Never,
            maximumAnisotropy: 1,
            minimumLod: 0,
            maximumLod: 0,
            lodBias: 0,
            borderColor: SamplerBorderColor.TransparentBlack));
        var resourceSet = _factory.CreateResourceSet(new ResourceSetDescription(_hudTextureLayout, texture, sampler));
        return new TextureBinding(texture, sampler, resourceSet);
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

    private static HudVertex[] BuildFullScreenOverlayVertices(bool visible)
    {
        if (!visible)
        {
            return [];
        }

        var color = Vector4.One;
        return
        [
            new HudVertex(new Vector3(-1, 1, 0), color, new Vector2(0, 0)),
            new HudVertex(new Vector3(1, 1, 0), color, new Vector2(1, 0)),
            new HudVertex(new Vector3(1, -1, 0), color, new Vector2(1, 1)),
            new HudVertex(new Vector3(-1, 1, 0), color, new Vector2(0, 0)),
            new HudVertex(new Vector3(1, -1, 0), color, new Vector2(1, 1)),
            new HudVertex(new Vector3(-1, -1, 0), color, new Vector2(0, 1))
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
            draw.EmissiveTextureId,
            draw.CloudShadowTextureId,
            draw.SurfaceWaterTextureId);
        if (_materialSets.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var baseColor = ResolveTexture(draw.TextureId, _whiteTexture);
        var normal = ResolveTexture(draw.NormalTextureId, _flatNormalTexture);
        var metallicRoughness = ResolveTexture(draw.MetallicRoughnessTextureId, _defaultMetallicRoughnessTexture);
        var occlusion = ResolveTexture(draw.OcclusionTextureId, _whiteTexture);
        var emissive = ResolveTexture(draw.EmissiveTextureId, _whiteTexture);
        var cloudShadow = ResolveTexture(draw.CloudShadowTextureId, _whiteTexture);
        var surfaceWater = ResolveTexture(draw.SurfaceWaterTextureId, _whiteTexture);
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
            emissive.Sampler,
            cloudShadow.Texture,
            cloudShadow.Sampler,
            surfaceWater.Texture,
            surfaceWater.Sampler));
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
        int sceneSupersampleFactor,
        ResourceLayout presentTextureLayout)
    {
        sceneSupersampleFactor = Math.Clamp(sceneSupersampleFactor, 1, 4);
        var width = checked((uint)Math.Max(1, displayWidth * sceneSupersampleFactor));
        var height = checked((uint)Math.Max(1, displayHeight * sceneSupersampleFactor));
        // Floating-point HDR scene target. The scene pass writes linear radiance and the
        // present pass tone maps, so highlights survive to be bloomed and graded rather than
        // being clamped to 1.0 at the end of the scene shader. The LDR target this replaced is
        // why the interactive path could not implement AgX or a white point at all.
        // A mip chain on the scene colour target is what makes a wide bloom smooth. Gathering
        // a wide radius from mip 0 with a handful of taps produces visibly separate copies of
        // each bright pixel; sampling a downsampled level instead means every tap already
        // averages many pixels, which is how the Vulkan capture path's bloom pyramid behaves.
        var bloomMipLevels = (uint)Math.Clamp(
            (int)Math.Log2(Math.Max(1, Math.Min(width, height))) - 1,
            1,
            6);
        var color = factory.CreateTexture(TextureDescription.Texture2D(
            width,
            height,
            bloomMipLevels,
            arrayLayers: 1,
            PixelFormat.R16_G16_B16_A16_Float,
            TextureUsage.RenderTarget | TextureUsage.Sampled | TextureUsage.GenerateMipmaps));
        var depth = factory.CreateTexture(TextureDescription.Texture2D(
            width,
            height,
            mipLevels: 1,
            arrayLayers: 1,
            PixelFormat.D32_Float_S8_UInt,
            TextureUsage.DepthStencil | TextureUsage.Sampled));
        var framebuffer = factory.CreateFramebuffer(new FramebufferDescription(depth, color));
        var sampler = factory.CreateSampler(new SamplerDescription(
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            SamplerFilter.MinLinear_MagLinear_MipLinear,
            ComparisonKind.Never,
            maximumAnisotropy: 1,
            minimumLod: 0,
            maximumLod: bloomMipLevels,
            lodBias: 0,
            borderColor: SamplerBorderColor.TransparentBlack));
        var resourceSet = factory.CreateResourceSet(new ResourceSetDescription(
            presentTextureLayout,
            color,
            sampler,
            depth,
            sampler));
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

    private void EnsureDirectionalShadowTarget(int resolution)
    {
        resolution = Math.Clamp(resolution, 256, 4096);
        if (_directionalShadowTarget.Resolution == resolution)
        {
            return;
        }

        _device.WaitForIdle();
        _frameSet.Dispose();
        _directionalShadowTarget.Dispose();
        _directionalShadowTarget = CreateDirectionalShadowTarget(_factory, resolution);
        _frameSet = _factory.CreateResourceSet(new ResourceSetDescription(
            _frameLayout,
            _frameUniformBuffer,
            _directionalShadowTarget.View,
            _directionalShadowTarget.Sampler,
            _fogUniformBuffer,
            _environmentTexture.Texture,
            _environmentTexture.Sampler));
        _log($"Interactive directional shadow atlas recreated resolution={resolution} cascades={RekallAgeInteractiveShadowFramePlanner.MaximumCascadeCount}.");
    }

    private static DirectionalShadowTarget CreateDirectionalShadowTarget(ResourceFactory factory, int resolution)
    {
        resolution = Math.Clamp(resolution, 256, 4096);
        var texture = factory.CreateTexture(TextureDescription.Texture2D(
            checked((uint)resolution),
            checked((uint)resolution),
            mipLevels: 1,
            arrayLayers: RekallAgeInteractiveShadowFramePlanner.MaximumCascadeCount,
            PixelFormat.D32_Float_S8_UInt,
            TextureUsage.DepthStencil | TextureUsage.Sampled));
        var view = factory.CreateTextureView(new TextureViewDescription(
            texture,
            baseMipLevel: 0,
            mipLevels: 1,
            baseArrayLayer: 0,
            arrayLayers: RekallAgeInteractiveShadowFramePlanner.MaximumCascadeCount));
        var sampler = factory.CreateSampler(new SamplerDescription(
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            SamplerFilter.MinLinear_MagLinear_MipPoint,
            ComparisonKind.Never,
            maximumAnisotropy: 1,
            minimumLod: 0,
            maximumLod: 0,
            lodBias: 0,
            borderColor: SamplerBorderColor.OpaqueWhite));
        var framebuffers = Enumerable.Range(0, RekallAgeInteractiveShadowFramePlanner.MaximumCascadeCount)
            .Select(layer => factory.CreateFramebuffer(new FramebufferDescription(
                new FramebufferAttachmentDescription(texture, checked((uint)layer)),
                [])))
            .ToArray();
        return new DirectionalShadowTarget(resolution, texture, view, sampler, framebuffers);
    }

    private static RenderPacket ApplyInteractiveShadowFrame(
        RenderPacket packet,
        RekallAgeInteractiveShadowFrame shadow)
    {
        var matrices = shadow.ViewProjections;
        FrameUniform Apply(FrameUniform uniform) => uniform with
        {
            ShadowViewProjection0 = matrices[0],
            ShadowViewProjection1 = matrices[1],
            ShadowViewProjection2 = matrices[2],
            ShadowViewProjection3 = matrices[3],
            ShadowSplitDepths = shadow.SplitDepths,
            ShadowParameters = new Vector4(
                shadow.Enabled ? shadow.CascadeCount : 0,
                shadow.DepthBias,
                shadow.NormalBias,
                1f / Math.Max(1, shadow.Resolution))
        };

        return packet with
        {
            FrameUniform = Apply(packet.FrameUniform),
            StereoFrameUniforms = packet.StereoFrameUniforms
                .Select(item => item with { Uniform = Apply(item.Uniform) })
                .ToArray()
        };
    }

    private static InteractiveFogUniform BuildInteractiveFogUniform(RekallAgeInteractiveFogFrame fog)
    {
        var packed = Enumerable.Repeat(InteractiveFogVolumeUniform.Disabled, RekallAgeInteractiveFogFramePlanner.MaximumVolumeCount)
            .ToArray();
        for (var index = 0; index < fog.Volumes.Count && index < packed.Length; index++)
        {
            var volume = fog.Volumes[index];
            var shape = volume.Shape.Equals("box", StringComparison.Ordinal) ? 1f
                : volume.Shape.Equals("sphere", StringComparison.Ordinal) ? 2f
                : 0f;
            packed[index] = new InteractiveFogVolumeUniform(
                new Vector4(volume.Position, shape),
                new Vector4(volume.HalfExtents, volume.Density),
                new Vector4(volume.Albedo, volume.Anisotropy),
                new Vector4(volume.Emission, volume.HeightFalloff),
                new Vector4(volume.BlendDistance, volume.Priority, 0, 0),
                volume.WorldToLocal);
        }

        return new InteractiveFogUniform(
            new Vector4(fog.Enabled ? fog.Volumes.Count : 0, 6, 0.24f, 0),
            packed[0], packed[1], packed[2], packed[3],
            packed[4], packed[5], packed[6], packed[7]);
    }

    private Dictionary<string, TextureBinding> CreateTextureBindings(
        GraphicsDevice device,
        ResourceFactory factory,
        ResourceLayout layout,
        RekallAgeRuntimeViewportAssetSet assets,
        uint maximumAnisotropy = 8)
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
                layout,
                maximumAnisotropy);
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
                    layout,
                    maximumAnisotropy);
                _log($"Decoded runtime texture id={runtimeTexture.Key} format={runtimeTexture.Value.Format} size={decoded.Width}x{decoded.Height} to RGBA upload.");
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
                layout,
                maximumAnisotropy);
        }

        foreach (var texture in assets.Models.Values
            .SelectMany(meshes => meshes)
            .SelectMany(mesh => new[]
            {
                mesh.BaseColorTexture,
                mesh.MetallicRoughnessTexture,
                mesh.NormalTexture,
                mesh.OcclusionTexture,
                mesh.EmissiveTexture,
                mesh.SurfaceWaterTexture
            })
            .OfType<RekallAgeVulkanSceneTexture>()
            .GroupBy(texture => texture.Id, StringComparer.Ordinal)
            .Select(group => group.First()))
        {
            if (!textures.ContainsKey(texture.Id))
            {
                textures[texture.Id] = CreateTextureBinding(device, factory, texture, layout, maximumAnisotropy);
            }
        }

        _log($"Created texture resources count={textures.Count}.");
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

    private TextureBinding CreateTextureBinding(
        GraphicsDevice device,
        ResourceFactory factory,
        RekallAgeVulkanSceneTexture texture,
        ResourceLayout layout,
        uint maximumAnisotropy = 8)
    {
        if (texture.RuntimeTexture is { } runtimeTexture
            && TryGetTexturePixelFormat(runtimeTexture.Format, out var runtimeFormat)
            && runtimeTexture.MipLevels.Count > 0)
        {
            return CreateRuntimeTextureBinding(device, factory, texture, runtimeTexture, runtimeFormat, layout, maximumAnisotropy);
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

        var anisotropy = device.Features.SamplerAnisotropy ? Math.Max(1u, maximumAnisotropy) : 1u;
        var filter = ToSamplerFilter(texture.Sampler.MinFilter, texture.Sampler.MagFilter, anisotropy > 1);
        var sampler = factory.CreateSampler(new SamplerDescription(
            ToSamplerAddressMode(texture.Sampler.WrapS),
            ToSamplerAddressMode(texture.Sampler.WrapT),
            SamplerAddressMode.Wrap,
            filter,
            ComparisonKind.Never,
            maximumAnisotropy: filter == SamplerFilter.Anisotropic ? anisotropy : 1u,
            minimumLod: 0,
            maximumLod: mipLevels - 1,
            lodBias: 0,
            borderColor: SamplerBorderColor.TransparentBlack));
        var resourceSet = factory.CreateResourceSet(new ResourceSetDescription(layout, gpuTexture, sampler));
        return new TextureBinding(gpuTexture, sampler, resourceSet);
    }

    private TextureBinding CreateRuntimeTextureBinding(
        GraphicsDevice device,
        ResourceFactory factory,
        RekallAgeVulkanSceneTexture texture,
        RekallAgeRuntimeTextureAsset runtimeTexture,
        PixelFormat format,
        ResourceLayout layout,
        uint maximumAnisotropy)
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

        var anisotropy = device.Features.SamplerAnisotropy ? Math.Max(1u, maximumAnisotropy) : 1u;
        var filter = ToSamplerFilter(texture.Sampler.MinFilter, texture.Sampler.MagFilter, anisotropy > 1);
        var sampler = factory.CreateSampler(new SamplerDescription(
            ToSamplerAddressMode(texture.Sampler.WrapS),
            ToSamplerAddressMode(texture.Sampler.WrapT),
            SamplerAddressMode.Wrap,
            filter,
            ComparisonKind.Never,
            maximumAnisotropy: filter == SamplerFilter.Anisotropic ? anisotropy : 1u,
            minimumLod: 0,
            maximumLod: mipLevels - 1,
            lodBias: 0,
            borderColor: SamplerBorderColor.TransparentBlack));
        var resourceSet = factory.CreateResourceSet(new ResourceSetDescription(layout, gpuTexture, sampler));
        _log($"Uploaded runtime texture id={texture.Id} format={runtimeTexture.Format} size={runtimeTexture.Width}x{runtimeTexture.Height} mips={runtimeTexture.MipLevels.Count}.");
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
            _log($"Frame={frameNumber} Fps={_fps} Vertices={vertexCount} Backend={_device.BackendType} Window={_surfaceWidth}x{_surfaceHeight}");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct GpuVertex(Vector3 Position, Vector3 Normal, Vector4 Color, Vector2 UV);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct HudVertex(Vector3 Position, Vector4 Color, Vector2 UV);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct FrameUniform(
        Matrix4x4 ViewProjection,
        Vector4 LightDirection,
        Vector4 LightColor,
        Vector4 LightPosition,
        Vector4 CameraPosition,
        Vector4 AdditionalLightDirection,
        Vector4 AdditionalLightColor,
        Vector4 AdditionalLightPosition,
        Vector4 AdditionalLightParameters,
        Vector4 AdditionalLightColor2,
        Vector4 AdditionalLightPosition2,
        Vector4 AdditionalLightParameters2,
        Vector4 AdditionalLightColor3,
        Vector4 AdditionalLightPosition3,
        Vector4 AdditionalLightParameters3,
        Vector4 AdditionalLightColor4,
        Vector4 AdditionalLightPosition4,
        Vector4 AdditionalLightParameters4,
        Vector4 SpotLightColor = default,
        Vector4 SpotLightPosition = default,
        Vector4 SpotLightDirection = default,
        Vector4 SpotLightParameters = default,
        Vector4 SpotLightColor2 = default,
        Vector4 SpotLightPosition2 = default,
        Vector4 SpotLightDirection2 = default,
        Vector4 SpotLightParameters2 = default,
        Vector4 SpotLightColor3 = default,
        Vector4 SpotLightPosition3 = default,
        Vector4 SpotLightDirection3 = default,
        Vector4 SpotLightParameters3 = default,
        Vector4 SpotLightColor4 = default,
        Vector4 SpotLightPosition4 = default,
        Vector4 SpotLightDirection4 = default,
        Vector4 SpotLightParameters4 = default,
        Matrix4x4 ShadowViewProjection0 = default,
        Matrix4x4 ShadowViewProjection1 = default,
        Matrix4x4 ShadowViewProjection2 = default,
        Matrix4x4 ShadowViewProjection3 = default,
        Vector4 ShadowSplitDepths = default,
        Vector4 ShadowParameters = default,
        Vector4 EnvironmentParameters = default,
        Vector4 EnvironmentAmbientSkyColor = default,
        Vector4 EnvironmentAmbientGroundColor = default);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct DirectionalShadowFrameUniform(Matrix4x4 ViewProjection);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct InteractiveFogVolumeUniform(
        Vector4 PositionShape,
        Vector4 HalfExtentsDensity,
        Vector4 AlbedoAnisotropy,
        Vector4 EmissionHeightFalloff,
        Vector4 BlendPriority,
        Matrix4x4 WorldToLocal)
    {
        public static InteractiveFogVolumeUniform Disabled { get; } = new(
            Vector4.Zero, Vector4.Zero, Vector4.Zero, Vector4.Zero, Vector4.Zero, Matrix4x4.Identity);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct InteractiveFogUniform(
        Vector4 Settings,
        InteractiveFogVolumeUniform Volume0,
        InteractiveFogVolumeUniform Volume1,
        InteractiveFogVolumeUniform Volume2,
        InteractiveFogVolumeUniform Volume3,
        InteractiveFogVolumeUniform Volume4,
        InteractiveFogVolumeUniform Volume5,
        InteractiveFogVolumeUniform Volume6,
        InteractiveFogVolumeUniform Volume7);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct DrawUniform(
        Matrix4x4 Model,
        Vector4 MaterialFactors,
        Vector4 EmissiveFactors,
        Vector4 AtmosphereFactors0,
        Vector4 AtmosphereFactors1,
        Vector4 AtmosphereColor0,
        Vector4 AtmosphereColor1,
        Vector4 AtmosphereColor2,
        Vector4 CloudFactors,
        Vector4 CloudColor,
        Vector4 CloudShadowFactors,
        Vector4 SurfaceWaterFactors,
        Vector4 ShadowFactors);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct PostProcessUniform(
        Vector4 Parameters,
        Vector4 ScreenParameters,
        Vector4 AmbientOcclusionParameters,
        Matrix4x4 InverseViewProjection,
        Vector4 CameraPosition,
        Vector4 EnvironmentParameters)
    {
        public static PostProcessUniform Default { get; } = new(
            new Vector4(0.86f, 0.42f, 1f, 1f),
            Vector4.One,
            Vector4.Zero,
            Matrix4x4.Identity,
            Vector4.Zero,
            new Vector4(0, 11.2f, 0, 0));
        public static PostProcessUniform Disabled { get; } = new(
            new Vector4(0.86f, 0f, 1f, 0f),
            Vector4.One,
            Vector4.Zero,
            Matrix4x4.Identity,
            Vector4.Zero,
            new Vector4(0, 11.2f, 0, 0));
    }

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
        RekallAgeVulkanSceneBatch StableBatch,
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
        string? CloudShadowTextureId,
        string? SurfaceWaterTextureId,
        Vector4 MaterialFactors,
        Vector4 EmissiveFactors,
        Vector4 AtmosphereFactors0,
        Vector4 AtmosphereFactors1,
        Vector4 AtmosphereColor0,
        Vector4 AtmosphereColor1,
        Vector4 AtmosphereColor2,
        Vector4 CloudFactors,
        Vector4 CloudColor,
        Vector4 CloudShadowFactors,
        Vector4 SurfaceWaterFactors,
        bool Transparent,
        RekallAgeRuntimeViewportShaderPipeline? ShaderPipeline,
        string EntityId,
        bool CastShadows,
        bool ReceiveShadows,
        string AlphaMode,
        float AlphaCutoff);

    private readonly record struct MaterialKey(
        string? BaseColorTextureId,
        string? NormalTextureId,
        string? MetallicRoughnessTextureId,
        string? OcclusionTextureId,
        string? EmissiveTextureId,
        string? CloudShadowTextureId,
        string? SurfaceWaterTextureId);

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

    private sealed record DirectionalShadowTarget(
        int Resolution,
        Texture Texture,
        TextureView View,
        Sampler Sampler,
        IReadOnlyList<Framebuffer> Framebuffers) : IDisposable
    {
        public void Dispose()
        {
            foreach (var framebuffer in Framebuffers)
            {
                framebuffer.Dispose();
            }
            Sampler.Dispose();
            View.Dispose();
            Texture.Dispose();
        }
    }
}
