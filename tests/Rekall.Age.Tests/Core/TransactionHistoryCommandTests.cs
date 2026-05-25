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
    }
}
