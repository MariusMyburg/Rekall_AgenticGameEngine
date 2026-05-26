using System.Globalization;
using System.Text.Json.Nodes;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeLifecycleEventSystem : IRekallAgeRuntimeWorldSystem
{
    public string Id => "runtime.events.lifecycle";

    public int Priority => -900;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeWorldFrameContext context)
    {
        var events = new List<RekallAgeRuntimeEvent>();
        foreach (var entity in world.Entities)
        {
            if (!entity.Visible)
            {
                continue;
            }

            foreach (var component in entity.Components.Where(component =>
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
                    if (MapEvent(entity, eventNode, world, context) is { } runtimeEvent)
                    {
                        events.Add(runtimeEvent);
                    }
                }
            }
        }

        return ValueTask.FromResult(world with
        {
            Subsystems = world.Subsystems with
            {
                Events = new RekallAgeRuntimeEventView(
                    world.Subsystems.Events.Events
                        .Concat(events)
                        .OrderBy(runtimeEvent => runtimeEvent.Frame)
                        .ThenBy(runtimeEvent => runtimeEvent.EntityName, StringComparer.Ordinal)
                        .ThenBy(runtimeEvent => runtimeEvent.Type, StringComparer.Ordinal)
                        .ThenBy(runtimeEvent => runtimeEvent.Handler, StringComparer.Ordinal)
                        .ToArray())
            }
        });
    }

    private static RekallAgeRuntimeEvent? MapEvent(
        RekallAgeRuntimeEntity entity,
        JsonObject definition,
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeWorldFrameContext context)
    {
        if (!ReadBoolean(definition, "active", true))
        {
            return null;
        }

        var eventType = ReadString(definition, "event") ?? ReadString(definition, "type");
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return null;
        }

        eventType = eventType.Trim();
        if (eventType.Equals("entity.begin", StringComparison.OrdinalIgnoreCase))
        {
            if (world.FrameIndex != 0)
            {
                return null;
            }

            return CreateEvent(
                context.FrameIndex,
                "entity.begin",
                entity,
                ReadString(definition, "handler"),
                new JsonObject
                {
                    ["reason"] = "first-frame"
                });
        }

        if (eventType.Equals("entity.tick", StringComparison.OrdinalIgnoreCase))
        {
            return CreateEvent(
                context.FrameIndex,
                "entity.tick",
                entity,
                ReadString(definition, "handler"),
                new JsonObject
                {
                    ["deltaSeconds"] = context.DeltaTime.TotalSeconds,
                    ["elapsedSeconds"] = context.ElapsedTime.TotalSeconds
                });
        }

        return null;
    }

    private static RekallAgeRuntimeEvent CreateEvent(
        int frame,
        string type,
        RekallAgeRuntimeEntity entity,
        string? handler,
        JsonObject payload)
    {
        return new RekallAgeRuntimeEvent(
            frame,
            type,
            entity.Id,
            entity.Name,
            "runtime.lifecycle",
            string.IsNullOrWhiteSpace(handler) ? null : handler.Trim(),
            payload);
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
}
