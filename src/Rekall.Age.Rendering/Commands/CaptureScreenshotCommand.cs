using Rekall.Age.Core.Commands;

namespace Rekall.Age.Rendering.Commands;

public sealed record CaptureScreenshotRequest(
    string ProjectRoot,
    string SceneName,
    string OutputDirectory);

public sealed record CaptureScreenshotResult(
    string ScreenshotPath,
    bool NonBlank,
    int Width,
    int Height,
    int VisibleRenderers,
    string? ActiveCamera);

public sealed class CaptureScreenshotCommand
    : IRekallAgeCommand<CaptureScreenshotRequest, CaptureScreenshotResult>
{
    public string Name => "rekall.capture.screenshot";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Captures a Vulkan-rendered screenshot for a scene.",
        typeof(CaptureScreenshotRequest).FullName!,
        typeof(CaptureScreenshotResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CaptureScreenshotResult>> ExecuteAsync(
        CaptureScreenshotRequest request,
        RekallAgeCommandContext context)
    {
        var capture = await new CaptureRuntimeViewportCommand().ExecuteAsync(
            new CaptureRuntimeViewportRequest(
                request.ProjectRoot,
                request.SceneName,
                Frames: 0,
                request.OutputDirectory,
                Width: 160,
                Height: 90,
                DebugOverlay: true,
                BackendId: "vulkan"),
            context);
        if (!capture.Ok)
        {
            return RekallAgeCommandResult<CaptureScreenshotResult>.Failure(
                new(string.Empty, false, 0, 0, 0, null),
                capture.Summary,
                capture.Errors);
        }
        var result = new CaptureScreenshotResult(
            capture.Value.ScreenshotPath,
            capture.Value.NonBlank,
            capture.Value.Width,
            capture.Value.Height,
            capture.Value.RenderableCount,
            capture.Value.ActiveCamera);

        context.Transaction.RecordChangedResource(capture.Value.ScreenshotPath);
        return RekallAgeCommandResult<CaptureScreenshotResult>.Success(
            result,
            $"Captured screenshot for scene '{request.SceneName}'.");
    }
}
