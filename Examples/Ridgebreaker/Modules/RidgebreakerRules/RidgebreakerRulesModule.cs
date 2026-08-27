using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.RidgebreakerRules;

[RekallAgeModule("RidgebreakerRules", "Ridgebreaker Rules")]
[RekallAgeRequiresCapability("world")]
public sealed class RidgebreakerRulesModule : RekallAgeModule
{
    public override void Configure(RekallAgeModuleBuilder builder)
    {
        builder.RegisterComponent<RunState>();
        builder.RegisterRuntimeSystem<RidgebreakerSystem>();
    }
}

[RekallAgeComponent("Run State", Description = "Ridgebreaker's per-run gameplay state: fuel, distance progress, wheel/camera cross-references, and outcome flags.")]
public sealed class RunState : RekallAgeComponent
{
    [RekallAgeProperty]
    public double Fuel { get; init; } = 100;

    [RekallAgeProperty]
    public double MaxFuel { get; init; } = 100;

    [RekallAgeProperty]
    public double StartX { get; init; }

    [RekallAgeProperty]
    public double FinishX { get; init; }

    [RekallAgeProperty]
    public string WheelRearId { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string WheelFrontId { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string CameraId { get; init; } = string.Empty;

    [RekallAgeProperty]
    public bool OutOfFuel { get; init; }

    [RekallAgeProperty]
    public bool Crashed { get; init; }

    [RekallAgeProperty]
    public bool Finished { get; init; }

    [RekallAgeProperty]
    public double ThrottleSpeed { get; init; } = 480;
}

/// <summary>
/// Ridgebreaker's whole gameplay loop: throttle-driven wheel spin (a live-authored angular
/// velocity target, re-asserted every frame - the same technique the built-in physics reads
/// for any Rigidbody3D), fuel drain, crystal-outcrop destruction on vehicle impact (reads
/// last frame's collision.begin facts and flips the crystal's own Rekall.Destructible.Triggered
/// flag; RekallAgeDestructionSystem does the actual fracture/replace), fuel-cell pickups (last
/// frame's trigger.enter facts), a chase camera, and crash/out-of-fuel/finish outcome tracking.
///
/// Priority -10 (before RekallAgeBepuPhysicsSystem's 0) so a wheel spin set this frame is
/// consumed by physics this same frame; this means collision/trigger events read here are one
/// frame old, which is imperceptible and matches how EmitBoundEvents-driven gameplay elsewhere
/// in this engine's examples already reads events a frame late.
/// </summary>
public sealed class RidgebreakerSystem : IRekallAgeRuntimeModuleSystem
{
    private const string RunStateType = "Game.Modules.RidgebreakerRules.RunState";
    private const string DestructibleType = "Rekall.Destructible";
    private const string Rigidbody3DType = "Rekall.Rigidbody3D";

    public string Id => nameof(RidgebreakerSystem);

    public int Priority => -10;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var chassis = world.EntitiesWithComponent(RunStateType).FirstOrDefault();
        if (chassis is null)
        {
            return ValueTask.FromResult(world);
        }

        var run = chassis.FindComponent(RunStateType)!;
        var fuel = run.Properties.ReadNumber("fuel", 100);
        var maxFuel = run.Properties.ReadNumber("maxFuel", 100);
        var startX = run.Properties.ReadNumber("startX", 0);
        var finishX = run.Properties.ReadNumber("finishX", 0);
        var wheelRearId = run.Properties.ReadString("wheelRearId") ?? string.Empty;
        var wheelFrontId = run.Properties.ReadString("wheelFrontId") ?? string.Empty;
        var cameraId = run.Properties.ReadString("cameraId") ?? string.Empty;
        var outOfFuel = run.Properties.ReadBoolean("outOfFuel", false);
        var crashed = run.Properties.ReadBoolean("crashed", false);
        var finished = run.Properties.ReadBoolean("finished", false);
        var throttleSpeed = run.Properties.ReadNumber("throttleSpeed", 480);
        var seconds = context.DeltaTime.TotalSeconds;
        var running = !crashed && !finished;

        // --- Crystal outcrops: shatter on impact with the vehicle ---
        var crystalHits = world.EventsOfType("collision.begin")
            .Where(item => item.Handler == "crystalHit")
            .Select(item => item.EntityId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var crystalId in crystalHits)
        {
            world = world.UpdateEntity(crystalId, entity =>
                entity.ComponentBoolean(DestructibleType, "triggered", false)
                    ? entity
                    : entity.WithComponentBoolean(DestructibleType, "triggered", true));
        }

        // --- Fuel cells: refill on pickup, then remove the pickup ---
        var fuelPickups = world.EventsOfType("trigger.enter")
            .Where(item => item.Handler == "fuelPickup")
            .ToArray();
        foreach (var pickup in fuelPickups)
        {
            fuel = Math.Min(maxFuel, fuel + 35);
            world = world.RemoveEntity(pickup.EntityId);
            world = world.EmitObservation(
                chassis, "ridgebreaker.fuel_refilled", "info", "gameplay", Id,
                $"Refueled to {fuel:0} by breaking/collecting a fuel cell.");
        }

        // --- Outcome checks (crash / finish) ---
        var pitch = chassis.Transform.Rotation3D.X;
        var roll = chassis.Transform.Rotation3D.Z;
        if (running && (Math.Abs(Normalize180(pitch)) > 100 || Math.Abs(Normalize180(roll)) > 100))
        {
            crashed = true;
            running = false;
            world = world.EmitObservation(chassis, "ridgebreaker.crashed", "warning", "gameplay", Id,
                $"The rover flipped over at distance {chassis.Transform.Position3D.X - startX:0.0}m.");
        }
        else if (running && chassis.Transform.Position3D.X >= finishX)
        {
            finished = true;
            running = false;
            world = world.EmitObservation(chassis, "ridgebreaker.finished", "info", "gameplay", Id,
                "The rover reached the summit!");
        }

        // --- Throttle: live wheel-spin target + fuel drain ---
        var throttle = running && fuel > 0 ? Math.Clamp(world.InputActionValue("throttle"), -1, 1) : 0;
        if (throttle != 0)
        {
            fuel = Math.Max(0, fuel - Math.Abs(throttle) * 3 * seconds);
        }

        if (fuel <= 0 && !outOfFuel)
        {
            outOfFuel = true;
            world = world.EmitObservation(chassis, "ridgebreaker.out_of_fuel", "warning", "gameplay", Id,
                $"Out of fuel at distance {chassis.Transform.Position3D.X - startX:0.0}m. Coasting only.");
        }

        // Wheel spin is re-authored every frame at the live throttle target. Forcing the CHASSIS's
        // own linearVelocityX directly (an earlier attempt) is worse, not better: the chassis is
        // the shared anchor for both wheel Hinge joints, and resetting its velocity externally
        // every frame while the solver simultaneously tries to satisfy two active joint
        // constraints on it caused outright numerical blow-up (positions diverging to thousands
        // of units within seconds). Driving the wheels (each only jointed to the chassis, not to
        // each other) is the more stable of the two, though sustained full-throttle for tens of
        // seconds can still eventually build up a pitch resonance - documented as a known tuning
        // follow-up rather than fully resolved in this pass.
        var wheelSpeed = -throttle * throttleSpeed;
        if (!string.IsNullOrEmpty(wheelRearId))
        {
            world = world.UpdateEntity(wheelRearId, wheel => wheel.WithComponentNumber(Rigidbody3DType, "angularVelocityZ", wheelSpeed));
        }

        if (!string.IsNullOrEmpty(wheelFrontId))
        {
            world = world.UpdateEntity(wheelFrontId, wheel => wheel.WithComponentNumber(Rigidbody3DType, "angularVelocityZ", wheelSpeed));
        }

        // --- Side-view chase camera: tracks the chassis X/Y, keeps its authored Z/rotation
        // (an unrotated camera faces +Z per Rekall.Camera3D's own contract, so this camera is
        // authored at yaw=180 to face back toward -Z where the level sits) ---
        if (!string.IsNullOrEmpty(cameraId))
        {
            var chassisPosition = chassis.Transform.Position3D;
            world = world.UpdateEntity(cameraId, camera => camera.WithPosition3D(new RekallAgeRuntimeVector3(
                chassisPosition.X,
                chassisPosition.Y + 2.5,
                camera.Transform.Position3D.Z)));
        }

        // --- Persist run state ---
        world = world.UpdateEntity(chassis.Id, entity => entity
            .WithComponentNumber(RunStateType, "fuel", fuel)
            .WithComponentBoolean(RunStateType, "outOfFuel", outOfFuel)
            .WithComponentBoolean(RunStateType, "crashed", crashed)
            .WithComponentBoolean(RunStateType, "finished", finished));

        return ValueTask.FromResult(world);
    }

    private static double Normalize180(double degrees)
    {
        var wrapped = degrees % 360;
        if (wrapped > 180)
        {
            wrapped -= 360;
        }
        else if (wrapped < -180)
        {
            wrapped += 360;
        }

        return wrapped;
    }
}
