using Rekall.Age.Core.Commands;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.Validation;
using Rekall.Age.World;

namespace Rekall.Age.Runtime.Commands;

public sealed record RunSceneRequest(
    string ProjectRoot,
    string SceneName,
    double Seconds,
    IReadOnlyList<RekallAgeRuntimeInputFrame>? Inputs = null);

public sealed record RunSceneResult(
    bool Ok,
    int FramesSimulated,
    IReadOnlyList<string> ActiveSystems,
    int InputActionCount,
    IReadOnlyList<RekallAgeRuntimeInputAction> InputActions,
    int XrActionCount,
    IReadOnlyList<RekallAgeRuntimeXrAction> XrActions,
    IReadOnlyList<RekallAgeRuntimeObservation> Observations,
    IReadOnlyList<string> Errors);

public sealed class RunSceneCommand : IRekallAgeCommand<RunSceneRequest, RunSceneResult>
{
    public string Name => "rekall.run.scene";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Runs a scene headlessly and reports active gameplay systems.",
        typeof(RunSceneRequest).FullName!,
        typeof(RunSceneResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<RunSceneResult>> ExecuteAsync(
        RunSceneRequest request,
        RekallAgeCommandContext context)
    {
        var sceneStore = new RekallAgeSceneStore();
        var runtime = new RekallAgeHeadlessRuntime(sceneStore, new RekallAgeProjectValidator(sceneStore));
        var result = await runtime.RunAsync(
            request.ProjectRoot,
            request.SceneName,
            TimeSpan.FromSeconds(request.Seconds),
            context.CancellationToken,
            request.Inputs);

        var value = new RunSceneResult(
            result.Ok,
            result.FramesSimulated,
            result.ActiveSystems,
            result.InputActions.Count,
            result.InputActions,
            result.XrActions.Count,
            result.XrActions,
            result.Observations,
            result.Errors);

        if (!result.Ok)
        {
            var errors = result.Errors
                .Select(error => new RekallAgeCommandError("REKALL_RUN_BLOCKED", error, request.SceneName))
                .ToArray();
            return RekallAgeCommandResult<RunSceneResult>.Failure(value, "Scene run was blocked.", errors);
        }

        return RekallAgeCommandResult<RunSceneResult>.Success(
            value,
            $"Simulated scene '{request.SceneName}' for {result.FramesSimulated} frames.");
    }
}
