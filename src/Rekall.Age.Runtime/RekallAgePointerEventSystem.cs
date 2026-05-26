using System.Globalization;
using System.Text.Json.Nodes;
using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public sealed class RekallAgePointerEventSystem : IRekallAgeRuntimeWorldSystem
{
    private const string PointerRayComponent = "Rekall.PointerRay";
    private const string PointerStateComponent = "Rekall.PointerState";

    public string Id => "runtime.events.pointer";

    public int Priority => -850;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeWorldFrameContext context)
    {
        var entities = world.Entities.ToArray();
        var emitted = new List<RekallAgeRuntimeEvent>();

        for (var index = 0; index < entities.Length; index++)
        {
            var source = entities[index];
            if (!source.Visible)
            {
                continue;
            }

            foreach (var pointer in source.Components.Where(component =>
                         component.Type.Equals(PointerRayComponent, StringComparison.Ordinal)))
            {
                if (!ReadBoolean(pointer.Properties, "active", true))
                {
                    continue;
                }

                var result = EvaluatePointer(world with { Entities = entities }, source, pointer, context);
                emitted.AddRange(result.Events);
                entities[index] = result.Source;
                source = result.Source;
            }
        }

        return ValueTask.FromResult(world with
        {
            Entities = entities,
            Subsystems = world.Subsystems with
            {
                Events = new RekallAgeRuntimeEventView(
                    world.Subsystems.Events.Events
                        .Concat(emitted)
                        .OrderBy(runtimeEvent => runtimeEvent.Frame)
                        .ThenBy(runtimeEvent => runtimeEvent.EntityName, StringComparer.Ordinal)
                        .ThenBy(runtimeEvent => runtimeEvent.Type, StringComparer.Ordinal)
                        .ThenBy(runtimeEvent => runtimeEvent.Handler, StringComparer.Ordinal)
                        .ToArray())
            }
        });
    }

    private static PointerEvaluationResult EvaluatePointer(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeEntity source,
        RekallAgeRuntimeComponent pointer,
        RekallAgeRuntimeWorldFrameContext context)
    {
        var pointerId = ReadString(pointer.Properties, "pointerId") ?? source.Id;
        var button = ReadString(pointer.Properties, "button") ?? "Left";
        var origin = new RekallAgeRuntimeVector3(
            source.Transform.Position3D.X + ReadNumber(pointer.Properties, "originX", 0),
            source.Transform.Position3D.Y + ReadNumber(pointer.Properties, "originY", 0),
            source.Transform.Position3D.Z + ReadNumber(pointer.Properties, "originZ", 0));
        var direction = new RekallAgeRuntimeVector3(
            ReadNumber(pointer.Properties, "directionX", 0),
            ReadNumber(pointer.Properties, "directionY", 0),
            ReadNumber(pointer.Properties, "directionZ", 1));
        var range = Math.Max(0, ReadNumber(pointer.Properties, "range", 100));
        var targetTag = ReadString(pointer.Properties, "targetTag");
        var targetComponentType = ReadString(pointer.Properties, "targetComponentType");
        var state = source.FindComponent(PointerStateComponent)?.Properties;
        var previousHoveredEntityId = ReadStringOptional(state, "hoveredEntityId");
        var previousPressedEntityId = ReadStringOptional(state, "pressedEntityId");
        var hit = world
            .Raycast3D(origin, direction, range, targetTag, targetComponentType)
            .FirstOrDefault(candidate => !candidate.Entity.Id.Equals(source.Id, StringComparison.Ordinal));
        var hitEntity = hit?.Entity;
        var nextHoveredEntityId = hitEntity?.Id;
        var events = new List<RekallAgeRuntimeEvent>();

        if (!string.Equals(previousHoveredEntityId, nextHoveredEntityId, StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(previousHoveredEntityId)
                && world.Entities.FirstOrDefault(entity =>
                    entity.Id.Equals(previousHoveredEntityId, StringComparison.Ordinal)) is { } previousTarget)
            {
                AddEvents(
                    events,
                    "pointer.leave",
                    previousTarget,
                    source,
                    pointerId,
                    button,
                    context.FrameIndex,
                    null);
            }

            if (hit is not null)
            {
                AddEvents(
                    events,
                    "pointer.enter",
                    hit.Entity,
                    source,
                    pointerId,
                    button,
                    context.FrameIndex,
                    hit);
            }
        }

        if (hit is not null)
        {
            AddEvents(
                events,
                "pointer.hit",
                hit.Entity,
                source,
                pointerId,
                button,
                context.FrameIndex,
                hit);
        }

        var nextPressedEntityId = previousPressedEntityId;
        if (Contains(context.Input.PressedButtonsThisFrame, button))
        {
            nextPressedEntityId = hitEntity?.Id;
            if (hit is not null)
            {
                AddEvents(
                    events,
                    "pointer.down",
                    hit.Entity,
                    source,
                    pointerId,
                    button,
                    context.FrameIndex,
                    hit);
            }
        }

        if (Contains(context.Input.ReleasedButtonsThisFrame, button))
        {
            if (hit is not null)
            {
                AddEvents(
                    events,
                    "pointer.up",
                    hit.Entity,
                    source,
                    pointerId,
                    button,
                    context.FrameIndex,
                    hit);

                if (string.Equals(previousPressedEntityId, hit.Entity.Id, StringComparison.Ordinal))
                {
                    AddEvents(
                        events,
                        "pointer.click",
                        hit.Entity,
                        source,
                        pointerId,
                        button,
                        context.FrameIndex,
                        hit);
                }
            }

            nextPressedEntityId = null;
        }

        var nextState = new JsonObject();
        if (!string.IsNullOrWhiteSpace(nextHoveredEntityId))
        {
            nextState["hoveredEntityId"] = nextHoveredEntityId;
        }

        if (!string.IsNullOrWhiteSpace(nextPressedEntityId))
        {
            nextState["pressedEntityId"] = nextPressedEntityId;
        }

        nextState["pointerId"] = pointerId;

        return new PointerEvaluationResult(
            source.UpsertComponent(PointerStateComponent, nextState),
            events);
    }

    private static void AddEvents(
        List<RekallAgeRuntimeEvent> events,
        string type,
        RekallAgeRuntimeEntity target,
        RekallAgeRuntimeEntity source,
        string pointerId,
        string button,
        int frame,
        RekallAgeRuntimeRaycastHit? hit)
    {
        foreach (var binding in EventBindingsFor(target, type))
        {
            events.Add(new RekallAgeRuntimeEvent(
                frame,
                type,
                target.Id,
                target.Name,
                "runtime.pointer",
                binding.Handler,
                CreatePayload(source, pointerId, button, hit)));
        }
    }

    private static JsonObject CreatePayload(
        RekallAgeRuntimeEntity source,
        string pointerId,
        string button,
        RekallAgeRuntimeRaycastHit? hit)
    {
        var payload = new JsonObject
        {
            ["pointerId"] = pointerId,
            ["sourceEntityId"] = source.Id,
            ["sourceEntityName"] = source.Name,
            ["button"] = button
        };

        if (hit is not null)
        {
            payload["distance"] = hit.Distance;
            payload["colliderType"] = hit.ColliderType;
            payload["point"] = new JsonObject
            {
                ["x"] = hit.Point.X,
                ["y"] = hit.Point.Y,
                ["z"] = hit.Point.Z
            };
        }

        return payload;
    }

    private static IReadOnlyList<PointerEventBinding> EventBindingsFor(
        RekallAgeRuntimeEntity target,
        string type)
    {
        var bindings = new List<PointerEventBinding>();
        foreach (var component in target.Components.Where(component =>
                     component.Type.Equals("Rekall.EventBindings", StringComparison.Ordinal)))
        {
            if (!ReadBoolean(component.Properties, "active", true)
                || !TryGetPropertyValue(component.Properties, "events", out var eventNodes)
                || eventNodes is not JsonArray eventArray)
            {
                continue;
            }

            foreach (var eventNode in eventArray.OfType<JsonObject>())
            {
                if (!ReadBoolean(eventNode, "active", true))
                {
                    continue;
                }

                var eventType = ReadString(eventNode, "event") ?? ReadString(eventNode, "type");
                if (eventType?.Equals(type, StringComparison.OrdinalIgnoreCase) == true)
                {
                    bindings.Add(new PointerEventBinding(Clean(ReadString(eventNode, "handler"))));
                }
            }
        }

        return bindings;
    }

    private static bool Contains(IReadOnlySet<string>? values, string value)
    {
        return values is not null && values.Contains(value.Trim());
    }

    private static string? Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? ReadStringOptional(JsonObject? properties, string name)
    {
        return properties is null ? null : ReadString(properties, name);
    }

    private static string? ReadString(JsonObject properties, string name)
    {
        return TryGetPropertyValue(properties, name, out var node)
            && node is JsonValue value
            && value.TryGetValue<string>(out var text)
            ? text
            : null;
    }

    private static bool ReadBoolean(JsonObject properties, string name, bool fallback)
    {
        if (!TryGetPropertyValue(properties, name, out var node) || node is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<bool>(out var boolean))
        {
            return boolean;
        }

        return value.TryGetValue<string>(out var text)
            && bool.TryParse(text, out var parsed)
            ? parsed
            : fallback;
    }

    private static double ReadNumber(JsonObject properties, string name, double fallback)
    {
        if (!TryGetPropertyValue(properties, name, out var node) || node is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<double>(out var doubleValue))
        {
            return doubleValue;
        }

        if (value.TryGetValue<int>(out var intValue))
        {
            return intValue;
        }

        if (value.TryGetValue<long>(out var longValue))
        {
            return longValue;
        }

        return value.TryGetValue<string>(out var text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static bool TryGetPropertyValue(
        JsonObject properties,
        string name,
        out JsonNode? node)
    {
        if (properties.TryGetPropertyValue(name, out node))
        {
            return true;
        }

        var pascalName = char.ToUpperInvariant(name[0]) + name[1..];
        if (properties.TryGetPropertyValue(pascalName, out node))
        {
            return true;
        }

        var match = properties.FirstOrDefault(property =>
            property.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
        node = match.Value;
        return !string.IsNullOrEmpty(match.Key);
    }

    private sealed record PointerEvaluationResult(
        RekallAgeRuntimeEntity Source,
        IReadOnlyList<RekallAgeRuntimeEvent> Events);

    private sealed record PointerEventBinding(string? Handler);
}
