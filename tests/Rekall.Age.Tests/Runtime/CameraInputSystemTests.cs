using System.Text.Json.Nodes;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Tests.Runtime;

public sealed class CameraInputSystemTests
{
    [Fact]
    public async Task MouseWheelZoomsOrthographicCamera()
    {
        var camera = new RekallAgeRuntimeEntity(
            "camera",
            "Orrery Camera",
            ["camera"],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity,
            [
                new RekallAgeRuntimeComponent(
                    "Rekall.Camera3D",
                    new JsonObject
                    {
                        ["active"] = true,
                        ["projectionMode"] = "orthographic",
                        ["orthographicSize"] = 200
                    }),
                new RekallAgeRuntimeComponent(
                    "Rekall.CameraZoomInput",
                    new JsonObject
                    {
                        ["active"] = true,
                        ["wheelZoomSpeed"] = 0.25,
                        ["minimumOrthographicSize"] = 50,
                        ["maximumOrthographicSize"] = 400
                    })
            ]);
        var world = new RekallAgeRuntimeWorld(
            "scene",
            "Main",
            0,
            TimeSpan.Zero,
            [camera],
            RekallAgeRuntimeSubsystemViews.Empty,
            []);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(
                world,
                1,
                CancellationToken.None,
                new RekallAgeRuntimeInputState(MouseWheelDelta: 1));

        var updatedCamera = result.World.Entities.Single().Components.Single(component =>
            component.Type == "Rekall.Camera3D");
        Assert.True(ReadNumber(updatedCamera.Properties, "orthographicSize") < 200);
        Assert.Equal(
            ReadNumber(updatedCamera.Properties, "orthographicSize"),
            Assert.Single(result.World.Subsystems.Rendering.Cameras).OrthographicSize,
            precision: 6);
        Assert.Contains("runtime.input.camera", result.World.SystemsRun);
    }

    [Fact]
    public async Task MouseWheelZoomHonorsOrthographicClamps()
    {
        var camera = new RekallAgeRuntimeEntity(
            "camera",
            "Orrery Camera",
            ["camera"],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity,
            [
                new RekallAgeRuntimeComponent(
                    "Rekall.Camera3D",
                    new JsonObject
                    {
                        ["active"] = true,
                        ["projectionMode"] = "orthographic",
                        ["orthographicSize"] = 60
                    }),
                new RekallAgeRuntimeComponent(
                    "Rekall.CameraZoomInput",
                    new JsonObject
                    {
                        ["active"] = true,
                        ["wheelZoomSpeed"] = 1,
                        ["minimumOrthographicSize"] = 50,
                        ["maximumOrthographicSize"] = 400
                    })
            ]);
        var world = new RekallAgeRuntimeWorld(
            "scene",
            "Main",
            0,
            TimeSpan.Zero,
            [camera],
            RekallAgeRuntimeSubsystemViews.Empty,
            []);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(
                world,
                1,
                CancellationToken.None,
                new RekallAgeRuntimeInputState(MouseWheelDelta: 12));

        var updatedCamera = result.World.Entities.Single().Components.Single(component =>
            component.Type == "Rekall.Camera3D");
        Assert.Equal(50, ReadNumber(updatedCamera.Properties, "orthographicSize"), precision: 6);
    }

    private static double ReadNumber(JsonObject properties, string name)
    {
        Assert.True(properties.TryGetPropertyValue(name, out var node));
        Assert.NotNull(node);
        return node!.GetValue<double>();
    }
}
