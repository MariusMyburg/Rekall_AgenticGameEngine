using Rekall.Age.Core.Commands;
using Rekall.Age.World;

namespace Rekall.Age.LevelDesign.Commands;

public sealed record InstantiatePrefabRequest(
    string ProjectRoot,
    string SceneName,
    string PrefabId,
    string? Name = null);

public sealed record InstantiatePrefabResult(
    string EntityId,
    RekallAgeSceneDocument Scene);

public sealed class InstantiatePrefabCommand : IRekallAgeCommand<InstantiatePrefabRequest, InstantiatePrefabResult>
{
    private readonly RekallAgeSceneStore _sceneStore = new();
    private readonly RekallAgePrefabStore _prefabStore = new();

    public string Name => "rekall.level.prefab.instantiate";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Instantiates a prefab into a scene using a new stable entity id.",
        typeof(InstantiatePrefabRequest).FullName!,
        typeof(InstantiatePrefabResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<InstantiatePrefabResult>> ExecuteAsync(
        InstantiatePrefabRequest request,
        RekallAgeCommandContext context)
    {
        var scene = await _sceneStore.LoadAsync(request.ProjectRoot, request.SceneName, context.CancellationToken);
        var prefab = await _prefabStore.LoadAsync(request.ProjectRoot, request.PrefabId, context.CancellationToken);
        var entity = new RekallAgeEntityDocument(
            $"ent_{Guid.NewGuid():N}",
            string.IsNullOrWhiteSpace(request.Name) ? prefab.Name : request.Name.Trim(),
            prefab.RootEntity.Tags,
            prefab.RootEntity.Components)
        {
            PrefabSourceId = prefab.Id,
            Visible = prefab.RootEntity.Visible,
            Locked = prefab.RootEntity.Locked
        };
        var updated = scene.AddEntity(entity);
        await _sceneStore.SaveAsync(request.ProjectRoot, updated, context.CancellationToken);
        context.Transaction.RecordChangedResource(_sceneStore.GetScenePath(request.ProjectRoot, request.SceneName));
        return RekallAgeCommandResult<InstantiatePrefabResult>.Success(
            new InstantiatePrefabResult(entity.Id, updated),
            $"Instantiated prefab '{prefab.Name}'.");
    }
}
