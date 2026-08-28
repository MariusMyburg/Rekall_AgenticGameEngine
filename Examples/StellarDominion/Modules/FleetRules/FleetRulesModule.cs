using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.FleetRules;

[RekallAgeModule("Game.Modules.FleetRules", "Fleet Rules")]
[RekallAgeRequiresCapability("world")]
public sealed class FleetRulesModule : RekallAgeModule
{
    public override void Configure(RekallAgeModuleBuilder builder)
    {
        builder.RegisterComponent<Drift>();
        builder.RegisterComponent<Escort>();
        builder.RegisterRuntimeSystem<FleetSystem>();
    }
}

[RekallAgeComponent("Drift", Description =
    "Moves a capital ship forward along its own heading at a constant speed, and keeps its " +
    "emissive drive block locked to the hull.")]
public sealed class Drift : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Enabled { get; init; } = true;

    [RekallAgeProperty(Minimum = 0, Maximum = 200)]
    public double Speed { get; init; } = 1;

    [RekallAgeProperty(Minimum = -360, Maximum = 360)]
    public double HeadingYaw { get; init; }
}

[RekallAgeComponent("Escort", Description =
    "Flies a fighter on an inclined circular patrol around a named leader ship, banking into " +
    "the turn and pointing along its own velocity.")]
public sealed class Escort : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Enabled { get; init; } = true;

    [RekallAgeProperty]
    public string Leader { get; init; } = string.Empty;

    [RekallAgeProperty(Minimum = 0.1, Maximum = 500)]
    public double Radius { get; init; } = 10;

    [RekallAgeProperty(Minimum = -360, Maximum = 360)]
    public double Phase { get; init; }

    [RekallAgeProperty(Minimum = -720, Maximum = 720)]
    public double AngularSpeed { get; init; } = 45;

    [RekallAgeProperty(Minimum = -90, Maximum = 90)]
    public double Inclination { get; init; } = 20;
}

/// <summary>
/// Drives the fleet: capitals translate along their heading, each capital's drive block follows
/// its hull, and fighters fly inclined circles around their leader.
///
/// Capitals integrate their heading with the fixed step's DeltaTime. Fighter orbits are solved
/// absolutely from ElapsedTime instead, so a wing's formation cannot drift out of phase however
/// many steps a frame runs.
/// </summary>
public sealed class FleetSystem : IRekallAgeRuntimeModuleSystem
{
    private const string DriftType = "Game.Modules.FleetRules.Drift";
    private const string EscortType = "Game.Modules.FleetRules.Escort";

    public string Id => "game.fleet";

    public int Priority => 0;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var elapsed = context.ElapsedTime.TotalSeconds;

        // Capitals first: fighters and drive blocks are positioned relative to where their
        // leader has moved to this step, so the leaders' new poses have to exist first.
        var leaders = new Dictionary<string, (double X, double Y, double Z, double Yaw)>(StringComparer.Ordinal);
        var moved = new Dictionary<string, RekallAgeRuntimeEntity>(StringComparer.Ordinal);

        foreach (var entity in world.Entities)
        {
            var drift = entity.FindComponent(DriftType);
            if (drift is null || !drift.Properties.ReadBoolean("enabled", true))
            {
                continue;
            }

            var speed = drift.Properties.ReadNumber("speed", 1);
            var headingYaw = drift.Properties.ReadNumber("headingYaw", entity.Transform.Rotation3D.Y);
            var radians = headingYaw * Math.PI / 180.0;
            // Per-step distance, not speed x total elapsed: this branch advances the ship from
            // wherever it already is, so using elapsed here would compound quadratically.
            var travelled = speed * context.DeltaTime.TotalSeconds;

            // Yaw 0 points down +Z in this engine's Euler convention, matching the hull meshes,
            // which are lofted along +Z with the prow at the positive end.
            var x = entity.Transform.Position3D.X + Math.Sin(radians) * travelled;
            var z = entity.Transform.Position3D.Z + Math.Cos(radians) * travelled;

            var next = entity.WithPosition3D(new RekallAgeRuntimeVector3(x, entity.Transform.Position3D.Y, z));
            moved[entity.Name] = next;
            leaders[entity.Name] = (x, entity.Transform.Position3D.Y, z, headingYaw);
        }

        var entities = new List<RekallAgeRuntimeEntity>(world.Entities.Count);
        foreach (var entity in world.Entities)
        {
            if (moved.TryGetValue(entity.Name, out var capital))
            {
                entities.Add(capital);
                continue;
            }

            // Drive blocks share their hull's name plus " Drive" and simply track it.
            if (entity.Name.EndsWith(" Drive", StringComparison.Ordinal)
                && leaders.TryGetValue(entity.Name[..^" Drive".Length], out var hull))
            {
                entities.Add(entity.WithPosition3D(new RekallAgeRuntimeVector3(hull.X, hull.Y, hull.Z)));
                continue;
            }

            var escort = entity.FindComponent(EscortType);
            if (escort is null || !escort.Properties.ReadBoolean("enabled", true))
            {
                entities.Add(entity);
                continue;
            }

            var leaderName = escort.Properties.ReadString("leader", string.Empty) ?? string.Empty;
            if (!leaders.TryGetValue(leaderName, out var leader))
            {
                entities.Add(entity);
                continue;
            }

            var radius = Math.Max(0.1, escort.Properties.ReadNumber("radius", 10));
            var phase = escort.Properties.ReadNumber("phase", 0);
            var angularSpeed = escort.Properties.ReadNumber("angularSpeed", 45);
            var inclination = escort.Properties.ReadNumber("inclination", 20) * Math.PI / 180.0;

            var angle = (phase + angularSpeed * elapsed) * Math.PI / 180.0;
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);

            // Circle in the leader's local XZ plane, tipped by the inclination so each wing
            // rides its own orbital plane instead of all of them sharing one disc.
            var offsetX = cos * radius;
            var offsetY = sin * radius * Math.Sin(inclination);
            var offsetZ = sin * radius * Math.Cos(inclination);

            var position = new RekallAgeRuntimeVector3(
                leader.X + offsetX,
                leader.Y + offsetY,
                leader.Z + offsetZ);

            // Face along the tangent of the circle, and bank into the turn.
            var headingDegrees = Math.Atan2(-sin, cos * Math.Cos(inclination)) * 180.0 / Math.PI;
            var bank = Math.Clamp(angularSpeed * 0.45, -55, 55);

            entities.Add(entity
                .WithPosition3D(position)
                .WithRotation3D(new RekallAgeRuntimeVector3(0, headingDegrees + 90, bank)));
        }

        return ValueTask.FromResult(world with { Entities = entities });
    }
}
