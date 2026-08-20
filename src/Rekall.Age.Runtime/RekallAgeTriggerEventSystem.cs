using System.Globalization;
using System.Text.Json.Nodes;
using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeTriggerEventSystem : IRekallAgeRuntimeWorldSystem
{
    private const string TriggerComponent = "Rekall.Trigger";
    private const string TriggerStateComponent = "Rekall.TriggerState";

    public string Id => "runtime.events.trigger";

    public int Priority => 11;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeWorldFrameContext context)
    {
        var colliders = world.Entities
            .Where(entity => entity.Visible)
            .Select(CreateColliderBody)
            .Where(body => body is not null)
            .Select(body => body!)
            .ToArray();
        var colliderById = colliders.ToDictionary(body => body.Entity.Id, StringComparer.Ordinal);
        var entityById = world.Entities.ToDictionary(entity => entity.Id, StringComparer.Ordinal);
        var emitted = new List<RekallAgeRuntimeEvent>();

        var updatedEntities = world.Entities.Select(entity =>
        {
            if (!entity.Visible || entity.FindComponent(TriggerComponent) is not { } trigger)
            {
                return entity;
            }

            if (!ReadBoolean(trigger.Properties, "active", true))
            {
                return entity;
            }

            var triggerBody = CreateTriggerBody(entity, trigger);
            var current = colliders
                .Where(body => !body.Entity.Id.Equals(entity.Id, StringComparison.Ordinal)
                               && MatchesFilters(body.Entity, trigger)
                               && Overlaps(triggerBody, body))
                .Select(body => body.Entity.Id)
                .ToHashSet(StringComparer.Ordinal);
            var previous = ReadPreviousOccupants(entity);

            EmitTransitions(
                emitted,
                entity,
                current,
                previous,
                triggerBody,
                colliderById,
                entityById,
                context.FrameIndex);
            return entity.UpsertComponent(TriggerStateComponent, CreateState(current));
        }).ToArray();

        return ValueTask.FromResult(world with
        {
            Entities = updatedEntities,
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

    private static void EmitTransitions(
        List<RekallAgeRuntimeEvent> emitted,
        RekallAgeRuntimeEntity trigger,
        HashSet<string> current,
        HashSet<string> previous,
        TriggerBody triggerBody,
        IReadOnlyDictionary<string, ColliderBody> colliderById,
        IReadOnlyDictionary<string, RekallAgeRuntimeEntity> entityById,
        int frame)
    {
        foreach (var otherId in current.Except(previous, StringComparer.Ordinal))
        {
            EmitBoundEvents(emitted, "trigger.enter", trigger, otherId, triggerBody, colliderById, entityById, frame);
        }

        foreach (var otherId in current.Intersect(previous, StringComparer.Ordinal))
        {
            EmitBoundEvents(emitted, "trigger.stay", trigger, otherId, triggerBody, colliderById, entityById, frame);
        }

        foreach (var otherId in previous.Except(current, StringComparer.Ordinal))
        {
            EmitBoundEvents(emitted, "trigger.exit", trigger, otherId, triggerBody, colliderById, entityById, frame);
        }
    }

    private static void EmitBoundEvents(
        List<RekallAgeRuntimeEvent> emitted,
        string type,
        RekallAgeRuntimeEntity trigger,
        string otherId,
        TriggerBody triggerBody,
        IReadOnlyDictionary<string, ColliderBody> colliderById,
        IReadOnlyDictionary<string, RekallAgeRuntimeEntity> entityById,
        int frame)
    {
        if (!entityById.TryGetValue(otherId, out var other))
        {
            return;
        }

        foreach (var binding in EventBindingsFor(trigger, type))
        {
            emitted.Add(new RekallAgeRuntimeEvent(
                frame,
                type,
                trigger.Id,
                trigger.Name,
                "runtime.trigger",
                binding.Handler,
                CreatePayload(other, triggerBody, colliderById)));
        }
    }

    private static JsonObject CreatePayload(
        RekallAgeRuntimeEntity other,
        TriggerBody triggerBody,
        IReadOnlyDictionary<string, ColliderBody> colliderById)
    {
        var payload = new JsonObject
        {
            ["otherEntityId"] = other.Id,
            ["otherEntityName"] = other.Name,
            ["triggerShape"] = triggerBody.Shape
        };

        if (colliderById.TryGetValue(other.Id, out var otherBody))
        {
            payload["otherColliderType"] = otherBody.Collider.Type;
        }

        return payload;
    }

    private static bool MatchesFilters(
        RekallAgeRuntimeEntity entity,
        RekallAgeRuntimeComponent trigger)
    {
        var targetTag = ReadString(trigger.Properties, "targetTag");
        if (!string.IsNullOrWhiteSpace(targetTag) && !entity.HasTag(targetTag))
        {
            return false;
        }

        var targetComponentType = ReadString(trigger.Properties, "targetComponentType");
        return string.IsNullOrWhiteSpace(targetComponentType)
               || entity.FindComponent(targetComponentType) is not null;
    }

    private static JsonObject CreateState(IEnumerable<string> occupants)
    {
        var array = new JsonArray();
        foreach (var occupant in occupants.OrderBy(value => value, StringComparer.Ordinal))
        {
            array.Add(occupant);
        }

        return new JsonObject { ["occupants"] = array };
    }

    private static HashSet<string> ReadPreviousOccupants(RekallAgeRuntimeEntity entity)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var state = entity.FindComponent(TriggerStateComponent);
        if (state is null
            || !TryGetPropertyValue(state.Properties, "occupants", out var occupantsNode)
            || occupantsNode is not JsonArray occupants)
        {
            return result;
        }

        foreach (var occupant in occupants)
        {
            if (occupant is JsonValue value
                && value.TryGetValue<string>(out var text)
                && !string.IsNullOrWhiteSpace(text))
            {
                result.Add(text.Trim());
            }
        }

        return result;
    }

    private static TriggerBody CreateTriggerBody(
        RekallAgeRuntimeEntity entity,
        RekallAgeRuntimeComponent trigger)
    {
        var is2D = entity.FindComponent("Rekall.Transform2D") is not null;
        var shape = (ReadString(trigger.Properties, "shape") ?? "sphere").Trim().ToLowerInvariant();
        var radius = shape switch
        {
            "box" when is2D => EstimateBox2DBoundingRadius(trigger),
            "box" => EstimateBoxBoundingRadius(trigger),
            _ => Math.Max(0.0001, ReadNumber(trigger.Properties, "radius", 1))
        };
        var position = is2D
            ? new RekallAgeRuntimeVector3(entity.Transform.Position2D.X, entity.Transform.Position2D.Y, 0)
            : entity.Transform.Position3D;
        return new TriggerBody(entity, trigger, shape, position, radius);
    }

    private static ColliderBody? CreateColliderBody(RekallAgeRuntimeEntity entity)
    {
        var collider = entity.Components.FirstOrDefault(IsCollider);
        if (collider is null)
        {
            return null;
        }

        var position = Is2DCollider(collider)
            ? new RekallAgeRuntimeVector3(entity.Transform.Position2D.X, entity.Transform.Position2D.Y, 0)
            : entity.Transform.Position3D;
        return new ColliderBody(entity, collider, position, EstimateColliderRadius(entity, collider));
    }

    private static bool IsCollider(RekallAgeRuntimeComponent component)
    {
        return component.Type is
            "Rekall.BoxCollider2D" or
            "Rekall.CircleCollider2D" or
            "Rekall.BoxCollider3D" or
            "Rekall.SphereCollider3D" or
            "Rekall.CapsuleCollider3D" or
            "Rekall.MeshCollider";
    }

    private static double EstimateColliderRadius(
        RekallAgeRuntimeEntity entity,
        RekallAgeRuntimeComponent collider)
    {
        return collider.Type switch
        {
            "Rekall.CircleCollider2D" => Math.Max(0.0001, ReadNumber(collider.Properties, "radius", 0.5)),
            "Rekall.BoxCollider2D" => EstimateBox2DBoundingRadius(collider),
            "Rekall.SphereCollider3D" => Math.Max(0.0001, ReadNumber(collider.Properties, "radius", 0.5)),
            "Rekall.CapsuleCollider3D" => (Math.Max(0.0001, ReadNumber(collider.Properties, "radius", 0.5))
                + Math.Max(0.0001, ReadNumber(collider.Properties, "length", 1)) * 0.5),
            "Rekall.BoxCollider3D" => EstimateBoxBoundingRadius(collider),
            _ => 1
        };
    }

    private static bool Is2DCollider(RekallAgeRuntimeComponent component)
    {
        return component.Type is "Rekall.BoxCollider2D" or "Rekall.CircleCollider2D";
    }

    private static double EstimateBox2DBoundingRadius(
        RekallAgeRuntimeComponent collider)
    {
        var width = Math.Max(0.0001, ReadNumber(collider.Properties, "width", 1));
        var height = Math.Max(0.0001, ReadNumber(collider.Properties, "height", 1));
        return Math.Sqrt(width * width + height * height) * 0.5;
    }

    private static double EstimateBoxBoundingRadius(
        RekallAgeRuntimeComponent component)
    {
        var width = Math.Max(0.0001, ReadNumber(component.Properties, "width", 1));
        var height = Math.Max(0.0001, ReadNumber(component.Properties, "height", 1));
        var depth = Math.Max(0.0001, ReadNumber(component.Properties, "depth", 1));
        return Math.Sqrt(width * width + height * height + depth * depth) * 0.5;
    }

    private static bool Overlaps(TriggerBody trigger, ColliderBody body)
    {
        var dx = trigger.Position.X - body.Position.X;
        var dy = trigger.Position.Y - body.Position.Y;
        var dz = trigger.Position.Z - body.Position.Z;
        var range = trigger.Radius + body.Radius;
        return dx * dx + dy * dy + dz * dz <= range * range;
    }

    private static IReadOnlyList<TriggerEventBinding> EventBindingsFor(
        RekallAgeRuntimeEntity target,
        string type)
    {
        var bindings = new List<TriggerEventBinding>();
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
                    bindings.Add(new TriggerEventBinding(Clean(ReadString(eventNode, "handler"))));
                }
            }
        }

        return bindings;
    }

    private static string? Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
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

    private sealed record TriggerBody(
        RekallAgeRuntimeEntity Entity,
        RekallAgeRuntimeComponent Trigger,
        string Shape,
        RekallAgeRuntimeVector3 Position,
        double Radius);

    private sealed record ColliderBody(
        RekallAgeRuntimeEntity Entity,
        RekallAgeRuntimeComponent Collider,
        RekallAgeRuntimeVector3 Position,
        double Radius);

    private sealed record TriggerEventBinding(string? Handler);
}
