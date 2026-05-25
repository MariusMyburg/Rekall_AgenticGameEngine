using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;

namespace Rekall.Age.Tests.Core;

public sealed class TransactionHistoryCommandTests
{
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
    }
}
