using Rekall.Age.Core.Commands;

namespace Rekall.Age.Playback.Commands;

public sealed record PlaytestSceneRequest(
    string ProjectRoot,
    string SceneName,
    int Frames = 10,
    IReadOnlyList<RekallAgePlaybackInput>? Inputs = null,
    IReadOnlyList<RekallAgeFrameAssertion>? Assertions = null,
    IReadOnlyList<RekallAgeDrawCommandAssertion>? DrawAssertions = null);

public sealed record RekallAgeFrameAssertion(
    int FrameIndex,
    string Contains,
    bool MustContain = true);

public sealed record RekallAgeFrameAssertionResult(
    int FrameIndex,
    string Contains,
    bool MustContain,
    bool Passed,
    string Frame);

public sealed record RekallAgeDrawCommandAssertion(
    int FrameIndex,
    string? Kind = null,
    string? Id = null,
    string? TextContains = null,
    bool MustExist = true);

public sealed record RekallAgeDrawCommandAssertionResult(
    int FrameIndex,
    string? Kind,
    string? Id,
    string? TextContains,
    bool MustExist,
    bool Passed,
    IReadOnlyList<RekallAgePlaybackDrawCommand> MatchingCommands);

public sealed record PlaytestSceneResult(
    string Kind,
    bool Passed,
    IReadOnlyList<string> Frames,
    IReadOnlyList<RekallAgeFrameAssertionResult> Assertions,
    IReadOnlyList<RekallAgePlaybackRenderFrame> RenderFrames,
    IReadOnlyList<RekallAgeDrawCommandAssertionResult> DrawAssertions);

public sealed class PlaytestSceneCommand : IRekallAgeCommand<PlaytestSceneRequest, PlaytestSceneResult>
{
    private readonly PlaySceneCommand _playSceneCommand = new();

    public string Name => "rekall.playtest.scene";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Runs a playable scene and evaluates deterministic frame assertions for agent playtesting.",
        typeof(PlaytestSceneRequest).FullName!,
        typeof(PlaytestSceneResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<PlaytestSceneResult>> ExecuteAsync(
        PlaytestSceneRequest request,
        RekallAgeCommandContext context)
    {
        var playResult = await _playSceneCommand.ExecuteAsync(
            new PlaySceneRequest(request.ProjectRoot, request.SceneName, request.Frames, request.Inputs),
            context);
        var assertions = request.Assertions ?? Array.Empty<RekallAgeFrameAssertion>();
        var drawAssertions = request.DrawAssertions ?? Array.Empty<RekallAgeDrawCommandAssertion>();
        var assertionResults = EvaluateAssertions(playResult.Value.Frames, assertions);
        var drawAssertionResults = EvaluateDrawAssertions(playResult.Value.RenderFrames, drawAssertions);
        var passed = playResult.Ok &&
            assertionResults.All(assertion => assertion.Passed) &&
            drawAssertionResults.All(assertion => assertion.Passed);
        var result = new PlaytestSceneResult(
            playResult.Value.Kind,
            passed,
            playResult.Value.Frames,
            assertionResults,
            playResult.Value.RenderFrames,
            drawAssertionResults);

        if (!playResult.Ok)
        {
            return RekallAgeCommandResult<PlaytestSceneResult>.Failure(
                result,
                playResult.Summary,
                playResult.Errors);
        }

        if (!passed)
        {
            var failedCount =
                assertionResults.Count(assertion => !assertion.Passed) +
                drawAssertionResults.Count(assertion => !assertion.Passed);
            return RekallAgeCommandResult<PlaytestSceneResult>.Failure(
                result,
                $"{failedCount} playtest assertion(s) failed.",
                [
                    new RekallAgeCommandError(
                        "REKALL_PLAYTEST_FAILED",
                        $"{failedCount} playtest assertion(s) failed.",
                        request.SceneName)
                ]);
        }

        return RekallAgeCommandResult<PlaytestSceneResult>.Success(
            result,
            $"Playtest passed for {playResult.Value.Kind} with {assertionResults.Count} assertion(s).");
    }

    private static IReadOnlyList<RekallAgeFrameAssertionResult> EvaluateAssertions(
        IReadOnlyList<string> frames,
        IReadOnlyList<RekallAgeFrameAssertion> assertions)
    {
        var results = new List<RekallAgeFrameAssertionResult>(assertions.Count);
        foreach (var assertion in assertions)
        {
            var frame = assertion.FrameIndex >= 0 && assertion.FrameIndex < frames.Count
                ? frames[assertion.FrameIndex]
                : string.Empty;
            var contains = frame.Contains(assertion.Contains, StringComparison.Ordinal);
            var passed = assertion.MustContain ? contains : !contains;
            results.Add(new RekallAgeFrameAssertionResult(
                assertion.FrameIndex,
                assertion.Contains,
                assertion.MustContain,
                passed,
                frame));
        }

        return results;
    }

    private static IReadOnlyList<RekallAgeDrawCommandAssertionResult> EvaluateDrawAssertions(
        IReadOnlyList<RekallAgePlaybackRenderFrame> frames,
        IReadOnlyList<RekallAgeDrawCommandAssertion> assertions)
    {
        var results = new List<RekallAgeDrawCommandAssertionResult>(assertions.Count);
        foreach (var assertion in assertions)
        {
            var frame = assertion.FrameIndex >= 0 && assertion.FrameIndex < frames.Count
                ? frames[assertion.FrameIndex]
                : null;
            IReadOnlyList<RekallAgePlaybackDrawCommand> matches = frame is null
                ? []
                : frame.DrawCommands.Where(command => Matches(command, assertion)).ToArray();
            var exists = matches.Count > 0;
            var passed = assertion.MustExist ? exists : !exists;
            results.Add(new RekallAgeDrawCommandAssertionResult(
                assertion.FrameIndex,
                assertion.Kind,
                assertion.Id,
                assertion.TextContains,
                assertion.MustExist,
                passed,
                matches));
        }

        return results;
    }

    private static bool Matches(
        RekallAgePlaybackDrawCommand command,
        RekallAgeDrawCommandAssertion assertion)
    {
        return (assertion.Kind is null || string.Equals(command.Kind, assertion.Kind, StringComparison.Ordinal)) &&
            (assertion.Id is null || string.Equals(command.Id, assertion.Id, StringComparison.Ordinal)) &&
            (assertion.TextContains is null || command.Text.Contains(assertion.TextContains, StringComparison.Ordinal));
    }
}
