using Rekall.Age.Core.Commands;

namespace Rekall.Age.Rendering.Commands;

public sealed record AddRenderResourceRequest(
    string ProjectRoot,
    string Id,
    string Kind,
    string Format,
    IReadOnlyList<string> Usage);

public sealed record AddRenderResourceResult(RekallAgeRenderPlanDocument Plan);

public sealed class AddRenderResourceCommand : IRekallAgeCommand<AddRenderResourceRequest, AddRenderResourceResult>
{
    private readonly RekallAgeRenderPlanStore _store = new();

    public string Name => "rekall.render.resource.add";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Adds or replaces a low-level render resource descriptor in the project render plan.",
        typeof(AddRenderResourceRequest).FullName!,
        typeof(AddRenderResourceResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<AddRenderResourceResult>> ExecuteAsync(
        AddRenderResourceRequest request,
        RekallAgeCommandContext context)
    {
        var plan = await _store.LoadAsync(request.ProjectRoot, context.CancellationToken);
        var updated = plan.AddResource(RekallAgeRenderResourceDescriptor.Create(
            request.Id,
            request.Kind,
            request.Format,
            request.Usage));
        await _store.SaveAsync(request.ProjectRoot, updated, context.CancellationToken);
        context.Transaction.RecordChangedResource(_store.GetPlanPath(request.ProjectRoot));
        return RekallAgeCommandResult<AddRenderResourceResult>.Success(
            new AddRenderResourceResult(updated),
            $"Added render resource '{request.Id}'.");
    }
}
