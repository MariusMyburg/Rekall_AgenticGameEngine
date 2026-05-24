namespace Rekall.Age.Core.Commands;

public interface IRekallAgeCommand<TRequest, TResult>
{
    string Name { get; }

    RekallAgeCommandSchema Schema { get; }

    ValueTask<RekallAgeCommandResult<TResult>> ExecuteAsync(
        TRequest request,
        RekallAgeCommandContext context);
}
