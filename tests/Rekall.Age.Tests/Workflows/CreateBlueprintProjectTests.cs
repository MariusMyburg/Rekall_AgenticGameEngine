using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Workflows.Commands;
using Rekall.Age.World;
using Rekall.Age.World.Commands;

namespace Rekall.Age.Tests.Workflows;

public sealed class CreateBlueprintProjectTests
{
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
}
