using Rekall.Age.Core.Commands;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;
using System.Globalization;
using System.Numerics;

namespace Rekall.Age.Rendering.Commands;

public sealed record CaptureRuntimeViewportRequest(
    string ProjectRoot,
    string SceneName,
    int Frames,
    string OutputDirectory,
    int Width = 320,
    int Height = 180,
    bool DebugOverlay = true,
    string BackendId = "software",
    string? PreferredDeviceType = "discrete-gpu",
    IReadOnlyList<RekallAgeRuntimeInputFrame>? Inputs = null)
{
    public string? QualityPreset { get; init; }

    public RekallAgeRenderQualityOverrides? QualityOverrides { get; init; }

    public bool IncludeGpuTimings { get; init; }
}

public sealed record CaptureRuntimeViewportResult(
    bool Captured,
    string ScreenshotPath,
    bool NonBlank,
    int Width,
    int Height,
    int FrameIndex,
    string? ActiveCamera,
    int RenderableCount,
    IReadOnlyList<string> RenderableKinds,
    int CulledRenderableCount,
    IReadOnlyList<CaptureRuntimeViewportCulledRenderable> CulledRenderables,
    int ObservationCount,
    IReadOnlyList<string> ObservationCodes,
    int AssetBackedRenderableCount,
    int FallbackRenderableCount,
    int MissingAssetCount,
    int UnsupportedAssetCount,
    IReadOnlyList<string> AssetIssueCodes,
    string BackendId,
    bool HardwareAccelerated,
    string AccelerationStatus,
    string? SelectedDeviceName,
    RekallAgeViewportFrameAnalysis FrameAnalysis,
    CaptureRuntimeViewportLayoutDiagnostics LayoutDiagnostics)
{
    public IReadOnlyList<RekallAgeRuntimeInputAction> InputActions { get; init; } =
        Array.Empty<RekallAgeRuntimeInputAction>();

    public double ElapsedSeconds { get; init; }

    public RekallAgeResolvedRenderFeaturePlan? QualityPlan { get; init; }

    public RekallAgeHighFidelityFrameReport? HighFidelityFrame { get; init; }

    public RekallAgePointLightSelectionReport? Lighting { get; init; }

    public RekallAgeGpuFrameTimingReport GpuTimings { get; init; } =
        RekallAgeGpuFrameTimingReport.Unavailable(0);

    public long ResourceBytes { get; init; }

    public int DrawCount { get; init; }

    public int DispatchCount { get; init; }

    public IReadOnlyList<string> SuggestedCommands { get; init; } = Array.Empty<string>();
}

public sealed record CaptureRuntimeViewportCulledRenderable(
    string EntityId,
    string EntityName,
    string Kind,
    string Layer,
    string Reason,
    string? CameraEntityName,
    string CullingMask);

public sealed record CaptureRuntimeViewportLayoutDiagnostics(
    bool Analyzed,
    CaptureRuntimeViewportCameraDiagnostics? ActiveCamera,
    CaptureRuntimeViewportWorldBounds WorldBounds,
    IReadOnlyList<string> WarningCodes,
    IReadOnlyList<string> AuthoringHints);

public sealed record CaptureRuntimeViewportCameraDiagnostics(
    string EntityId,
    string EntityName,
    string Kind,
    string ProjectionMode,
    CaptureRuntimeViewportPixelRect PixelRect,
    double X,
    double Y,
    double Z,
    double RotationX,
    double RotationY,
    double RotationZ,
    double FieldOfViewDegrees,
    double OrthographicSize,
    string CullingMask);

public sealed record CaptureRuntimeViewportPixelRect(
    int X,
    int Y,
    int Width,
    int Height);

public sealed record CaptureRuntimeViewportWorldBounds(
    int SpatialRenderableCount,
    double MinX,
    double MaxX,
    double SpanX,
    double MinY,
    double MaxY,
    double SpanY,
    double MinZ,
    double MaxZ,
    double SpanZ,
    double MaxScaleX,
    double MaxScaleY,
    double MaxScaleZ);

