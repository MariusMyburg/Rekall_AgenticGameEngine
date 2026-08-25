using System.Diagnostics;
using System.Text.Json.Nodes;
using Rekall.Age.Project;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Cli;

public sealed class RuntimeInspectCliTests
{
    [Fact]
    public async Task RuntimeInspectPrintsSubsystemCounts()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Runtime CLI", ["world"]),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world"])
                .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true }))),
            CancellationToken.None);

        var result = await RunAsync(FindCliAssemblyPath(), "runtime", "inspect", root, "Main", "2");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Runtime Main frame 2", result.Output);
        Assert.Contains("Entities: 1", result.Output);
        Assert.Contains("Renderable: 1", result.Output);
    }

    [Fact]
    public async Task RuntimeSoakPrintsCheckpointsAndPassedChecks()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Runtime Soak CLI", ["world"]),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world"])
                .AddEntity(RekallAgeEntityDocument.Create("Actor", ["actor"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject()))),
            CancellationToken.None);

        var result = await RunAsync(
            FindCliAssemblyPath(),
            "runtime",
            "soak",
            root,
            "Main",
            "125",
            "50",
            "0",
            "-1",
            "0",
            "128",
            "1024");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Completed frames: 125", result.Output);
        Assert.Contains("Checkpoints: 3", result.Output);
        Assert.Contains("Check complete-execution: PASS", result.Output);
        Assert.Contains("Check frame-continuity: PASS", result.Output);
        Assert.Contains("Check elapsed-continuity: PASS", result.Output);
        Assert.Contains("Check stable-systems: PASS", result.Output);
    }

    [Fact]
    public async Task RuntimeInspectAcceptsRuntimeInputJson()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Runtime Input CLI", ["world"]),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world"])
                .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true })))
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

        var result = await RunAsync(
            FindCliAssemblyPath(),
            "runtime",
            "inspect",
            root,
            "Main",
            "1",
            """[{"pressedKeys":["W"],"pressedKeysThisFrame":["W"]}]""");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Input actions: 1", result.Output);
        Assert.Contains("thrust: value=1 down=True pressed=True released=False", result.Output);
    }

    [Fact]
    public async Task RuntimeInspectPrintsAnimationStateGraphFacts()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Runtime Graph CLI", ["world", "animation"]),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "animation"])
                .AddEntity(RekallAgeEntityDocument.Create("Actor", ["actor"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject()))
                    .AddComponent(RekallAgeComponentDocument.Create(
                        "Rekall.AnimationStateGraph",
                        new JsonObject
                        {
                            ["version"] = 1,
                            ["initialState"] = "idle",
                            ["parameters"] = new JsonObject { ["phase"] = 1 },
                            ["states"] = new JsonArray
                            {
                                new JsonObject { ["name"] = "idle", ["clip"] = "clip-idle" },
                                new JsonObject { ["name"] = "active", ["clip"] = "clip-active" }
                            },
                            ["transitions"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["from"] = "idle", ["to"] = "active", ["durationSeconds"] = 1,
                                    ["conditions"] = new JsonArray
                                    {
                                        new JsonObject { ["parameter"] = "phase", ["operator"] = "greater", ["value"] = 0 }
                                    }
                                }
                            }
                        }))),
            CancellationToken.None);

        var result = await RunAsync(FindCliAssemblyPath(), "runtime", "inspect", root, "Main", "1");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("kind=AnimationStateGraph", result.Output);
        Assert.Contains("state=active previous=idle transition=0.017", result.Output);
    }

    [Fact]
    public async Task RuntimeInspectPrintsBoundedMorphState()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Runtime Morph CLI", ["world", "animation", "rendering3d"]),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "animation", "rendering3d"])
                .AddEntity(RekallAgeEntityDocument.Create("Morph Actor", ["actor"])
                    .AddComponent(RekallAgeComponentDocument.Create(
                        "Rekall.MorphWeights",
                        new JsonObject { ["weights"] = new JsonArray(0.25, -0.5, 2) }))),
            CancellationToken.None);

        var result = await RunAsync(FindCliAssemblyPath(), "runtime", "inspect", root, "Main", "1");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Morph states: 1", result.Output);
        Assert.Contains("Morph Morph Actor: count=3 weights=[0.25,-0.5,2]", result.Output);
    }

    [Fact]
    public async Task RuntimeInspectPrintsCameraCulledRenderables()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Runtime Culling CLI", ["world", "rendering3d"]),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
                .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                    {
                        ["active"] = true,
                        ["cullingMask"] = "world"
                    })))
                .AddEntity(RekallAgeEntityDocument.Create("Visible Cube", ["prop"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.RenderLayer", new JsonObject { ["layer"] = "world" }))
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" })))
                .AddEntity(RekallAgeEntityDocument.Create("Hidden Cube", ["prop"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.RenderLayer", new JsonObject { ["layer"] = "helpers" }))
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" }))),
            CancellationToken.None);

        var result = await RunAsync(FindCliAssemblyPath(), "runtime", "inspect", root, "Main", "0");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Visible renderables: 1", result.Output);
        Assert.Contains("Culled renderables: 1", result.Output);
        Assert.Contains("Culled: Hidden Cube; layer: helpers; reason: camera-culling-mask", result.Output);
    }

    [Fact]
    public async Task RuntimeInspectPrintsInjectedXrPoseAndActions()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Runtime XR CLI", ["world", "rendering3d", "vr"]),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d", "vr"])
                .AddEntity(RekallAgeEntityDocument.Create("VrRig", ["xr"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.XrRig", new JsonObject { ["active"] = true })))
                .AddEntity(RekallAgeEntityDocument.Create("HeadCamera", ["camera"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject()))
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                    {
                        ["active"] = true,
                        ["stereoMode"] = "stereo"
                    }))
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.XrPoseSource", new JsonObject
                    {
                        ["source"] = "head"
                    })))
                .AddEntity(RekallAgeEntityDocument.Create("LeftHand", ["controller"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.XrController", new JsonObject
                    {
                        ["hand"] = "left"
                    }))),
            CancellationToken.None);

        var result = await RunAsync(
            FindCliAssemblyPath(),
            "runtime",
            "inspect",
            root,
            "Main",
            "1",
            """[{"xrPoses":[{"source":"head","isTracked":true,"x":1,"y":1.7,"z":-1}],"xrActions":[{"hand":"left","name":"trigger","value":0.8,"isDown":true,"wasPressed":true,"wasReleased":false}]}]""");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("XR: 1 rigs, 1 controllers, 1 poses, 1 actions", result.Output);
        Assert.Contains("left/trigger: value=0.8 down=True pressed=True released=False", result.Output);
    }

    [Fact]
    public async Task RunSceneAcceptsRuntimeInputJson()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Run Scene Input CLI", ["world"]),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world"])
                .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true })))
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

        var result = await RunAsync(
            FindCliAssemblyPath(),
            "run",
            "scene",
            root,
            "Main",
            "0.016",
            """[{"pressedKeys":["W"],"pressedKeysThisFrame":["W"]}]""");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Systems: runtime.input.actions", result.Output);
        Assert.Contains("Input actions: 1", result.Output);
        Assert.Contains("thrust: value=1 down=True pressed=True released=False", result.Output);
    }

    [Fact]
    public async Task RuntimeInspectAcceptsControllerInputJsonAndReportsPhysicalSource()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Controller Input CLI", ["world"]),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world"])
                .AddEntity(RekallAgeEntityDocument.Create("Input", ["input"])
                    .AddComponent(RekallAgeComponentDocument.Create(
                        "Rekall.InputActionMap",
                        new JsonObject
                        {
                            ["actions"] = new JsonArray
                            {
                                new JsonObject { ["name"] = "steer", ["controllerAxis"] = "LeftX" }
                            }
                        }))),
            CancellationToken.None);

        var result = await RunAsync(
            FindCliAssemblyPath(),
            "runtime",
            "inspect",
            root,
            "Main",
            "1",
            """[{"controllers":[{"deviceId":"pad-alpha","kind":"gamepad","playerIndex":0,"axes":[{"name":"LeftX","value":0.75}],"pressedButtons":[],"pressedButtonsThisFrame":[],"releasedButtonsThisFrame":[],"hats":[]}]}]""");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("steer: value=0.75 down=True", result.Output);
        Assert.Contains("device=pad-alpha kind=gamepad", result.Output);
    }

    [Fact]
    public async Task InputCliInspectsAndRebindsSemanticActions()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world"])
                .AddEntity((RekallAgeEntityDocument.Create("Input", ["input"]) with { Id = "input" })
                    .AddComponent(RekallAgeComponentDocument.Create(
                        "Rekall.InputActionMap",
                        new JsonObject
                        {
                            ["actions"] = new JsonArray
                            {
                                new JsonObject { ["name"] = "primary", ["key"] = "Space" }
                            }
                        }))),
            CancellationToken.None);

        var inspect = await RunAsync(FindCliAssemblyPath(), "input", "inspect", root, "Main");
        Assert.Equal(0, inspect.ExitCode);
        Assert.Contains("primary", inspect.Output);

        var rebind = await RunAsync(
            FindCliAssemblyPath(), "input", "rebind", root, "Main", "input", "primary",
            """{"key":"Enter","controllerButton":"A"}""");
        Assert.Equal(0, rebind.ExitCode);
        Assert.Contains("Rebound input action 'primary'", rebind.Output);
        var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
        var action = Assert.IsType<JsonObject>(Assert.Single(scene.GetRequiredEntity("input").Components.Single().Properties["actions"]!.AsArray()));
        Assert.Equal("A", action["controllerButton"]!.GetValue<string>());
    }

    [Fact]
    public async Task RuntimeViewportCapturePrintsCaptureSummary()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Runtime Viewport CLI", ["world", "rendering2d"]),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"])
                .AddEntity(RekallAgeEntityDocument.Create("MainCamera", ["camera"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true })))
                .AddEntity(RekallAgeEntityDocument.Create("Player", ["player"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform2D", new JsonObject { ["x"] = 16, ["y"] = 24 }))
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.SpriteRenderer", new JsonObject { ["sprite"] = "asset_player" }))),
            CancellationToken.None);
        var outputDirectory = Path.Combine(root, "ViewportCaptures");

        var result = await RunAsync(FindCliAssemblyPath(), "render", "viewport", "capture", root, "Main", "3", outputDirectory);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Runtime viewport Main frame 3", result.Output);
        Assert.Contains("Backend: software", result.Output);
        Assert.Contains("Hardware accelerated: False", result.Output);
        Assert.Contains("Acceleration: software-rasterized", result.Output);
        Assert.Contains("Active camera: MainCamera", result.Output);
        Assert.Contains("Frame analysis: informative=", result.Output);
        Assert.Contains("Dominant color:", result.Output);
        Assert.Contains("Renderable: 1", result.Output);
        Assert.Contains("Asset-backed: 0", result.Output);
        Assert.Contains("Fallback: 1", result.Output);
        Assert.Contains("Main_runtime_003.png", result.Output);
        Assert.True(File.Exists(Path.Combine(outputDirectory, "Main_runtime_003.png")));
    }

    [Fact]
    public async Task RuntimeViewportCapturePrintsResolvedQualityOverrideAndTruthfulUnavailableGpuTiming()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Runtime Quality CLI", ["world", "rendering3d"]),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
                .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true })))
                .AddEntity(RekallAgeEntityDocument.Create("Cube", ["prop"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject { ["z"] = 3 }))
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" }))),
            CancellationToken.None);
        var outputDirectory = Path.Combine(root, "QualityCapture");

        var result = await RunAsync(
            FindCliAssemblyPath(),
            "render", "viewport", "capture",
            root, "Main", "0", outputDirectory, "160", "90", "software", "[]",
            "Medium", "{\"resolutionScale\":0.5}", "true");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Requested quality: Medium", result.Output);
        Assert.Contains("Resolved quality: Medium", result.Output);
        Assert.Contains("Internal resolution: 80x45", result.Output);
        Assert.Contains("GPU timings: REKALL_GPU_TIMESTAMPS_UNAVAILABLE", result.Output);
        Assert.Contains("Degradation: REKALL_RENDER_FEATURE_DEVICE_CLAMPED", result.Output);
        Assert.Contains("Requested=true; resolved=false", result.Output);
    }

    [Fact]
    public async Task QualityCompareCliUsesSharedOperationAndPrintsAlignedCaptureFacts()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Compare Quality CLI", ["world", "rendering2d"]),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"])
                .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true })))
                .AddEntity(RekallAgeEntityDocument.Create("Sprite", ["prop"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform2D", new JsonObject { ["x"] = 20, ["y"] = 20 }))
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.SpriteRenderer", new JsonObject { ["sprite"] = "missing" }))),
            CancellationToken.None);

        var result = await RunAsync(
            FindCliAssemblyPath(),
            "render", "quality", "compare",
            root, "Main", "2", Path.Combine(root, "Compare"), "128", "72", "software",
            "Performance,High", "{}", "false");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Compared quality presets for Main at frame 2", result.Output);
        Assert.Contains("Performance -> Performance", result.Output);
        Assert.Contains("High -> High", result.Output);
        Assert.Contains("Internal: 64x36", result.Output);
        Assert.Contains("Next: command execute rekall.render.capture_runtime_viewport", result.Output);
    }

    [Fact]
    public async Task PerformanceBudgetCliAcceptsQualityOverridesAndPrintsTruthfulGpuTimingState()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Performance Quality CLI", ["world", "rendering3d"]),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
                .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true })))
                .AddEntity(RekallAgeEntityDocument.Create("Cube", ["prop"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject { ["z"] = 3 }))
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" }))),
            CancellationToken.None);

        var result = await RunAsync(
            FindCliAssemblyPath(),
            "render", "performance", "budget",
            root, "Main", "desktop60", "1", "160", "90",
            "Medium", "{\"resolutionScale\":0.5}", "false");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Requested quality: Medium", result.Output);
        Assert.Contains("Resolved quality: Medium", result.Output);
        Assert.Contains("Internal resolution: 80x45", result.Output);
        Assert.Contains("Resource bytes:", result.Output);
        Assert.Contains("GPU timings: REKALL_GPU_TIMESTAMPS_UNAVAILABLE", result.Output);
    }

    [Fact]
    public async Task ContextScenePrintsCameraMasksAndRenderLayers()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Context Scene CLI", ["world", "rendering3d"]),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
                .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                    {
                        ["active"] = true,
                        ["cullingMask"] = "world"
                    })))
                .AddEntity(RekallAgeEntityDocument.Create("World Cube", ["prop"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.RenderLayer", new JsonObject { ["layer"] = "world" }))
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" }))),
            CancellationToken.None);

        var result = await RunAsync(FindCliAssemblyPath(), "context", "scene", root, "Main");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Camera: Camera; kind: Camera3D; active: True; order: 0; viewport: 0,0 1x1; culling mask: world", result.Output);
        Assert.Contains("Render layer: world; renderables: 1; entities: World Cube", result.Output);
    }

    [Fact]
    public async Task ContextScenePrintsHeadsetCameraMetadata()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Context Scene VR CLI", ["world", "rendering3d", "vr"]),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d", "vr"])
                .AddEntity(RekallAgeEntityDocument.Create("SpectatorCamera", ["camera"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                    {
                        ["active"] = true,
                        ["renderOrder"] = -10
                    })))
                .AddEntity(RekallAgeEntityDocument.Create("HeadCamera", ["camera"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                    {
                        ["active"] = true,
                        ["renderOrder"] = 0,
                        ["stereoMode"] = "vr",
                        ["stereoRenderMode"] = "single-pass-multiview"
                    }))),
            CancellationToken.None);

        var result = await RunAsync(FindCliAssemblyPath(), "context", "scene", root, "Main");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Headset camera: HeadCamera", result.Output);
        Assert.Contains("Camera: SpectatorCamera; kind: Camera3D; active: True; order: -10; viewport: 0,0 1x1; culling mask: *; stereo: mono; headset: False", result.Output);
        Assert.Contains("Camera: HeadCamera; kind: Camera3D; active: True; order: 0; viewport: 0,0 1x1; culling mask: *; stereo: stereo; headset: True", result.Output);
    }

    [Fact]
    public async Task RenderVisibilityInspectPrintsPerCameraVisibility()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Render Visibility CLI", ["world", "rendering3d"]),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
                .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                    {
                        ["active"] = true,
                        ["cullingMask"] = "world"
                    })))
                .AddEntity(RekallAgeEntityDocument.Create("World Cube", ["prop"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.RenderLayer", new JsonObject { ["layer"] = "world" }))
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" })))
                .AddEntity(RekallAgeEntityDocument.Create("Hidden Helper", ["debug"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.RenderLayer", new JsonObject { ["layer"] = "helpers" }))
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "sphere" }))),
            CancellationToken.None);

        var result = await RunAsync(FindCliAssemblyPath(), "render", "visibility", "inspect", root, "Main");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("visibility: 2 renderables across 1 camera", result.Output);
        Assert.Contains("Camera: Camera; active: True; order: 0; viewport: 0,0 1x1; culling mask: world; visible: 1; culled: 1", result.Output);
        Assert.Contains("Visible: World Cube; kind: mesh; layer: world", result.Output);
        Assert.Contains("Culled: Hidden Helper; kind: mesh; layer: helpers; reason: camera-culling-mask", result.Output);
        Assert.Contains("Unseen by active camera: Hidden Helper; kind: mesh; layer: helpers", result.Output);
        Assert.Contains("Unseen by any camera: Hidden Helper; kind: mesh; layer: helpers", result.Output);
    }

    [Fact]
    public async Task RenderVirtualGeometryApplyAddsComponentToExistingDenseScene()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d", "planet"])
                .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true })))
                .AddEntity(RekallAgeEntityDocument.Create("Existing Planet", ["planet"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.PlanetRenderer", new JsonObject
                    {
                        ["radius"] = 6,
                        ["meshSlices"] = 192,
                        ["meshStacks"] = 96
                    }))),
            CancellationToken.None);

        var result = await RunAsync(
            FindCliAssemblyPath(),
            "render",
            "virtual-geometry",
            "apply",
            root,
            "Main",
            "10000");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Applied virtual geometry to 1", result.Output);
        Assert.Contains("Applied: Existing Planet", result.Output);
        var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
        var planet = scene.Entities.Single(entity => entity.Name == "Existing Planet");
        Assert.Contains(planet.Components, component => component.Type == "Rekall.VirtualGeometry");
    }

    [Fact]
    public async Task RenderVirtualGeometryApplyDryRunDoesNotModifyScene()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d", "planet"])
                .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true })))
                .AddEntity(RekallAgeEntityDocument.Create("Existing Planet", ["planet"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.PlanetRenderer", new JsonObject
                    {
                        ["radius"] = 6,
                        ["meshSlices"] = 192,
                        ["meshStacks"] = 96
                    }))),
            CancellationToken.None);

        var result = await RunAsync(
            FindCliAssemblyPath(),
            "render",
            "virtual-geometry",
            "apply",
            root,
            "Main",
            "10000",
            "--dry-run");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Dry run: True", result.Output);
        Assert.Contains("Applied: Existing Planet", result.Output);
        var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
        var planet = scene.Entities.Single(entity => entity.Name == "Existing Planet");
        Assert.DoesNotContain(planet.Components, component => component.Type == "Rekall.VirtualGeometry");
    }

    [Fact]
    public async Task RenderVirtualGeometryApplyEntityTargetsOneDenseEntity()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d", "planet"])
                .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true })))
                .AddEntity(RekallAgeEntityDocument.Create("Earth", ["planet"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.PlanetRenderer", new JsonObject
                    {
                        ["radius"] = 6,
                        ["meshSlices"] = 192,
                        ["meshStacks"] = 96
                    })))
                .AddEntity(RekallAgeEntityDocument.Create("Jupiter", ["planet"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.PlanetRenderer", new JsonObject
                    {
                        ["radius"] = 8,
                        ["meshSlices"] = 192,
                        ["meshStacks"] = 96
                    }))),
            CancellationToken.None);

        var result = await RunAsync(
            FindCliAssemblyPath(),
            "render",
            "virtual-geometry",
            "apply-entity",
            root,
            "Main",
            "Earth",
            "30000");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Applied: Earth", result.Output);
        Assert.DoesNotContain("Applied: Jupiter", result.Output);
        var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
        Assert.Contains(scene.Entities.Single(entity => entity.Name == "Earth").Components, component => component.Type == "Rekall.VirtualGeometry");
        Assert.DoesNotContain(scene.Entities.Single(entity => entity.Name == "Jupiter").Components, component => component.Type == "Rekall.VirtualGeometry");
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string cliAssembly, params string[] args)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        startInfo.ArgumentList.Add(cliAssembly);
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask + await errorTask;
        return (process.ExitCode, output);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rekall.AGE.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find Rekall.AGE.sln from the test output directory.");
    }

    private static string FindCliAssemblyPath()
    {
        var cliAssembly = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Rekall.Age.Cli",
            "bin",
            "Debug",
            "net10.0",
            "Rekall.Age.Cli.dll");
        if (File.Exists(cliAssembly))
        {
            return cliAssembly;
        }

        throw new InvalidOperationException($"Could not find built CLI assembly at '{cliAssembly}'.");
    }
}
