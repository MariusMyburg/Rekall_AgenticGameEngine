using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;

namespace Rekall.Age.World.Commands;

public sealed record AddComponentRequest(
    string ProjectRoot,
    string SceneName,
    string EntityId,
    string ComponentType,
    JsonObject Properties);

public sealed record AddComponentResult(RekallAgeSceneDocument Scene);

public sealed class AddComponentCommand : IRekallAgeCommand<AddComponentRequest, AddComponentResult>
{
    private readonly RekallAgeSceneStore _store = new();

    public string Name => "rekall.component.add";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Adds or replaces a component on an entity.",
        typeof(AddComponentRequest).FullName!,
        typeof(AddComponentResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<AddComponentResult>> ExecuteAsync(
        AddComponentRequest request,
        RekallAgeCommandContext context)
    {
        var scene = await _store.LoadAsync(request.ProjectRoot, request.SceneName, context.CancellationToken);
        var component = RekallAgeComponentDocument.Create(request.ComponentType, request.Properties);
        var updated = scene.UpdateEntity(request.EntityId, entity => entity.AddComponent(component));
        await _store.SaveAsync(request.ProjectRoot, updated, context.CancellationToken);
        context.Transaction.RecordChangedResource(_store.GetScenePath(request.ProjectRoot, request.SceneName));
        return RekallAgeCommandResult<AddComponentResult>.Success(
            new AddComponentResult(updated),
            $"Added component '{component.Type}'.");
    }
}
