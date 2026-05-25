using Rekall.Age.Core.Commands;

namespace Rekall.Age.Core.Transactions;

public sealed record ListTransactionHistoryRequest(
    string ProjectRoot,
    int Limit = 20);

public sealed record ListTransactionHistoryResult(
    string LogPath,
    IReadOnlyList<RekallAgeTransactionLogEntry> Transactions);

public sealed class ListTransactionHistoryCommand
    : IRekallAgeCommand<ListTransactionHistoryRequest, ListTransactionHistoryResult>
{
    private readonly RekallAgeTransactionLogStore _store;

    public ListTransactionHistoryCommand()
        : this(new RekallAgeTransactionLogStore())
    {
    }

    public ListTransactionHistoryCommand(RekallAgeTransactionLogStore store)
    {
        _store = store;
    }

    public string Name => "rekall.transaction.history";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Lists recent persisted project transactions for agents and Studio.",
        typeof(ListTransactionHistoryRequest).FullName!,
        typeof(ListTransactionHistoryResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ListTransactionHistoryResult>> ExecuteAsync(
        ListTransactionHistoryRequest request,
        RekallAgeCommandContext context)
    {
        var document = await _store.LoadAsync(request.ProjectRoot, context.CancellationToken);
        var limit = request.Limit <= 0 ? int.MaxValue : request.Limit;
        var transactions = document.Transactions
            .Take(limit)
            .ToArray();
        var value = new ListTransactionHistoryResult(_store.GetPath(request.ProjectRoot), transactions);

        return RekallAgeCommandResult<ListTransactionHistoryResult>.Success(
            value,
            $"Loaded {transactions.Length} transaction(s).");
    }
}
