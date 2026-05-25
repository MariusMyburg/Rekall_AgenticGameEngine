using System.Globalization;
using System.Text.Json.Nodes;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeCameraInputSystem : IRekallAgeRuntimeWorldSystem
{
    public string Id => "runtime.input.camera";

    public int Priority => 90;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeWorldFrameContext context)
    {
        if (Math.Abs(context.Input.MouseWheelDelta) <= 0.000001)
        {
            return ValueTask.FromResult(world);
        }

        var entities = world.Entities
            .Select(entity => ApplyCameraZoom(entity, context.Input.MouseWheelDelta))
            .ToArray();
        return ValueTask.FromResult(world with { Entities = entities });
    }

    private static RekallAgeRuntimeEntity ApplyCameraZoom(
        RekallAgeRuntimeEntity entity,
        double wheelDelta)
    {
        var zoom = entity.Components.FirstOrDefault(component =>
            component.Type.Equals("Rekall.CameraZoomInput", StringComparison.Ordinal));
        if (zoom is null || !ReadBoolean(zoom.Properties, "active", true))
        {
            return entity;
        }

        var cameraIndex = -1;
        var camera = default(RekallAgeRuntimeComponent);
        for (var i = 0; i < entity.Components.Count; i++)
        {
            if (entity.Components[i].Type is "Rekall.Camera2D" or "Rekall.Camera3D")
            {
                cameraIndex = i;
                camera = entity.Components[i];
                break;
            }
        }

        if (cameraIndex < 0 || camera is null || !ReadBoolean(camera.Properties, "active", true))
        {
            return entity;
        }

        var adjustedWheel = ReadBoolean(zoom.Properties, "invertWheel", false)
            ? -wheelDelta
            : wheelDelta;
        var speed = Math.Max(0.0001, ReadNumber(zoom.Properties, "wheelZoomSpeed", 0.12));
        var factor = Math.Exp(-adjustedWheel * speed);
        var projectionMode = ReadString(camera.Properties, "projectionMode")
            ?? (camera.Type == "Rekall.Camera2D" ? "orthographic" : "perspective");

        RekallAgeRuntimeComponent updatedCamera;
        if (projectionMode.Equals("orthographic", StringComparison.OrdinalIgnoreCase)
            || camera.Type == "Rekall.Camera2D")
        {
            var current = Math.Max(0.001, ReadNumber(camera.Properties, "orthographicSize", 10));
            var minimum = Math.Max(0.001, ReadNumber(zoom.Properties, "minimumOrthographicSize", 0.1));
            var maximum = Math.Max(minimum, ReadNumber(zoom.Properties, "maximumOrthographicSize", 100000));
            updatedCamera = camera with
            {
                Properties = SetNumber(camera.Properties, "orthographicSize", Math.Clamp(current * factor, minimum, maximum))
            };
        }
        else
        {
            var current = Math.Clamp(ReadNumber(camera.Properties, "fieldOfView", 65), 1, 179);
            var minimum = Math.Clamp(ReadNumber(zoom.Properties, "minimumFieldOfView", 15), 1, 179);
            var maximum = Math.Clamp(ReadNumber(zoom.Properties, "maximumFieldOfView", 120), minimum, 179);
            updatedCamera = camera with
            {
                Properties = SetNumber(camera.Properties, "fieldOfView", Math.Clamp(current * factor, minimum, maximum))
            };
        }

        var components = entity.Components.ToArray();
        components[cameraIndex] = updatedCamera;
        return entity with { Components = components };
    }

    private static JsonObject SetNumber(JsonObject source, string name, double value)
    {
        var copy = new JsonObject();
        foreach (var property in source)
        {
            if (!property.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                copy[property.Key] = property.Value?.DeepClone();
            }
        }

        copy[name] = value;
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
}
