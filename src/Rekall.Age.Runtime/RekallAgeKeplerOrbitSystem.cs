using System.Globalization;
using System.Text.Json.Nodes;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeKeplerOrbitSystem : IRekallAgeRuntimeWorldSystem
{
    private const double GravitationalConstant = 6.67430e-11;

    public string Id => "runtime.celestial.kepler";

    public int Priority => -100;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeWorldFrameContext context)
    {
        var bodyById = world.Entities
            .Select(entity => (Entity: entity, Body: entity.Components.FirstOrDefault(component =>
                component.Type.Equals("Rekall.CelestialBody", StringComparison.Ordinal))))
            .Where(item => item.Body is not null)
            .Select(item => (item.Entity, BodyId: ReadString(item.Body!.Properties, "bodyId") ?? item.Entity.Name))
            .GroupBy(item => item.BodyId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Entity, StringComparer.Ordinal);
        var resolved = new Dictionary<string, RekallAgeRuntimeEntity>(StringComparer.Ordinal);
        var entities = world.Entities
            .Select(entity => ResolveOrbit(entity, bodyById, resolved, new HashSet<string>(StringComparer.Ordinal), context))
            .ToArray();
        return ValueTask.FromResult(world with { Entities = entities });
    }

    private static RekallAgeRuntimeEntity ResolveOrbit(
        RekallAgeRuntimeEntity entity,
        IReadOnlyDictionary<string, RekallAgeRuntimeEntity> bodyById,
        IDictionary<string, RekallAgeRuntimeEntity> resolved,
        ISet<string> visiting,
        RekallAgeRuntimeWorldFrameContext context)
    {
        if (resolved.TryGetValue(entity.Id, out var cached))
        {
            return cached;
        }

        if (!visiting.Add(entity.Id))
        {
            resolved[entity.Id] = entity;
            return entity;
        }

        var orbit = entity.Components.FirstOrDefault(component =>
            component.Type.Equals("Rekall.KeplerOrbit", StringComparison.Ordinal));
        var parentBodyId = orbit is null ? null : ReadString(orbit.Properties, "parentBodyId");
        var parent = !string.IsNullOrWhiteSpace(parentBodyId) && bodyById.TryGetValue(parentBodyId, out var foundParent)
            ? ResolveOrbit(foundParent, bodyById, resolved, visiting, context)
            : null;
        var updated = orbit is null || parent is null
            ? entity
            : ApplyOrbit(entity, orbit, parent, context);
        visiting.Remove(entity.Id);
        resolved[entity.Id] = updated;
        return updated;
    }

    private static RekallAgeRuntimeEntity ApplyOrbit(
        RekallAgeRuntimeEntity entity,
        RekallAgeRuntimeComponent orbit,
        RekallAgeRuntimeEntity parent,
        RekallAgeRuntimeWorldFrameContext context)
    {
        var semiMajorAxisKm = Math.Max(0, ReadNumber(orbit.Properties, "semiMajorAxisKm", 0));
        if (semiMajorAxisKm <= 0)
        {
            return entity;
        }

        var eccentricity = Math.Clamp(ReadNumber(orbit.Properties, "eccentricity", 0), 0, 0.999999);
        var periodSeconds = Math.Max(0, ReadNumber(orbit.Properties, "periodSeconds", 0));
        if (periodSeconds <= 0)
        {
            periodSeconds = EstimatePeriodSeconds(parent, semiMajorAxisKm);
        }

        if (periodSeconds <= 0)
        {
            return entity;
        }

        var timeAtPeriapsis = ReadNumber(orbit.Properties, "timeAtPeriapsisSeconds", 0);
        var timeScale = Math.Max(0, ReadNumber(orbit.Properties, "timeScale", 1));
        var meanAnomaly = Tau * (((context.ElapsedTime.TotalSeconds * timeScale) - timeAtPeriapsis) / periodSeconds);
        var eccentricAnomaly = SolveEccentricAnomaly(NormalizeRadians(meanAnomaly), eccentricity);
        var x = semiMajorAxisKm * (Math.Cos(eccentricAnomaly) - eccentricity);
        var y = semiMajorAxisKm * Math.Sqrt(1 - eccentricity * eccentricity) * Math.Sin(eccentricAnomaly);
        var rotated = RotateOrbitPlane(
            x,
            y,
            DegreesToRadians(ReadNumber(orbit.Properties, "inclinationDegrees", 0)),
            DegreesToRadians(ReadNumber(orbit.Properties, "longitudeOfAscendingNodeDegrees", 0)),
            DegreesToRadians(ReadNumber(orbit.Properties, "argumentOfPeriapsisDegrees", 0)));
        var scale = ReadNumber(orbit.Properties, "distanceScale", 1);
        var parentPosition = parent.Transform.Position3D;
        return entity with
        {
            Transform = entity.Transform with
            {
                Position3D = new RekallAgeRuntimeVector3(
                    parentPosition.X + rotated.X * scale,
                    parentPosition.Y + rotated.Y * scale,
                    parentPosition.Z + rotated.Z * scale)
            }
        };
    }

    private static double EstimatePeriodSeconds(RekallAgeRuntimeEntity parent, double semiMajorAxisKm)
    {
        var parentBody = parent.Components.FirstOrDefault(component =>
            component.Type.Equals("Rekall.CelestialBody", StringComparison.Ordinal));
        if (parentBody is null)
        {
            return 0;
        }

        var massKg = ReadNumber(parentBody.Properties, "massKg", 0);
        if (massKg <= 0)
        {
            return 0;
        }

        var semiMajorAxisM = semiMajorAxisKm * 1000;
        return Tau * Math.Sqrt(Math.Pow(semiMajorAxisM, 3) / (GravitationalConstant * massKg));
    }

    private static RekallAgeRuntimeVector3 RotateOrbitPlane(
        double x,
        double y,
        double inclination,
        double longitudeOfAscendingNode,
        double argumentOfPeriapsis)
    {
        var cosNode = Math.Cos(longitudeOfAscendingNode);
        var sinNode = Math.Sin(longitudeOfAscendingNode);
        var cosInc = Math.Cos(inclination);
        var sinInc = Math.Sin(inclination);
        var cosArg = Math.Cos(argumentOfPeriapsis);
        var sinArg = Math.Sin(argumentOfPeriapsis);

        var worldX = (cosNode * cosArg - sinNode * sinArg * cosInc) * x
            + (-cosNode * sinArg - sinNode * cosArg * cosInc) * y;
        var worldY = (sinArg * sinInc) * x + (cosArg * sinInc) * y;
        var worldZ = (sinNode * cosArg + cosNode * sinArg * cosInc) * x
            + (-sinNode * sinArg + cosNode * cosArg * cosInc) * y;
        return new RekallAgeRuntimeVector3(worldX, worldY, worldZ);
    }

    private static double SolveEccentricAnomaly(double meanAnomaly, double eccentricity)
    {
        var anomaly = eccentricity < 0.8 ? meanAnomaly : Math.PI;
        for (var i = 0; i < 8; i++)
        {
            var f = anomaly - eccentricity * Math.Sin(anomaly) - meanAnomaly;
            var fp = 1 - eccentricity * Math.Cos(anomaly);
            anomaly -= f / fp;
        }

        return anomaly;
    }

    private static double NormalizeRadians(double radians)
    {
        radians %= Tau;
        return radians < 0 ? radians + Tau : radians;
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }

    private static double Tau => Math.PI * 2;

    private static string? ReadString(JsonObject properties, string name)
    {
        return properties.TryGetPropertyValue(name, out var node)
            && node is JsonValue value
            && value.TryGetValue<string>(out var text)
            ? text
            : null;
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
