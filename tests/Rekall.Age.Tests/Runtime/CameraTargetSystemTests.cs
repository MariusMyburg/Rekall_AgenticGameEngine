using System.Text.Json.Nodes;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Tests.Runtime;

public sealed class CameraTargetSystemTests
{
    [Fact]
    public async Task DefaultRuntimePositionsAndAimsCameraAtTargetEntity()
    {
        var target = new RekallAgeRuntimeEntity(
            "target",
            "Earth",
            ["planet"],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity,
            []);
        var camera = new RekallAgeRuntimeEntity(
            "camera",
            "Chase Camera",
            ["camera"],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity,
            [
                new RekallAgeRuntimeComponent("Rekall.Camera3D", new JsonObject { ["active"] = true }),
                new RekallAgeRuntimeComponent(
                    "Rekall.CameraTarget3D",
                    new JsonObject
                    {
                        ["targetName"] = "Earth",
                        ["offsetX"] = 0,
                        ["offsetY"] = 2,
                        ["offsetZ"] = 6
                    })
            ]);
        var world = new RekallAgeRuntimeWorld(
            "scene",
            "Main",
            0,
            TimeSpan.Zero,
            [camera, target],
            RekallAgeRuntimeSubsystemViews.Empty,
            []);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 1, CancellationToken.None);

