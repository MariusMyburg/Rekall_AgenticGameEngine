using System.Globalization;
using System.Text.Json.Nodes;
using Rekall.Age.Modules;
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
        var deltaSeconds = context.DeltaTime.TotalSeconds;
        var entities = world.Entities
            .Select(entity => ApplyCameraTarget(entity, world, deltaSeconds))
            .ToArray();
        return ValueTask.FromResult(world with { Entities = entities });
    }

    private static RekallAgeRuntimeEntity ApplyCameraTarget(
        RekallAgeRuntimeEntity entity,
        RekallAgeRuntimeWorld world,
        double deltaSeconds)
    {
        var entities = world.Entities;
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

        var offset = ResolveOffset(cameraTarget.Properties, target, entities);
        var targetOffset = new RekallAgeRuntimeVector3(
            ReadNumber(cameraTarget.Properties, "targetOffsetX", 0),
            ReadNumber(cameraTarget.Properties, "targetOffsetY", 0),
            ReadNumber(cameraTarget.Properties, "targetOffsetZ", 0));
        var followPosition = ReadBoolean(cameraTarget.Properties, "followPosition", true);
        var lookAt = ReadBoolean(cameraTarget.Properties, "lookAt", true);

        var currentPosition = entity.Transform.Position3D;
        var targetPosition = target.Transform.Position3D;
        var instantPosition = followPosition
            ? Add(targetPosition, offset)
            : currentPosition;

        // Delayed-motion spring-arm behavior: instead of snapping to instantPosition every frame,
        // exponentially decay from the camera's own current position toward it. Off by default (an
        // authored scene that never set this property keeps the exact prior instant-follow
        // behavior, unchanged).
        var cameraPosition = followPosition && ReadBoolean(cameraTarget.Properties, "positionLagEnabled", false)
            ? ApplyPositionLag(
                currentPosition,
                instantPosition,
                Math.Max(0.0001, ReadNumber(cameraTarget.Properties, "positionLagSpeed", 10)),
                ReadNumber(cameraTarget.Properties, "maximumPositionLagDistance", 0),
                deltaSeconds)
            : instantPosition;

        // Real spring-arm collision avoidance: probe from the target out toward the (possibly
        // lagged) desired camera position with a single ray - not a true swept sphere/capsule,
        // since world.Raycast3D is itself a point ray, an honest, simpler approximation of what a
        // full arm-radius sweep would do - and if something is in the way, pull the camera in to
        // just short of the hit point instead of letting it clip through geometry.
        if (ReadBoolean(cameraTarget.Properties, "collisionAvoidanceEnabled", false))
        {
            cameraPosition = ApplyCollisionAvoidance(
                world,
                entity,
                target,
                targetPosition,
                cameraPosition,
                Math.Max(0, ReadNumber(cameraTarget.Properties, "collisionMinimumDistance", 0.1)));
        }

        var currentRotation = entity.Transform.Rotation3D;
        var rotation = currentRotation;
        if (lookAt)
        {
            // Aimed from cameraPosition (the actual, possibly-lagged position the camera ends up
            // at this frame), not the instant unlagged position - otherwise a lagging camera would
            // look toward the target from a point it isn't actually standing at.
            var aimPoint = Add(targetPosition, targetOffset);
            var instantRotation = DirectionToRotation(Subtract(aimPoint, cameraPosition), currentRotation.Z);
            rotation = ReadBoolean(cameraTarget.Properties, "rotationLagEnabled", false)
                ? ApplyRotationLag(
                    currentRotation,
                    instantRotation,
                    Math.Max(0.0001, ReadNumber(cameraTarget.Properties, "rotationLagSpeed", 10)),
                    deltaSeconds)
                : instantRotation;
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

    private static RekallAgeRuntimeVector3 ResolveOffset(
        JsonObject properties,
        RekallAgeRuntimeEntity target,
        IReadOnlyList<RekallAgeRuntimeEntity> entities)
    {
        var reference = ResolveOffsetReference(properties, entities);
        if (reference is not null)
        {
            var distance = ReadNumber(properties, "offsetDistance", 0);
            if (distance > 0.000001)
            {
                var targetPosition = target.Transform.Position3D;
                var referencePosition = reference.Transform.Position3D;
                var direction = Subtract(referencePosition, targetPosition);
                var mode = ReadString(properties, "offsetReferenceMode") ?? "toward";
                if (mode.Equals("away", StringComparison.OrdinalIgnoreCase)
                    || mode.Equals("awayFromReference", StringComparison.OrdinalIgnoreCase))
                {
                    direction = Subtract(targetPosition, referencePosition);
                }

                var forward = Normalize(direction, new RekallAgeRuntimeVector3(0, 0, 1));
                var right = Normalize(Cross(new RekallAgeRuntimeVector3(0, 1, 0), forward), new RekallAgeRuntimeVector3(1, 0, 0));
                var vertical = ReadNumber(properties, "offsetVertical", 0);
                var lateral = ReadNumber(properties, "offsetLateral", 0);
                return Add(
                    Add(Scale(forward, distance), Scale(right, lateral)),
                    new RekallAgeRuntimeVector3(0, vertical, 0));
            }
        }

        return new RekallAgeRuntimeVector3(
            ReadNumber(properties, "offsetX", 0),
            ReadNumber(properties, "offsetY", 0),
            ReadNumber(properties, "offsetZ", 0));
    }

    private static RekallAgeRuntimeEntity? ResolveOffsetReference(
        JsonObject properties,
        IReadOnlyList<RekallAgeRuntimeEntity> entities)
    {
        var reference = new JsonObject();
        if (ReadString(properties, "offsetReferenceEntityId") is { Length: > 0 } entityId)
        {
            reference["targetEntityId"] = entityId;
        }

        if (ReadString(properties, "offsetReferenceName") is { Length: > 0 } name)
        {
            reference["targetName"] = name;
        }

        if (ReadString(properties, "offsetReferenceTag") is { Length: > 0 } tag)
        {
            reference["targetTag"] = tag;
        }

        return reference.Count == 0 ? null : ResolveTarget(reference, entities);
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

    /// <summary>
    /// Exponentially decays from the camera's current position toward the freshly computed instant
    /// target+offset position - the standard spring-arm-lite "camera lag" technique (framerate-
    /// independent because it's a continuous decay rate, not a fixed per-frame step). When
    /// maximumLagDistance is positive, clamps how far behind the instant position the result may
    /// fall, so a sudden fast target movement can't leave the camera arbitrarily far behind.
    /// </summary>
    private static RekallAgeRuntimeVector3 ApplyPositionLag(
        RekallAgeRuntimeVector3 current,
        RekallAgeRuntimeVector3 instant,
        double lagSpeed,
        double maximumLagDistance,
        double deltaSeconds)
    {
        var t = Math.Clamp(1.0 - Math.Exp(-lagSpeed * Math.Max(0, deltaSeconds)), 0, 1);
        var lagged = new RekallAgeRuntimeVector3(
            current.X + (instant.X - current.X) * t,
            current.Y + (instant.Y - current.Y) * t,
            current.Z + (instant.Z - current.Z) * t);

        if (maximumLagDistance <= 0.000001)
        {
            return lagged;
        }

        var offset = Subtract(lagged, instant);
        var distance = Math.Sqrt(offset.X * offset.X + offset.Y * offset.Y + offset.Z * offset.Z);
        return distance <= maximumLagDistance
            ? lagged
            : Add(instant, Scale(offset, maximumLagDistance / distance));
    }

    /// <summary>
    /// Same exponential-decay idea as <see cref="ApplyPositionLag"/>, applied per-axis to the
    /// look-at rotation using shortest-path angle interpolation (so decaying from, say, 359 degrees
    /// toward 1 degree steps the short way through 0 rather than the long way around through 180).
    /// </summary>
    private static RekallAgeRuntimeVector3 ApplyRotationLag(
        RekallAgeRuntimeVector3 current,
        RekallAgeRuntimeVector3 instant,
        double lagSpeed,
        double deltaSeconds)
    {
        var t = Math.Clamp(1.0 - Math.Exp(-lagSpeed * Math.Max(0, deltaSeconds)), 0, 1);
        return new RekallAgeRuntimeVector3(
            LerpAngleDegrees(current.X, instant.X, t),
            LerpAngleDegrees(current.Y, instant.Y, t),
            LerpAngleDegrees(current.Z, instant.Z, t));
    }

    private static double LerpAngleDegrees(double current, double target, double t)
    {
        var diff = target - current;
        var wrapped = diff - 360.0 * Math.Floor((diff + 180.0) / 360.0);
        return current + wrapped * t;
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

    private static RekallAgeRuntimeVector3 Scale(RekallAgeRuntimeVector3 vector, double scalar)
    {
        return new RekallAgeRuntimeVector3(vector.X * scalar, vector.Y * scalar, vector.Z * scalar);
    }

    private static RekallAgeRuntimeVector3 Cross(
        RekallAgeRuntimeVector3 a,
        RekallAgeRuntimeVector3 b)
    {
        return new RekallAgeRuntimeVector3(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);
    }

    private static RekallAgeRuntimeVector3 Normalize(
        RekallAgeRuntimeVector3 vector,
        RekallAgeRuntimeVector3 fallback)
    {
        var length = Length(vector);
        return length <= 0.000001
            ? fallback
            : new RekallAgeRuntimeVector3(vector.X / length, vector.Y / length, vector.Z / length);
    }

    private static double Length(RekallAgeRuntimeVector3 vector)
    {
        return Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y + vector.Z * vector.Z);
    }

    /// <summary>
    /// Probes a single ray from the target's position toward the desired camera position (via the
    /// already-generic, physics-independent <see cref="RekallAgeRuntimeModuleSdk.Raycast3D"/> the
    /// pointer/picking system already uses - not a new physics dependency). If the nearest hit
    /// (excluding the target and camera entities themselves, so neither ever obstructs its own arm)
    /// is closer than the desired distance, pulls the camera in along the same line to
    /// <paramref name="minimumDistance"/> short of that hit, the same way a real spring arm's
    /// collision probe prevents the camera clipping through geometry between it and its target.
    /// </summary>
    private static RekallAgeRuntimeVector3 ApplyCollisionAvoidance(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeEntity cameraEntity,
        RekallAgeRuntimeEntity target,
        RekallAgeRuntimeVector3 targetPosition,
        RekallAgeRuntimeVector3 desiredPosition,
        double minimumDistance)
    {
        var toCamera = Subtract(desiredPosition, targetPosition);
        var desiredDistance = Length(toCamera);
        if (desiredDistance <= 0.000001)
        {
            return desiredPosition;
        }

        var direction = Scale(toCamera, 1.0 / desiredDistance);
        var hit = world.Raycast3D(targetPosition, direction, desiredDistance)
            .FirstOrDefault(candidate =>
                !candidate.Entity.Id.Equals(target.Id, StringComparison.Ordinal)
                && !candidate.Entity.Id.Equals(cameraEntity.Id, StringComparison.Ordinal));
        if (hit is null)
        {
            return desiredPosition;
        }

        // Math.Min/Max rather than Math.Clamp deliberately: minimumDistance could theoretically be
        // authored larger than desiredDistance itself, which would make Math.Clamp's min > max and
        // throw - this stays well-defined (falls back toward minimumDistance) in that case instead.
        var clampedDistance = Math.Max(minimumDistance, Math.Min(desiredDistance, hit.Distance - minimumDistance));
        return Add(targetPosition, Scale(direction, clampedDistance));
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
