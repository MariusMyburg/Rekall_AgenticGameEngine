using System.Globalization;
using System.Text.Json.Nodes;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeCameraTarget3DSystem : IRekallAgeRuntimeWorldSystem
{
    public string Id => "runtime.camera.target3d";

    public int Priority => 100;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeWorldFrameContext context)
    {
        var entities = world.Entities
            .Select(entity => ApplyCameraTarget(entity, world.Entities))
            .ToArray();
        return ValueTask.FromResult(world with { Entities = entities });
    }

    private static RekallAgeRuntimeEntity ApplyCameraTarget(
        RekallAgeRuntimeEntity entity,
        IReadOnlyList<RekallAgeRuntimeEntity> entities)
    {
        var cameraTarget = entity.Components.FirstOrDefault(component =>
            component.Type.Equals("Rekall.CameraTarget3D", StringComparison.Ordinal));
        if (cameraTarget is null || !ReadBoolean(cameraTarget.Properties, "active", true))
        {
            return entity;
        }

        var target = ResolveTarget(cameraTarget.Properties, entities);
        if (target is null)
        {
            return entity;
        }

        var offset = new RekallAgeRuntimeVector3(
            ReadNumber(cameraTarget.Properties, "offsetX", 0),
            ReadNumber(cameraTarget.Properties, "offsetY", 0),
            ReadNumber(cameraTarget.Properties, "offsetZ", 0));
        var targetOffset = new RekallAgeRuntimeVector3(
            ReadNumber(cameraTarget.Properties, "targetOffsetX", 0),
            ReadNumber(cameraTarget.Properties, "targetOffsetY", 0),
            ReadNumber(cameraTarget.Properties, "targetOffsetZ", 0));
        var followPosition = ReadBoolean(cameraTarget.Properties, "followPosition", true);
        var lookAt = ReadBoolean(cameraTarget.Properties, "lookAt", true);

        var currentPosition = entity.Transform.Position3D;
        var targetPosition = target.Transform.Position3D;
        var cameraPosition = followPosition
            ? Add(targetPosition, offset)
            : currentPosition;
        var rotation = entity.Transform.Rotation3D;
        if (lookAt)
        {
            var aimPoint = Add(targetPosition, targetOffset);
            rotation = DirectionToRotation(Subtract(aimPoint, cameraPosition), rotation.Z);
        }

        return entity with
        {
            Transform = entity.Transform with
            {
                Position3D = cameraPosition,
                Rotation3D = rotation
            }
        };
    }

    private static RekallAgeRuntimeEntity? ResolveTarget(
        JsonObject properties,
        IReadOnlyList<RekallAgeRuntimeEntity> entities)
    {
        var targetEntityId = ReadString(properties, "targetEntityId") ?? ReadString(properties, "entityId");
        if (!string.IsNullOrWhiteSpace(targetEntityId))
        {
            var target = entities.FirstOrDefault(entity =>
                entity.Id.Equals(targetEntityId, StringComparison.Ordinal));
            if (target is not null)
            {
                return target;
            }
        }

        var targetName = ReadString(properties, "targetName") ?? ReadString(properties, "target");
        if (!string.IsNullOrWhiteSpace(targetName))
        {
            var target = entities.FirstOrDefault(entity =>
                entity.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase));
            if (target is not null)
            {
                return target;
            }
        }

        var targetTag = ReadString(properties, "targetTag");
        return string.IsNullOrWhiteSpace(targetTag)
            ? null
            : entities.FirstOrDefault(entity => entity.Tags.Any(tag =>
                tag.Equals(targetTag, StringComparison.OrdinalIgnoreCase)));
    }

    private static RekallAgeRuntimeVector3 DirectionToRotation(
        RekallAgeRuntimeVector3 direction,
        double rollDegrees)
    {
        var length = Math.Sqrt(
            direction.X * direction.X
            + direction.Y * direction.Y
            + direction.Z * direction.Z);
        if (length <= 0.000001)
        {
            return new RekallAgeRuntimeVector3(0, 0, rollDegrees);
        }

        var x = direction.X / length;
        var y = direction.Y / length;
        var z = direction.Z / length;
        var pitch = -Math.Asin(Math.Clamp(y, -1, 1)) * 180.0 / Math.PI;
        var yaw = Math.Atan2(x, z) * 180.0 / Math.PI;
        return new RekallAgeRuntimeVector3(pitch, yaw, rollDegrees);
    }

    private static RekallAgeRuntimeVector3 Add(
        RekallAgeRuntimeVector3 a,
        RekallAgeRuntimeVector3 b)
    {
        return new RekallAgeRuntimeVector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    }

    private static RekallAgeRuntimeVector3 Subtract(
        RekallAgeRuntimeVector3 a,
        RekallAgeRuntimeVector3 b)
    {
        return new RekallAgeRuntimeVector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    }

    private static string? ReadString(JsonObject properties, string name)
    {
        return properties.TryGetPropertyValue(name, out var node)
            && node is JsonValue value
            && value.TryGetValue<string>(out var text)
            ? text
            : null;
    }

    private static bool ReadBoolean(JsonObject properties, string name, bool fallback)
    {
        if (!properties.TryGetPropertyValue(name, out var node) || node is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<bool>(out var boolean))
        {
            return boolean;
        }

        return value.TryGetValue<string>(out var text) && bool.TryParse(text, out var parsed)
            ? parsed
            : fallback;
    }

    private static double ReadNumber(JsonObject properties, string name, double fallback)
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
}
