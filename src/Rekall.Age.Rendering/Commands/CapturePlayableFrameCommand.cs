using Rekall.Age.Core.Commands;
using Rekall.Age.Playback;
using Rekall.Age.World;

namespace Rekall.Age.Rendering.Commands;

public sealed record CapturePlayableFrameRequest(
    string ProjectRoot,
    string SceneName,
    string OutputDirectory,
    int FrameIndex = 1,
    int Width = 320,
    int Height = 180,
    IReadOnlyList<RekallAgePlaybackInput>? Inputs = null);

public sealed record CapturePlayableFrameResult(
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

public sealed class CapturePlayableFrameCommand
    : IRekallAgeCommand<CapturePlayableFrameRequest, CapturePlayableFrameResult>
{
    private readonly RekallAgeSceneStore _sceneStore = new();
    private readonly RekallAgePlayableFrameRasterizer _rasterizer = new();

    public string Name => "rekall.play.capture_frame";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Runs a playable scene frame and captures its structured module draw commands to a deterministic PNG.",
        typeof(CapturePlayableFrameRequest).FullName!,
        typeof(CapturePlayableFrameResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CapturePlayableFrameResult>> ExecuteAsync(
        CapturePlayableFrameRequest request,
        RekallAgeCommandContext context)
    {
        var frameIndex = Math.Clamp(request.FrameIndex, 1, 600);
        var width = Math.Clamp(request.Width, 1, 4096);
        var height = Math.Clamp(request.Height, 1, 4096);
        var scene = await _sceneStore.LoadAsync(request.ProjectRoot, request.SceneName, context.CancellationToken);

        IRekallAgePlayableGame game;
        try
        {
            game = RekallAgePlayableGameFactory.Create(request.ProjectRoot, scene);
        }
        catch (RekallAgePlayableModuleMissingException ex)
        {
            var result = new CapturePlayableFrameResult(
                false,
                string.Empty,
                "missing-module",
                frameIndex,
                width,
                height,
                false,
                0,
                0,
                [],
                string.Empty);
            var error = new RekallAgeCommandError("REKALL_PLAYABLE_MODULE_MISSING", ex.Message, ex.SceneName);
            return RekallAgeCommandResult<CapturePlayableFrameResult>.Failure(result, error.Message, [error]);
        }

        RekallAgePlaybackRenderFrame renderFrame = new(0, game.Kind, string.Empty, []);
        for (var i = 0; i < frameIndex; i++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var input = request.Inputs is { Count: > 0 } inputs && i < inputs.Count
                ? inputs[i]
                : RekallAgePlaybackInput.None;
            game.Tick(input);
            renderFrame = game.RenderFrame(i + 1);
        }

        Directory.CreateDirectory(request.OutputDirectory);
        var outputPath = Path.Combine(request.OutputDirectory, $"{SafeFileStem(request.SceneName)}_play_frame_{frameIndex:000}.png");
        var raster = _rasterizer.Rasterize(renderFrame, width, height);
        await RekallAgePngWriter.WriteRgbaAsync(outputPath, width, height, raster.Pixels, context.CancellationToken);
        context.Transaction.RecordChangedResource(outputPath);

        var resultValue = new CapturePlayableFrameResult(
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
        return RekallAgeCommandResult<CapturePlayableFrameResult>.Success(
            resultValue,
            $"Captured playable frame {renderFrame.FrameIndex} for scene '{request.SceneName}'.");
    }

    private static string SafeFileStem(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var characters = value.Select(character => invalid.Contains(character) ? '_' : character).ToArray();
        var stem = new string(characters).Trim();
        return string.IsNullOrWhiteSpace(stem) ? "scene" : stem;
    }
}
