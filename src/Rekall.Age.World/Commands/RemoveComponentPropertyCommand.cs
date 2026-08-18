using Rekall.Age.Core.Commands;

namespace Rekall.Age.World.Commands;

public sealed record RemoveComponentPropertyRequest(
    string ProjectRoot,
    string SceneName,
    string EntityId,
    string ComponentType,
    string PropertyName,
    string? ExpectedRevision = null);

public sealed record RemoveComponentPropertyResult(RekallAgeSceneDocument Scene);

public sealed class RemoveComponentPropertyCommand
    : IRekallAgeCommand<RemoveComponentPropertyRequest, RemoveComponentPropertyResult>
{
    private readonly RekallAgeSceneStore _store = new();

    public string Name => "rekall.component.remove_property";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Removes one invalid or obsolete component property without replacing the component. Use the exact arguments suggested by validation diagnostics.",
        typeof(RemoveComponentPropertyRequest).FullName!,
        typeof(RemoveComponentPropertyResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<RemoveComponentPropertyResult>> ExecuteAsync(
        RemoveComponentPropertyRequest request,
        RekallAgeCommandContext context)
    {
        var loaded = await _store.LoadVersionedAsync(request.ProjectRoot, request.SceneName, context.CancellationToken);
        var scene = loaded.Value;
        var updated = scene.UpdateEntity(
            request.EntityId,
            entity => entity.UpdateComponent(
                request.ComponentType,
                component => component.RemoveProperty(request.PropertyName)));
        var scenePath = _store.GetScenePath(request.ProjectRoot, request.SceneName);
        context.Transaction.CaptureResourcePreimage(scenePath);
        await _store.SaveIfRevisionAsync(
            request.ProjectRoot,
            updated,
            request.ExpectedRevision ?? loaded.Revision,
            context.CancellationToken);
        context.Transaction.RecordChangedResource(scenePath);

        return RekallAgeCommandResult<RemoveComponentPropertyResult>.Success(
            new RemoveComponentPropertyResult(updated),
            $"Removed '{request.PropertyName}' from '{request.ComponentType}'.");
    }
}
