using System.Globalization;
using System.Text.Json.Nodes;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeTransformAnimationSystem : IRekallAgeRuntimeWorldSystem
{
    public string Id => "runtime.animation";

    public int Priority => 0;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeWorldFrameContext context)
    {
        var entities = world.Entities.Select(entity => ApplyTransformAnimation(entity, context)).ToArray();
        return ValueTask.FromResult(world with { Entities = entities });
    }

    private static RekallAgeRuntimeEntity ApplyTransformAnimation(
        RekallAgeRuntimeEntity entity,
        RekallAgeRuntimeWorldFrameContext context)
    {
        var pitchRate = 0.0;
        var yawRate = 0.0;
        var rollRate = 0.0;

        foreach (var component in entity.Components.Where(component =>
            component.Type.Equals("Rekall.TransformAnimation", StringComparison.Ordinal)))
        {
            if (!ReadBoolean(component.Properties, "active", true))
            {
                continue;
            }

            pitchRate += ReadNumber(component.Properties, "pitchDegreesPerSecond", 0)
                + ReadNumber(component.Properties, "pitchRate", 0);
            yawRate += ReadNumber(component.Properties, "yawDegreesPerSecond", 0)
                + ReadNumber(component.Properties, "yawRate", 0);
            rollRate += ReadNumber(component.Properties, "rollDegreesPerSecond", 0)
                + ReadNumber(component.Properties, "rollRate", 0);
        }

        if (pitchRate == 0 && yawRate == 0 && rollRate == 0)
        {
            return entity;
        }

        var seconds = context.DeltaTime.TotalSeconds;
        var rotation = entity.Transform.Rotation3D;
        return entity with
        {
            Transform = entity.Transform with
            {
                Rotation3D = new RekallAgeRuntimeVector3(
                    rotation.X + pitchRate * seconds,
                    rotation.Y + yawRate * seconds,
                    rotation.Z + rollRate * seconds)
            }
        };
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

        if (value.TryGetValue<string>(out var text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return fallback;
    }
}
