using Rekall.Age.Core.Commands;
using Rekall.Age.Runtime;
using System.Globalization;

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
    string? PreferredDeviceType = "discrete-gpu");

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
    string? SelectedDeviceName);

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
            context.CancellationToken);
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(
            world,
            request.Width,
            request.Height,
            request.DebugOverlay);
        var assets = await new RekallAgeRuntimeViewportAssetResolver().ResolveAsync(
            request.ProjectRoot,
            frame,
            context.CancellationToken);
        var backendId = NormalizeBackendId(request.BackendId);
        if (backendId.Equals("vulkan", StringComparison.Ordinal))
        {
            return await CaptureVulkanViewportAsync(request, context, frame, assets);
        }

        var capture = await new RekallAgeRuntimeSoftwareRenderer().CaptureAsync(
            frame,
            request.OutputDirectory,
            $"{world.SceneName}_runtime_{world.FrameIndex:000}.png",
            assets,
            context.CancellationToken);
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
            null);

        context.Transaction.RecordChangedResource(capture.ScreenshotPath);
        return RekallAgeCommandResult<CaptureRuntimeViewportResult>.Success(
            result,
            $"Captured runtime viewport for scene '{request.SceneName}' at frame {result.FrameIndex}.");
    }

    private async ValueTask<RekallAgeCommandResult<CaptureRuntimeViewportResult>> CaptureVulkanViewportAsync(
        CaptureRuntimeViewportRequest request,
        RekallAgeCommandContext context,
        Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportAssetSet assets)
    {
        if (frame.Renderables.Count == 0)
        {
            return await CaptureVulkanClearViewportAsync(request, context, frame);
        }

        var capture = await _vulkanSceneCapture.CaptureSceneAsync(
            frame,
            assets,
            request.OutputDirectory,
            request.PreferredDeviceType,
            context.CancellationToken);
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
            frame.Observations.Count,
            frame.Observations
                .Select(observation => observation.Code)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(code => code, StringComparer.Ordinal)
                .ToArray(),
            capture.SpriteCount,
            0,
            assets.Issues.Count(issue =>
                issue.Code.Equals("REKALL_RENDER_ASSET_MISSING", StringComparison.Ordinal)),
            assets.Issues.Count(issue =>
                issue.Code.Equals("REKALL_RENDER_ASSET_UNSUPPORTED", StringComparison.Ordinal)),
            assets.Issues
                .Select(issue => issue.Code)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(code => code, StringComparer.Ordinal)
                .ToArray(),
            "vulkan",
            capture.Captured && capture.SelectedDevice is not null,
            capture.Captured ? "vulkan-scene-rendered" : "vulkan-scene-failed",
            capture.SelectedDevice?.Name);

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
        Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportFrame frame)
    {

        var capture = await _vulkanCapture.CaptureClearRenderPassAsync(
            checked((uint)request.Width),
            checked((uint)request.Height),
            "R8G8B8A8_UNorm",
            request.PreferredDeviceType,
            request.OutputDirectory,
            ParseClearColor(frame.ActiveCamera?.ClearColor),
            context.CancellationToken);
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
            capture.SelectedDevice?.Name);

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
        if (backendId is not "software" and not "vulkan")
        {
            errors.Add(new RekallAgeCommandError(
                "REKALL_RUNTIME_VIEWPORT_BACKEND_UNSUPPORTED",
                "Runtime viewport backend must be 'software' or 'vulkan'.",
                request.BackendId));
        }

        return errors;
    }

    private static string NormalizeBackendId(string backendId)
    {
        return string.IsNullOrWhiteSpace(backendId)
            ? "software"
            : backendId.Trim().ToLowerInvariant();
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
            Array.Empty<string>(),
            0,
            0,
            0,
            0,
            Array.Empty<string>(),
            NormalizeBackendId(request.BackendId),
            false,
            "not-captured",
            null);
    }
}
