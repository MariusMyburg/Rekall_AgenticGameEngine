using System.Text.Json.Nodes;
using Rekall.Age.Agent;
using Rekall.Age.Project;
using Rekall.Age.Validation;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Agent;

public sealed class AgentContextTests
{
    [Fact]
    public async Task ProjectSummaryReportsMissingSceneCamera()
    {
        var root = TestPaths.CreateTempDirectory();
        var projectStore = new RekallAgeProjectStore();
        var sceneStore = new RekallAgeSceneStore();

        await projectStore.SaveAsync(root, RekallAgeProjectManifest.Create("Crystal Mines", ["world", "rendering2d"]), CancellationToken.None);
        await sceneStore.SaveAsync(root, RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"]), CancellationToken.None);

        var builder = new RekallAgeContextBuilder(projectStore, sceneStore, new RekallAgeProjectValidator(sceneStore));
        var summary = await builder.BuildProjectSummaryAsync(root, CancellationToken.None);

        Assert.Equal("Crystal Mines", summary.Project);
        Assert.Equal("blocked", summary.Health.Status);
        Assert.Contains(summary.Health.BlockingIssues, issue => issue.Contains("active camera", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("rekall.workflow.fix_validation_errors", summary.RecommendedNextActions);
    }

    [Fact]
    public async Task ProjectSummaryIsOkWhenSceneHasCameraAndPlayableLoop()
    {
        var root = TestPaths.CreateTempDirectory();
        var projectStore = new RekallAgeProjectStore();
        var sceneStore = new RekallAgeSceneStore();
        var camera = RekallAgeEntityDocument.Create("MainCamera", ["camera"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true }));
        var gameRules = RekallAgeEntityDocument.Create("GameRules", ["game_rules"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.PlayableLoop", new JsonObject { ["kind"] = "puzzle" }));
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"]).AddEntity(camera).AddEntity(gameRules);

        await projectStore.SaveAsync(root, RekallAgeProjectManifest.Create("Puzzle Box", ["world", "rendering2d"]), CancellationToken.None);
        await sceneStore.SaveAsync(root, scene, CancellationToken.None);

        var builder = new RekallAgeContextBuilder(projectStore, sceneStore, new RekallAgeProjectValidator(sceneStore));
        var summary = await builder.BuildProjectSummaryAsync(root, CancellationToken.None);

        Assert.Equal("ok", summary.Health.Status);
        Assert.Empty(summary.Health.BlockingIssues);
        Assert.Contains("rekall.workflow.package_playable_game", summary.RecommendedNextActions);
    }

    [Fact]
    public async Task ProjectSummaryReportsPackageArtifactsAndAuditNextAction()
    {
        var root = TestPaths.CreateTempDirectory();
        var builds = Path.Combine(root, "Builds", "Packaged");
        Directory.CreateDirectory(builds);
        await File.WriteAllTextAsync(Path.Combine(builds, "rekall.package.json"), "{}");
        await File.WriteAllTextAsync($"{builds}.zip", "fake package");

        var projectStore = new RekallAgeProjectStore();
        var sceneStore = new RekallAgeSceneStore();
        var camera = RekallAgeEntityDocument.Create("MainCamera", ["camera"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true }));
        var gameRules = RekallAgeEntityDocument.Create("GameRules", ["game_rules"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.PlayableLoop", new JsonObject { ["kind"] = "puzzle" }));
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"]).AddEntity(camera).AddEntity(gameRules);

        await projectStore.SaveAsync(root, RekallAgeProjectManifest.Create("Packaged Puzzle", ["world", "rendering2d"]), CancellationToken.None);
        await sceneStore.SaveAsync(root, scene, CancellationToken.None);

        var builder = new RekallAgeContextBuilder(projectStore, sceneStore, new RekallAgeProjectValidator(sceneStore));
        var summary = await builder.BuildProjectSummaryAsync(root, CancellationToken.None);

        Assert.Contains(summary.Artifacts, artifact => artifact.Kind == "playable-package-archive" && artifact.Path == "Builds/Packaged.zip");
        Assert.Contains(summary.Artifacts, artifact => artifact.Kind == "playable-package-manifest" && artifact.Path == "Builds/Packaged/rekall.package.json");
        Assert.Contains("rekall.workflow.audit_playable_package", summary.RecommendedNextActions);
    }
}
