using System.Text.Json.Nodes;
using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.KsaSolarFlight;

[RekallAgeModule("game.ksa-solar-flight", "KSA Solar Flight")]
[RekallAgeRequiresCapability("world")]
public sealed class KsaSolarFlightModule : RekallAgeModule
{
    public override void Configure(RekallAgeModuleBuilder builder)
    {
        builder.RegisterComponent<SpacecraftFlight>();
        builder.RegisterComponent<SpacecraftTelemetry>();
        builder.RegisterRuntimeSystem<KsaSolarFlightSystem>();
    }
}

[RekallAgeComponent("Spacecraft Flight")]
public sealed class SpacecraftFlight : RekallAgeComponent
{
    [RekallAgeProperty]
    public string ParentBodyId { get; init; } = "Earth";

    [RekallAgeProperty]
    public double PositionXKm { get; init; }

    [RekallAgeProperty]
    public double PositionYKm { get; init; }

    [RekallAgeProperty]
    public double PositionZKm { get; init; }

    [RekallAgeProperty]
    public double VelocityXKmPerSecond { get; init; }

    [RekallAgeProperty]
    public double VelocityYKmPerSecond { get; init; }

    [RekallAgeProperty]
    public double VelocityZKmPerSecond { get; init; }

    [RekallAgeProperty]
    public double DryMassKg { get; init; } = 9000;

    [RekallAgeProperty]
    public double FuelMassKg { get; init; } = 80000;

    [RekallAgeProperty]
    public double ThrustKilonewtons { get; init; } = 1800;

    [RekallAgeProperty]
    public double SpecificImpulseSeconds { get; init; } = 310;

    [RekallAgeProperty]
    public double Throttle { get; init; } = 1;

    [RekallAgeProperty]
    public double LaunchPitchDegrees { get; init; } = 68;

    [RekallAgeProperty]
    public double DisplayDistanceScale { get; init; } = 0.00003;
}

[RekallAgeComponent("Spacecraft Telemetry")]
public sealed class SpacecraftTelemetry : RekallAgeComponent
{
    [RekallAgeProperty]
    public double AltitudeKm { get; init; }

    [RekallAgeProperty]
    public double SpeedKmPerSecond { get; init; }

    [RekallAgeProperty]
    public double FuelMassKg { get; init; }
}

public sealed class KsaSolarFlightSystem : IRekallAgeRuntimeModuleSystem
{
    private const string FlightType = "Game.Modules.KsaSolarFlight.SpacecraftFlight";
    private const string TelemetryType = "Game.Modules.KsaSolarFlight.SpacecraftTelemetry";
    private const double G = 6.67430e-20;
    private const double StandardGravity = 9.80665;

    public string Id => nameof(KsaSolarFlightSystem);

