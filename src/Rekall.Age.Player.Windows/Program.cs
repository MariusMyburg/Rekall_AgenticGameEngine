using System.Diagnostics;
using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.Recovery;
using Rekall.Age.Core.Diagnostics;
using Rekall.Age.Core.Product;
using Rekall.Age.Playback;
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
        try
        {
            return await RunAsync(args).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            PlayerLog.Write($"REKALL_PLAYER_PROCESS_FATAL: {exception}");
            Console.Error.WriteLine($"REKALL_PLAYER_PROCESS_FATAL: {exception.Message}");
            try
            {
                var report = RekallAgeFailureReport.Create(
                    "player.windows", "fatal", "process.unhandled", "REKALL_PLAYER_PROCESS_FATAL", "none",
                    0, 0, null,
                    exception.GetType().FullName ?? exception.GetType().Name,
                    exception.Message,
                    exception.StackTrace ?? string.Empty,
                    "vulkan",
                    args.FirstOrDefault() ?? string.Empty,
                    args.Skip(1).FirstOrDefault() ?? string.Empty,
                    ["The player failed outside the supervised graphics-session lifecycle."],
                    ["rekall.diagnostics.inspect_failures"]);
                var path = await new RekallAgeFailureReportStore().WriteAsync(report).ConfigureAwait(false);
                Console.Error.WriteLine($"Report: {path}");
            }
            catch (Exception reportException)
            {
                PlayerLog.Write($"REKALL_PLAYER_FAILURE_REPORT_WRITE_FAILED: {reportException.Message}");
            }

            return 10;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        PlayerLog.Write("Player process starting.");
        args = RekallAgePackagedLaunchResolver.Resolve(
            Environment.ProcessPath ?? typeof(Program).Assembly.Location,
            args);
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
        var contentMode = RekallAgePlayerContentModePlanner.Plan(args);
        var playableMode = contentMode.Mode == RekallAgePlayerContentMode.LegacyProofAdapter;
        if (contentMode.ObsoletePlayableFlagPresent)
        {
            PlayerLog.Write("The obsolete --playable option was ignored; Windows player launches the canonical runtime scene by default. Use --legacy-playable-adapter only for explicit proof-adapter diagnostics.");
        }
        var sceneSupersampleFactor = ReadPositiveIntOption(args, "--ssaa") ?? RekallAgeVeldridPlayer.DefaultSceneSupersampleFactor;
        var openXrEyeWidth = ReadPositiveIntOption(args, "--vr-eye-width") ?? RekallAgeVeldridPlayer.DefaultOpenXrPlayableEyeWidth;
        var openXrEyeHeight = ReadPositiveIntOption(args, "--vr-eye-height") ?? RekallAgeVeldridPlayer.DefaultOpenXrPlayableEyeHeight;
        var frameLimit = ReadPositiveIntOption(args, "--frames") ?? 0;
        RekallAgePlayerScreenshotRequest.Path = ReadOption(args, "--screenshot");
        RekallAgePlayerScreenshotRequest.Frame = ReadPositiveIntOption(args, "--screenshot-frame") ?? 60;
        var debugHudEnabled = RekallAgePlayerPresentationPolicy.Plan(args).DebugHudEnabled;
        var audioRequired = HasOption(args, "--audio-required");
        var projectRoot = Path.GetFullPath(args[0]);
        var sceneName = args[1];
        var faultInjection = RekallAgePlayerFaultInjection.Parse(args, ReadPositiveIntOption, HasOption);
        var factory = new RekallAgeVeldridPlayerSessionFactory(
            projectRoot,
            sceneName,
            syncToVerticalBlank,
            openXrRequested,
            simulateXrInput,
            probeOpenXrCompositor,
            playableMode,
            sceneSupersampleFactor,
            openXrEyeWidth,
            openXrEyeHeight,
            debugHudEnabled,
            audioRequired,
            faultInjection);
        var evidenceWriter = new RekallAgePlayerFailureReportWriter(
            new RekallAgeFailureReportStore(),
            new RekallAgePlayerFailureReportContext("player.windows", backend, projectRoot, sceneName));
        var supervisor = new RekallAgePlayerSessionSupervisor(
            factory,
            new RekallAgeGraphicsFailureClassifier(),
            evidenceWriter);

        PlayerLog.Write("Player entering supervised render loop.");
        var result = await supervisor.RunAsync(frameLimit <= 0 ? null : frameLimit, CancellationToken.None)
            .ConfigureAwait(false);
        Console.WriteLine(result.Code);
        Console.WriteLine($"Outcome: {result.Outcome}");
        Console.WriteLine($"Recovery mode: {result.RecoveryMode}");
        Console.WriteLine($"Attempts: {result.Attempts}");
        Console.WriteLine($"Frames: {result.CompletedFrames}/{result.RequestedFrames?.ToString(CultureInfo.InvariantCulture) ?? "continuous"}");
        foreach (var path in result.EvidencePaths)
        {
            Console.WriteLine($"Report: {path}");
        }
        foreach (var issue in result.EvidenceIssues)
        {
            Console.Error.WriteLine(issue);
        }

        if (!result.Succeeded)
        {
            if (result.LastFailure?.Exception is RekallAgePlayerAudioUnavailableException)
            {
                PlayerLog.Write("Player process exiting: required audio output is unavailable.");
                Console.Error.WriteLine("Required SDL audio output is unavailable. See the player log for details.");
                return 3;
            }

            PlayerLog.Write($"Player process exiting: {result.Code}.");
            return result.Outcome == RekallAgePlayerSessionOutcomes.Exhausted ? 11 : 10;
        }

        if (audioRequired && factory.AudioSubmittedFrameCount == 0)
        {
            PlayerLog.Write("Player process exiting: required audio output received no runtime mix frames.");
            Console.Error.WriteLine("Required SDL audio output received no runtime mix frames.");
            return 4;
        }

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

    internal static int? ReadPositiveIntOption(string[] args, string name)
    {
        var raw = ReadOption(args, name);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : null;
    }
}

internal sealed class RekallAgeVeldridPlayer : IAsyncDisposable
{
    private static readonly object SdlInitializationGate = new();
    private static bool _sdlVideoInitialized;
    private const int InitialWidth = 1280;
    private const int InitialHeight = 720;
    private const int HudWidth = 360;
    private const int HudHeight = 224;
    private const int HudMargin = 16;
    private const int PlayableWidth = 960;
    private const int PlayableHeight = 540;
    public const int DefaultSceneSupersampleFactor = 1;
    public const int DefaultOpenXrPlayableEyeWidth = 1600;
    public const int DefaultOpenXrPlayableEyeHeight = 1600;

    private static readonly JsonSerializerOptions LiveJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _projectRoot;
    private string _sceneName;
    private readonly bool _playableMode;
    private readonly IRekallAgePlayableGame? _playableGame;
    private readonly RekallAgePlayableFrameRasterizer _playableRasterizer = new();
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private readonly string _livePipeName;
    private readonly ConcurrentQueue<LiveEditWorkItem> _liveEditQueue = new();
    private readonly RekallAgeLivePlayerNamedPipeServer _liveServer;
    private FileSystemWatcher? _assetWatcher;
    private FileSystemWatcher? _shaderWatcher;
    private readonly Sdl2Window _window;
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
    private readonly ResourceSet _postProcessSet;
    private readonly RekallAgeRuntimeExecutionLoop _runtimeLoop;
    private readonly RekallAgeSdlControllerInput _controllerInput = new();
    private readonly RekallAgeRuntimeSimulationClock _simulationClock;
    private readonly RekallAgeSdlAudioOutput? _audioOutput;
    private readonly RekallAgeRuntimeRenderFrameBuilder _frameBuilder = new();
    private RekallAgeRuntimeViewportAssetSet _assets;
    private int _entityCount;
    private readonly Dictionary<string, TextureBinding> _textures;
    private readonly Dictionary<MaterialKey, ResourceSet> _materialSets = new();
    private readonly TextureBinding _whiteTexture;
    private readonly TextureBinding _flatNormalTexture;
    private readonly TextureBinding _defaultMetallicRoughnessTexture;
    private readonly TextureBinding _environmentTexture;
    private readonly TextureBinding _hudTexture;
    private readonly ResourceSet _hudTextureSet;
    private TextureBinding _uiTexture;
    private readonly RekallAgeRuntimeSoftwareRenderer _softwareRenderer = new();
    private readonly RekallAgeOpenXrSessionBootstrapResult? _openXrStatus;
    private readonly RekallAgeOpenXrVulkanInteropInspection? _openXrVulkanInterop;
    private readonly RekallAgeOpenXrCompositorSessionBootstrapResult? _openXrCompositorSession;
    private readonly bool _simulateXrInput;
    private readonly bool _debugHudEnabled;
    private const string SceneTransitionComponentType = "Rekall.SceneTransition";
    private const string PersistentStateComponentType = "Rekall.PersistentState";
    private readonly Dictionary<string, string> _persistedStateBySlot = new(StringComparer.Ordinal);

    /// <summary>
    /// Slots whose stored document could not be read. Writing is refused for these: a scene
    /// carries authored defaults, so saving after a failed read would replace a player's real
    /// saved state with those defaults and destroy it. A read that fails must never cause a
    /// write.
    /// </summary>
    private readonly HashSet<string> _stateSlotsBlockedFromWriting = new(StringComparer.Ordinal);
    private static readonly MouseButton[] PolledMouseButtons =
        [MouseButton.Left, MouseButton.Right, MouseButton.Middle];

    private readonly Dictionary<string, bool> _mouseButtonDown = new(StringComparer.Ordinal);
    private string? _screenshotPath;
    private int _screenshotFrame;
    private int _lastUiVertexCount;
    private int _lastHudVertexCount;
    private readonly int _sceneSupersampleFactor;
    private readonly int _openXrEyeWidth;
    private readonly int _openXrEyeHeight;
    private SceneRenderTarget _sceneTarget;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
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
    private int _frameIndex;
    private double _lastPlayableTickSeconds;
    private Rekall.Age.Runtime.Abstractions.RekallAgeRuntimeWorld _runtimeWorld;
    private Vector2 _lastMousePosition;
    private Vector2 _previousMousePosition;
    private bool _hasMousePosition;
    private bool _mouseCaptured;
    private readonly RekallAgeWindowsInputBridge _inputBridge = new();
    private readonly uint[] _drawUniformDynamicOffsets = new uint[1];
    private int _lastFpsFrame;
    private double _lastFpsTime;
    private int _fps;
    private int _sceneRevision = 1;
    private int _assetRevision = 1;
    private int _assetHotReloadPending;
    private long _lastAssetHotReloadRequestTicks;
    private int _shaderHotReloadPending;
    private long _lastShaderHotReloadRequestTicks;
    private CachedRenderGeometry? _cachedStaticGeometry;
    private bool _hudDirty = true;
    private int? _uiOverlaySignature;
    private RekallAgeSceneDocument _sceneDocument;
    private readonly object _runtimeInputGate = new();
    private RekallAgeRuntimeInputState _latestRuntimeInput = RekallAgeRuntimeInputState.Empty;
    private long _runtimeInputSequence;
    private long _openXrLastConsumedInputSequence;
    private CancellationTokenSource? _openXrSubmitCts;
    private Task? _openXrSubmitTask;
    private bool _audioSubmissionLogged;
    private string? _lastRuntimeGpuWorkloadStatus;
    private int _profileFrameCount;
    private double _profileSimulationMs;
    private double _profileFrameBuildMs;
    private double _profilePacketMs;
    private double _profileUiMs;
    private double _profileSubmitMs;
    private int _profileGeometryCacheHits;
    private int _profileGeometryCacheMisses;
    private DirectionalShadowTarget _directionalShadowTarget;
    private readonly RekallAgeInteractiveShadowFramePlanner _interactiveShadowPlanner = new();
    private readonly RekallAgeInteractiveFogFramePlanner _interactiveFogPlanner = new();
    private readonly RekallAgeInteractiveAmbientOcclusionPlanner _interactiveAmbientOcclusionPlanner = new();
    private readonly RekallAgeInteractiveParticleBridge _interactiveParticleBridge = new();

    public bool AudioOutputAvailable => _audioOutput is not null;

    public int AudioSubmittedFrameCount => _audioOutput?.SubmittedFrameCount ?? 0;

    private RekallAgeVeldridPlayer(
        string projectRoot,
        string sceneName,
        bool playableMode,
        IRekallAgePlayableGame? playableGame,
        RekallAgeSceneDocument sceneDocument,
        Sdl2Window window,
        GraphicsDevice device,
        CommandList commands,
        Pipeline scenePipeline,
        Pipeline sceneTransparentPipeline,
        Pipeline directionalShadowPipeline,
        RekallAgeVeldridShaderPipelineCache shaderPipelineCache,
        Pipeline presentPipeline,
        Pipeline hudPipeline,
        ResourceLayout frameLayout,
        ResourceLayout directionalShadowFrameLayout,
        ResourceLayout drawLayout,
        ResourceLayout materialLayout,
        ResourceLayout presentTextureLayout,
        ResourceLayout postProcessLayout,
        ResourceLayout hudTextureLayout,
        ResourceSet frameSet,
        ResourceSet directionalShadowFrameSet,
        ResourceSet drawSet,
        ResourceSet postProcessSet,
        DeviceBuffer vertexBuffer,
        DeviceBuffer indexBuffer,
        DeviceBuffer hudVertexBuffer,
        DeviceBuffer frameUniformBuffer,
        DeviceBuffer fogUniformBuffer,
        DeviceBuffer directionalShadowFrameUniformBuffer,
        DeviceBuffer drawUniformBuffer,
        DeviceBuffer postProcessUniformBuffer,
        Rekall.Age.Runtime.Abstractions.RekallAgeRuntimeWorld runtimeWorld,
        RekallAgeRuntimeExecutionLoop runtimeLoop,
        RekallAgeRuntimeViewportAssetSet assets,
        int entityCount,
        Dictionary<string, TextureBinding> textures,
        TextureBinding whiteTexture,
        TextureBinding flatNormalTexture,
        TextureBinding defaultMetallicRoughnessTexture,
        TextureBinding environmentTexture,
        TextureBinding hudTexture,
        DirectionalShadowTarget directionalShadowTarget,
        RekallAgeOpenXrSessionBootstrapResult? openXrStatus,
        RekallAgeOpenXrVulkanInteropInspection? openXrVulkanInterop,
        RekallAgeOpenXrCompositorSessionBootstrapResult? openXrCompositorSession,
        bool simulateXrInput,
        int sceneSupersampleFactor,
        int openXrEyeWidth,
        int openXrEyeHeight,
        bool debugHudEnabled)
    {
        _projectRoot = projectRoot;
        _sceneName = sceneName;
        _playableMode = playableMode;
        _playableGame = playableGame;
        _sceneDocument = sceneDocument;
        _livePipeName = RekallAgeLivePlayerEndpoint.ResolvePipeName(projectRoot, sceneName);
        _liveServer = new RekallAgeLivePlayerNamedPipeServer(_livePipeName, EnqueueLiveEditAsync);
        _window = window;
        _device = device;
        _factory = device.ResourceFactory;
        _commands = commands;
        _scenePipeline = scenePipeline;
        _sceneTransparentPipeline = sceneTransparentPipeline;
        _directionalShadowPipeline = directionalShadowPipeline;
        _shaderPipelineCache = shaderPipelineCache;
        _presentPipeline = presentPipeline;
        _presentPassAdapter = new RekallAgeVeldridPresentPassAdapter();
        _runtimeGpuWorkloadExecutor = new RekallAgeVeldridRuntimeGpuWorkloadExecutor(projectRoot, device, commands);
        _hudPipeline = hudPipeline;
        _frameLayout = frameLayout;
        _directionalShadowFrameLayout = directionalShadowFrameLayout;
        _drawLayout = drawLayout;
        _materialLayout = materialLayout;
        _presentTextureLayout = presentTextureLayout;
        _postProcessLayout = postProcessLayout;
        _hudTextureLayout = hudTextureLayout;
        _frameSet = frameSet;
        _directionalShadowFrameSet = directionalShadowFrameSet;
        _drawSet = drawSet;
        _postProcessSet = postProcessSet;
        _vertexBuffer = vertexBuffer;
        _indexBuffer = indexBuffer;
        _hudVertexBuffer = hudVertexBuffer;
        _frameUniformBuffer = frameUniformBuffer;
        _fogUniformBuffer = fogUniformBuffer;
        _directionalShadowFrameUniformBuffer = directionalShadowFrameUniformBuffer;
        _drawUniformBuffer = drawUniformBuffer;
        _postProcessUniformBuffer = postProcessUniformBuffer;
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
        _audioOutput = RekallAgeSdlAudioOutput.TryCreate(out var audioStatus);
        PlayerLog.Write(audioStatus);
        _assets = assets;
        _entityCount = entityCount;
        _textures = textures;
        _whiteTexture = whiteTexture;
        _flatNormalTexture = flatNormalTexture;
        _defaultMetallicRoughnessTexture = defaultMetallicRoughnessTexture;
        _environmentTexture = environmentTexture;
        _hudTexture = hudTexture;
        _directionalShadowTarget = directionalShadowTarget;
        _hudTextureSet = _factory.CreateResourceSet(new ResourceSetDescription(_hudTextureLayout, _hudTexture.Texture, _hudTexture.Sampler));
        _uiTexture = CreateUiTextureBinding(InitialWidth, InitialHeight);
        _openXrStatus = openXrStatus;
        _openXrVulkanInterop = openXrVulkanInterop;
        _openXrCompositorSession = openXrCompositorSession;
        _simulateXrInput = simulateXrInput;
        _debugHudEnabled = debugHudEnabled;
        LoadPersistentState();
        _screenshotPath = RekallAgePlayerScreenshotRequest.Path;
        _screenshotFrame = Math.Max(1, RekallAgePlayerScreenshotRequest.Frame);
        _sceneSupersampleFactor = Math.Clamp(sceneSupersampleFactor, 1, 4);
        _openXrEyeWidth = Math.Clamp(openXrEyeWidth, 64, RekallAgeOpenXrHeadsetSubmitPlanner.MaxSceneEyeExtent);
        _openXrEyeHeight = Math.Clamp(openXrEyeHeight, 64, RekallAgeOpenXrHeadsetSubmitPlanner.MaxSceneEyeExtent);
        _sceneTarget = CreateSceneRenderTarget(_factory, InitialWidth, InitialHeight, _sceneSupersampleFactor, _presentTextureLayout);
        // Capture the pointer only for scenes that actually steer with mouse motion. Grabbing
        // it unconditionally hides the cursor, which is right for a first-person camera and
        // wrong for anything the player clicks on - a tactical game cannot be played without a
        // visible pointer. Escape still releases capture when it is taken.
        if (!_playableMode && SceneBindsMouseLook(_runtimeWorld))
        {
            SetMouseCapture(true);
        }
    }

