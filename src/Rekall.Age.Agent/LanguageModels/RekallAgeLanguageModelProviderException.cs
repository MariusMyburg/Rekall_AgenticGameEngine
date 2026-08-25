namespace Rekall.Age.Agent.LanguageModels;

public sealed class RekallAgeLanguageModelProviderException : Exception
{
    private const int MaximumMessageCharacters = 4_096;

    public RekallAgeLanguageModelProviderException(
        string code,
        string providerId,
        string message,
        int? httpStatus = null,
        string? requestId = null,
        bool retryable = false,
        string? requestedValue = null,
        string? resolvedValue = null,
        IReadOnlyCollection<string>? sensitiveValues = null)
        : base(BuildMessage(message, sensitiveValues))
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        Code = code;
        ProviderId = providerId;
        HttpStatus = httpStatus;
        RequestId = Redact(requestId, sensitiveValues);
        Retryable = retryable;
        RequestedValue = Redact(requestedValue, sensitiveValues);
        ResolvedValue = Redact(resolvedValue, sensitiveValues);
    }

    public string Code { get; }

    public string ProviderId { get; }

    public int? HttpStatus { get; }

    public string? RequestId { get; }

    public bool Retryable { get; }

    public string? RequestedValue { get; }

    public string? ResolvedValue { get; }

    private static string BuildMessage(string message, IReadOnlyCollection<string>? sensitiveValues)
    {
        ArgumentNullException.ThrowIfNull(message);
        var redacted = Redact(message, sensitiveValues)!;
        return redacted.Length <= MaximumMessageCharacters
            ? redacted
            : redacted[..(MaximumMessageCharacters - 1)] + "…";
    }

    private static string? Redact(string? value, IReadOnlyCollection<string>? sensitiveValues)
    {
        if (value is null)
        {
            return null;
        }

        var redacted = value;
        if (sensitiveValues is not null)
        {
            foreach (var sensitiveValue in sensitiveValues.Where(value => !string.IsNullOrEmpty(value)))
            {
                redacted = redacted.Replace(sensitiveValue, "[REDACTED]", StringComparison.Ordinal);
            }
        }

        return redacted;
    }
}
