using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.World;
using Rekall.Age.World.Commands;

namespace Rekall.Age.Tests.Core;

public sealed class TransactionHistoryCommandTests
{
    [Fact]
    public async Task DeferredResourcePreimageRecordsSuppliedPreSaveBytesAfterTheResourceChanges()
    {
        var root = TestPaths.CreateTempDirectory();
        var path = Path.Combine(root, "deferred.age.json");
        await File.WriteAllTextAsync(path, "{\"value\":1}");
        var before = await File.ReadAllBytesAsync(path);
        await File.WriteAllTextAsync(path, "{\"value\":2}");
        var transaction = RekallAgeTransaction.Begin("deferred preimage");

        transaction.RecordResourcePreimage(path, existedBefore: true, before);

        before[0] = (byte)'!';
        var preimage = Assert.Single(transaction.ResourcePreimages);
        Assert.Equal(path, preimage.Resource);
        Assert.True(preimage.ExistedBefore);
        Assert.Equal("{\"value\":1}", preimage.ReadUtf8Text());
        Assert.NotEqual(await File.ReadAllBytesAsync(path), preimage.Content);
    }

    [Fact]
    public async Task RestoreFallsBackToFullPreimageWhenResourceAdvancedBeyondDeltaAfterState()
    {
        var root = TestPaths.CreateTempDirectory();
        var path = Path.Combine(root, "large.age.json");
        var initial = new JsonObject
        {
            ["values"] = new JsonArray(Enumerable.Range(0, 256).Select(index => JsonValue.Create(index)).ToArray())
        };
        await File.WriteAllTextAsync(path, initial.ToJsonString(new() { WriteIndented = true }) + Environment.NewLine);
        var originalBytes = await File.ReadAllBytesAsync(path);
        var transaction = RekallAgeTransaction.Begin("first edit");
        transaction.CaptureResourcePreimage(path);
        initial["values"]![128] = 900;
        await File.WriteAllTextAsync(path, initial.ToJsonString(new() { WriteIndented = true }) + Environment.NewLine);
        transaction.RecordChangedResource(path);
        var history = new RekallAgeTransactionLogStore();
        await history.AppendAsync(root, transaction, "agent", CancellationToken.None);
        initial["values"]![129] = 901;
        await File.WriteAllTextAsync(path, initial.ToJsonString(new() { WriteIndented = true }) + Environment.NewLine);

        var result = await new RestoreTransactionPreimageCommand(
            history,
            new RekallAgeResourceRestorationPolicy()).ExecuteAsync(
            new(root, transaction.Id, "large.age.json"),
            new("agent", RekallAgeTransaction.Begin("restore"), CancellationToken.None));

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task ListTransactionHistoryReturnsRecentProjectTransactions()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeTransactionLogStore();
        Directory.CreateDirectory(Path.Combine(root, "Scenes"));
        await File.WriteAllTextAsync(Path.Combine(root, "rekall.project.json"), "{}", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(root, "Scenes", "Main.age.scene.json"), "{\"entities\":[]}", CancellationToken.None);
        var first = RekallAgeTransaction.Begin("first command");
        first.RecordChangedResource(Path.Combine(root, "rekall.project.json"));
        await store.AppendAsync(root, first, "agent", CancellationToken.None);
        await Task.Delay(5, CancellationToken.None);
        var second = RekallAgeTransaction.Begin("second command");
        second.RecordChangedResource(Path.Combine(root, "Scenes", "Main.age.scene.json"));
        await store.AppendAsync(root, second, "mcp", CancellationToken.None);

        var result = await new ListTransactionHistoryCommand().ExecuteAsync(
            new ListTransactionHistoryRequest(root, Limit: 1),
            new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("history"), CancellationToken.None));

