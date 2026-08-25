using System.Text.Json;

namespace Rekall.Age.Agent.Codex;

public static class RekallAgeCodexErrorCodes
{
    public const string RuntimeMissing = "REKALL_CODEX_RUNTIME_MISSING";
    public const string ProtocolUnsupported = "REKALL_CODEX_PROTOCOL_UNSUPPORTED";
    public const string AuthenticationRequired = "REKALL_CODEX_AUTHENTICATION_REQUIRED";
    public const string ModelUnavailable = "REKALL_CODEX_MODEL_UNAVAILABLE";
    public const string ProcessExited = "REKALL_CODEX_PROCESS_EXITED";
    public const string ProtocolInvalid = "REKALL_CODEX_PROTOCOL_INVALID";
    public const string TurnFailed = "REKALL_CODEX_TURN_FAILED";
    public const string Cancelled = "REKALL_CODEX_CANCELLED";
}

public sealed record RekallAgeCodexInitializeResult(
    string UserAgent,
    string PlatformFamily,
    string PlatformOs);

public sealed record RekallAgeCodexAccount(
    string? AuthenticationType,
    bool RequiresOpenAiAuthentication,
    bool IsAuthenticated);

public sealed record RekallAgeCodexReasoningEffort(string Id, string Description);

public sealed record RekallAgeCodexModel(
    string Id,
    string Model,
    string DisplayName,
    bool Hidden,
    bool IsDefault,
    string DefaultReasoningEffort,
    IReadOnlyList<RekallAgeCodexReasoningEffort> SupportedReasoningEfforts);

public sealed record RekallAgeCodexThreadStartRequest(
    string ProjectRoot,
    string Model,
    string DeveloperInstructions)
{
    public string ApprovalPolicy { get; init; } = "on-request";

    public bool NetworkEnabled { get; init; }

    public IReadOnlyList<RekallAgeCodexMcpServer> McpServers { get; init; } = [];
}

public sealed record RekallAgeCodexMcpServer(
    string Name,
    string Command,
    IReadOnlyList<string> Arguments);

public sealed record RekallAgeCodexThread(string Id, string Model, string ProjectRoot);

public sealed record RekallAgeCodexTurn(string ThreadId, string Id, string Status);

public sealed record RekallAgeCodexTurnCompletion(string ThreadId, string TurnId, string Status);

public sealed record RekallAgeCodexNotification(string Method, JsonElement Params);

public sealed record RekallAgeCodexServerRequest(JsonElement Id, string Method, JsonElement Params);

public sealed record RekallAgeCodexDiagnostic(string Code, string Message);

public sealed class RekallAgeCodexAppServerOptions
{
    public string? ExecutablePath { get; init; }

    public string ClientName { get; init; } = "rekall-age";

    public string ClientTitle { get; init; } = "Rekall AGE";

    public string ClientVersion { get; init; } = "1.0.0";

    public int MaximumJsonLineCharacters { get; init; } = 1_048_576;

    public int MaximumStderrCharacters { get; init; } = 16_384;

    public int MaximumPendingRequests { get; init; } = 128;

    public int NotificationCapacity { get; init; } = 256;

    public int ServerRequestCapacity { get; init; } = 32;

    public int DiagnosticCapacity { get; init; } = 64;

    public int ModelPageSize { get; init; } = 100;

    public int MaximumModelPages { get; init; } = 100;

    public TimeSpan InterruptTimeout { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(5);

    internal void Validate()
    {
        ValidateRequired(ClientName, nameof(ClientName));
        ValidateRequired(ClientTitle, nameof(ClientTitle));
        ValidateRequired(ClientVersion, nameof(ClientVersion));
        ValidatePositive(MaximumJsonLineCharacters, nameof(MaximumJsonLineCharacters));
        ValidatePositive(MaximumStderrCharacters, nameof(MaximumStderrCharacters));
        ValidatePositive(MaximumPendingRequests, nameof(MaximumPendingRequests));
        ValidatePositive(NotificationCapacity, nameof(NotificationCapacity));
        ValidatePositive(ServerRequestCapacity, nameof(ServerRequestCapacity));
        ValidatePositive(DiagnosticCapacity, nameof(DiagnosticCapacity));
        ValidatePositive(ModelPageSize, nameof(ModelPageSize));
        ValidatePositive(MaximumModelPages, nameof(MaximumModelPages));

        if (InterruptTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(InterruptTimeout));
        }

        if (ShutdownTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ShutdownTimeout));
        }
    }

    private static void ValidateRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }
    }

    private static void ValidatePositive(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
