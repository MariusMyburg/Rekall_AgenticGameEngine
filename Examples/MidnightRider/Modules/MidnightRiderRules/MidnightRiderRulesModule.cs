using System.Text.Json.Nodes;
using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.MidnightRiderRules;

[RekallAgeModule("Game.Modules.MidnightRiderRules", "Midnight Rider Rules")]
[RekallAgeRequiresCapability("world")]
public sealed class MidnightRiderRulesModule : RekallAgeModule
{
    public override void Configure(RekallAgeModuleBuilder builder)
    {
        builder.RegisterComponent<RunState>();
        builder.RegisterRuntimeSystem<MidnightRiderSystem>();
    }
}

[RekallAgeComponent("Run State", Description = "Drives the motorbike infinite-runner: throttle/steering control, a rider-like balance controller on the front fork, and infinite procedural road/streetlight/hazard spawning ahead of the bike.")]
public sealed class RunState : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Enabled { get; init; } = true;

    [RekallAgeProperty]
    public bool Crashed { get; init; }

    [RekallAgeProperty]
    public double TargetSpeed { get; init; }

    [RekallAgeProperty]
    public double PreviousRoll { get; init; }

    [RekallAgeProperty]
    public double PreviousYaw { get; init; }

    [RekallAgeProperty]
    public double DistanceTraveled { get; init; }

    [RekallAgeProperty]
    public double NextSpawnX { get; init; }

    [RekallAgeProperty]
    public int NextChunkIndex { get; init; }

    [RekallAgeProperty]
    public int Seed { get; init; } = 1;

    [RekallAgeProperty]
    public string ChassisId { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string ForkId { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string RearWheelId { get; init; } = string.Empty;
}

/// <summary>
/// Priority -10 (before RekallAgeBepuPhysicsSystem's 0) so a motor target set this frame is
/// consumed by physics this same frame, the same convention Ridgebreaker's own vehicle system
/// established.
/// </summary>
public sealed class MidnightRiderSystem : IRekallAgeRuntimeModuleSystem
{
    private const string RunStateType = "Game.Modules.MidnightRiderRules.RunState";
    private const string HingeJointType = "Rekall.HingeJoint";

    // Tuned against a physics probe: chassis (mass 180) + fork (mass 4) + two capsule-tired
    // wheels (mass 3 each, radius 0.32) driven by real BEPU hinge motors - see
    // docs/production/PROGRESS.md's HingeJoint-axis-fix checkpoint for how this assembly was
    // arrived at, and docs/superpowers/specs for the balance-controller rationale.
    private const double WheelRadius = 0.32;
    private const double MaxSpeed = 22;
    private const double AccelerationPerSecond = 9;
    private const double MaxSteerAngleDegrees = 24;
    private const double MaxForkAngleDegrees = 30;
    private const double SteerAngleGain = 10;
    private const double MaxForkMotorDegreesPerSecond = 160;
    private const double BalanceProportionalGain = 2.4;
    private const double BalanceDerivativeGain = 0.35;
    // A real, physically-genuine emergent yaw drift shows up on a long unattended throttle-only
    // run (confirmed via a zero-balance-gain control run that still drifted, ruling out the roll
    // balance controller's own limit cycle as the cause) - the rear wheel's large spin angular
    // momentum at speed couples with chassis pitch/roll into a slow yaw precession, plus real
    // wheel slip (measured rear-wheel angular speed runs ~6x faster than v/r implies - see
    // docs/production/PROGRESS.md's 2026-08-28 checkpoint). Left at 0 (disabled): tested with a
    // meaningful gain and it did not reliably reduce the drift over a 30-second run, and
    // increasing wheel/road friction to try to fix the underlying slip made propulsion worse
    // (the wheel locked up instead of rolling) rather than better. This is a genuine open gap,
    // not resolved by this term - the wiring (PreviousYaw tracking, yawRate below) is kept as
    // the hook for whoever picks this up next, not as a working fix.
    private const double YawDampingGain = 0;
    private const double CrashRollDegrees = 62;
    private const double CrashPitchDegrees = 70;

    private const double ChunkLength = 40;
    private const double SpawnAheadDistance = 160;
    private const double DespawnBehindDistance = 60;
    private const double RoadHalfWidth = 4.5;
    private const double StreetLightSpacing = 24;
    private const double HazardMinimumSpacing = 70;
    private const double HazardMaximumSpacing = 160;

    public string Id => nameof(MidnightRiderSystem);

    public int Priority => -10;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var runStateEntity = world.EntitiesWithComponent(RunStateType).FirstOrDefault();
        if (runStateEntity is null)
        {
            return ValueTask.FromResult(world);
        }

        var run = runStateEntity.FindComponent(RunStateType)!;
        var seconds = context.DeltaTime.TotalSeconds;
        var chassisId = run.Properties.ReadString("chassisId") ?? string.Empty;
        var forkId = run.Properties.ReadString("forkId") ?? string.Empty;
        var rearWheelId = run.Properties.ReadString("rearWheelId") ?? string.Empty;
        var chassis = world.FindEntity(chassisId);
        var fork = world.FindEntity(forkId);
        if (chassis is null || fork is null)
        {
            return ValueTask.FromResult(world);
        }

        var enabled = run.Properties.ReadBoolean("enabled", true);
        var crashed = run.Properties.ReadBoolean("crashed", false);
        var targetSpeed = run.Properties.ReadNumber("targetSpeed", 0);
        var previousRoll = run.Properties.ReadNumber("previousRoll", 0);
        var previousYaw = run.Properties.ReadNumber("previousYaw", 0);
        var distanceTraveled = run.Properties.ReadNumber("distanceTraveled", 0);
        var nextSpawnX = run.Properties.ReadNumber("nextSpawnX", 0);
        var nextChunkIndex = (int)run.Properties.ReadNumber("nextChunkIndex", 0);
        var seed = (int)run.Properties.ReadNumber("seed", 1);
        var startX = 0.0;

        var roll = Normalize180(chassis.Transform.Rotation3D.Z);
        var pitch = Normalize180(chassis.Transform.Rotation3D.X);
        var chassisYaw = chassis.Transform.Rotation3D.Y;
        var chassisX = chassis.Transform.Position3D.X;

        if (!crashed && (Math.Abs(roll) > CrashRollDegrees || Math.Abs(pitch) > CrashPitchDegrees))
        {
            crashed = true;
            world = world.EmitObservation(
                chassis, "midnight_rider.crashed", "warning", "gameplay", Id,
                $"The bike went down at distance {chassisX - startX:0.0}m.");
        }

        var running = enabled && !crashed;

        // --- Throttle: Up/Down set a target speed; the rear wheel's hinge motor chases it. ---
        var throttle = running ? Math.Clamp(world.InputActionValue("throttle"), -1, 1) : 0;
        targetSpeed = Math.Clamp(targetSpeed + (throttle * AccelerationPerSecond * seconds), 0, MaxSpeed);
        if (!running)
        {
            targetSpeed = Math.Max(0, targetSpeed - (AccelerationPerSecond * seconds));
        }

        if (!string.IsNullOrEmpty(rearWheelId))
        {
            var motorTarget = -(targetSpeed / WheelRadius) * (180 / Math.PI);
            world = world.UpdateEntity(rearWheelId, wheel => wheel.WithComponentNumber(HingeJointType, "motorTargetVelocity", motorTarget));
        }

        // --- Steering + balance: the player's Left/Right input sets a desired lean/turn angle;
        // a small proportional-derivative correction on the chassis's own measured roll and
        // roll rate is added on top, exactly the kind of continuous micro-correction a real
        // rider supplies to stay upright - it nudges the STEERING target, never the lean itself,
        // so the resulting lean stays a genuine consequence of real contact/inertia physics. ---
        var steerInput = running ? Math.Clamp(world.InputActionValue("steer"), -1, 1) : 0;
        var rollRate = seconds > 0 ? (roll - previousRoll) / seconds : 0;
        var balanceCorrection = (BalanceProportionalGain * roll) + (BalanceDerivativeGain * rollRate);
        var yawRate = seconds > 0 ? Normalize180(chassisYaw - previousYaw) / seconds : 0;
        var yawDamping = -YawDampingGain * yawRate;
        var targetSteerAngle = Math.Clamp(
            (steerInput * MaxSteerAngleDegrees) + balanceCorrection + yawDamping,
            -MaxForkAngleDegrees,
            MaxForkAngleDegrees);

        var forkYaw = fork.Transform.Rotation3D.Y;
        var currentSteerAngle = Normalize180(forkYaw - chassisYaw);
        var steerError = targetSteerAngle - currentSteerAngle;
        var forkMotorVelocity = Math.Clamp(steerError * SteerAngleGain, -MaxForkMotorDegreesPerSecond, MaxForkMotorDegreesPerSecond);
        if (!string.IsNullOrEmpty(forkId))
        {
            world = world.UpdateEntity(forkId, forkEntity => forkEntity.WithComponentNumber(HingeJointType, "motorTargetVelocity", forkMotorVelocity));
        }

        distanceTraveled = Math.Max(distanceTraveled, chassisX - startX);

        // --- Infinite road: spawn chunks/streetlights/hazards ahead, remove them once well
        // behind the bike. Deterministic per-chunk seeding keeps hazard placement reproducible
        // for a given seed even though chunks are streamed in and out over time. ---
        while (chassisX + SpawnAheadDistance > nextSpawnX)
        {
            world = SpawnChunk(world, nextChunkIndex, nextSpawnX, seed);
            nextChunkIndex++;
            nextSpawnX += ChunkLength;
        }

        var despawnBeforeX = chassisX - DespawnBehindDistance;
        var toRemove = world.Entities
            .Where(entity => entity.HasTag("road-chunk") && entity.Transform.Position3D.X + ChunkLength < despawnBeforeX)
            .Select(entity => entity.Id)
            .ToArray();
        foreach (var id in toRemove)
        {
            world = world.RemoveEntity(id);
        }

        world = world.UpdateEntity(runStateEntity.Id, entity => entity
            .WithComponentBoolean(RunStateType, "crashed", crashed)
            .WithComponentNumber(RunStateType, "targetSpeed", targetSpeed)
            .WithComponentNumber(RunStateType, "previousRoll", roll)
            .WithComponentNumber(RunStateType, "previousYaw", chassisYaw)
            .WithComponentNumber(RunStateType, "distanceTraveled", distanceTraveled)
            .WithComponentNumber(RunStateType, "nextSpawnX", nextSpawnX)
            .WithComponentNumber(RunStateType, "nextChunkIndex", nextChunkIndex));

        return ValueTask.FromResult(world);
    }

    /// <summary>
    /// One 40-unit slice of infinite road: a tarmac slab, a streetlight (approximately every
    /// second chunk), and an occasional hazard - a raised speed-bump box the wheels physically
    /// collide with via BEPU's own contact resolution, not a scripted effect.
    /// </summary>
    private static RekallAgeRuntimeWorld SpawnChunk(RekallAgeRuntimeWorld world, int chunkIndex, double startX, int seed)
    {
        var centerX = startX + (ChunkLength / 2);
        var roadId = $"road-chunk-{chunkIndex}";
        var road = RekallAgeRuntimeModuleSdk.CreateEntity(roadId, $"Road {chunkIndex}")
            .WithTag("road-chunk")
            .WithPosition3D(new RekallAgeRuntimeVector3(centerX, -0.1, 0))
            .WithScale3D(new RekallAgeRuntimeVector3(ChunkLength, 0.2, RoadHalfWidth * 2))
            .UpsertComponent("Rekall.BoxCollider3D", new JsonObject { ["width"] = ChunkLength, ["height"] = 0.2, ["depth"] = RoadHalfWidth * 2 })
            .UpsertComponent("Rekall.PhysicsMaterial3D", new JsonObject { ["friction"] = 1.3, ["restitution"] = 0 })
            .UpsertComponent("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" })
            .UpsertComponent("Rekall.Material", new JsonObject
            {
                ["baseColor"] = "#1c1d20",
                ["metallicFactor"] = 0.05,
                ["roughnessFactor"] = 0.92
            })
            .UpsertComponent("Rekall.MeshRenderer", new JsonObject { ["active"] = true, ["castShadows"] = true, ["receiveShadows"] = true });
        world = world.AddEntity(road);

        // Invisible guard rails along both edges of the road: found via a real sustained-throttle
        // run (60 real seconds, no steering input) that the bike's own small, physically genuine
        // yaw drift - the same reaction-torque/balance-correction coupling that makes it lean into
        // turns - compounds over a long enough run into a real lateral drift, eventually carrying
        // it past RoadHalfWidth and off the road into open space with nothing underneath. A real
        // rider corrects that drift continuously; this is a deliberate, low-risk gameplay
        // safeguard (invisible walls, a standard infinite-runner convention) rather than an
        // attempt to perfectly zero out a physically emergent drift, which is exactly the kind of
        // authored realism the balance controller is meant to keep organic.
        var railHeight = 1.5;
        var railThickness = 0.4;
        world = world.AddEntity(RekallAgeRuntimeModuleSdk.CreateEntity($"road-rail-left-{chunkIndex}", $"Road Rail Left {chunkIndex}")
            .WithTag("road-chunk")
            .WithPosition3D(new RekallAgeRuntimeVector3(centerX, railHeight / 2, RoadHalfWidth + (railThickness / 2)))
            .UpsertComponent("Rekall.BoxCollider3D", new JsonObject { ["width"] = ChunkLength, ["height"] = railHeight, ["depth"] = railThickness })
            .UpsertComponent("Rekall.PhysicsMaterial3D", new JsonObject { ["friction"] = 0.4, ["restitution"] = 0.1 }));
        world = world.AddEntity(RekallAgeRuntimeModuleSdk.CreateEntity($"road-rail-right-{chunkIndex}", $"Road Rail Right {chunkIndex}")
            .WithTag("road-chunk")
            .WithPosition3D(new RekallAgeRuntimeVector3(centerX, railHeight / 2, -RoadHalfWidth - (railThickness / 2)))
            .UpsertComponent("Rekall.BoxCollider3D", new JsonObject { ["width"] = ChunkLength, ["height"] = railHeight, ["depth"] = railThickness })
            .UpsertComponent("Rekall.PhysicsMaterial3D", new JsonObject { ["friction"] = 0.4, ["restitution"] = 0.1 }));

        if (chunkIndex % 2 == 0)
        {
            world = SpawnStreetLight(world, chunkIndex, startX + (StreetLightSpacing * 0.5), RoadHalfWidth + 1.2);
        }

        var hazardRoll = RekallAgeRuntimeModuleSdk.DeterministicUnit(seed, chunkIndex * 7919L + 13);
        if (chunkIndex > 2 && hazardRoll < 0.35)
        {
            var offset = RekallAgeRuntimeModuleSdk.DeterministicRange(seed, (chunkIndex * 7919L) + 29, ChunkLength * 0.25, ChunkLength * 0.75);
            world = SpawnHazard(world, chunkIndex, startX + offset, seed, chunkIndex);
        }

        return world;
    }

    private static RekallAgeRuntimeWorld SpawnStreetLight(RekallAgeRuntimeWorld world, int chunkIndex, double x, double lateralOffset)
    {
        var side = chunkIndex % 4 == 0 ? 1 : -1;
        var poleId = $"street-light-{chunkIndex}";
        var pole = RekallAgeRuntimeModuleSdk.CreateEntity(poleId, $"Street Light {chunkIndex}")
            .WithTag("road-chunk")
            .WithPosition3D(new RekallAgeRuntimeVector3(x, 0, lateralOffset * side))
            .WithScale3D(new RekallAgeRuntimeVector3(0.12, 4.2, 0.12))
            .UpsertComponent("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cylinder" })
            .UpsertComponent("Rekall.Material", new JsonObject { ["baseColor"] = "#2b2d31", ["metallicFactor"] = 0.8, ["roughnessFactor"] = 0.4 })
            .UpsertComponent("Rekall.MeshRenderer", new JsonObject { ["active"] = true, ["castShadows"] = true, ["receiveShadows"] = true });
        world = world.AddEntity(pole);

        var lampId = $"street-light-lamp-{chunkIndex}";
        var lamp = RekallAgeRuntimeModuleSdk.CreateEntity(lampId, $"Street Lamp {chunkIndex}")
            .WithTag("road-chunk")
            .WithTag("lighting")
            .WithPosition3D(new RekallAgeRuntimeVector3(x, 4.6, (lateralOffset - 0.6) * side))
            .WithRotation3D(new RekallAgeRuntimeVector3(90, 0, 0))
            .UpsertComponent("Rekall.SpotLight", new JsonObject
            {
                ["intensity"] = 7,
                ["range"] = 14,
                ["innerConeAngle"] = 18,
                ["outerConeAngle"] = 34,
                ["color"] = "#ffc98a",
                ["priority"] = 1
            });
        world = world.AddEntity(lamp);
        return world;
    }

    private static RekallAgeRuntimeWorld SpawnHazard(RekallAgeRuntimeWorld world, int chunkIndex, double x, int seed, long sequence)
    {
        var lateral = RekallAgeRuntimeModuleSdk.DeterministicRange(seed, (sequence * 7919L) + 41, -RoadHalfWidth + 1, RoadHalfWidth - 1);
        var hazardId = $"hazard-{chunkIndex}";
        var hazard = RekallAgeRuntimeModuleSdk.CreateEntity(hazardId, $"Hazard {chunkIndex}")
            .WithTag("road-chunk")
            .WithPosition3D(new RekallAgeRuntimeVector3(x, 0.08, lateral))
            .WithScale3D(new RekallAgeRuntimeVector3(1.4, 0.16, 0.7))
            .UpsertComponent("Rekall.BoxCollider3D", new JsonObject { ["width"] = 1.4, ["height"] = 0.16, ["depth"] = 0.7 })
            .UpsertComponent("Rekall.PhysicsMaterial3D", new JsonObject { ["friction"] = 1.1, ["restitution"] = 0.05 })
            .UpsertComponent("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" })
            .UpsertComponent("Rekall.Material", new JsonObject { ["baseColor"] = "#c98a2b", ["metallicFactor"] = 0.1, ["roughnessFactor"] = 0.8 })
            .UpsertComponent("Rekall.MeshRenderer", new JsonObject { ["active"] = true, ["castShadows"] = true, ["receiveShadows"] = true });
        return world.AddEntity(hazard);
    }

    private static double Normalize180(double degrees)
    {
        var wrapped = degrees % 360;
        if (wrapped > 180) wrapped -= 360;
        if (wrapped < -180) wrapped += 360;
        return wrapped;
    }
}
