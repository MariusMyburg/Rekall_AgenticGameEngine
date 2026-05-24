using Rekall.Age.Core.Transactions;

namespace Rekall.Age.Core.Commands;

public sealed record RekallAgeCommandContext(
    string Actor,
    RekallAgeTransaction Transaction,
    CancellationToken CancellationToken);
