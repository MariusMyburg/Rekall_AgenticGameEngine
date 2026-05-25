using Rekall.Age.Core.Commands;

namespace Rekall.Age.World.Commands;

public sealed record DeleteEntityRequest(
    string ProjectRoot,
    string SceneName,
    string EntityId);

public sealed record DeleteEntityResult(
    RekallAgeSceneDocument Scene,
    string DeletedEntityId,
    string DeletedEntityName);

public sealed class DeleteEntityCommand : IRekallAgeCommand<DeleteEntityRequest, DeleteEntityResult>
{
    private readonly RekallAgeSceneStore _store = new();

    public string Name => "rekall.entity.delete";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Deletes one entity from a Rekall AGE scene.",
        typeof(DeleteEntityRequest).FullName!,
        typeof(DeleteEntityResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<DeleteEntityResult>> ExecuteAsync(
        DeleteEntityRequest request,
        RekallAgeCommandContext context)
    {
        var scene = await _store.LoadAsync(request.ProjectRoot, request.SceneName, context.CancellationToken);
        var deleted = scene.Entities.FirstOrDefault(entity => entity.Id.Equals(request.EntityId, StringComparison.Ordinal));
        if (deleted is null)
        {
            var error = new RekallAgeCommandError(
                "REKALL_ENTITY_NOT_FOUND",
                $"Entity '{request.EntityId}' was not found in scene '{request.SceneName}'.",
                request.EntityId);
            return RekallAgeCommandResult<DeleteEntityResult>.Failure(Empty(request.EntityId), error.Message, [error]);
        }

        var updated = scene with
        {
            Entities = scene.Entities
                .Where(entity => !entity.Id.Equals(request.EntityId, StringComparison.Ordinal))
                .ToArray()
        };
        var scenePath = _store.GetScenePath(request.ProjectRoot, request.SceneName);
        context.Transaction.CaptureResourcePreimage(scenePath);
        await _store.SaveAsync(request.ProjectRoot, updated, context.CancellationToken);
        context.Transaction.RecordChangedResource(scenePath);

        return RekallAgeCommandResult<DeleteEntityResult>.Success(
            new DeleteEntityResult(updated, deleted.Id, deleted.Name),
            $"Deleted entity '{deleted.Name}'.");
    }

    private static DeleteEntityResult Empty(string entityId)
    {
        return new DeleteEntityResult(RekallAgeSceneDocument.Create("Empty", []), entityId, string.Empty);
    }
}
