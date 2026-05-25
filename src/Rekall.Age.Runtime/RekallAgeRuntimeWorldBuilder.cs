using System.Globalization;
using System.Text.Json.Nodes;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.World;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeRuntimeWorldBuilder
{
    public RekallAgeRuntimeWorld Build(RekallAgeSceneDocument scene)
    {
        var entities = scene.Entities
            .OrderBy(entity => entity.Name, StringComparer.Ordinal)
            .ThenBy(entity => entity.Id, StringComparer.Ordinal)
            .Select(ToRuntimeEntity)
            .ToArray();

        return new RekallAgeRuntimeWorld(
            scene.Id,
            scene.Name,
            0,
            TimeSpan.Zero,
            entities,
            RekallAgeRuntimeSubsystemViews.Empty,
            Array.Empty<Rekall.Age.Runtime.Abstractions.RekallAgeRuntimeObservation>());
    }

    private static RekallAgeRuntimeEntity ToRuntimeEntity(RekallAgeEntityDocument entity)
    {
        return new RekallAgeRuntimeEntity(
            entity.Id,
            entity.Name,
            entity.Tags.ToArray(),
            entity.ParentId,
            entity.PrefabSourceId,
            entity.Visible,
            entity.Locked,
            ExtractTransform(entity),
            entity.Components
                .OrderBy(component => component.Type, StringComparer.Ordinal)
                .Select(component => new RekallAgeRuntimeComponent(
                    component.Type,
                    component.Properties.DeepClone().AsObject()))
                .ToArray());
    }

    private static RekallAgeRuntimeTransform ExtractTransform(RekallAgeEntityDocument entity)
    {
        var transform = RekallAgeRuntimeTransform.Identity;
        var transform2D = entity.Components.FirstOrDefault(component =>
            component.Type.Equals("Rekall.Transform2D", StringComparison.Ordinal));
        var transform3D = entity.Components.FirstOrDefault(component =>
            component.Type.Equals("Rekall.Transform3D", StringComparison.Ordinal));

        if (transform2D is not null)
        {
            transform = transform with
            {
                Position2D = new RekallAgeRuntimeVector2(
                    ReadNumber(transform2D.Properties, "x", transform.Position2D.X),
                    ReadNumber(transform2D.Properties, "y", transform.Position2D.Y)),
                Rotation2D = ReadNumber(transform2D.Properties, "rotation", transform.Rotation2D),
                Scale2D = new RekallAgeRuntimeVector2(
                    ReadNumber(transform2D.Properties, "scaleX", transform.Scale2D.X),
                    ReadNumber(transform2D.Properties, "scaleY", transform.Scale2D.Y))
            };
        }

        if (transform3D is not null)
        {
            transform = transform with
            {
                Position3D = new RekallAgeRuntimeVector3(
                    ReadNumber(transform3D.Properties, "x", transform.Position3D.X),
                    ReadNumber(transform3D.Properties, "y", transform.Position3D.Y),
                    ReadNumber(transform3D.Properties, "z", transform.Position3D.Z)),
                Rotation3D = new RekallAgeRuntimeVector3(
                    ReadNumber(transform3D.Properties, "pitch", transform.Rotation3D.X),
                    ReadNumber(transform3D.Properties, "yaw", transform.Rotation3D.Y),
                    ReadNumber(transform3D.Properties, "roll", transform.Rotation3D.Z)),
                Scale3D = new RekallAgeRuntimeVector3(
                    ReadNumber(transform3D.Properties, "scaleX", transform.Scale3D.X),
                    ReadNumber(transform3D.Properties, "scaleY", transform.Scale3D.Y),
                    ReadNumber(transform3D.Properties, "scaleZ", transform.Scale3D.Z))
            };
        }

        return transform;
    }

    private static double ReadNumber(JsonObject properties, string name, double fallback)
    {
        if (!properties.TryGetPropertyValue(name, out var node) || node is null)
        {
            return fallback;
        }

        if (node is not JsonValue value)
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

        if (value.TryGetValue<decimal>(out var decimalValue))
        {
            return (double)decimalValue;
        }

        if (value.TryGetValue<string>(out var text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return fallback;
    }
}
