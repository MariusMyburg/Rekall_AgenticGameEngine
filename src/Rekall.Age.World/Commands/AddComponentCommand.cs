using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;

namespace Rekall.Age.World.Commands;

public sealed record AddComponentRequest(
    string ProjectRoot,
    string SceneName,
    string EntityId,
    string ComponentType,
    JsonObject? Properties = null,
    string? ExpectedRevision = null);

public sealed record AddComponentResult(RekallAgeSceneDocument Scene);

public sealed class AddComponentCommand : IRekallAgeCommand<AddComponentRequest, AddComponentResult>
{
    private readonly RekallAgeSceneStore _store = new();
    private readonly IRekallAgeComponentPropertyAdmissionPolicy? _propertyAdmission;

    public AddComponentCommand(IRekallAgeComponentPropertyAdmissionPolicy? propertyAdmission = null)
    {
        _propertyAdmission = propertyAdmission;
    }

    public string Name => "rekall.component.add";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Adds or replaces a component on an entity.",
        typeof(AddComponentRequest).FullName!,
        typeof(AddComponentResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<AddComponentResult>> ExecuteAsync(
        AddComponentRequest request,
        RekallAgeCommandContext context)
    {
        if (RekallAgeReservedComponentAuthoring.Validate(request.ComponentType, request.ComponentType) is { } error)
        {
            return RekallAgeCommandResult<AddComponentResult>.Failure(
                new AddComponentResult(RekallAgeSceneDocument.Create(request.SceneName, [])),
                error.Message,
                [error]);
        }

        var properties = request.Properties ?? new JsonObject();
        if (_propertyAdmission is not null)
        {
            var propertyErrors = await _propertyAdmission.ValidateAsync(
                request.ProjectRoot,
                request.ComponentType,
                properties,
                request.ComponentType,
                context.CancellationToken);
            if (propertyErrors.Count > 0)
            {
                return RekallAgeCommandResult<AddComponentResult>.Failure(
                    new AddComponentResult(RekallAgeSceneDocument.Create(request.SceneName, [])),
                    $"Component '{request.ComponentType}' has {propertyErrors.Count} invalid authored property value(s).",
                    propertyErrors);
            }
        }

        var loaded = await _store.LoadVersionedAsync(request.ProjectRoot, request.SceneName, context.CancellationToken);
        var scene = loaded.Value;
        var component = RekallAgeComponentDocument.Create(request.ComponentType, properties);
        var updated = scene.UpdateEntity(request.EntityId, entity => entity.AddComponent(component));
        var scenePath = _store.GetScenePath(request.ProjectRoot, request.SceneName);
        context.Transaction.CaptureResourcePreimage(scenePath);
        await _store.SaveIfRevisionAsync(
            request.ProjectRoot,
            updated,
            request.ExpectedRevision ?? loaded.Revision,
            context.CancellationToken);
        context.Transaction.RecordChangedResource(scenePath);
        return RekallAgeCommandResult<AddComponentResult>.Success(
            new AddComponentResult(updated),
            $"Added component '{component.Type}'.");
    }
}
