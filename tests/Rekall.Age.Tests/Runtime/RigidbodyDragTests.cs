using System.Numerics;
using System.Text.Json.Nodes;
using Rekall.Age.Runtime;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Runtime;

public sealed class RigidbodyDragTests
{
    [Fact]
    public async Task LinearDragSlowsAFreelyMovingBodyFasterThanNoDrag()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Physics Settings", ["settings"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.PhysicsWorld3D", new JsonObject { ["GravityY"] = 0 })))
            .AddEntity(RekallAgeEntityDocument.Create("Dragged", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject { ["x"] = 0, ["y"] = 5, ["z"] = 0 }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Rigidbody3D", new JsonObject { ["mass"] = 1, ["linearVelocityX"] = 10, ["linearDrag"] = 3 }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.SphereCollider3D", new JsonObject { ["radius"] = 0.2 })))
            .AddEntity(RekallAgeEntityDocument.Create("Undragged", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject { ["x"] = 0, ["y"] = 8, ["z"] = 0 }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Rigidbody3D", new JsonObject { ["mass"] = 1, ["linearVelocityX"] = 10 }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.SphereCollider3D", new JsonObject { ["radius"] = 0.2 })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, frames: 60, CancellationToken.None);

        var dragged = result.World.Entities.Single(entity => entity.Name == "Dragged").Transform.Position3D.X;
        var undragged = result.World.Entities.Single(entity => entity.Name == "Undragged").Transform.Position3D.X;

        Assert.True(dragged < undragged, $"Expected LinearDrag to travel less far than an equal, undragged body. dragged={dragged}, undragged={undragged}.");
        Assert.True(dragged > 0, "Expected the dragged body to still make meaningful forward progress, not be stopped dead.");
    }

    [Fact]
    public async Task AngularDragSlowsSpinFasterThanNoDrag()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Physics Settings", ["settings"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.PhysicsWorld3D", new JsonObject { ["GravityY"] = 0 })))
            .AddEntity(RekallAgeEntityDocument.Create("Dragged", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject { ["x"] = 0, ["y"] = 5, ["z"] = 0 }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Rigidbody3D", new JsonObject { ["mass"] = 1, ["angularVelocityY"] = 180, ["angularDrag"] = 5 }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.SphereCollider3D", new JsonObject { ["radius"] = 0.2 })))
            .AddEntity(RekallAgeEntityDocument.Create("Undragged", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject { ["x"] = 0, ["y"] = 8, ["z"] = 0 }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Rigidbody3D", new JsonObject { ["mass"] = 1, ["angularVelocityY"] = 180 }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.SphereCollider3D", new JsonObject { ["radius"] = 0.2 })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        // 180 deg/s for 10 frames (~1/6s) at 60fps stays well clear of a half or full
        // revolution for either body, so raw (unwrapped) yaw stays monotonic and comparable -
        // a longer/faster run risks a near-360-degree spin reading back as ~0 (Euler-angle
        // extraction from an orientation has no memory of total revolutions, only current pose).
        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, frames: 10, CancellationToken.None);

        var draggedYaw = Math.Abs(NormalizeDegrees(result.World.Entities.Single(entity => entity.Name == "Dragged").Transform.Rotation3D.Y));
        var undraggedYaw = Math.Abs(NormalizeDegrees(result.World.Entities.Single(entity => entity.Name == "Undragged").Transform.Rotation3D.Y));

        Assert.True(draggedYaw < undraggedYaw, $"Expected AngularDrag to have spun less far than an equal, undragged body. dragged={draggedYaw}, undragged={undraggedYaw}.");
    }

    [Fact]
    public async Task ZeroDragMatchesPriorUndampedBehaviorForABodyUnderGravity()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Falling", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject { ["x"] = 0, ["y"] = 10, ["z"] = 0 }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Rigidbody3D", new JsonObject { ["mass"] = 1 }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.SphereCollider3D", new JsonObject { ["radius"] = 0.2 })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, frames: 10, CancellationToken.None);

        var y = result.World.Entities.Single(entity => entity.Name == "Falling").Transform.Position3D.Y;

        // Free fall over 10 frames at 60fps (~0.1667s): y = 10 - 0.5*9.81*t^2 ~= 9.86.
        Assert.InRange(y, 9.5, 10.0);
    }

    private static double NormalizeDegrees(double degrees)
    {
        var wrapped = degrees % 360;
        if (wrapped > 180) wrapped -= 360;
        if (wrapped < -180) wrapped += 360;
        return wrapped;
    }
}
