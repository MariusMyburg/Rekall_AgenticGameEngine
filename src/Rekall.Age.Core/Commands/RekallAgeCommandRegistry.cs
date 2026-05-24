namespace Rekall.Age.Core.Commands;

public sealed class RekallAgeCommandRegistry
{
    private readonly Dictionary<string, IRekallAgeCommandDescriptor> _commands = new(StringComparer.Ordinal);

    public IReadOnlyList<RekallAgeCommandSchema> Schemas =>
        _commands.Values
            .Select(command => command.Schema)
            .OrderBy(schema => schema.Name, StringComparer.Ordinal)
            .ToArray();

    public void Register<TRequest, TResult>(IRekallAgeCommand<TRequest, TResult> command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!_commands.TryAdd(command.Name, new RekallAgeCommandDescriptor<TRequest, TResult>(command)))
        {
            throw new InvalidOperationException($"Command '{command.Name}' is already registered.");
        }
    }

    public async ValueTask<RekallAgeCommandResult<TResult>> ExecuteAsync<TRequest, TResult>(
        string name,
        TRequest request,
        RekallAgeCommandContext context)
    {
        if (!_commands.TryGetValue(name, out var command))
        {
            var error = new RekallAgeCommandError("REKALL_COMMAND_NOT_FOUND", $"Command '{name}' is not registered.");
            return RekallAgeCommandResult<TResult>.Failure(default!, error.Message, [error]);
        }

        if (command is not RekallAgeCommandDescriptor<TRequest, TResult> typed)
        {
            var error = new RekallAgeCommandError(
                "REKALL_COMMAND_TYPE_MISMATCH",
                $"Command '{name}' was called with incompatible request or result types.");
            return RekallAgeCommandResult<TResult>.Failure(default!, error.Message, [error]);
        }

        context.CancellationToken.ThrowIfCancellationRequested();
        return await typed.Command.ExecuteAsync(request, context);
    }

    private interface IRekallAgeCommandDescriptor
    {
        RekallAgeCommandSchema Schema { get; }
    }

    private sealed record RekallAgeCommandDescriptor<TRequest, TResult>(
        IRekallAgeCommand<TRequest, TResult> Command) : IRekallAgeCommandDescriptor
    {
        public RekallAgeCommandSchema Schema => Command.Schema;
    }
}
