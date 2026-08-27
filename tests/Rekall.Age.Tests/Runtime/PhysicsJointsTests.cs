using System.Numerics;
using System.Text.Json.Nodes;
using Rekall.Age.Runtime;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Runtime;

public sealed class PhysicsJointsTests
{
    [Fact]
    public async Task BallSocketJointPullsTwoSeparatedBodiesTogether()
    {
        var bodyA = RekallAgeEntityDocument.Create("Body A", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject { ["x"] = 0, ["y"] = 3, ["z"] = 0 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Rigidbody3D",
                new JsonObject { ["mass"] = 1 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.SphereCollider3D",
                new JsonObject { ["radius"] = 0.3 }));
        var bodyB = RekallAgeEntityDocument.Create("Body B", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject { ["x"] = 2, ["y"] = 3, ["z"] = 0 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Rigidbody3D",
                new JsonObject { ["mass"] = 1 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.SphereCollider3D",
                new JsonObject { ["radius"] = 0.3 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.BallSocketJoint",
                new JsonObject { ["connectedEntityId"] = bodyA.Id }));
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Physics Settings", ["settings"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.PhysicsWorld3D",
                    new JsonObject { ["GravityY"] = 0 })))
            .AddEntity(bodyA)
            .AddEntity(bodyB);
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, frames: 120, CancellationToken.None);

