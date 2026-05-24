namespace Rekall.Age.Core.Commands;

public sealed record RekallAgeCommandError(
    string Code,
    string Message,
    string? Target = null,
    IReadOnlyList<RekallAgeSuggestedCommand>? SuggestedCommands = null);

public sealed record RekallAgeSuggestedCommand(
    string Tool,
    IReadOnlyDictionary<string, object?> Arguments);

public sealed record RekallAgeCommandResult<TResult>(
    bool Ok,
    string Summary,
    TResult Value,
    IReadOnlyList<RekallAgeCommandError> Errors)
{
    public static RekallAgeCommandResult<TResult> Success(TResult value, string summary = "Command succeeded.")
    {
        return new RekallAgeCommandResult<TResult>(true, summary, value, Array.Empty<RekallAgeCommandError>());
    }

    public static RekallAgeCommandResult<TResult> Failure(
        TResult value,
        string summary,
        IReadOnlyList<RekallAgeCommandError> errors)
    {
        return new RekallAgeCommandResult<TResult>(false, summary, value, errors);
    }
}
