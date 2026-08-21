using Rekall.Age.Core.Commands;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime;
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
    CaptureRuntimeViewportLayoutDiagnostics LayoutDiagnostics);

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
            BuildLayoutDiagnostics(frame));

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
            capture.SelectedDevice?.Name,
            frameAnalysis,
            BuildLayoutDiagnostics(frame));

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
            BuildLayoutDiagnostics(frame));

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
        RekallAgeRuntimeViewportFrame frame)
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
            AddPlaneOrientationDiagnostics(frame.Renderables, camera, warnings, hints);
        }

        AddUiDiagnostics(frame, warnings, hints);

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
        List<string> warnings,
        List<string> hints)
    {
        const int maximumHints = 8;
        foreach (var renderable in frame.Renderables.Where(item => item.UiVisual is not null))
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
                var visiblePercent = CalculateVisibleTextPercent(frame, visual);
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

        var textGeometry = frame.Renderables
            .Where(item => !string.IsNullOrEmpty(item.UiVisual?.Text))
            .Select(item => (item.EntityName, Geometry: CalculateVisibleTextGeometry(frame, item.UiVisual!)))
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
        RekallAgeRuntimeViewportUiVisual visual)
    {
        var geometry = CalculateVisibleTextGeometry(frame, visual);
        return geometry.FullArea == 0
            ? 100
            : (int)Math.Round(geometry.VisibleArea * 100d / geometry.FullArea);
    }

    private static UiTextGeometry CalculateVisibleTextGeometry(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportUiVisual visual)
    {
        var scale = Math.Max(1, visual.FontSize / 5);
        var textWidth = visual.Text!.Sum(character =>
            character == ' '
                ? 2 * scale
                : (RekallAgeBitmapFont.Width(character) + 1) * scale);
        var textX = visual.X + Math.Max(2, visual.BorderWidth + 2);
        var textY = visual.Y + Math.Max(0, (visual.Height - 5 * scale) / 2);
        var clipLeft = Math.Max(0, visual.ClipX);
        var clipTop = Math.Max(0, visual.ClipY);
        var clipRight = Math.Min(frame.Width, visual.ClipX + visual.ClipWidth);
        var clipBottom = Math.Min(frame.Height, visual.ClipY + visual.ClipHeight);
        var textHeight = 5 * scale;
        var visibleWidth = IntersectionLength(textX, textX + textWidth, clipLeft, clipRight);
        var visibleHeight = IntersectionLength(textY, textY + textHeight, clipTop, clipBottom);
        var textArea = (long)textWidth * textHeight;
        return new UiTextGeometry(
            textArea,
            Math.Max(textX, clipLeft),
            Math.Max(textY, clipTop),
            visibleWidth,
            visibleHeight);
    }

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
            EmptyLayoutDiagnostics());
    }
}
