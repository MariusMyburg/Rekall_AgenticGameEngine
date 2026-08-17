using System.Text.Json.Nodes;

namespace Rekall.Age.Agent.LanguageModels;

public interface IRekallAgeAgentToolExecutor
{
    IReadOnlyList<RekallAgeLanguageModelTool> Tools { get; }

    ValueTask<JsonNode> ExecuteAsync(string name, JsonObject arguments, CancellationToken cancellationToken);
}

public sealed record RekallAgeLanguageModelAgentRequest(string Model, string SystemPrompt, string Task)
{
    public int MaxTurns { get; init; } = 24;

    public string? Think { get; init; } = "medium";

    public double? Temperature { get; init; }
}

public sealed record RekallAgeLanguageModelAgentResult(
    bool Completed,
    string StopReason,
    string FinalContent,
    int Turns,
    int ToolCallCount,
    RekallAgeLanguageModelUsage Usage,
    IReadOnlyList<RekallAgeLanguageModelMessage> Transcript);

public sealed class RekallAgeLanguageModelAgent(
    IRekallAgeLanguageModelClient modelClient,
    IRekallAgeAgentToolExecutor toolExecutor)
{
    public async ValueTask<RekallAgeLanguageModelAgentResult> RunAsync(
        RekallAgeLanguageModelAgentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Model);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Task);
        var maxTurns = Math.Clamp(request.MaxTurns, 1, 256);
        var transcript = new List<RekallAgeLanguageModelMessage>();
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            transcript.Add(new RekallAgeLanguageModelMessage("system", request.SystemPrompt));
        }

        transcript.Add(new RekallAgeLanguageModelMessage("user", request.Task));
        var promptTokens = 0;
        var completionTokens = 0;
        long totalDuration = 0;
        var toolCallCount = 0;
        var finalContent = string.Empty;
        for (var turn = 1; turn <= maxTurns; turn++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await modelClient.ChatAsync(
                new RekallAgeLanguageModelRequest(request.Model, transcript.ToArray(), toolExecutor.Tools)
                {
                    Think = request.Think,
                    Temperature = request.Temperature
                },
                cancellationToken);
            promptTokens += response.Usage.PromptTokens;
            completionTokens += response.Usage.CompletionTokens;
            totalDuration = checked(totalDuration + response.Usage.TotalDurationNanoseconds);
            finalContent = response.Content;
            transcript.Add(new RekallAgeLanguageModelMessage(
                "assistant",
                response.Content,
                ToolCalls: response.ToolCalls));

            if (response.ToolCalls.Count == 0)
            {
                return Result(true, response.FinishReason.Length == 0 ? "complete" : response.FinishReason, finalContent, turn);
            }

            foreach (var call in response.ToolCalls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                toolCallCount++;
                JsonNode output;
                try
                {
                    output = await toolExecutor.ExecuteAsync(call.Name, call.Arguments, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    output = new JsonObject
                    {
                        ["ok"] = false,
                        ["error"] = exception.Message,
                        ["exceptionType"] = exception.GetType().Name
                    };
                }

                transcript.Add(new RekallAgeLanguageModelMessage("tool", output.ToJsonString(), call.Name));
            }
        }

        return Result(false, "turn_limit", finalContent, maxTurns);

        RekallAgeLanguageModelAgentResult Result(bool completed, string reason, string content, int turns) => new(
            completed,
            reason,
            content,
            turns,
            toolCallCount,
            new RekallAgeLanguageModelUsage(promptTokens, completionTokens, totalDuration),
            transcript.ToArray());
    }
}
