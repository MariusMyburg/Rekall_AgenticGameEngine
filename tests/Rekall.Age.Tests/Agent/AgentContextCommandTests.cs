using Rekall.Age.Agent.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.GameTemplates.Commands;
using Rekall.Age.Mcp;

namespace Rekall.Age.Tests.Agent;

public sealed class AgentContextCommandTests
{
    [Fact]
    public async Task ProjectAndSceneSummaryCommandsReturnCompactInspectableContext()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("context"), CancellationToken.None);
        await new CreateGameFromTemplateCommand()
            .ExecuteAsync(new CreateGameFromTemplateRequest(root, "Context Puzzle", "puzzle"), context);

        var project = await new GetProjectSummaryCommand()
            .ExecuteAsync(new GetProjectSummaryRequest(root), context);
        var scene = await new GetSceneSummaryCommand()
            .ExecuteAsync(new GetSceneSummaryRequest(root, "Main"), context);

        Assert.True(project.Ok);
        Assert.Equal("ok", project.Value.Summary.Health.Status);
        Assert.Equal("puzzle", project.Value.Summary.SourceTemplateId);
        Assert.True(scene.Ok);
        Assert.Equal("Main", scene.Value.Summary.Scene);
        Assert.Contains(scene.Value.Summary.Entities, entity => entity.Name == "PuzzleGrid");
        Assert.Contains(scene.Value.Summary.ComponentTypes, component => component.EndsWith(".GridBoard", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SceneSummaryReportsAuthoredRenderLayersAndCameraMasks()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = Rekall.Age.World.RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(Rekall.Age.World.RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(Rekall.Age.World.RekallAgeComponentDocument.Create("Rekall.Camera3D", new System.Text.Json.Nodes.JsonObject
                {
                    ["active"] = true,
                    ["renderOrder"] = 10,
                    ["viewportX"] = 0.5,
                    ["viewportWidth"] = 0.5,
                    ["cullingMask"] = "world, helpers"
                })))
            .AddEntity(Rekall.Age.World.RekallAgeEntityDocument.Create("World Cube", ["prop"])
                .AddComponent(Rekall.Age.World.RekallAgeComponentDocument.Create("Rekall.RenderLayer", new System.Text.Json.Nodes.JsonObject { ["layer"] = "world" }))
                .AddComponent(Rekall.Age.World.RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new System.Text.Json.Nodes.JsonObject { ["primitive"] = "cube" })))
            .AddEntity(Rekall.Age.World.RekallAgeEntityDocument.Create("Helper Cube", ["debug"])
                .AddComponent(Rekall.Age.World.RekallAgeComponentDocument.Create("Rekall.RenderLayer", new System.Text.Json.Nodes.JsonObject { ["layer"] = "helpers" }))
                .AddComponent(Rekall.Age.World.RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new System.Text.Json.Nodes.JsonObject { ["primitive"] = "cube" })));
        await new Rekall.Age.World.RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);

        var result = await new GetSceneSummaryCommand().ExecuteAsync(
            new GetSceneSummaryRequest(root, "Main"),
            new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("scene summary"), CancellationToken.None));

        Assert.True(result.Ok, result.Summary);
        Assert.Contains(result.Value.Summary.Cameras, camera =>
            camera.EntityName == "Camera"
            && camera.Kind == "Camera3D"
            && camera.Active
            && camera.RenderOrder == 10
            && camera.ViewportX == 0.5
            && camera.ViewportWidth == 0.5
            && camera.CullingMask == "world, helpers");
        Assert.Contains(result.Value.Summary.RenderLayers, layer => layer.Layer == "world" && layer.RenderableCount == 1);
        Assert.Contains(result.Value.Summary.RenderLayers, layer => layer.Layer == "helpers" && layer.RenderableCount == 1);
    }

    [Fact]
    public async Task SceneSummaryIdentifiesHeadsetCameraSeparatelyFromViewportCamera()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = Rekall.Age.World.RekallAgeSceneDocument.Create("Main", ["world", "rendering3d", "vr"])
            .AddEntity(Rekall.Age.World.RekallAgeEntityDocument.Create("SpectatorCamera", ["camera"])
                .AddComponent(Rekall.Age.World.RekallAgeComponentDocument.Create("Rekall.Camera3D", new System.Text.Json.Nodes.JsonObject
                {
                    ["active"] = true,
                    ["renderOrder"] = -10
                })))
            .AddEntity(Rekall.Age.World.RekallAgeEntityDocument.Create("HeadCamera", ["camera"])
                .AddComponent(Rekall.Age.World.RekallAgeComponentDocument.Create("Rekall.Camera3D", new System.Text.Json.Nodes.JsonObject
                {
                    ["active"] = true,
                    ["renderOrder"] = 0,
                    ["stereoMode"] = "xr",
                    ["stereoRenderMode"] = "single-pass-multiview",
                    ["xrViewConfiguration"] = "primary-stereo"
                })));
        await new Rekall.Age.World.RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);

        var result = await new GetSceneSummaryCommand().ExecuteAsync(
            new GetSceneSummaryRequest(root, "Main"),
            new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("vr scene summary"), CancellationToken.None));

        Assert.True(result.Ok, result.Summary);
        Assert.Equal("HeadCamera", result.Value.Summary.HeadsetCameraName);
        Assert.Contains(result.Value.Summary.Cameras, camera =>
            camera.EntityName == "SpectatorCamera"
            && camera.Active
            && !camera.DrivesHeadsetOutput
            && camera.StereoMode == "mono");
        Assert.Contains(result.Value.Summary.Cameras, camera =>
            camera.EntityName == "HeadCamera"
            && camera.Active
            && camera.DrivesHeadsetOutput
            && camera.StereoMode == "stereo"
            && camera.StereoRenderMode == "single-pass-multiview"
            && camera.XrViewConfiguration == "primary-stereo");
    }

    [Fact]
    public void ContextCommandsAreVisibleToMcpCatalog()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new GetEngineStatusCommand());
        registry.Register(new GetProjectSummaryCommand());
        registry.Register(new GetSceneSummaryCommand());
        registry.Register(new ListGameTemplatesCommand());

        var catalog = RekallAgeMcpCatalog.FromRegistry(registry);

        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.context.engine_status");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.context.project_summary");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.context.scene_summary");
        Assert.Contains(catalog.Tools, tool => tool.Name == "rekall.templates.list");
    }

    [Fact]
    public async Task EngineStatusReturnsAgentFirstMvpWorkflowMap()
    {
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("engine status"), CancellationToken.None);

        var result = await new GetEngineStatusCommand().ExecuteAsync(new GetEngineStatusRequest(), context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal("Rekall AGE", result.Value.EngineName);
        Assert.True(result.Value.AgentFirst);
        Assert.Contains("pong", result.Value.MvpTemplateIds);
        Assert.Contains("first-person-exploration", result.Value.MvpTemplateIds);
        Assert.Contains(result.Value.WorkflowTools, workflow => workflow.Tool == "rekall.templates.inspect" && workflow.Recommended);
        Assert.Contains(result.Value.WorkflowTools, workflow => workflow.Tool == "rekall.geometry.create_primitive");
        Assert.Contains(result.Value.WorkflowTools, workflow => workflow.Tool == "rekall.scene.apply_blueprint" && workflow.Recommended);
        Assert.Contains(result.Value.WorkflowTools, workflow => workflow.Tool == "rekall.validation.scene" && workflow.Recommended);
        Assert.Contains(result.Value.WorkflowTools, workflow => workflow.Tool == "rekall.entity.delete");
        Assert.Contains(result.Value.WorkflowTools, workflow => workflow.Tool == "rekall.module.scaffold_runtime_system" && workflow.Recommended);
        Assert.Contains(result.Value.WorkflowTools, workflow => workflow.Tool == "rekall.module.list_sources");
        Assert.Contains(result.Value.WorkflowTools, workflow => workflow.Tool == "rekall.module.read_source");
        Assert.Contains(result.Value.WorkflowTools, workflow => workflow.Tool == "rekall.build.modules" && workflow.Recommended);
        Assert.Contains(result.Value.WorkflowTools, workflow => workflow.Tool == "rekall.shader.assign_pipeline");
        Assert.Contains(result.Value.WorkflowTools, workflow => workflow.Tool == "rekall.render.performance.inspect_scene_budget");
        Assert.Contains(result.Value.WorkflowTools, workflow => workflow.Tool == "rekall.render.visibility.inspect_scene");
        Assert.Contains(result.Value.WorkflowTools, workflow => workflow.Tool == "rekall.render.openxr.bootstrap_session");
        Assert.Contains(result.Value.WorkflowTools, workflow => workflow.Tool == "rekall.render.openxr.inspect_headset_frame_plan");
        Assert.Contains(result.Value.WorkflowTools, workflow => workflow.Tool == "rekall.workflow.create_playable_package_from_template" && workflow.Recommended);
        Assert.Contains(result.Value.WorkflowTools, workflow => workflow.Tool == "rekall.templates.verify_mvp");
        Assert.Contains(result.Value.AuthoringContracts, contract =>
            contract.Name == "runtime-module-system"
            && contract.PrimaryType == "IRekallAgeRuntimeModuleSystem"
            && contract.Capabilities.Contains("own-game-rules"));
        Assert.Contains(result.Value.AuthoringContracts, contract =>
            contract.Name == "runtime-module-sdk"
            && contract.PrimaryType == "RekallAgeRuntimeModuleSdk"
            && contract.Capabilities.Contains("raycast3d")
            && contract.Capabilities.Contains("write-components"));
        Assert.Contains(result.Value.AuthoringContracts, contract =>
            contract.Name == "runtime-render-mesh"
            && contract.PrimaryType == "RekallAgeRuntimeRenderMesh"
            && contract.Capabilities.Contains("custom-kind")
            && contract.Capabilities.Contains("custom-variant")
            && contract.Capabilities.Contains("shader-pipeline"));
        Assert.Contains(result.Value.AuthoringContracts, contract =>
            contract.Name == "runtime-lod-selection"
            && contract.PrimaryType == "Rekall.LodGroup"
            && contract.Capabilities.Contains("distance-levels"));
        Assert.Contains(result.Value.AuthoringContracts, contract =>
            contract.Name == "runtime-render-layers"
            && contract.PrimaryType == "Rekall.RenderLayer"
            && contract.Capabilities.Contains("camera-culling-mask")
            && contract.Capabilities.Contains("mask-exclusions")
            && contract.Capabilities.Contains("per-camera-visibility")
            && contract.Capabilities.Contains("culling-diagnostics"));
        Assert.Contains(result.Value.AuthoringContracts, contract =>
            contract.Name == "xr-camera-contract"
            && contract.PrimaryType == "Rekall.Camera3D"
            && contract.Capabilities.Contains("render-order")
            && contract.Capabilities.Contains("normalized-viewport")
            && contract.Capabilities.Contains("headset-camera-selection")
            && contract.Capabilities.Contains("single-pass-multiview"));
        Assert.Contains(result.Value.AuthoringContracts, contract =>
            contract.Name == "xr-runtime-input"
            && contract.PrimaryType == "Rekall.XrPoseSource"
            && contract.Capabilities.Contains("headset-pose")
            && contract.Capabilities.Contains("controller-actions"));
        Assert.Contains("vulkan", result.Value.RenderingPosture, StringComparison.OrdinalIgnoreCase);
    }
}
