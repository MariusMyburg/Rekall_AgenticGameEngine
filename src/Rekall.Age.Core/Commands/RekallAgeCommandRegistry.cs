using System.Text.Json;
using Rekall.Age.Core.Transactions;

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
            return new RekallAgeDynamicCommandResult(false, error.Message, null, [error], CreateTransactionSummary(context));
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
                var normalizedArgumentsJson = RekallAgeCommandJsonArgumentNormalizer.Normalize(argumentsJson, typeof(TRequest));
                using var document = JsonDocument.Parse(normalizedArgumentsJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new JsonException($"Arguments for command '{Command.Name}' must be a JSON object.");
                }
                var allowedFields = typeof(TRequest).GetProperties()
                    .Where(property => property.GetMethod is not null && property.GetIndexParameters().Length == 0)
                    .Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                var unknownFields = document.RootElement.EnumerateObject()
                    .Select(property => property.Name)
                    .Where(name => !allowedFields.Contains(name, StringComparer.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                if (unknownFields.Length > 0)
                {
                    var unknownNames = string.Join(", ", unknownFields.Select(field => $"'{field}'"));
                    var allowedNames = string.Join(", ", allowedFields.Select(field => $"'{field}'"));
                    var contract = Schema.Description.Length <= 1_000
                        ? Schema.Description
                        : Schema.Description[..1_000] + "…";
                    var message = $"Command '{Command.Name}' received unknown argument field(s): {unknownNames}. "
                        + $"Allowed fields: {allowedNames}. Use the exact allowed names and native JSON arrays/objects for structured values. "
                        + $"Expected command contract: {contract}";
                    var error = new RekallAgeCommandError(
                        "REKALL_COMMAND_ARGUMENT_UNKNOWN",
                        message,
                        Command.Name);
                    return new RekallAgeDynamicCommandResult(
                        false,
                        error.Message,
                        null,
                        [error],
                        CreateTransactionSummary(context));
                }
                var required = typeof(TRequest).GetConstructors()
                    .OrderByDescending(constructor => constructor.GetParameters().Length)
                    .FirstOrDefault()
                    ?.GetParameters()
                    .Where(parameter => !parameter.HasDefaultValue)
                    .Select(parameter => parameter.Name ?? string.Empty)
                    .Where(parameterName => parameterName.Length > 0)
                    .Where(parameterName => !document.RootElement.EnumerateObject().Any(property =>
                        property.Name.Equals(parameterName, StringComparison.OrdinalIgnoreCase)
                        && property.Value.ValueKind is not JsonValueKind.Null))
                    .ToArray() ?? [];
                if (required.Length > 0)
                {
                    var fieldNames = string.Join(", ", required.Select(name => $"'{name}'"));
                    var error = new RekallAgeCommandError(
                        "REKALL_COMMAND_ARGUMENT_REQUIRED",
                        $"Command '{Command.Name}' is missing required argument fields: {fieldNames}.",
                        Command.Name);
                    return new RekallAgeDynamicCommandResult(
                        false,
                        error.Message,
                        null,
                        [error],
                        CreateTransactionSummary(context));
                }
                request = JsonSerializer.Deserialize<TRequest>(normalizedArgumentsJson, JsonOptions)
                    ?? throw new JsonException($"Arguments for command '{Command.Name}' were null.");
            }
            catch (JsonException ex)
            {
                var message = string.IsNullOrWhiteSpace(ex.Path)
                    || ex.Message.Contains(ex.Path, StringComparison.Ordinal)
                    ? ex.Message
                    : $"{ex.Message} Path: {ex.Path}.";
                var contract = Schema.Description.Length <= 1_000
                    ? Schema.Description
                    : Schema.Description[..1_000] + "…";
                message += $" Expected command contract: {contract}";
                var error = new RekallAgeCommandError(
                    "REKALL_COMMAND_ARGUMENTS_INVALID",
                    message,
                    Command.Name);
                return new RekallAgeDynamicCommandResult(false, error.Message, null, [error], CreateTransactionSummary(context));
            }

            try
            {
                var result = await Command.ExecuteAsync(request, context);
                return new RekallAgeDynamicCommandResult(
                    result.Ok,
                    result.Summary,
                    result.Value,
                    result.Errors,
                    CreateTransactionSummary(context));
            }
            catch (RekallAgeCodedBoundaryException ex)
            {
                var error = new RekallAgeCommandError(ex.Code, ex.Message, ex.Target);
                return new RekallAgeDynamicCommandResult(
                    false,
                    ex.Message,
                    null,
                    [error],
                    CreateTransactionSummary(context));
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException)
            {
                var error = new RekallAgeCommandError(
                    "REKALL_COMMAND_EXECUTION_FAILED",
                    ex.Message,
                    Command.Name);
                return new RekallAgeDynamicCommandResult(false, ex.Message, null, [error], CreateTransactionSummary(context));
            }
        }
    }

    private static RekallAgeCommandTransactionSummary CreateTransactionSummary(RekallAgeCommandContext context)
    {
        var projectRoot = RekallAgeTransactionProjectRootResolver.Resolve(context.Transaction.ChangedResources);
        var resourceChanges = projectRoot is null
            ? Array.Empty<RekallAgeTransactionResourceChange>()
            : RekallAgeTransactionResourceChangeSummarizer.Summarize(
                projectRoot,
                context.Transaction.ChangedResources);

        return new RekallAgeCommandTransactionSummary(
            context.Transaction.Id,
            context.Transaction.Name,
            context.Actor,
            context.Transaction.StartedAtUtc,
            context.Transaction.ChangedResources.ToArray(),
            resourceChanges);
    }
}
