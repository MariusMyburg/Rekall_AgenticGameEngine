using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rekall.Age.Agent.LanguageModels;

public interface IRekallAgeLanguageModelClient
{
    string ProviderId { get; }

    ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(CancellationToken cancellationToken);

    ValueTask<RekallAgeLanguageModelResponse> ChatAsync(
        RekallAgeLanguageModelRequest request,
        CancellationToken cancellationToken);
}

public interface IRekallAgeStreamingLanguageModelClient
{
    IAsyncEnumerable<RekallAgeLanguageModelStreamEvent> StreamChatAsync(
        RekallAgeLanguageModelRequest request,
        CancellationToken cancellationToken);
}

public sealed class RekallAgeLanguageModelOpaqueState
{
    public const int MaximumItemCharacters = 4_194_304;
    public const int MaximumTotalCharacters = 8_388_608;
    public const int MaximumItems = 256;

    public RekallAgeLanguageModelOpaqueState(string providerId, IReadOnlyList<string> items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentNullException.ThrowIfNull(items);
        if (providerId.Length > 64 || providerId.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
        {
            throw new ArgumentException("Provider identity is invalid.", nameof(providerId));
        }
        if (items.Count == 0)
        {
            throw new ArgumentException("Opaque provider state requires at least one item.", nameof(items));
        }
        if (items.Count > MaximumItems)
        {
            throw new ArgumentOutOfRangeException(nameof(items), "Opaque provider state has too many items.");
        }

        var copiedItems = new string[items.Count];
        var totalCharacters = 0;
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (string.IsNullOrEmpty(item))
            {
                throw new ArgumentException("Opaque provider state items cannot be empty.", nameof(items));
            }
            if (item.Length > MaximumItemCharacters)
            {
                throw new ArgumentOutOfRangeException(nameof(items), "An opaque provider state item is too large.");
            }

            totalCharacters = checked(totalCharacters + item.Length);
            if (totalCharacters > MaximumTotalCharacters)
            {
                throw new ArgumentOutOfRangeException(nameof(items), "Opaque provider state is too large.");
            }

            copiedItems[index] = item;
        }

        ProviderId = providerId;
        Items = new ReadOnlyCollection<string>(copiedItems);
    }

    public string ProviderId { get; }

    [JsonIgnore]
    public IReadOnlyList<string> Items { get; }

    public override string ToString() =>
        $"Opaque provider state for {ProviderId} ({Items.Count} item(s)).";
}

public sealed record RekallAgeLanguageModelRequest(
    string Model,
    IReadOnlyList<RekallAgeLanguageModelMessage> Messages,
    IReadOnlyList<RekallAgeLanguageModelTool> Tools)
{
    public string? Think { get; init; }

    public double? Temperature { get; init; }

    public string? KeepAlive { get; init; }

    public int? ContextWindowTokens { get; init; }

    public int? MaxOutputTokens { get; init; }
}

public sealed record RekallAgeLanguageModelMessage(
    string Role,
    string Content,
    string? ToolName = null,
    IReadOnlyList<RekallAgeLanguageModelToolCall>? ToolCalls = null)
{
    public string? ToolCallId { get; init; }

    [JsonIgnore]
    public RekallAgeLanguageModelOpaqueState? OpaqueProviderState { get; init; }
}

public sealed record RekallAgeLanguageModelTool(
    string Name,
    string Description,
    JsonObject Parameters);

public sealed record RekallAgeLanguageModelToolCall(string Name, JsonObject Arguments)
{
    public string? Id { get; init; }
}

public sealed record RekallAgeLanguageModelResponse(
    string ProviderId,
    string Model,
    string Content,
    string Thinking,
    IReadOnlyList<RekallAgeLanguageModelToolCall> ToolCalls,
    string FinishReason,
    RekallAgeLanguageModelUsage Usage)
{
    public string? ResponseId { get; init; }

    [JsonIgnore]
    public RekallAgeLanguageModelOpaqueState? OpaqueProviderState { get; init; }
}

public sealed record RekallAgeLanguageModelUsage(
    int PromptTokens,
    int CompletionTokens,
    long TotalDurationNanoseconds)
{
    public int? CachedInputTokens { get; init; }

    public int? ReasoningTokens { get; init; }
}

public sealed record RekallAgeLanguageModelInfo(
    string Id,
    long SizeBytes,
    bool? SupportsTools = null,
    bool? SupportsCompletion = null);

public enum RekallAgeLanguageModelStreamEventKind
{
    TextDelta,
    ThinkingDelta,
    ToolCallDelta,
    Usage,
    Completed
}

public sealed record RekallAgeLanguageModelStreamEvent(
    RekallAgeLanguageModelStreamEventKind Kind,
    string Text,
    RekallAgeLanguageModelResponse? Response = null);
