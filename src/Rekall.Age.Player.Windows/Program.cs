using System.Diagnostics;
using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.Windows;
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
using Veldrid.StartupUtilities;

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
    private const int PlayableWidth = 960;
    private const int PlayableHeight = 540;
    public static int DefaultSceneSupersampleFactor =>
        RekallAgeInteractiveAntialiasing.DefaultSupersampleFactor;
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
    private readonly RekallAgeVeldridVulkanPresentationSession _presentationSession;
    private readonly RekallAgeRuntimeExecutionLoop _runtimeLoop;
    private readonly RekallAgeSdlControllerInput _controllerInput = new();
    private readonly RekallAgeRuntimeSimulationClock _simulationClock;
    private readonly RekallAgeSdlAudioOutput? _audioOutput;
    private readonly RekallAgeRuntimeRenderFrameBuilder _frameBuilder = new();
    private RekallAgeRuntimeViewportAssetSet _assets;
    private int _entityCount;
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
    private readonly int _openXrEyeWidth;
    private readonly int _openXrEyeHeight;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly RekallAgeInteractiveQualityFrameResolver _interactiveQualityResolver = new();
    private int _frameIndex;
    private double _lastPlayableTickSeconds;
    private Rekall.Age.Runtime.Abstractions.RekallAgeRuntimeWorld _runtimeWorld;
    private Vector2 _lastMousePosition;
    private Vector2 _previousMousePosition;
    private bool _hasMousePosition;
    private bool _mouseCaptured;
    private readonly RekallAgeWindowsInputBridge _inputBridge = new();
    private int _sceneRevision = 1;
    private int _assetRevision = 1;
    private int _assetHotReloadPending;
    private long _lastAssetHotReloadRequestTicks;
    private int _shaderHotReloadPending;
    private long _lastShaderHotReloadRequestTicks;
    private RekallAgeSceneDocument _sceneDocument;
    private readonly object _runtimeInputGate = new();
    private RekallAgeRuntimeInputState _latestRuntimeInput = RekallAgeRuntimeInputState.Empty;
    private long _runtimeInputSequence;
    private long _openXrLastConsumedInputSequence;
    private CancellationTokenSource? _openXrSubmitCts;
    private Task? _openXrSubmitTask;
    private bool _audioSubmissionLogged;

    public bool AudioOutputAvailable => _audioOutput is not null;

    public int AudioSubmittedFrameCount => _audioOutput?.SubmittedFrameCount ?? 0;

    private RekallAgeVeldridPlayer(
        string projectRoot,
        string sceneName,
        bool playableMode,
        IRekallAgePlayableGame? playableGame,
        RekallAgeSceneDocument sceneDocument,
        Sdl2Window window,
        RekallAgeVeldridVulkanPresentationSession presentationSession,
        Rekall.Age.Runtime.Abstractions.RekallAgeRuntimeWorld runtimeWorld,
        RekallAgeRuntimeExecutionLoop runtimeLoop,
        RekallAgeRuntimeViewportAssetSet assets,
        int entityCount,
        RekallAgeOpenXrSessionBootstrapResult? openXrStatus,
        RekallAgeOpenXrVulkanInteropInspection? openXrVulkanInterop,
        RekallAgeOpenXrCompositorSessionBootstrapResult? openXrCompositorSession,
        bool simulateXrInput,
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
        _presentationSession = presentationSession;
        _runtimeWorld = runtimeWorld;
        _runtimeLoop = runtimeLoop;
        _simulationClock = new RekallAgeRuntimeSimulationClock(_runtimeLoop, _clock.Elapsed);
        _audioOutput = RekallAgeSdlAudioOutput.TryCreate(out var audioStatus);
        PlayerLog.Write(audioStatus);
        _assets = assets;
        _entityCount = entityCount;
        _openXrStatus = openXrStatus;
        _openXrVulkanInterop = openXrVulkanInterop;
        _openXrCompositorSession = openXrCompositorSession;
        _simulateXrInput = simulateXrInput;
        _debugHudEnabled = debugHudEnabled;
        LoadPersistentState();
        _screenshotPath = RekallAgePlayerScreenshotRequest.Path;
        _screenshotFrame = Math.Max(1, RekallAgePlayerScreenshotRequest.Frame);
        _openXrEyeWidth = Math.Clamp(openXrEyeWidth, 64, RekallAgeOpenXrHeadsetSubmitPlanner.MaxSceneEyeExtent);
        _openXrEyeHeight = Math.Clamp(openXrEyeHeight, 64, RekallAgeOpenXrHeadsetSubmitPlanner.MaxSceneEyeExtent);
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
        var initialAuthoredQuality = world.Subsystems.Rendering.QualityProfiles
            .OrderBy(profile => profile.EntityName, StringComparer.Ordinal)
            .ThenBy(profile => profile.EntityId, StringComparer.Ordinal)
            .Select(profile => profile.Intent)
            .FirstOrDefault();
        baseFrame = new RekallAgeInteractiveQualityFrameResolver().Resolve(
            baseFrame,
            initialAuthoredQuality,
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("veldrid-vulkan"));
        var swapchainSource = VeldridStartup.GetSwapchainSource(window);
        var presentationSession = new RekallAgeVeldridVulkanPresentationSession(
            swapchainSource,
            InitialWidth,
            InitialHeight,
            new RekallAgeVulkanPresentationOptions(
                projectRoot,
                syncToVerticalBlank,
                sceneSupersampleFactor,
                debugHudEnabled,
                PlayerLog.Write),
            baseFrame,
            assets,
            initialAssetRevision: 1);
        var openXrVulkanInterop = InspectOpenXrVulkanInterop(
            presentationSession.DeviceInfo,
            openXrStatus);
        RekallAgeOpenXrCompositorSessionBootstrapResult? openXrCompositorSession = null;
        if (probeOpenXrCompositor)
        {
            PlayerLog.Write("OpenXR compositor probe enabled.");
            openXrCompositorSession = await BootstrapOpenXrCompositorSessionAsync(
                    presentationSession.DeviceInfo,
                    openXrVulkanInterop,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else if (openXrRequested)
        {
            PlayerLog.Write("OpenXR compositor probe skipped; windowed player will drive headset submission when the HMD session is ready.");
        }
        var player = new RekallAgeVeldridPlayer(
            projectRoot,
            sceneName,
            playableMode,
            playableGame,
            scene,
            window,
            presentationSession,
            world,
            runtimeLoop,
            assets,
            entityCount,
            openXrStatus,
            openXrVulkanInterop,
            openXrCompositorSession,
            simulateXrInput,
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
        RekallAgeVulkanNativeDeviceInfo device,
        RekallAgeOpenXrSessionBootstrapResult? openXrStatus)
    {
        if (openXrStatus is null)
        {
            return null;
        }

        var vulkan = new RekallAgeOpenXrVulkanDeviceInteropInfo(
            device.Backend,
            device.Instance,
            device.PhysicalDevice,
            device.Device,
            device.GraphicsQueue,
            device.GraphicsQueueFamilyIndex,
            ExternalTextureWrappingSupported: true,
            device.DriverName,
            device.DriverInfo);

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
        RekallAgeVulkanNativeDeviceInfo device,
        RekallAgeOpenXrVulkanInteropInspection? inspection,
        CancellationToken cancellationToken)
    {
        if (inspection is not { ReadyForXrGraphicsBinding: true })
        {
            return null;
        }

        var vulkan = new RekallAgeOpenXrVulkanDeviceInteropInfo(
            device.Backend,
            device.Instance,
            device.PhysicalDevice,
            device.Device,
            device.GraphicsQueue,
            device.GraphicsQueueFamilyIndex,
            ExternalTextureWrappingSupported: true,
            device.DriverName,
            device.DriverInfo,
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
        try
        {
            var pixels = _presentationSession
                .CapturePresentedRgbaAsync(CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            RekallAgePngWriter
                .WriteRgbaAsync(path, pixels.Width, pixels.Height, pixels.Rgba.ToArray(), CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            PlayerLog.Write($"Wrote player screenshot {path} ({pixels.Width}x{pixels.Height}).");
        }
        catch (Exception exception)
        {
            PlayerLog.Write($"Player screenshot failed: {exception.Message}");
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

        try
        {
            var presentationStarted = Stopwatch.GetTimestamp();
            await _presentationSession.DisposeAsync().ConfigureAwait(false);
            var presentationElapsed = Stopwatch.GetElapsedTime(presentationStarted);
            if (presentationElapsed >= TimeSpan.FromMilliseconds(100))
            {
                PlayerLog.Write($"Player cleanup slow target=presentation-session elapsedMs={presentationElapsed.TotalMilliseconds:F0}.");
            }
        }
        catch (Exception exception)
        {
            PlayerLog.Write($"Player cleanup issue target=presentation-session: {exception.Message}");
        }

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
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("veldrid-vulkan"));
        var assets = new RekallAgeRuntimeViewportAssetResolver()
            .ResolveAsync(_projectRoot, frame, CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();

        _assets = assets;
        _assetRevision++;
        _presentationSession
            .InvalidateAssetsAsync(CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
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

        ProcessLiveEditQueue();
        ProcessAssetHotReload();
        ProcessShaderHotReload();
        Interlocked.Increment(ref _frameIndex);
        AdvanceSimulationToWallClock();
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
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("veldrid-vulkan"));
        var submission = new RekallAgeVulkanSceneSubmission(
            frame,
            _assets,
            _runtimeWorld.Subsystems.Rendering.GpuWorkloads,
            _sceneRevision,
            _assetRevision,
            BuildDebugBackendText());
        _presentationSession.PresentAsync(submission, CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
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
        var raster = _playableRasterizer.Rasterize(renderFrame, PlayableWidth, PlayableHeight);
        var uiFrame = _frameBuilder.Build(
            _runtimeWorld,
            Math.Max(1, _window.Width),
            Math.Max(1, _window.Height),
            debugOverlay: false);
        var sceneSubmission = new RekallAgeVulkanSceneSubmission(
            uiFrame,
            _assets,
            _runtimeWorld.Subsystems.Rendering.GpuWorkloads,
            _sceneRevision,
            _assetRevision,
            BuildDebugBackendText());
        _presentationSession
            .PresentRgbaAsync(
                new RekallAgeVulkanPixelSubmission(
                    PlayableWidth,
                    PlayableHeight,
                    raster.Pixels,
                    sceneSubmission),
                CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    private string? BuildDebugBackendText()
    {
        var suffixes = new List<string>();
        if (_simulateXrInput)
        {
            suffixes.Add("XR SIM");
        }

        if (_openXrStatus is not null)
        {
            suffixes.Add(_openXrStatus.HeadsetSessionReady ? "OXR READY" : "OXR WAIT");
        }

        if (_openXrVulkanInterop is not null)
        {
            suffixes.Add(_openXrVulkanInterop.ReadyForCompositorSession ? "CMP READY" : "CMP WAIT");
        }

        if (_openXrCompositorSession is not null)
        {
            suffixes.Add(_openXrCompositorSession.FrameLoopReady ? "SES READY" : "SES WAIT");
        }

        return suffixes.Count == 0 ? null : string.Join(' ', suffixes);
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

        _presentationSession
            .InvalidateShadersAsync(CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
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

