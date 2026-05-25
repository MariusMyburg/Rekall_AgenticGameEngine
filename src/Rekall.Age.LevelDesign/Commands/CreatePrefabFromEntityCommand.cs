using Rekall.Age.Core.Commands;
using Rekall.Age.World;

namespace Rekall.Age.LevelDesign.Commands;

public sealed record CreatePrefabFromEntityRequest(
    string ProjectRoot,
    string SceneName,
    string EntityId,
    string PrefabName);

public sealed record CreatePrefabFromEntityResult(
    string PrefabId,
    string PrefabPath);

public sealed class CreatePrefabFromEntityCommand
    : IRekallAgeCommand<CreatePrefabFromEntityRequest, CreatePrefabFromEntityResult>
{
    private readonly RekallAgeSceneStore _sceneStore = new();
    private readonly RekallAgePrefabStore _prefabStore = new();

    public string Name => "rekall.level.prefab.create_from_entity";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Creates a prefab asset from an entity.",
        typeof(CreatePrefabFromEntityRequest).FullName!,
        typeof(CreatePrefabFromEntityResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CreatePrefabFromEntityResult>> ExecuteAsync(
        CreatePrefabFromEntityRequest request,
        RekallAgeCommandContext context)
    {
        var scene = await _sceneStore.LoadAsync(request.ProjectRoot, request.SceneName, context.CancellationToken);
        var prefab = RekallAgePrefabDocument.Create(request.PrefabName, scene.GetRequiredEntity(request.EntityId));
        await _prefabStore.SaveAsync(request.ProjectRoot, prefab, context.CancellationToken);
        var path = _prefabStore.GetPath(request.ProjectRoot, prefab.Id);
        context.Transaction.RecordChangedResource(path);
        return RekallAgeCommandResult<CreatePrefabFromEntityResult>.Success(
            new CreatePrefabFromEntityResult(prefab.Id, path),
            $"Created prefab '{prefab.Name}'.");
    }
}
