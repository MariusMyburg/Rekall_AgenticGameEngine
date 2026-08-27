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
