using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.World;
using Rekall.Age.World.Commands;

namespace Rekall.Age.Tests.World;

public sealed class SceneStoreTests
{
    [Fact]
    public async Task CreateSceneAndEntityWritesStableJson()
    {
        var root = TestPaths.CreateTempDirectory();
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new CreateSceneCommand());
        registry.Register(new CreateEntityCommand());
        var context = new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("world"), CancellationToken.None);

        await registry.ExecuteAsync<CreateSceneRequest, CreateSceneResult>(
            "rekall.scene.create",
            new CreateSceneRequest(root, "Main", ["rendering2d", "world"]),
            context);

        var entity = await registry.ExecuteAsync<CreateEntityRequest, CreateEntityResult>(
            "rekall.entity.create",
            new CreateEntityRequest(root, "Main", "Player", ["player"]),
            context);

        Assert.True(entity.Ok);

        var scenePath = Path.Combine(root, "Scenes", "Main.age.scene.json");
        var json = await File.ReadAllTextAsync(scenePath);
        Assert.Contains("\"name\": \"Main\"", json);
        Assert.Contains("\"name\": \"Player\"", json);
        Assert.Contains("\"tags\"", json);
    }

    [Fact]
    public async Task AddComponentUpdatesTargetEntity()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeSceneStore();
        var scene = RekallAgeSceneDocument.Create("Main", ["world"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"]));
        await store.SaveAsync(root, scene, CancellationToken.None);
        var cameraId = scene.Entities.Single().Id;
        var command = new AddComponentCommand();
        var context = new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("component"), CancellationToken.None);

        var result = await command.ExecuteAsync(
            new AddComponentRequest(root, "Main", cameraId, "Rekall.Camera2D", new JsonObject { ["active"] = true }),
            context);

        Assert.True(result.Ok);
        Assert.Contains(result.Value.Scene.Entities.Single().Components, component => component.Type == "Rekall.Camera2D");
    }
}
