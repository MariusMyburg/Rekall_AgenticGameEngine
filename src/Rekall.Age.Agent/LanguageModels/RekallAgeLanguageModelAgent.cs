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

    public bool RequireCompletionAudit { get; init; }

    public bool RequireRuntimeBehaviorAssertions { get; init; }

    public int MaxRuntimeBehaviorRepairTurns { get; init; } = 12;

    public IProgress<RekallAgeLanguageModelAgentProgress>? Progress { get; init; }

    public IReadOnlySet<string> TerminalSuccessTools { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    public IReadOnlySet<string> CompletionAuditPrimingTools { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}

public sealed record RekallAgeLanguageModelAgentProgress(
    int Turn,
    string Phase,
    string Message,
    RekallAgeLanguageModelToolExecution? ToolExecution = null);

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

public static class RekallAgeLanguageModelAgentDiagnostics
{
    public static string FormatFailures(
        IReadOnlyList<RekallAgeLanguageModelToolExecution> executions,
        int maxFailures = 12,
        int maxPreviewCharacters = 1_200)
    {
        ArgumentNullException.ThrowIfNull(executions);
        maxFailures = Math.Clamp(maxFailures, 1, 64);
        maxPreviewCharacters = Math.Clamp(maxPreviewCharacters, 100, 4_000);
        var lines = executions
            .Where(execution => !execution.Succeeded)
            .TakeLast(maxFailures)
            .Select(execution =>
            {
                var arguments = execution.Arguments.ToJsonString();
                if (arguments.Length > 600)
                {
                    arguments = arguments[..600] + "…";
                }
                var preview = execution.ResultPreview.Length <= maxPreviewCharacters
                    ? execution.ResultPreview
                    : execution.ResultPreview[..maxPreviewCharacters] + "…";
                return $"#{execution.Sequence} {execution.Name} args={arguments}: {preview}";
            })
            .ToArray();
        return lines.Length == 0
            ? string.Empty
            : "Failed tool execution details:\n" + string.Join('\n', lines);
    }
}

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
        var maxRuntimeBehaviorRepairTurns = Math.Clamp(request.MaxRuntimeBehaviorRepairTurns, 0, 64);
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
        var completionAuditPending = false;
        var runtimeCheckpointPrompted = false;
        var runtimeRepairReserveActivated = false;
        var turnLimit = maxTurns;
        for (var turn = 1; turn <= turnLimit; turn++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request.Progress?.Report(new RekallAgeLanguageModelAgentProgress(
                turn,
                "turn.started",
                $"Running agent turn {turn} of {turnLimit}."));
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
                if (string.IsNullOrWhiteSpace(response.Content))
                {
                    transcript.Add(new RekallAgeLanguageModelMessage(
                        "user",
                        "Your empty response cannot complete the task. Continue from the tool ledger, verify every requested outcome, and return a concrete evidence-backed final response only when the work is complete."));
                    continue;
                }

                if (request.RequireRuntimeBehaviorAssertions
                    && RequiresFreshRuntimeBehaviorAssertions(toolExecutions, out var runtimeEvidenceMessage))
                {
                    transcript.Add(new RekallAgeLanguageModelMessage("user", runtimeEvidenceMessage));
                    continue;
                }

                if (request.RequireCompletionAudit && !completionAuditPending)
                {
                    completionAuditPending = true;
                    transcript.Add(new RekallAgeLanguageModelMessage(
                        "user",
                        "Completion audit required. Treat your preceding response only as a proposal, not as accepted completion. Re-read the original task and direct tool evidence, and verify every explicit requirement. Zero counts, warnings or validation issues, missing components or artifacts, stale package proof after later mutations, and evidence that proves mere existence rather than the requested behavior are failures. Reuse direct passing evidence that remains current: do not repeat a passing operation merely to make it newer, do not recreate a passing package, and do not relocate an already proven relocated package again. Only call tools when a requirement is missing, contradicted, stale because of a later mutation, or not directly evidenced. During audit, do not redesign or wholesale replace a scene, and do not add new entities merely to exercise validation. Repair only the explicit failed requirement with the smallest targeted canonical mutation on existing relevant content using exact registered schemas; then rerun only evidence made stale by that mutation. If every requirement is directly proven, return the final evidence-backed response again. Do not rely on your prior narrative."));
                    continue;
                }

                var completed = Result(true, response.FinishReason.Length == 0 ? "complete" : response.FinishReason, finalContent, turn);
                request.Progress?.Report(new RekallAgeLanguageModelAgentProgress(
                    turn,
                    "run.completed",
                    $"Agent completed after {turn} turns and {toolCallCount} tool calls."));
                return completed;
            }

            var failedRuntimeAssertionThisTurn = false;
            foreach (var call in response.ToolCalls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                completionAuditPending = false;
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
                var execution = new RekallAgeLanguageModelToolExecution(
                    toolCallCount,
                    call.Name,
                    (JsonObject)call.Arguments.DeepClone(),
                    succeeded,
                    outputText.Length <= 1_200 ? outputText : outputText[..1_200] + "…");
                toolExecutions.Add(execution);
                request.Progress?.Report(new RekallAgeLanguageModelAgentProgress(
                    turn,
                    "tool.completed",
                    $"{call.Name} {(succeeded ? "completed" : "failed")}.",
                    execution));
                transcript.Add(new RekallAgeLanguageModelMessage("tool", outputText, call.Name));
                if (!succeeded
                    && call.Name.Equals("rekall.runtime.inspect_scene", StringComparison.Ordinal)
                    && HasNonemptyArrayArgument(call.Arguments, "assertions"))
                {
                    failedRuntimeAssertionThisTurn = true;
                }
                var effectiveToolName = EffectiveToolName(
                    call,
                    request.TerminalSuccessTools,
                    request.CompletionAuditPrimingTools);
                if (succeeded && request.CompletionAuditPrimingTools.Contains(effectiveToolName))
                {
                    completionAuditPending = true;
                }

                if (succeeded && request.TerminalSuccessTools.Contains(effectiveToolName))
                {
                    var summary = output["summary"]?.GetValue<string>();
                    finalContent = string.IsNullOrWhiteSpace(summary)
                        ? $"Terminal workflow '{effectiveToolName}' completed successfully."
                        : summary;
                    var terminal = Result(true, "terminal_tool_success", finalContent, turn);
                    request.Progress?.Report(new RekallAgeLanguageModelAgentProgress(
                        turn,
                        "run.completed",
                        $"Terminal workflow {effectiveToolName} completed successfully."));
                    return terminal;
                }
            }

            if (failedRuntimeAssertionThisTurn)
            {
                if (!runtimeRepairReserveActivated && maxRuntimeBehaviorRepairTurns > 0)
                {
                    runtimeRepairReserveActivated = true;
                    turnLimit = checked(maxTurns + maxRuntimeBehaviorRepairTurns);
                }

                runtimeCheckpointPrompted = true;
                transcript.Add(new RekallAgeLanguageModelMessage(
                    "user",
                    $"The executable gameplay checkpoint failed. Treat the returned assertion results and actual bounded values as direct repair evidence. You now have a protected repair-and-retest reserve through turn {turnLimit}. Make the smallest targeted scene or module correction, rebuild only if source changed, and rerun representative input with non-empty assertions immediately. Do not spend this reserve on polish, broad discovery, packaging, capture, or repeated validation until the failed gameplay transition passes."));
            }
            else if (request.RequireRuntimeBehaviorAssertions
                && !runtimeCheckpointPrompted
                && ShouldPromptFirstRuntimeCheckpoint(toolExecutions))
            {
                runtimeCheckpointPrompted = true;
                transcript.Add(new RekallAgeLanguageModelMessage(
                    "user",
                    "Run the first runnable gameplay checkpoint now, before visual polish, broad schema cleanup, packaging, capture, or delivery audit. Use rekall.runtime.inspect_scene with representative semantic input frames and non-empty assertions for the attached agent-owned state plus the first requested transition. If it fails, repair from the actual bounded values and retest immediately. Establish this thin executable vertical slice before expanding or polishing the rest of the game."));
            }
        }

        var limited = Result(false, "turn_limit", finalContent, turnLimit);
        request.Progress?.Report(new RekallAgeLanguageModelAgentProgress(
            turnLimit,
            "run.stopped",
            runtimeRepairReserveActivated
                ? $"Agent reached the {turnLimit}-turn limit after using its protected runtime repair reserve."
                : $"Agent reached the {turnLimit}-turn limit."));
        return limited;

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

    private static bool ShouldPromptFirstRuntimeCheckpoint(
        IReadOnlyList<RekallAgeLanguageModelToolExecution> executions)
    {
        var authoredRuntime = executions.Any(execution =>
            execution.Succeeded
            && (execution.Name.Equals("rekall.module.scaffold_runtime_system", StringComparison.Ordinal)
                || execution.Name.Equals("rekall.module.write_source", StringComparison.Ordinal)));
        if (!authoredRuntime)
        {
            return false;
        }

        var latestSuccessfulBuild = executions
            .Where(execution => execution.Succeeded
                && execution.Name.Equals("rekall.build.modules", StringComparison.Ordinal))
            .Select(execution => execution.Sequence)
            .DefaultIfEmpty(0)
            .Max();
        if (latestSuccessfulBuild == 0)
        {
            return false;
        }

        return !executions.Any(execution =>
            execution.Sequence > latestSuccessfulBuild
            && execution.Name.Equals("rekall.runtime.inspect_scene", StringComparison.Ordinal));
    }

    private static bool RequiresFreshRuntimeBehaviorAssertions(
        IReadOnlyList<RekallAgeLanguageModelToolExecution> executions,
        out string message)
    {
        message = string.Empty;
        var scaffoldedRuntime = executions.Any(execution =>
            execution.Succeeded
            && execution.Name.Equals("rekall.module.scaffold_runtime_system", StringComparison.Ordinal));
        var inspectedRuntimeSdk = executions.Any(execution =>
            execution.Succeeded
            && execution.Name.Equals("rekall.module.inspect_runtime_sdk", StringComparison.Ordinal));
        var wroteModuleSource = executions.Any(execution =>
            execution.Succeeded
            && execution.Name.Equals("rekall.module.write_source", StringComparison.Ordinal));
        if (!scaffoldedRuntime && !(inspectedRuntimeSdk && wroteModuleSource))
        {
            return false;
        }

        var latestMutation = executions
            .Where(execution => execution.Succeeded && InvalidatesRuntimeBehaviorEvidence(execution.Name))
            .Select(execution => execution.Sequence)
            .DefaultIfEmpty(0)
            .Max();
        var passingAssertionEvidence = executions.Any(execution =>
            execution.Succeeded
            && execution.Sequence > latestMutation
            && execution.Name.Equals("rekall.runtime.inspect_scene", StringComparison.Ordinal)
            && HasNonemptyArrayArgument(execution.Arguments, "assertions"));
        if (passingAssertionEvidence)
        {
            return false;
        }

        message =
            "Deterministic gameplay evidence is still required. You authored a runtime-system module, so narrative source inspection, a successful build, zero validation issues, soak, capture, package, and package audit cannot complete the task by themselves. Call rekall.runtime.inspect_scene after the latest scene/module mutation with representative authored input frames and a non-empty assertions array. Require passing runtime behavior assertions for the exact relevant entities: attached agent-owned gameplay components, semantic-input movement or other requested state transition, progress/contact state, completion/HUD state when requested, and reset state when requested. Subjects are entity, visible, component, component.property, transform.position2d.x/y, transform.position3d.x/y/z, delta.position2d.x/y, and delta.position3d.x/y/z. Operators are exists, not-exists, equals, not-equals, contains, greater-than, greater-than-or-equal, less-than, and less-than-or-equal. A failed assertion is direct repair evidence. Do not claim completion until at least one assertion-bearing runtime inspection passes after the latest mutation.";
        return true;
    }

    private static bool InvalidatesRuntimeBehaviorEvidence(string toolName) =>
        toolName.Equals("rekall.module.scaffold_runtime_system", StringComparison.Ordinal)
        || toolName.Equals("rekall.module.write_source", StringComparison.Ordinal)
        || toolName.Equals("rekall.build.modules", StringComparison.Ordinal)
        || toolName.Equals("rekall.scene.apply_blueprint", StringComparison.Ordinal)
        || toolName.Equals("rekall.validation.repair_project", StringComparison.Ordinal)
        || toolName.StartsWith("rekall.entity.", StringComparison.Ordinal)
        || toolName.StartsWith("rekall.component.", StringComparison.Ordinal);

    private static bool HasNonemptyArrayArgument(JsonObject arguments, string name)
    {
        var value = arguments.FirstOrDefault(property =>
            property.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
        return value is JsonArray array && array.Count > 0;
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
        var durableEvidence = executions
            .Where(execution => execution.Succeeded && IsDurableEvidenceTool(execution.Name))
            .GroupBy(
                execution => execution.Name + "\n" + execution.Arguments.ToJsonString(),
                StringComparer.Ordinal)
            .Select(group => group.Last())
            .TakeLast(12);
        var selectedExecutions = durableEvidence
            .Concat(executions.TakeLast(12))
            .GroupBy(execution => execution.Sequence)
            .Select(group => group.First())
            .OrderBy(execution => execution.Sequence);
        var lines = selectedExecutions.Select(execution =>
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

    private static bool IsDurableEvidenceTool(string name) =>
        name.StartsWith("rekall.validation.", StringComparison.Ordinal)
        || name.StartsWith("rekall.runtime.inspect", StringComparison.Ordinal)
        || name.StartsWith("rekall.build.", StringComparison.Ordinal)
        || name.StartsWith("rekall.workflow.package_", StringComparison.Ordinal)
        || name.StartsWith("rekall.workflow.audit_", StringComparison.Ordinal)
        || name.StartsWith("rekall.workflow.relocate_", StringComparison.Ordinal)
        || name.StartsWith("rekall.workflow.capture_", StringComparison.Ordinal)
        || name.StartsWith("rekall.workflow.run_", StringComparison.Ordinal);

    private static string EffectiveToolName(
        RekallAgeLanguageModelToolCall call,
        IReadOnlySet<string> terminalTools,
        IReadOnlySet<string> completionAuditPrimingTools)
    {
        if ((terminalTools.Contains(call.Name) || completionAuditPrimingTools.Contains(call.Name)))
        {
            return call.Name;
        }

        if (call.Arguments["name"] is JsonValue target
            && target.TryGetValue<string>(out var targetName)
            && (terminalTools.Contains(targetName) || completionAuditPrimingTools.Contains(targetName)))
        {
            return targetName;
        }

        return call.Name;
    }
}
