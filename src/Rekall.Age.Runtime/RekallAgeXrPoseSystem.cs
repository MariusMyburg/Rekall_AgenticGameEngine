using System.Text.Json.Nodes;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeXrPoseSystem : IRekallAgeRuntimeWorldSystem
{
    public string Id => "runtime.xr.pose";

    public int Priority => -900;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeWorldFrameContext context)
    {
        var poses = (context.Input.XrPoses ?? Array.Empty<RekallAgeRuntimeXrPose>())
            .Where(pose => !string.IsNullOrWhiteSpace(pose.Source))
            .ToDictionary(pose => Normalize(pose.Source), pose => pose, StringComparer.OrdinalIgnoreCase);
        var entities = world.Entities
            .Select(entity => ApplyPose(entity, poses, out _))
            .ToArray();
        var trackedPoses = entities
            .Select(entity => CreateTrackedPose(entity, poses))
            .OfType<RekallAgeRuntimeXrTrackedPose>()
            .OrderBy(pose => pose.EntityName, StringComparer.Ordinal)
            .ThenBy(pose => pose.EntityId, StringComparer.Ordinal)
            .ToArray();
        var controllers = entities
            .Select(CreateController)
            .OfType<RekallAgeRuntimeXrController>()
            .OrderBy(controller => controller.EntityName, StringComparer.Ordinal)
            .ThenBy(controller => controller.EntityId, StringComparer.Ordinal)
            .ToArray();
        var rigs = entities
            .Select(CreateRig)
            .OfType<RekallAgeRuntimeXrRig>()
            .OrderBy(rig => rig.EntityName, StringComparer.Ordinal)
            .ThenBy(rig => rig.EntityId, StringComparer.Ordinal)
            .ToArray();
        var actions = (context.Input.XrActions ?? Array.Empty<RekallAgeRuntimeXrAction>())
            .Where(action => !string.IsNullOrWhiteSpace(action.Hand) && !string.IsNullOrWhiteSpace(action.Name))
            .OrderBy(action => action.Hand, StringComparer.OrdinalIgnoreCase)
            .ThenBy(action => action.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return ValueTask.FromResult(world with
        {
            Entities = entities,
            Subsystems = world.Subsystems with
            {
                Xr = new RekallAgeRuntimeXrView(rigs, controllers, trackedPoses, actions)
            }
        });
    }

    private static RekallAgeRuntimeEntity ApplyPose(
        RekallAgeRuntimeEntity entity,
        IReadOnlyDictionary<string, RekallAgeRuntimeXrPose> poses,
        out RekallAgeRuntimeXrPose? pose)
    {
        pose = null;
        var source = entity.Components.FirstOrDefault(component =>
            component.Type.Equals("Rekall.XrPoseSource", StringComparison.Ordinal));
        if (source is null || !ReadBoolean(source.Properties, "active", true))
        {
            return entity;
        }

        var sourceName = Normalize(ReadString(source.Properties, "source") ?? string.Empty);
        if (string.IsNullOrWhiteSpace(sourceName)
            || !poses.TryGetValue(sourceName, out pose)
            || !pose.IsTracked)
        {
            return entity;
        }

        var applyPosition = ReadBoolean(source.Properties, "applyPosition", true);
        var applyRotation = ReadBoolean(source.Properties, "applyRotation", true);
        return entity with
        {
            Transform = entity.Transform with
            {
                Position3D = applyPosition
                    ? new RekallAgeRuntimeVector3(pose.X, pose.Y, pose.Z)
                    : entity.Transform.Position3D,
                Rotation3D = applyRotation
                    ? new RekallAgeRuntimeVector3(pose.Pitch, pose.Yaw, pose.Roll)
                    : entity.Transform.Rotation3D
            }
        };
    }

    private static RekallAgeRuntimeXrTrackedPose? CreateTrackedPose(
        RekallAgeRuntimeEntity entity,
        IReadOnlyDictionary<string, RekallAgeRuntimeXrPose> poses)
    {
        var source = entity.Components.FirstOrDefault(component =>
            component.Type.Equals("Rekall.XrPoseSource", StringComparison.Ordinal));
        if (source is null || !ReadBoolean(source.Properties, "active", true))
        {
            return null;
        }

        var sourceName = Normalize(ReadString(source.Properties, "source") ?? string.Empty);
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return null;
        }

        poses.TryGetValue(sourceName, out var pose);
        return new RekallAgeRuntimeXrTrackedPose(
            entity.Id,
            entity.Name,
            sourceName,
            pose?.IsTracked ?? false,
            entity.Transform.Position3D.X,
            entity.Transform.Position3D.Y,
            entity.Transform.Position3D.Z,
            entity.Transform.Rotation3D.X,
            entity.Transform.Rotation3D.Y,
            entity.Transform.Rotation3D.Z);
    }

    private static RekallAgeRuntimeXrController? CreateController(RekallAgeRuntimeEntity entity)
    {
        var controller = entity.Components.FirstOrDefault(component =>
            component.Type.Equals("Rekall.XrController", StringComparison.Ordinal));
        if (controller is null)
        {
            return null;
        }

        var hand = Normalize(ReadString(controller.Properties, "hand") ?? "unknown");
        return new RekallAgeRuntimeXrController(
            entity.Id,
            entity.Name,
            hand,
            Normalize(ReadString(controller.Properties, "poseSource") ?? $"{hand}-hand"),
            ReadBoolean(controller.Properties, "active", true));
    }

    private static RekallAgeRuntimeXrRig? CreateRig(RekallAgeRuntimeEntity entity)
    {
        var rig = entity.Components.FirstOrDefault(component =>
            component.Type.Equals("Rekall.XrRig", StringComparison.Ordinal));
        if (rig is null)
        {
            return null;
        }

        return new RekallAgeRuntimeXrRig(
            entity.Id,
            entity.Name,
            Normalize(ReadString(rig.Properties, "trackingSpace") ?? "local-floor"),
            Normalize(ReadString(rig.Properties, "viewConfiguration") ?? "primary-stereo"),
            ReadBoolean(rig.Properties, "active", true));
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToLowerInvariant();
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

        return value.TryGetValue<string>(out var text) && bool.TryParse(text, out var parsed)
            ? parsed
            : fallback;
    }

    private static bool TryGetPropertyValue(JsonObject properties, string name, out JsonNode? node)
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
