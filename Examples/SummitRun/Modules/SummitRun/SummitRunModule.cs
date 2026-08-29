using System;
using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.SummitRun;

[RekallAgeModule("game.summitrun", "Summit Run")]
[RekallAgeRequiresCapability("world")]
public sealed class SummitRunModule : RekallAgeModule
{
    public override void Configure(RekallAgeModuleBuilder builder)
    {
        builder.RegisterComponent<SummitRunState>();
        builder.RegisterComponent<CellState>();
        builder.RegisterRuntimeSystem<SummitRunSystem>();
    }
}

// Inspectable per-frame game state attached to the rover.
[RekallAgeComponent("Summit Run State")]
public sealed class SummitRunState : RekallAgeComponent
{
    [RekallAgeProperty] public bool Enabled { get; init; } = true;
    [RekallAgeProperty] public double RoverX { get; init; } = 1;
    [RekallAgeProperty] public double RoverVel { get; init; } = 0;
    [RekallAgeProperty] public double Lean { get; init; } = 0;
    [RekallAgeProperty] public double WheelSpin { get; init; } = 0;
    [RekallAgeProperty] public double Fuel { get; init; } = 1;
    [RekallAgeProperty] public double Cells { get; init; } = 0;
    [RekallAgeProperty] public double Distance { get; init; } = 0;
    [RekallAgeProperty] public string Status { get; init; } = "ready";
    // Monotonic count of reset/restart actions processed; 0 at scene start, increments on each reset.
    [RekallAgeProperty] public double ResetCount { get; init; } = 0;
}

// State for an energy-cell collectible.
[RekallAgeComponent("Summit Cell")]
public sealed class CellState : RekallAgeComponent
{
    [RekallAgeProperty] public bool Collected { get; init; } = false;
}

public sealed class SummitRunSystem : IRekallAgeRuntimeModuleSystem
{
    public string Id => nameof(SummitRunSystem);
    // Drive the hinge motors before the engine's physics system consumes them.
    public int Priority => -10;

    private const string StateType = "Game.Modules.SummitRun.SummitRunState";
    private const string CellType = "Game.Modules.SummitRun.CellState";
    private const string RigidbodyType = "Rekall.Rigidbody2D";
    private const string HingeType = "Rekall.HingeJoint";

    private const double CourseStart = 1;
    private const double FinishX = 71;
    private const double StartChassisY = 5.967;
    private const double StartWheelY = 5.217;
    private const double MotorSpeed = 220;
    private const double FuelDrainPerSecond = 0.04;
    private const double CellRefuel = 0.35;
    private const double CellRadius = 1.5;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var rovers = world.EntitiesWithTag("rover");
        if (rovers.Count == 0)
        {
            world = world.EmitSceneObservation(
                "SUMMITRUN_ROVER_MISSING", "warning", "gameplay", "game.summitrun",
                "No entity tagged 'rover' with a Summit Run state was found; physics is idle. Author a Rover entity tagged 'rover' with a SummitRunState component.",
                null);
            return ValueTask.FromResult(world);
        }

        if (rovers[0] is not { } rover)
            return ValueTask.FromResult(world);
        if (!rover.ComponentBoolean(StateType, "enabled", true))
            return ValueTask.FromResult(world);

        var seconds = context.DeltaTime.TotalSeconds;
        if (seconds <= 0)
            return ValueTask.FromResult(world);

        var previousX = rover.ComponentNumber(StateType, "roverX", CourseStart);
        var x = rover.Transform.Position2D.X;
        var vel = rover.ComponentNumber(StateType, "roverVel", 0);
        var lean = rover.Transform.Rotation2D;
        var spin = rover.ComponentNumber(StateType, "wheelSpin", 0);
        var fuel = rover.ComponentNumber(StateType, "fuel", 1);
        var cells = rover.ComponentNumber(StateType, "cells", 0);
        var distance = rover.ComponentNumber(StateType, "distance", 0);
        var status = rover.ComponentString(StateType, "status", "ready");
        var resetCount = rover.ComponentNumber(StateType, "resetCount", 0);

        var throttle = world.InputActionValue("drive");
        var leanInput = world.InputActionValue("lean");
        var reset = world.WasInputActionPressed("reset");

