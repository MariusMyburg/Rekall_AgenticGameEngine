using System.Globalization;
using System.Text.Json.Nodes;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeCelestialRotationSystem : IRekallAgeRuntimeWorldSystem
{
    public string Id => "runtime.celestial.rotation";

    public int Priority => -90;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeWorldFrameContext context)
    {
        var entities = world.Entities
            .Select(entity => ApplyRotation(entity, context.ElapsedTime.TotalSeconds))
            .ToArray();
        return ValueTask.FromResult(world with { Entities = entities });
    }

    private static RekallAgeRuntimeEntity ApplyRotation(
        RekallAgeRuntimeEntity entity,
        double elapsedSeconds)
    {
        var rotation = entity.Components.FirstOrDefault(component =>
            component.Type.Equals("Rekall.CelestialRotation", StringComparison.Ordinal));
        if (rotation is null || !ReadBoolean(rotation.Properties, "active", true))
        {
            return entity;
        }

        var periodSeconds = Math.Abs(ReadNumber(rotation.Properties, "siderealPeriodSeconds", 0));
        if (periodSeconds <= 0)
        {
            return entity;
        }

        var direction = ReadBoolean(rotation.Properties, "retrograde", false) ? -1 : 1;
        var timeScale = ReadNumber(rotation.Properties, "timeScale", 1);
        var spin = direction * 360.0 * elapsedSeconds * timeScale / periodSeconds;
        var pitch = ReadNumber(rotation.Properties, "tiltDegrees", 0);
        var yaw = NormalizeDegrees(
            ReadNumber(rotation.Properties, "azimuthDegrees", 0)
            + ReadNumber(rotation.Properties, "initialLongitudeDegrees", 0)
            + spin);
        var roll = ReadNumber(rotation.Properties, "rollDegrees", 0);

        return entity with
        {
            Transform = entity.Transform with
            {
                Rotation3D = new RekallAgeRuntimeVector3(pitch, yaw, roll)
            }
        };
    }

    private static double NormalizeDegrees(double value)
    {
        value %= 360.0;
        return value < 0 ? value + 360.0 : value;
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
