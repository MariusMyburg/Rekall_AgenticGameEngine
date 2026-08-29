using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;

namespace Rekall.Age.World.Commands;

public sealed record SetComponentPropertyRequest(
    string ProjectRoot,
    string SceneName,
    string EntityId,
    string ComponentType,
    string PropertyName,
    JsonNode? Value,
    string? ExpectedRevision = null);

public sealed record SetComponentPropertyResult(RekallAgeSceneDocument Scene);

public sealed class SetComponentPropertyCommand
    : IRekallAgeCommand<SetComponentPropertyRequest, SetComponentPropertyResult>
{
    private readonly RekallAgeSceneStore _store = new();
    private readonly IRekallAgeComponentPropertyAdmissionPolicy? _propertyAdmission;

    public SetComponentPropertyCommand(IRekallAgeComponentPropertyAdmissionPolicy? propertyAdmission = null)
    {
        _propertyAdmission = propertyAdmission;
    }

    public string Name => "rekall.component.set_property";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Sets one component property without replacing the rest of the component. Value is native JSON: pass arrays and objects directly, never as JSON-encoded strings. Use rekall.module.search_component_schemas first for the exact property kind and shape.",
        typeof(SetComponentPropertyRequest).FullName!,
        typeof(SetComponentPropertyResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<SetComponentPropertyResult>> ExecuteAsync(
        SetComponentPropertyRequest request,
        RekallAgeCommandContext context)
    {
        var loaded = await _store.LoadVersionedAsync(request.ProjectRoot, request.SceneName, context.CancellationToken);
        var scene = loaded.Value;
        if (_propertyAdmission is not null)
        {
            var component = scene.GetRequiredEntity(request.EntityId).Components.Single(item =>
                item.Type.Equals(request.ComponentType, StringComparison.Ordinal));
            var prospectiveProperties = component
                .SetProperty(request.PropertyName, request.Value)
                .Properties;
            var propertyErrors = await _propertyAdmission.ValidateAsync(
                request.ProjectRoot,
                request.ComponentType,
                prospectiveProperties,
                request.ComponentType,
                context.CancellationToken);
            if (propertyErrors.Count > 0)
            {
                return RekallAgeCommandResult<SetComponentPropertyResult>.Failure(
                    new SetComponentPropertyResult(scene),
                    $"Component '{request.ComponentType}' has {propertyErrors.Count} invalid authored property value(s).",
                    propertyErrors);
            }
        }
        var updated = scene.UpdateEntity(
            request.EntityId,
            entity => entity.UpdateComponent(
                request.ComponentType,
                component => component.SetProperty(request.PropertyName, request.Value)));
        var scenePath = _store.GetScenePath(request.ProjectRoot, request.SceneName);
        context.Transaction.CaptureResourcePreimage(scenePath);
        await _store.SaveIfRevisionAsync(
            request.ProjectRoot,
            updated,
            request.ExpectedRevision ?? loaded.Revision,
            context.CancellationToken);
        context.Transaction.RecordChangedResource(scenePath);

        return RekallAgeCommandResult<SetComponentPropertyResult>.Success(
            new SetComponentPropertyResult(updated),
            $"Set '{request.PropertyName}' on '{request.ComponentType}'.");
    }
}