    public int Priority => -50;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var seconds = context.DeltaTime.TotalSeconds;
        var bodyById = world.Entities
            .Select(entity => (Entity: entity, Body: entity.FindComponent("Rekall.CelestialBody")))
            .Where(item => item.Body is not null)
            .ToDictionary(
                item => item.Body!.Properties.ReadString("bodyId", item.Entity.Name) ?? item.Entity.Name,
                item => item.Entity,
                StringComparer.Ordinal);
        var entities = world.Entities.Select(entity =>
        {
            var flight = entity.FindComponent(FlightType);
            if (flight is null)
            {
                return entity;
            }

            var parentBodyId = flight.Properties.ReadString("parentBodyId", "Earth") ?? "Earth";
            if (!bodyById.TryGetValue(parentBodyId, out var parent))
            {
                return entity;
            }

            var parentBody = parent.FindComponent("Rekall.CelestialBody");
            if (parentBody is null)
            {
                return entity;
            }

            var position = new Vector3d(
                flight.Properties.ReadNumber("positionXKm", parentBody.Properties.ReadNumber("meanRadiusKm", 6371) + 0.2),
                flight.Properties.ReadNumber("positionYKm", 0),
                flight.Properties.ReadNumber("positionZKm", 0));
            var velocity = new Vector3d(
                flight.Properties.ReadNumber("velocityXKmPerSecond", 0),
                flight.Properties.ReadNumber("velocityYKmPerSecond", 0),
                flight.Properties.ReadNumber("velocityZKmPerSecond", 0.46));
            var dryMass = Math.Max(1, flight.Properties.ReadNumber("dryMassKg", 9000));
            var fuelMass = Math.Max(0, flight.Properties.ReadNumber("fuelMassKg", 0));
            var totalMass = dryMass + fuelMass;
            var radius = Math.Max(0.0001, position.Length);
            var gravity = position * (-(G * parentBody.Properties.ReadNumber("massKg", 0)) / (radius * radius * radius));
            var throttle = Math.Clamp(flight.Properties.ReadNumber("throttle", 0), 0, 1);
            var thrustDirection = Normalize(
                Normalize(position) * Math.Sin(DegreesToRadians(flight.Properties.ReadNumber("launchPitchDegrees", 68)))
                + new Vector3d(0, 0, 1) * Math.Cos(DegreesToRadians(flight.Properties.ReadNumber("launchPitchDegrees", 68))));
            var thrustKn = fuelMass > 0 ? Math.Max(0, flight.Properties.ReadNumber("thrustKilonewtons", 0)) * throttle : 0;
            var thrustAcceleration = thrustDirection * ((thrustKn / totalMass) * seconds);
            var acceleration = gravity * seconds + thrustAcceleration;
            velocity += acceleration;
            position += velocity * seconds;
            var isp = Math.Max(1, flight.Properties.ReadNumber("specificImpulseSeconds", 310));
            fuelMass = Math.Max(0, fuelMass - (thrustKn * 1000 / (isp * StandardGravity)) * seconds);
            var displayScale = flight.Properties.ReadNumber("displayDistanceScale", 0.00003);
            var display = parent.Transform.Position3D;
            var altitude = Math.Max(0, position.Length - parentBody.Properties.ReadNumber("meanRadiusKm", 6371));
            var speed = velocity.Length;

            return entity
                .WithPosition3D(new RekallAgeRuntimeVector3(
                    display.X + position.X * displayScale,
                    display.Y + position.Y * displayScale,
                    display.Z + position.Z * displayScale))
                .UpdateComponent(FlightType, properties =>
                {
                    properties["positionXKm"] = position.X;
                    properties["positionYKm"] = position.Y;
                    properties["positionZKm"] = position.Z;
                    properties["velocityXKmPerSecond"] = velocity.X;
                    properties["velocityYKmPerSecond"] = velocity.Y;
                    properties["velocityZKmPerSecond"] = velocity.Z;
                    properties["fuelMassKg"] = fuelMass;
                    return properties;
                })
                .UpsertComponent(
                    TelemetryType,
                    new JsonObject
                    {
                        ["altitudeKm"] = altitude,
                        ["speedKmPerSecond"] = speed,
                        ["fuelMassKg"] = fuelMass
                    });
        }).ToArray();

        return ValueTask.FromResult(world with { Entities = entities });
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180;
    }

    private static Vector3d Normalize(Vector3d vector)
    {
        var length = vector.Length;
        return length <= 0.000000001 ? new Vector3d(1, 0, 0) : vector / length;
    }

    private readonly record struct Vector3d(double X, double Y, double Z)
    {
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

        public static Vector3d operator +(Vector3d left, Vector3d right)
        {
            return new Vector3d(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        }

        public static Vector3d operator *(Vector3d vector, double scale)
        {
            return new Vector3d(vector.X * scale, vector.Y * scale, vector.Z * scale);
        }

        public static Vector3d operator /(Vector3d vector, double scale)
        {
            return new Vector3d(vector.X / scale, vector.Y / scale, vector.Z / scale);
        }
    }
}
