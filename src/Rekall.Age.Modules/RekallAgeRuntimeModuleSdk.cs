using System.Globalization;
using System.Text.Json.Nodes;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Modules;

public sealed record RekallAgeRuntimeRaycastHit(
    RekallAgeRuntimeEntity Entity,
    double Distance,
    RekallAgeRuntimeVector3 Point,
    string ColliderType);

public static class RekallAgeRuntimeModuleSdk
{
    public static RekallAgeRuntimeComponent? FindComponent(
        this RekallAgeRuntimeEntity entity,
        string componentType)
    {
        return entity.Components.FirstOrDefault(component =>
            component.Type.Equals(componentType, StringComparison.Ordinal));
    }

    public static bool HasTag(this RekallAgeRuntimeEntity entity, string tag)
    {
        return entity.Tags.Any(item => item.Equals(tag, StringComparison.OrdinalIgnoreCase));
    }

    public static RekallAgeRuntimeEntity WithPosition3D(
        this RekallAgeRuntimeEntity entity,
        RekallAgeRuntimeVector3 position)
    {
        return entity with
        {
            Transform = entity.Transform with { Position3D = position }
        };
    }

    public static RekallAgeRuntimeEntity WithRotation3D(
        this RekallAgeRuntimeEntity entity,
        RekallAgeRuntimeVector3 rotation)
    {
        return entity with
        {
            Transform = entity.Transform with { Rotation3D = rotation }
        };
    }

    public static RekallAgeRuntimeEntity WithScale3D(
        this RekallAgeRuntimeEntity entity,
        RekallAgeRuntimeVector3 scale)
    {
        return entity with
        {
            Transform = entity.Transform with { Scale3D = scale }
        };
    }

    public static RekallAgeRuntimeEntity UpsertComponent(
        this RekallAgeRuntimeEntity entity,
        string componentType,
        JsonObject properties)
    {
        var replaced = false;
        var components = entity.Components.Select(component =>
        {
            if (!component.Type.Equals(componentType, StringComparison.Ordinal))
            {
                return component;
            }

            replaced = true;
            return new RekallAgeRuntimeComponent(componentType, properties.DeepClone().AsObject());
        }).ToList();

        if (!replaced)
        {
            components.Add(new RekallAgeRuntimeComponent(componentType, properties.DeepClone().AsObject()));
        }

        return entity with
        {
            Components = components
                .OrderBy(component => component.Type, StringComparer.Ordinal)
                .ToArray()
        };
    }

    public static RekallAgeRuntimeEntity UpdateComponent(
        this RekallAgeRuntimeEntity entity,
        string componentType,
        Func<JsonObject, JsonObject> update)
    {
        return entity.UpsertComponent(
            componentType,
            update((entity.FindComponent(componentType)?.Properties.DeepClone() as JsonObject) ?? new JsonObject()));
    }

    public static IReadOnlyList<RekallAgeRuntimeRaycastHit> Raycast3D(
        this RekallAgeRuntimeWorld world,
        RekallAgeRuntimeVector3 origin,
        RekallAgeRuntimeVector3 direction,
        double range,
        string? tag = null,
        string? componentType = null)
    {
        if (range <= 0)
        {
            return Array.Empty<RekallAgeRuntimeRaycastHit>();
        }

        var normalized = Normalize(direction);
        if (LengthSquared(normalized) <= 0.000001)
        {
            return Array.Empty<RekallAgeRuntimeRaycastHit>();
        }

        var hits = new List<RekallAgeRuntimeRaycastHit>();
        foreach (var entity in world.Entities)
        {
            if (!entity.Visible)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(tag) && !entity.HasTag(tag))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(componentType) && entity.FindComponent(componentType) is null)
            {
                continue;
            }

