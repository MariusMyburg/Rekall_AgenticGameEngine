using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;

namespace Rekall.Age.World.Commands;

public sealed record ApplySceneBlueprintRequest(
    string ProjectRoot,
    string SceneName,
    IReadOnlyList<RekallAgeSceneBlueprintEntity> Entities,
    bool ClearExisting = false);

public sealed record RekallAgeSceneBlueprintEntity(
    string Name,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<RekallAgeSceneBlueprintComponent>? Components = null,
    string? Id = null,
    string? ParentId = null,
    bool? Visible = null,
    bool? Locked = null);

public sealed record RekallAgeSceneBlueprintComponent(
    string Type,
    JsonObject? Properties = null);

public sealed record ApplySceneBlueprintResult(
    RekallAgeSceneDocument Scene,
    int EntityCount,
    int UpsertedCount,
    int RemovedCount);

public sealed class ApplySceneBlueprintCommand
    : IRekallAgeCommand<ApplySceneBlueprintRequest, ApplySceneBlueprintResult>
{
    private readonly RekallAgeSceneStore _store = new();

    public string Name => "rekall.scene.apply_blueprint";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Applies a generic scene entity/component blueprint in one transaction for efficient agent world authoring.",
        typeof(ApplySceneBlueprintRequest).FullName!,
        typeof(ApplySceneBlueprintResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ApplySceneBlueprintResult>> ExecuteAsync(
        ApplySceneBlueprintRequest request,
        RekallAgeCommandContext context)
    {
        if (request.Entities.Count == 0)
        {
            var error = new RekallAgeCommandError(
                "REKALL_SCENE_BLUEPRINT_EMPTY",
                "Scene blueprint must contain at least one entity.",
                request.SceneName);
            return RekallAgeCommandResult<ApplySceneBlueprintResult>.Failure(Empty(), error.Message, [error]);
        }

        var scene = await _store.LoadAsync(request.ProjectRoot, request.SceneName, context.CancellationToken);
        var existing = request.ClearExisting ? [] : scene.Entities.ToList();
        var removedCount = request.ClearExisting ? scene.Entities.Count : 0;
        var upsertedCount = 0;

        foreach (var blueprint in request.Entities)
        {
            var entity = CreateEntity(blueprint);
            var replacementIndex = FindReplacementIndex(existing, blueprint);
            if (replacementIndex < 0)
            {
                existing.Add(entity);
            }
            else
            {
                existing[replacementIndex] = entity;
            }

            upsertedCount++;
        }

        var updated = scene with
        {
            Entities = existing
                .OrderBy(entity => entity.Name, StringComparer.Ordinal)
                .ThenBy(entity => entity.Id, StringComparer.Ordinal)
                .ToArray()
        };
        var scenePath = _store.GetScenePath(request.ProjectRoot, request.SceneName);
        context.Transaction.CaptureResourcePreimage(scenePath);
        await _store.SaveAsync(request.ProjectRoot, updated, context.CancellationToken);
        context.Transaction.RecordChangedResource(scenePath);

        return RekallAgeCommandResult<ApplySceneBlueprintResult>.Success(
            new ApplySceneBlueprintResult(updated, updated.Entities.Count, upsertedCount, removedCount),
            $"Applied scene blueprint to '{request.SceneName}': {upsertedCount} upserted, {removedCount} removed.");
    }

    private static int FindReplacementIndex(List<RekallAgeEntityDocument> existing, RekallAgeSceneBlueprintEntity blueprint)
    {
        if (!string.IsNullOrWhiteSpace(blueprint.Id))
        {
            var byId = existing.FindIndex(entity => entity.Id.Equals(blueprint.Id, StringComparison.Ordinal));
            if (byId >= 0)
            {
                return byId;
            }
        }

        var nameMatches = existing
            .Select((entity, index) => (entity, index))
            .Where(item => item.entity.Name.Equals(blueprint.Name.Trim(), StringComparison.Ordinal))
            .ToArray();
        return nameMatches.Length == 1 ? nameMatches[0].index : -1;
    }

    private static RekallAgeEntityDocument CreateEntity(RekallAgeSceneBlueprintEntity blueprint)
    {
        var entity = RekallAgeEntityDocument.Create(blueprint.Name, blueprint.Tags ?? []);
        if (!string.IsNullOrWhiteSpace(blueprint.Id))
        {
            entity = entity with { Id = blueprint.Id.Trim() };
        }

        entity = entity with
        {
            ParentId = string.IsNullOrWhiteSpace(blueprint.ParentId) ? null : blueprint.ParentId.Trim(),
            Visible = blueprint.Visible ?? true,
            Locked = blueprint.Locked ?? false
        };

        foreach (var component in blueprint.Components ?? [])
        {
            entity = entity.AddComponent(RekallAgeComponentDocument.Create(component.Type, component.Properties));
        }

        return entity;
    }

    private static ApplySceneBlueprintResult Empty()
    {
        return new ApplySceneBlueprintResult(RekallAgeSceneDocument.Create("Empty", []), 0, 0, 0);
    }
}
