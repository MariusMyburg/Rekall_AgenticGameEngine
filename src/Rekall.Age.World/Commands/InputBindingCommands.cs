using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;

namespace Rekall.Age.World.Commands;

public sealed record InspectInputBindingsRequest(
    string ProjectRoot,
    string SceneName,
    int Limit = 256);

public sealed record RekallAgeInputBindingInspection(
    string EntityId,
    string EntityName,
    bool Active,
    string ActionName,
    JsonObject Binding);

public sealed record InspectInputBindingsResult(
    string SceneName,
    int ActionMapCount,
    int TotalBindingCount,
    IReadOnlyList<RekallAgeInputBindingInspection> Bindings,
    bool Truncated);

public sealed class InspectInputBindingsCommand
    : IRekallAgeCommand<InspectInputBindingsRequest, InspectInputBindingsResult>
{
    private readonly RekallAgeSceneStore _store = new();

    public string Name => "rekall.input.inspect_bindings";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Inspects bounded native Rekall.InputActionMap bindings across a scene, including keyboard, mouse, gamepad, joystick, device, and player filters. Use this before rebinding or proving runtime input.",
        typeof(InspectInputBindingsRequest).FullName!,
        typeof(InspectInputBindingsResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<InspectInputBindingsResult>> ExecuteAsync(
        InspectInputBindingsRequest request,
        RekallAgeCommandContext context)
    {
        var scene = await _store.LoadAsync(request.ProjectRoot, request.SceneName, context.CancellationToken);
        var limit = Math.Clamp(request.Limit, 1, 1024);
        var maps = scene.Entities
            .SelectMany(entity => entity.Components
                .Where(component => component.Type.Equals("Rekall.InputActionMap", StringComparison.Ordinal))
                .Select(component => (entity, component)))
            .OrderBy(item => item.entity.Id, StringComparer.Ordinal)
            .ToArray();
        var bindings = maps.SelectMany(item => ReadActions(item.component.Properties)
                .Select(binding => new RekallAgeInputBindingInspection(
                    item.entity.Id,
                    item.entity.Name,
                    ReadBoolean(item.component.Properties, "active", true),
                    ReadString(binding, "name") ?? string.Empty,
                    binding.DeepClone().AsObject())))
            .ToArray();
        var bounded = bindings.Take(limit).ToArray();
        return RekallAgeCommandResult<InspectInputBindingsResult>.Success(
            new InspectInputBindingsResult(
                scene.Name,
                maps.Length,
                bindings.Length,
                bounded,
                bindings.Length > bounded.Length),
            $"Inspected {bindings.Length} input binding(s) across {maps.Length} action map(s).");
    }

    internal static IReadOnlyList<JsonObject> ReadActions(JsonObject properties)
    {
        var node = FindProperty(properties, "actions");
        return node is JsonArray array ? array.OfType<JsonObject>().ToArray() : [];
    }

    internal static JsonNode? FindProperty(JsonObject properties, string name) =>
        properties.FirstOrDefault(property => property.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    internal static string? ReadString(JsonObject properties, string name) =>
        FindProperty(properties, name) is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : null;

    private static bool ReadBoolean(JsonObject properties, string name, bool fallback) =>
        FindProperty(properties, name) is JsonValue value && value.TryGetValue<bool>(out var result)
            ? result
            : fallback;
}

public sealed record RebindInputActionRequest(
    string ProjectRoot,
    string SceneName,
    string EntityId,
    string ActionName,
    JsonObject? Binding = null,
    bool Remove = false,
    string? ExpectedRevision = null);

public sealed record RebindInputActionResult(
    RekallAgeSceneDocument Scene,
    string ActionName,
    bool Removed);

public sealed class RebindInputActionCommand
    : IRekallAgeCommand<RebindInputActionRequest, RebindInputActionResult>
{
    private readonly RekallAgeSceneStore _store = new();

    public string Name => "rekall.input.rebind_action";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Transactionally replaces or removes one scene-authored semantic input binding. Binding is a native JSON object without name; the command preserves the exact action name. Use rekall.input.inspect_bindings and rekall.module.search_component_schemas first.",
        typeof(RebindInputActionRequest).FullName!,
        typeof(RebindInputActionResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<RebindInputActionResult>> ExecuteAsync(
        RebindInputActionRequest request,
        RekallAgeCommandContext context)
    {
        var loaded = await _store.LoadVersionedAsync(request.ProjectRoot, request.SceneName, context.CancellationToken);
        var scene = loaded.Value;
        if (string.IsNullOrWhiteSpace(request.ActionName) || (!request.Remove && request.Binding is null))
        {
            return Failure("REKALL_INPUT_REBIND_INVALID", "A non-blank actionName and native binding object are required unless remove is true.");
        }

        var entity = scene.Entities.FirstOrDefault(item => item.Id.Equals(request.EntityId, StringComparison.Ordinal));
        var component = entity?.Components.FirstOrDefault(item => item.Type.Equals("Rekall.InputActionMap", StringComparison.Ordinal));
        if (entity is null || component is null)
        {
            return Failure("REKALL_INPUT_ACTION_MAP_NOT_FOUND", $"Entity '{request.EntityId}' does not have Rekall.InputActionMap.");
        }

        var actions = InspectInputBindingsCommand.ReadActions(component.Properties);
        var matches = actions.Where(action =>
            string.Equals(
                InspectInputBindingsCommand.ReadString(action, "name")?.Trim(),
                request.ActionName.Trim(),
                StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1)
        {
            return Failure(
                matches.Length == 0 ? "REKALL_INPUT_ACTION_NOT_FOUND" : "REKALL_INPUT_ACTION_DUPLICATE",
                matches.Length == 0
                    ? $"Input action '{request.ActionName.Trim()}' was not found on entity '{entity.Name}'."
                    : $"Input action '{request.ActionName.Trim()}' is duplicated on entity '{entity.Name}'; repair the map before rebinding.");
        }

        if (request.Binding is { Count: > 64 })
        {
            return Failure("REKALL_INPUT_BINDING_FIELD_LIMIT", "An input binding may contain at most 64 fields.");
        }

        var replacement = request.Binding?.DeepClone().AsObject();
        if (replacement is not null)
        {
            foreach (var key in replacement.Select(property => property.Key)
                         .Where(key => key.Equals("name", StringComparison.OrdinalIgnoreCase))
                         .ToArray())
            {
                replacement.Remove(key);
            }

            replacement["name"] = request.ActionName.Trim();
        }

        var updatedActions = new JsonArray();
        foreach (var action in actions)
        {
            if (ReferenceEquals(action, matches[0]))
            {
                if (!request.Remove)
                {
                    updatedActions.Add(replacement);
                }
            }
            else
            {
                updatedActions.Add(action.DeepClone());
            }
        }

        var actionsPropertyName = component.Properties
            .First(property => property.Key.Equals("actions", StringComparison.OrdinalIgnoreCase))
            .Key;
        var updated = scene.UpdateEntity(entity.Id, current => current.UpdateComponent(
            "Rekall.InputActionMap",
            map => map.SetProperty(actionsPropertyName, updatedActions)));
        var scenePath = _store.GetScenePath(request.ProjectRoot, request.SceneName);
        context.Transaction.CaptureResourcePreimage(scenePath);
        await _store.SaveIfRevisionAsync(
            request.ProjectRoot,
            updated,
            request.ExpectedRevision ?? loaded.Revision,
            context.CancellationToken);
        context.Transaction.RecordChangedResource(scenePath);
        return RekallAgeCommandResult<RebindInputActionResult>.Success(
            new RebindInputActionResult(updated, request.ActionName.Trim(), request.Remove),
            request.Remove
                ? $"Removed input action '{request.ActionName.Trim()}'."
                : $"Rebound input action '{request.ActionName.Trim()}'.");

        RekallAgeCommandResult<RebindInputActionResult> Failure(string code, string message) =>
            RekallAgeCommandResult<RebindInputActionResult>.Failure(
                new RebindInputActionResult(scene, request.ActionName?.Trim() ?? string.Empty, false),
                message,
                [new RekallAgeCommandError(code, message, "actionName")]);
    }
}
