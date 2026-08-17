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

    public int MaxContextMessages { get; init; } = 20;

    public int MaxToolResultCharacters { get; init; } = 12_000;
}

public sealed record RekallAgeLanguageModelAgentResult(
    bool Completed,
    string StopReason,
    string FinalContent,
    int Turns,
    int ToolCallCount,
    RekallAgeLanguageModelUsage Usage,
    IReadOnlyList<RekallAgeLanguageModelMessage> Transcript)
{
    public IReadOnlyList<RekallAgeLanguageModelToolExecution> ToolExecutions { get; init; } =
        Array.Empty<RekallAgeLanguageModelToolExecution>();
}

public sealed record RekallAgeLanguageModelToolExecution(
    int Sequence,
    string Name,
    JsonObject Arguments,
    bool Succeeded,
    string ResultPreview);

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
        var maxContextMessages = Math.Clamp(request.MaxContextMessages, 4, 128);
        var maxToolResultCharacters = Math.Clamp(request.MaxToolResultCharacters, 1_000, 100_000);
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
        var toolExecutions = new List<RekallAgeLanguageModelToolExecution>();
        var finalContent = string.Empty;
        for (var turn = 1; turn <= maxTurns; turn++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await modelClient.ChatAsync(
                new RekallAgeLanguageModelRequest(
                    request.Model,
                    BuildContext(transcript, toolExecutions, maxContextMessages),
                    toolExecutor.Tools)
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

                var outputText = output.ToJsonString();
                if (outputText.Length > maxToolResultCharacters)
                {
                    outputText = outputText[..maxToolResultCharacters]
                        + $"\n[tool result truncated at {maxToolResultCharacters} of {outputText.Length} characters; use a narrower inspect tool if more detail is required]";
                }

                var succeeded = output["ok"] is not JsonValue okValue
                    || !okValue.TryGetValue<bool>(out var ok)
                    || ok;
                toolExecutions.Add(new RekallAgeLanguageModelToolExecution(
                    toolCallCount,
                    call.Name,
                    (JsonObject)call.Arguments.DeepClone(),
                    succeeded,
                    outputText.Length <= 1_200 ? outputText : outputText[..1_200] + "…"));
                transcript.Add(new RekallAgeLanguageModelMessage("tool", outputText, call.Name));
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
            transcript.ToArray())
        {
            ToolExecutions = toolExecutions.ToArray()
        };
    }

    private static IReadOnlyList<RekallAgeLanguageModelMessage> BuildContext(
        IReadOnlyList<RekallAgeLanguageModelMessage> transcript,
        IReadOnlyList<RekallAgeLanguageModelToolExecution> executions,
        int maxMessages)
    {
        if (transcript.Count <= maxMessages)
        {
            return transcript.ToArray();
        }

        var prefixCount = Math.Min(2, transcript.Count);
        return transcript.Take(prefixCount)
            .Append(CreateLedgerMessage(executions))
            .Concat(transcript.Skip(transcript.Count - (maxMessages - prefixCount - 1)))
            .ToArray();
    }

    private static RekallAgeLanguageModelMessage CreateLedgerMessage(
        IReadOnlyList<RekallAgeLanguageModelToolExecution> executions)
    {
        var lines = executions.TakeLast(12).Select(execution =>
        {
            var arguments = execution.Arguments.ToJsonString();
            if (arguments.Length > 500)
            {
                arguments = arguments[..500] + "…";
            }

            return $"#{execution.Sequence} {execution.Name} {(execution.Succeeded ? "ok" : "failed")} args={arguments} result={execution.ResultPreview}";
        });
        return new RekallAgeLanguageModelMessage(
            "system",
            "Persistent Rekall tool ledger (older raw tool messages may have been pruned; trust this ledger and inspect current state when uncertain):\n"
            + string.Join('\n', lines));
    }
}
