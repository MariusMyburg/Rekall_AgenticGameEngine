using System.Globalization;
using System.Text.Json.Nodes;
using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeCollisionEventSystem : IRekallAgeRuntimeWorldSystem
{
    private const string CollisionStateComponent = "Rekall.CollisionState";

    public string Id => "runtime.events.collision";

    public int Priority => 10;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeWorldFrameContext context)
    {
        // Collision bodies are derived entirely from collider components; with none present
        // there are no pairs to test and no entity changes. This also skips the O(n^2)
        // broadphase below, which remains a scaling concern for collider-heavy scenes. The
        // canonical event ordering is still applied, as other systems' events depend on it.
        if (!RekallAgeRuntimeComponentPresence.AnyEntityHasCollider(world))
        {
            return ValueTask.FromResult(RekallAgeRuntimeEventOrdering.WithCanonicalEvents(world));
        }

        var bodies = world.Entities
            .Where(entity => entity.Visible)
            .Select(CreateCollisionBody)
            .Where(body => body is not null)
            .Select(body => body!)
            .ToArray();
        var currentOverlaps = bodies.ToDictionary(
            body => body.Entity.Id,
            _ => new HashSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);

        for (var leftIndex = 0; leftIndex < bodies.Length; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < bodies.Length; rightIndex++)
            {
                var left = bodies[leftIndex];
                var right = bodies[rightIndex];
                if (!Overlaps(left, right) || !RekallAgeCollisionFilter.Allows(left.Entity, right.Entity))
                {
                    continue;
                }

                currentOverlaps[left.Entity.Id].Add(right.Entity.Id);
                currentOverlaps[right.Entity.Id].Add(left.Entity.Id);
            }
        }

        var bodyById = bodies.ToDictionary(body => body.Entity.Id, StringComparer.Ordinal);
        var entityById = world.Entities.ToDictionary(entity => entity.Id, StringComparer.Ordinal);
        var emitted = new List<RekallAgeRuntimeEvent>();
        var updatedEntities = world.Entities.Select(entity =>
        {
            if (!currentOverlaps.TryGetValue(entity.Id, out var current))
            {
                return entity;
            }

            var previous = ReadPreviousOverlaps(entity);
            EmitTransitions(
                emitted,
                entity,
                current,
                previous,
                bodyById,
                entityById,
                context.FrameIndex);
            return entity.UpsertComponent(CollisionStateComponent, CreateState(current));
        }).ToArray();

        return ValueTask.FromResult(world with
        {
            Entities = updatedEntities,
            Subsystems = world.Subsystems with
            {
                Events = new RekallAgeRuntimeEventView(
                    RekallAgeRuntimeEventOrdering.Canonical(world.Subsystems.Events.Events.Concat(emitted)))
            }
        });
    }

    private static void EmitTransitions(
        List<RekallAgeRuntimeEvent> emitted,
        RekallAgeRuntimeEntity entity,
        HashSet<string> current,
        HashSet<string> previous,
        IReadOnlyDictionary<string, CollisionBody> bodyById,
        IReadOnlyDictionary<string, RekallAgeRuntimeEntity> entityById,
        int frame)
    {
        foreach (var otherId in current.Except(previous, StringComparer.Ordinal))
        {
            EmitBoundEvents(emitted, "collision.begin", entity, otherId, bodyById, entityById, frame);
        }

        foreach (var otherId in current.Intersect(previous, StringComparer.Ordinal))
        {
            EmitBoundEvents(emitted, "collision.stay", entity, otherId, bodyById, entityById, frame);
        }

        foreach (var otherId in previous.Except(current, StringComparer.Ordinal))
        {
            EmitBoundEvents(emitted, "collision.end", entity, otherId, bodyById, entityById, frame);
        }
    }

    private static void EmitBoundEvents(
        List<RekallAgeRuntimeEvent> emitted,
        string type,
        RekallAgeRuntimeEntity entity,
        string otherId,
        IReadOnlyDictionary<string, CollisionBody> bodyById,
        IReadOnlyDictionary<string, RekallAgeRuntimeEntity> entityById,
        int frame)
    {
        if (!entityById.TryGetValue(otherId, out var other))
        {
            return;
        }

        foreach (var binding in EventBindingsFor(entity, type))
        {
            emitted.Add(new RekallAgeRuntimeEvent(
                frame,
                type,
                entity.Id,
                entity.Name,
                "runtime.collision",
                binding.Handler,
                CreatePayload(entity, other, bodyById)));
        }
    }

    private static JsonObject CreatePayload(
        RekallAgeRuntimeEntity entity,
        RekallAgeRuntimeEntity other,
        IReadOnlyDictionary<string, CollisionBody> bodyById)
    {
        var payload = new JsonObject
        {
            ["otherEntityId"] = other.Id,
            ["otherEntityName"] = other.Name
        };

        if (bodyById.TryGetValue(entity.Id, out var body))
        {
            payload["colliderType"] = body.Collider.Type;
        }

        if (bodyById.TryGetValue(other.Id, out var otherBody))
        {
            payload["otherColliderType"] = otherBody.Collider.Type;
        }

        return payload;
    }

    private static JsonObject CreateState(IEnumerable<string> overlaps)
    {
        var array = new JsonArray();
        foreach (var overlap in overlaps.OrderBy(value => value, StringComparer.Ordinal))
        {
            array.Add(overlap);
        }

        return new JsonObject { ["overlaps"] = array };
    }

    private static HashSet<string> ReadPreviousOverlaps(RekallAgeRuntimeEntity entity)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var state = entity.FindComponent(CollisionStateComponent);
        if (state is null
            || !TryGetPropertyValue(state.Properties, "overlaps", out var overlapsNode)
            || overlapsNode is not JsonArray overlaps)
        {
            return result;
        }

        foreach (var overlap in overlaps)
        {
            if (overlap is JsonValue value
                && value.TryGetValue<string>(out var text)
                && !string.IsNullOrWhiteSpace(text))
            {
                result.Add(text.Trim());
            }
        }

        return result;
    }

    private static CollisionBody? CreateCollisionBody(RekallAgeRuntimeEntity entity)
    {
        var collider = entity.Components.FirstOrDefault(IsCollider);
        if (collider is null)
        {
            return null;
        }

        var position = Is2DCollider(collider)
            ? new RekallAgeRuntimeVector3(entity.Transform.Position2D.X, entity.Transform.Position2D.Y, 0)
            : entity.Transform.Position3D;
        return new CollisionBody(entity, collider, position, EstimateBoundingRadius(entity, collider));
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

    private static double EstimateBoundingRadius(
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
        RekallAgeRuntimeComponent collider)
    {
        var width = Math.Max(0.0001, ReadNumber(collider.Properties, "width", 1));
        var height = Math.Max(0.0001, ReadNumber(collider.Properties, "height", 1));
        var depth = Math.Max(0.0001, ReadNumber(collider.Properties, "depth", 1));
        return Math.Sqrt(width * width + height * height + depth * depth) * 0.5;
    }

    private static bool Overlaps(CollisionBody left, CollisionBody right)
    {
        var dx = left.Position.X - right.Position.X;
        var dy = left.Position.Y - right.Position.Y;
        var dz = left.Position.Z - right.Position.Z;
        var range = left.Radius + right.Radius;
        return dx * dx + dy * dy + dz * dz <= range * range;
    }

    private static IReadOnlyList<CollisionEventBinding> EventBindingsFor(
        RekallAgeRuntimeEntity target,
        string type)
    {
        var bindings = new List<CollisionEventBinding>();
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
                    bindings.Add(new CollisionEventBinding(Clean(ReadString(eventNode, "handler"))));
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

    private sealed record CollisionBody(
        RekallAgeRuntimeEntity Entity,
        RekallAgeRuntimeComponent Collider,
        RekallAgeRuntimeVector3 Position,
        double Radius);

    private sealed record CollisionEventBinding(string? Handler);
}