    /// <summary>
    /// True when any authored input action map binds a mouse-motion axis, which is how the
    /// runtime projects mouse look. Checked against the authored bindings rather than a
    /// hard-coded genre assumption, so a scene decides for itself whether it wants the pointer.
    /// </summary>
    private static bool SceneBindsMouseLook(RekallAgeRuntimeWorld world)
    {
        foreach (var entity in world.Entities)
        {
            foreach (var component in entity.Components)
            {
                if (!component.Type.Equals("Rekall.InputActionMap", StringComparison.Ordinal))
                {
                    continue;
                }

                var bindings = component.Properties.ToJsonString();
                if (bindings.Contains("mousex", StringComparison.OrdinalIgnoreCase)
                    || bindings.Contains("mousey", StringComparison.OrdinalIgnoreCase)
                    || bindings.Contains("deltax", StringComparison.OrdinalIgnoreCase)
                    || bindings.Contains("deltay", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static async ValueTask<RekallAgeVeldridPlayer> CreateAsync(
        string projectRoot,
        string sceneName,
        bool syncToVerticalBlank,
        bool openXrRequested,
        bool simulateXrInput,
        bool probeOpenXrCompositor,
        bool playableMode,
        int sceneSupersampleFactor,
        int openXrEyeWidth,
        int openXrEyeHeight,
        bool debugHudEnabled,
        CancellationToken cancellationToken)
    {
        sceneSupersampleFactor = Math.Clamp(sceneSupersampleFactor, 1, 4);
        PlayerLog.Write("Loading runtime scene.");
        var scene = await new Rekall.Age.World.RekallAgeSceneStore()
            .LoadAsync(projectRoot, sceneName, cancellationToken);
        var playableGame = playableMode
            ? RekallAgePlayableGameFactory.Create(projectRoot, scene)
            : null;
        if (playableGame is not null)
        {
            PlayerLog.Write($"Loaded playable module kind={playableGame.Kind}.");
        }

        var initialWorld = new RekallAgeRuntimeWorldBuilder().Build(scene, projectRoot);
        var runtimeLoop = RekallAgeRuntimeExecutionLoop.CreateDefault(projectRoot);
        var runResult = await runtimeLoop.RunAsync(initialWorld, 1, cancellationToken);
        var world = runResult.World;
        var baseFrame = new RekallAgeRuntimeRenderFrameBuilder()
            .Build(world, InitialWidth, InitialHeight, debugOverlay: debugHudEnabled);
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
            BuildWindowTitle(sceneName, openXrRequested, simulateXrInput, playableMode));
        PlayerLog.Write("Creating SDL window.");
        EnsureSdlVideoInitialized();
        var window = new Sdl2Window(
            windowInfo.WindowTitle,
            windowInfo.X,
            windowInfo.Y,
            windowInfo.WindowWidth,
            windowInfo.WindowHeight,
            SDL_WindowFlags.OpenGL | SDL_WindowFlags.Resizable | SDL_WindowFlags.Shown,
            threadedProcessing: true);
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
            PlayerLog.Write("OpenXR compositor probe skipped; windowed player will drive headset submission when the HMD session is ready.");
        }

        var commands = factory.CreateCommandList();
        PlayerLog.Write("Compiling SPIR-V shaders.");
        var sceneShaders = factory.CreateFromSpirv(
            new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(SceneVertexShader), "main"),
            new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(SceneFragmentShader), "main"));
        var directionalShadowShaders = factory.CreateFromSpirv(
            new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(DirectionalShadowVertexShader), "main"));
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
            new ResourceLayoutElementDescription("FrameUniform", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment),
            new ResourceLayoutElementDescription("DirectionalShadowAtlas", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("DirectionalShadowSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("InteractiveFogUniform", ResourceKind.UniformBuffer, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("EnvironmentTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("EnvironmentSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
        var directionalShadowFrameLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("DirectionalShadowFrameUniform", ResourceKind.UniformBuffer, ShaderStages.Vertex)));
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
            new ResourceLayoutElementDescription("EmissiveSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("CloudShadowTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("CloudShadowSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("SurfaceWaterTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("SurfaceWaterSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
        var presentTextureLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("SceneTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("SceneSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("SceneDepthTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("SceneDepthSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
        var postProcessLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("PostProcessUniform", ResourceKind.UniformBuffer, ShaderStages.Fragment)));
        var hudTextureLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("SurfaceTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("SurfaceSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
        var directionalShadowTarget = CreateDirectionalShadowTarget(factory, 2048);
        using var initialSceneTarget = CreateSceneRenderTarget(factory, InitialWidth, InitialHeight, sceneSupersampleFactor, presentTextureLayout);
        var sceneShaderSet = new ShaderSetDescription([sceneVertexLayout], sceneShaders);
        var scenePipelineDescription = new GraphicsPipelineDescription(
            BlendStateDescription.SingleAlphaBlend,
            DepthStencilStateDescription.DepthOnlyLessEqual,
            RasterizerStateDescription.CullNone,
            PrimitiveTopology.TriangleList,
            sceneShaderSet,
            [frameLayout, drawLayout, materialLayout],
            initialSceneTarget.Framebuffer.OutputDescription);
        var sceneTransparentPipelineDescription = new GraphicsPipelineDescription(
            BlendStateDescription.SingleAlphaBlend,
            new DepthStencilStateDescription(true, false, ComparisonKind.LessEqual),
            RasterizerStateDescription.CullNone,
            PrimitiveTopology.TriangleList,
            sceneShaderSet,
            [frameLayout, drawLayout, materialLayout],
            initialSceneTarget.Framebuffer.OutputDescription);
        var directionalShadowPipelineDescription = new GraphicsPipelineDescription(
            BlendStateDescription.Empty,
            DepthStencilStateDescription.DepthOnlyLessEqual,
            RasterizerStateDescription.CullNone,
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription([sceneVertexLayout], [directionalShadowShaders]),
            [directionalShadowFrameLayout, drawLayout],
            directionalShadowTarget.Framebuffers[0].OutputDescription);
        var presentShaderSet = new ShaderSetDescription([], presentShaders);
        var presentPipelineDescription = new GraphicsPipelineDescription(
            BlendStateDescription.SingleOverrideBlend,
            DepthStencilStateDescription.Disabled,
            RasterizerStateDescription.CullNone,
            PrimitiveTopology.TriangleList,
            presentShaderSet,
            [presentTextureLayout, postProcessLayout],
            device.SwapchainFramebuffer.OutputDescription);
        var hudShaderSet = new ShaderSetDescription([hudVertexLayout], hudShaders);
        var hudPipelineDescription = new GraphicsPipelineDescription(
            BlendStateDescription.SingleAlphaBlend,
            DepthStencilStateDescription.Disabled,
            RasterizerStateDescription.CullNone,
            PrimitiveTopology.TriangleList,
            hudShaderSet,
            [hudTextureLayout],
            device.SwapchainFramebuffer.OutputDescription);
        PlayerLog.Write("Creating graphics pipelines.");
        var scenePipeline = factory.CreateGraphicsPipeline(scenePipelineDescription);
        var sceneTransparentPipeline = factory.CreateGraphicsPipeline(sceneTransparentPipelineDescription);
        var directionalShadowPipeline = factory.CreateGraphicsPipeline(directionalShadowPipelineDescription);
        var presentPipeline = factory.CreateGraphicsPipeline(presentPipelineDescription);
        var hudPipeline = factory.CreateGraphicsPipeline(hudPipelineDescription);
        foreach (var shader in sceneShaders.Concat([directionalShadowShaders]).Concat(presentShaders).Concat(hudShaders))
        {
            shader.Dispose();
        }
        var shaderPipelineCache = new RekallAgeVeldridShaderPipelineCache(
            projectRoot,
            factory,
            sceneVertexLayout,
            [frameLayout, drawLayout, materialLayout],
            initialSceneTarget.Framebuffer.OutputDescription,
            device.WaitForIdle,
            PlayerLog.Write);

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
        var fogUniformBuffer = factory.CreateBuffer(new BufferDescription(
            checked((uint)Marshal.SizeOf<InteractiveFogUniform>()),
            BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        var directionalShadowFrameUniformBuffer = factory.CreateBuffer(new BufferDescription(
            checked((uint)Marshal.SizeOf<DirectionalShadowFrameUniform>()),
            BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        var drawUniformStrideBytes = AlignTo(
            checked((uint)Marshal.SizeOf<DrawUniform>()),
            Math.Max(1, device.UniformBufferMinOffsetAlignment));
        var drawUniformBuffer = factory.CreateBuffer(new BufferDescription(
            checked(drawUniformStrideBytes * 256),
            BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        var postProcessUniformBuffer = factory.CreateBuffer(new BufferDescription(
            checked((uint)Marshal.SizeOf<PostProcessUniform>()),
            BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        var directionalShadowFrameSet = factory.CreateResourceSet(new ResourceSetDescription(
            directionalShadowFrameLayout,
            directionalShadowFrameUniformBuffer));
        var drawSet = factory.CreateResourceSet(new ResourceSetDescription(drawLayout, drawUniformBuffer));
        var postProcessSet = factory.CreateResourceSet(new ResourceSetDescription(postProcessLayout, postProcessUniformBuffer));
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
        var initialAuthoredQuality = world.Subsystems.Rendering.QualityProfiles
            .OrderBy(profile => profile.EntityName, StringComparer.Ordinal)
            .ThenBy(profile => profile.EntityId, StringComparer.Ordinal)
            .Select(profile => profile.Intent)
            .FirstOrDefault();
        var initialQuality = new RekallAgeRenderQualityProfileResolver().Resolve(
            initialAuthoredQuality ?? new RekallAgeRenderQualityIntent(),
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("veldrid-vulkan"),
            baseFrame.Width,
            baseFrame.Height);
        var textures = CreateTextureBindings(
            device,
            factory,
            hudTextureLayout,
            assets,
            checked((uint)initialQuality.Textures.MaximumAnisotropy));
        var environmentTexture = !string.IsNullOrWhiteSpace(baseFrame.Environment?.SkyAssetId)
            && textures.TryGetValue(baseFrame.Environment.SkyAssetId, out var authoredEnvironment)
                ? authoredEnvironment
                : whiteTexture;
        var frameSet = factory.CreateResourceSet(new ResourceSetDescription(
            frameLayout,
            frameUniformBuffer,
            directionalShadowTarget.View,
            directionalShadowTarget.Sampler,
            fogUniformBuffer,
            environmentTexture.Texture,
            environmentTexture.Sampler));
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
            playableMode,
            playableGame,
            scene,
            window,
            device,
            commands,
            scenePipeline,
            sceneTransparentPipeline,
            directionalShadowPipeline,
            shaderPipelineCache,
            presentPipeline,
            hudPipeline,
            frameLayout,
            directionalShadowFrameLayout,
            drawLayout,
            materialLayout,
            presentTextureLayout,
            postProcessLayout,
            hudTextureLayout,
            frameSet,
            directionalShadowFrameSet,
            drawSet,
            postProcessSet,
            vertexBuffer,
            indexBuffer,
            hudVertexBuffer,
            frameUniformBuffer,
            fogUniformBuffer,
            directionalShadowFrameUniformBuffer,
            drawUniformBuffer,
            postProcessUniformBuffer,
            world,
            runtimeLoop,
            assets,
            entityCount,
            textures,
            whiteTexture,
            flatNormalTexture,
            defaultMetallicRoughnessTexture,
            environmentTexture,
            hudTexture,
            directionalShadowTarget,
            openXrStatus,
            openXrVulkanInterop,
            openXrCompositorSession,
            simulateXrInput,
            sceneSupersampleFactor,
            openXrEyeWidth,
            openXrEyeHeight,
            debugHudEnabled);
        player.StartLiveEditServer();
        player.StartAssetHotReloadWatcher();
        player.StartShaderHotReloadWatcher();
        var windowedVrPlan = RekallAgeWindowedPlayableVrSessionPlanner.Plan(
            openXrRequested,
            openXrStatus?.HeadsetSessionReady == true,
            playableMode);
        if (openXrRequested)
        {
            PlayerLog.Write(windowedVrPlan.Reason);
        }

        if (windowedVrPlan.ShouldStartHeadsetSubmit)
        {
            player.StartOpenXrHeadsetSubmit();
        }

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

    private void StartShaderHotReloadWatcher()
    {
        var shadersRoot = Path.Combine(_projectRoot, "Shaders");
        Directory.CreateDirectory(shadersRoot);
        _shaderWatcher = new FileSystemWatcher(shadersRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.LastWrite
                | NotifyFilters.Size
                | NotifyFilters.CreationTime
        };
        _shaderWatcher.Changed += (_, _) => MarkShaderHotReloadPending();
        _shaderWatcher.Created += (_, _) => MarkShaderHotReloadPending();
        _shaderWatcher.Deleted += (_, _) => MarkShaderHotReloadPending();
        _shaderWatcher.Renamed += (_, _) => MarkShaderHotReloadPending();
        _shaderWatcher.EnableRaisingEvents = true;
        PlayerLog.Write($"Shader hot reload watching {shadersRoot}.");
    }

    public int Run(int frameLimit = 0, Action<int>? beforeFrame = null)
    {
        var renderedFrames = 0;
        try
        {
            while (_window.Exists && (frameLimit <= 0 || renderedFrames < frameLimit))
            {
                CaptureInput(_window.PumpEvents());
                if (!_window.Exists)
                {
                    break;
                }

                beforeFrame?.Invoke(renderedFrames + 1);
                RenderFrame();
                renderedFrames++;

                if (_screenshotPath is not null && renderedFrames >= _screenshotFrame)
                {
                    CaptureScreenshot(_screenshotPath);
                    _screenshotPath = null;
                    if (frameLimit <= 0)
                    {
                        break;
                    }
                }
            }

            _device.WaitForIdle();
            return renderedFrames;
        }
        catch (Exception exception) when (exception is not RekallAgePlayerSessionRunException)
        {
            throw new RekallAgePlayerSessionRunException(renderedFrames, exception);
        }
    }

    /// <summary>
    /// Writes the presented frame to a PNG. The interactive player and the Vulkan capture
    /// command render through two separate implementations, and without a way to see what the
    /// player actually produced the only available check was "it starts and holds a frame
    /// rate" - which is how an HDR bloom regression reached the screen while the capture path
    /// still looked correct. This makes the two paths comparable.
    /// </summary>
    private void CaptureScreenshot(string path)
    {
        _device.WaitForIdle();
        var width = (uint)Math.Max(1, _window.Width);
        var height = (uint)Math.Max(1, _window.Height);
        Texture? color = null;
        Texture? depth = null;
        Framebuffer? framebuffer = null;
        Texture? staging = null;
        try
        {
            // Re-run the present pass into an offscreen LDR target rather than reading the
            // swapchain, so the screenshot is the finished presented image - bloom, tone
            // mapping and all - not the raw HDR scene buffer.
            // The present pipeline was created against the swapchain's output description, so
            // this target has to match it - same colour format, and a depth attachment if the
            // swapchain has one - or Veldrid rejects the pipeline/framebuffer pairing.
            var swapchain = _device.SwapchainFramebuffer;
            var colorFormat = swapchain.ColorTargets[0].Target.Format;
            color = _factory.CreateTexture(TextureDescription.Texture2D(
                width, height, mipLevels: 1, arrayLayers: 1,
                colorFormat,
                TextureUsage.RenderTarget | TextureUsage.Sampled));
            if (swapchain.DepthTarget is { } swapchainDepth)
            {
                depth = _factory.CreateTexture(TextureDescription.Texture2D(
                    width, height, mipLevels: 1, arrayLayers: 1,
                    swapchainDepth.Target.Format,
                    TextureUsage.DepthStencil));
            }

            framebuffer = _factory.CreateFramebuffer(new FramebufferDescription(depth, color));
            staging = _factory.CreateTexture(TextureDescription.Texture2D(
                width, height, mipLevels: 1, arrayLayers: 1,
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
            // Replay the overlay draws so the authored UI appears in the screenshot.
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
                var pixels = new byte[width * height * 4];
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    // The PNG writer expects RGBA; swapchains are commonly BGRA.
                    pixels[i + 0] = bgra ? map[i + 2] : map[i + 0];
                    pixels[i + 1] = map[i + 1];
                    pixels[i + 2] = bgra ? map[i + 0] : map[i + 2];
                    pixels[i + 3] = 255;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                RekallAgePngWriter
                    .WriteRgbaAsync(path, (int)width, (int)height, pixels, CancellationToken.None)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
                PlayerLog.Write($"Wrote player screenshot {path} ({width}x{height}).");
            }
            finally
            {
                _device.Unmap(staging);
            }
        }
        catch (Exception exception)
        {
            PlayerLog.Write($"Player screenshot failed: {exception.Message}");
        }
        finally
        {
            staging?.Dispose();
            framebuffer?.Dispose();
            depth?.Dispose();
            color?.Dispose();
        }
    }

    private void StartOpenXrHeadsetSubmit()
    {
        if (_openXrSubmitTask is not null)
        {
            return;
        }

        _openXrSubmitCts = new CancellationTokenSource();
        var cancellationToken = _openXrSubmitCts.Token;
        PlayerLog.Write($"Starting OpenXR headset scene submit from the windowed player input stream at {_openXrEyeWidth}x{_openXrEyeHeight} per eye.");
        _openXrSubmitTask = Task.Run(() =>
        {
            try
            {
                var result = new RekallAgeSilkOpenXrHeadsetClearSubmitter().SubmitSoftwareScene(
                    new RekallAgeOpenXrHeadsetSoftwareSceneSubmitRequest(
                        _projectRoot,
                        _sceneName,
                        FrameCount: RekallAgeOpenXrHeadsetSubmitPlanner.ContinuousSceneFrameCount,
                        RenderWidth: _openXrEyeWidth,
                        RenderHeight: _openXrEyeHeight),
                    cancellationToken,
                    ConsumePublishedRuntimeInputForOpenXr);
                PlayerLog.Write(
                    $"OpenXR headset scene submit ended submitted={result.Submitted} frames={result.SubmittedFrames} backend={result.RenderingBackend} errors={string.Join(" | ", result.Errors)}");
            }
            catch (OperationCanceledException)
            {
                PlayerLog.Write("OpenXR headset scene submit cancelled.");
            }
            catch (Exception ex)
            {
                PlayerLog.Write($"OpenXR headset scene submit failed: {ex.Message}");
            }
        });
    }

    private void CaptureInput(InputSnapshot snapshot)
    {
        _previousMousePosition = _lastMousePosition;
        _lastMousePosition = snapshot.MousePosition;
        if (!_hasMousePosition)
        {
            _previousMousePosition = _lastMousePosition;
            _hasMousePosition = true;
        }

        var mouseDelta = _mouseCaptured
            ? _window.MouseDelta
            : _lastMousePosition - _previousMousePosition;
        _inputBridge.RecordMouseDelta(mouseDelta.X, mouseDelta.Y);

        if (!_window.Focused && _mouseCaptured)
        {
            SetMouseCapture(false);
        }

        foreach (var keyEvent in snapshot.KeyEvents)
        {
            var key = keyEvent.Key.ToString();
            if (keyEvent.Down && key.Equals("Escape", StringComparison.OrdinalIgnoreCase))
            {
                SetMouseCapture(false);
            }

            _inputBridge.RecordKey(key, keyEvent.Down);
        }

        // Poll button state rather than reading snapshot.MouseEvents. That event list is empty
        // in this window configuration, so nothing was ever recorded and mouse buttons never
        // reached the runtime at all - the pointer moved, but no click, selection or UI press
        // could ever fire. Polling the held state and deriving the edges here does not depend
        // on the event list being populated.
        foreach (var button in PolledMouseButtons)
        {
            var down = snapshot.IsMouseDown(button);
            var name = button.ToString();
            if (down == _mouseButtonDown.GetValueOrDefault(name))
            {
                continue;
            }

            _mouseButtonDown[name] = down;

            // Re-capturing on click is only correct for scenes that steer with mouse motion.
            // For a click-to-select scene it would swallow the pointer on the player's very
            // first interaction, which is the opposite of what the click was for.
            if (down && !_mouseCaptured && SceneBindsMouseLook(_runtimeWorld))
            {
                SetMouseCapture(true);
            }

            _inputBridge.RecordMouseButton(name, down);
        }

        if (Math.Abs(snapshot.WheelDelta) <= 0.000001f)
        {
            return;
        }

        _inputBridge.RecordMouseWheel(snapshot.WheelDelta);
        _cachedStaticGeometry = null;
    }

    private static string BuildWindowTitle(
        string sceneName,
        bool openXrRequested,
        bool simulateXrInput,
        bool playableMode)
    {
        var suffixes = new List<string> { playableMode ? "Legacy proof adapter" : "Runtime scene" };
        if (openXrRequested)
        {
            suffixes.Add("OpenXR window+headset");
        }

        if (simulateXrInput)
        {
            suffixes.Add("XR sim");
        }

        return $"Rekall AGE Player - {sceneName} | {string.Join(" | ", suffixes)}";
    }

    private void SetMouseCapture(bool captured)
    {
        if (_mouseCaptured == captured)
        {
            return;
        }

        var window = new SDL_Window(_window.SdlWindowHandle);
        Sdl2Native.SDL_SetWindowGrab(window, captured);
        Sdl2Native.SDL_CaptureMouse(captured);
        Sdl2Native.SDL_SetRelativeMouseMode(captured);
        _window.CursorVisible = !captured;
        _mouseCaptured = captured;
        _inputBridge.ResetPendingMouseDelta();
        _previousMousePosition = _lastMousePosition;
        PlayerLog.Write(captured
            ? "Runtime mouse capture enabled; press Escape to release."
            : "Runtime mouse capture released; click the window to recapture.");
    }

    public async ValueTask DisposeAsync()
    {
        await StopOpenXrHeadsetSubmitAsync().ConfigureAwait(false);
        void Cleanup(string target, Action action)
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                action();
            }
            catch (Exception exception)
            {
                PlayerLog.Write($"Player cleanup issue target={target}: {exception.Message}");
            }
            finally
            {
                var elapsed = Stopwatch.GetElapsedTime(started);
                if (elapsed >= TimeSpan.FromMilliseconds(100))
                {
                    PlayerLog.Write($"Player cleanup slow target={target} elapsedMs={elapsed.TotalMilliseconds:F0}.");
                }
            }
        }

        Cleanup("graphics.wait-idle", _device.WaitForIdle);
        if (_mouseCaptured)
        {
            Cleanup("mouse-capture", () => SetMouseCapture(false));
        }

        Cleanup("asset-watcher", () => _assetWatcher?.Dispose());
        Cleanup("shader-watcher", () => _shaderWatcher?.Dispose());
        Cleanup("audio-output", () => _audioOutput?.Dispose());
        Cleanup("playable-game", () => _playableGame?.Dispose());
        Cleanup("runtime-loop", _runtimeLoop.Dispose);
        try
        {
            var liveServerStarted = Stopwatch.GetTimestamp();
            await _liveServer.DisposeAsync();
            var liveServerElapsed = Stopwatch.GetElapsedTime(liveServerStarted);
            if (liveServerElapsed >= TimeSpan.FromMilliseconds(100))
            {
                PlayerLog.Write($"Player cleanup slow target=live-server elapsedMs={liveServerElapsed.TotalMilliseconds:F0}.");
            }
        }
        catch (Exception exception)
        {
            PlayerLog.Write($"Player cleanup issue target=live-server: {exception.Message}");
        }

        Cleanup("runtime-gpu-workloads", _runtimeGpuWorkloadExecutor.Dispose);
        Cleanup("scene-target", _sceneTarget.Dispose);
        foreach (var materialSet in _materialSets.Values)
        {
            Cleanup("material-set", materialSet.Dispose);
        }

        Cleanup("hud-texture-set", _hudTextureSet.Dispose);
        Cleanup("ui-texture", _uiTexture.Dispose);
        Cleanup("frame-set", _frameSet.Dispose);
        Cleanup("directional-shadow-frame-set", _directionalShadowFrameSet.Dispose);
        Cleanup("draw-set", _drawSet.Dispose);
        Cleanup("post-process-set", _postProcessSet.Dispose);
        Cleanup("vertex-buffer", _vertexBuffer.Dispose);
        Cleanup("index-buffer", _indexBuffer.Dispose);
        Cleanup("hud-vertex-buffer", _hudVertexBuffer.Dispose);
        Cleanup("frame-uniform-buffer", _frameUniformBuffer.Dispose);
        Cleanup("fog-uniform-buffer", _fogUniformBuffer.Dispose);
        Cleanup("directional-shadow-frame-uniform-buffer", _directionalShadowFrameUniformBuffer.Dispose);
        Cleanup("draw-uniform-buffer", _drawUniformBuffer.Dispose);
        Cleanup("post-process-uniform-buffer", _postProcessUniformBuffer.Dispose);
        foreach (var texture in _textures.Values)
        {
            Cleanup("project-texture", texture.Dispose);
        }

        Cleanup("white-texture", _whiteTexture.Dispose);
        Cleanup("flat-normal-texture", _flatNormalTexture.Dispose);
        Cleanup("metallic-roughness-texture", _defaultMetallicRoughnessTexture.Dispose);
        Cleanup("hud-texture", _hudTexture.Dispose);
        Cleanup("project-shader-pipelines", _shaderPipelineCache.Dispose);
        Cleanup("scene-pipeline", _scenePipeline.Dispose);
        Cleanup("scene-transparent-pipeline", _sceneTransparentPipeline.Dispose);
        Cleanup("directional-shadow-pipeline", _directionalShadowPipeline.Dispose);
        Cleanup("present-pipeline", _presentPipeline.Dispose);
        Cleanup("present-pass-adapter", _presentPassAdapter.Dispose);
        Cleanup("hud-pipeline", _hudPipeline.Dispose);
        Cleanup("frame-layout", _frameLayout.Dispose);
        Cleanup("directional-shadow-frame-layout", _directionalShadowFrameLayout.Dispose);
        Cleanup("draw-layout", _drawLayout.Dispose);
        Cleanup("material-layout", _materialLayout.Dispose);
        Cleanup("present-texture-layout", _presentTextureLayout.Dispose);
        Cleanup("post-process-layout", _postProcessLayout.Dispose);
        Cleanup("hud-texture-layout", _hudTextureLayout.Dispose);
        Cleanup("directional-shadow-target", _directionalShadowTarget.Dispose);
        Cleanup("command-list", _commands.Dispose);
        Cleanup("graphics-device", _device.Dispose);
        Cleanup("window", () =>
        {
            _window.Close();
            if (!SpinWait.SpinUntil(() => !_window.Exists, TimeSpan.FromSeconds(1)))
            {
                throw new TimeoutException("SDL window owner did not close within one second.");
            }
        });
        Cleanup("controller-input", _controllerInput.Dispose);
    }

    private static void EnsureSdlVideoInitialized()
    {
        lock (SdlInitializationGate)
        {
            if (_sdlVideoInitialized)
            {
                return;
            }

            if (Sdl2Native.SDL_Init(SDLInitFlags.Video) != 0)
            {
                throw new InvalidOperationException("SDL video initialization failed.");
            }

            _sdlVideoInitialized = true;
        }
    }

    private async ValueTask StopOpenXrHeadsetSubmitAsync()
    {
        var cts = _openXrSubmitCts;
        var task = _openXrSubmitTask;
        if (cts is null || task is null)
        {
            return;
        }

        cts.Cancel();
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            PlayerLog.Write("OpenXR headset scene submit did not stop within the shutdown grace period.");
        }
        catch (OperationCanceledException)
        {
            PlayerLog.Write("OpenXR headset scene submit stopped.");
        }
        finally
        {
            cts.Dispose();
            _openXrSubmitCts = null;
            _openXrSubmitTask = null;
        }
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
        _runtimeGpuWorkloadExecutor.InvalidateFrameResources();
        _sceneTarget.Dispose();
        _sceneTarget = CreateSceneRenderTarget(_factory, displayWidth, displayHeight, _sceneSupersampleFactor, _presentTextureLayout);
        _cachedStaticGeometry = null;
        PlayerLog.Write($"Recreated supersampled scene target {_sceneTarget.Width}x{_sceneTarget.Height} for window {displayWidth}x{displayHeight}.");
    }

    private void EnsurePlayableRenderTarget()
    {
        if (_sceneTarget.Width == PlayableWidth && _sceneTarget.Height == PlayableHeight)
        {
            return;
        }

        _device.WaitForIdle();
        _runtimeGpuWorkloadExecutor.InvalidateFrameResources();
        _sceneTarget.Dispose();
        _sceneTarget = CreateSceneRenderTarget(
            _factory,
            PlayableWidth / _sceneSupersampleFactor,
            PlayableHeight / _sceneSupersampleFactor,
            _sceneSupersampleFactor,
            _presentTextureLayout);
        _cachedStaticGeometry = null;
        PlayerLog.Write($"Recreated playable frame target {_sceneTarget.Width}x{_sceneTarget.Height}.");
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
        var initialWorld = new RekallAgeRuntimeWorldBuilder().Build(scene, _projectRoot);
        var runResult = _runtimeLoop.RunAsync(initialWorld, 1, CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        _sceneDocument = scene;
        _runtimeWorld = runResult.World;
        LoadPersistentState();
        _entityCount = _runtimeWorld.Entities.Count;
        _sceneRevision++;
        _simulationClock.Reset(_clock.Elapsed);
        _cachedStaticGeometry = null;
        _hudDirty = true;
        _uiOverlaySignature = null;
    }

    private JsonObject ReloadAssetsForCurrentWorld(string message)
    {
        var frame = _frameBuilder.Build(
            _runtimeWorld,
            Math.Max(1, _window.Width),
            Math.Max(1, _window.Height),
            debugOverlay: _debugHudEnabled);
        var authoredQuality = _runtimeWorld.Subsystems.Rendering.QualityProfiles
            .OrderBy(profile => profile.EntityName, StringComparer.Ordinal)
            .ThenBy(profile => profile.EntityId, StringComparer.Ordinal)
            .Select(profile => profile.Intent)
            .FirstOrDefault();
        frame = _interactiveQualityResolver.Resolve(
            frame,
            authoredQuality,
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline($"veldrid-{_device.BackendType.ToString().ToLowerInvariant()}"));
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
        _uiOverlaySignature = null;
        PlayerLog.Write($"Live assets reloaded images={assets.Images.Count} textures={assets.Textures.Count} models={assets.Models.Count} issues={assets.Issues.Count}.");
        return CreateLiveStatus("reload_assets", true, message);
    }

    private JsonObject CreateLiveStatus(string operation, bool applied, string message)
    {
        var frame = _frameBuilder.Build(
            _runtimeWorld,
            Math.Max(1, _window.Width),
            Math.Max(1, _window.Height),
            debugOverlay: _debugHudEnabled);
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
        if (_playableMode)
        {
            RenderPlayableFrame();
            return;
        }

        var profileStart = Stopwatch.GetTimestamp();
        ProcessLiveEditQueue();
        ProcessAssetHotReload();
        ProcessShaderHotReload();
        var frameNumber = Interlocked.Increment(ref _frameIndex);
        AdvanceSimulationToWallClock();
        var profileAfterSimulation = Stopwatch.GetTimestamp();
        var frame = _frameBuilder.Build(
            _runtimeWorld,
            Math.Max(1, _window.Width),
            Math.Max(1, _window.Height),
            debugOverlay: _debugHudEnabled);
        var authoredQuality = _runtimeWorld.Subsystems.Rendering.QualityProfiles
            .OrderBy(profile => profile.EntityName, StringComparer.Ordinal)
            .ThenBy(profile => profile.EntityId, StringComparer.Ordinal)
            .Select(profile => profile.Intent)
            .FirstOrDefault();
        frame = _interactiveQualityResolver.Resolve(
            frame,
            authoredQuality,
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline($"veldrid-{_device.BackendType.ToString().ToLowerInvariant()}"));
        var profileAfterFrameBuild = Stopwatch.GetTimestamp();
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
            PlayerLog.Write(
                $"Interactive high fidelity quality={sceneFrame.ResolvedQualityPlan?.ResolvedPreset ?? "none"} " +
                $"post={sceneFrame.PostProcessStack?.Enabled == true} plan={highFidelityPlan is not null} " +
                $"shadows={highFidelityPlan?.ShadowPlan.Enabled == true} cascades={highFidelityPlan?.ShadowPlan.Cascades.Count ?? 0} " +
                $"fog={interactiveFog.Enabled} fogMode={interactiveFog.RequestedMode}->{interactiveFog.ExecutedMode} fogVolumes={interactiveFog.Volumes.Count} " +
                $"ao={ambientOcclusion.Enabled} aoSamples={ambientOcclusion.SampleCount} " +
                $"diagnostics={shadowDiagnostics}.");
            foreach (var diagnostic in interactiveFog.Diagnostics)
            {
                PlayerLog.Write($"Interactive fog diagnostic {diagnostic.Code}: {diagnostic.Message}");
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
            PlayerLog.Write(
                $"Interactive particles mode={interactiveParticles.ExecutionMode} " +
                $"emitters={interactiveParticles.EmitterCount} active={interactiveParticles.ActiveParticleCount}.");
        }
        _device.UpdateBuffer(_fogUniformBuffer, 0, BuildInteractiveFogUniform(interactiveFog));
        var profileAfterPacket = Stopwatch.GetTimestamp();

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
                        // ShadowFactors.y/.z are spare slots (only .x, ReceiveShadows, was used) -
                        // repurposed here for AlphaCutoff and the "is this a mask-mode draw" flag,
                        // rather than growing this shared uniform struct: appending a whole new
                        // field would still be safe (every custom shader already reads a shorter
                        // prefix of this same buffer than the live player's own C# struct declares,
                        // e.g. none of them know about ShadowFactors either), but reusing already-
                        // spare bytes is simpler when they're sitting right there unused.
                        new Vector4(
                            draw.ReceiveShadows ? 1 : 0,
                            draw.AlphaCutoff,
                            draw.AlphaMode.Equals("mask", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
                            0)));
            }
        }

        UpdateTitle(frameNumber, _clock.Elapsed.TotalSeconds, packet.Vertices.Length);
        var uiVertices = BuildFullScreenOverlayVertices(frame.Renderables.Any(renderable => renderable.UiVisual is not null));
        var hudVertices = _debugHudEnabled
            ? BuildHudVertices(frame.Width, frame.Height)
            : [];
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

        // Remembered so a screenshot can replay the same overlay draws. A screenshot that
        // omits the authored UI is not a picture of the game.
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
        var profileAfterUi = Stopwatch.GetTimestamp();

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

        // Downsample the finished scene so the present pass can read a smooth, wide bloom
        // from the coarse levels instead of gathering a wide radius from mip 0.
        _commands.GenerateMipmaps(_sceneTarget.Color);
        _presentPassAdapter.Record(
            _commands,
            _device.SwapchainFramebuffer,
            _presentPipeline,
            _sceneTarget.ResourceSet,
            _postProcessSet,
            _window.Width,
            _window.Height,
            new RgbaFloat(0.08f, 0.10f, 0.14f, 1f));
        RecordRuntimeGpuWorkloads();

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
        var profileAfterSubmit = Stopwatch.GetTimestamp();
        RecordFrameProfile(
            profileStart,
            profileAfterSimulation,
            profileAfterFrameBuild,
            profileAfterPacket,
            profileAfterUi,
            profileAfterSubmit);
    }

    private void RecordFrameProfile(
        long start,
        long afterSimulation,
        long afterFrameBuild,
        long afterPacket,
        long afterUi,
        long afterSubmit)
    {
        if (!_debugHudEnabled)
        {
            return;
        }

        _profileFrameCount++;
        _profileSimulationMs += Stopwatch.GetElapsedTime(start, afterSimulation).TotalMilliseconds;
        _profileFrameBuildMs += Stopwatch.GetElapsedTime(afterSimulation, afterFrameBuild).TotalMilliseconds;
        _profilePacketMs += Stopwatch.GetElapsedTime(afterFrameBuild, afterPacket).TotalMilliseconds;
        _profileUiMs += Stopwatch.GetElapsedTime(afterPacket, afterUi).TotalMilliseconds;
        _profileSubmitMs += Stopwatch.GetElapsedTime(afterUi, afterSubmit).TotalMilliseconds;
        if (_profileFrameCount < 120)
        {
            return;
        }

        PlayerLog.Write(
            $"Frame profile avgMs simulation={_profileSimulationMs / _profileFrameCount:F2} " +
            $"frameBuild={_profileFrameBuildMs / _profileFrameCount:F2} " +
            $"packet={_profilePacketMs / _profileFrameCount:F2} " +
            $"ui={_profileUiMs / _profileFrameCount:F2} " +
            $"submit={_profileSubmitMs / _profileFrameCount:F2} " +
            $"geometryCache={_profileGeometryCacheHits}h/{_profileGeometryCacheMisses}m.");
        _profileFrameCount = 0;
        _profileSimulationMs = 0;
        _profileFrameBuildMs = 0;
        _profilePacketMs = 0;
        _profileUiMs = 0;
        _profileSubmitMs = 0;
        _profileGeometryCacheHits = 0;
        _profileGeometryCacheMisses = 0;
    }

    private void RenderPlayableFrame()
    {
        var game = _playableGame
            ?? throw new InvalidOperationException("Playable mode requires a loaded playable module.");
        var frameNumber = Interlocked.Increment(ref _frameIndex);
        var now = _clock.Elapsed.TotalSeconds;
        var deltaSeconds = _lastPlayableTickSeconds <= 0
            ? 1.0 / 60.0
            : Math.Clamp(now - _lastPlayableTickSeconds, 0, 1.0 / 15.0);
        _lastPlayableTickSeconds = now;
        var playableInput = BuildPlayableInput(deltaSeconds);
        AdvanceSimulationToWallClock();
        game.Tick(playableInput.ToRuntimeInputFrame());
        var renderFrame = game.RenderFrame(frameNumber);
        EnsurePlayableRenderTarget();
        var raster = _playableRasterizer.Rasterize(renderFrame, _sceneTarget.Width, _sceneTarget.Height);
        _device.UpdateTexture(
            _sceneTarget.Color,
            raster.Pixels,
            0,
            0,
            0,
            (uint)_sceneTarget.Width,
            (uint)_sceneTarget.Height,
            1,
            0,
            0);
        UpdateTitle(frameNumber, _clock.Elapsed.TotalSeconds, raster.NonBackgroundPixels);
        var uiFrame = _frameBuilder.Build(
            _runtimeWorld,
            Math.Max(1, _window.Width),
            Math.Max(1, _window.Height),
            debugOverlay: false);
        var uiVertices = BuildFullScreenOverlayVertices(
            uiFrame.Renderables.Any(renderable => renderable.UiVisual is not null));
        if (uiVertices.Length > 0)
        {
            UpdateUiTexture(uiFrame);
            EnsureHudVertexBufferCapacity(uiVertices);
            _device.UpdateBuffer(_hudVertexBuffer, 0, uiVertices);
        }

        _commands.Begin();
        _device.UpdateBuffer(_postProcessUniformBuffer, 0, PostProcessUniform.Default);
        _presentPassAdapter.Record(
            _commands,
            _device.SwapchainFramebuffer,
            _presentPipeline,
            _sceneTarget.ResourceSet,
            _postProcessSet,
            _window.Width,
            _window.Height,
            new RgbaFloat(0.02f, 0.04f, 0.08f, 1f));
        RecordRuntimeGpuWorkloads();
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
    }

    private void RecordRuntimeGpuWorkloads()
    {
        var report = _runtimeGpuWorkloadExecutor.Record(
            _runtimeWorld.Subsystems.Rendering.GpuWorkloads,
            _sceneTarget.Color,
            _device.SwapchainFramebuffer);
        var status = report.Diagnostics.Count == 0
            ? $"Runtime GPU workloads enabled={report.EnabledWorkloads} executed={report.ExecutedWorkloads}."
            : $"Runtime GPU workloads enabled={report.EnabledWorkloads} executed={report.ExecutedWorkloads} diagnostics={string.Join(" | ", report.Diagnostics.Select(item => $"{item.Code}: {item.Message}"))}";
        if (status.Equals(_lastRuntimeGpuWorkloadStatus, StringComparison.Ordinal)) return;
        _lastRuntimeGpuWorkloadStatus = status;
        PlayerLog.Write(status);
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

            var drawUniformOffset = checked(_drawUniformStrideBytes * (uint)i);
            _drawUniformDynamicOffsets[0] = drawUniformOffset;
            _commands.SetGraphicsResourceSet(1, _drawSet, _drawUniformDynamicOffsets);
            _commands.SetGraphicsResourceSet(2, ResolveMaterialSet(draw));
            _commands.DrawIndexed(draw.IndexCount, 1, draw.FirstIndex, draw.VertexOffset, 0);
        }
    }

    private void AdvanceSimulationToWallClock()
    {
        RekallAgeRuntimeInputState? capturedInput = null;
        var result = _simulationClock.AdvanceToAsync(
                _runtimeWorld,
                _clock.Elapsed,
                CancellationToken.None,
                step => RekallAgeRuntimeInputPersistence.ForSimulationStep(
                    capturedInput ??= ConsumeRuntimeInput(),
                    step))
            .AsTask()
            .GetAwaiter()
            .GetResult();
        _runtimeWorld = result.World;
        _audioOutput?.Submit(result.AudioFrames);
        PersistChangedState();
        HonourSceneTransitionRequest();
        if (!_audioSubmissionLogged && _audioOutput is { SubmittedFrameCount: > 0 } audioOutput)
        {
            _audioSubmissionLogged = true;
            PlayerLog.Write($"Audio output queued runtime mix frames={audioOutput.SubmittedFrameCount} bytes={audioOutput.QueuedBytes}.");
        }
    }

    /// <summary>
    /// Loads every Rekall.PersistentState slot the scene declares into its Document.
    ///
    /// Called once when a scene becomes active, so an authored module sees its saved settings
    /// and campaign progress as ordinary component state rather than needing file access.
    /// </summary>
    private void LoadPersistentState()
    {
        _persistedStateBySlot.Clear();
        _stateSlotsBlockedFromWriting.Clear();
        var entities = new List<RekallAgeRuntimeEntity>(_runtimeWorld.Entities.Count);
        var changed = false;
        foreach (var entity in _runtimeWorld.Entities)
        {
            var component = entity.Components.FirstOrDefault(item =>
                item.Type.Equals(PersistentStateComponentType, StringComparison.Ordinal));
            var slot = component?.Properties["slot"]?.GetValue<string>();
            if (component is null || string.IsNullOrWhiteSpace(slot))
            {
                entities.Add(entity);
                continue;
            }

            try
            {
                var document = RekallAgePersistentStateStore
                    .ReadAsync(_projectRoot, slot, CancellationToken.None)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
                if (document is null)
                {
                    // No stored document yet: this is a first run, and the authored defaults
                    // are the right thing to save once something changes them.
                    entities.Add(entity);
                    continue;
                }

                var properties = component.Properties.DeepClone().AsObject();
                properties["document"] = document.DeepClone();
                _persistedStateBySlot[slot] = document.ToJsonString();
                entities.Add(entity with
                {
                    Components = entity.Components
                        .Select(item => ReferenceEquals(item, component)
                            ? item with { Properties = properties }
                            : item)
                        .ToArray()
                });
                changed = true;
                PlayerLog.Write($"Loaded persistent state slot '{slot}'.");
            }
            catch (Exception exception)
            {
                // Unreadable saved state must not stop the game starting: keep the authored
                // defaults and say so.
                _stateSlotsBlockedFromWriting.Add(slot);
                PlayerLog.Write(
                    $"Persistent state slot '{slot}' could not be loaded: {exception.Message} "
                    + "Saving to this slot is disabled for this session so the stored document is not overwritten.");
                entities.Add(entity);
            }
        }

        if (changed)
        {
            _runtimeWorld = _runtimeWorld with { Entities = entities };
        }
    }

    /// <summary>
    /// Writes back any Rekall.PersistentState document a module has changed this step.
    ///
    /// Compared against the last value written rather than saved unconditionally, so a scene
    /// that never touches its settings never touches the disk.
    /// </summary>
    private void PersistChangedState()
    {
        foreach (var entity in _runtimeWorld.Entities)
        {
            var component = entity.Components.FirstOrDefault(item =>
                item.Type.Equals(PersistentStateComponentType, StringComparison.Ordinal));
            var slot = component?.Properties["slot"]?.GetValue<string>();
            if (component is null
                || string.IsNullOrWhiteSpace(slot)
                || _stateSlotsBlockedFromWriting.Contains(slot)
                || component.Properties["document"] is not JsonObject document)
            {
                continue;
            }

            var serialized = document.ToJsonString();
            if (_persistedStateBySlot.TryGetValue(slot, out var previous)
                && string.Equals(previous, serialized, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                RekallAgePersistentStateStore
                    .WriteAsync(_projectRoot, slot, document, CancellationToken.None)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
                _persistedStateBySlot[slot] = serialized;
            }
            catch (Exception exception)
            {
                // Record the attempt so a failing write is not retried every step.
                _persistedStateBySlot[slot] = serialized;
                PlayerLog.Write($"Persistent state slot '{slot}' could not be saved: {exception.Message}");
            }
        }
    }

    /// <summary>
    /// Loads the scene an authored module has asked for through Rekall.SceneTransition.
    ///
    /// Scene changes were previously only reachable from outside the running player over the
    /// live-edit pipe, which meant a game could not move between its own menus, briefings and
    /// missions. Honouring the request here reuses the same ApplySceneDocument path a live
    /// reload already takes, so a game drives its own level flow through an ordinary component.
    ///
    /// The request needs no explicit acknowledgement: satisfying it replaces the world that
    /// carried it, so a scene cannot re-trigger its own transition.
    /// </summary>
    private void HonourSceneTransitionRequest()
    {
        string? requested = null;
        string? reason = null;
        foreach (var entity in _runtimeWorld.Entities)
        {
            var component = entity.Components.FirstOrDefault(item =>
                item.Type.Equals(SceneTransitionComponentType, StringComparison.Ordinal));
            if (component is null)
            {
                continue;
            }

            var value = component.Properties["requestedScene"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(value))
            {
                requested = value.Trim();
                reason = component.Properties["reason"]?.GetValue<string>();
                break;
            }
        }

        if (requested is null
            || requested.Equals(_sceneName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var scene = new RekallAgeSceneStore()
                .LoadAsync(_projectRoot, requested, CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            _sceneName = requested;
            ApplySceneDocument(scene);
            PlayerLog.Write($"Scene transition to '{requested}' honoured. reason={reason}");
        }
        catch (Exception exception)
        {
            // A bad scene name must not take the player down: report it and keep playing the
            // scene that is already loaded.
            PlayerLog.Write($"Scene transition to '{requested}' failed: {exception.Message}");
        }
    }

    private void MarkShaderHotReloadPending()
    {
        Interlocked.Exchange(ref _lastShaderHotReloadRequestTicks, Stopwatch.GetTimestamp());
        Interlocked.Exchange(ref _shaderHotReloadPending, 1);
    }

    private void ProcessShaderHotReload()
    {
        if (Volatile.Read(ref _shaderHotReloadPending) == 0)
        {
            return;
        }

        var elapsedSeconds = (Stopwatch.GetTimestamp() - Volatile.Read(ref _lastShaderHotReloadRequestTicks))
            / (double)Stopwatch.Frequency;
        if (elapsedSeconds < 0.5
            || Interlocked.Exchange(ref _shaderHotReloadPending, 0) == 0)
        {
            return;
        }

        _shaderPipelineCache.InvalidateAll();
        _cachedStaticGeometry = null;
        PlayerLog.Write("Project shader pipelines invalidated after debounced filesystem change.");
    }

    private RekallAgeRuntimeInputState ConsumeRuntimeInput()
    {
        var controllers = _controllerInput.Poll();
        var captured = _inputBridge.ConsumeRuntimeInput(
            _lastMousePosition.X,
            _lastMousePosition.Y,
            Math.Max(1, _window.Width),
            Math.Max(1, _window.Height),
            controllers);
        var inputFrame = _simulateXrInput
            ? RekallAgeXrInputSimulator.CreateFrame(captured, _clock.Elapsed)
            : captured;
        PublishRuntimeInput(inputFrame);
        return inputFrame;
    }

    private void PublishRuntimeInput(RekallAgeRuntimeInputState input)
    {
        lock (_runtimeInputGate)
        {
            _latestRuntimeInput = input;
            _runtimeInputSequence++;
        }
    }

    private RekallAgeRuntimeInputState ConsumePublishedRuntimeInputForOpenXr()
    {
        lock (_runtimeInputGate)
        {
            var input = _latestRuntimeInput;
            var sequence = _runtimeInputSequence;
            if (sequence == _openXrLastConsumedInputSequence)
            {
                return KeepHeldRuntimeInput(input);
            }

            _openXrLastConsumedInputSequence = sequence;
            return input;
        }
    }

    private static RekallAgeRuntimeInputState KeepHeldRuntimeInput(RekallAgeRuntimeInputState input)
    {
        return RekallAgeRuntimeInputPersistence.ForSimulationStep(input, 1);
    }

    private RekallAgePlaybackInput BuildPlayableInput(double deltaSeconds)
    {
        var verticalAxis = 0;
        if (IsPressed("W") || IsPressed("Up"))
        {
            verticalAxis -= 1;
        }

        if (IsPressed("S") || IsPressed("Down"))
        {
            verticalAxis += 1;
        }

        var primaryAction = IsPressedThisFrame("Space") ||
            IsPressedThisFrame("Enter") ||
            IsPressedThisFrame("Return");
        return new RekallAgePlaybackInput(Math.Clamp(verticalAxis, -1, 1), primaryAction, deltaSeconds);
    }

    private bool IsPressed(string key)
    {
        return _inputBridge.IsPressed(key);
    }

    private bool IsPressedThisFrame(string key)
    {
        return _inputBridge.IsPressedThisFrame(key);
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
        var baseLine = $"{_device.BackendType} {_sceneSupersampleFactor}xSSAA";
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
        PlayerLog.Write($"Interactive directional shadow atlas recreated resolution={resolution} cascades={RekallAgeInteractiveShadowFramePlanner.MaximumCascadeCount}.");
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

    private static Dictionary<string, TextureBinding> CreateTextureBindings(
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

    private static TextureBinding CreateRuntimeTextureBinding(
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

    private const string DirectionalShadowVertexShader = """
        #version 450

        layout(location = 0) in vec3 Position;

        layout(set = 0, binding = 0) uniform DirectionalShadowFrameUniformBuffer
        {
            mat4 ViewProjection;
        } ShadowFrame;

        layout(set = 1, binding = 0) uniform DrawUniformBuffer
        {
            mat4 Model;
        } Draw;

        void main()
        {
            gl_Position = ShadowFrame.ViewProjection * Draw.Model * vec4(Position, 1.0);
        }
        """;

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
            vec4 CameraPosition;
            vec4 AdditionalLightDirection;
            vec4 AdditionalLightColor;
            vec4 AdditionalLightPosition;
            vec4 AdditionalLightParameters;
            vec4 AdditionalLightColor2;
            vec4 AdditionalLightPosition2;
            vec4 AdditionalLightParameters2;
            vec4 AdditionalLightColor3;
            vec4 AdditionalLightPosition3;
            vec4 AdditionalLightParameters3;
            vec4 AdditionalLightColor4;
            vec4 AdditionalLightPosition4;
            vec4 AdditionalLightParameters4;
            vec4 SpotLightColor;
            vec4 SpotLightPosition;
            vec4 SpotLightDirection;
            vec4 SpotLightParameters;
            vec4 SpotLightColor2;
            vec4 SpotLightPosition2;
            vec4 SpotLightDirection2;
            vec4 SpotLightParameters2;
            vec4 SpotLightColor3;
            vec4 SpotLightPosition3;
            vec4 SpotLightDirection3;
            vec4 SpotLightParameters3;
            vec4 SpotLightColor4;
            vec4 SpotLightPosition4;
            vec4 SpotLightDirection4;
            vec4 SpotLightParameters4;
            mat4 ShadowViewProjection0;
            mat4 ShadowViewProjection1;
            mat4 ShadowViewProjection2;
            mat4 ShadowViewProjection3;
            vec4 ShadowSplitDepths;
            vec4 ShadowParameters;
            vec4 EnvironmentParameters;
            vec4 EnvironmentAmbientSkyColor;
            vec4 EnvironmentAmbientGroundColor;
        } Frame;

        layout(set = 1, binding = 0) uniform DrawUniformBuffer
        {
            mat4 Model;
            vec4 MaterialFactors;
            vec4 EmissiveFactors;
            vec4 AtmosphereFactors0;
            vec4 AtmosphereFactors1;
            vec4 AtmosphereColor0;
            vec4 AtmosphereColor1;
            vec4 AtmosphereColor2;
            vec4 CloudFactors;
            vec4 CloudColor;
            vec4 CloudShadowFactors;
            vec4 SurfaceWaterFactors;
            vec4 ShadowFactors;
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
            vec4 CameraPosition;
            vec4 AdditionalLightDirection;
            vec4 AdditionalLightColor;
            vec4 AdditionalLightPosition;
            vec4 AdditionalLightParameters;
            vec4 AdditionalLightColor2;
            vec4 AdditionalLightPosition2;
            vec4 AdditionalLightParameters2;
            vec4 AdditionalLightColor3;
            vec4 AdditionalLightPosition3;
            vec4 AdditionalLightParameters3;
            vec4 AdditionalLightColor4;
            vec4 AdditionalLightPosition4;
            vec4 AdditionalLightParameters4;
            vec4 SpotLightColor;
            vec4 SpotLightPosition;
            vec4 SpotLightDirection;
            vec4 SpotLightParameters;
            vec4 SpotLightColor2;
            vec4 SpotLightPosition2;
            vec4 SpotLightDirection2;
            vec4 SpotLightParameters2;
            vec4 SpotLightColor3;
            vec4 SpotLightPosition3;
            vec4 SpotLightDirection3;
            vec4 SpotLightParameters3;
            vec4 SpotLightColor4;
            vec4 SpotLightPosition4;
            vec4 SpotLightDirection4;
            vec4 SpotLightParameters4;
            mat4 ShadowViewProjection0;
            mat4 ShadowViewProjection1;
            mat4 ShadowViewProjection2;
            mat4 ShadowViewProjection3;
            vec4 ShadowSplitDepths;
            vec4 ShadowParameters;
            vec4 EnvironmentParameters;
            vec4 EnvironmentAmbientSkyColor;
            vec4 EnvironmentAmbientGroundColor;
        } Frame;
        
        layout(set = 1, binding = 0) uniform DrawUniformBuffer
        {
            mat4 Model;
            vec4 MaterialFactors;
            vec4 EmissiveFactors;
            vec4 AtmosphereFactors0;
            vec4 AtmosphereFactors1;
            vec4 AtmosphereColor0;
            vec4 AtmosphereColor1;
            vec4 AtmosphereColor2;
            vec4 CloudFactors;
            vec4 CloudColor;
            vec4 CloudShadowFactors;
            vec4 SurfaceWaterFactors;
            vec4 ShadowFactors;
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
        layout(set = 2, binding = 10) uniform texture2D CloudShadowTexture;
        layout(set = 2, binding = 11) uniform sampler CloudShadowSampler;
        layout(set = 2, binding = 12) uniform texture2D SurfaceWaterTexture;
        layout(set = 2, binding = 13) uniform sampler SurfaceWaterSampler;
        layout(set = 0, binding = 1) uniform texture2DArray DirectionalShadowAtlas;
        layout(set = 0, binding = 2) uniform sampler DirectionalShadowSampler;

        struct InteractiveFogVolume
        {
            vec4 PositionShape;
            vec4 HalfExtentsDensity;
            vec4 AlbedoAnisotropy;
            vec4 EmissionHeightFalloff;
            vec4 BlendPriority;
            mat4 WorldToLocal;
        };

        layout(set = 0, binding = 3) uniform InteractiveFogUniformBuffer
        {
            vec4 Settings;
            InteractiveFogVolume Volumes[8];
        } Fog;

        layout(set = 0, binding = 4) uniform texture2D EnvironmentTexture;
        layout(set = 0, binding = 5) uniform sampler EnvironmentSampler;
        
        layout(location = 0) out vec4 fsout_Color;const float PI = 3.14159265359;
        const int MAX_VIEW_SAMPLE_COUNT = 32;
        const int MAX_LIGHT_SAMPLE_COUNT = 16;

        vec2 directionToEquirectangularUv(vec3 direction)
        {
            vec3 d = normalize(direction);
            return vec2(atan(d.z, d.x) / (2.0 * PI) + 0.5, asin(clamp(d.y, -1.0, 1.0)) / PI + 0.5);
        }

        vec3 sampleEnvironmentRadiance(vec3 direction, float roughness)
        {
            float maximumLod = max(float(textureQueryLevels(sampler2D(EnvironmentTexture, EnvironmentSampler)) - 1), 0.0);
            vec3 encoded = textureLod(
                sampler2D(EnvironmentTexture, EnvironmentSampler),
                directionToEquirectangularUv(direction),
                clamp(roughness, 0.0, 1.0) * maximumLod).rgb;
            return pow(max(encoded, vec3(0.0)), vec3(2.2));
        }
        
        vec3 perturbNormal(vec3 normal)
        {
            vec3 tangentNormal = texture(sampler2D(NormalTexture, NormalSampler), fsin_UV).xyz * 2.0 - 1.0;
            tangentNormal.xy *= Draw.MaterialFactors.z;
            vec3 q1 = dFdx(fsin_WorldPosition);
            vec3 q2 = dFdy(fsin_WorldPosition);
            vec2 st1 = dFdx(fsin_UV);
            vec2 st2 = dFdy(fsin_UV);
            float determinant = st1.s * st2.t - st1.t * st2.s;
            if (abs(determinant) <= 0.0000001)
            {
                return normal;
            }
            vec3 tangentRaw = q1 * st2.t - q2 * st1.t;
            vec3 tangentProjected = tangentRaw - normal * dot(normal, tangentRaw);
            float tangentLengthSquared = dot(tangentProjected, tangentProjected);
            if (tangentLengthSquared <= 0.0000001)
            {
                return normal;
            }
            vec3 tangent = tangentProjected * inversesqrt(tangentLengthSquared);
            vec3 bitangent = normalize(cross(normal, tangent)) * sign(determinant);
            mat3 tbn = mat3(tangent, bitangent, normal);
            vec3 mapped = tbn * tangentNormal;
            float mappedLengthSquared = dot(mapped, mapped);
            return mappedLengthSquared <= 0.0000001 ? normal : mapped * inversesqrt(mappedLengthSquared);
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
        
        float phaseRayleigh(float cosTheta)
        {
            return 3.0 / (16.0 * PI) * (1.0 + cosTheta * cosTheta);
        }
        
        float phaseMie(float cosTheta, float anisotropy)
        {
            float g = clamp(anisotropy, -0.99, 0.99);
            float g2 = g * g;
            float denom = pow(max(1.0 + g2 - 2.0 * g * cosTheta, 0.0001), 1.5);
            return 3.0 / (8.0 * PI) * ((1.0 - g2) * (1.0 + cosTheta * cosTheta)) / max((2.0 + g2) * denom, 0.0001);
        }
        
        bool intersectSphere(vec3 origin, vec3 direction, vec3 center, float radius, out vec2 hit)
        {
            vec3 local = origin - center;
            float b = dot(local, direction);
            float c = dot(local, local) - radius * radius;
            float discriminant = b * b - c;
            if (discriminant < 0.0)
            {
                hit = vec2(0.0);
                return false;
            }
        
            float root = sqrt(discriminant);
            hit = vec2(-b - root, -b + root);
            return hit.y >= 0.0;
        }
        
        float atmosphereDensityAtPoint(vec3 point, vec3 center, float planetRadius, float atmosphereRadius, float density, float falloff)
        {
            float height = max(0.0, length(point - center) - planetRadius);
            float normalizedHeight = clamp(height / max(atmosphereRadius - planetRadius, 0.0001), 0.0, 1.0);
            return max(density, 0.0) * exp(-normalizedHeight / max(falloff, 0.001));
        }
        
        float integrateOpticalDepth(vec3 origin, vec3 direction, float rayLength, vec3 center, float planetRadius, float atmosphereRadius, float density, float falloff, int sampleCount)
        {
            float stepSize = rayLength / float(sampleCount);
            float opticalDepth = 0.0;
            for (int i = 0; i < MAX_LIGHT_SAMPLE_COUNT; i++)
            {
                if (i >= sampleCount)
                {
                    break;
                }
        
                float t = (float(i) + 0.5) * stepSize;
                vec3 samplePoint = origin + direction * t;
                opticalDepth += atmosphereDensityAtPoint(samplePoint, center, planetRadius, atmosphereRadius, density, falloff) * stepSize;
            }
        
            return opticalDepth;
        }
        
        bool hasAtmosphereData()
        {
            return Draw.AtmosphereFactors0.y > 0.0;
        }
        
        bool isAtmosphereShell()
        {
            return hasAtmosphereData() && Draw.AtmosphereFactors1.w >= 0.0;
        }
        
        float atmosphereSunIntensity()
        {
            return abs(Draw.AtmosphereFactors1.w);
        }
        
        vec3 atmosphereLightColor()
        {
            return max(Frame.LightColor.rgb, vec3(0.0));
        }
        
        float ozoneAbsorption()
        {
            return max(Draw.AtmosphereColor2.w, 0.0);
        }
        
        bool shouldDiscardAtmosphereBackHemisphere(vec3 rayOrigin, vec3 rayDirection, vec3 planetCenter, float atmosphereRadius)
        {
            float cameraRadius = length(rayOrigin - planetCenter);
            if (cameraRadius <= atmosphereRadius)
            {
                return false;
            }
        
            vec3 shellNormal = normalize(fsin_WorldPosition - planetCenter);
            return dot(shellNormal, rayDirection) > 0.0;
        }
        
        vec3 atmosphereExtinction()
        {
            float rayleighStrength = max(Draw.AtmosphereFactors1.x, 0.0);
            float mieStrength = max(Draw.AtmosphereFactors1.y, 0.0);
            return Draw.AtmosphereColor0.rgb * rayleighStrength
                + Draw.AtmosphereColor1.rgb * mieStrength
                + Draw.AtmosphereColor2.rgb * ozoneAbsorption();
        }
        
        vec3 surfaceAtmosphereExtinction()
        {
            float rayleighStrength = max(Draw.AtmosphereFactors1.x, 0.0);
            float mieStrength = max(Draw.AtmosphereFactors1.y, 0.0);
            vec3 rayleighWavelengthWeight = vec3(0.45, 0.95, 1.85);
            return rayleighWavelengthWeight * rayleighStrength
                + vec3(mieStrength)
                + Draw.AtmosphereColor2.rgb * ozoneAbsorption();
        }
        
        float planetShadowFactor(vec3 samplePoint, vec3 sunDirection, vec3 planetCenter)
        {
            vec3 localUp = normalize(samplePoint - planetCenter);
            return smoothstep(-0.03, 0.08, dot(localUp, sunDirection));
        }
        
        float spaceAmbientFloor()
        {
            return 0.0;
        }
        
        float aerialPerspectiveStrength()
        {
            return clamp(Draw.AtmosphereColor1.w, 0.0, 2.0);
        }
        
        vec3 surfaceAtmosphereTransmittance(vec3 surfacePosition, vec3 lightDirection)
        {
            if (!hasAtmosphereData())
            {
                return vec3(1.0);
            }
        
            float planetRadius = max(Draw.AtmosphereFactors0.x, 0.0001);
            float atmosphereRadius = max(Draw.AtmosphereFactors0.y, planetRadius + 0.0001);
            float density = max(Draw.AtmosphereFactors0.z, 0.0);
            float densityFalloff = max(Draw.AtmosphereFactors0.w, 0.001);
            vec3 planetCenter = Draw.Model[3].xyz;
            vec3 surfaceNormal = normalize(surfacePosition - planetCenter);
            vec3 rayOrigin = surfacePosition + surfaceNormal * max(planetRadius * 0.001, 0.0001);
            vec3 rayDirection = normalize(lightDirection);
        
            vec2 groundHit;
            if (intersectSphere(rayOrigin, rayDirection, planetCenter, planetRadius, groundHit) && groundHit.x > 0.0)
            {
                return vec3(0.0);
            }
        
            vec2 atmosphereHit;
            if (!intersectSphere(rayOrigin, rayDirection, planetCenter, atmosphereRadius, atmosphereHit))
            {
                return vec3(1.0);
            }
        
            float rayLength = max(atmosphereHit.y, 0.0);
            float opticalDepth = integrateOpticalDepth(
                rayOrigin,
                rayDirection,
                rayLength,
                planetCenter,
                planetRadius,
                atmosphereRadius,
                density,
                densityFalloff,
                8);
            return exp(-opticalDepth * surfaceAtmosphereExtinction());
        }
        
        vec2 sphericalUv(vec3 direction)
        {
            vec3 n = normalize(direction);
            float u = atan(n.z, n.x) / (2.0 * PI) + 0.5;
            float v = acos(clamp(n.y, -1.0, 1.0)) / PI;
            return vec2(u, v);
        }
        
        float sampleCloudShadow(vec3 surfacePosition, vec3 lightDirection)
        {
            if (Draw.CloudShadowFactors.x <= 0.5)
            {
                return 1.0;
            }
        
            vec3 planetCenter = Draw.Model[3].xyz;
            float cloudRadius = max(Draw.CloudShadowFactors.y, 0.0001);
            vec3 surfaceNormal = normalize(surfacePosition - planetCenter);
            vec3 rayOrigin = surfacePosition + surfaceNormal * max(cloudRadius * 0.0001, 0.0001);
            vec2 cloudHit;
            if (!intersectSphere(rayOrigin, normalize(lightDirection), planetCenter, cloudRadius, cloudHit) || cloudHit.y <= 0.0)
            {
                return 1.0;
            }
        
            float hitDistance = cloudHit.x > 0.0 ? cloudHit.x : cloudHit.y;
            vec3 cloudPoint = rayOrigin + normalize(lightDirection) * hitDistance;
            float coverage = texture(sampler2D(CloudShadowTexture, CloudShadowSampler), sphericalUv(cloudPoint - planetCenter)).a;
            float strength = clamp(Draw.CloudShadowFactors.z, 0.0, 1.0);
            float daylight = planetShadowFactor(surfacePosition, normalize(lightDirection), planetCenter);
            return clamp(1.0 - coverage * strength * daylight, 0.0, 1.0);
        }
        
        bool hasSurfaceWater()
        {
            return Draw.SurfaceWaterFactors.x > 0.5;
        }
        
        float surfaceWaterSpecularStrength()
        {
            return clamp(Draw.SurfaceWaterFactors.z, 0.0, 8.0);
        }
        
        float sampleSurfaceWaterCoverage(vec2 uv, vec3 baseTextureColor, out vec3 waterTint)
        {
            vec4 water = texture(sampler2D(SurfaceWaterTexture, SurfaceWaterSampler), uv);
            waterTint = mix(vec3(0.006, 0.075, 0.34), pow(max(water.rgb, vec3(0.0)), vec3(2.2)), 0.35);
            float waterColorPresence = max(max(water.r, water.g), water.b) * water.a;
            float baseBlueDominance = baseTextureColor.b - max(baseTextureColor.r, baseTextureColor.g);
            float baseSaturation = max(max(baseTextureColor.r, baseTextureColor.g), baseTextureColor.b)
                - min(min(baseTextureColor.r, baseTextureColor.g), baseTextureColor.b);
            float authoredWaterRegion = smoothstep(0.015, 0.12, baseBlueDominance)
                * smoothstep(0.03, 0.18, baseSaturation);
            float mask = max(waterColorPresence * authoredWaterRegion, waterColorPresence * 0.65);
            return clamp(mask * clamp(Draw.SurfaceWaterFactors.y, 0.0, 4.0), 0.0, 1.0);
        }
        
        vec3 surfaceAerialPerspectiveScattering(vec3 rayOrigin, vec3 rayDirection, float rayStart, float rayEnd, vec3 sunDirection)
        {
            float planetRadius = max(Draw.AtmosphereFactors0.x, 0.0001);
            float atmosphereRadius = max(Draw.AtmosphereFactors0.y, planetRadius + 0.0001);
            float density = max(Draw.AtmosphereFactors0.z, 0.0);
            float densityFalloff = max(Draw.AtmosphereFactors0.w, 0.001);
            float rayleighStrength = max(Draw.AtmosphereFactors1.x, 0.0);
            float mieStrength = max(Draw.AtmosphereFactors1.y, 0.0);
            float mieAnisotropy = clamp(Draw.AtmosphereFactors1.z, -0.99, 0.99);
            vec3 planetCenter = Draw.Model[3].xyz;
            vec3 beta = surfaceAtmosphereExtinction();
            float rayLength = max(rayEnd - rayStart, 0.0);
            if (rayLength <= 0.0001)
            {
                return vec3(0.0);
            }
        
            const int aerialSampleCount = 10;
            float stepSize = rayLength / float(aerialSampleCount);
            float viewOpticalDepth = 0.0;
            vec3 scattered = vec3(0.0);
            for (int i = 0; i < aerialSampleCount; i++)
            {
                float t = rayStart + (float(i) + 0.5) * stepSize;
                vec3 samplePoint = rayOrigin + rayDirection * t;
                float localDensity = atmosphereDensityAtPoint(samplePoint, planetCenter, planetRadius, atmosphereRadius, density, densityFalloff);
                viewOpticalDepth += localDensity * stepSize;
        
                float horizonLight = planetShadowFactor(samplePoint, sunDirection, planetCenter);
                if (horizonLight <= 0.0001)
                {
                    continue;
                }
        
                vec2 lightHit;
                if (!intersectSphere(samplePoint, sunDirection, planetCenter, atmosphereRadius, lightHit))
                {
                    continue;
                }
        
                vec2 lightGroundHit;
                bool hitsPlanetOnLightRay = intersectSphere(samplePoint, sunDirection, planetCenter, planetRadius, lightGroundHit);
                if (hitsPlanetOnLightRay && lightGroundHit.y > 0.0)
                {
                    continue;
                }
        
                float lightDepth = integrateOpticalDepth(samplePoint, sunDirection, max(lightHit.y, 0.0), planetCenter, planetRadius, atmosphereRadius, density, densityFalloff, 6);
                vec3 transmittance = exp(-(viewOpticalDepth + lightDepth) * beta);
                scattered += localDensity * horizonLight * transmittance * stepSize;
            }
        
            float mu = dot(rayDirection, sunDirection);
            vec3 rayleigh = Draw.AtmosphereColor0.rgb * rayleighStrength * phaseRayleigh(mu);
            vec3 mie = Draw.AtmosphereColor1.rgb * mieStrength * phaseMie(mu, mieAnisotropy);
            return (rayleigh + mie) * scattered * atmosphereSunIntensity() * atmosphereLightColor();
        }
        
        vec3 applySurfaceAerialPerspective(vec3 surfaceColor, vec3 surfacePosition, vec3 lightDirection)
        {
            if (!hasAtmosphereData())
            {
                return surfaceColor;
            }
        
            float planetRadius = max(Draw.AtmosphereFactors0.x, 0.0001);
            float atmosphereRadius = max(Draw.AtmosphereFactors0.y, planetRadius + 0.0001);
            float density = max(Draw.AtmosphereFactors0.z, 0.0);
            float densityFalloff = max(Draw.AtmosphereFactors0.w, 0.001);
            vec3 planetCenter = Draw.Model[3].xyz;
            vec3 rayOrigin = Frame.CameraPosition.xyz;
            vec3 rayDirection = normalize(surfacePosition - rayOrigin);
            float surfaceDistance = length(surfacePosition - rayOrigin);
        
            vec2 atmosphereHit;
            if (!intersectSphere(rayOrigin, rayDirection, planetCenter, atmosphereRadius, atmosphereHit))
            {
                return surfaceColor;
            }
        
            float rayStart = max(atmosphereHit.x, 0.0);
            float rayEnd = min(surfaceDistance, atmosphereHit.y);
            if (rayEnd <= rayStart)
            {
                return surfaceColor;
            }
        
            float opticalDepth = integrateOpticalDepth(
                rayOrigin + rayDirection * rayStart,
                rayDirection,
                rayEnd - rayStart,
                planetCenter,
                planetRadius,
                atmosphereRadius,
                density,
                densityFalloff,
                10);
            float strength = aerialPerspectiveStrength();
            float cameraAtmosphereRatio = length(rayOrigin - planetCenter) / atmosphereRadius;
            float lowAltitudeView = 1.0 - smoothstep(1.0, 1.35, cameraAtmosphereRatio);
            float effectiveStrength = strength * mix(1.0, 0.32, lowAltitudeView);
            vec3 transmittance = exp(-opticalDepth * surfaceAtmosphereExtinction() * effectiveStrength);
            vec3 scattering = surfaceAerialPerspectiveScattering(rayOrigin, rayDirection, rayStart, rayEnd, normalize(lightDirection));
            vec3 surfaceNormal = normalize(surfacePosition - planetCenter);
            float surfaceSun = smoothstep(-0.08, 0.18, dot(surfaceNormal, normalize(lightDirection)));
            vec3 scatteringTint = mix(vec3(1.0), vec3(0.55, 0.78, 1.35), lowAltitudeView);
            return surfaceColor * transmittance + scattering * scatteringTint * effectiveStrength * surfaceSun;
        }
        
        vec4 renderAtmosphere()
        {
            float planetRadius = max(Draw.AtmosphereFactors0.x, 0.0001);
            float atmosphereRadius = max(Draw.AtmosphereFactors0.y, planetRadius + 0.0001);
            float density = max(Draw.AtmosphereFactors0.z, 0.0);
            float densityFalloff = max(Draw.AtmosphereFactors0.w, 0.001);
            float rayleighStrength = max(Draw.AtmosphereFactors1.x, 0.0);
            float mieStrength = max(Draw.AtmosphereFactors1.y, 0.0);
            float mieAnisotropy = clamp(Draw.AtmosphereFactors1.z, -0.99, 0.99);
            float sunIntensity = atmosphereSunIntensity();
            int viewSampleCount = int(clamp(Draw.MaterialFactors.x, 4.0, float(MAX_VIEW_SAMPLE_COUNT)));
            int lightSampleCount = int(clamp(Draw.MaterialFactors.y, 2.0, float(MAX_LIGHT_SAMPLE_COUNT)));
            vec3 planetCenter = Draw.Model[3].xyz;
            vec3 rayOrigin = Frame.CameraPosition.xyz;
            vec3 rayDirection = normalize(fsin_WorldPosition - rayOrigin);
            vec3 sunDirection = Frame.LightPosition.w > 0.5
                ? normalize(Frame.LightPosition.xyz - fsin_WorldPosition)
                : normalize(-Frame.LightDirection.xyz);
            if (shouldDiscardAtmosphereBackHemisphere(rayOrigin, rayDirection, planetCenter, atmosphereRadius))
            {
                discard;
            }
        
            vec2 atmosphereHit;
            if (!intersectSphere(rayOrigin, rayDirection, planetCenter, atmosphereRadius, atmosphereHit))
            {
                return vec4(0.0);
            }
        
            vec2 groundHit;
            float rayStart = max(atmosphereHit.x, 0.0);
            float rayEnd = atmosphereHit.y;
            bool hitsGround = intersectSphere(rayOrigin, rayDirection, planetCenter, planetRadius, groundHit);
            if (hitsGround && groundHit.x > 0.0)
            {
                discard;
            }
        
            float rayLength = max(rayEnd - rayStart, 0.0);
            if (rayLength <= 0.0001)
            {
                return vec4(0.0);
            }
        
            float stepSize = rayLength / float(viewSampleCount);
            float viewOpticalDepth = 0.0;
            vec3 scattered = vec3(0.0);
            vec3 beta = atmosphereExtinction();
            for (int i = 0; i < MAX_VIEW_SAMPLE_COUNT; i++)
            {
                if (i >= viewSampleCount)
                {
                    break;
                }
        
                float t = rayStart + (float(i) + 0.5) * stepSize;
                vec3 samplePoint = rayOrigin + rayDirection * t;
                float localDensity = atmosphereDensityAtPoint(samplePoint, planetCenter, planetRadius, atmosphereRadius, density, densityFalloff);
                float horizonLight = planetShadowFactor(samplePoint, sunDirection, planetCenter);
                if (horizonLight <= 0.0001)
                {
                    continue;
                }
        
                viewOpticalDepth += localDensity * stepSize;
        
                vec2 lightHit;
                bool exitsAtmosphere = intersectSphere(samplePoint, sunDirection, planetCenter, atmosphereRadius, lightHit);
                vec2 lightGroundHit;
                bool hitsPlanetOnLightRay = intersectSphere(samplePoint, sunDirection, planetCenter, planetRadius, lightGroundHit);
                bool shadowed = hitsPlanetOnLightRay && lightGroundHit.y > 0.0;
                if (!exitsAtmosphere || shadowed)
                {
                    continue;
                }
        
                float lightDepth = integrateOpticalDepth(samplePoint, sunDirection, max(lightHit.y, 0.0), planetCenter, planetRadius, atmosphereRadius, density, densityFalloff, lightSampleCount);
                vec3 transmittance = exp(-(viewOpticalDepth + lightDepth) * beta);
                scattered += localDensity * horizonLight * transmittance * stepSize;
            }
        
            float mu = dot(rayDirection, sunDirection);
            vec3 rayleigh = Draw.AtmosphereColor0.rgb * rayleighStrength * phaseRayleigh(mu);
            vec3 mie = Draw.AtmosphereColor1.rgb * mieStrength * phaseMie(mu, mieAnisotropy);
            vec3 color = (rayleigh + mie) * scattered * sunIntensity * atmosphereLightColor();
            vec3 shellNormal = normalize(fsin_WorldPosition - planetCenter);
            vec3 viewDirection = normalize(rayOrigin - fsin_WorldPosition);
            float limb = pow(clamp(1.0 - abs(dot(shellNormal, viewDirection)), 0.0, 1.0), 3.5);
            float sunlitRim = smoothstep(-0.22, 0.18, dot(shellNormal, sunDirection));
            color += Draw.AtmosphereColor0.rgb * atmosphereLightColor() * limb * sunlitRim * rayleighStrength * sunIntensity * 0.18;
            vec3 mapped = vec3(1.0) - exp(-color * max(Draw.EmissiveFactors.a, 0.0));
            float alpha = clamp(max(max(mapped.r, mapped.g), mapped.b) * 1.8, 0.0, 0.9);
            return vec4(pow(mapped, vec3(1.0 / 2.2)), alpha);
        }
        
        bool isCloudLayer()
        {
            return Draw.CloudFactors.y > 0.0;
        }
        
        bool cloudAlphaFromTextureOnly()
        {
            return Draw.CloudFactors.x > 0.5;
        }
        
        float cloudSkyVisibility(vec3 cloudPosition, vec3 sunDirection, vec3 planetCenter)
        {
            return planetShadowFactor(cloudPosition, sunDirection, planetCenter);
        }
        
        vec4 renderCloudLayer()
        {
            vec3 rayOrigin = Frame.CameraPosition.xyz;
            vec3 rayDirection = normalize(fsin_WorldPosition - rayOrigin);
            vec3 planetCenter = Draw.Model[3].xyz;
            float shellRadius = max(length(fsin_WorldPosition - planetCenter), 0.0001);
            if (shouldDiscardAtmosphereBackHemisphere(rayOrigin, rayDirection, planetCenter, shellRadius))
            {
                discard;
            }
        
            vec3 normal = normalize(fsin_WorldPosition - planetCenter);
            vec3 view = normalize(rayOrigin - fsin_WorldPosition);
            vec3 light = Frame.LightPosition.w > 0.5
                ? normalize(Frame.LightPosition.xyz - fsin_WorldPosition)
                : normalize(-Frame.LightDirection.xyz);
            float lambertian = max(dot(normal, light), 0.0);
            float skyVisibility = cloudSkyVisibility(fsin_WorldPosition, light, planetCenter);
        
            vec4 textureColor = texture(sampler2D(BaseColorTexture, BaseColorSampler), fsin_UV);
            float textureCoverage = cloudAlphaFromTextureOnly()
                ? textureColor.a
                : max(max(textureColor.r, textureColor.g), textureColor.b) * textureColor.a;
            float slantPath = clamp(1.0 / max(abs(dot(normal, view)), 0.22), 1.0, 3.25);
            float opticalDepth = textureCoverage * max(Draw.CloudFactors.y, 0.0) * max(Draw.CloudColor.a, 0.0) * slantPath;
            float alpha = clamp((1.0 - exp(-opticalDepth)) * mix(0.32, 1.0, skyVisibility), 0.0, 0.72);
            if (alpha < 0.01)
            {
                discard;
            }
        
            vec3 cloudBase = cloudAlphaFromTextureOnly()
                ? vec3(0.92, 0.95, 1.0)
                : mix(vec3(0.88, 0.91, 0.96), pow(max(textureColor.rgb, vec3(0.0)), vec3(2.2)), 0.82);
            float lightTerm = mix(1.0, lambertian, clamp(Draw.CloudFactors.z, 0.0, 1.0));
            vec3 directTransmittance = surfaceAtmosphereTransmittance(fsin_WorldPosition, light);
            float ambientTerm = max(Draw.CloudFactors.w, 0.0) * mix(0.04, 1.0, skyVisibility);
            float sunView = clamp(dot(light, view), 0.0, 1.0);
            float silverLining = pow(sunView, 12.0) * smoothstep(0.0, 0.35, lambertian) * skyVisibility;
            vec3 color = cloudBase
                * pow(max(Draw.CloudColor.rgb, vec3(0.0)), vec3(2.2))
                * (Frame.LightColor.rgb * directTransmittance * lightTerm * skyVisibility * 2.65 + vec3(ambientTerm));
            color += Frame.LightColor.rgb * directTransmittance * silverLining * 0.75;
            color = applySurfaceAerialPerspective(color, fsin_WorldPosition, light);
            return vec4(pow(max(color, vec3(0.0)), vec3(1.0 / 2.2)), alpha);
        }

        mat4 directionalShadowMatrix(int cascadeIndex)
        {
            if (cascadeIndex == 0) return Frame.ShadowViewProjection0;
            if (cascadeIndex == 1) return Frame.ShadowViewProjection1;
            if (cascadeIndex == 2) return Frame.ShadowViewProjection2;
            return Frame.ShadowViewProjection3;
        }

        float sampleDirectionalShadow(vec3 worldPosition, vec3 normal, vec3 lightDirection)
        {
            int cascadeCount = int(Frame.ShadowParameters.x + 0.5);
            if (cascadeCount <= 0 || Draw.ShadowFactors.x < 0.5)
            {
                return 1.0;
            }

            float viewDistance = distance(Frame.CameraPosition.xyz, worldPosition);
            int cascadeIndex = 0;
            if (cascadeCount > 1 && viewDistance > Frame.ShadowSplitDepths.x) cascadeIndex = 1;
            if (cascadeCount > 2 && viewDistance > Frame.ShadowSplitDepths.y) cascadeIndex = 2;
            if (cascadeCount > 3 && viewDistance > Frame.ShadowSplitDepths.z) cascadeIndex = 3;

            vec3 offsetPosition = worldPosition + normal * Frame.ShadowParameters.z;
            vec4 shadowClip = directionalShadowMatrix(cascadeIndex) * vec4(offsetPosition, 1.0);
            if (shadowClip.w <= 0.00001)
            {
                return 1.0;
            }

            vec3 shadowNdc = shadowClip.xyz / shadowClip.w;
            vec2 shadowUv = shadowNdc.xy * 0.5 + 0.5;
            if (shadowUv.x <= 0.0 || shadowUv.x >= 1.0 || shadowUv.y <= 0.0 || shadowUv.y >= 1.0 || shadowNdc.z <= 0.0 || shadowNdc.z >= 1.0)
            {
                return 1.0;
            }

            float slopeBias = Frame.ShadowParameters.y * (1.0 + 2.0 * (1.0 - max(dot(normal, lightDirection), 0.0)));
            float referenceDepth = shadowNdc.z - slopeBias;
            float texel = Frame.ShadowParameters.w;
            float visibility = 0.0;
            for (int y = -1; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    float storedDepth = texture(
                        sampler2DArray(DirectionalShadowAtlas, DirectionalShadowSampler),
                        vec3(shadowUv + vec2(x, y) * texel, float(cascadeIndex))).r;
                    visibility += referenceDepth <= storedDepth ? 1.0 : 0.0;
                }
            }
            return mix(0.22, 1.0, visibility / 9.0);
        }

        float interactiveFogInfluence(int volumeIndex, vec3 worldPosition)
        {
            InteractiveFogVolume volume = Fog.Volumes[volumeIndex];
            int shape = int(volume.PositionShape.w + 0.5);
            float influence = 1.0;
            if (shape == 1)
            {
                vec3 local = (volume.WorldToLocal * vec4(worldPosition, 1.0)).xyz;
                vec3 remaining = volume.HalfExtentsDensity.xyz - abs(local);
                float edge = min(remaining.x, min(remaining.y, remaining.z));
                if (edge <= 0.0) return 0.0;
                float blend = volume.BlendPriority.x;
                influence = blend <= 0.0001 ? 1.0 : smoothstep(0.0, blend, edge);
            }
            else if (shape == 2)
            {
                vec3 local = (volume.WorldToLocal * vec4(worldPosition, 1.0)).xyz;
                float normalizedRadius = length(local / max(volume.HalfExtentsDensity.xyz, vec3(0.001)));
                if (normalizedRadius >= 1.0) return 0.0;
                float normalizedBlend = volume.BlendPriority.x / max(max(volume.HalfExtentsDensity.x, volume.HalfExtentsDensity.y), volume.HalfExtentsDensity.z);
                influence = normalizedBlend <= 0.0001
                    ? 1.0
                    : smoothstep(0.0, normalizedBlend, 1.0 - normalizedRadius);
            }

            float height = max(0.0, worldPosition.y - volume.PositionShape.y);
            return influence * exp(-height * volume.EmissionHeightFalloff.w);
        }

        vec3 applyInteractiveFog(vec3 surfaceColor, vec3 surfacePosition, vec3 lightDirection)
        {
            int volumeCount = int(Fog.Settings.x + 0.5);
            if (volumeCount <= 0)
            {
                return surfaceColor;
            }

            vec3 ray = surfacePosition - Frame.CameraPosition.xyz;
            float rayLength = min(length(ray), 140.0);
            if (rayLength <= 0.001)
            {
                return surfaceColor;
            }

            vec3 rayDirection = normalize(ray);
            const int stepCount = 6;
            float stepSize = rayLength / float(stepCount);
            float transmittance = 1.0;
            vec3 integrated = vec3(0.0);
            for (int stepIndex = 0; stepIndex < stepCount; stepIndex++)
            {
                vec3 samplePosition = Frame.CameraPosition.xyz + rayDirection * ((float(stepIndex) + 0.5) * stepSize);
                for (int volumeIndex = 0; volumeIndex < 8; volumeIndex++)
                {
                    if (volumeIndex >= volumeCount) break;
                    InteractiveFogVolume volume = Fog.Volumes[volumeIndex];
                    float influence = interactiveFogInfluence(volumeIndex, samplePosition);
                    float opticalDepth = volume.HalfExtentsDensity.w * influence * stepSize * Fog.Settings.z;
                    if (opticalDepth <= 0.000001) continue;

                    float extinction = exp(-opticalDepth);
                    float forwardPhase = mix(0.34, 0.78, pow(max(dot(rayDirection, lightDirection), 0.0), mix(2.0, 8.0, max(volume.AlbedoAnisotropy.w, 0.0))));
                    float shadowVisibility = sampleDirectionalShadow(samplePosition, lightDirection, lightDirection);
                    vec3 fogLight = volume.AlbedoAnisotropy.rgb
                        * (vec3(0.16) + Frame.LightColor.rgb * forwardPhase * shadowVisibility * 0.58)
                        + volume.EmissionHeightFalloff.rgb;
                    integrated += transmittance * fogLight * (1.0 - extinction);
                    transmittance *= extinction;
                }
            }
            return surfaceColor * transmittance + integrated;
        }
        
        void main()
        {
            if (isAtmosphereShell())
            {
                fsout_Color = renderAtmosphere();
                return;
            }
        
            if (isCloudLayer())
            {
                fsout_Color = renderCloudLayer();
                return;
            }
        
            vec4 textureColor = texture(sampler2D(BaseColorTexture, BaseColorSampler), fsin_UV);
            vec3 albedo = pow(max(fsin_Color.rgb * textureColor.rgb, vec3(0.0)), vec3(2.2));
            vec4 metalRough = texture(sampler2D(MetallicRoughnessTexture, MetallicRoughnessSampler), fsin_UV);
            float metallic = clamp(metalRough.b * Draw.MaterialFactors.x, 0.0, 1.0);
            float roughness = clamp(metalRough.g * Draw.MaterialFactors.y, 0.04, 1.0);
            vec3 waterTint = vec3(0.0);
            float waterCoverage = hasSurfaceWater() ? sampleSurfaceWaterCoverage(fsin_UV, textureColor.rgb, waterTint) : 0.0;
            if (waterCoverage > 0.0001)
            {
                albedo = mix(albedo, waterTint, waterCoverage * 0.78);
                roughness = mix(roughness, clamp(Draw.SurfaceWaterFactors.w, 0.01, 1.0), waterCoverage);
                metallic = mix(metallic, 0.0, waterCoverage);
            }
            float occlusion = 1.0;
            if (Draw.MaterialFactors.w > 0.0001)
            {
                occlusion = mix(1.0, texture(sampler2D(OcclusionTexture, OcclusionSampler), fsin_UV).r, Draw.MaterialFactors.w);
            }
            vec3 light = Frame.LightPosition.w > 0.5
                ? normalize(Frame.LightPosition.xyz - fsin_WorldPosition)
                : normalize(-Frame.LightDirection.xyz);
            vec3 view = normalize(Frame.CameraPosition.xyz - fsin_WorldPosition);
            vec3 normal = hasAtmosphereData()
                ? normalize(fsin_WorldPosition - Draw.Model[3].xyz)
                : normalize(fsin_Normal);
            if (dot(normal, view) < 0.0)
            {
                normal = -normal;
            }
        
            if (Draw.MaterialFactors.z > 0.0001)
            {
                normal = perturbNormal(normal);
            }
            vec3 halfVector = normalize(view + light);
            float ndotl = max(dot(normal, light), 0.0);
            float ndotv = max(dot(normal, view), 0.0);
            vec3 f0 = mix(mix(vec3(0.04), albedo, metallic), vec3(0.02), waterCoverage);
            float d = distributionGgx(normal, halfVector, roughness);
            float g = geometrySchlickGgx(ndotv, roughness) * geometrySchlickGgx(ndotl, roughness);
            vec3 f = fresnelSchlick(max(dot(halfVector, view), 0.0), f0);
            vec3 specular = d * g * f / max(4.0 * ndotv * ndotl, 0.0001);
            specular *= mix(1.0, surfaceWaterSpecularStrength(), waterCoverage);
            vec3 diffuse = (1.0 - f) * (1.0 - metallic) * albedo / PI;
            diffuse *= mix(1.0, 0.42, waterCoverage);
            vec3 directTransmittance = surfaceAtmosphereTransmittance(fsin_WorldPosition, light);
            float ambientStrength = hasAtmosphereData()
                ? spaceAmbientFloor()
                : 0.12 * max(Frame.EnvironmentParameters.x, 0.0);
            float ambientHemisphere = clamp(normal.y * 0.5 + 0.5, 0.0, 1.0);
            vec3 environmentAmbientColor = mix(Frame.EnvironmentAmbientGroundColor.rgb, Frame.EnvironmentAmbientSkyColor.rgb, ambientHemisphere);
            bool hasEnvironmentImage = Frame.EnvironmentAmbientSkyColor.a > 0.5;
            vec3 environmentDiffuse = hasEnvironmentImage
                ? sampleEnvironmentRadiance(normal, 0.82)
                : environmentAmbientColor;
            vec3 environmentSpecular = hasEnvironmentImage
                ? sampleEnvironmentRadiance(reflect(-view, normal), roughness)
                : environmentAmbientColor * mix(0.28, 1.0, 1.0 - roughness);
            vec3 ambientFresnel = fresnelSchlick(ndotv, f0);
            vec3 ambientDiffuse = (1.0 - ambientFresnel) * (1.0 - metallic) * albedo;
            vec3 ambient = (ambientDiffuse * environmentDiffuse + ambientFresnel * environmentSpecular)
                * ambientStrength
                * occlusion;
            vec3 waterFresnel = fresnelSchlick(ndotv, vec3(0.02));
            ambient += waterFresnel * Frame.LightColor.rgb * directTransmittance * waterCoverage * 0.018;
            vec3 emissive = pow(max(texture(sampler2D(EmissiveTexture, EmissiveSampler), fsin_UV).rgb * Draw.EmissiveFactors.rgb, vec3(0.0)), vec3(2.2)) * Draw.EmissiveFactors.a;
            float cloudShadow = sampleCloudShadow(fsin_WorldPosition, light);
            float directionalShadow = sampleDirectionalShadow(fsin_WorldPosition, normal, light);
            vec3 color = emissive + ambient + (diffuse + specular) * Frame.LightColor.rgb * directTransmittance * cloudShadow * directionalShadow * ndotl * 1.8;
            vec4 practicalColors[4] = vec4[](Frame.AdditionalLightColor, Frame.AdditionalLightColor2, Frame.AdditionalLightColor3, Frame.AdditionalLightColor4);
            vec4 practicalPositions[4] = vec4[](Frame.AdditionalLightPosition, Frame.AdditionalLightPosition2, Frame.AdditionalLightPosition3, Frame.AdditionalLightPosition4);
            vec4 practicalParameters[4] = vec4[](Frame.AdditionalLightParameters, Frame.AdditionalLightParameters2, Frame.AdditionalLightParameters3, Frame.AdditionalLightParameters4);
            for (int practicalIndex = 0; practicalIndex < 4; practicalIndex++)
            {
                if (dot(practicalColors[practicalIndex].rgb, practicalColors[practicalIndex].rgb) <= 0.000001) continue;
                vec3 practicalOffset = practicalPositions[practicalIndex].xyz - fsin_WorldPosition;
                float practicalDistance = length(practicalOffset);
                vec3 practicalLight = practicalOffset / max(practicalDistance, 0.0001);
                float practicalRange = max(practicalParameters[practicalIndex].x, 0.001);
                float practicalWindow = pow(clamp(1.0 - practicalDistance / practicalRange, 0.0, 1.0), 2.0);
                float practicalAttenuation = practicalWindow / (1.0 + 0.045 * practicalDistance * practicalDistance);
                vec3 practicalHalf = normalize(view + practicalLight);
                float practicalNdotL = max(dot(normal, practicalLight), 0.0);
                float practicalD = distributionGgx(normal, practicalHalf, roughness);
                float practicalG = geometrySchlickGgx(ndotv, roughness) * geometrySchlickGgx(practicalNdotL, roughness);
                vec3 practicalF = fresnelSchlick(max(dot(practicalHalf, view), 0.0), f0);
                vec3 practicalSpecular = practicalD * practicalG * practicalF / max(4.0 * ndotv * practicalNdotL, 0.0001);
                vec3 practicalDiffuse = (1.0 - practicalF) * (1.0 - metallic) * albedo / PI;
                color += (practicalDiffuse + practicalSpecular) * practicalColors[practicalIndex].rgb * practicalNdotL * practicalAttenuation * 4.5;
            }
            vec4 spotColors[4] = vec4[](Frame.SpotLightColor, Frame.SpotLightColor2, Frame.SpotLightColor3, Frame.SpotLightColor4);
            vec4 spotPositions[4] = vec4[](Frame.SpotLightPosition, Frame.SpotLightPosition2, Frame.SpotLightPosition3, Frame.SpotLightPosition4);
            vec4 spotDirections[4] = vec4[](Frame.SpotLightDirection, Frame.SpotLightDirection2, Frame.SpotLightDirection3, Frame.SpotLightDirection4);
            vec4 spotParameters[4] = vec4[](Frame.SpotLightParameters, Frame.SpotLightParameters2, Frame.SpotLightParameters3, Frame.SpotLightParameters4);
            for (int spotIndex = 0; spotIndex < 4; spotIndex++)
            {
                if (dot(spotColors[spotIndex].rgb, spotColors[spotIndex].rgb) <= 0.000001) continue;
                vec3 spotOffset = spotPositions[spotIndex].xyz - fsin_WorldPosition;
                float spotDistance = length(spotOffset);
                vec3 spotLight = spotOffset / max(spotDistance, 0.0001);
                float spotRange = max(spotParameters[spotIndex].x, 0.001);
                float spotWindow = pow(clamp(1.0 - spotDistance / spotRange, 0.0, 1.0), 2.0);
                float spotDistanceAttenuation = spotWindow / (1.0 + 0.045 * spotDistance * spotDistance);
                vec3 spotForward = normalize(spotDirections[spotIndex].xyz);
                float spotCos = dot(-spotLight, spotForward);
                float spotInnerCos = spotParameters[spotIndex].z;
                float spotOuterCos = spotParameters[spotIndex].w;
                float spotConeAttenuation = clamp((spotCos - spotOuterCos) / max(spotInnerCos - spotOuterCos, 0.0001), 0.0, 1.0);
                spotConeAttenuation *= spotConeAttenuation;
                float spotAttenuation = spotDistanceAttenuation * spotConeAttenuation;
                if (spotAttenuation <= 0.0001) continue;
                vec3 spotHalf = normalize(view + spotLight);
                float spotNdotL = max(dot(normal, spotLight), 0.0);
                float spotD = distributionGgx(normal, spotHalf, roughness);
                float spotG = geometrySchlickGgx(ndotv, roughness) * geometrySchlickGgx(spotNdotL, roughness);
                vec3 spotF = fresnelSchlick(max(dot(spotHalf, view), 0.0), f0);
                vec3 spotSpecular = spotD * spotG * spotF / max(4.0 * ndotv * spotNdotL, 0.0001);
                vec3 spotDiffuse = (1.0 - spotF) * (1.0 - metallic) * albedo / PI;
                color += (spotDiffuse + spotSpecular) * spotColors[spotIndex].rgb * spotNdotL * spotAttenuation * 4.5;
            }
            color = applySurfaceAerialPerspective(color, fsin_WorldPosition, light);
            color = applyInteractiveFog(color, fsin_WorldPosition, light);
            // The scene target is a floating-point HDR buffer, so this pass writes linear
            // radiance and leaves exposure, tone mapping and gamma to the present pass -
            // matching the Vulkan capture path, which tone maps at present too. Doing it here
            // instead forced the present pass to work on gamma-encoded LDR values, which is
            // why the interactive path could not implement AgX or a white point at all.
            vec3 lit = max(color, vec3(0.0));
            float surfaceAlpha = hasAtmosphereData() ? fsin_Color.a : fsin_Color.a * textureColor.a;
            // Draw.ShadowFactors.y/.z are repurposed as AlphaCutoff and an "is this a mask-mode
            // draw" flag, rather than adding new fields to this shared uniform struct. A masked
            // material discards below its cutoff instead of blending, the real alpha-tested cutout
            // behavior (foliage, chain-link) as opposed to AlphaMode "blend"'s soft transparency.
            if (Draw.ShadowFactors.z > 0.5 && surfaceAlpha < Draw.ShadowFactors.y)
            {
                discard;
            }

            fsout_Color = vec4(lit, surfaceAlpha);
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
        layout(set = 0, binding = 2) uniform texture2D SceneDepthTexture;
        layout(set = 0, binding = 3) uniform sampler SceneDepthSampler;
        layout(set = 1, binding = 0) uniform PostProcessUniformBuffer
        {
            vec4 PostProcessParameters;
            vec4 ScreenParameters;
            vec4 AmbientOcclusionParameters;
            mat4 InverseViewProjection;
            vec4 CameraPosition;
            vec4 EnvironmentParameters;
        };

        layout(location = 0) out vec4 fsout_Color;

        float luma(vec3 color)
        {
            return dot(color, vec3(0.299, 0.587, 0.114));
        }

        // Kept byte-identical to agxCurve in Shaders/rekall_tonemap.frag so the interactive
        // player and the Vulkan capture path apply the same display transform. If one of these
        // changes, the other must change with it.
        vec3 agxCurve(vec3 value)
        {
            value = max(value, vec3(0.0));
            vec3 logValue = clamp((log2(max(value, vec3(1e-6))) + 10.0) / 16.5, 0.0, 1.0);
            vec3 sigmoid = logValue * logValue * (3.0 - 2.0 * logValue);
            return sigmoid * sigmoid * (3.0 - 2.0 * sigmoid);
        }

        float dirtHash(vec2 p)
        {
            return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
        }

        float dirtNoise(vec2 p)
        {
            vec2 i = floor(p);
            vec2 f = fract(p);
            vec2 u = f * f * (3.0 - 2.0 * f);
            return mix(mix(dirtHash(i), dirtHash(i + vec2(1.0, 0.0)), u.x),
                       mix(dirtHash(i + vec2(0.0, 1.0)), dirtHash(i + vec2(1.0, 1.0)), u.x), u.y);
        }

        float dirtFbm(vec2 p, int octaves)
        {
            const mat2 turn = mat2(0.8, -0.6, 0.6, 0.8);
            float total = 0.0;
            float amp = 0.5;
            float norm = 0.0;
            for (int i = 0; i < octaves; ++i)
            {
                total += dirtNoise(p) * amp;
                norm += amp;
                p = turn * p * 2.03 + 19.7;
                amp *= 0.5;
            }
            return total / max(norm, 0.0001);
        }

        float lensDirtMask(vec2 uv)
        {
            vec2 centred = uv - 0.5;
            vec2 aspect = vec2(textureSize(sampler2D(SceneTexture, SceneSampler), 0));
            vec2 p = vec2(centred.x * (aspect.x / max(aspect.y, 1.0)), centred.y);
            float smudge = smoothstep(0.46, 0.78, dirtFbm(p * 3.4, 5));
            float speck = smoothstep(0.70, 0.86, dirtFbm(p * 26.0 + 41.7, 3));
            float ang = atan(p.y, p.x);
            float streak = smoothstep(0.52, 0.84, dirtFbm(vec2(ang * 1.7, length(p) * 4.5) + 7.1, 4)) * 0.5;
            float edgeBias = 0.30 + 0.70 * smoothstep(0.05, 0.70, length(centred));
            return clamp((smudge * 0.55 + speck * 0.35 + streak) * edgeBias, 0.0, 1.0);
        }

        vec3 brightPass(vec3 color)
        {
            float brightness = max(max(color.r, color.g), color.b);
            float threshold = PostProcessParameters.x;
            // Keep only the amount by which this pixel exceeds the threshold, scaled back onto
            // its own hue. The previous smoothstep(threshold, 1.0, brightness) knee was written
            // for an LDR scene target: on the floating-point target it saturates to 1.0 for any
            // value above 1, so it returned the pixel at full strength and bloom became a set of
            // offset copies of the scene rather than a glow.
            float excess = max(brightness - threshold, 0.0);
            return color * (excess / max(brightness, 0.0001));
        }

        vec3 sampleBloom(vec2 uv, vec2 texel)
        {
            // One centred tap per downsampled level, weighted so the coarse levels supply the
            // wide falloff. Offset taps are deliberately absent: on a mip chain each tap is
            // already an average of many pixels, so a ring of them only stamps the sampling
            // pattern into the image - the diamond clusters that appeared around bright drives.
            vec3 bloom = vec3(0.0);
            float weight = 0.0;
            for (int level = 1; level <= 5; ++level)
            {
                float lod = float(level);
                float levelWeight = 1.0 / float(level);
                bloom += brightPass(textureLod(sampler2D(SceneTexture, SceneSampler), uv, lod).rgb) * levelWeight;
                weight += levelWeight;
            }

            return bloom / max(weight, 0.0001);
        }

        float resolveAmbientOcclusion(vec2 uv, vec2 texel)
        {
            int sampleCount = int(AmbientOcclusionParameters.x + 0.5);
            if (sampleCount <= 0)
            {
                return 1.0;
            }

            float centerDepth = texture(sampler2D(SceneDepthTexture, SceneDepthSampler), uv).r;
            if (centerDepth >= 0.99999)
            {
                return 1.0;
            }

            const vec2 directions[12] = vec2[](
                vec2(1.0, 0.0), vec2(0.707, 0.707), vec2(0.0, 1.0), vec2(-0.707, 0.707),
                vec2(-1.0, 0.0), vec2(-0.707, -0.707), vec2(0.0, -1.0), vec2(0.707, -0.707),
                vec2(0.383, 0.924), vec2(-0.924, 0.383), vec2(-0.383, -0.924), vec2(0.924, -0.383));
            vec4 centerWorldH = InverseViewProjection * vec4(uv * 2.0 - 1.0, centerDepth, 1.0);
            vec3 centerWorld = centerWorldH.xyz / max(abs(centerWorldH.w), 0.00001);
            float centerDistance = distance(CameraPosition.xyz, centerWorld);
            float radius = AmbientOcclusionParameters.y;
            float bias = AmbientOcclusionParameters.w;
            float occlusion = 0.0;
            for (int index = 0; index < 12; ++index)
            {
                if (index >= sampleCount)
                {
                    break;
                }

                float ring = 0.45 + 0.55 * (float(index + 1) / float(sampleCount));
                vec2 sampleUv = clamp(uv + directions[index] * texel * radius * ring, texel, vec2(1.0) - texel);
                float sampleDepth = texture(sampler2D(SceneDepthTexture, SceneDepthSampler), sampleUv).r;
                if (sampleDepth >= 0.99999)
                {
                    continue;
                }
                vec4 sampleWorldH = InverseViewProjection * vec4(sampleUv * 2.0 - 1.0, sampleDepth, 1.0);
                vec3 sampleWorld = sampleWorldH.xyz / max(abs(sampleWorldH.w), 0.00001);
                float depthDelta = centerDistance - distance(CameraPosition.xyz, sampleWorld);
                float blocker = smoothstep(bias, bias + 0.42, depthDelta);
                float rangeWeight = 1.0 - smoothstep(0.2, 3.5, depthDelta);
                occlusion += blocker * rangeWeight;
            }

            float normalized = occlusion / float(sampleCount);
            return clamp(1.0 - normalized * AmbientOcclusionParameters.z, 0.55, 1.0);
        }

        vec4 resolveFxaa(vec2 texel)
        {
            vec4 center = textureLod(sampler2D(SceneTexture, SceneSampler), fsin_UV, 0.0);
            vec3 nw = textureLod(sampler2D(SceneTexture, SceneSampler), fsin_UV + texel * vec2(-1.0, -1.0), 0.0).rgb;
            vec3 ne = textureLod(sampler2D(SceneTexture, SceneSampler), fsin_UV + texel * vec2(1.0, -1.0), 0.0).rgb;
            vec3 sw = textureLod(sampler2D(SceneTexture, SceneSampler), fsin_UV + texel * vec2(-1.0, 1.0), 0.0).rgb;
            vec3 se = textureLod(sampler2D(SceneTexture, SceneSampler), fsin_UV + texel * vec2(1.0, 1.0), 0.0).rgb;
            float lumaCenter = luma(center.rgb);
            float lumaNw = luma(nw);
            float lumaNe = luma(ne);
            float lumaSw = luma(sw);
            float lumaSe = luma(se);
            float lumaMin = min(lumaCenter, min(min(lumaNw, lumaNe), min(lumaSw, lumaSe)));
            float lumaMax = max(lumaCenter, max(max(lumaNw, lumaNe), max(lumaSw, lumaSe)));
            float edgeContrast = lumaMax - lumaMin;
            if (edgeContrast < max(0.0312, lumaMax * 0.125))
            {
                return center;
            }

            vec2 direction = vec2(
                -((lumaNw + lumaNe) - (lumaSw + lumaSe)),
                 ((lumaNw + lumaSw) - (lumaNe + lumaSe)));
            float directionReduce = max((lumaNw + lumaNe + lumaSw + lumaSe) * 0.0078125, 0.0009765625);
            float directionScale = 1.0 / (min(abs(direction.x), abs(direction.y)) + directionReduce);
            direction = clamp(direction * directionScale, vec2(-8.0), vec2(8.0)) * texel;

            vec3 rgbA = 0.5 * (
                texture(sampler2D(SceneTexture, SceneSampler), fsin_UV + direction * (1.0 / 3.0 - 0.5)).rgb +
                texture(sampler2D(SceneTexture, SceneSampler), fsin_UV + direction * (2.0 / 3.0 - 0.5)).rgb);
            vec3 rgbB = rgbA * 0.5 + 0.25 * (
                texture(sampler2D(SceneTexture, SceneSampler), fsin_UV + direction * -0.5).rgb +
                texture(sampler2D(SceneTexture, SceneSampler), fsin_UV + direction * 0.5).rgb);
            float lumaB = luma(rgbB);
            return vec4((lumaB < lumaMin || lumaB > lumaMax) ? rgbA : rgbB, center.a);
        }

        void main()
        {
            vec2 texel = 1.0 / vec2(textureSize(sampler2D(SceneTexture, SceneSampler), 0));
            vec4 resolved = resolveFxaa(texel);
            vec3 bloom = sampleBloom(fsin_UV, texel);
            float ambientOcclusion = resolveAmbientOcclusion(fsin_UV, texel);

            // The scene target is now linear HDR, so this pass owns the whole display
            // transform - matching the Vulkan capture path's tone-map pass. Bloom is added in
            // linear light before exposure, exactly as it is there.
            vec3 hdr = resolved.rgb * ambientOcclusion
                + bloom * PostProcessParameters.y * PostProcessParameters.w;

            if (EnvironmentParameters.w > 0.5)
            {
                hdr *= 1.0 + lensDirtMask(fsin_UV) * EnvironmentParameters.w * 0.6;
            }

            hdr *= exp2(EnvironmentParameters.x);
            // 11.2 is the conventional neutral scene-white reference; an authored white point
            // moves highlight placement without crushing midtones.
            hdr *= 11.2 / max(EnvironmentParameters.y, 0.0001);

            // EnvironmentParameters.z selects AgX; anything else keeps the exponential curve
            // this path used before, so scenes that never asked for AgX are unchanged.
            vec3 graded = EnvironmentParameters.z > 0.5
                ? agxCurve(hdr)
                : vec3(1.0) - exp(-max(hdr, vec3(0.0)) * 1.15);

            vec3 color = pow(max(graded, vec3(0.0)), vec3(1.0 / 2.2));
            fsout_Color = vec4(clamp(color, 0.0, 1.0), resolved.a);
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

/// <summary>
/// Diagnostic screenshot request for the interactive player, set from the command line.
///
/// Kept as a small holder rather than threaded through the session factory and player
/// constructor: it is a diagnostic hook, not part of the player's authored configuration.
/// </summary>
internal static class RekallAgePlayerScreenshotRequest
{
    public static string? Path { get; set; }

    public static int Frame { get; set; } = 1;
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

