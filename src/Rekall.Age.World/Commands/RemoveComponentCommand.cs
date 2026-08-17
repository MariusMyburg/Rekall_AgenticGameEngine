using Rekall.Age.Core.Commands;

namespace Rekall.Age.World.Commands;

public sealed record RemoveComponentRequest(
    string ProjectRoot,
    string SceneName,
    string EntityId,
    string ComponentType);

public sealed record RemoveComponentResult(RekallAgeSceneDocument Scene);

public sealed class RemoveComponentCommand : IRekallAgeCommand<RemoveComponentRequest, RemoveComponentResult>
{
    private readonly RekallAgeSceneStore _store = new();

    public string Name => "rekall.component.remove";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Removes one exact component type from an entity while preserving its other authored components.",
        typeof(RemoveComponentRequest).FullName!,
        typeof(RemoveComponentResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<RemoveComponentResult>> ExecuteAsync(
        RemoveComponentRequest request,
        RekallAgeCommandContext context)
    {
        var scene = await _store.LoadAsync(request.ProjectRoot, request.SceneName, context.CancellationToken);
        var updated = scene.UpdateEntity(
            request.EntityId,
            entity => entity.RemoveComponent(request.ComponentType));
        var scenePath = _store.GetScenePath(request.ProjectRoot, request.SceneName);
        context.Transaction.CaptureResourcePreimage(scenePath);
        await _store.SaveAsync(request.ProjectRoot, updated, context.CancellationToken);
        context.Transaction.RecordChangedResource(scenePath);
        return RekallAgeCommandResult<RemoveComponentResult>.Success(
            new RemoveComponentResult(updated),
            $"Removed component '{request.ComponentType}'.");
    }
}
