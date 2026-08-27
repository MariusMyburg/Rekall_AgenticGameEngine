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

        // Real spring-arm collision avoidance: sweep a sphere from the target out toward the
        // (possibly lagged) desired camera position, approximating obstructions as bounding spheres
        // around their colliders (see ApplyCollisionAvoidance's own doc comment) - and if something
        // is in the way, pull the camera in to just short of the hit point instead of letting it
        // clip through geometry.
        if (ReadBoolean(cameraTarget.Properties, "collisionAvoidanceEnabled", false))
        {
            cameraPosition = ApplyCollisionAvoidance(
                world,
                entity,
                target,
                targetPosition,
                cameraPosition,
                Math.Max(0, ReadNumber(cameraTarget.Properties, "collisionMinimumDistance", 0.1)),
                Math.Max(0, ReadNumber(cameraTarget.Properties, "collisionProbeRadius", 0.15)));
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
    /// Sweeps a sphere of <paramref name="probeRadius"/> from the target's position toward the
    /// desired camera position - not a single ray - so a thin obstruction near the edge of where
    /// the camera would sit is still caught, the same way a real spring arm's own collision channel
    /// (which sweeps its actual camera-mount radius, not an infinitely thin line) behaves. Each
    /// candidate obstruction is approximated as a bounding sphere around its collider (the same
    /// approximation <see cref="RekallAgeTriggerEventSystem"/> already uses for trigger-volume
    /// overlap, not a new kind of imprecision this feature introduces) so the sweep reduces to an
    /// ordinary ray-vs-sphere test against (probeRadius + that bounding radius). If the nearest hit
    /// (excluding the target and camera entities themselves, so neither ever obstructs its own arm)
    /// is closer than the desired distance, pulls the camera in along the same line to
    /// <paramref name="minimumDistance"/> short of that hit.
    /// </summary>
    private static RekallAgeRuntimeVector3 ApplyCollisionAvoidance(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeEntity cameraEntity,
        RekallAgeRuntimeEntity target,
        RekallAgeRuntimeVector3 targetPosition,
        RekallAgeRuntimeVector3 desiredPosition,
        double minimumDistance,
        double probeRadius)
    {
        var toCamera = Subtract(desiredPosition, targetPosition);
        var desiredDistance = Length(toCamera);
        if (desiredDistance <= 0.000001)
        {
            return desiredPosition;
        }

        var direction = Scale(toCamera, 1.0 / desiredDistance);
        double? nearestHitDistance = null;
        foreach (var entity in world.Entities)
        {
            if (!entity.Visible
                || entity.Id.Equals(target.Id, StringComparison.Ordinal)
                || entity.Id.Equals(cameraEntity.Id, StringComparison.Ordinal))
            {
                continue;
            }

            var collider = entity.Components.FirstOrDefault(Is3DCollider);
            if (collider is null)
            {
                continue;
            }

            var sphereCenter = entity.Transform.Position3D;
            var sphereRadius = probeRadius + EstimateColliderBoundingRadius(collider);
            var entryDistance = RaySphereEntryDistance(targetPosition, direction, sphereCenter, sphereRadius);
            if (entryDistance is not { } distance || distance > desiredDistance)
            {
                continue;
            }

            if (nearestHitDistance is null || distance < nearestHitDistance)
            {
                nearestHitDistance = distance;
            }
        }

        if (nearestHitDistance is not { } hitDistance)
        {
            return desiredPosition;
        }

        // Math.Min/Max rather than Math.Clamp deliberately: minimumDistance could theoretically be
        // authored larger than desiredDistance itself, which would make Math.Clamp's min > max and
        // throw - this stays well-defined (falls back toward minimumDistance) in that case instead.
        var clampedDistance = Math.Max(minimumDistance, Math.Min(desiredDistance, hitDistance - minimumDistance));
        return Add(targetPosition, Scale(direction, clampedDistance));
    }

    /// <summary>Ray-vs-sphere intersection: returns the distance along the ray (from
    /// <paramref name="origin"/>, in the already-normalized <paramref name="direction"/>) to the
    /// nearest entry point on the sphere, or null if the ray never enters it. If the origin already
    /// starts inside the sphere, returns 0 (the whole arm is already touching the obstruction).</summary>
    private static double? RaySphereEntryDistance(
        RekallAgeRuntimeVector3 origin,
        RekallAgeRuntimeVector3 direction,
        RekallAgeRuntimeVector3 sphereCenter,
        double sphereRadius)
    {
        var toCenter = Subtract(origin, sphereCenter);
        var b = Dot(toCenter, direction);
        var c = Dot(toCenter, toCenter) - sphereRadius * sphereRadius;
        var discriminant = b * b - c;
        if (discriminant < 0)
        {
            return null;
        }

        var entry = -b - Math.Sqrt(discriminant);
        return Math.Max(0, entry);
    }

    private static double Dot(RekallAgeRuntimeVector3 a, RekallAgeRuntimeVector3 b)
    {
        return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    }

    private static bool Is3DCollider(RekallAgeRuntimeComponent component)
    {
        return component.Type is
            "Rekall.BoxCollider3D" or
            "Rekall.SphereCollider3D" or
            "Rekall.CapsuleCollider3D" or
            "Rekall.MeshCollider";
    }

    /// <summary>Same collider-to-bounding-sphere-radius approximation
    /// <see cref="RekallAgeTriggerEventSystem"/> already uses for trigger-volume overlap.</summary>
    private static double EstimateColliderBoundingRadius(RekallAgeRuntimeComponent collider)
    {
        return collider.Type switch
        {
            "Rekall.SphereCollider3D" => Math.Max(0.0001, ReadNumber(collider.Properties, "radius", 0.5)),
            "Rekall.CapsuleCollider3D" => Math.Max(0.0001, ReadNumber(collider.Properties, "radius", 0.5))
                + Math.Max(0.0001, ReadNumber(collider.Properties, "length", 1)) * 0.5,
            "Rekall.BoxCollider3D" => EstimateBoxBoundingRadius(collider),
            _ => 1
        };
    }

    private static double EstimateBoxBoundingRadius(RekallAgeRuntimeComponent collider)
    {
        var width = Math.Max(0.0001, ReadNumber(collider.Properties, "width", 1));
        var height = Math.Max(0.0001, ReadNumber(collider.Properties, "height", 1));
        var depth = Math.Max(0.0001, ReadNumber(collider.Properties, "depth", 1));
        return Math.Sqrt(width * width + height * height + depth * depth) * 0.5;
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
