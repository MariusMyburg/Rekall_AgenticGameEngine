using System.Text.Json;

namespace Rekall.Age.Core.Commands;

public sealed class RekallAgeCommandRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Dictionary<string, IRekallAgeCommandDescriptor> _commands = new(StringComparer.Ordinal);

    public IReadOnlyList<RekallAgeCommandSchema> Schemas =>
        _commands.Values
            .Select(command => command.Schema)
            .OrderBy(schema => schema.Name, StringComparer.Ordinal)
            .ToArray();

    public IReadOnlyList<RekallAgeRegisteredCommand> RegisteredCommands =>
        _commands.Values
            .Select(command => new RekallAgeRegisteredCommand(
                command.Schema,
                command.RequestType,
                command.ResultType))
            .OrderBy(command => command.Schema.Name, StringComparer.Ordinal)
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

    public async ValueTask<RekallAgeDynamicCommandResult> ExecuteJsonAsync(
        string name,
        string argumentsJson,
        RekallAgeCommandContext context)
    {
        if (!_commands.TryGetValue(name, out var command))
        {
            var error = new RekallAgeCommandError("REKALL_COMMAND_NOT_FOUND", $"Command '{name}' is not registered.");
            return new RekallAgeDynamicCommandResult(false, error.Message, null, [error]);
        }

        context.CancellationToken.ThrowIfCancellationRequested();
        return await command.ExecuteJsonAsync(argumentsJson, context);
    }

    private interface IRekallAgeCommandDescriptor
    {
        RekallAgeCommandSchema Schema { get; }

        Type RequestType { get; }

        Type ResultType { get; }

        ValueTask<RekallAgeDynamicCommandResult> ExecuteJsonAsync(
            string argumentsJson,
            RekallAgeCommandContext context);
    }

    private sealed record RekallAgeCommandDescriptor<TRequest, TResult>(
        IRekallAgeCommand<TRequest, TResult> Command) : IRekallAgeCommandDescriptor
    {
        public RekallAgeCommandSchema Schema => Command.Schema;

        public Type RequestType => typeof(TRequest);

        public Type ResultType => typeof(TResult);

        public async ValueTask<RekallAgeDynamicCommandResult> ExecuteJsonAsync(
            string argumentsJson,
            RekallAgeCommandContext context)
        {
            TRequest request;
            try
            {
                request = JsonSerializer.Deserialize<TRequest>(argumentsJson, JsonOptions)
                    ?? throw new JsonException($"Arguments for command '{Command.Name}' were null.");
            }
            catch (JsonException ex)
            {
                var error = new RekallAgeCommandError(
                    "REKALL_COMMAND_ARGUMENTS_INVALID",
                    ex.Message,
                    Command.Name);
                return new RekallAgeDynamicCommandResult(false, error.Message, null, [error]);
            }

            try
            {
                var result = await Command.ExecuteAsync(request, context);
                return new RekallAgeDynamicCommandResult(result.Ok, result.Summary, result.Value, result.Errors);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException)
            {
                var error = new RekallAgeCommandError(
                    "REKALL_COMMAND_EXECUTION_FAILED",
                    ex.Message,
                    Command.Name);
                return new RekallAgeDynamicCommandResult(false, ex.Message, null, [error]);
            }
        }
    }
}
