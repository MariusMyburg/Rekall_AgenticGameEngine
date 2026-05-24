using Rekall.Age.Core.Commands;
using Rekall.Age.Project;
using Rekall.Age.Validation;
using Rekall.Age.World;

namespace Rekall.Age.Agent.Commands;

public sealed record GetSceneSummaryRequest(string ProjectRoot, string SceneName);

public sealed record GetSceneSummaryResult(RekallAgeSceneSummary Summary);

public sealed class GetSceneSummaryCommand
    : IRekallAgeCommand<GetSceneSummaryRequest, GetSceneSummaryResult>
{
    public string Name => "rekall.context.scene_summary";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Returns a compact agent-readable scene summary.",
        typeof(GetSceneSummaryRequest).FullName!,
        typeof(GetSceneSummaryResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<GetSceneSummaryResult>> ExecuteAsync(
        GetSceneSummaryRequest request,
        RekallAgeCommandContext context)
    {
        var sceneStore = new RekallAgeSceneStore();
        var builder = new RekallAgeContextBuilder(
            new RekallAgeProjectStore(),
            sceneStore,
            new RekallAgeProjectValidator(sceneStore));
        var summary = await builder.BuildSceneSummaryAsync(
            request.ProjectRoot,
            request.SceneName,
            context.CancellationToken);
        return RekallAgeCommandResult<GetSceneSummaryResult>.Success(
            new GetSceneSummaryResult(summary),
            $"Loaded scene summary for '{summary.Scene}'.");
    }
}
