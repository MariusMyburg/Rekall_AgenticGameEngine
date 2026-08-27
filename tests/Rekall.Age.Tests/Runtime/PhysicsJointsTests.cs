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
    public async Task HingeJointAxisIsAWorldSpaceDirectionEvenWhenTheConnectedBodiesDoNotShareAnOrientation()
    {
        // Found while modeling a wheel whose own collider needed a 90-degree local rotation
        // (to align a capsule shape's own long axis with the wheel's spin axis) while its hinge
        // parent (the chassis) stays unrotated. The authored axis (0,0,1) is meant as "spin
        // around world Z" regardless of that rotation - previously the same raw axis value was
        // used verbatim as both bodies' LOCAL axis, which is only correct when both bodies share
        // an orientation. This wheel is deliberately pre-rotated 90 degrees around X relative to
        // its chassis, so a broken (unconverted) axis would drive the wheel to spin around the
        // wrong world axis entirely and the assembly would never translate.
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
                new JsonObject { ["x"] = 1, ["y"] = 0.5, ["z"] = 0, ["pitch"] = 90 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Rigidbody3D",
                new JsonObject { ["mass"] = 2 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.CapsuleCollider3D",
                new JsonObject { ["radius"] = 0.5, ["length"] = 0.2 }))
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
            .RunAsync(world, frames: 240, CancellationToken.None);

        var wheelEntity = result.World.Entities.Single(entity => entity.Name == "Wheel");
        var chassisEntity = result.World.Entities.Single(entity => entity.Name == "Chassis");

        // A correctly-resolved world-Z spin axis rolls the wheel and drags the chassis along
        // with it (via the hinge anchor); a broken axis instead spins around some other world
        // direction (most likely local Y, world "up" for the unrotated chassis's own frame) and
        // the assembly barely translates from its starting X.
        Assert.True(
            Math.Abs(chassisEntity.Transform.Position3D.X) > 0.2,
            $"Expected the chassis to be dragged along the ground by a correctly-axed wheel motor, actual X {chassisEntity.Transform.Position3D.X}.");
        Assert.InRange(Math.Abs(chassisEntity.Transform.Rotation3D.Z), 0, 45);
    }

    [Fact]
    public async Task WeldJointLocksBothRelativePositionAndOrientation()
    {
        // Body A starts pre-rotated 30 degrees around Y and slightly separated from Body B, which
        // starts unrotated. The weld's authored LocalOffset/LocalOrientation are both identity (B
        // should end up coincident with, and oriented like, A). Since both bodies have equal mass and
        // moment of inertia, a symmetric two-body correction settles them toward each other rather than
        // one pinning the other exactly at its original value - so this asserts what Weld actually
        // promises (position AND orientation converge to match) rather than predicting one exact
        // final angle.
        var bodyA = RekallAgeEntityDocument.Create("Body A", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject { ["x"] = 0, ["y"] = 3, ["z"] = 0, ["yaw"] = 30 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Rigidbody3D",
                new JsonObject { ["mass"] = 1 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.BoxCollider3D",
                new JsonObject { ["width"] = 0.4, ["height"] = 0.4, ["depth"] = 0.4 }));
        var bodyB = RekallAgeEntityDocument.Create("Body B", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject { ["x"] = 0.3, ["y"] = 3, ["z"] = 0 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Rigidbody3D",
                new JsonObject { ["mass"] = 1 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.BoxCollider3D",
                new JsonObject { ["width"] = 0.4, ["height"] = 0.4, ["depth"] = 0.4 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.WeldJoint",
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
            .RunAsync(world, frames: 90, CancellationToken.None);

        var a = result.World.Entities.Single(entity => entity.Name == "Body A");
        var b = result.World.Entities.Single(entity => entity.Name == "Body B");
        var positionA = a.Transform.Position3D;
        var positionB = b.Transform.Position3D;
        var distance = Vector3.Distance(
            new Vector3((float)positionA.X, (float)positionA.Y, (float)positionA.Z),
            new Vector3((float)positionB.X, (float)positionB.Y, (float)positionB.Z));

        Assert.True(distance < 0.2, $"Expected the weld to pull the two bodies to a coincident position, actual distance {distance}.");
        Assert.InRange(
            Math.Abs(Normalize180(a.Transform.Rotation3D.Y - b.Transform.Rotation3D.Y)),
            0,
            10);
    }

    [Fact]
    public async Task FixedJointPinsABodyToAWorldSpaceAnchorInsteadOfAnotherEntity()
    {
        var body = RekallAgeEntityDocument.Create("Body", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject { ["x"] = 3, ["y"] = 5, ["z"] = 0 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Rigidbody3D",
                new JsonObject { ["mass"] = 1 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.SphereCollider3D",
                new JsonObject { ["radius"] = 0.2 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.FixedJoint",
                new JsonObject { ["anchorX"] = 0, ["anchorY"] = 5, ["anchorZ"] = 0 }));
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Physics Settings", ["settings"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.PhysicsWorld3D",
                    new JsonObject { ["GravityY"] = -9.81 })))
            .AddEntity(body);
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, frames: 120, CancellationToken.None);

        var position = result.World.Entities.Single(entity => entity.Name == "Body").Transform.Position3D;
        var distanceFromAnchor = Vector3.Distance(
            new Vector3((float)position.X, (float)position.Y, (float)position.Z),
            new Vector3(0, 5, 0));
        Assert.True(
            distanceFromAnchor < 0.5,
            $"Expected the fixed joint to hold the body near its world anchor despite gravity and its starting offset, actual distance {distanceFromAnchor}.");
    }

    [Fact]
    public async Task HingeJointAngleLimitStopsRotationWithinAuthoredBounds()
    {
        // Same setup as HingeJointConstrainsRelativeRotationToOneAxisWhilePinningPosition, but with a
        // tight [-10, 10] degree limit and enough spin (45 deg/s over one second) that an unlimited
        // hinge would rotate roughly 45 degrees - the limit must visibly cap that.
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
                    ["axisZ"] = 0,
                    ["angleLimitMinimum"] = -10,
                    ["angleLimitMaximum"] = 10
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

        var rotationY = result.World.Entities.Single(entity => entity.Name == "Body B").Transform.Rotation3D.Y;
        Assert.True(
            Math.Abs(Normalize180(rotationY)) < 20,
            $"Expected the [-10, 10] degree angle limit to cap Body B's rotation well short of the ~45 degrees an unlimited hinge would reach, actual {rotationY}.");
    }

    [Fact]
    public async Task DistanceJointRangeAllowsMovementWithinLimitsButCapsBeyondThem()
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
                new JsonObject { ["x"] = 1.5, ["y"] = 3, ["z"] = 0 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Rigidbody3D",
                new JsonObject { ["mass"] = 1, ["linearVelocityX"] = 20 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.SphereCollider3D",
                new JsonObject { ["radius"] = 0.2 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.DistanceJoint",
                new JsonObject
                {
                    ["connectedEntityId"] = bodyA.Id,
                    ["distanceLimitMinimum"] = 1,
                    ["distanceLimitMaximum"] = 2
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
            .RunAsync(world, frames: 120, CancellationToken.None);

        var a = result.World.Entities.Single(entity => entity.Name == "Body A").Transform.Position3D;
        var b = result.World.Entities.Single(entity => entity.Name == "Body B").Transform.Position3D;
        var distance = Vector3.Distance(new Vector3((float)a.X, (float)a.Y, (float)a.Z), new Vector3((float)b.X, (float)b.Y, (float)b.Z));

        // A fast outward launch (20 units/s) would separate the bodies to ~40 units apart within two
        // seconds if nothing constrained them; the range limit must keep them within the authored
        // [1, 2] bounds throughout instead of a single fixed TargetDistance or unbounded separation.
        Assert.InRange(distance, 0.9, 2.3);
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

    [Fact]
    public async Task DistanceJointWorksBetweenTwoRigidbody2DEntitiesTheSameAsIn3D()
    {
        // Joint components aren't 2D/3D-specific - there's one shared set (BallSocketJoint,
        // HingeJoint, DistanceJoint, WeldJoint, FixedJoint). BEPU itself has no 2D concept at all
        // (confirmed: its own shipped XML docs contain zero mentions of "2D" or "planar" anywhere);
        // this engine's own 2D support is a planar projection of ordinary 3D BEPU bodies, so a joint
        // between two Rigidbody2D entities should work identically to the 3D case above.
        var bodyA = RekallAgeEntityDocument.Create("Body A", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform2D",
                new JsonObject { ["x"] = 0, ["y"] = 3 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Rigidbody2D",
                new JsonObject { ["mass"] = 1 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.CircleCollider2D",
                new JsonObject { ["radius"] = 0.2 }));
        var bodyB = RekallAgeEntityDocument.Create("Body B", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform2D",
                new JsonObject { ["x"] = 0.5, ["y"] = 3 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Rigidbody2D",
                new JsonObject { ["mass"] = 1 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.CircleCollider2D",
                new JsonObject { ["radius"] = 0.2 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.DistanceJoint",
                new JsonObject { ["connectedEntityId"] = bodyA.Id, ["targetDistance"] = 2 }));
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics2d"])
            .AddEntity(RekallAgeEntityDocument.Create("Physics Settings", ["settings"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.PhysicsWorld3D",
                    new JsonObject { ["GravityY"] = 0 })))
            .AddEntity(bodyA)
            .AddEntity(bodyB);
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, frames: 150, CancellationToken.None);

        var a = result.World.Entities.Single(entity => entity.Name == "Body A").Transform.Position2D;
        var b = result.World.Entities.Single(entity => entity.Name == "Body B").Transform.Position2D;
        var distance = Vector2.Distance(new Vector2((float)a.X, (float)a.Y), new Vector2((float)b.X, (float)b.Y));

        Assert.InRange(distance, 1.5, 2.5);
        Assert.DoesNotContain(result.World.Observations, observation => observation.Code == "runtime.physics.joint_unresolved");
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
