using System.Globalization;
using System.Text.Json.Nodes;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeCameraTargetCycleInputSystem : IRekallAgeRuntimeWorldSystem
{
    public string Id => "runtime.input.camera_target_cycle";

    public int Priority => 95;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeWorldFrameContext context)
    {
        var entities = world.Entities
            .Select(entity => ApplyCycle(entity, world.Subsystems.Input.Actions))
            .ToArray();
        return ValueTask.FromResult(world with { Entities = entities });
    }

    private static RekallAgeRuntimeEntity ApplyCycle(
        RekallAgeRuntimeEntity entity,
        IReadOnlyList<RekallAgeRuntimeInputAction> actions)
    {
        if (!entity.Visible)
        {
            return entity;
        }

        var cycleIndex = FindComponentIndex(entity, "Rekall.CameraTargetCycleInput");
        var targetIndex = FindComponentIndex(entity, "Rekall.CameraTarget3D");
        if (cycleIndex < 0 || targetIndex < 0)
        {
            return entity;
        }

        var cycle = entity.Components[cycleIndex];
        if (!ReadBoolean(cycle.Properties, "active", true)
            || !TryGetPropertyValue(cycle.Properties, "targets", out var targetsNode)
            || targetsNode is not JsonArray targets
            || targets.Count == 0)
        {
            return entity;
        }

        var nextAction = ReadString(cycle.Properties, "nextAction") ?? "nextTarget";
        var previousAction = ReadString(cycle.Properties, "previousAction") ?? "previousTarget";
        var currentIndex = Math.Clamp(ReadInt(cycle.Properties, "currentIndex", 0), 0, targets.Count - 1);
        var delta = 0;
        if (WasPressed(actions, nextAction))
        {
            delta++;
        }

        if (WasPressed(actions, previousAction))
        {
            delta--;
        }

        var selectedIndex = delta == 0
            ? currentIndex
            : ((currentIndex + delta) % targets.Count + targets.Count) % targets.Count;
        var selected = ParseTarget(targets[selectedIndex]);
        if (selected is null)
        {
            return entity;
        }

        var components = entity.Components.ToArray();
        components[cycleIndex] = cycle with
        {
            Properties = SetNumber(cycle.Properties, "currentIndex", selectedIndex)
        };
        components[targetIndex] = entity.Components[targetIndex] with
        {
            Properties = ApplyTarget(entity.Components[targetIndex].Properties, selected)
        };

        var cameraIndex = FindCameraIndex(entity);
        if (cameraIndex >= 0 && selected.ProjectionMode is { Length: > 0 } projectionMode)
        {
            components[cameraIndex] = entity.Components[cameraIndex] with
            {
                Properties = SetStringProperty(entity.Components[cameraIndex].Properties, "projectionMode", projectionMode)
            };
        }

        if (cameraIndex >= 0 && selected.OrthographicSize is { } orthographicSize)
        {
            components[cameraIndex] = components[cameraIndex] with
            {
                Properties = SetNumber(components[cameraIndex].Properties, "orthographicSize", orthographicSize)
            };
        }

        if (cameraIndex >= 0 && selected.FieldOfView is { } fieldOfView)
        {
            components[cameraIndex] = components[cameraIndex] with
            {
                Properties = SetNumber(components[cameraIndex].Properties, "fieldOfView", fieldOfView)
            };
        }

        if (cameraIndex >= 0 && selected.CullingMask is { Length: > 0 } cullingMask)
        {
            components[cameraIndex] = components[cameraIndex] with
            {
                Properties = SetStringProperty(components[cameraIndex].Properties, "cullingMask", cullingMask)
            };
        }

        return entity with { Components = components };
    }

    private static CameraTarget? ParseTarget(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            return string.IsNullOrWhiteSpace(text)
                ? null
                : new CameraTarget(text.Trim(), null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
        }

        if (node is not JsonObject target)
        {
            return null;
        }

        var targetName = ReadString(target, "targetName") ?? ReadString(target, "name");
        var targetEntityId = ReadString(target, "targetEntityId") ?? ReadString(target, "entityId");
        var targetTag = ReadString(target, "targetTag") ?? ReadString(target, "tag");
        if (string.IsNullOrWhiteSpace(targetName)
            && string.IsNullOrWhiteSpace(targetEntityId)
            && string.IsNullOrWhiteSpace(targetTag))
        {
            return null;
        }

        return new CameraTarget(
            targetName,
            targetEntityId,
            targetTag,
            ReadOptionalNumber(target, "offsetX"),
            ReadOptionalNumber(target, "offsetY"),
            ReadOptionalNumber(target, "offsetZ"),
            ReadOptionalNumber(target, "targetOffsetX"),
            ReadOptionalNumber(target, "targetOffsetY"),
            ReadOptionalNumber(target, "targetOffsetZ"),
            ReadOptionalNumber(target, "fieldOfView"),
            ReadOptionalNumber(target, "fov"),
            ReadString(target, "projectionMode"),
            ReadOptionalNumber(target, "orthographicSize"),
            ReadString(target, "cullingMask"),
            ReadString(target, "offsetReferenceEntityId"),
            ReadString(target, "offsetReferenceName"),
            ReadString(target, "offsetReferenceTag"),
            ReadString(target, "offsetReferenceMode"),
            ReadOptionalNumber(target, "offsetDistance"),
            ReadOptionalNumber(target, "offsetVertical"),
            ReadOptionalNumber(target, "offsetLateral"));
    }

    private static JsonObject ApplyTarget(JsonObject source, CameraTarget target)
    {
        var properties = CloneProperties(source);
        SetString(properties, "targetName", target.TargetName);
        SetString(properties, "targetEntityId", target.TargetEntityId);
        SetString(properties, "targetTag", target.TargetTag);
        SetOptionalNumber(properties, "offsetX", target.OffsetX);
        SetOptionalNumber(properties, "offsetY", target.OffsetY);
        SetOptionalNumber(properties, "offsetZ", target.OffsetZ);
        SetOptionalNumber(properties, "targetOffsetX", target.TargetOffsetX);
        SetOptionalNumber(properties, "targetOffsetY", target.TargetOffsetY);
        SetOptionalNumber(properties, "targetOffsetZ", target.TargetOffsetZ);
        SetString(properties, "offsetReferenceEntityId", target.OffsetReferenceEntityId);
        SetString(properties, "offsetReferenceName", target.OffsetReferenceName);
        SetString(properties, "offsetReferenceTag", target.OffsetReferenceTag);
        SetString(properties, "offsetReferenceMode", target.OffsetReferenceMode);
        SetOptionalNumber(properties, "offsetDistance", target.OffsetDistance);
        SetOptionalNumber(properties, "offsetVertical", target.OffsetVertical);
        SetOptionalNumber(properties, "offsetLateral", target.OffsetLateral);
        properties["followPosition"] = true;
        properties["lookAt"] = true;
        properties["active"] = true;
        return properties;
    }

    private static bool WasPressed(IReadOnlyList<RekallAgeRuntimeInputAction> actions, string actionName)
    {
        return !string.IsNullOrWhiteSpace(actionName)
            && actions.Any(action =>
                action.Name.Equals(actionName, StringComparison.OrdinalIgnoreCase)
                && action.WasPressed);
    }

    private static int FindComponentIndex(RekallAgeRuntimeEntity entity, string type)
    {
        for (var i = 0; i < entity.Components.Count; i++)
        {
            if (entity.Components[i].Type.Equals(type, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindCameraIndex(RekallAgeRuntimeEntity entity)
    {
        for (var i = 0; i < entity.Components.Count; i++)
        {
            if (entity.Components[i].Type is "Rekall.Camera2D" or "Rekall.Camera3D")
            {
                return i;
            }
        }

        return -1;
    }

    private static JsonObject SetNumber(JsonObject source, string name, double value)
    {
        var copy = CloneProperties(source);
        copy[name] = value;
        return copy;
    }

    private static void SetString(JsonObject properties, string name, string? value)
    {
        properties[name] = value ?? string.Empty;
    }

    private static JsonObject SetStringProperty(JsonObject source, string name, string value)
    {
        var copy = CloneProperties(source);
        copy[name] = value;
        return copy;
    }

    private static void SetOptionalNumber(JsonObject properties, string name, double? value)
    {
        if (value is { } number)
        {
            properties[name] = number;
        }
    }

    private static JsonObject CloneProperties(JsonObject source)
    {
        var copy = new JsonObject();
        foreach (var property in source)
        {
            copy[property.Key] = property.Value?.DeepClone();
        }

        return copy;
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

    private static int ReadInt(JsonObject properties, string name, int fallback)
    {
        return (int)Math.Round(ReadNumber(properties, name, fallback));
    }

    private static double ReadNumber(JsonObject properties, string name, double fallback)
    {
        return ReadOptionalNumber(properties, name) ?? fallback;
    }

    private static double? ReadOptionalNumber(JsonObject properties, string name)
    {
        if (!TryGetPropertyValue(properties, name, out var node) || node is not JsonValue value)
        {
            return null;
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
            : null;
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

    private sealed record CameraTarget(
        string? TargetName,
        string? TargetEntityId,
        string? TargetTag,
        double? OffsetX,
        double? OffsetY,
        double? OffsetZ,
        double? TargetOffsetX,
        double? TargetOffsetY,
        double? TargetOffsetZ,
        double? FieldOfViewValue,
        double? Fov,
        string? ProjectionMode,
        double? OrthographicSize,
        string? CullingMask,
        string? OffsetReferenceEntityId,
        string? OffsetReferenceName,
        string? OffsetReferenceTag,
        string? OffsetReferenceMode,
        double? OffsetDistance,
        double? OffsetVertical,
        double? OffsetLateral)
    {
        public double? FieldOfView => FieldOfViewValue ?? Fov;
    }
}
