using Rekall.Age.Core.Commands;

namespace Rekall.Age.World.Commands;

public sealed record CreateEntityRequest(
    string ProjectRoot,
    string SceneName,
    string Name,
    IReadOnlyList<string> Tags);

public sealed record CreateEntityResult(
    string EntityId,
    RekallAgeSceneDocument Scene);

public sealed class CreateEntityCommand : IRekallAgeCommand<CreateEntityRequest, CreateEntityResult>
{
    private readonly RekallAgeSceneStore _store = new();

    public string Name => "rekall.entity.create";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Creates an entity in a Rekall AGE scene.",
        typeof(CreateEntityRequest).FullName!,
        typeof(CreateEntityResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CreateEntityResult>> ExecuteAsync(
        CreateEntityRequest request,
        RekallAgeCommandContext context)
    {
        var scene = await _store.LoadAsync(request.ProjectRoot, request.SceneName, context.CancellationToken);
        var entity = RekallAgeEntityDocument.Create(request.Name, request.Tags);
        var updated = scene.AddEntity(entity);
        await _store.SaveAsync(request.ProjectRoot, updated, context.CancellationToken);
        context.Transaction.RecordChangedResource(_store.GetScenePath(request.ProjectRoot, request.SceneName));
        return RekallAgeCommandResult<CreateEntityResult>.Success(
            new CreateEntityResult(entity.Id, updated),
            $"Created entity '{entity.Name}'.");
    }
}
