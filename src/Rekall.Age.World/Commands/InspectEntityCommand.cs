using Rekall.Age.Core.Commands;

namespace Rekall.Age.World.Commands;

public sealed record InspectEntityRequest(string ProjectRoot, string SceneName, string EntityId);

public sealed record InspectEntityResult(RekallAgeEntityDocument Entity);

public sealed class InspectEntityCommand : IRekallAgeCommand<InspectEntityRequest, InspectEntityResult>
{
    private readonly RekallAgeSceneStore _store = new();

    public string Name => "rekall.entity.inspect";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Returns one entity with full component properties for agent inspection.",
        typeof(InspectEntityRequest).FullName!,
        typeof(InspectEntityResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<InspectEntityResult>> ExecuteAsync(
        InspectEntityRequest request,
        RekallAgeCommandContext context)
    {
        var scene = await _store.LoadAsync(request.ProjectRoot, request.SceneName, context.CancellationToken);
        var entity = scene.Entities.SingleOrDefault(entity => entity.Id.Equals(request.EntityId, StringComparison.Ordinal));
        if (entity is null)
        {
            var error = new RekallAgeCommandError(
                "REKALL_ENTITY_NOT_FOUND",
                $"Entity '{request.EntityId}' was not found in scene '{request.SceneName}'.",
                request.EntityId);
            return RekallAgeCommandResult<InspectEntityResult>.Failure(default!, error.Message, [error]);
        }

        return RekallAgeCommandResult<InspectEntityResult>.Success(
            new InspectEntityResult(entity),
            $"Loaded entity '{entity.Name}'.");
    }
}