public sealed class CaptureRuntimeViewportCommand
    : IRekallAgeCommand<CaptureRuntimeViewportRequest, CaptureRuntimeViewportResult>
{
    private readonly IRekallAgeVulkanRenderPassCapture _vulkanCapture;
    private readonly IRekallAgeVulkanSceneCapture _vulkanSceneCapture;

    public CaptureRuntimeViewportCommand()
        : this(new RekallAgeNativeVulkanRenderPassSubmission())
    {
    }

    public CaptureRuntimeViewportCommand(IRekallAgeVulkanRenderPassCapture vulkanCapture)
        : this(vulkanCapture, new RekallAgeNativeVulkanSceneCapture(vulkanCapture))
    {
    }

    public CaptureRuntimeViewportCommand(
        IRekallAgeVulkanRenderPassCapture vulkanCapture,
        IRekallAgeVulkanSceneCapture vulkanSceneCapture)
    {
        _vulkanCapture = vulkanCapture;
        _vulkanSceneCapture = vulkanSceneCapture;
    }

    public string Name => "rekall.render.capture_runtime_viewport";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Captures a deterministic runtime viewport frame for a scene.",
        typeof(CaptureRuntimeViewportRequest).FullName!,
        typeof(CaptureRuntimeViewportResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CaptureRuntimeViewportResult>> ExecuteAsync(
        CaptureRuntimeViewportRequest request,
        RekallAgeCommandContext context)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return RekallAgeCommandResult<CaptureRuntimeViewportResult>.Failure(
                Empty(request),
                "Runtime viewport capture requires a non-negative frame count and positive dimensions.",
                errors);
        }

        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            request.ProjectRoot,
            request.SceneName,
            request.Frames,
            request.Inputs,
            context.CancellationToken);
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(
            world,
            request.Width,
            request.Height,
            request.DebugOverlay);
        frame = ApplyQualityPlan(frame, world, request);
        var assets = await new RekallAgeRuntimeViewportAssetResolver().ResolveAsync(
            request.ProjectRoot,
            frame,
            context.CancellationToken);
        var backendId = NormalizeBackendId(request.BackendId);
        if (backendId.Equals("auto", StringComparison.Ordinal))
        {
            var probe = await new RekallAgeNativeVulkanBackendProbe().ProbeAsync(context.CancellationToken);
            backendId = probe.Available ? "vulkan" : "software";
        }

        if (backendId.Equals("vulkan", StringComparison.Ordinal))
        {
            return await CaptureVulkanViewportAsync(
                request,
                context,
                frame,
                assets,
                world.Subsystems.Input.Actions,
                world.ElapsedTime.TotalSeconds);
        }

        var capture = await new RekallAgeRuntimeSoftwareRenderer().CaptureAsync(
            frame,
            request.OutputDirectory,
            $"{world.SceneName}_runtime_{world.FrameIndex:000}.png",
            assets,
            context.CancellationToken);
        var frameAnalysis = await AnalyzeCaptureAsync(capture.Captured, capture.ScreenshotPath, context.CancellationToken);
        var result = new CaptureRuntimeViewportResult(
            capture.Captured,
            capture.ScreenshotPath,
            capture.NonBlank,
            capture.Width,
            capture.Height,
            capture.FrameIndex,
            capture.ActiveCamera,
            capture.RenderableCount,
            frame.Renderables
                .Select(renderable => renderable.Kind)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(kind => kind, StringComparer.Ordinal)
                .ToArray(),
            frame.Culling.CulledRenderableCount,
            BuildCulledRenderables(frame),
            capture.ObservationCount,
            frame.Observations
                .Select(observation => observation.Code)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(code => code, StringComparer.Ordinal)
                .ToArray(),
            capture.AssetBackedRenderableCount,
            capture.FallbackRenderableCount,
            capture.MissingAssetCount,
            capture.UnsupportedAssetCount,
            capture.AssetIssueCodes,
            "software",
            false,
            "software-rasterized",
            null,
            frameAnalysis,
            BuildLayoutDiagnostics(frame, assets))
        {
            InputActions = world.Subsystems.Input.Actions,
            ElapsedSeconds = world.ElapsedTime.TotalSeconds,
            QualityPlan = frame.ResolvedQualityPlan,
            GpuTimings = RekallAgeGpuFrameTimingReport.Unavailable(frame.FrameIndex),
            ResourceBytes = EstimatedResourceBytes(frame.ResolvedQualityPlan),
            DrawCount = frame.Renderables.Count,
            SuggestedCommands = BuildSuggestedCommands(request)
        };

        context.Transaction.RecordChangedResource(capture.ScreenshotPath);

        // The software rasterizer cannot execute post processing, atmospheric scattering or
        // tone mapping. Say so when the scene actually declares them, rather than returning a
        // flat image that looks like the scene is wrong.
        var droppedFeatures = SoftwareBackendUnsupportedFeatures(frame);
        var summary = droppedFeatures.Count == 0
            ? $"Captured runtime viewport for scene '{request.SceneName}' at frame {result.FrameIndex} on the software backend."
            : $"Captured runtime viewport for scene '{request.SceneName}' at frame {result.FrameIndex} on the software backend, "
              + $"which cannot render {string.Join(", ", droppedFeatures)}. Re-run with backend 'vulkan' to see them.";

        return RekallAgeCommandResult<CaptureRuntimeViewportResult>.Success(result, summary);
    }

    private static IReadOnlyList<string> SoftwareBackendUnsupportedFeatures(
        Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportFrame frame)
    {
        var features = new List<string>();
        if (frame.PostProcessStack is { Enabled: true, Passes.Count: > 0 })
        {
            features.Add("the authored post-process stack");
        }

        if (frame.Renderables.Any(renderable => renderable.Atmosphere is not null))
        {
            features.Add("atmospheric scattering");
        }

        if (frame.Environment is { } environment
            && !string.Equals(environment.ToneMapper, "linear", StringComparison.OrdinalIgnoreCase))
        {
            features.Add($"{environment.ToneMapper} tone mapping");
        }

        return features;
    }

    private async ValueTask<RekallAgeCommandResult<CaptureRuntimeViewportResult>> CaptureVulkanViewportAsync(
        CaptureRuntimeViewportRequest request,
        RekallAgeCommandContext context,
        Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportAssetSet assets,
        IReadOnlyList<RekallAgeRuntimeInputAction> inputActions,
        double elapsedSeconds)
    {
        if (frame.Renderables.Count == 0)
        {
            return await CaptureVulkanClearViewportAsync(request, context, frame, inputActions, elapsedSeconds);
        }

        var capture = await _vulkanSceneCapture.CaptureProjectSceneAsync(
            request.ProjectRoot,
            frame,
            assets,
            request.OutputDirectory,
            request.PreferredDeviceType,
            context.CancellationToken);
        var frameAnalysis = await AnalyzeCaptureAsync(capture.Captured, capture.OutputPath, context.CancellationToken);
        var result = new CaptureRuntimeViewportResult(
            capture.Captured,
            capture.OutputPath,
            capture.NonZeroBytes > 0,
            checked((int)capture.Width),
            checked((int)capture.Height),
            frame.FrameIndex,
            frame.ActiveCamera?.EntityName,
            frame.Renderables.Count,
            frame.Renderables
                .Select(renderable => renderable.Kind)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(kind => kind, StringComparer.Ordinal)
                .ToArray(),
            frame.Culling.CulledRenderableCount,
            BuildCulledRenderables(frame),
            frame.Observations.Count,
            frame.Observations
                .Select(observation => observation.Code)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(code => code, StringComparer.Ordinal)
                .ToArray(),
            capture.SpriteCount,
            0,
            assets.Issues.Count(issue =>
                issue.Code.Equals("REKALL_RENDER_ASSET_MISSING", StringComparison.Ordinal)
                || issue.Code.Equals("REKALL_RENDER_FONT_MISSING", StringComparison.Ordinal)),
            assets.Issues.Count(issue =>
                issue.Code.Equals("REKALL_RENDER_ASSET_UNSUPPORTED", StringComparison.Ordinal)
                || issue.Code.Equals("REKALL_RENDER_FONT_UNSUPPORTED", StringComparison.Ordinal)),
            assets.Issues
                .Select(issue => issue.Code)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(code => code, StringComparer.Ordinal)
                .ToArray(),
            "vulkan",
            capture.Captured && capture.SelectedDevice is not null,
            capture.Captured ? "vulkan-scene-rendered" : "vulkan-scene-failed",
            capture.SelectedDevice?.Name,
            frameAnalysis,
            BuildLayoutDiagnostics(frame, assets))
        {
            InputActions = inputActions,
            ElapsedSeconds = elapsedSeconds,
            QualityPlan = frame.ResolvedQualityPlan,
            HighFidelityFrame = capture.HighFidelityFrame,
            Lighting = capture.HighFidelityFrame?.Lighting,
            GpuTimings = capture.HighFidelityFrame?.GpuTimings
                ?? RekallAgeGpuFrameTimingReport.Unavailable(frame.FrameIndex),
            ResourceBytes = capture.HighFidelityFrame?.ResourceBytes
                ?? EstimatedResourceBytes(frame.ResolvedQualityPlan),
            DrawCount = capture.HighFidelityFrame?.DrawCount ?? capture.DrawCallCount,
            DispatchCount = capture.HighFidelityFrame?.DispatchCount ?? 0,
            SuggestedCommands = BuildSuggestedCommands(request)
        };

        if (capture.Captured)
        {
            context.Transaction.RecordChangedResource(capture.OutputPath);
            return RekallAgeCommandResult<CaptureRuntimeViewportResult>.Success(
                result,
                $"Captured Vulkan runtime viewport scene for scene '{request.SceneName}' at frame {result.FrameIndex}.");
        }

        var code = capture.UnsupportedRenderableCount > 0
            ? "REKALL_RUNTIME_VIEWPORT_VULKAN_RENDERABLE_UNSUPPORTED"
            : "REKALL_RUNTIME_VIEWPORT_VULKAN_SCENE_FAILED";
        var message = capture.Errors.Count == 0
            ? "Vulkan runtime viewport scene capture failed."
            : string.Join(" ", capture.Errors);
        var error = new RekallAgeCommandError(code, message, request.SceneName);
        return RekallAgeCommandResult<CaptureRuntimeViewportResult>.Failure(result, error.Message, [error]);
    }

    private async ValueTask<RekallAgeCommandResult<CaptureRuntimeViewportResult>> CaptureVulkanClearViewportAsync(
        CaptureRuntimeViewportRequest request,
        RekallAgeCommandContext context,
        Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportFrame frame,
        IReadOnlyList<RekallAgeRuntimeInputAction> inputActions,
        double elapsedSeconds)
    {

        var capture = await _vulkanCapture.CaptureClearRenderPassAsync(
            checked((uint)request.Width),
            checked((uint)request.Height),
            "R8G8B8A8_UNorm",
            request.PreferredDeviceType,
            request.OutputDirectory,
            ParseClearColor(frame.ActiveCamera?.ClearColor),
            context.CancellationToken);
        var frameAnalysis = await AnalyzeCaptureAsync(capture.Captured, capture.OutputPath, context.CancellationToken);
        var result = new CaptureRuntimeViewportResult(
            capture.Captured,
            capture.OutputPath,
            capture.NonZeroBytes > 0,
            checked((int)capture.Width),
            checked((int)capture.Height),
            frame.FrameIndex,
            frame.ActiveCamera?.EntityName,
            frame.Renderables.Count,
            Array.Empty<string>(),
            frame.Culling.CulledRenderableCount,
            BuildCulledRenderables(frame),
            frame.Observations.Count,
            frame.Observations
                .Select(observation => observation.Code)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(code => code, StringComparer.Ordinal)
                .ToArray(),
            0,
            0,
            0,
            0,
            Array.Empty<string>(),
            "vulkan",
            capture.Captured && capture.SelectedDevice is not null,
            capture.Captured ? "vulkan-clear-pass" : "vulkan-unavailable",
            capture.SelectedDevice?.Name,
            frameAnalysis,
            BuildLayoutDiagnostics(frame, RekallAgeRuntimeViewportAssetSet.Empty))
        {
            InputActions = inputActions,
            ElapsedSeconds = elapsedSeconds,
            QualityPlan = frame.ResolvedQualityPlan,
            GpuTimings = RekallAgeGpuFrameTimingReport.Unavailable(frame.FrameIndex),
            ResourceBytes = EstimatedResourceBytes(frame.ResolvedQualityPlan),
            SuggestedCommands = BuildSuggestedCommands(request)
        };

        if (capture.Captured)
        {
            context.Transaction.RecordChangedResource(capture.OutputPath);
            return RekallAgeCommandResult<CaptureRuntimeViewportResult>.Success(
                result,
                $"Captured Vulkan runtime viewport clear pass for scene '{request.SceneName}' at frame {result.FrameIndex}.");
        }

        var error = new RekallAgeCommandError(
            "REKALL_RUNTIME_VIEWPORT_VULKAN_UNAVAILABLE",
            capture.Errors.Count == 0
                ? "Vulkan runtime viewport capture failed."
                : string.Join(" ", capture.Errors),
            request.SceneName);
        return RekallAgeCommandResult<CaptureRuntimeViewportResult>.Failure(result, error.Message, [error]);
    }

    private static IReadOnlyList<RekallAgeCommandError> Validate(CaptureRuntimeViewportRequest request)
    {
        var errors = new List<RekallAgeCommandError>();
        if (request.Frames < 0)
        {
            errors.Add(new RekallAgeCommandError(
                "REKALL_RUNTIME_VIEWPORT_INVALID_REQUEST",
                "Frame count cannot be negative.",
                request.SceneName));
        }

        if (request.Width <= 0)
        {
            errors.Add(new RekallAgeCommandError(
                "REKALL_RUNTIME_VIEWPORT_INVALID_REQUEST",
                "Viewport width must be greater than zero.",
                request.SceneName));
        }

        if (request.Height <= 0)
        {
            errors.Add(new RekallAgeCommandError(
                "REKALL_RUNTIME_VIEWPORT_INVALID_REQUEST",
                "Viewport height must be greater than zero.",
                request.SceneName));
        }

        var backendId = NormalizeBackendId(request.BackendId);
        if (backendId is not "software" and not "vulkan" and not "auto")
        {
            errors.Add(new RekallAgeCommandError(
                "REKALL_RUNTIME_VIEWPORT_BACKEND_UNSUPPORTED",
                "Runtime viewport backend must be 'auto', 'software' or 'vulkan'.",
                request.BackendId));
        }

        return errors;
    }

    private static RekallAgeRuntimeViewportFrame ApplyQualityPlan(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeWorld world,
        CaptureRuntimeViewportRequest request) => ApplyQualityPlan(
            frame,
            world,
            request.QualityPreset,
            request.QualityOverrides,
            request.IncludeGpuTimings,
            request.BackendId);

    internal static RekallAgeRuntimeViewportFrame ApplyQualityPlan(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeWorld world,
        string? qualityPreset,
        RekallAgeRenderQualityOverrides? overrides,
        bool includeGpuTimings,
        string backendId)
    {
        var bounded = BoundOverrides(overrides);
        overrides = bounded.Overrides;
        var authored = world.Subsystems.Rendering.QualityProfiles
            .OrderBy(profile => profile.EntityName, StringComparer.Ordinal)
            .ThenBy(profile => profile.EntityId, StringComparer.Ordinal)
            .Select(profile => profile.Intent)
            .FirstOrDefault()
            ?? new RekallAgeRenderQualityIntent();
        var intent = new RekallAgeRenderQualityIntent(
            Preset: string.IsNullOrWhiteSpace(qualityPreset) ? authored.Preset : qualityPreset,
            ResolutionScale: overrides?.ResolutionScale ?? authored.ResolutionScale,
            ShadowCascadeCount: overrides?.ShadowCascadeCount ?? authored.ShadowCascadeCount,
            ShadowResolution: overrides?.ShadowResolution ?? authored.ShadowResolution,
            FogMode: overrides?.FogMode ?? authored.FogMode,
            Bloom: overrides?.Bloom ?? authored.Bloom,
            Ssao: overrides?.Ssao ?? authored.Ssao,
            MaximumActiveParticles: overrides?.MaximumActiveParticles ?? authored.MaximumActiveParticles,
            AutomaticScaling: authored.AutomaticScaling,
            TargetFramesPerSecond: authored.TargetFramesPerSecond)
        {
            EnableGpuTimestamps = includeGpuTimings
        };
        var normalizedBackendId = NormalizeBackendId(backendId);
        var capabilities = RekallAgeRenderingDeviceCapabilities.DesktopBaseline(normalizedBackendId);
        if (!normalizedBackendId.Equals("vulkan", StringComparison.Ordinal))
        {
            capabilities = capabilities with { SupportsTimestampQueries = false };
        }

        var resolved = new RekallAgeRenderQualityProfileResolver().Resolve(
            intent,
            capabilities,
            frame.Width,
            frame.Height);
        if (bounded.Degradations.Count > 0)
        {
            resolved = resolved with
            {
                Degradations = bounded.Degradations.Concat(resolved.Degradations).ToArray()
            };
        }

        return frame with
        {
            ResolvedQualityPlan = resolved
        };
    }

    private static (
        RekallAgeRenderQualityOverrides? Overrides,
        IReadOnlyList<RekallAgeRenderFeatureDegradation> Degradations) BoundOverrides(
        RekallAgeRenderQualityOverrides? overrides)
    {
        if (overrides is null)
        {
            return (null, []);
        }

        var degradations = new List<RekallAgeRenderFeatureDegradation>();
        var resolutionScale = ClampFiniteOverride(
            overrides.ResolutionScale,
            0.25,
            2,
            "resolutionScale",
            degradations);
        var shadowCascadeCount = ClampOverride(
            overrides.ShadowCascadeCount,
            1,
            4,
            "shadowCascadeCount",
            degradations);
        var shadowResolution = ClampOverride(
            overrides.ShadowResolution,
            128,
            8_192,
            "shadowResolution",
            degradations);
        var maximumActiveParticles = ClampOverride(
            overrides.MaximumActiveParticles,
            0,
            RekallAgeVulkanParticlePlanner.MaximumGlobalCapacity,
            "maximumActiveParticles",
            degradations);
        return (
            overrides with
            {
                ResolutionScale = resolutionScale,
                ShadowCascadeCount = shadowCascadeCount,
                ShadowResolution = shadowResolution,
                MaximumActiveParticles = maximumActiveParticles
            },
            degradations);
    }

    private static double? ClampFiniteOverride(
        double? requested,
        double minimum,
        double maximum,
        string feature,
        ICollection<RekallAgeRenderFeatureDegradation> degradations)
    {
        if (!requested.HasValue || !double.IsFinite(requested.Value))
        {
            return requested;
        }

        var resolved = Math.Clamp(requested.Value, minimum, maximum);
        AddOverrideClamp(feature, requested.Value, resolved, degradations);
        return resolved;
    }

    private static int? ClampOverride(
        int? requested,
        int minimum,
        int maximum,
        string feature,
        ICollection<RekallAgeRenderFeatureDegradation> degradations)
    {
        if (!requested.HasValue)
        {
            return null;
        }

        var resolved = Math.Clamp(requested.Value, minimum, maximum);
        AddOverrideClamp(feature, requested.Value, resolved, degradations);
        return resolved;
    }

    private static void AddOverrideClamp(
        string feature,
        object requested,
        object resolved,
        ICollection<RekallAgeRenderFeatureDegradation> degradations)
    {
        if (Equals(requested, resolved))
        {
            return;
        }

        var requestedValue = ToInvariantString(requested);
        var resolvedValue = ToInvariantString(resolved);
        degradations.Add(new RekallAgeRenderFeatureDegradation(
            "REKALL_RENDER_QUALITY_OVERRIDE_CLAMPED",
            feature,
            requestedValue,
            resolvedValue,
            $"The caller-scoped {feature} override was clamped from '{requestedValue}' to bounded value '{resolvedValue}'."));
    }

    private static string ToInvariantString(object value) => value is IFormattable formattable
        ? formattable.ToString(null, CultureInfo.InvariantCulture)
        : value.ToString() ?? string.Empty;

    private static long EstimatedResourceBytes(RekallAgeResolvedRenderFeaturePlan? plan) =>
        plan is null ? 0 : checked(plan.EstimatedTransientBytes + plan.EstimatedPersistentBytes);

    /// <summary>
    /// Unspecified means "auto": prefer the Vulkan path and fall back to the software
    /// rasterizer only when no Vulkan device is available.
    ///
    /// The default used to be "software", which silently produced a flat-shaded image with no
    /// atmosphere, bloom or tone mapping. An agent capturing a frame to check its scene would
    /// draw the wrong conclusion from that and start debugging a scene that was already
    /// correct, so the default now matches what the scene actually declares.
    /// </summary>
    private static string NormalizeBackendId(string backendId)
    {
        return string.IsNullOrWhiteSpace(backendId)
            ? "auto"
            : backendId.Trim().ToLowerInvariant();
    }

    private static IReadOnlyList<CaptureRuntimeViewportCulledRenderable> BuildCulledRenderables(
        Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportFrame frame)
    {
        return frame.Culling.CulledRenderables
            .Select(renderable => new CaptureRuntimeViewportCulledRenderable(
                renderable.EntityId,
                renderable.EntityName,
                renderable.Kind,
                renderable.Layer,
                renderable.Reason,
                renderable.CameraEntityName,
                renderable.CullingMask))
            .ToArray();
    }

    private static CaptureRuntimeViewportLayoutDiagnostics BuildLayoutDiagnostics(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportAssetSet assets)
    {
        var camera = frame.ActiveCamera;
        var bounds = BuildWorldBounds(frame.Renderables);
        var hasUi = frame.Renderables.Any(renderable => renderable.UiVisual is not null);
        var warnings = new List<string>();
        var hints = new List<string>();

        if (camera is null && bounds.SpatialRenderableCount > 0)
        {
            warnings.Add("REKALL_VIEWPORT_NO_ACTIVE_CAMERA");
            hints.Add("Add or activate a generic Rekall.Camera2D or Rekall.Camera3D entity before capturing the viewport.");
        }

        if (bounds.SpatialRenderableCount == 0 && !hasUi)
        {
            warnings.Add("REKALL_VIEWPORT_NO_SPATIAL_RENDERABLES");
            hints.Add("Add visible renderable entities with generic transform and renderer components before judging composition.");
        }
        else
        {
            AddAxisDiagnostics(bounds, warnings, hints);
        }

        if (camera is not null)
        {
            AddCameraContentVisibilityDiagnostics(frame.Renderables, camera, warnings, hints);
            AddPlaneOrientationDiagnostics(frame.Renderables, camera, warnings, hints);
        }

        AddUiDiagnostics(frame, assets, warnings, hints);

        return new CaptureRuntimeViewportLayoutDiagnostics(
            true,
            camera is null ? null : BuildCameraDiagnostics(frame, camera),
            bounds,
            warnings.Distinct(StringComparer.Ordinal).ToArray(),
            hints.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static CaptureRuntimeViewportCameraDiagnostics BuildCameraDiagnostics(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportCamera camera)
    {
        var rect = RekallAgeRuntimeViewportCameraRect.FromCamera(frame.Width, frame.Height, camera);
        return new CaptureRuntimeViewportCameraDiagnostics(
            camera.EntityId,
            camera.EntityName,
            camera.Kind,
            camera.ProjectionMode,
            new CaptureRuntimeViewportPixelRect(rect.X, rect.Y, rect.Width, rect.Height),
            camera.X,
            camera.Y,
            camera.Z,
            camera.RotationX,
            camera.RotationY,
            camera.RotationZ,
            camera.FieldOfViewDegrees,
            camera.OrthographicSize,
            camera.CullingMask);
    }

    private static CaptureRuntimeViewportWorldBounds BuildWorldBounds(
        IReadOnlyList<RekallAgeRuntimeViewportRenderable> renderables)
    {
        var spatial = renderables
            .Where(renderable => !renderable.Kind.Equals("light", StringComparison.Ordinal)
                && renderable.UiVisual is null)
            .ToArray();
        if (spatial.Length == 0)
        {
            return EmptyWorldBounds();
        }

        var minX = spatial.Min(renderable => renderable.X - Math.Abs(renderable.ScaleX) * 0.5);
        var maxX = spatial.Max(renderable => renderable.X + Math.Abs(renderable.ScaleX) * 0.5);
        var minY = spatial.Min(renderable => renderable.Y - Math.Abs(renderable.ScaleY) * 0.5);
        var maxY = spatial.Max(renderable => renderable.Y + Math.Abs(renderable.ScaleY) * 0.5);
        var minZ = spatial.Min(renderable => renderable.Z - Math.Abs(renderable.ScaleZ) * 0.5);
        var maxZ = spatial.Max(renderable => renderable.Z + Math.Abs(renderable.ScaleZ) * 0.5);

        return new CaptureRuntimeViewportWorldBounds(
            spatial.Length,
            minX,
            maxX,
            maxX - minX,
            minY,
            maxY,
            maxY - minY,
            minZ,
            maxZ,
            maxZ - minZ,
            spatial.Max(renderable => Math.Abs(renderable.ScaleX)),
            spatial.Max(renderable => Math.Abs(renderable.ScaleY)),
            spatial.Max(renderable => Math.Abs(renderable.ScaleZ)));
    }

    private static void AddUiDiagnostics(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportAssetSet assets,
        List<string> warnings,
        List<string> hints)
    {
        const int maximumHints = 8;
        var uiRenderables = frame.Renderables.Where(item => item.UiVisual is not null).ToArray();
        foreach (var renderable in uiRenderables)
        {
            var visual = renderable.UiVisual!;
            var visibleWidth = IntersectionLength(
                visual.X,
                visual.X + visual.Width,
                Math.Max(0, visual.ClipX),
                Math.Min(frame.Width, visual.ClipX + visual.ClipWidth));
            var visibleHeight = IntersectionLength(
                visual.Y,
                visual.Y + visual.Height,
                Math.Max(0, visual.ClipY),
                Math.Min(frame.Height, visual.ClipY + visual.ClipHeight));
            var elementArea = (long)visual.Width * visual.Height;
            var visibleArea = (long)visibleWidth * visibleHeight;
            if (elementArea > 0 && visibleArea * 2 < elementArea)
            {
                warnings.Add("REKALL_VIEWPORT_UI_ELEMENT_SEVERELY_CLIPPED");
                if (hints.Count < maximumHints)
                {
                    var percent = (int)Math.Round(visibleArea * 100d / elementArea);
                    hints.Add($"UI element '{renderable.EntityName}' is only {percent}% visible because of parent clipping; resize or reposition the element or its parent, then recapture.");
                }
            }

            if (!string.IsNullOrEmpty(visual.Text))
            {
                var visiblePercent = CalculateVisibleTextPercent(frame, visual, ResolveFont(visual, assets));
                if (visiblePercent < 75)
                {
                    warnings.Add("REKALL_VIEWPORT_UI_TEXT_SEVERELY_CLIPPED");
                    if (hints.Count < maximumHints)
                    {
                        hints.Add($"UI text on '{renderable.EntityName}' is only {visiblePercent}% visible; enlarge its effective layout width/height or adjust parent clipping, then recapture.");
                    }
                }

                if (visiblePercent == 0)
                {
                    warnings.Add("REKALL_VIEWPORT_UI_TEXT_NOT_VISIBLE");
                    if (hints.Count < maximumHints)
                    {
                        hints.Add($"UI text on '{renderable.EntityName}' is outside its effective clip rectangle; adjust element bounds or parent clipping before accepting the proof frame.");
                    }
                }
            }
        }

        var spatialRenderableCount = frame.Renderables.Count(item =>
            item.UiVisual is null && !item.Kind.Equals("light", StringComparison.Ordinal));
        var uiCoverageArea = CalculateUiCoverageArea(frame, uiRenderables);
        var viewportArea = (long)frame.Width * frame.Height;
        if (spatialRenderableCount > 0
            && viewportArea > 0
            && uiCoverageArea * 100 >= viewportArea * 35)
        {
            warnings.Add("REKALL_VIEWPORT_UI_LARGE_COVERAGE");
            if (hints.Count < maximumHints)
            {
                var percent = (int)Math.Round(uiCoverageArea * 100d / viewportArea);
                hints.Add($"UI layout bounds cover {percent}% of a viewport that also contains world renderables. Use an intentional canvas reference width/height and compact anchored bounds so the HUD preserves the playable world view, then recapture.");
            }
        }

        var textGeometry = uiRenderables
            .Where(item => !string.IsNullOrEmpty(item.UiVisual?.Text))
            .Select(item => (item.EntityName, Geometry: CalculateVisibleTextGeometry(
                frame,
                item.UiVisual!,
                ResolveFont(item.UiVisual!, assets))))
            .Where(item => item.Geometry.VisibleArea > 0)
            .ToArray();
        for (var leftIndex = 0; leftIndex < textGeometry.Length; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < textGeometry.Length; rightIndex++)
            {
                var left = textGeometry[leftIndex];
                var right = textGeometry[rightIndex];
                var overlapWidth = IntersectionLength(
                    left.Geometry.X,
                    left.Geometry.X + left.Geometry.Width,
                    right.Geometry.X,
                    right.Geometry.X + right.Geometry.Width);
                var overlapHeight = IntersectionLength(
                    left.Geometry.Y,
                    left.Geometry.Y + left.Geometry.Height,
                    right.Geometry.Y,
                    right.Geometry.Y + right.Geometry.Height);
                var overlapArea = (long)overlapWidth * overlapHeight;
                var smallerArea = Math.Min(left.Geometry.VisibleArea, right.Geometry.VisibleArea);
                if (smallerArea == 0 || overlapArea * 5 < smallerArea)
                {
                    continue;
                }

                warnings.Add("REKALL_VIEWPORT_UI_TEXT_OVERLAP");
                if (hints.Count < maximumHints)
                {
                    var percent = (int)Math.Round(overlapArea * 100d / smallerArea);
                    hints.Add($"UI text on '{left.EntityName}' and '{right.EntityName}' overlaps by {percent}% of the smaller visible text area; separate their effective X/Y layouts or use a non-overlapping parent layout, then recapture.");
                }
            }
        }

        for (var textIndex = 0; textIndex < uiRenderables.Length; textIndex++)
        {
            var textRenderable = uiRenderables[textIndex];
            if (string.IsNullOrEmpty(textRenderable.UiVisual?.Text))
            {
                continue;
            }

            var text = CalculateVisibleTextGeometry(
                frame,
                textRenderable.UiVisual!,
                ResolveFont(textRenderable.UiVisual!, assets));
            if (text.VisibleArea == 0)
            {
                continue;
            }

            for (var laterIndex = textIndex + 1; laterIndex < uiRenderables.Length; laterIndex++)
            {
                var later = uiRenderables[laterIndex];
                var laterVisual = later.UiVisual!;
                if (!IsOpaqueUiOccluder(laterVisual))
                {
                    continue;
                }

                var occluderLeft = Math.Max(0, Math.Max(laterVisual.X, laterVisual.ClipX));
                var occluderTop = Math.Max(0, Math.Max(laterVisual.Y, laterVisual.ClipY));
                var occluderRight = Math.Min(frame.Width, Math.Min(
                    laterVisual.X + laterVisual.Width,
                    laterVisual.ClipX + laterVisual.ClipWidth));
                var occluderBottom = Math.Min(frame.Height, Math.Min(
                    laterVisual.Y + laterVisual.Height,
                    laterVisual.ClipY + laterVisual.ClipHeight));
                var overlapWidth = IntersectionLength(text.X, text.X + text.Width, occluderLeft, occluderRight);
                var overlapHeight = IntersectionLength(text.Y, text.Y + text.Height, occluderTop, occluderBottom);
                var overlapArea = (long)overlapWidth * overlapHeight;
                if (overlapArea * 2 < text.VisibleArea)
                {
                    continue;
                }

                warnings.Add("REKALL_VIEWPORT_UI_TEXT_OCCLUDED");
                if (hints.Count < maximumHints)
                {
                    var percent = (int)Math.Round(overlapArea * 100d / text.VisibleArea);
                    hints.Add($"UI text on '{textRenderable.EntityName}' is {percent}% covered by later-drawn opaque UI element '{later.EntityName}'; correct their draw order, canvas layer, or hierarchy so the background renders behind the text, then recapture.");
                }
                break;
            }
        }
    }

    private static bool IsOpaqueUiOccluder(RekallAgeRuntimeViewportUiVisual visual)
    {
        if (visual.Width <= 0 || visual.Height <= 0
            || string.IsNullOrWhiteSpace(visual.BackgroundColor)
            || visual.BackgroundColor.Equals("transparent", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var color = visual.BackgroundColor.Trim();
        return color.Length != 9 || color[0] != '#' || !color.AsSpan(1, 2).Equals("00", StringComparison.OrdinalIgnoreCase);
    }

    private static long CalculateUiCoverageArea(
        RekallAgeRuntimeViewportFrame frame,
        IReadOnlyList<RekallAgeRuntimeViewportRenderable> renderables)
    {
        var rectangles = renderables
            .Select(item => item.UiVisual!)
            .Select(visual =>
            {
                var left = Math.Max(0, Math.Max(visual.X, visual.ClipX));
                var top = Math.Max(0, Math.Max(visual.Y, visual.ClipY));
                var right = Math.Min(frame.Width, Math.Min(visual.X + visual.Width, visual.ClipX + visual.ClipWidth));
                var bottom = Math.Min(frame.Height, Math.Min(visual.Y + visual.Height, visual.ClipY + visual.ClipHeight));
                return (Left: left, Top: top, Right: right, Bottom: bottom);
            })
            .Where(rectangle => rectangle.Right > rectangle.Left && rectangle.Bottom > rectangle.Top)
            .ToArray();
        var xCoordinates = rectangles
            .SelectMany(rectangle => new[] { rectangle.Left, rectangle.Right })
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        long area = 0;
        for (var index = 0; index + 1 < xCoordinates.Length; index++)
        {
            var left = xCoordinates[index];
            var right = xCoordinates[index + 1];
            var intervals = rectangles
                .Where(rectangle => rectangle.Left < right && rectangle.Right > left)
                .Select(rectangle => (rectangle.Top, rectangle.Bottom))
                .OrderBy(interval => interval.Top)
                .ToArray();
            if (intervals.Length == 0)
            {
                continue;
            }

            var unionHeight = 0;
            var currentTop = intervals[0].Top;
            var currentBottom = intervals[0].Bottom;
            foreach (var interval in intervals.Skip(1))
            {
                if (interval.Top > currentBottom)
                {
                    unionHeight += currentBottom - currentTop;
                    currentTop = interval.Top;
                    currentBottom = interval.Bottom;
                }
                else
                {
                    currentBottom = Math.Max(currentBottom, interval.Bottom);
                }
            }
            unionHeight += currentBottom - currentTop;
            area += (long)(right - left) * unionHeight;
        }

        return area;
    }

    private static void AddPlaneOrientationDiagnostics(
        IReadOnlyList<RekallAgeRuntimeViewportRenderable> renderables,
        RekallAgeRuntimeViewportCamera camera,
        List<string> warnings,
        List<string> hints)
    {
        var cameraForward = RotateDirection(
            Vector3.UnitZ,
            camera.RotationX,
            camera.RotationY,
            camera.RotationZ);
        foreach (var renderable in renderables.Where(renderable =>
            renderable.Variant?.Equals("rekall.geometry.plane", StringComparison.OrdinalIgnoreCase) == true
            && renderable.FacingMode.Equals("world", StringComparison.OrdinalIgnoreCase)))
        {
            var normal = RotateDirection(
                Vector3.UnitY,
                renderable.RotationX,
                renderable.RotationY,
                renderable.RotationZ);
            if (Math.Abs(Vector3.Dot(normal, cameraForward)) >= 0.15f)
            {
                continue;
            }

            warnings.Add("REKALL_VIEWPORT_PLANE_EDGE_ON_TO_CAMERA");
            if (hints.Count < 8)
            {
                hints.Add($"Plane '{renderable.EntityName}' is nearly edge-on to the active camera. Rekall geometry planes lie on local XZ with a +Y normal; rotate the plane (commonly 90 degrees around X for a Z-facing backdrop) or use an appropriate camera-facing primitive, then recapture.");
            }
        }
    }

    private static void AddCameraContentVisibilityDiagnostics(
        IReadOnlyList<RekallAgeRuntimeViewportRenderable> renderables,
        RekallAgeRuntimeViewportCamera camera,
        List<string> warnings,
        List<string> hints)
    {
        if (!camera.Kind.Equals("Camera3D", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var spatial = renderables
            .Where(renderable => !renderable.Kind.Equals("light", StringComparison.Ordinal)
                && renderable.UiVisual is null)
            .ToArray();
        if (spatial.Length == 0)
        {
            return;
        }

        var forward = RotateDirection(
            Vector3.UnitZ,
            camera.RotationX,
            camera.RotationY,
            camera.RotationZ);
        var cameraPosition = new Vector3((float)camera.X, (float)camera.Y, (float)camera.Z);
        var depths = spatial.Select(renderable =>
        {
            var center = new Vector3((float)renderable.X, (float)renderable.Y, (float)renderable.Z);
            var radius = 0.5 * Math.Sqrt(
                renderable.ScaleX * renderable.ScaleX
                + renderable.ScaleY * renderable.ScaleY
                + renderable.ScaleZ * renderable.ScaleZ);
            return (Center: center, Depth: Vector3.Dot(center - cameraPosition, forward), Radius: radius);
        }).ToArray();
        var near = Math.Min(camera.NearClip, camera.FarClip);
        var far = Math.Max(camera.NearClip, camera.FarClip);
        var inDepthRange = depths
            .Where(item => item.Depth + item.Radius >= near && item.Depth - item.Radius <= far)
            .ToArray();
        if (inDepthRange.Length == 0)
        {
            if (depths.All(item => item.Depth + item.Radius < near))
            {
                warnings.Add("REKALL_VIEWPORT_CAMERA_FACES_AWAY_FROM_CONTENT");
                var centroid = depths.Aggregate(Vector3.Zero, (sum, item) => sum + item.Center) / depths.Length;
                hints.Add(
                    $"Active Camera3D '{camera.EntityName}' has no spatial content in its +Z-forward depth range. "
                    + $"Camera position=({camera.X:F2}, {camera.Y:F2}, {camera.Z:F2}), "
                    + $"forward=({forward.X:F2}, {forward.Y:F2}, {forward.Z:F2}), "
                    + $"content centroid=({centroid.X:F2}, {centroid.Y:F2}, {centroid.Z:F2}). "
                    + "A camera on +Z looking toward lower Z normally needs yaw 180; author the intended rotation, then recapture.");
            }

            return;
        }

        // Depth-along-forward alone does not prove content is inside the view cone: a camera
        // can be pitched so content sits at a valid depth while actually being well above or
        // below the frustum. Cross-check the angle between forward and the direction to each
        // in-range item against the camera's field of view before declaring visibility okay.
        var halfFovRadians = (float)(Math.Clamp(camera.FieldOfViewDegrees, 1, 179) * 0.5 * Math.PI / 180.0);
        var toleranceRadians = halfFovRadians * 1.6f;
        var withinFieldOfView = inDepthRange.Any(item =>
        {
            var toCenter = item.Center - cameraPosition;
            var distance = toCenter.Length();
            if (distance <= 0.0001f)
            {
                return true;
            }

            var angularSlack = MathF.Atan((float)(item.Radius / distance));
            var angle = MathF.Acos(Math.Clamp(Vector3.Dot(forward, toCenter / distance), -1f, 1f));
            return angle - angularSlack <= toleranceRadians;
        });
        if (withinFieldOfView)
        {
            return;
        }

        warnings.Add("REKALL_VIEWPORT_CAMERA_CONTENT_OUTSIDE_FIELD_OF_VIEW");
        var offAxisCentroid = inDepthRange.Aggregate(Vector3.Zero, (sum, item) => sum + item.Center) / inDepthRange.Length;
        hints.Add(
            $"Active Camera3D '{camera.EntityName}' has spatial content at a valid depth but outside its "
            + $"{camera.FieldOfViewDegrees:F0}-degree field of view. "
            + $"Camera position=({camera.X:F2}, {camera.Y:F2}, {camera.Z:F2}), rotation=({camera.RotationX:F2}, {camera.RotationY:F2}, {camera.RotationZ:F2}), "
            + $"forward=({forward.X:F2}, {forward.Y:F2}, {forward.Z:F2}), "
            + $"content centroid=({offAxisCentroid.X:F2}, {offAxisCentroid.Y:F2}, {offAxisCentroid.Z:F2}). "
            + "Rekall.Transform3D pitch tilts the view toward -Y as pitch increases and toward +Y as pitch decreases; "
            + "adjust pitch (and yaw) so forward points toward the content centroid, then recapture.");
    }

    private static Vector3 RotateDirection(
        Vector3 direction,
        double degreesX,
        double degreesY,
        double degreesZ)
    {
        var x = direction.X;
        var y = direction.Y;
        var z = direction.Z;
        var radians = MathF.PI / 180f;
        var cos = MathF.Cos((float)degreesX * radians);
        var sin = MathF.Sin((float)degreesX * radians);
        (y, z) = (y * cos - z * sin, y * sin + z * cos);
        cos = MathF.Cos((float)degreesY * radians);
        sin = MathF.Sin((float)degreesY * radians);
        (x, z) = (x * cos + z * sin, -x * sin + z * cos);
        cos = MathF.Cos((float)degreesZ * radians);
        sin = MathF.Sin((float)degreesZ * radians);
        (x, y) = (x * cos - y * sin, x * sin + y * cos);
        var rotated = new Vector3(x, y, z);
        return rotated.LengthSquared() <= 0.000001f ? direction : Vector3.Normalize(rotated);
    }

    private static int CalculateVisibleTextPercent(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportUiVisual visual,
        RekallAgeRuntimeFontAsset? font)
    {
        var geometry = CalculateVisibleTextGeometry(frame, visual, font);
        return geometry.FullArea == 0
            ? 100
            : (int)Math.Round(geometry.VisibleArea * 100d / geometry.FullArea);
    }

    private static UiTextGeometry CalculateVisibleTextGeometry(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportUiVisual visual,
        RekallAgeRuntimeFontAsset? font)
    {
        var layout = RekallAgeRuntimeUiTextLayoutResolver.Resolve(frame, visual, font);
        var raster = layout.Raster;
        var textWidth = raster.Width;
        var textX = layout.X;
        var textY = layout.Y;
        var clipLeft = layout.Clip.Left;
        var clipTop = layout.Clip.Top;
        var clipRight = layout.Clip.Right;
        var clipBottom = layout.Clip.Bottom;
        var textHeight = raster.Height;
        var textRight = (int)Math.Clamp((long)textX + textWidth, int.MinValue, int.MaxValue);
        var textBottom = (int)Math.Clamp((long)textY + textHeight, int.MinValue, int.MaxValue);
        var visibleWidth = IntersectionLength(textX, textRight, clipLeft, clipRight);
        var visibleHeight = IntersectionLength(textY, textBottom, clipTop, clipBottom);
        var textArea = (long)raster.FullWidth * raster.FullHeight;
        return new UiTextGeometry(
            textArea,
            Math.Max(textX, clipLeft),
            Math.Max(textY, clipTop),
            visibleWidth,
            visibleHeight);
    }

    private static RekallAgeRuntimeFontAsset? ResolveFont(
        RekallAgeRuntimeViewportUiVisual visual,
        RekallAgeRuntimeViewportAssetSet assets) =>
        visual.FontAssetId is { } fontAssetId
        && assets.Fonts.TryGetValue(fontAssetId, out var font)
            ? font
            : null;

    private sealed record UiTextGeometry(long FullArea, int X, int Y, int Width, int Height)
    {
        public long VisibleArea => (long)Width * Height;
    }

    private static int IntersectionLength(int start, int end, int clipStart, int clipEnd)
    {
        return Math.Max(0, Math.Min(end, clipEnd) - Math.Max(start, clipStart));
    }

    private static void AddAxisDiagnostics(
        CaptureRuntimeViewportWorldBounds bounds,
        List<string> warnings,
        List<string> hints)
    {
        var spans = new[]
        {
            ("X", bounds.SpanX),
            ("Y", bounds.SpanY),
            ("Z", bounds.SpanZ)
        };
        var dominant = spans.OrderByDescending(item => item.Item2).First();
        var second = spans.OrderByDescending(item => item.Item2).Skip(1).First();
        if (dominant.Item2 >= 1 && dominant.Item2 >= Math.Max(0.001, second.Item2) * 4)
        {
            warnings.Add($"REKALL_VIEWPORT_LAYOUT_{dominant.Item1}_DOMINATES");
            hints.Add($"The authored bounds are dominated by the {dominant.Item1} axis; reduce scale{dominant.Item1} or add variation on the other axes before recapturing.");
        }

        if (bounds.SpanX >= 2 && bounds.SpanY <= 0.5)
        {
            warnings.Add("REKALL_VIEWPORT_LAYOUT_FLAT_Y");
            hints.Add("The authored spatial bounds are nearly flat vertically; add vertical variation or reduce scaleX for clearer viewport composition.");
        }

        if (bounds.SpanX >= 2 && bounds.SpanZ <= 0.5)
        {
            warnings.Add("REKALL_VIEWPORT_LAYOUT_FLAT_Z");
            hints.Add("The authored spatial bounds have little depth variation; add z separation or reduce the dominant horizontal span.");
        }
    }

    private static CaptureRuntimeViewportLayoutDiagnostics EmptyLayoutDiagnostics()
    {
        return new CaptureRuntimeViewportLayoutDiagnostics(
            false,
            null,
            EmptyWorldBounds(),
            Array.Empty<string>(),
            Array.Empty<string>());
    }

    private static CaptureRuntimeViewportWorldBounds EmptyWorldBounds()
    {
        return new CaptureRuntimeViewportWorldBounds(
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0);
    }

    private static RekallAgeVulkanClearColor ParseClearColor(string? clearColor)
    {
        if (clearColor is { Length: 7 }
            && clearColor[0] == '#'
            && byte.TryParse(clearColor.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            && byte.TryParse(clearColor.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            && byte.TryParse(clearColor.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return new RekallAgeVulkanClearColor(r / 255f, g / 255f, b / 255f, 1);
        }

        return RekallAgeVulkanClearColor.Default;
    }

    private static async ValueTask<RekallAgeViewportFrameAnalysis> AnalyzeCaptureAsync(
        bool captured,
        string screenshotPath,
        CancellationToken cancellationToken)
    {
        if (!captured || string.IsNullOrWhiteSpace(screenshotPath) || !File.Exists(screenshotPath))
        {
            return RekallAgeViewportFrameAnalysis.NotAnalyzed;
        }

        var image = await RekallAgePngReader.ReadRgbaAsync(screenshotPath, cancellationToken);
        return RekallAgeViewportFrameAnalyzer.Analyze(image);
    }

    private static CaptureRuntimeViewportResult Empty(CaptureRuntimeViewportRequest request)
    {
        return new CaptureRuntimeViewportResult(
            false,
            string.Empty,
            false,
            Math.Max(0, request.Width),
            Math.Max(0, request.Height),
            Math.Max(0, request.Frames),
            null,
            0,
            Array.Empty<string>(),
            0,
            Array.Empty<CaptureRuntimeViewportCulledRenderable>(),
            0,
            Array.Empty<string>(),
            0,
            0,
            0,
            0,
            Array.Empty<string>(),
            NormalizeBackendId(request.BackendId),
            false,
            "not-captured",
            null,
            RekallAgeViewportFrameAnalysis.NotAnalyzed,
            EmptyLayoutDiagnostics())
        {
            ElapsedSeconds = 0,
            GpuTimings = RekallAgeGpuFrameTimingReport.Unavailable(Math.Max(0, request.Frames)),
            SuggestedCommands = BuildSuggestedCommands(request)
        };
    }

    private static IReadOnlyList<string> BuildSuggestedCommands(CaptureRuntimeViewportRequest request)
    {
        var projectRoot = BoundedCommandValue(request.ProjectRoot);
        var sceneName = BoundedCommandValue(request.SceneName);
        return
        [
            BoundCommand($"command execute rekall.render.compare_quality_presets with projectRoot='{projectRoot}', sceneName='{sceneName}', presets=['Performance','High'], frames={Math.Max(0, request.Frames)}, width={Math.Max(1, request.Width)}, and height={Math.Max(1, request.Height)}."),
            BoundCommand($"command execute rekall.render.performance.inspect_scene_budget with projectRoot='{projectRoot}', sceneName='{sceneName}', frames={Math.Max(0, request.Frames)}, width={Math.Max(1, request.Width)}, and height={Math.Max(1, request.Height)}.")
        ];
    }

    private static string BoundedCommandValue(string? value)
    {
        var sanitized = (value ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace("'", "''", StringComparison.Ordinal);
        return sanitized.Length <= 160 ? sanitized : sanitized[..160];
    }

    private static string BoundCommand(string command) =>
        command.Length <= 512 ? command : command[..512];
}

public sealed record CompareQualityPresetsRequest(
    string ProjectRoot,
    string SceneName,
    IReadOnlyList<string> Presets,
    int Frames = 0,
    string OutputDirectory = "QualityCaptures",
    int Width = 320,
    int Height = 180,
    string BackendId = "vulkan",
    RekallAgeRenderQualityOverrides? Overrides = null,
    bool IncludeGpuTimings = false,
    IReadOnlyList<RekallAgeRuntimeInputFrame>? Inputs = null);

public sealed record RekallAgeQualityPresetCapture(
    string RequestedPreset,
    string ResolvedPreset,
    int FrameIndex,
    string ScreenshotPath,
    bool NonBlank,
    int OutputWidth,
    int OutputHeight,
    int RenderWidth,
    int RenderHeight,
    long ResourceBytes,
    int DrawCount,
    int DispatchCount,
    IReadOnlyList<RekallAgeRenderFeatureDegradation> Degradations,
    RekallAgeGpuFrameTimingReport GpuTimings,
    RekallAgeViewportFrameAnalysis FrameAnalysis)
{
    internal RekallAgeHighFidelityFrameReport? HighFidelityFrame { get; init; }
}

public sealed record CompareQualityPresetsResult(
    string SceneName,
    int FrameIndex,
    IReadOnlyList<RekallAgeQualityPresetCapture> Captures,
    IReadOnlyList<string> NextCommands);

/// <summary>
/// Captures identical deterministic runtime input at multiple render-quality presets without editing authored content.
/// </summary>
public sealed class CompareQualityPresetsCommand
    : IRekallAgeCommand<CompareQualityPresetsRequest, CompareQualityPresetsResult>
{
    private static readonly HashSet<string> SupportedPresets = new(
        ["Performance", "Low", "Medium", "High", "Ultra", "Epic"],
        StringComparer.OrdinalIgnoreCase);
    private readonly Func<QualityPresetCaptureSession> _captureSessionFactory;

    public CompareQualityPresetsCommand()
        : this(CreateIsolatedNativeSession)
    {
    }

    internal CompareQualityPresetsCommand(CaptureRuntimeViewportCommand capture)
        : this(() => new QualityPresetCaptureSession(capture, null))
    {
    }

    private CompareQualityPresetsCommand(Func<QualityPresetCaptureSession> captureSessionFactory)
    {
        _captureSessionFactory = captureSessionFactory;
    }

    public string Name => "rekall.render.compare_quality_presets";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Captures aligned deterministic frames for bounded render-quality presets without mutating authored scene content.",
        typeof(CompareQualityPresetsRequest).FullName!,
        typeof(CompareQualityPresetsResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CompareQualityPresetsResult>> ExecuteAsync(
        CompareQualityPresetsRequest request,
        RekallAgeCommandContext context)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return RekallAgeCommandResult<CompareQualityPresetsResult>.Failure(
                EmptyComparison(request),
                "Quality preset comparison requires two to six distinct supported presets and bounded capture inputs.",
                errors);
        }

        var captures = new List<RekallAgeQualityPresetCapture>(request.Presets.Count);
        foreach (var requestedPreset in request.Presets)
        {
            using var captureSession = _captureSessionFactory();
            var canonicalPreset = SupportedPresets.Single(value =>
                value.Equals(requestedPreset.Trim(), StringComparison.OrdinalIgnoreCase));
            var outputDirectory = Path.Combine(
                request.OutputDirectory,
                canonicalPreset.ToLowerInvariant());
            var captureRequest = new CaptureRuntimeViewportRequest(
                    request.ProjectRoot,
                    request.SceneName,
                    request.Frames,
                    outputDirectory,
                    request.Width,
                    request.Height,
                    DebugOverlay: false,
                    request.BackendId,
                    Inputs: request.Inputs)
                {
                    QualityPreset = canonicalPreset,
                    QualityOverrides = request.Overrides,
                    IncludeGpuTimings = request.IncludeGpuTimings
                };
            if (request.IncludeGpuTimings
                && request.BackendId.Equals("vulkan", StringComparison.OrdinalIgnoreCase))
            {
                var warmupRequest = captureRequest with
                {
                    Frames = Math.Max(0, request.Frames - 1)
                };
                var warmup = await captureSession.Capture.ExecuteAsync(warmupRequest, context);
                if (!warmup.Ok || !warmup.Value.Captured)
                {
                    var nested = warmup.Errors.Count > 0
                        ? warmup.Errors
                        : [new RekallAgeCommandError(
                            "REKALL_RENDER_QUALITY_CAPTURE_FAILED",
                            $"Preset '{canonicalPreset}' did not produce a timing warmup capture.",
                            canonicalPreset)];
                    return RekallAgeCommandResult<CompareQualityPresetsResult>.Failure(
                        new CompareQualityPresetsResult(request.SceneName, request.Frames, captures, BuildNextCommands(request)),
                        $"Quality preset comparison timing warmup stopped at '{canonicalPreset}'.",
                        nested);
                }
            }

            var capture = await captureSession.Capture.ExecuteAsync(captureRequest, context);
            if (!capture.Ok || !capture.Value.Captured)
            {
                var nested = capture.Errors.Count > 0
                    ? capture.Errors
                    : [new RekallAgeCommandError(
                        "REKALL_RENDER_QUALITY_CAPTURE_FAILED",
                        $"Preset '{canonicalPreset}' did not produce a capture.",
                        canonicalPreset)];
                return RekallAgeCommandResult<CompareQualityPresetsResult>.Failure(
                    new CompareQualityPresetsResult(request.SceneName, request.Frames, captures, BuildNextCommands(request)),
                    $"Quality preset comparison stopped at '{canonicalPreset}'.",
                    nested);
            }

            var quality = capture.Value.QualityPlan
                ?? throw new InvalidOperationException("Runtime viewport capture did not return its resolved quality plan.");
            captures.Add(new RekallAgeQualityPresetCapture(
                quality.RequestedPreset,
                quality.ResolvedPreset,
                capture.Value.FrameIndex,
                capture.Value.ScreenshotPath,
                capture.Value.NonBlank,
                quality.OutputWidth,
                quality.OutputHeight,
                quality.RenderWidth,
                quality.RenderHeight,
                capture.Value.ResourceBytes,
                capture.Value.DrawCount,
                capture.Value.DispatchCount,
                quality.Degradations,
                capture.Value.GpuTimings,
                capture.Value.FrameAnalysis)
            {
                HighFidelityFrame = capture.Value.HighFidelityFrame
            });
        }

        var frameIndex = captures.Count == 0 ? request.Frames : captures[0].FrameIndex;
        return RekallAgeCommandResult<CompareQualityPresetsResult>.Success(
            new CompareQualityPresetsResult(
                request.SceneName,
                frameIndex,
                captures,
                BuildNextCommands(request)),
            $"Compared {captures.Count} quality presets for scene '{request.SceneName}' at frame {frameIndex}.");
    }

    private static IReadOnlyList<RekallAgeCommandError> Validate(CompareQualityPresetsRequest request)
    {
        var errors = new List<RekallAgeCommandError>();
        if (request.Presets is null || request.Presets.Count is < 2 or > 6)
        {
            errors.Add(new RekallAgeCommandError(
                "REKALL_RENDER_QUALITY_PRESET_COUNT_INVALID",
                "Quality comparison accepts two to six presets.",
                request.Presets?.Count.ToString(CultureInfo.InvariantCulture) ?? "null"));
        }
        else
        {
            foreach (var preset in request.Presets)
            {
                if (string.IsNullOrWhiteSpace(preset) || !SupportedPresets.Contains(preset.Trim()))
                {
                    errors.Add(new RekallAgeCommandError(
                        "REKALL_RENDER_QUALITY_PRESET_UNSUPPORTED",
                        "Preset must be one of Performance, Low, Medium, High, Ultra, or Epic.",
                        preset ?? string.Empty));
                }
            }

            if (request.Presets
                    .Select(preset => preset?.Trim() ?? string.Empty)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() != request.Presets.Count)
            {
                errors.Add(new RekallAgeCommandError(
                    "REKALL_RENDER_QUALITY_PRESET_DUPLICATE",
                    "Quality comparison presets must be distinct.",
                    string.Join(",", request.Presets)));
            }
        }

        if (request.Frames < 0 || request.Width <= 0 || request.Height <= 0)
        {
            errors.Add(new RekallAgeCommandError(
                "REKALL_RENDER_QUALITY_COMPARE_INVALID_REQUEST",
                "Frame count must be non-negative and dimensions must be positive.",
                $"frames={request.Frames}; resolution={request.Width}x{request.Height}"));
        }

        var backendId = string.IsNullOrWhiteSpace(request.BackendId)
            ? string.Empty
            : request.BackendId.Trim().ToLowerInvariant();
        if (backendId is not "software" and not "vulkan")
        {
            errors.Add(new RekallAgeCommandError(
                "REKALL_RENDER_QUALITY_COMPARE_BACKEND_UNSUPPORTED",
                "Quality comparison backend must be 'software' or 'vulkan'.",
                $"requested={request.BackendId}; resolved=unavailable"));
        }

        if (string.IsNullOrWhiteSpace(request.OutputDirectory))
        {
            errors.Add(new RekallAgeCommandError(
                "REKALL_RENDER_QUALITY_COMPARE_INVALID_REQUEST",
                "Output directory is required.",
                request.SceneName));
        }

        return errors;
    }

    private static IReadOnlyList<string> BuildNextCommands(CompareQualityPresetsRequest request) =>
        [
            $"command execute rekall.render.capture_runtime_viewport with projectRoot='{request.ProjectRoot}', sceneName='{request.SceneName}', frames={request.Frames}, and the selected qualityPreset.",
            $"command execute rekall.render.performance.inspect_scene_budget with projectRoot='{request.ProjectRoot}', sceneName='{request.SceneName}', frames={request.Frames}, width={request.Width}, and height={request.Height}."
        ];

    private static CompareQualityPresetsResult EmptyComparison(CompareQualityPresetsRequest request) =>
        new(request.SceneName, Math.Max(0, request.Frames), [], BuildNextCommands(request));

    private static QualityPresetCaptureSession CreateIsolatedNativeSession()
    {
        var clearCapture = new RekallAgeNativeVulkanRenderPassSubmission();
        var sceneCapture = new RekallAgeNativeVulkanSceneCapture(clearCapture);
        return new QualityPresetCaptureSession(
            new CaptureRuntimeViewportCommand(clearCapture, sceneCapture),
            sceneCapture);
    }

    private sealed class QualityPresetCaptureSession(
        CaptureRuntimeViewportCommand capture,
        IDisposable? owner) : IDisposable
    {
        public CaptureRuntimeViewportCommand Capture { get; } = capture;

        public void Dispose() => owner?.Dispose();
    }
}
