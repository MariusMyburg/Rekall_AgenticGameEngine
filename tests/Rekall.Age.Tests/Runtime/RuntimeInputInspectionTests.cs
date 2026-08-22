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
    public async Task RuntimeInspectionProjectsInjectedSemanticActionsThroughDeclaredActionMap()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world"])
            .AddEntity(RekallAgeEntityDocument.Create("Input", ["input"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.InputActionMap",
                    new JsonObject
                    {
                        ["actions"] = new JsonArray
                        {
                            new JsonObject { ["name"] = "move.horizontal", ["positiveKey"] = "D", ["negativeKey"] = "A" }
                        }
                    })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);

        var result = await new InspectSceneRuntimeCommand().ExecuteAsync(
            new InspectSceneRuntimeRequest(
                root,
                "Main",
                1,
                [
                    new RekallAgeRuntimeInputFrame(
                        SemanticActions:
                        [
                            new RekallAgeRuntimeSemanticActionSample(
                                "move.horizontal",
                                Value: -0.75,
                                IsDown: true,
                                WasPressed: true),
                            new RekallAgeRuntimeSemanticActionSample("undeclared.action")
                        ])
                ]),
            new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("semantic-runtime-input"), CancellationToken.None));

        Assert.True(result.Ok);
        var action = Assert.Single(result.Value.InputActions);
        Assert.Equal("move.horizontal", action.Name);
        Assert.Equal(-0.75, action.Value);
        Assert.True(action.IsDown);
        Assert.True(action.WasPressed);
        Assert.False(action.WasReleased);
    }

    [Fact]
    public async Task RuntimeInspectionRejectsDuplicateSemanticActionSamples()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world"]);
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);

        var result = await new InspectSceneRuntimeCommand().ExecuteAsync(
            new InspectSceneRuntimeRequest(
                root,
                "Main",
                1,
                [
                    new RekallAgeRuntimeInputFrame(
                        SemanticActions:
                        [
                            new RekallAgeRuntimeSemanticActionSample("move.horizontal"),
                            new RekallAgeRuntimeSemanticActionSample("move.horizontal", Value: -1)
                        ])
                ]),
            new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("invalid-semantic-runtime-input"), CancellationToken.None));

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_RUNTIME_INPUT_SEMANTIC_ACTION_DUPLICATE");
    }

    [Fact]
    public async Task RuntimeInspectionRejectsUnboundedOrDuplicateControllerPayloads()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world"]),
            CancellationToken.None);
        var controllers = Enumerable.Range(0, 65)
            .Select(index => new RekallAgeRuntimeControllerState(
                index < 2 ? "duplicate" : $"pad-{index}",
                "gamepad",
                index,
                [],
                [],
                [],
                [],
                []))
            .ToArray();

        var result = await new InspectSceneRuntimeCommand().ExecuteAsync(
            new InspectSceneRuntimeRequest(
                root,
                "Main",
                1,
                [new RekallAgeRuntimeInputFrame(Controllers: controllers)]),
            new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("invalid-controller-input"), CancellationToken.None));

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_RUNTIME_INPUT_CONTROLLER_LIMIT");
        Assert.Contains(result.Errors, error => error.Code == "REKALL_RUNTIME_INPUT_CONTROLLER_DUPLICATE");
    }

    [Fact]
    public async Task RuntimeInspectionExplainsJsonEncodedActionMapInsteadOfSilentlyDroppingActions()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world"])
            .AddEntity(RekallAgeEntityDocument.Create("Input", ["input"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.InputActionMap",
                    new JsonObject
                    {
                        ["Actions"] = "[{\"name\":\"move.horizontal\",\"positiveKey\":\"D\"}]"
                    })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);

        var result = await new InspectSceneRuntimeCommand().ExecuteAsync(
            new InspectSceneRuntimeRequest(
                root,
                "Main",
                1,
                [new RekallAgeRuntimeInputFrame(SemanticActions: [new("move.horizontal")])]),
            new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("malformed-action-map"), CancellationToken.None));

        Assert.True(result.Ok);
        Assert.Empty(result.Value.InputActions);
        var malformed = Assert.Single(result.Value.Observations, observation =>
            observation.Code == "runtime.input.action_map_actions_invalid");
        Assert.Equal("error", malformed.Severity);
        Assert.Contains("JSON array", malformed.Message, StringComparison.Ordinal);
        Assert.Contains("not a JSON-encoded string", malformed.Message, StringComparison.Ordinal);
        Assert.Contains("positiveKey", malformed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RuntimeInspectionReportsInjectedSemanticActionMissingFromDeclaredMap()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world"])
            .AddEntity(RekallAgeEntityDocument.Create("Input", ["input"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.InputActionMap",
                    new JsonObject
                    {
                        ["Actions"] = new JsonArray(new JsonObject
                        {
                            ["name"] = "move.horizontal",
                            ["positiveKey"] = "D",
                            ["negativeKey"] = "A"
                        })
                    })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);

        var result = await new InspectSceneRuntimeCommand().ExecuteAsync(
            new InspectSceneRuntimeRequest(
                root,
                "Main",
                1,
                [new RekallAgeRuntimeInputFrame(SemanticActions: [new("move.vertical")])]),
            new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("undeclared-semantic-action"), CancellationToken.None));

        Assert.True(result.Ok);
        var undeclared = Assert.Single(result.Value.Observations, observation =>
            observation.Code == "runtime.input.semantic_action_undeclared");
        Assert.Contains("move.vertical", undeclared.Message, StringComparison.Ordinal);
        Assert.Contains("move.horizontal", undeclared.Message, StringComparison.Ordinal);
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
