using System.Text;

namespace Rekall.Age.Agent.LanguageModels;

public sealed class RekallAgeLanguageModelProviderException : Exception
{
    private const int MaximumMessageCharacters = 4_096;
    private const int MaximumIdentifierCharacters = 128;
    private const string FallbackCode = "REKALL_LANGUAGE_MODEL_PROVIDER_ERROR";
    private const string FallbackProviderId = "unknown";

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
        Code = BuildCode(code, sensitiveValues);
        ProviderId = BuildProviderId(providerId, sensitiveValues);
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

    private static string BuildCode(string? code, IReadOnlyCollection<string>? sensitiveValues)
    {
        if (ContainsSensitiveValue(code, sensitiveValues))
        {
            return FallbackCode;
        }

        var normalized = NormalizeIdentifier(
            code,
            '_',
            upperInvariant: true,
            allowHyphens: false,
            allowDots: false);
        return normalized.Length is > 0 and <= MaximumIdentifierCharacters
            && normalized.StartsWith("REKALL_", StringComparison.Ordinal)
            ? normalized
            : FallbackCode;
    }

    private static string BuildProviderId(string? providerId, IReadOnlyCollection<string>? sensitiveValues)
    {
        if (ContainsSensitiveValue(providerId, sensitiveValues))
        {
            return FallbackProviderId;
        }

        var normalized = NormalizeIdentifier(
            providerId,
            '-',
            upperInvariant: false,
            allowHyphens: true,
            allowDots: true);
        return normalized.Length is > 0 and <= MaximumIdentifierCharacters
            ? normalized
            : FallbackProviderId;
    }

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

    private static bool ContainsSensitiveValue(
        string? value,
        IReadOnlyCollection<string>? sensitiveValues) =>
        value is not null
        && sensitiveValues is not null
        && sensitiveValues.Any(sensitiveValue =>
            !string.IsNullOrEmpty(sensitiveValue)
            && value.Contains(sensitiveValue, StringComparison.Ordinal));

    private static string NormalizeIdentifier(
        string? value,
        char separator,
        bool upperInvariant,
        bool allowHyphens,
        bool allowDots)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var source = upperInvariant
            ? value.Trim().ToUpperInvariant()
            : value.Trim().ToLowerInvariant();
        var normalized = new StringBuilder(Math.Min(source.Length, MaximumIdentifierCharacters + 1));
        foreach (var character in source)
        {
            var allowed = character is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9'
                or '_'
                || (allowHyphens && character == '-')
                || (allowDots && character == '.');
            var next = allowed ? character : separator;
            if (next == separator && normalized.Length > 0 && normalized[^1] == separator)
            {
                continue;
            }

            normalized.Append(next);
            if (normalized.Length > MaximumIdentifierCharacters)
            {
                return string.Empty;
            }
        }

        return allowDots
            ? normalized.ToString().Trim('-', '_', '.')
            : normalized.ToString().Trim('_', '-');
    }
}
