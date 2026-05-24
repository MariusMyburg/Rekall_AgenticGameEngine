using Rekall.Age.Core.Commands;

namespace Rekall.Age.Playback.Commands;

public sealed record PlaytestSceneRequest(
    string ProjectRoot,
    string SceneName,
    int Frames = 10,
    IReadOnlyList<RekallAgePlaybackInput>? Inputs = null,
    IReadOnlyList<RekallAgeFrameAssertion>? Assertions = null);

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

public sealed record PlaytestSceneResult(
    string Kind,
    bool Passed,
    IReadOnlyList<string> Frames,
    IReadOnlyList<RekallAgeFrameAssertionResult> Assertions);

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
        var assertionResults = EvaluateAssertions(playResult.Value.Frames, assertions);
        var passed = playResult.Ok && assertionResults.All(assertion => assertion.Passed);
        var result = new PlaytestSceneResult(playResult.Value.Kind, passed, playResult.Value.Frames, assertionResults);

        if (!playResult.Ok)
        {
            return RekallAgeCommandResult<PlaytestSceneResult>.Failure(
                result,
                playResult.Summary,
                playResult.Errors);
        }

        if (!passed)
        {
            var failedCount = assertionResults.Count(assertion => !assertion.Passed);
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
}
