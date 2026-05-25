using Rekall.Age.Core.Commands;
using Rekall.Age.World;

namespace Rekall.Age.LevelDesign.Commands;

public sealed record ParentEntityRequest(
    string ProjectRoot,
    string SceneName,
    string EntityId,
    string? ParentId);

public sealed record ParentEntityResult(RekallAgeSceneDocument Scene);

public sealed class ParentEntityCommand : IRekallAgeCommand<ParentEntityRequest, ParentEntityResult>
{
    private readonly RekallAgeSceneStore _store = new();

    public string Name => "rekall.level.entity.parent";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Parents or unparents an entity by stable id.",
        typeof(ParentEntityRequest).FullName!,
        typeof(ParentEntityResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ParentEntityResult>> ExecuteAsync(
        ParentEntityRequest request,
        RekallAgeCommandContext context)
    {
        if (request.ParentId is not null && request.ParentId.Equals(request.EntityId, StringComparison.Ordinal))
        {
            var error = new RekallAgeCommandError("REKALL_PARENT_SELF", "An entity cannot be parented to itself.", request.EntityId);
            return RekallAgeCommandResult<ParentEntityResult>.Failure(default!, error.Message, [error]);
        }

        var scene = await _store.LoadAsync(request.ProjectRoot, request.SceneName, context.CancellationToken);
        if (!string.IsNullOrWhiteSpace(request.ParentId))
        {
            scene.GetRequiredEntity(request.ParentId);
        }

        var updated = scene.UpdateEntity(request.EntityId, entity => entity with { ParentId = request.ParentId });
        await _store.SaveAsync(request.ProjectRoot, updated, context.CancellationToken);
        context.Transaction.RecordChangedResource(_store.GetScenePath(request.ProjectRoot, request.SceneName));
        return RekallAgeCommandResult<ParentEntityResult>.Success(
            new ParentEntityResult(updated),
            $"Updated parent for entity '{request.EntityId}'.");
    }
}