        var updatedCamera = result.World.Entities.Single(entity => entity.Name == "Chase Camera");
        Assert.Equal(0, updatedCamera.Transform.Position3D.X, precision: 3);
        Assert.Equal(2, updatedCamera.Transform.Position3D.Y, precision: 3);
        Assert.Equal(6, updatedCamera.Transform.Position3D.Z, precision: 3);
        Assert.Equal(18.435, updatedCamera.Transform.Rotation3D.X, precision: 2);
        Assert.Equal(180, updatedCamera.Transform.Rotation3D.Y, precision: 2);
        Assert.Contains("runtime.camera.target3d", result.World.SystemsRun);
    }

    [Fact]
    public async Task PositionLagTrailsPartwayTowardTheInstantTargetInsteadOfSnapping()
    {
        var target = CreateStationaryEntity("target", "Target", x: 10);
        var camera = CreateCamera(
            startX: 0,
            new JsonObject
            {
                ["targetName"] = "Target",
                ["offsetX"] = 0,
                ["offsetY"] = 0,
                ["offsetZ"] = 0,
                ["lookAt"] = false,
                ["positionLagEnabled"] = true,
                ["positionLagSpeed"] = 1
            });
        var world = CreateWorld(camera, target);

        // deltaTime=1s, lagSpeed=1 => t = 1 - e^-1 ≈ 0.63212, so the camera should land at
        // 0 + (10 - 0) * 0.63212 ≈ 6.3212 - meaningfully short of the instant target at X=10,
        // proving it trails rather than snapping.
        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 1, CancellationToken.None, deltaTime: TimeSpan.FromSeconds(1));

        var updatedCamera = result.World.Entities.Single(entity => entity.Id == "camera");
        Assert.Equal(6.3212, updatedCamera.Transform.Position3D.X, precision: 3);
        Assert.True(updatedCamera.Transform.Position3D.X > 0 && updatedCamera.Transform.Position3D.X < 10);
    }

    [Fact]
    public async Task PositionLagDisabledKeepsTheExactPriorInstantSnapBehavior()
    {
        var target = CreateStationaryEntity("target", "Target", x: 10);
        var camera = CreateCamera(
            startX: 0,
            new JsonObject
            {
                ["targetName"] = "Target",
                ["offsetX"] = 0,
                ["offsetY"] = 0,
                ["offsetZ"] = 0,
                ["lookAt"] = false
                // positionLagEnabled intentionally omitted - must default to false/instant.
            });
        var world = CreateWorld(camera, target);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 1, CancellationToken.None, deltaTime: TimeSpan.FromSeconds(1));

        var updatedCamera = result.World.Entities.Single(entity => entity.Id == "camera");
        Assert.Equal(10, updatedCamera.Transform.Position3D.X, precision: 6);
    }

    [Fact]
    public async Task MaximumPositionLagDistanceClampsHowFarBehindTheCameraCanFall()
    {
        var target = CreateStationaryEntity("target", "Target", x: 100);
        var camera = CreateCamera(
            startX: 0,
            new JsonObject
            {
                ["targetName"] = "Target",
                ["offsetX"] = 0,
                ["offsetY"] = 0,
                ["offsetZ"] = 0,
                ["lookAt"] = false,
                ["positionLagEnabled"] = true,
                ["positionLagSpeed"] = 0.01, // deliberately slow: would lag ~99 units behind unclamped
                ["maximumPositionLagDistance"] = 5
            });
        var world = CreateWorld(camera, target);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 1, CancellationToken.None, deltaTime: TimeSpan.FromSeconds(1));

        var updatedCamera = result.World.Entities.Single(entity => entity.Id == "camera");
        // Instant target is X=100; the clamp must hold the result within 5 units of that, i.e. >= 95,
        // even though the slow lag speed alone would have left it far further behind.
        Assert.True(updatedCamera.Transform.Position3D.X >= 95);
    }

    [Fact]
    public async Task RotationLagTrailsPartwayTowardTheInstantLookAtRotation()
    {
        // Camera stays put (followPosition=false) at the origin while the target sits off to the
        // side at (5, 0, 6) - a genuinely diagonal look-at direction (not straight down +Z, which
        // would trivially yaw 0 regardless of lag) - so an instant look-at yaws away from the
        // camera's starting (unrotated, yaw=0) orientation, giving lag something real to trail.
        var target = CreateStationaryEntity("target", "Target", x: 5, z: 6);
        var camera = CreateCamera(
            startX: 0,
            new JsonObject
            {
                ["targetName"] = "Target",
                ["followPosition"] = false,
                ["lookAt"] = true,
                ["rotationLagEnabled"] = true,
                ["rotationLagSpeed"] = 1
            });
        var world = CreateWorld(camera, target);

        var instantResult = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 1, CancellationToken.None, deltaTime: TimeSpan.FromSeconds(1000));
        var instantYaw = instantResult.World.Entities.Single(entity => entity.Id == "camera")
            .Transform.Rotation3D.Y;

        var laggedResult = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 1, CancellationToken.None, deltaTime: TimeSpan.FromSeconds(1));
        var laggedYaw = laggedResult.World.Entities.Single(entity => entity.Id == "camera")
            .Transform.Rotation3D.Y;

        // A huge deltaTime (1000s) drives t to effectively 1.0, so instantResult is a stand-in for
        // the true instant look-at yaw; the real 1-second-lag run must land meaningfully short of it
        // starting from the camera's own default (unrotated, yaw=0) orientation.
        Assert.NotEqual(instantYaw, laggedYaw, precision: 2);
        Assert.True(Math.Abs(laggedYaw) < Math.Abs(instantYaw));
    }

    private static RekallAgeRuntimeEntity CreateStationaryEntity(string id, string name, double x, double z = 0)
    {
        return new RekallAgeRuntimeEntity(
            id,
            name,
            [],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity with
            {
                Position3D = new RekallAgeRuntimeVector3(x, 0, z)
            },
            []);
    }

    private static RekallAgeRuntimeEntity CreateCamera(double startX, JsonObject cameraTargetProperties)
    {
        return new RekallAgeRuntimeEntity(
            "camera",
            "Chase Camera",
            ["camera"],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity with
            {
                Position3D = new RekallAgeRuntimeVector3(startX, 0, 0)
            },
            [
                new RekallAgeRuntimeComponent("Rekall.Camera3D", new JsonObject { ["active"] = true }),
                new RekallAgeRuntimeComponent("Rekall.CameraTarget3D", cameraTargetProperties)
            ]);
    }

    private static RekallAgeRuntimeWorld CreateWorld(
        RekallAgeRuntimeEntity camera,
        RekallAgeRuntimeEntity target,
        params RekallAgeRuntimeEntity[] extraEntities)
    {
        return new RekallAgeRuntimeWorld(
            "scene",
            "Main",
            0,
            TimeSpan.Zero,
            [camera, target, .. extraEntities],
            RekallAgeRuntimeSubsystemViews.Empty,
            []);
    }

    [Fact]
    public async Task CollisionAvoidancePullsTheCameraInFrontOfAnObstruction()
    {
        // Target at the origin; the camera's desired position (via offset) sits 10 units behind it
        // along -Z. A wall entity's collider sits 4 units behind the target, directly on that line,
        // so the probe ray must find it and pull the camera in short of it instead of letting it end
        // up on the far side, clipped through the wall.
        var target = CreateStationaryEntity("target", "Target", x: 0);
        var wall = new RekallAgeRuntimeEntity(
            "wall",
            "Wall",
            [],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity with
            {
                Position3D = new RekallAgeRuntimeVector3(0, 0, -4)
            },
            [new RekallAgeRuntimeComponent(
                "Rekall.BoxCollider3D",
                new JsonObject { ["width"] = 4, ["height"] = 4, ["depth"] = 1 })]);
        var camera = CreateCamera(
            startX: 0,
            new JsonObject
            {
                ["targetName"] = "Target",
                ["offsetX"] = 0,
                ["offsetY"] = 0,
                ["offsetZ"] = -10,
                ["lookAt"] = false,
                ["collisionAvoidanceEnabled"] = true,
                ["collisionMinimumDistance"] = 0.5
            });
        var world = CreateWorld(camera, target, wall);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 1, CancellationToken.None);

        var updatedCamera = result.World.Entities.Single(entity => entity.Id == "camera");
        var finalZ = updatedCamera.Transform.Position3D.Z;
        // Must have been pulled in well short of the desired Z=-10 (the wall's near face sits around
        // Z=-3.5), and must still sit on the correct (negative-Z) side of the target, not overshoot
        // past the origin or flip to the wrong side entirely.
        Assert.True(finalZ > -10 && finalZ < 0);
    }

    [Fact]
    public async Task CollisionAvoidanceLeavesTheCameraAtItsDesiredPositionWhenNothingIsInTheWay()
    {
        var target = CreateStationaryEntity("target", "Target", x: 0);
        var camera = CreateCamera(
            startX: 0,
            new JsonObject
            {
                ["targetName"] = "Target",
                ["offsetX"] = 0,
                ["offsetY"] = 0,
                ["offsetZ"] = -10,
                ["lookAt"] = false,
                ["collisionAvoidanceEnabled"] = true,
                ["collisionMinimumDistance"] = 0.5
            });
        var world = CreateWorld(camera, target);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 1, CancellationToken.None);

        var updatedCamera = result.World.Entities.Single(entity => entity.Id == "camera");
        Assert.Equal(-10, updatedCamera.Transform.Position3D.Z, precision: 3);
    }

    [Fact]
    public async Task CollisionAvoidanceDisabledLeavesTheCameraClippedThroughAnObstruction()
    {
        // Regression proof that the feature is genuinely opt-in: with the exact same obstructing
        // wall as the positive test above but collisionAvoidanceEnabled omitted (defaults false),
        // the camera must land at its full, unmodified desired position - on the far side of, and
        // clipped through, the wall - exactly the prior behavior before this feature existed.
        var target = CreateStationaryEntity("target", "Target", x: 0);
        var wall = new RekallAgeRuntimeEntity(
            "wall",
            "Wall",
            [],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity with
            {
                Position3D = new RekallAgeRuntimeVector3(0, 0, -4)
            },
            [new RekallAgeRuntimeComponent(
                "Rekall.BoxCollider3D",
                new JsonObject { ["width"] = 4, ["height"] = 4, ["depth"] = 1 })]);
        var camera = CreateCamera(
            startX: 0,
            new JsonObject
            {
                ["targetName"] = "Target",
                ["offsetX"] = 0,
                ["offsetY"] = 0,
                ["offsetZ"] = -10,
                ["lookAt"] = false
            });
        var world = CreateWorld(camera, target, wall);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 1, CancellationToken.None);

        var updatedCamera = result.World.Entities.Single(entity => entity.Id == "camera");
        Assert.Equal(-10, updatedCamera.Transform.Position3D.Z, precision: 3);
    }

    [Fact]
    public async Task CollisionAvoidanceSweepsASphereRatherThanAThinRay()
    {
        // A small sphere obstruction sits 0.2 units off to the side of the dead-straight target-to-
        // camera line (which runs along X=0, Y=0 for all Z) rather than directly on it. Its own
        // bounding radius (0.1) alone is too small to reach the line - a thin ray straight down the
        // line would miss it entirely - but with the default CollisionProbeRadius (0.15) added, the
        // combined 0.25 reach exceeds the 0.2 perpendicular offset, so a genuine sphere sweep must
        // still catch it. This is the exact scenario a single-ray probe (the prior implementation)
        // could not have detected.
        var target = CreateStationaryEntity("target", "Target", x: 0);
        var obstruction = new RekallAgeRuntimeEntity(
            "obstruction",
            "Obstruction",
            [],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity with
            {
                Position3D = new RekallAgeRuntimeVector3(0.2, 0, -5)
            },
            [new RekallAgeRuntimeComponent(
                "Rekall.SphereCollider3D",
                new JsonObject { ["radius"] = 0.1 })]);
        var camera = CreateCamera(
            startX: 0,
            new JsonObject
            {
                ["targetName"] = "Target",
                ["offsetX"] = 0,
                ["offsetY"] = 0,
                ["offsetZ"] = -10,
                ["lookAt"] = false,
                ["collisionAvoidanceEnabled"] = true,
                ["collisionMinimumDistance"] = 0.5
                // collisionProbeRadius intentionally omitted - exercises the default (0.15).
            });
        var world = CreateWorld(camera, target, obstruction);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 1, CancellationToken.None);

        var updatedCamera = result.World.Entities.Single(entity => entity.Id == "camera");
        var finalZ = updatedCamera.Transform.Position3D.Z;
        Assert.True(finalZ > -10 && finalZ < 0);
    }

    [Fact]
    public async Task CollisionProbeRadiusZeroDegradesToAThinRayMissingTheOffAxisObstruction()
    {
        // Same off-axis obstruction as the positive sweep test above, but with CollisionProbeRadius
        // explicitly set to 0: the combined reach (0 + the obstruction's own 0.1 bounding radius)
        // no longer reaches the 0.2 perpendicular offset, so this must NOT be detected - proving the
        // probe radius genuinely controls sweep thickness rather than always catching everything
        // nearby regardless of the authored radius.
        var target = CreateStationaryEntity("target", "Target", x: 0);
        var obstruction = new RekallAgeRuntimeEntity(
            "obstruction",
            "Obstruction",
            [],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity with
            {
                Position3D = new RekallAgeRuntimeVector3(0.2, 0, -5)
            },
            [new RekallAgeRuntimeComponent(
                "Rekall.SphereCollider3D",
                new JsonObject { ["radius"] = 0.1 })]);
        var camera = CreateCamera(
            startX: 0,
            new JsonObject
            {
                ["targetName"] = "Target",
                ["offsetX"] = 0,
                ["offsetY"] = 0,
                ["offsetZ"] = -10,
                ["lookAt"] = false,
                ["collisionAvoidanceEnabled"] = true,
                ["collisionMinimumDistance"] = 0.5,
                ["collisionProbeRadius"] = 0
            });
        var world = CreateWorld(camera, target, obstruction);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 1, CancellationToken.None);

        var updatedCamera = result.World.Entities.Single(entity => entity.Id == "camera");
        Assert.Equal(-10, updatedCamera.Transform.Position3D.Z, precision: 3);
    }
}
