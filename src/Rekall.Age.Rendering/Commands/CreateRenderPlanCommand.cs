using Rekall.Age.Core.Commands;

namespace Rekall.Age.Rendering.Commands;

public sealed record CreateRenderPlanRequest(string ProjectRoot, string BackendId, string Name);

public sealed record CreateRenderPlanResult(RekallAgeRenderPlanDocument Plan);

public sealed class CreateRenderPlanCommand : IRekallAgeCommand<CreateRenderPlanRequest, CreateRenderPlanResult>
{
    private readonly RekallAgeRenderPlanStore _store = new();

    public string Name => "rekall.render.plan.create";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Creates a project render plan for a low-level backend such as Vulkan.",
        typeof(CreateRenderPlanRequest).FullName!,
        typeof(CreateRenderPlanResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CreateRenderPlanResult>> ExecuteAsync(
        CreateRenderPlanRequest request,
        RekallAgeCommandContext context)
    {
        var plan = RekallAgeRenderPlanDocument.Create(request.BackendId, request.Name);
        await _store.SaveAsync(request.ProjectRoot, plan, context.CancellationToken);
        context.Transaction.RecordChangedResource(_store.GetPlanPath(request.ProjectRoot));
        return RekallAgeCommandResult<CreateRenderPlanResult>.Success(
            new CreateRenderPlanResult(plan),
            $"Created {plan.BackendId} render plan '{plan.Name}'.");
    }
}