            foreach (var collider in entity.Components.Where(Is3DCollider))
            {
                if (TryIntersectCollider(origin, normalized, range, entity, collider, out var distance, out var point))
                {
                    hits.Add(new RekallAgeRuntimeRaycastHit(entity, distance, point, collider.Type));
                    break;
                }
            }
        }

        return hits
            .OrderBy(hit => hit.Distance)
            .ThenBy(hit => hit.Entity.Name, StringComparer.Ordinal)
            .ThenBy(hit => hit.Entity.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public static double ReadNumber(this JsonObject properties, string name, double fallback)
    {
        if (!properties.TryGetPropertyValue(name, out var node) || node is not JsonValue value)
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

    public static bool ReadBoolean(this JsonObject properties, string name, bool fallback)
    {
        if (!properties.TryGetPropertyValue(name, out var node) || node is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<bool>(out var boolValue))
        {
            return boolValue;
        }

        return value.TryGetValue<string>(out var text) && bool.TryParse(text, out var parsed)
            ? parsed
            : fallback;
    }

    public static string? ReadString(this JsonObject properties, string name, string? fallback = null)
    {
        if (!properties.TryGetPropertyValue(name, out var node) || node is not JsonValue value)
        {
            return fallback;
        }

        return value.TryGetValue<string>(out var text) ? text : fallback;
    }

    private static bool Is3DCollider(RekallAgeRuntimeComponent component)
    {
        return component.Type is
            "Rekall.BoxCollider3D" or
            "Rekall.SphereCollider3D" or
            "Rekall.CapsuleCollider3D" or
            "Rekall.MeshCollider";
    }

    private static bool TryIntersectCollider(
        RekallAgeRuntimeVector3 origin,
        RekallAgeRuntimeVector3 direction,
        double range,
        RekallAgeRuntimeEntity entity,
        RekallAgeRuntimeComponent collider,
        out double distance,
        out RekallAgeRuntimeVector3 point)
    {
        return collider.Type switch
        {
            "Rekall.SphereCollider3D" => TryIntersectSphere(
                origin,
                direction,
                range,
                entity.Transform.Position3D,
                Math.Max(0.0001, collider.Properties.ReadNumber("radius", collider.Properties.ReadNumber("Radius", 0.5))),
                out distance,
                out point),
            "Rekall.CapsuleCollider3D" => TryIntersectSphere(
                origin,
                direction,
                range,
                entity.Transform.Position3D,
                Math.Max(0.0001, collider.Properties.ReadNumber("radius", collider.Properties.ReadNumber("Radius", 0.5))),
                out distance,
                out point),
            "Rekall.BoxCollider3D" => TryIntersectSphere(
                origin,
                direction,
                range,
                entity.Transform.Position3D,
                EstimateBoxBoundingRadius(entity, collider),
                out distance,
                out point),
            "Rekall.MeshCollider" => TryIntersectSphere(
                origin,
                direction,
                range,
                entity.Transform.Position3D,
                1,
                out distance,
                out point),
            _ => NoHit(out distance, out point)
        };
    }

    private static bool TryIntersectSphere(
        RekallAgeRuntimeVector3 origin,
        RekallAgeRuntimeVector3 direction,
        double range,
        RekallAgeRuntimeVector3 center,
        double radius,
        out double distance,
        out RekallAgeRuntimeVector3 point)
    {
        var oc = Subtract(origin, center);
        var b = 2 * Dot(oc, direction);
        var c = Dot(oc, oc) - radius * radius;
        var discriminant = b * b - 4 * c;
        if (discriminant < 0)
        {
            return NoHit(out distance, out point);
        }

        var sqrt = Math.Sqrt(discriminant);
        var t0 = (-b - sqrt) * 0.5;
        var t1 = (-b + sqrt) * 0.5;
        distance = t0 >= 0 ? t0 : t1;
        if (distance < 0 || distance > range)
        {
            return NoHit(out distance, out point);
        }

        point = Add(origin, Multiply(direction, distance));
        return true;
    }

    private static double EstimateBoxBoundingRadius(RekallAgeRuntimeEntity entity, RekallAgeRuntimeComponent collider)
    {
        var width = collider.Properties.ReadNumber("width", collider.Properties.ReadNumber("Width", 1)) * entity.Transform.Scale3D.X;
        var height = collider.Properties.ReadNumber("height", collider.Properties.ReadNumber("Height", 1)) * entity.Transform.Scale3D.Y;
        var depth = collider.Properties.ReadNumber("depth", collider.Properties.ReadNumber("Depth", 1)) * entity.Transform.Scale3D.Z;
        return Math.Sqrt(width * width + height * height + depth * depth) * 0.5;
    }

    private static bool NoHit(out double distance, out RekallAgeRuntimeVector3 point)
    {
        distance = 0;
        point = new RekallAgeRuntimeVector3(0, 0, 0);
        return false;
    }

    private static RekallAgeRuntimeVector3 Normalize(RekallAgeRuntimeVector3 value)
    {
        var length = Math.Sqrt(LengthSquared(value));
        return length <= 0.000001
            ? new RekallAgeRuntimeVector3(0, 0, 0)
            : new RekallAgeRuntimeVector3(value.X / length, value.Y / length, value.Z / length);
    }

    private static double LengthSquared(RekallAgeRuntimeVector3 value)
    {
        return Dot(value, value);
    }

    private static double Dot(RekallAgeRuntimeVector3 left, RekallAgeRuntimeVector3 right)
    {
        return left.X * right.X + left.Y * right.Y + left.Z * right.Z;
    }

    private static RekallAgeRuntimeVector3 Add(RekallAgeRuntimeVector3 left, RekallAgeRuntimeVector3 right)
    {
        return new RekallAgeRuntimeVector3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
    }

    private static RekallAgeRuntimeVector3 Subtract(RekallAgeRuntimeVector3 left, RekallAgeRuntimeVector3 right)
    {
        return new RekallAgeRuntimeVector3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
    }

    private static RekallAgeRuntimeVector3 Multiply(RekallAgeRuntimeVector3 value, double scalar)
    {
        return new RekallAgeRuntimeVector3(value.X * scalar, value.Y * scalar, value.Z * scalar);
    }
}
