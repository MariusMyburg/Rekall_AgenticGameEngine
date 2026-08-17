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
}
