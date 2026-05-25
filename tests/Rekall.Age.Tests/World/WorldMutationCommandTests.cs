using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.World;
using Rekall.Age.World.Commands;

namespace Rekall.Age.Tests.World;

public sealed class WorldMutationCommandTests
{
    [Fact]
    public async Task SetComponentPropertyUpdatesOnePropertyWithoutReplacingComponent()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeSceneStore();
        var entity = RekallAgeEntityDocument.Create("Player", ["player"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Game.PlayerController",
                new JsonObject
                {
                    ["speed"] = 4,
                    ["health"] = 3
                }));
        await store.SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["2d"]).AddEntity(entity),
            CancellationToken.None);
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("set property"), CancellationToken.None);
        var command = new SetComponentPropertyCommand();

        var result = await command.ExecuteAsync(
            new SetComponentPropertyRequest(root, "Main", entity.Id, "Game.PlayerController", "speed", JsonValue.Create(7)!),
            context);

        Assert.True(result.Ok, result.Summary);
        var updated = await store.LoadAsync(root, "Main", CancellationToken.None);
        var component = Assert.Single(updated.Entities.Single().Components);
        Assert.Equal(7, component.Properties["speed"]!.GetValue<int>());
        Assert.Equal(3, component.Properties["health"]!.GetValue<int>());
        var preimage = Assert.Single(context.Transaction.ResourcePreimages);
        Assert.EndsWith("Main.age.scene.json", preimage.Resource, StringComparison.Ordinal);
        Assert.True(preimage.ExistedBefore);
        Assert.Contains("\"speed\": 4", preimage.ReadUtf8Text(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InspectEntityReturnsFullComponentProperties()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeSceneStore();
        var entity = RekallAgeEntityDocument.Create("Door", ["interactive"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Game.LockedDoor",
                new JsonObject { ["requiresKey"] = true }));
        await store.SaveAsync(root, RekallAgeSceneDocument.Create("Main", ["2d"]).AddEntity(entity), CancellationToken.None);
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("inspect"), CancellationToken.None);
        var command = new InspectEntityCommand();

        var result = await command.ExecuteAsync(new InspectEntityRequest(root, "Main", entity.Id), context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal("Door", result.Value.Entity.Name);
        var component = Assert.Single(result.Value.Entity.Components);
        Assert.Equal("Game.LockedDoor", component.Type);
        Assert.True(component.Properties["requiresKey"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ApplySceneBlueprintReplacesSceneEntitiesInOneCommand()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeSceneStore();
        await store.SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world"])
                .AddEntity(RekallAgeEntityDocument.Create("Old Marker", ["stale"])),
            CancellationToken.None);
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("apply blueprint"), CancellationToken.None);
        var command = new ApplySceneBlueprintCommand();

        var result = await command.ExecuteAsync(
            new ApplySceneBlueprintRequest(
                root,
                "Main",
                [
                    new RekallAgeSceneBlueprintEntity(
                        "Camera",
                        ["camera"],
                        [
                            new RekallAgeSceneBlueprintComponent(
                                "Rekall.Camera3D",
                                new JsonObject { ["active"] = true }),
                            new RekallAgeSceneBlueprintComponent(
                                "Rekall.Transform3D",
                                new JsonObject { ["x"] = 1, ["y"] = 2, ["z"] = 3 })
                        ]),
                    new RekallAgeSceneBlueprintEntity(
                        "Target",
                        ["enemy", "target"],
                        [
                            new RekallAgeSceneBlueprintComponent(
                                "Game.Agent.Target",
                                new JsonObject { ["health"] = 100 })
                        ],
                        Visible: false)
                ],
                ClearExisting: true),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(2, result.Value.EntityCount);
        Assert.Equal(2, result.Value.UpsertedCount);
        Assert.Equal(1, result.Value.RemovedCount);
        var saved = await store.LoadAsync(root, "Main", CancellationToken.None);
        Assert.DoesNotContain(saved.Entities, entity => entity.Name == "Old Marker");
        var camera = saved.Entities.Single(entity => entity.Name == "Camera");
        Assert.Contains(camera.Components, component => component.Type == "Rekall.Camera3D");
        var target = saved.Entities.Single(entity => entity.Name == "Target");
        Assert.False(target.Visible);
        Assert.Equal(["enemy", "target"], target.Tags);
        Assert.Single(context.Transaction.ResourcePreimages);
        Assert.Single(context.Transaction.ChangedResources);
    }

    [Fact]
    public async Task DeleteEntityRemovesOneEntityAndCapturesPreimage()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeSceneStore();
        var keep = RekallAgeEntityDocument.Create("Keep", ["level"]);
        var remove = RekallAgeEntityDocument.Create("Remove", ["duplicate"]);
        await store.SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world"]).AddEntity(keep).AddEntity(remove),
            CancellationToken.None);
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("delete entity"), CancellationToken.None);
        var command = new DeleteEntityCommand();

        var result = await command.ExecuteAsync(new DeleteEntityRequest(root, "Main", remove.Id), context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(remove.Id, result.Value.DeletedEntityId);
        var saved = await store.LoadAsync(root, "Main", CancellationToken.None);
        Assert.Single(saved.Entities);
        Assert.Equal("Keep", saved.Entities.Single().Name);
        Assert.Single(context.Transaction.ResourcePreimages);
        Assert.Single(context.Transaction.ChangedResources);
    }
}
