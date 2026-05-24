using Rekall.Age.Core.Commands;
using Rekall.Age.Playback;
using Rekall.Age.Rendering;

namespace Rekall.Age.GameTemplates.Commands;

public sealed record CapturePlayablePackageFrameRequest(
    string PackagePath,
    string OutputDirectory,
    int FrameIndex = 1,
    int Width = 320,
    int Height = 180,
    IReadOnlyList<RekallAgePlaybackInput>? Inputs = null);

public sealed record CapturePlayablePackageFrameResult(
    bool Captured,
    string OutputPath,
    string Kind,
    int FrameIndex,
    int Width,
    int Height,
    bool NonBlank,
    int NonBackgroundPixels,
    int DrawCommandCount,
    IReadOnlyList<string> DrawCommandKinds,
    string Text);

public sealed class CapturePlayablePackageFrameCommand
    : IRekallAgeCommand<CapturePlayablePackageFrameRequest, CapturePlayablePackageFrameResult>
{
    private readonly RunPlayablePackageCommand _runPackage = new();
    private readonly RekallAgePlayableFrameRasterizer _rasterizer = new();

    public string Name => "rekall.workflow.capture_playable_package_frame";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Runs a packaged playable game and captures one structured render frame to a deterministic PNG.",
        typeof(CapturePlayablePackageFrameRequest).FullName!,
        typeof(CapturePlayablePackageFrameResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CapturePlayablePackageFrameResult>> ExecuteAsync(
        CapturePlayablePackageFrameRequest request,
        RekallAgeCommandContext context)
    {
        var frameIndex = Math.Clamp(request.FrameIndex, 1, 600);
        var width = Math.Clamp(request.Width, 1, 4096);
        var height = Math.Clamp(request.Height, 1, 4096);
        var run = await _runPackage.ExecuteAsync(
            new RunPlayablePackageRequest(request.PackagePath, frameIndex, request.Inputs),
            context);
        if (!run.Ok || run.Value.RenderFrames.Count < frameIndex)
        {
            var result = new CapturePlayablePackageFrameResult(
                false,
                string.Empty,
                "missing-frame",
                frameIndex,
                width,
                height,
                false,
                0,
                0,
                [],
                string.Empty);
            var error = new RekallAgeCommandError(
                "REKALL_PLAYABLE_PACKAGE_FRAME_MISSING",
                $"Packaged playable did not produce render frame {frameIndex}.",
                request.PackagePath);
            return RekallAgeCommandResult<CapturePlayablePackageFrameResult>.Failure(
                result,
                error.Message,
                [error]);
        }

        var renderFrame = run.Value.RenderFrames[frameIndex - 1];
        Directory.CreateDirectory(request.OutputDirectory);
        var outputPath = Path.Combine(request.OutputDirectory, $"package_play_frame_{frameIndex:000}.png");
        var raster = _rasterizer.Rasterize(renderFrame, width, height);
        await RekallAgePngWriter.WriteRgbaAsync(outputPath, width, height, raster.Pixels, context.CancellationToken);
        context.Transaction.RecordChangedResource(outputPath);

        var resultValue = new CapturePlayablePackageFrameResult(
            true,
            outputPath,
            renderFrame.Kind,
            renderFrame.FrameIndex,
            width,
            height,
            raster.NonBlank,
            raster.NonBackgroundPixels,
            renderFrame.DrawCommands.Count,
            renderFrame.DrawCommands.Select(command => command.Kind).ToArray(),
            renderFrame.Text);
        return RekallAgeCommandResult<CapturePlayablePackageFrameResult>.Success(
            resultValue,
            $"Captured packaged playable frame {renderFrame.FrameIndex}.");
    }
}
