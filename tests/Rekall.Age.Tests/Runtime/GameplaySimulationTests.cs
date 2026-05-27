using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.GameTemplates.Commands;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Commands;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.Validation;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Runtime;

public sealed class GameplaySimulationTests
{
    public static TheoryData<string> Templates => new()
    {
        { "pong" },
        { "breakout" },
        { "asteroids" },
        { "top-down-shooter" },
        { "platformer-2d" },
        { "tower-defense" },
        { "visual-novel" },
        { "first-person-exploration" },
        { "collectathon-3d" },
        { "puzzle" }
    };

    [Theory]
    [MemberData(nameof(Templates))]
    public async Task RunSceneReportsOnlyCoreRuntimeSystemsForTemplates(string templateId)
    {
        var root = TestPaths.CreateTempDirectory();
        var createGame = new CreateGameFromTemplateCommand();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("simulation"), CancellationToken.None);
        await createGame.ExecuteAsync(new CreateGameFromTemplateRequest(root, $"Game {templateId}", templateId), context);
        var runScene = new RunSceneCommand();

        var result = await runScene.ExecuteAsync(
            new RunSceneRequest(root, "Main", 0.1),
            context);

        Assert.True(result.Ok);
        Assert.True(result.Value.FramesSimulated >= 6);
        Assert.Contains(result.Value.Observations, observation => observation.System is "Camera2D" or "Camera3D");
        Assert.DoesNotContain(result.Value.Observations, observation =>
            observation.System is "PaddleController" or "BrickGrid" or "GridBoard" or "FirstPersonController" or "ThirdPersonController");
    }

    [Fact]
    public async Task RuntimeRefusesBlockedSceneWhenValidatorIsEnabled()
    {
        var root = TestPaths.CreateTempDirectory();
        var sceneStore = new RekallAgeSceneStore();
        await sceneStore.SaveAsync(root, RekallAgeSceneDocument.Create("Main", ["world"]), CancellationToken.None);
        var runtime = new RekallAgeHeadlessRuntime(sceneStore, new RekallAgeProjectValidator(sceneStore));

        var result = await runtime.RunAsync(root, "Main", TimeSpan.FromMilliseconds(16), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Contains("active camera", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(result.Observations);
    }

    [Fact]
    public async Task RunSceneUsesCanonicalRuntimeAndInjectedInputActions()
    {
        var root = TestPaths.CreateTempDirectory();
        var sceneStore = new RekallAgeSceneStore();
        await sceneStore.SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world"])
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
                                new JsonObject { ["name"] = "thrust", ["key"] = "W" }
                            }
                        }))),
            CancellationToken.None);
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("run-scene-input"), CancellationToken.None);

        var result = await new RunSceneCommand().ExecuteAsync(
            new RunSceneRequest(
                root,
                "Main",
                1.0 / 60.0,
                [
                    new RekallAgeRuntimeInputFrame(
                        PressedKeys: ["W"],
                        PressedKeysThisFrame: ["W"])
                ]),
            context);

        Assert.True(result.Ok);
        Assert.Contains("runtime.input.actions", result.Value.ActiveSystems);
        var thrust = Assert.Single(result.Value.InputActions, action => action.Name == "thrust");
        Assert.True(thrust.IsDown);
        Assert.True(thrust.WasPressed);
        Assert.Equal(1, thrust.Value);
    }

    [Fact]
    public async Task CameraTargetCycleInputRepointsGenericCameraTargetFromSemanticActions()
    {
        var camera = new RekallAgeRuntimeEntity(
            "camera",
            "Tour Camera",
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
                        ["projectionMode"] = "perspective",
                        ["fieldOfView"] = 52,
                        ["orthographicSize"] = 100,
                        ["cullingMask"] = "*"
                    }),
                new RekallAgeRuntimeComponent(
                    "Rekall.CameraTarget3D",
                    new JsonObject { ["targetName"] = "Sol", ["offsetZ"] = 100, ["lookAt"] = true }),
                new RekallAgeRuntimeComponent(
                    "Rekall.CameraTargetCycleInput",
                    new JsonObject
                    {
                        ["nextAction"] = "nextTarget",
                        ["previousAction"] = "previousTarget",
                        ["currentIndex"] = 0,
                        ["targets"] = new JsonArray
                        {
                            new JsonObject { ["targetName"] = "Sol", ["offsetZ"] = 100, ["fieldOfView"] = 52 },
                            new JsonObject
                            {
                                ["targetName"] = "Earth",
                                ["offsetReferenceName"] = "Sol",
                                ["offsetReferenceMode"] = "toward",
                                ["offsetDistance"] = 4.5,
                                ["offsetVertical"] = 0.5,
                                ["projectionMode"] = "orthographic",
                                ["orthographicSize"] = 25,
                                ["fieldOfView"] = 36,
                                ["cullingMask"] = "default"
                            }
                        }
                    })
            ]);
        var world = new RekallAgeRuntimeWorld(
            "scene",
            "Main",
            0,
            TimeSpan.Zero,
            [camera],
            RekallAgeRuntimeSubsystemViews.Empty with
            {
                Input = new RekallAgeRuntimeInputView(
                [
                    new RekallAgeRuntimeInputAction("nextTarget", 1, true, true, false, "input", "Input")
                ])
            },
            []);

        var updated = await new RekallAgeCameraTargetCycleInputSystem()
            .UpdateAsync(world, new RekallAgeRuntimeWorldFrameContext(1, TimeSpan.FromSeconds(1.0 / 60.0), TimeSpan.FromSeconds(1.0 / 60.0), CancellationToken.None));

        var updatedCamera = Assert.Single(updated.Entities);
        var target = updatedCamera.Components.Single(component => component.Type == "Rekall.CameraTarget3D");
        Assert.Equal("Earth", target.Properties["targetName"]!.GetValue<string>());
        Assert.Equal("Sol", target.Properties["offsetReferenceName"]!.GetValue<string>());
        Assert.Equal("toward", target.Properties["offsetReferenceMode"]!.GetValue<string>());
        Assert.Equal(4.5, target.Properties["offsetDistance"]!.GetValue<double>(), precision: 6);
        Assert.Equal(0.5, target.Properties["offsetVertical"]!.GetValue<double>(), precision: 6);
        var cycle = updatedCamera.Components.Single(component => component.Type == "Rekall.CameraTargetCycleInput");
        Assert.Equal(1, cycle.Properties["currentIndex"]!.GetValue<double>(), precision: 6);
        var cameraComponent = updatedCamera.Components.Single(component => component.Type == "Rekall.Camera3D");
        Assert.Equal("orthographic", cameraComponent.Properties["projectionMode"]!.GetValue<string>());
        Assert.Equal(25, cameraComponent.Properties["orthographicSize"]!.GetValue<double>(), precision: 6);
        Assert.Equal(36, cameraComponent.Properties["fieldOfView"]!.GetValue<double>(), precision: 6);
        Assert.Equal("default", cameraComponent.Properties["cullingMask"]!.GetValue<string>());
    }

    [Fact]
    public async Task CameraTarget3DCanPlaceCameraRelativeToAReferenceEntity()
    {
        var sol = new RekallAgeRuntimeEntity(
            "sol",
            "Sol",
            [],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity,
            []);
        var earth = new RekallAgeRuntimeEntity(
            "earth",
            "Earth",
            [],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity with
            {
                Position3D = new RekallAgeRuntimeVector3(100, 0, 0)
            },
            []);
        var camera = new RekallAgeRuntimeEntity(
            "camera",
            "Camera",
            ["camera"],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity,
            [
                new RekallAgeRuntimeComponent(
                    "Rekall.CameraTarget3D",
                    new JsonObject
                    {
                        ["targetName"] = "Earth",
                        ["offsetReferenceName"] = "Sol",
                        ["offsetReferenceMode"] = "toward",
                        ["offsetDistance"] = 10,
                        ["offsetVertical"] = 2,
                        ["lookAt"] = true
                    })
            ]);
        var world = new RekallAgeRuntimeWorld(
            "scene",
            "Main",
            0,
            TimeSpan.Zero,
            [sol, earth, camera],
            RekallAgeRuntimeSubsystemViews.Empty,
            []);

        var updated = await new RekallAgeCameraTarget3DSystem()
            .UpdateAsync(world, new RekallAgeRuntimeWorldFrameContext(1, TimeSpan.FromSeconds(1.0 / 60.0), TimeSpan.FromSeconds(1.0 / 60.0), CancellationToken.None));

        var updatedCamera = updated.Entities.Single(entity => entity.Name == "Camera");
        Assert.Equal(90, updatedCamera.Transform.Position3D.X, precision: 6);
        Assert.Equal(2, updatedCamera.Transform.Position3D.Y, precision: 6);
        Assert.Equal(0, updatedCamera.Transform.Position3D.Z, precision: 6);
        Assert.True(updatedCamera.Transform.Rotation3D.Y > 80);
    }
}
