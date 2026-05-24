namespace Rekall.Age.Core.Commands;

public sealed record RekallAgeDynamicCommandResult(
    bool Ok,
    string Summary,
    object? Value,
    IReadOnlyList<RekallAgeCommandError> Errors);
