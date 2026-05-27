using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Playback;
using Rekall.Age.Playback.Commands;
using Rekall.Age.Validation;
using Rekall.Age.World;

namespace Rekall.Age.Workflows.Commands;

public sealed record VerifyPlayableGameRequest(
    string ProjectRoot,
    string SceneName = "Main",
    int Frames = 2,
    IReadOnlyList<RekallAgePlaybackInput>? Inputs = null,
    IReadOnlyList<RekallAgeFrameAssertion>? Assertions = null,
    IReadOnlyList<RekallAgeDrawCommandAssertion>? DrawAssertions = null);

public sealed record RekallAgePlayableGameCheck(
    string Name,
    bool Passed,
    string Summary);

public sealed record VerifyPlayableGameResult(
    bool Ready,
    bool BuildSucceeded,
    bool PlaytestPassed,
    IReadOnlyList<RekallAgePlayableGameCheck> Checks,
    IReadOnlyList<string> Frames,
    IReadOnlyList<RekallAgePlaybackRenderFrame> RenderFrames,
    IReadOnlyList<RekallAgeDrawCommandAssertionResult> DrawAssertions);

public sealed class VerifyPlayableGameCommand
    : IRekallAgeCommand<VerifyPlayableGameRequest, VerifyPlayableGameResult>
{
    private readonly BuildModulesCommand _buildModules = new();
    private readonly PlaytestSceneCommand _playtestScene = new();
    private readonly RekallAgeProjectValidator _validator = new(new RekallAgeSceneStore());

    public string Name => "rekall.workflow.verify_playable_game";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Builds modules, validates the scene, and playtests a playable game for agent-readable readiness.",
        typeof(VerifyPlayableGameRequest).FullName!,
        typeof(VerifyPlayableGameResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<VerifyPlayableGameResult>> ExecuteAsync(
        VerifyPlayableGameRequest request,
        RekallAgeCommandContext context)
    {
        var checks = new List<RekallAgePlayableGameCheck>();
        var validation = await _validator.ValidateSceneAsync(request.ProjectRoot, request.SceneName, context.CancellationToken);
        var validationPassed = validation.BlockingMessages.Count == 0;
        checks.Add(new RekallAgePlayableGameCheck(
            "scene-validation",
            validationPassed,
            validationPassed
                ? $"Scene '{request.SceneName}' passed blocking validation."
                : string.Join(Environment.NewLine, validation.BlockingMessages)));

        var build = await _buildModules.ExecuteAsync(new BuildModulesRequest(request.ProjectRoot), context);
        checks.Add(new RekallAgePlayableGameCheck("module-build", build.Ok, build.Summary));
        if (!validationPassed || !build.Ok)
        {
            return NotReady(checks, build.Ok, playtestPassed: false, frames: [], renderFrames: [], drawAssertions: [], request.SceneName);
        }

        var playtest = await _playtestScene.ExecuteAsync(
            new PlaytestSceneRequest(
                request.ProjectRoot,
                request.SceneName,
                request.Frames,
                request.Inputs,
                request.Assertions,
                request.DrawAssertions),
            context);
        checks.Add(new RekallAgePlayableGameCheck("playtest", playtest.Ok && playtest.Value.Passed, playtest.Summary));
        if (!playtest.Ok || !playtest.Value.Passed)
        {
            return NotReady(
                checks,
                build.Ok,
                playtest.Value.Passed,
                playtest.Value.Frames,
                playtest.Value.RenderFrames,
                playtest.Value.DrawAssertions,
                request.SceneName);
        }

        return RekallAgeCommandResult<VerifyPlayableGameResult>.Success(
            new VerifyPlayableGameResult(
                Ready: true,
                BuildSucceeded: true,
                PlaytestPassed: true,
                Checks: checks,
                Frames: playtest.Value.Frames,
                RenderFrames: playtest.Value.RenderFrames,
                DrawAssertions: playtest.Value.DrawAssertions),
            $"Playable game '{request.SceneName}' is ready.");
    }

    private static RekallAgeCommandResult<VerifyPlayableGameResult> NotReady(
        IReadOnlyList<RekallAgePlayableGameCheck> checks,
        bool buildSucceeded,
        bool playtestPassed,
        IReadOnlyList<string> frames,
        IReadOnlyList<RekallAgePlaybackRenderFrame>? renderFrames,
        IReadOnlyList<RekallAgeDrawCommandAssertionResult>? drawAssertions,
        string sceneName)
    {
        var error = new RekallAgeCommandError(
            "REKALL_PLAYABLE_GAME_NOT_READY",
            $"Playable game '{sceneName}' is not ready.",
            sceneName);
        return RekallAgeCommandResult<VerifyPlayableGameResult>.Failure(
            new VerifyPlayableGameResult(
                Ready: false,
                BuildSucceeded: buildSucceeded,
                PlaytestPassed: playtestPassed,
                Checks: checks,
                Frames: frames,
                RenderFrames: renderFrames ?? [],
                DrawAssertions: drawAssertions ?? []),
            error.Message,
            [error]);
    }

}