        if (reset)
        {
            x = CourseStart;
            vel = 0;
            lean = 0;
            spin = 0;
            fuel = 1;
            cells = 0;
            distance = 0;
            status = "ready";
            resetCount = resetCount + 1;
            world = world.UpdateEntitiesWithTag("cell", e =>
                e.WithVisible(true).WithComponentBoolean(CellType, "collected", false));
            world = world.UpdateEntity(rover.Id, e => e
                .WithPhysicsPoseAndVelocity2D(
                    new RekallAgeRuntimeVector2(CourseStart, StartChassisY),
                    0,
                    new RekallAgeRuntimeVector2(0, 0),
                    0));
            world = world.UpdateEntitiesWithTag("wheel", e =>
            {
                var wheelX = e.Name.Equals("WheelBack", StringComparison.Ordinal) ? 0.15 : 1.85;
                return e.WithPhysicsPoseAndVelocity2D(
                    new RekallAgeRuntimeVector2(wheelX, StartWheelY),
                    0,
                    new RekallAgeRuntimeVector2(0, 0),
                    0);
            });
        }

        var active = status != "complete" && status != "out-of-fuel";
        if (active && !reset)
        {
            vel = (x - previousX) / seconds;
            spin += vel / 0.55 * seconds;
            if (Math.Abs(throttle) > 0.01)
                fuel = Math.Max(0, fuel - FuelDrainPerSecond * Math.Abs(throttle) * seconds);
            distance = Math.Max(distance, x - CourseStart);

            // Transition to "driving" when the player applies throttle and we're still in "ready".
            if (Math.Abs(throttle) > 0.01 && status == "ready") status = "driving";

            if (fuel <= 0.0001) status = "out-of-fuel";
            if (x >= FinishX) status = "complete";
        }

        var motorTarget = active && fuel > 0 ? -Math.Clamp(throttle, -1, 1) * MotorSpeed : 0;
        world = world.UpdateEntitiesWithTag("wheel", e =>
            e.WithComponentNumber(HingeType, "motorTargetVelocity", motorTarget));
        world = world.UpdateEntity(rover.Id, e =>
            e.WithComponentNumber(
                RigidbodyType,
                "angularCorrectionZ",
                -lean * 6 + leanInput * 180));

        // Collect energy cells near the rover.
        foreach (var c in reset ? Array.Empty<RekallAgeRuntimeEntity>() : world.EntitiesWithTag("cell"))
        {
            if (c is null) continue;
            if (c.ComponentBoolean(CellType, "collected", false)) continue;
            var cx = c.Transform.Position2D.X;
            var cy = c.Transform.Position2D.Y;
            var dx = cx - x;
            var dy = cy - rover.Transform.Position2D.Y;
            if (dx * dx + dy * dy < CellRadius * CellRadius)
            {
                world = world.UpdateEntity(c.Id, cur =>
                    cur.WithVisible(false).WithComponentBoolean(CellType, "collected", true));
                cells += 1;
                fuel = Math.Min(1, fuel + CellRefuel);
            }
        }

        world = world.UpdateEntity(rover.Id, current => current
            .WithComponentNumber(StateType, "roverX", x)
            .WithComponentNumber(StateType, "roverVel", vel)
            .WithComponentNumber(StateType, "lean", lean)
            .WithComponentNumber(StateType, "wheelSpin", spin)
            .WithComponentNumber(StateType, "fuel", fuel)
            .WithComponentNumber(StateType, "cells", cells)
            .WithComponentNumber(StateType, "distance", distance)
            .WithComponentString(StateType, "status", status)
            .WithComponentNumber(StateType, "resetCount", resetCount));

        // Follow the rover with the orthographic camera.
        var cams = world.EntitiesWithTag("camera");
        if (cams.Count > 0 && cams[0] is { } cam)
        {
            var followY = reset ? StartChassisY + 1.0 : rover.Transform.Position2D.Y + 1.0;
            world = world.UpdateEntity(cam.Id, cur =>
                cur.WithPosition2D(new RekallAgeRuntimeVector2(x + 1.5, followY)));
        }
        else
        {
            world = world.EmitSceneObservation(
                "SUMMITRUN_CAMERA_MISSING", "warning", "camera", "game.summitrun",
                "No entity tagged 'camera' was found; the view is not following the rover. Author a Camera2D entity tagged 'camera'.",
                null);
        }

        // Keep the HUD readable.
        world = UpdateHud(world, "hudFuel", $"FUEL  {Math.Round(fuel * 100):0}%");
        world = UpdateHud(world, "hudDistance", $"DIST  {Math.Round(distance):0} m");
        world = UpdateHud(world, "hudCells", $"CELLS  {Math.Round(cells):0}");
        world = UpdateHud(world, "hudStatus", $"STATUS  {(status ?? "ready").ToUpperInvariant()}");

        return ValueTask.FromResult(world);
    }

    private static RekallAgeRuntimeWorld UpdateHud(
        RekallAgeRuntimeWorld world, string tag, string text)
    {
        var labels = world.EntitiesWithTag(tag);
        if (labels.Count == 0) return world;
        if (labels[0] is not { } first) return world;
        return world.UpdateEntity(first.Id, cur =>
            cur.WithComponentString("Rekall.Label", "Text", text));
    }
}
