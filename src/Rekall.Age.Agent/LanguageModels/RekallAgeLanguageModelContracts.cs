using System.Text.Json.Nodes;

namespace Rekall.Age.Agent.LanguageModels;

public interface IRekallAgeLanguageModelClient
{
    string ProviderId { get; }

    ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(CancellationToken cancellationToken);

    ValueTask<RekallAgeLanguageModelResponse> ChatAsync(
        RekallAgeLanguageModelRequest request,
        CancellationToken cancellationToken);
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
    IReadOnlyList<RekallAgeLanguageModelToolCall>? ToolCalls = null);

public sealed record RekallAgeLanguageModelTool(
    string Name,
    string Description,
    JsonObject Parameters);

public sealed record RekallAgeLanguageModelToolCall(string Name, JsonObject Arguments);

public sealed record RekallAgeLanguageModelResponse(
    string ProviderId,
    string Model,
    string Content,
    string Thinking,
    IReadOnlyList<RekallAgeLanguageModelToolCall> ToolCalls,
    string FinishReason,
    RekallAgeLanguageModelUsage Usage);

public sealed record RekallAgeLanguageModelUsage(
    int PromptTokens,
    int CompletionTokens,
    long TotalDurationNanoseconds);

public sealed record RekallAgeLanguageModelInfo(string Id, long SizeBytes);
