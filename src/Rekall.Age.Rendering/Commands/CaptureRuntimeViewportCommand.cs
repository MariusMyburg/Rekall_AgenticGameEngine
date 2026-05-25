using Rekall.Age.Core.Commands;
using Rekall.Age.Runtime;

namespace Rekall.Age.Rendering.Commands;

public sealed record CaptureRuntimeViewportRequest(
    string ProjectRoot,
    string SceneName,
    int Frames,
    string OutputDirectory,
    int Width = 320,
    int Height = 180,
    bool DebugOverlay = true);

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
    IReadOnlyList<string> AssetIssueCodes);

public sealed class CaptureRuntimeViewportCommand
    : IRekallAgeCommand<CaptureRuntimeViewportRequest, CaptureRuntimeViewportResult>
{
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
            capture.AssetIssueCodes);

        context.Transaction.RecordChangedResource(capture.ScreenshotPath);
        return RekallAgeCommandResult<CaptureRuntimeViewportResult>.Success(
            result,
            $"Captured runtime viewport for scene '{request.SceneName}' at frame {result.FrameIndex}.");
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

        return errors;
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
            Array.Empty<string>());
    }
}
