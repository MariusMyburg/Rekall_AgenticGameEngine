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
}
