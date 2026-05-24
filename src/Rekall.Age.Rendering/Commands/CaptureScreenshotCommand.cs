using Rekall.Age.Core.Commands;
using Rekall.Age.World;

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
        "Captures a deterministic software preview screenshot for a scene.",
        typeof(CaptureScreenshotRequest).FullName!,
        typeof(CaptureScreenshotResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CaptureScreenshotResult>> ExecuteAsync(
        CaptureScreenshotRequest request,
        RekallAgeCommandContext context)
    {
        var capture = await new RekallAgeSoftwarePreview(new RekallAgeSceneStore())
            .CaptureAsync(request.ProjectRoot, request.SceneName, request.OutputDirectory, context.CancellationToken);
        var result = new CaptureScreenshotResult(
            capture.ScreenshotPath,
            capture.NonBlank,
            capture.Width,
            capture.Height,
            capture.VisibleRenderers,
            capture.ActiveCamera);

        context.Transaction.RecordChangedResource(capture.ScreenshotPath);
        return RekallAgeCommandResult<CaptureScreenshotResult>.Success(
            result,
            $"Captured screenshot for scene '{request.SceneName}'.");
    }
}
