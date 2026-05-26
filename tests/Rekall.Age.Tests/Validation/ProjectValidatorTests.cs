using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Validation;
using Rekall.Age.Validation.Commands;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Validation;

public sealed class ProjectValidatorTests
{
    [Fact]
    public async Task ValidateSceneReportsMultipleActiveCameras()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera A", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Camera B", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true })));
        var sceneStore = new RekallAgeSceneStore();
        await sceneStore.SaveAsync(root, scene, CancellationToken.None);

        var report = await new RekallAgeProjectValidator(sceneStore)
            .ValidateSceneAsync(root, "Main", CancellationToken.None);

        var issue = Assert.Single(report.Issues, item => item.Code == "REKALL_CAMERA_MULTIPLE_ACTIVE");
        Assert.Equal("warning", issue.Severity);
        Assert.Equal("Main", issue.Target);
    }

    [Fact]
    public async Task ValidateSceneReportsCameraCullingMaskWithNoMatchingRenderableLayer()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["cullingMask"] = "world, helpers"
                })))
            .AddEntity(RekallAgeEntityDocument.Create("World Cube", ["prop"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.RenderLayer", new JsonObject { ["layer"] = "world" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" })));
        var sceneStore = new RekallAgeSceneStore();
        await sceneStore.SaveAsync(root, scene, CancellationToken.None);

        var report = await new RekallAgeProjectValidator(sceneStore)
            .ValidateSceneAsync(root, "Main", CancellationToken.None);

        var issue = Assert.Single(report.Issues, item => item.Code == "REKALL_CAMERA_CULLING_MASK_EMPTY_LAYER");
        Assert.Equal("warning", issue.Severity);
        Assert.Equal("Camera", issue.Target);
        Assert.Contains("helpers", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateSceneReportsRenderableLayerExcludedFromEveryActiveCamera()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["cullingMask"] = "world"
                })))
            .AddEntity(RekallAgeEntityDocument.Create("World Cube", ["prop"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.RenderLayer", new JsonObject { ["layer"] = "world" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" })))
            .AddEntity(RekallAgeEntityDocument.Create("Helper Cube", ["debug"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.RenderLayer", new JsonObject { ["layer"] = "helpers" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" })));
        var sceneStore = new RekallAgeSceneStore();
        await sceneStore.SaveAsync(root, scene, CancellationToken.None);

        var report = await new RekallAgeProjectValidator(sceneStore)
            .ValidateSceneAsync(root, "Main", CancellationToken.None);

        var issue = Assert.Single(report.Issues, item => item.Code == "REKALL_RENDER_LAYER_NOT_VISIBLE");
        Assert.Equal("warning", issue.Severity);
        Assert.Equal("helpers", issue.Target);
        Assert.Contains("Helper Cube", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateSceneAcceptsRenderableLayersWhenActiveCameraUsesWildcardMask()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["cullingMask"] = "*"
                })))
            .AddEntity(RekallAgeEntityDocument.Create("Helper Cube", ["debug"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.RenderLayer", new JsonObject { ["layer"] = "helpers" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" })));
        var sceneStore = new RekallAgeSceneStore();
        await sceneStore.SaveAsync(root, scene, CancellationToken.None);

        var report = await new RekallAgeProjectValidator(sceneStore)
            .ValidateSceneAsync(root, "Main", CancellationToken.None);

        Assert.DoesNotContain(report.Issues, item => item.Code == "REKALL_RENDER_LAYER_NOT_VISIBLE");
    }

    [Fact]
    public async Task ValidateSceneTreatsExcludedCameraMaskLayerAsNotVisible()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["cullingMask"] = "*, !helpers"
                })))
            .AddEntity(RekallAgeEntityDocument.Create("Helper Cube", ["debug"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.RenderLayer", new JsonObject { ["layer"] = "helpers" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" })));
        var sceneStore = new RekallAgeSceneStore();
        await sceneStore.SaveAsync(root, scene, CancellationToken.None);

        var report = await new RekallAgeProjectValidator(sceneStore)
            .ValidateSceneAsync(root, "Main", CancellationToken.None);

        Assert.Contains(report.Issues, item =>
            item.Code == "REKALL_RENDER_LAYER_NOT_VISIBLE"
            && item.Target == "helpers");
        Assert.DoesNotContain(report.Issues, item =>
            item.Code == "REKALL_CAMERA_CULLING_MASK_EMPTY_LAYER"
            && item.Message.Contains("helpers", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateVrSceneReportsMissingRigAndTrackedCamera()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d", "vr"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["stereoMode"] = "mono"
                })));
        var sceneStore = new RekallAgeSceneStore();
        await sceneStore.SaveAsync(root, scene, CancellationToken.None);

        var report = await new RekallAgeProjectValidator(sceneStore)
            .ValidateSceneAsync(root, "Main", CancellationToken.None);

        Assert.Contains(report.Issues, issue =>
            issue.Code == "REKALL_XR_RIG_MISSING"
            && issue.Severity == "warning");
        Assert.Contains(report.Issues, issue =>
            issue.Code == "REKALL_XR_CAMERA_NOT_STEREO"
            && issue.Message.Contains("stereo", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Issues, issue =>
            issue.Code == "REKALL_XR_CAMERA_POSE_SOURCE_MISSING"
            && issue.Message.Contains("XrPoseSource", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateVrSceneAcceptsRigPoseSourceAndControllers()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d", "vr"])
            .AddEntity(RekallAgeEntityDocument.Create("VrRig", ["xr"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.XrRig", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("HeadCamera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["stereoMode"] = "stereo",
                    ["stereoRenderMode"] = "single-pass-multiview"
                }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.XrPoseSource", new JsonObject
                {
                    ["source"] = "head"
                })))
            .AddEntity(RekallAgeEntityDocument.Create("LeftController", ["controller"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.XrController", new JsonObject { ["hand"] = "left" })))
            .AddEntity(RekallAgeEntityDocument.Create("RightController", ["controller"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.XrController", new JsonObject { ["hand"] = "right" })));
        var sceneStore = new RekallAgeSceneStore();
        await sceneStore.SaveAsync(root, scene, CancellationToken.None);

        var report = await new RekallAgeProjectValidator(sceneStore)
            .ValidateSceneAsync(root, "Main", CancellationToken.None);

        Assert.DoesNotContain(report.Issues, issue => issue.Code.StartsWith("REKALL_XR_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateSceneCommandReturnsAgentReadableIssueSummary()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d", "vr"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["stereoMode"] = "mono"
                })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);
        var context = new RekallAgeCommandContext(
            "test",
            RekallAgeTransaction.Begin("validate scene"),
            CancellationToken.None);

        var result = await new ValidateSceneCommand().ExecuteAsync(
            new ValidateSceneRequest(root, "Main"),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal("ok", result.Value.Status);
        Assert.True(result.Value.WarningCount >= 3);
        Assert.Contains(result.Value.Issues, issue => issue.Code == "REKALL_XR_RIG_MISSING");
        Assert.Contains(result.Value.SuggestedNextActions, action => action.Tool == "rekall.scene.apply_blueprint");
    }
}
