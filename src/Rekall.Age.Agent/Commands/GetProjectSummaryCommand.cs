using Rekall.Age.Core.Commands;
using Rekall.Age.Project;
using Rekall.Age.Validation;
using Rekall.Age.World;

namespace Rekall.Age.Agent.Commands;

public sealed record GetProjectSummaryRequest(string ProjectRoot);

public sealed record GetProjectSummaryResult(RekallAgeProjectSummary Summary);

public sealed class GetProjectSummaryCommand
    : IRekallAgeCommand<GetProjectSummaryRequest, GetProjectSummaryResult>
{
    public string Name => "rekall.context.project_summary";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Returns a compact agent-readable project summary.",
        typeof(GetProjectSummaryRequest).FullName!,
        typeof(GetProjectSummaryResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<GetProjectSummaryResult>> ExecuteAsync(
        GetProjectSummaryRequest request,
        RekallAgeCommandContext context)
    {
        var sceneStore = new RekallAgeSceneStore();
        var builder = new RekallAgeContextBuilder(
            new RekallAgeProjectStore(),
            sceneStore,
            new RekallAgeProjectValidator(sceneStore));
        var summary = await builder.BuildProjectSummaryAsync(request.ProjectRoot, context.CancellationToken);
        return RekallAgeCommandResult<GetProjectSummaryResult>.Success(
            new GetProjectSummaryResult(summary),
            $"Loaded project summary for '{summary.Project}'.");
    }
}
