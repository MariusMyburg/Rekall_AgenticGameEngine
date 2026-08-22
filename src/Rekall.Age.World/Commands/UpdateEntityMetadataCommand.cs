using Rekall.Age.Core.Commands;

namespace Rekall.Age.World.Commands;

public sealed record UpdateEntityMetadataRequest(
    string ProjectRoot,
    string SceneName,
    string EntityId,
    string? Name = null,
    bool? Visible = null,
    bool? Locked = null,
    string? ParentId = null,
    bool ClearParent = false,
    string? ExpectedRevision = null);

public sealed record UpdateEntityMetadataResult(
    RekallAgeSceneDocument Scene,
    RekallAgeEntityDocument Entity);

public sealed class UpdateEntityMetadataCommand
    : IRekallAgeCommand<UpdateEntityMetadataRequest, UpdateEntityMetadataResult>
{
    private readonly RekallAgeSceneStore _store = new();

    public string Name => "rekall.scene.entity.update_metadata";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Partially updates generic entity name, visibility, lock, or parent metadata by stable id. " +
        "Omitted fields remain unchanged; set clearParent to true to unparent. Parent updates reject missing entities and cycles.",
        typeof(UpdateEntityMetadataRequest).FullName!,
        typeof(UpdateEntityMetadataResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<UpdateEntityMetadataResult>> ExecuteAsync(
        UpdateEntityMetadataRequest request,
        RekallAgeCommandContext context)
    {
        if (request.Name is not null && string.IsNullOrWhiteSpace(request.Name))
        {
            return Failure(
                request.EntityId,
                "REKALL_ENTITY_NAME_REQUIRED",
                "Entity name cannot be blank.");
        }

        if (request.ClearParent && request.ParentId is not null)
        {
            return Failure(
                request.EntityId,
                "REKALL_PARENT_UPDATE_AMBIGUOUS",
                "Specify parentId or clearParent, not both.");
        }

        var loaded = await _store.LoadVersionedAsync(
            request.ProjectRoot,
            request.SceneName,
            context.CancellationToken);
        var scene = loaded.Value;
        var entity = scene.Entities.FirstOrDefault(candidate =>
            candidate.Id.Equals(request.EntityId, StringComparison.Ordinal));
        if (entity is null)
        {
            return Failure(
                request.EntityId,
                "REKALL_ENTITY_NOT_FOUND",
                $"Entity '{request.EntityId}' was not found in scene '{request.SceneName}'.");
        }

        var parentId = request.ClearParent ? null : request.ParentId ?? entity.ParentId;
        if (request.ParentId is not null)
        {
            var parentError = ValidateParent(scene, entity.Id, request.ParentId);
            if (parentError is not null)
            {
                return RekallAgeCommandResult<UpdateEntityMetadataResult>.Failure(
                    new UpdateEntityMetadataResult(scene, entity),
                    parentError.Message,
                    [parentError]);
            }
        }

        var updatedEntity = entity with
        {
            Name = request.Name?.Trim() ?? entity.Name,
            Visible = request.Visible ?? entity.Visible,
            Locked = request.Locked ?? entity.Locked,
            ParentId = parentId
        };
        var updated = scene.ReplaceEntity(updatedEntity);
        var scenePath = _store.GetScenePath(request.ProjectRoot, request.SceneName);
        context.Transaction.CaptureResourcePreimage(scenePath);
        await _store.SaveIfRevisionAsync(
            request.ProjectRoot,
            updated,
            request.ExpectedRevision ?? loaded.Revision,
            context.CancellationToken);
        context.Transaction.RecordChangedResource(scenePath);

        return RekallAgeCommandResult<UpdateEntityMetadataResult>.Success(
            new UpdateEntityMetadataResult(updated, updatedEntity),
            $"Updated metadata for entity '{updatedEntity.Name}'.");
    }

    private static RekallAgeCommandError? ValidateParent(
        RekallAgeSceneDocument scene,
        string entityId,
        string parentId)
    {
        if (parentId.Equals(entityId, StringComparison.Ordinal))
        {
            return new RekallAgeCommandError(
                "REKALL_PARENT_SELF",
                "An entity cannot be parented to itself.",
                entityId);
        }

        var parent = scene.Entities.FirstOrDefault(candidate =>
            candidate.Id.Equals(parentId, StringComparison.Ordinal));
        if (parent is null)
        {
            return new RekallAgeCommandError(
                "REKALL_PARENT_NOT_FOUND",
                $"Parent entity '{parentId}' was not found.",
                parentId);
        }

        var visited = new HashSet<string>(StringComparer.Ordinal) { entityId };
        RekallAgeEntityDocument? cursor = parent;
        while (cursor is not null)
        {
            if (!visited.Add(cursor.Id))
            {
                return new RekallAgeCommandError(
                    "REKALL_PARENT_CYCLE",
                    $"Parenting entity '{entityId}' to '{parentId}' would create a cycle.",
                    parentId);
            }

            cursor = cursor.ParentId is null
                ? null
                : scene.Entities.FirstOrDefault(candidate =>
                    candidate.Id.Equals(cursor.ParentId, StringComparison.Ordinal));
        }

        return null;
    }

    private static RekallAgeCommandResult<UpdateEntityMetadataResult> Failure(
        string entityId,
        string code,
        string message)
    {
        var emptyScene = RekallAgeSceneDocument.Create("Empty", []);
        var emptyEntity = new RekallAgeEntityDocument(entityId, string.Empty, [], []);
        var error = new RekallAgeCommandError(code, message, entityId);
        return RekallAgeCommandResult<UpdateEntityMetadataResult>.Failure(
            new UpdateEntityMetadataResult(emptyScene, emptyEntity),
            message,
            [error]);
    }
}