        Assert.True(result.Ok, result.Summary);
        Assert.EndsWith(Path.Combine("Transactions", "transactions.age.json"), result.Value.LogPath, StringComparison.Ordinal);
        var transaction = Assert.Single(result.Value.Transactions);
        Assert.Equal(second.Id, transaction.Id);
        Assert.Equal("second command", transaction.Name);
        Assert.Equal("mcp", transaction.Actor);
        Assert.Contains(transaction.ChangedResources, resource => resource.EndsWith("Main.age.scene.json", StringComparison.Ordinal));
        var resourceChange = Assert.Single(transaction.ResourceChanges);
        Assert.Equal(Path.Combine("Scenes", "Main.age.scene.json"), resourceChange.RelativePath);
        Assert.Equal("scene", resourceChange.Kind);
        Assert.True(resourceChange.Exists);
        Assert.True(resourceChange.SizeBytes > 0);
    }

    [Fact]
    public async Task TransactionLogLoadKeepsOlderEntriesCompatible()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeTransactionLogStore();
        Directory.CreateDirectory(Path.GetDirectoryName(store.GetPath(root))!);
        await File.WriteAllTextAsync(
            store.GetPath(root),
            """
            {
              "transactions": [
                {
                  "id": "txn_old",
                  "name": "old command",
                  "actor": "agent",
                  "startedAtUtc": "2026-05-25T10:00:00+00:00",
                  "changedResources": ["rekall.project.json"]
                }
              ]
            }
            """,
            CancellationToken.None);

        var document = await store.LoadAsync(root, CancellationToken.None);

        var transaction = Assert.Single(document.Transactions);
        Assert.Equal("txn_old", transaction.Id);
        Assert.Empty(transaction.ResourceChanges);
        Assert.Empty(transaction.ResourcePreimages);
    }

    [Fact]
    public async Task TransactionHistoryPersistsResourcePreimageSnapshots()
    {
        var root = TestPaths.CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(root, "rekall.project.json"), "{}", CancellationToken.None);
        var sceneStore = new RekallAgeSceneStore();
        var entity = RekallAgeEntityDocument.Create("Player", ["player"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Game.PlayerController",
                new System.Text.Json.Nodes.JsonObject
                {
                    ["speed"] = 4,
                    ["health"] = 3
                }));
        await sceneStore.SaveAsync(root, RekallAgeSceneDocument.Create("Main", ["2d"]).AddEntity(entity), CancellationToken.None);
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("set speed"), CancellationToken.None);

        var result = await new SetComponentPropertyCommand().ExecuteAsync(
            new SetComponentPropertyRequest(
                root,
                "Main",
                entity.Id,
                "Game.PlayerController",
                "speed",
                System.Text.Json.Nodes.JsonValue.Create(7)!),
            context);
        await new RekallAgeTransactionLogStore().AppendAsync(root, context.Transaction, context.Actor, CancellationToken.None);

        Assert.True(result.Ok, result.Summary);
        var log = await new RekallAgeTransactionLogStore().LoadAsync(root, CancellationToken.None);
        var transaction = Assert.Single(log.Transactions);
        var preimage = Assert.Single(transaction.ResourcePreimages);
        Assert.Equal(Path.Combine("Scenes", "Main.age.scene.json"), preimage.RelativePath);
        Assert.True(preimage.ExistedBefore);
        Assert.NotNull(preimage.SnapshotPath);
        Assert.True(preimage.SizeBytes > 0);
        Assert.False(string.IsNullOrWhiteSpace(preimage.Sha256));
        var snapshot = await File.ReadAllTextAsync(Path.Combine(root, preimage.SnapshotPath!), CancellationToken.None);
        Assert.Contains("\"speed\": 4", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("\"speed\": 7", snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestoreTransactionPreimageRestoresChangedResourceAndCapturesCurrentPreimage()
    {
        var root = TestPaths.CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(root, "rekall.project.json"), "{}", CancellationToken.None);
        var sceneStore = new RekallAgeSceneStore();
        var entity = RekallAgeEntityDocument.Create("Player", ["player"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Game.PlayerController",
                new System.Text.Json.Nodes.JsonObject
                {
                    ["speed"] = 4,
                    ["health"] = 3
                }));
        await sceneStore.SaveAsync(root, RekallAgeSceneDocument.Create("Main", ["2d"]).AddEntity(entity), CancellationToken.None);
        var mutateContext = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("set speed"), CancellationToken.None);
        await new SetComponentPropertyCommand().ExecuteAsync(
            new SetComponentPropertyRequest(
                root,
                "Main",
                entity.Id,
                "Game.PlayerController",
                "speed",
                System.Text.Json.Nodes.JsonValue.Create(7)!),
            mutateContext);
        await new RekallAgeTransactionLogStore().AppendAsync(root, mutateContext.Transaction, mutateContext.Actor, CancellationToken.None);
        var restoreContext = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("restore speed"), CancellationToken.None);

        var result = await new RestoreTransactionPreimageCommand(
            new RekallAgeResourceRestorationPolicy()).ExecuteAsync(
            new RestoreTransactionPreimageRequest(
                root,
                mutateContext.Transaction.Id,
                Path.Combine("Scenes", "Main.age.scene.json")),
            restoreContext);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(Path.Combine("Scenes", "Main.age.scene.json"), result.Value.RelativePath);
        Assert.True(result.Value.BytesRestored > 0);
        var restoredScene = await sceneStore.LoadAsync(root, "Main", CancellationToken.None);
        var component = Assert.Single(restoredScene.Entities.Single().Components);
        Assert.Equal(4, component.Properties["speed"]!.GetValue<int>());
        var restorePreimage = Assert.Single(restoreContext.Transaction.ResourcePreimages);
        Assert.Contains("\"speed\": 7", restorePreimage.ReadUtf8Text(), StringComparison.Ordinal);
        Assert.Contains(restoreContext.Transaction.ChangedResources, resource =>
            resource.EndsWith("Main.age.scene.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConcurrentTransactionAppendsRetainEveryDistinctEntry()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeTransactionLogStore();
        var transactions = Enumerable.Range(0, 32)
            .Select(index => RekallAgeTransaction.Begin($"concurrent {index:D2}"))
            .ToArray();

        await Task.WhenAll(transactions.Select(transaction =>
            store.AppendAsync(root, transaction, "agent", CancellationToken.None).AsTask()));

        var document = await store.LoadAsync(root, CancellationToken.None);
        Assert.Equal(32, document.Transactions.Count);
        Assert.Equal(
            transactions.Select(transaction => transaction.Id).Order(StringComparer.Ordinal),
            document.Transactions.Select(transaction => transaction.Id).Order(StringComparer.Ordinal));
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "Transactions"), ".transactions.age.json.*"));
    }
}
