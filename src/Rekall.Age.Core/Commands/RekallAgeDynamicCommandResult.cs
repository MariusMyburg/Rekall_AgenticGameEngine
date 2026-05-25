namespace Rekall.Age.Core.Commands;

public sealed record RekallAgeDynamicCommandResult(
    bool Ok,
    string Summary,
    object? Value,
    IReadOnlyList<RekallAgeCommandError> Errors,
    RekallAgeCommandTransactionSummary Transaction);

public sealed record RekallAgeCommandTransactionSummary(
    string Id,
    string Name,
    string Actor,
    DateTimeOffset StartedAtUtc,
    IReadOnlyList<string> ChangedResources);
