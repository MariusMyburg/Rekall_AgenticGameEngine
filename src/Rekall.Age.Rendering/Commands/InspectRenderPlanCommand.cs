using Rekall.Age.Core.Commands;

namespace Rekall.Age.Rendering.Commands;

public sealed record InspectRenderPlanRequest(string ProjectRoot);

public sealed record InspectRenderPlanResult(RekallAgeRenderPlanDocument Plan);

public sealed class InspectRenderPlanCommand : IRekallAgeCommand<InspectRenderPlanRequest, InspectRenderPlanResult>
{
    private readonly RekallAgeRenderPlanStore _store = new();

    public string Name => "rekall.render.plan.inspect";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Returns the project render plan for agent inspection.",
        typeof(InspectRenderPlanRequest).FullName!,
        typeof(InspectRenderPlanResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<InspectRenderPlanResult>> ExecuteAsync(
        InspectRenderPlanRequest request,
        RekallAgeCommandContext context)
    {
        var plan = await _store.LoadAsync(request.ProjectRoot, context.CancellationToken);
        return RekallAgeCommandResult<InspectRenderPlanResult>.Success(
            new InspectRenderPlanResult(plan),
            $"Loaded render plan '{plan.Name}'.");
    }
}
