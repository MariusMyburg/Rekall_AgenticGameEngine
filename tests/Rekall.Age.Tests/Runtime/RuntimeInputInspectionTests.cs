using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.Runtime.Commands;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Runtime;

public sealed class RuntimeInputInspectionTests
{
    [Fact]
    public async Task RuntimeInspectionAcceptsInjectedInputFramesAndReportsActions()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Camera2D",
                    new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Input", ["input"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.InputActionMap",
                    new JsonObject
                    {
                        ["actions"] = new JsonArray
                        {
                            new JsonObject { ["name"] = "thrust", ["key"] = "W" },
                            new JsonObject { ["name"] = "zoom", ["mouseWheelScale"] = 0.5 }
                        }
                    })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("runtime-input"), CancellationToken.None);

        var result = await new InspectSceneRuntimeCommand().ExecuteAsync(
            new InspectSceneRuntimeRequest(
                root,
                "Main",
                1,
                [
                    new RekallAgeRuntimeInputFrame(
                        MouseWheelDelta: -2,
                        PressedKeys: ["W"],
                        PressedKeysThisFrame: ["W"])
                ]),
            context);

        Assert.True(result.Ok);
        Assert.Equal(2, result.Value.InputActionCount);
        var thrust = Assert.Single(result.Value.InputActions, action => action.Name == "thrust");
        Assert.True(thrust.IsDown);
        Assert.True(thrust.WasPressed);
        Assert.Equal(1, thrust.Value);
        var zoom = Assert.Single(result.Value.InputActions, action => action.Name == "zoom");
        Assert.Equal(-1, zoom.Value);
    }

    [Fact]
    public async Task RuntimeInspectionReportsRuntimeEventsForAgents()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world"])
            .AddEntity(RekallAgeEntityDocument.Create("Pointer", ["input"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.PointerRay",
                    new JsonObject
                    {
                        ["pointerId"] = "primary",
                        ["directionZ"] = 1,
                        ["range"] = 10
                    })))
            .AddEntity(RekallAgeEntityDocument.Create("Target", ["target"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["z"] = 5 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.SphereCollider3D",
                    new JsonObject { ["radius"] = 0.5 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.EventBindings",
                    new JsonObject
                    {
                        ["events"] = new JsonArray
                        {
                            new JsonObject { ["event"] = "pointer.hit", ["handler"] = "inspect" }
                        }
                    })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);

        var result = await new InspectSceneRuntimeCommand().ExecuteAsync(
            new InspectSceneRuntimeRequest(root, "Main", 1),
            new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("runtime-events"), CancellationToken.None));

        Assert.True(result.Ok);
        Assert.Equal(1, result.Value.EventCount);
        var runtimeEvent = Assert.Single(result.Value.Events);
        Assert.Equal("pointer.hit", runtimeEvent.Type);
        Assert.Equal("Target", runtimeEvent.EntityName);
        Assert.Equal("inspect", runtimeEvent.Handler);
    }
}