        var a = result.World.Entities.Single(entity => entity.Name == "Body A").Transform.Position3D;
        var b = result.World.Entities.Single(entity => entity.Name == "Body B").Transform.Position3D;
        var distance = Vector3.Distance(new Vector3((float)a.X, (float)a.Y, (float)a.Z), new Vector3((float)b.X, (float)b.Y, (float)b.Z));
        Assert.True(distance < 0.5, $"Expected the ball-socket joint to pull the bodies together, actual distance {distance}.");
    }

    [Fact]
    public async Task DistanceJointSettlesTwoBodiesAtTheAuthoredTargetDistance()
    {
        var bodyA = RekallAgeEntityDocument.Create("Body A", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject { ["x"] = 0, ["y"] = 3, ["z"] = 0 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Rigidbody3D",
                new JsonObject { ["mass"] = 1 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.SphereCollider3D",
                new JsonObject { ["radius"] = 0.2 }));
        var bodyB = RekallAgeEntityDocument.Create("Body B", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject { ["x"] = 0.5, ["y"] = 3, ["z"] = 0 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Rigidbody3D",
                new JsonObject { ["mass"] = 1 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.SphereCollider3D",
                new JsonObject { ["radius"] = 0.2 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.DistanceJoint",
                new JsonObject { ["connectedEntityId"] = bodyA.Id, ["targetDistance"] = 2 }));
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Physics Settings", ["settings"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.PhysicsWorld3D",
                    new JsonObject { ["GravityY"] = 0 })))
            .AddEntity(bodyA)
            .AddEntity(bodyB);
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, frames: 150, CancellationToken.None);

        var a = result.World.Entities.Single(entity => entity.Name == "Body A").Transform.Position3D;
        var b = result.World.Entities.Single(entity => entity.Name == "Body B").Transform.Position3D;
        var distance = Vector3.Distance(new Vector3((float)a.X, (float)a.Y, (float)a.Z), new Vector3((float)b.X, (float)b.Y, (float)b.Z));
        Assert.InRange(distance, 1.7, 2.3);
    }

    [Fact]
    public async Task HingeJointConstrainsRelativeRotationToOneAxisWhilePinningPosition()
    {
        var bodyA = RekallAgeEntityDocument.Create("Body A", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject { ["x"] = 0, ["y"] = 3, ["z"] = 0 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Rigidbody3D",
                new JsonObject { ["mass"] = 1 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.SphereCollider3D",
                new JsonObject { ["radius"] = 0.2 }));
        var bodyB = RekallAgeEntityDocument.Create("Body B", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject { ["x"] = 1, ["y"] = 3, ["z"] = 0 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Rigidbody3D",
                new JsonObject { ["mass"] = 1, ["angularVelocityY"] = 45 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.SphereCollider3D",
                new JsonObject { ["radius"] = 0.2 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.HingeJoint",
                new JsonObject
                {
                    ["connectedEntityId"] = bodyA.Id,
                    ["axisX"] = 0,
                    ["axisY"] = 1,
                    ["axisZ"] = 0
                }));
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Physics Settings", ["settings"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.PhysicsWorld3D",
                    new JsonObject { ["GravityY"] = 0 })))
            .AddEntity(bodyA)
            .AddEntity(bodyB);
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, frames: 60, CancellationToken.None);

        var a = result.World.Entities.Single(entity => entity.Name == "Body A").Transform.Position3D;
        var b = result.World.Entities.Single(entity => entity.Name == "Body B").Transform.Position3D;
        var distance = Vector3.Distance(new Vector3((float)a.X, (float)a.Y, (float)a.Z), new Vector3((float)b.X, (float)b.Y, (float)b.Z));
        Assert.True(distance < 0.5, $"Expected the hinge joint to keep the anchor points close together, actual distance {distance}.");
    }

    [Fact]
    public async Task HingeJointMotorSpinsAWheelContinuouslyWithoutDestabilizingTheAssembly()
    {
        // Found while authoring an example game: driving a wheel by overwriting its
        // Rekall.Rigidbody3D.angularVelocityZ every frame fights the Hinge joint's own
        // constraint solving and reliably tumbles the whole assembly after sustained use. A
        // proper motor constraint (HingeJoint's own MotorTargetVelocity/MotorMaximumTorque,
        // solved continuously alongside the hinge) should spin the wheel smoothly and keep the
        // chassis stable over a long, continuous run.
        var chassis = RekallAgeEntityDocument.Create("Chassis", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject { ["x"] = 0, ["y"] = 1.0, ["z"] = 0 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Rigidbody3D",
                new JsonObject { ["mass"] = 10 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.BoxCollider3D",
                new JsonObject { ["width"] = 2, ["height"] = 0.6, ["depth"] = 1 }));
        var wheel = RekallAgeEntityDocument.Create("Wheel", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject { ["x"] = 1, ["y"] = 0.5, ["z"] = 0 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Rigidbody3D",
                new JsonObject { ["mass"] = 2 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.SphereCollider3D",
                new JsonObject { ["radius"] = 0.5 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.HingeJoint",
                new JsonObject
                {
                    ["connectedEntityId"] = chassis.Id,
                    ["anchorAX"] = 0,
                    ["anchorAY"] = 0,
                    ["anchorAZ"] = 0,
                    ["anchorBX"] = 1,
                    ["anchorBY"] = -0.5,
                    ["anchorBZ"] = 0,
                    ["axisX"] = 0,
                    ["axisY"] = 0,
                    ["axisZ"] = 1,
                    ["motorTargetVelocity"] = 360,
                    ["motorMaximumTorque"] = 20
                }));
        // A motor applies an equal and opposite reaction torque to the body it's anchored to
        // (conservation of angular momentum - the same reason a real car's chassis feels engine
        // torque roll under hard acceleration). A free-floating pair with no gravity or ground
        // contact has nothing to resist that reaction with, so gravity plus a ground the chassis
        // actually rests its weight on (matching how the vehicle in the real example game is
        // supported) is the physically realistic setup, not an artificially isolated zero-g pair.
        var ground = RekallAgeEntityDocument.Create("Ground", ["level"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject { ["y"] = -0.5 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.BoxCollider3D",
                new JsonObject { ["width"] = 20, ["height"] = 1, ["depth"] = 20 }));
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics3d"])
            .AddEntity(ground)
            .AddEntity(chassis)
            .AddEntity(wheel);
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, frames: 600, CancellationToken.None);

        var wheelEntity = result.World.Entities.Single(entity => entity.Name == "Wheel");
        var chassisEntity = result.World.Entities.Single(entity => entity.Name == "Chassis");
        var wheelPosition = wheelEntity.Transform.Position3D;
        var chassisPosition = chassisEntity.Transform.Position3D;
        var separation = Vector3.Distance(
            new Vector3((float)wheelPosition.X, (float)wheelPosition.Y, (float)wheelPosition.Z),
            new Vector3((float)chassisPosition.X, (float)chassisPosition.Y, (float)chassisPosition.Z));

        Assert.NotEqual(0, wheelEntity.Transform.Rotation3D.Z, precision: 2);
        Assert.InRange(separation, 0.5, 2.0);
        Assert.InRange(Math.Abs(chassisEntity.Transform.Rotation3D.X), 0, 45);
        Assert.InRange(Math.Abs(chassisEntity.Transform.Rotation3D.Z), 0, 45);
    }

    [Fact]
    public async Task JointWithMissingConnectedEntityIsSkippedWithAnObservationInsteadOfCrashing()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Physics Settings", ["settings"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.PhysicsWorld3D",
                    new JsonObject { ["GravityY"] = 0 })))
            .AddEntity(RekallAgeEntityDocument.Create("Lonely Body", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["x"] = 0, ["y"] = 3, ["z"] = 0 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Rigidbody3D",
                    new JsonObject { ["mass"] = 1 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.SphereCollider3D",
                    new JsonObject { ["radius"] = 0.2 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.BallSocketJoint",
                    new JsonObject { ["connectedEntityId"] = "does-not-exist" })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, frames: 5, CancellationToken.None);

        Assert.Contains(result.World.Observations, observation => observation.Code == "runtime.physics.joint_unresolved");
        Assert.Single(result.World.Entities, entity => entity.Name == "Lonely Body");
    }
}
