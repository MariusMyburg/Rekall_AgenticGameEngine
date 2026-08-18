using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Compatibility;
using Rekall.Age.Core.Transactions;
using Rekall.Age.World;
using Rekall.Age.World.Commands;
using System.Text;
using System.Collections.Concurrent;

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

    [Fact]
    public async Task SavedSceneDeclaresCurrentSchema()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeSceneStore();
        var scene = RekallAgeSceneDocument.Create("Main", ["world"]);

        await store.SaveAsync(root, scene, CancellationToken.None);

        var json = await File.ReadAllTextAsync(store.GetScenePath(root, "Main"));
        Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);
        Assert.Equal(1, scene.SchemaVersion);
    }

    [Fact]
    public async Task LegacySceneLoadsAsCurrentWithoutRewritingSource()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeSceneStore();
        var path = store.GetScenePath(root, "Main");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const string legacy = """
            {
              "id": "scene_legacy",
              "name": "Main",
              "capabilities": ["world"],
              "entities": []
            }
            """;
        await File.WriteAllTextAsync(path, legacy);

        var scene = await store.LoadAsync(root, "Main", CancellationToken.None);

        Assert.Equal(1, scene.SchemaVersion);
        Assert.Equal(legacy, await File.ReadAllTextAsync(path));
    }

    [Theory]
    [InlineData("2", "REKALL_DOCUMENT_SCHEMA_FUTURE")]
    [InlineData("-1", "REKALL_DOCUMENT_SCHEMA_INVALID")]
    [InlineData("true", "REKALL_DOCUMENT_SCHEMA_INVALID")]
    public async Task UnsupportedSceneSchemaFailsClosed(string schemaToken, string expectedCode)
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeSceneStore();
        var path = store.GetScenePath(root, "Main");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            $$"""{"schemaVersion":{{schemaToken}},"id":"scene_blocked","name":"Main","capabilities":[],"entities":[]}""");

        var error = await Assert.ThrowsAsync<RekallAgeDocumentCompatibilityException>(
            () => store.LoadAsync(root, "Main", CancellationToken.None).AsTask());

        Assert.Equal(expectedCode, error.Code);
        Assert.Equal("scene", error.DocumentKind);
        Assert.Equal(Path.GetFullPath(path), error.DocumentPath);
    }

    [Fact]
    public async Task SceneLoadAcceptsTheSameBoundedDepthAsSchemaInspection()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeSceneStore();
        var path = store.GetScenePath(root, "Main");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var nested = new string('[', 80) + "0" + new string(']', 80);
        await File.WriteAllTextAsync(
            path,
            $$"""{"schemaVersion":1,"id":"deep","name":"Main","capabilities":[],"entities":[],"extension":{{nested}}}""");

        var scene = await store.LoadAsync(root, "Main", CancellationToken.None);

        Assert.Equal("deep", scene.Id);
    }

    [Fact]
    public async Task SceneSavePublishesBomlessJsonWithoutTemporarySiblings()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeSceneStore();

        await store.SaveAsync(root, RekallAgeSceneDocument.Create("Main", ["world"]), CancellationToken.None);

        var path = store.GetScenePath(root, "Main");
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, ".Main.age.scene.json.tmp-*"));
    }

    [Fact]
    public async Task ConcurrentReadersObserveOnlyCompleteSceneDocumentsDuringRepeatedSaves()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeSceneStore();
        var scene = Enumerable.Range(0, 128).Aggregate(
            RekallAgeSceneDocument.Create("Main", ["world"]),
            (current, index) => current.AddEntity(RekallAgeEntityDocument.Create($"Entity {index:D3}", ["stress"])));
        await store.SaveAsync(root, scene, CancellationToken.None);
        var failures = new ConcurrentQueue<Exception>();
        using var stop = new CancellationTokenSource();
        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    var loaded = await store.LoadAsync(root, "Main", CancellationToken.None);
                    Assert.Equal(128, loaded.Entities.Count);
                }
                catch (Exception error)
                {
                    failures.Enqueue(error);
                    break;
                }
            }
        })).ToArray();

        for (var index = 0; index < 50; index++)
        {
            await store.SaveAsync(root, scene with { Id = $"scene_{index:D3}" }, CancellationToken.None);
            await Task.Delay(1);
        }
        stop.Cancel();
        await Task.WhenAll(readers);

        Assert.Empty(failures);
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "Scenes"), ".Main.age.scene.json.tmp-*"));
    }
}
