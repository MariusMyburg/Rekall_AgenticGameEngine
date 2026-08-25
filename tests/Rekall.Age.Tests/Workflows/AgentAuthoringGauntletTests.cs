using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules.Commands;
using Rekall.Age.Project;
using Rekall.Age.Workflows.Commands;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Workflows;

public sealed class AgentAuthoringGauntletTests
{
    [Fact]
    public async Task GauntletAuthorsAnExistingEmptyEditorSceneBeforePackaging()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Editor Project", ["world", "rendering2d"]),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"]),
            CancellationToken.None);
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("existing empty editor scene gauntlet"),
            CancellationToken.None);

        var result = await new RunAgentAuthoringGauntletCommand().ExecuteAsync(
            new RunAgentAuthoringGauntletRequest(root, "Editor Project", "Main", Path.Combine(root, "Package")),
            context);

        Assert.True(result.Ok, result.Summary + Environment.NewLine + string.Join(
            Environment.NewLine,
            result.Errors.Select(error => $"{error.Code}: {error.Message}")));
        Assert.True(result.Value.Ready);
        Assert.Contains(result.Value.Checks, check => check is { Name: "scene-blueprint-authored", Passed: true });
        Assert.DoesNotContain(result.Value.Checks, check => check.Name == "scene-preserved");
        var authored = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
        Assert.Contains(authored.Entities, entity => entity.Name == "Agent Authored Marker");
        Assert.NotNull(result.Value.Package);
        Assert.NotNull(result.Value.Audit);
    }

    [Fact]
    public async Task GauntletPackagesExistingThreeDimensionalGameWithoutReplacingAuthoredScene()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Existing 3D Game", ["world", "rendering3d"]),
            CancellationToken.None);
        var authoredScene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("AuthoredCamera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Camera3D",
                    new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("AuthoredCitadel", ["authored"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.MeshRenderer",
                    new JsonObject { ["mesh"] = "rekall.primitive.cube" })));
        await new RekallAgeSceneStore().SaveAsync(root, authoredScene, CancellationToken.None);
        var output = Path.Combine(root, "Package");
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("existing 3d authoring gauntlet"),
            CancellationToken.None);
        var scaffold = await new ScaffoldPlayableModuleCommand().ExecuteAsync(
            new ScaffoldPlayableModuleRequest(root, "existing.game", "Existing Game", "ExistingGame"),
            context);
        Assert.True(scaffold.Ok, scaffold.Summary);

        var result = await new RunAgentAuthoringGauntletCommand().ExecuteAsync(
            new RunAgentAuthoringGauntletRequest(root, "Existing 3D Game", "Main", output),
            context);

        Assert.True(result.Ok, result.Summary + Environment.NewLine + string.Join(
            Environment.NewLine,
            result.Errors.Select(error => $"{error.Code}: {error.Message}")));
        Assert.True(result.Value.Ready);
        Assert.Contains(result.Value.Checks, check => check is { Name: "scene-preserved", Passed: true });
        var preserved = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
        Assert.Contains(preserved.Entities, entity => entity.Name == "AuthoredCitadel");
        Assert.DoesNotContain(preserved.Entities, entity => entity.Name == "Agent Authored Marker");
    }

    [Fact]
    public async Task GauntletAuthorsPackagesAuditsAndCapturesGenericPlayableProject()
    {
        var root = TestPaths.CreateTempDirectory();
        var output = Path.Combine(root, "Package");
        var context = new Rekall.Age.Core.Commands.RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("agent authoring gauntlet"),
            CancellationToken.None);

        var result = await new RunAgentAuthoringGauntletCommand().ExecuteAsync(
            new RunAgentAuthoringGauntletRequest(root, "Gauntlet Proof", "Main", output),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.Ready);
        Assert.Equal(root, result.Value.ProjectRoot);
        Assert.Equal("Main", result.Value.SceneName);
        Assert.NotNull(result.Value.Package);
        Assert.NotNull(result.Value.Audit);
        Assert.Contains(result.Value.Package!.Checks, check => check is { Name: "module-trust", Passed: true });
        Assert.True(File.Exists(result.Value.Package!.ArchivePath));
        Assert.True(File.Exists(result.Value.Audit!.Capture.OutputPath));
        Assert.All(result.Value.Checks, check => Assert.True(check.Passed, check.Summary));
        Assert.Contains(result.Value.Checks, check => check.Name == "project-created");
        Assert.Contains(result.Value.Checks, check => check.Name == "scene-blueprint-authored");
        Assert.Contains(result.Value.Checks, check => check.Name == "module-source-authored");
        Assert.Contains(result.Value.Checks, check => check.Name == "package-audited");
        Assert.Contains(result.Value.NextActions, action => action == "rekall.workflow.inspect_playable_package");
        Assert.Contains(result.Value.NextActions, action => action == "rekall.workflow.run_playable_package");
        Assert.Contains(result.Value.NextActions, action => action == "rekall.workflow.capture_playable_package_frame");
        Assert.DoesNotContain("template", result.Value.AuthoringMode, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(result.Value.NextActions, action => action.Contains("template", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GauntletStopsWithStructuredChecksWhenProjectRootIsUnsafe()
    {
        var context = new Rekall.Age.Core.Commands.RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("agent authoring gauntlet failure"),
            CancellationToken.None);

        var result = await new RunAgentAuthoringGauntletCommand().ExecuteAsync(
            new RunAgentAuthoringGauntletRequest(string.Empty, "Gauntlet Proof"),
            context);

        Assert.False(result.Ok);
        Assert.False(result.Value.Ready);
        Assert.Contains(result.Value.Checks, check => check is { Name: "project-root", Passed: false });
        Assert.Contains(result.Value.NextActions, action => action == "rekall.project.create");
        Assert.Contains(result.Errors, error => error.Code == "REKALL_AGENT_GAUNTLET_PROJECT_ROOT_INVALID");
    }
}
