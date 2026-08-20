using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Project.Commands;
using Rekall.Age.Workflows.Commands;
using Rekall.Age.World;
using Rekall.Age.World.Commands;

namespace Rekall.Age.Tests.Workflows;

public sealed class CreateBlueprintProjectTests
{
    [Fact]
    public void BlueprintSchemasExposeCompactExactNestedJsonShapesForAgents()
    {
        var projectDescription = new CreateBlueprintProjectCommand().Schema.Description;
        var sceneDescription = new ApplySceneBlueprintCommand().Schema.Description;

        Assert.Contains("\"scenes\":[", projectDescription, StringComparison.Ordinal);
        Assert.Contains("\"projectRoot\"", projectDescription, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"Rekall.Transform3D\"", projectDescription, StringComparison.Ordinal);
        Assert.Contains("never a JSON string", projectDescription, StringComparison.Ordinal);
        Assert.Contains("\"entities\":[", sceneDescription, StringComparison.Ordinal);
        Assert.Contains("\"properties\":{", sceneDescription, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreatesProjectSceneAndCompleteAgentSuppliedBlueprintInOneCommand()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("blueprint project"), CancellationToken.None);

        var result = await new CreateBlueprintProjectCommand().ExecuteAsync(
            new CreateBlueprintProjectRequest(
                root,
                "Agent Project",
                ["world", "ui"],
                "Main",
                ["world", "ui"],
                [new RekallAgeSceneBlueprintEntity(
                    "HUD",
                    ["ui"],
                    [new RekallAgeSceneBlueprintComponent("Rekall.UiCanvas", new JsonObject())])]),
            context);

        Assert.True(result.Ok, result.Summary);
        var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
        Assert.Equal("HUD", Assert.Single(scene.Entities).Name);
        Assert.Contains(context.Transaction.ChangedResources, path => path.EndsWith("rekall.project.json"));
        Assert.Contains(context.Transaction.ChangedResources, path => path.EndsWith("Main.age.scene.json"));
    }

    [Fact]
    public async Task CreatesMultipleCompleteScenesInOneCommand()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("multi-scene blueprint project"),
            CancellationToken.None);

        var result = await new CreateBlueprintProjectCommand().ExecuteAsync(
            new CreateBlueprintProjectRequest(
                root,
                "Multi Scene Agent Project",
                ["world", "physics"],
                Scenes:
                [
                    new RekallAgeProjectBlueprintScene(
                        "Main",
                        ["world", "physics"],
                        [new RekallAgeSceneBlueprintEntity("3D Body", ["physics"], [])]),
                    new RekallAgeProjectBlueprintScene(
                        "Physics2D",
                        ["world", "physics"],
                        [new RekallAgeSceneBlueprintEntity("2D Body", ["physics"], [])])
                ]),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(2, result.Value.Scenes.Count);
        var store = new RekallAgeSceneStore();
        Assert.Equal("3D Body", Assert.Single((await store.LoadAsync(root, "Main", CancellationToken.None)).Entities).Name);
        Assert.Equal("2D Body", Assert.Single((await store.LoadAsync(root, "Physics2D", CancellationToken.None)).Entities).Name);
        Assert.Contains(context.Transaction.ChangedResources, path => path.EndsWith("Main.age.scene.json"));
        Assert.Contains(context.Transaction.ChangedResources, path => path.EndsWith("Physics2D.age.scene.json"));
    }

    [Fact]
    public async Task CreatesNamedEmptySceneScaffoldsForIncrementalAuthoringRecovery()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("empty scene scaffold"),
            CancellationToken.None);

        var result = await new CreateBlueprintProjectCommand().ExecuteAsync(
            new CreateBlueprintProjectRequest(
                root,
                "Scaffolded Project",
                ["world"],
                Scenes:
                [
                    new RekallAgeProjectBlueprintScene("Main", ["world"], []),
                    new RekallAgeProjectBlueprintScene("Physics2D", ["world"], [])
                ]),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(2, result.Value.Scenes.Count);
        var store = new RekallAgeSceneStore();
        Assert.Empty((await store.LoadAsync(root, "Main", CancellationToken.None)).Entities);
        Assert.Empty((await store.LoadAsync(root, "Physics2D", CancellationToken.None)).Entities);
    }

    [Fact]
    public async Task ReusesCompatibleExistingStudioProjectAndScene()
    {
        var root = TestPaths.CreateTempDirectory();
        var setup = new RekallAgeCommandContext(
            "studio",
            RekallAgeTransaction.Begin("open project"),
            CancellationToken.None);
        await new CreateProjectCommand().ExecuteAsync(
            new CreateProjectRequest(root, "Studio Project", ["world", "ui"]),
            setup);
        await new CreateSceneCommand().ExecuteAsync(
            new CreateSceneRequest(root, "Main", ["world", "ui"]),
            setup);

        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("author open project"),
            CancellationToken.None);
        var result = await new CreateBlueprintProjectCommand().ExecuteAsync(
            new CreateBlueprintProjectRequest(
                root,
                "Studio Project",
                ["world", "ui"],
                "Main",
                ["world", "ui"],
                [new RekallAgeSceneBlueprintEntity("HUD", ["ui"], [])]),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Contains("existing", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("HUD", Assert.Single(
            (await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None)).Entities).Name);
        Assert.DoesNotContain(
            context.Transaction.ChangedResources,
            path => path.EndsWith("rekall.project.json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RejectsExistingProjectOrSceneMissingRequestedCapabilitiesWithoutRewritingThem()
    {
        var root = TestPaths.CreateTempDirectory();
        var setup = new RekallAgeCommandContext(
            "studio",
            RekallAgeTransaction.Begin("open limited project"),
            CancellationToken.None);
        await new CreateProjectCommand().ExecuteAsync(
            new CreateProjectRequest(root, "Limited Project", ["world"]),
            setup);
        await new CreateSceneCommand().ExecuteAsync(
            new CreateSceneRequest(root, "Main", ["world"]),
            setup);

        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("reject incompatible project"),
            CancellationToken.None);
        var result = await new CreateBlueprintProjectCommand().ExecuteAsync(
            new CreateBlueprintProjectRequest(
                root,
                "Limited Project",
                ["world", "ui"],
                "Main",
                ["world", "ui"],
                [new RekallAgeSceneBlueprintEntity("HUD")]),
            context);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_BLUEPRINT_PROJECT_CAPABILITY_MISSING");
        Assert.Empty(context.Transaction.ChangedResources);
        var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
        Assert.Empty(scene.Entities);
    }

    [Fact]
    public async Task RejectsInvalidLaterSceneBeforeWritingAnyProjectFiles()
    {
        var parent = TestPaths.CreateTempDirectory();
        var root = Path.Combine(parent, "AtomicProject");
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("reject invalid multi-scene blueprint"),
            CancellationToken.None);

        var result = await new CreateBlueprintProjectCommand().ExecuteAsync(
            new CreateBlueprintProjectRequest(
                root,
                "Atomic Project",
                ["world"],
                Scenes:
                [
                    new RekallAgeProjectBlueprintScene(
                        "Main",
                        ["world"],
                        [new RekallAgeSceneBlueprintEntity("Valid Entity")]),
                    new RekallAgeProjectBlueprintScene(
                        "Broken",
                        ["world"],
                        [new RekallAgeSceneBlueprintEntity(" ")])
                ]),
            context);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_SCENE_BLUEPRINT_ENTITY_NAME_REQUIRED");
        Assert.False(Directory.Exists(root));
        Assert.Empty(context.Transaction.ChangedResources);
    }
}
