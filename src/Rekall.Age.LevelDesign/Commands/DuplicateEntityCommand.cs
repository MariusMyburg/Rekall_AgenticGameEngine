using Rekall.Age.Core.Commands;
using Rekall.Age.World;

namespace Rekall.Age.LevelDesign.Commands;

public sealed record DuplicateEntityRequest(
    string ProjectRoot,
    string SceneName,
    string EntityId,
    string? Name = null);

public sealed record DuplicateEntityResult(
    string EntityId,
    RekallAgeSceneDocument Scene);

public sealed class DuplicateEntityCommand : IRekallAgeCommand<DuplicateEntityRequest, DuplicateEntityResult>
{
    private readonly RekallAgeSceneStore _store = new();

    public string Name => "rekall.level.entity.duplicate";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Duplicates an entity using a new stable id while preserving components and editor metadata.",
        typeof(DuplicateEntityRequest).FullName!,
        typeof(DuplicateEntityResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<DuplicateEntityResult>> ExecuteAsync(
        DuplicateEntityRequest request,
        RekallAgeCommandContext context)
    {
        var scene = await _store.LoadAsync(request.ProjectRoot, request.SceneName, context.CancellationToken);
        var source = scene.GetRequiredEntity(request.EntityId);
        var duplicate = new RekallAgeEntityDocument(
            $"ent_{Guid.NewGuid():N}",
            string.IsNullOrWhiteSpace(request.Name) ? $"{source.Name} Copy" : request.Name.Trim(),
            source.Tags,
            source.Components)
        {
            ParentId = source.ParentId,
            PrefabSourceId = source.PrefabSourceId,
            Visible = source.Visible,
            Locked = source.Locked
        };
        var updated = scene.AddEntity(duplicate);
        await _store.SaveAsync(request.ProjectRoot, updated, context.CancellationToken);
        context.Transaction.RecordChangedResource(_store.GetScenePath(request.ProjectRoot, request.SceneName));
        return RekallAgeCommandResult<DuplicateEntityResult>.Success(
            new DuplicateEntityResult(duplicate.Id, updated),
            $"Duplicated entity '{source.Name}'.");
    }
}
