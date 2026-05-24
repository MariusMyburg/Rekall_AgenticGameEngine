using Rekall.Age.Core.Commands;
using Rekall.Age.World;

namespace Rekall.Age.Playback.Commands;

public sealed record PlaySceneRequest(string ProjectRoot, string SceneName, int Frames = 10);

public sealed record PlaySceneResult(string Kind, IReadOnlyList<string> Frames);

public sealed class PlaySceneCommand : IRekallAgeCommand<PlaySceneRequest, PlaySceneResult>
{
    private readonly RekallAgeSceneStore _sceneStore = new();

    public string Name => "rekall.play.scene";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Runs a playable scene through the MVP player and returns deterministic text frames.",
        typeof(PlaySceneRequest).FullName!,
        typeof(PlaySceneResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<PlaySceneResult>> ExecuteAsync(
        PlaySceneRequest request,
        RekallAgeCommandContext context)
    {
        var scene = await _sceneStore.LoadAsync(request.ProjectRoot, request.SceneName, context.CancellationToken);
        IRekallAgePlayableGame game;
        try
        {
            game = RekallAgePlayableGameFactory.Create(request.ProjectRoot, scene);
        }
        catch (RekallAgePlayableModuleMissingException ex)
        {
            var error = new RekallAgeCommandError(
                "REKALL_PLAYABLE_MODULE_MISSING",
                ex.Message,
                ex.SceneName,
                [
                    new RekallAgeSuggestedCommand(
                        "rekall.module.scaffold_playable",
                        new Dictionary<string, object?>
                        {
                            ["projectRoot"] = request.ProjectRoot,
                            ["moduleId"] = "game.playable",
                            ["displayName"] = "Game Playable",
                            ["moduleName"] = "GamePlayable",
                            ["kind"] = "game"
                        })
                ]);
            return RekallAgeCommandResult<PlaySceneResult>.Failure(
                new PlaySceneResult("missing-module", Array.Empty<string>()),
                error.Message,
                [error]);
        }
        var frames = new List<string>();
        var frameCount = Math.Clamp(request.Frames, 1, 600);
        for (var i = 0; i < frameCount; i++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            game.Tick(RekallAgePlaybackInput.None);
            frames.Add(game.RenderAscii());
        }

        return RekallAgeCommandResult<PlaySceneResult>.Success(
            new PlaySceneResult(game.Kind, frames),
            $"Played {frameCount} frame(s) of {game.Kind}.");
    }
}
