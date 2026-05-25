using System.Text.Json.Nodes;
using Rekall.Age.Validation;
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
}
