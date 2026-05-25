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
        Assert.Equal(-18.435, updatedCamera.Transform.Rotation3D.X, precision: 2);
        Assert.Equal(0, updatedCamera.Transform.Rotation3D.Y, precision: 2);
        Assert.Contains("runtime.camera.target3d", result.World.SystemsRun);
    }
}
