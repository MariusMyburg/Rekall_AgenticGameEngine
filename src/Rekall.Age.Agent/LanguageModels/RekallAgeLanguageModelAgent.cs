using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Runtime.Abstractions;

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

    public bool RequireCompletionAuditToolEvidence { get; init; }

    public bool RequireRuntimeBehaviorAssertions { get; init; }

    public int MaxRuntimeBehaviorRepairTurns { get; init; } = 12;

    public int MaxPostRuntimeDeliveryTurns { get; init; } = 16;

    public int MaxPreRuntimeAuthoringMutations { get; init; } = 4;

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
    private const int MaxEncodedStructuredArgumentCharacters = 1_000_000;

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
        var maxPostRuntimeDeliveryTurns = Math.Clamp(request.MaxPostRuntimeDeliveryTurns, 0, 32);
        var maxPreRuntimeAuthoringMutations = Math.Clamp(request.MaxPreRuntimeAuthoringMutations, 0, 32);
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
        var postRuntimeDeliveryReserveActivated = false;
        var runtimeAuthoringCheckpointPrompted = false;
        var requireAgentStateTransitionProof = request.RequireRuntimeBehaviorAssertions
            && RequiresAgentStateTransitionProof(request.Task);
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
                    && RequiresFreshRuntimeBehaviorAssertions(
                        toolExecutions,
                        requireAgentStateTransitionProof,
                        out var runtimeEvidenceMessage))
                {
                    if (requireAgentStateTransitionProof
                        && !runtimeRepairReserveActivated
                        && maxRuntimeBehaviorRepairTurns > 0)
                    {
                        runtimeRepairReserveActivated = true;
                        turnLimit = Math.Max(
                            turnLimit,
                            Math.Min(256, checked(turn + maxRuntimeBehaviorRepairTurns)));
                    }
                    transcript.Add(new RekallAgeLanguageModelMessage("user", runtimeEvidenceMessage));
                    continue;
                }

                if (request.RequireRuntimeBehaviorAssertions
                    && RequiresRuntimeAuthoringCheckpoint(toolExecutions, maxPreRuntimeAuthoringMutations))
                {
                    transcript.Add(new RekallAgeLanguageModelMessage(
                        "user",
                        BuildRuntimeAuthoringCheckpointPrompt(maxPreRuntimeAuthoringMutations)));
                    continue;
                }

                if (request.RequireCompletionAuditToolEvidence && !completionAuditPending)
                {
                    var requiredTools = request.CompletionAuditPrimingTools.Count == 0
                        ? "a configured completion-audit tool"
                        : string.Join(" or ", request.CompletionAuditPrimingTools.Order(StringComparer.Ordinal));
                    transcript.Add(new RekallAgeLanguageModelMessage(
                        "user",
                        $"Strict completion evidence required. A narrative claim cannot complete this task. Successfully call {requiredTools} against the current deliverable, then provide the final evidence-backed response without any intervening tool call. If the audit fails, repair from its direct evidence and rerun it."));
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
            var successfulRuntimeCheckpointThisTurn = false;
            string? repeatedFailureRecovery = null;
            string? packageAuditRecovery = null;
            string? blueprintStructureRecovery = null;
            string? runtimeEvidenceRecovery = null;
            foreach (var call in response.ToolCalls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                completionAuditPending = false;
                toolCallCount++;
                JsonNode output;
                if (request.RequireRuntimeBehaviorAssertions
                    && RequiresRuntimeAuthoringCheckpoint(toolExecutions, maxPreRuntimeAuthoringMutations)
                    && ShouldDeferUntilRuntimeAuthoring(call))
                {
                    output = RuntimeAuthoringCheckpointRequired(call, maxPreRuntimeAuthoringMutations);
                }
                else if (request.RequireRuntimeBehaviorAssertions
                    && RequiresImmediateRuntimeCheckpoint(
                        toolExecutions,
                        requireAgentStateTransitionProof)
                    && ShouldDeferUntilRuntimeCheckpoint(call))
                {
                    output = RuntimeCheckpointRequired(call, requireAgentStateTransitionProof);
                }
                else
                {
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
                }

                var outputText = output.ToJsonString();
                if (outputText.Length > maxToolResultCharacters)
                {
                    outputText = outputText[..maxToolResultCharacters]
                        + $"\n[tool result truncated at {maxToolResultCharacters} of {outputText.Length} characters; use a narrower inspect tool if more detail is required]";
                }

                var executedToolName = CanonicalToolName(call.Name, output);
                var succeeded = output["ok"] is not JsonValue okValue
                    || !okValue.TryGetValue<bool>(out var ok)
                    || ok;
                var execution = new RekallAgeLanguageModelToolExecution(
                    toolCallCount,
                    executedToolName,
                    (JsonObject)call.Arguments.DeepClone(),
                    succeeded,
                    outputText.Length <= 1_200 ? outputText : outputText[..1_200] + "…");
                toolExecutions.Add(execution);
                var identicalFailureCount = CountConsecutiveIdenticalFailures(toolExecutions);
                repeatedFailureRecovery = identicalFailureCount >= 3
                    ? BuildRepeatedFailureRecovery(execution, output, identicalFailureCount)
                    : null;
                if (!succeeded
                    && executedToolName.Equals("rekall.scene.apply_blueprint", StringComparison.Ordinal)
                    && CountRecentFailedToolAttempts(toolExecutions, executedToolName) >= 3)
                {
                    blueprintStructureRecovery =
                        "Stop broad blueprint retries: several recent rekall.scene.apply_blueprint calls have failed. "
                        + "Use one small flat logical-entity repair at a time. The top-level entities field must be a native JSON array of sibling entity objects; each entity has name and a components array; each component is exactly one object containing type and optional properties together. Never nest entity name/id/tags/components inside a component or split type and properties across adjacent objects. Keep every component of the same logical object on that same logical entity: do not create separate FooTransform, FooMesh, FooCollider, or FooState sibling entities. "
                        + "For an existing entity, prefer rekall.component.add or the relevant targeted component/entity mutation when available. Re-inspect the current scene before retrying; preserve already-valid authored content.";
                }
                if (!succeeded
                    && executedToolName.Equals("rekall.runtime.inspect_scene", StringComparison.Ordinal)
                    && CountRecentFailedToolAttempts(toolExecutions, executedToolName) >= 3)
                {
                    runtimeEvidenceRecovery =
                        "Stop runtime evidence-shape retries: several recent rekall.runtime.inspect_scene calls have failed. Do not attach or remove an unrelated component merely to satisfy evidence, weaken the requested assertion, or keep permuting fields. Repair the authored gameplay rule and its scene prerequisites from the actual failure. Keep transform, renderer, collider, and agent-owned state for one logical object on the same entity. EntitiesNamed is case-insensitive exact-name matching, never prefix matching; for numbered or grouped objects use EntitiesWithComponent, EntitiesWithTag, or EntitiesWithTagAndComponent. Then use one exact agent-owned state assertion shape: {\"entityName\":\"Player\",\"subject\":\"changed.component.property\",\"operator\":\"equals\",\"componentType\":\"Game.Modules.Rules.PlayerState\",\"propertyName\":\"Score\",\"expected\":true}. Explicitly author the initial property on that same component, repeat held input for every needed frame, and rerun only after the rule or prerequisite changed.";
                }
                if (!succeeded
                    && executedToolName.Equals(
                        "rekall.workflow.audit_playable_package",
                        StringComparison.Ordinal))
                {
                    packageAuditRecovery = BuildPackageAuditRecovery(output);
                }
                request.Progress?.Report(new RekallAgeLanguageModelAgentProgress(
                    turn,
                    "tool.completed",
                    $"{executedToolName} {(succeeded ? "completed" : "failed")}.",
                    execution));
                transcript.Add(new RekallAgeLanguageModelMessage("tool", outputText, executedToolName));
                if (!succeeded
                    && executedToolName.Equals("rekall.runtime.inspect_scene", StringComparison.Ordinal)
                    && HasRuntimeCheckpointCoverage(call.Arguments))
                {
                    failedRuntimeAssertionThisTurn = true;
                }
                else if (succeeded
                    && executedToolName.Equals("rekall.runtime.inspect_scene", StringComparison.Ordinal)
                    && HasRuntimeCheckpointCoverage(call.Arguments))
                {
                    successfulRuntimeCheckpointThisTurn = true;
                }
                var effectiveToolName = EffectiveToolName(
                    call with { Name = executedToolName },
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

            if (packageAuditRecovery is not null)
            {
                transcript.Add(new RekallAgeLanguageModelMessage("user", packageAuditRecovery));
            }

            if (repeatedFailureRecovery is not null)
            {
                transcript.Add(new RekallAgeLanguageModelMessage("user", repeatedFailureRecovery));
            }

            if (blueprintStructureRecovery is not null)
            {
                transcript.Add(new RekallAgeLanguageModelMessage("user", blueprintStructureRecovery));
            }

            if (runtimeEvidenceRecovery is not null)
            {
                transcript.Add(new RekallAgeLanguageModelMessage("user", runtimeEvidenceRecovery));
            }

            if (request.RequireRuntimeBehaviorAssertions
                && !runtimeAuthoringCheckpointPrompted
                && RequiresRuntimeAuthoringCheckpoint(toolExecutions, maxPreRuntimeAuthoringMutations))
            {
                runtimeAuthoringCheckpointPrompted = true;
                transcript.Add(new RekallAgeLanguageModelMessage(
                    "user",
                    BuildRuntimeAuthoringCheckpointPrompt(maxPreRuntimeAuthoringMutations)));
            }

            if (request.RequireRuntimeBehaviorAssertions
                && !postRuntimeDeliveryReserveActivated
                && maxPostRuntimeDeliveryTurns > 0
                && successfulRuntimeCheckpointThisTurn
                && HasFreshSuccessfulRuntimeCheckpoint(
                    toolExecutions,
                    requireAgentStateTransitionProof))
            {
                var extendedTurnLimit = Math.Min(256, checked(turn + maxPostRuntimeDeliveryTurns));
                if (extendedTurnLimit > turnLimit)
                {
                    postRuntimeDeliveryReserveActivated = true;
                    turnLimit = extendedTurnLimit;
                    transcript.Add(new RekallAgeLanguageModelMessage(
                        "user",
                        $"The executable gameplay checkpoint passed. You now have a protected delivery reserve through turn {turnLimit}. If this task requires a packaged deliverable and no compiled package-proof adapter exists, call rekall.module.scaffold_playable now, before the final build; it is only the generic deterministic package adapter, so keep all requested world gameplay in the runtime-system module. Then perform the final build and refresh runtime proof once because module changes stale the prior checkpoint. Validate the current project, apply only the smallest evidence-backed repairs, then package, capture proof, and audit the deliverable. Do not reopen broad authoring or spend this finite reserve on optional polish."));
                }
            }

            if (failedRuntimeAssertionThisTurn)
            {
                if (!runtimeRepairReserveActivated && maxRuntimeBehaviorRepairTurns > 0)
                {
                    runtimeRepairReserveActivated = true;
                    turnLimit = Math.Max(
                        turnLimit,
                        Math.Min(256, checked(maxTurns + maxRuntimeBehaviorRepairTurns)));
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
                    "Run the first runnable gameplay checkpoint now, before visual polish, broad schema cleanup, packaging, capture, or delivery audit. Use rekall.runtime.inspect_scene with representative semantic input frames, an existence assertion for an attached agent-owned component (an exact non-Rekall.* runtime component identity), and a strict assertion proving either a nonzero transform delta or a changed agent-owned component property. If it fails, repair from the actual bounded values without weakening the assertion and retest immediately. Establish this thin executable vertical slice before expanding or polishing the rest of the game."));
            }
        }

        var limited = Result(false, "turn_limit", finalContent, turnLimit);
        request.Progress?.Report(new RekallAgeLanguageModelAgentProgress(
            turnLimit,
            "run.stopped",
            runtimeRepairReserveActivated
                ? $"Agent reached the {turnLimit}-turn limit after using its protected runtime repair reserve."
                : postRuntimeDeliveryReserveActivated
                    ? $"Agent reached the {turnLimit}-turn limit after using its protected post-runtime delivery reserve."
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

    private static int CountConsecutiveIdenticalFailures(
        IReadOnlyList<RekallAgeLanguageModelToolExecution> executions)
    {
        if (executions.Count == 0 || executions[^1].Succeeded)
        {
            return 0;
        }

        var latest = executions[^1];
        var count = 0;
        for (var index = executions.Count - 1; index >= 0; index--)
        {
            var candidate = executions[index];
            if (candidate.Succeeded
                || !candidate.Name.Equals(latest.Name, StringComparison.Ordinal)
                || !JsonNode.DeepEquals(candidate.Arguments, latest.Arguments))
            {
                break;
            }

            count++;
        }

        return count;
    }

    private static int CountRecentFailedToolAttempts(
        IReadOnlyList<RekallAgeLanguageModelToolExecution> executions,
        string toolName) => executions
        .Where(execution => execution.Name.Equals(toolName, StringComparison.Ordinal))
        .TakeLast(6)
        .Count(execution => !execution.Succeeded);

    private static string BuildRepeatedFailureRecovery(
        RekallAgeLanguageModelToolExecution execution,
        JsonNode output,
        int identicalFailureCount)
    {
        var suggestedActions = output["value"]?["nextActions"]
            ?? output["nextActions"];
        var actionText = suggestedActions is null
            ? "No structured recovery action was returned; inspect the failure facts and make a different targeted call."
            : $"Execute one of these engine-returned recovery actions exactly: {suggestedActions.ToJsonString()}";
        if (actionText.Length > 4_000)
        {
            actionText = actionText[..4_000] + "…";
        }

        return
            $"The same failed tool call has now been attempted three consecutive times or more (current count: {identicalFailureCount}): "
            + $"{execution.Name} args={execution.Arguments.ToJsonString()}. "
            + "Do not call it again with the same arguments until another operation changes the relevant state. "
            + actionText
            + " Rekall AGE has not executed that recovery action for you; you must select and call it.";
    }

    private static string BuildPackageAuditRecovery(JsonNode output)
    {
        var summary = output["summary"]?.GetValue<string>() ?? "Package audit failed.";
        var firstError = output["errors"] is JsonArray errors && errors.Count > 0
            ? errors[0]?["message"]?.GetValue<string>()
            : null;
        var reason = string.IsNullOrWhiteSpace(firstError)
            ? summary
            : $"{summary} {firstError}";
        if (reason.Length > 1_200)
        {
            reason = reason[..1_200] + "…";
        }

        return
            $"Package audit recovery required. Direct evidence: {reason} "
            + "Re-read the original task and repair only its requested entities, visuals, HUD, and behavior. "
            + "Do not add generic Cube/Test/Demo/Fault entities or unrelated validation/showcase filler merely to change pixels or satisfy a metric. "
            + "Use exact registered generic component schemas to improve the requested content itself. "
            + "After any scene or module mutation, obtain clean validation, rerun the original task's runtime assertions for every requested transition, rebuild if source changed, create a fresh package, and audit that fresh package. "
            + "The failed or now-stale package is not completion evidence.";
    }

    private static bool RequiresImmediateRuntimeCheckpoint(
        IReadOnlyList<RekallAgeLanguageModelToolExecution> executions,
        bool requireAgentStateTransition)
    {
        var latestRuntimeAuthoring = executions
            .Where(execution => execution.Succeeded
                && (execution.Name.Equals("rekall.module.scaffold_runtime_system", StringComparison.Ordinal)
                    || execution.Name.Equals("rekall.module.write_source", StringComparison.Ordinal)))
            .Select(execution => execution.Sequence)
            .DefaultIfEmpty(0)
            .Max();
        if (latestRuntimeAuthoring == 0)
        {
            return false;
        }

        var latestSuccessfulBuild = executions
            .Where(execution => execution.Succeeded
                && execution.Sequence > latestRuntimeAuthoring
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
            && execution.Name.Equals("rekall.runtime.inspect_scene", StringComparison.Ordinal)
            && HasRuntimeCheckpointCoverage(execution.Arguments, requireAgentStateTransition));
    }

    private static bool RequiresRuntimeAuthoringCheckpoint(
        IReadOnlyList<RekallAgeLanguageModelToolExecution> executions,
        int maxPreRuntimeAuthoringMutations)
    {
        if (maxPreRuntimeAuthoringMutations <= 0
            || executions.Any(execution => execution.Succeeded
                && (execution.Name.Equals("rekall.module.scaffold_runtime_system", StringComparison.Ordinal)
                    || execution.Name.Equals("rekall.module.write_source", StringComparison.Ordinal))))
        {
            return false;
        }

        return executions.Count(execution => execution.Succeeded && IsWorldAuthoringMutation(execution.Name))
            >= maxPreRuntimeAuthoringMutations;
    }

    private static bool IsWorldAuthoringMutation(string toolName) =>
        toolName.Equals("rekall.scene.apply_blueprint", StringComparison.Ordinal)
        || toolName.StartsWith("rekall.entity.", StringComparison.Ordinal)
        || toolName.StartsWith("rekall.component.", StringComparison.Ordinal);

    private static bool ShouldDeferUntilRuntimeAuthoring(RekallAgeLanguageModelToolCall call)
    {
        var toolName = CanonicalPolicyToolName(call.Name);
        return !toolName.Equals("rekall.tools.search", StringComparison.Ordinal)
            && !toolName.StartsWith("rekall.context.", StringComparison.Ordinal)
            && !toolName.StartsWith("rekall.compatibility.", StringComparison.Ordinal)
            && !toolName.StartsWith("rekall.module.", StringComparison.Ordinal)
            && !toolName.Equals("rekall.build.modules", StringComparison.Ordinal);
    }

    private static JsonObject RuntimeAuthoringCheckpointRequired(
        RekallAgeLanguageModelToolCall call,
        int maxPreRuntimeAuthoringMutations)
    {
        var message =
            $"Tool '{call.Name}' is deferred after {maxPreRuntimeAuthoringMutations} successful world-authoring mutations until the required agent-owned runtime module slice begins.";
        return new JsonObject
        {
            ["ok"] = false,
            ["summary"] = message,
            ["errors"] = new JsonArray(new JsonObject
            {
                ["code"] = "REKALL_RUNTIME_AUTHORING_CHECKPOINT_REQUIRED",
                ["message"] = message,
                ["target"] = "rekall.module.scaffold_runtime_system"
            }),
            ["requiredScaffoldShape"] = new JsonObject
            {
                ["projectRoot"] = "<open project root>",
                ["moduleId"] = "game.rules",
                ["displayName"] = "Game Rules",
                ["moduleName"] = "GameRules",
                ["componentName"] = "GameState",
                ["systemName"] = "GameRulesSystem"
            },
            ["instruction"] = "Inspect the runtime SDK if needed, then call rekall.module.scaffold_runtime_system with every required field. List and read the returned source, make targeted edits, and build the module. Do not continue scene polish, validation, capture, or packaging before this thin executable slice begins."
        };
    }

    private static string BuildRuntimeAuthoringCheckpointPrompt(int maxPreRuntimeAuthoringMutations) =>
        $"You have used {maxPreRuntimeAuthoringMutations} successful world-authoring mutations without beginning the required runtime module. Establish the thin executable gameplay slice now. Inspect the runtime SDK if needed, call rekall.module.scaffold_runtime_system with projectRoot, moduleId, displayName, moduleName, componentName, and systemName, then list/read/edit/build that source. Further scene polish, validation, capture, packaging, and delivery are deferred until runtime-module authoring begins.";

    private static JsonObject RuntimeCheckpointRequired(
        RekallAgeLanguageModelToolCall call,
        bool requireAgentStateTransition)
    {
        var attemptedInspection = CanonicalPolicyToolName(call.Name)
            .Equals("rekall.runtime.inspect_scene", StringComparison.Ordinal);
        var hasAssertions = attemptedInspection && HasNonemptyArrayArgument(call.Arguments, "assertions");
        var coverage = EvaluateRuntimeCheckpointCoverage(call.Arguments);
        var missing = coverage.Missing
            .Concat(requireAgentStateTransition && !coverage.AgentStateTransition
                ? ["changed agent-owned component property assertion for requested stateful gameplay"]
                : Array.Empty<string>())
            .ToArray();
        var code = !attemptedInspection
            ? "REKALL_RUNTIME_CHECKPOINT_REQUIRED"
            : !hasAssertions
                ? "REKALL_RUNTIME_ASSERTIONS_REQUIRED"
                : "REKALL_RUNTIME_CHECKPOINT_COVERAGE_REQUIRED";
        var message = !attemptedInspection
            ? $"Tool '{call.Name}' is deferred until the first executable gameplay checkpoint passes or returns direct repair evidence."
            : !hasAssertions
                ? "The first executable gameplay checkpoint requires representative input frames and a non-empty assertions array."
                : $"Runtime checkpoint coverage is incomplete. Missing: {string.Join(", ", missing)}.";
        var candidateAgentComponentAssertion = BuildCandidateAgentComponentAssertion(call.Arguments);
        return new JsonObject
        {
            ["ok"] = false,
            ["summary"] = message,
            ["coverage"] = new JsonObject
            {
                ["inputs"] = coverage.Inputs,
                ["agentComponent"] = coverage.AgentComponent,
                ["transition"] = coverage.Transition,
                ["agentStateTransition"] = coverage.AgentStateTransition,
                ["missing"] = new JsonArray(missing
                    .Select(item => (JsonNode?)JsonValue.Create(item))
                    .ToArray())
            },
            ["requiredAgentComponentAssertionShape"] = new JsonObject
            {
                ["entityName"] = "<runtime entity name>",
                ["subject"] = "component",
                ["operator"] = "exists",
                ["componentType"] = "<exact attached agent-owned non-Rekall.* type>"
            },
            ["requiredSemanticInputFrameShape"] = new JsonObject
            {
                ["semanticActions"] = new JsonArray(new JsonObject
                {
                    ["name"] = "move.horizontal",
                    ["value"] = 1,
                    ["isDown"] = true
                })
            },
            ["candidateAgentComponentAssertion"] = candidateAgentComponentAssertion,
            ["errors"] = new JsonArray(new JsonObject
            {
                ["code"] = code,
                ["message"] = message,
                ["target"] = "rekall.runtime.inspect_scene"
            }),
            ["instruction"] = "Call rekall.runtime.inspect_scene now with effective input frames. To drive a semantic action consumed by InputActionValue/IsInputActionDown, inject the exact action name declared by Rekall.InputActionMap with this copyable shape: {\"inputs\":[{\"semanticActions\":[{\"name\":\"move.horizontal\",\"value\":1,\"isDown\":true}]}]}. Do not invent flat fields such as move_horizontal; unknown fields are ignored by the typed runtime frame and do not count as input. Raw-device arrays such as pressedKeys remain supported. The agent-owned existence assertion must put the runtime entity name in entityName and the exact attached non-Rekall.* component identity in componentType. Both scaffold-qualified names such as Game.Modules.Rules.PlayerState and exact authored CLR identities such as PlayerState are accepted: {\"entityName\":\"Player\",\"subject\":\"component\",\"operator\":\"exists\",\"componentType\":\"PlayerState\"}. Also include a transition assertion using an exact subject: {\"entityName\":\"Player\",\"subject\":\"delta.position3d.x\",\"operator\":\"greater-than\",\"expected\":0}; or use delta.position2d.x/y, delta.position3d.x/y/z, delta.component.property with componentType set to the agent-owned identity strictly compared with 0, or changed.component.property with that componentType equals true."
                + (requireAgentStateTransition
                    ? " This task requests stateful gameplay, so also prove an agent-owned property actually changes with delta.component.property compared strictly with zero or changed.component.property equals true; movement or a static component.property assertion cannot unlock delivery. Author the initial numeric value explicitly on the scene component for numeric deltas, and repeat held semantic input samples for every simulated frame needed to reach contact because inputs[i] applies only to frame i."
                    : string.Empty)
                + " The intuitive delta.transform.position2d/3d axis aliases are accepted and normalized. Do not substitute an engine-owned Rekall.* component, put a component type in entityName, omit componentType, or weaken a failed transition assertion. A failed qualifying assertion opens targeted repair work; unrelated discovery, validation, polish, capture, and packaging remain deferred until this checkpoint executes."
        };
    }

    private static JsonObject? BuildCandidateAgentComponentAssertion(JsonObject arguments)
    {
        if (GetArrayArgument(arguments, "assertions") is not { } assertions)
        {
            return null;
        }

        var values = assertions.OfType<JsonObject>().ToArray();
        var entityName = values
            .Where(assertion =>
                GetString(assertion, "subject").Equals("component", StringComparison.OrdinalIgnoreCase)
                && GetString(assertion, "operator").Equals("exists", StringComparison.OrdinalIgnoreCase))
            .Select(assertion => GetString(assertion, "entityName"))
            .FirstOrDefault(value => value.Length > 0);
        var componentType = values
            .Select(assertion => GetString(assertion, "componentType"))
            .FirstOrDefault(IsAgentOwnedComponent)
            ?? values
                .Select(assertion => GetString(assertion, "entityName"))
                .FirstOrDefault(value => value.StartsWith("Game.", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(entityName) || string.IsNullOrWhiteSpace(componentType))
        {
            return null;
        }

        return new JsonObject
        {
            ["entityName"] = entityName,
            ["subject"] = "component",
            ["operator"] = "exists",
            ["componentType"] = componentType
        };
    }

    private static bool ShouldDeferUntilRuntimeCheckpoint(RekallAgeLanguageModelToolCall call)
    {
        var toolName = CanonicalPolicyToolName(call.Name);
        if (toolName.Equals("rekall.runtime.inspect_scene", StringComparison.Ordinal))
        {
            return !HasRuntimeCheckpointCoverage(call.Arguments);
        }

        return !IsRuntimeCheckpointPreparationTool(toolName);
    }

    private static bool IsRuntimeCheckpointPreparationTool(string toolName) =>
        toolName.Equals("rekall.tools.search", StringComparison.Ordinal)
        || toolName.Equals("rekall.context.project_summary", StringComparison.Ordinal)
        || toolName.Equals("rekall.context.scene_summary", StringComparison.Ordinal)
        || toolName.Equals("rekall.workflow.create_blueprint_project", StringComparison.Ordinal)
        || toolName.StartsWith("rekall.scene.", StringComparison.Ordinal)
        || toolName.StartsWith("rekall.entity.", StringComparison.Ordinal)
        || toolName.StartsWith("rekall.component.", StringComparison.Ordinal)
        || toolName.Equals("rekall.module.search_component_schemas", StringComparison.Ordinal)
        || toolName.Equals("rekall.module.component_schemas", StringComparison.Ordinal)
        || toolName.Equals("rekall.module.inspect_runtime_sdk", StringComparison.Ordinal)
        || toolName.Equals("rekall.module.list_sources", StringComparison.Ordinal)
        || toolName.Equals("rekall.module.read_source", StringComparison.Ordinal)
        || toolName.Equals("rekall.module.write_source", StringComparison.Ordinal)
        || toolName.Equals("rekall.build.modules", StringComparison.Ordinal);

    private static string CanonicalToolName(string attemptedName, JsonNode output) =>
        output["toolNameCorrection"]?["canonical"] is JsonValue canonical
        && canonical.TryGetValue<string>(out var corrected)
        && !string.IsNullOrWhiteSpace(corrected)
            ? corrected
            : CanonicalPolicyToolName(attemptedName);

    private static string CanonicalPolicyToolName(string toolName) =>
        toolName.StartsWith("rekal.", StringComparison.Ordinal)
            ? "rekall." + toolName["rekal.".Length..]
            : toolName;

    private static bool RequiresFreshRuntimeBehaviorAssertions(
        IReadOnlyList<RekallAgeLanguageModelToolExecution> executions,
        bool requireAgentStateTransition,
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
            && HasRuntimeCheckpointCoverage(execution.Arguments, requireAgentStateTransition));
        if (passingAssertionEvidence)
        {
            return false;
        }

        var stateRequirement = requireAgentStateTransition
            ? " This task explicitly requests stateful gameplay, so movement and a static component.property value are insufficient: at least one passing assertion must prove a changed agent-owned component property through delta.component.property compared strictly with zero or changed.component.property equals true. Explicitly author the initial numeric property on the scene component before using delta.component.property; CLR class defaults and helper fallbacks do not create persisted initial assertion state. inputs[i] drives only frame i, so repeat held semantic samples for every simulated frame needed to reach the requested contact or transition."
            : string.Empty;
        message =
            "Deterministic gameplay evidence is still required. You authored a runtime-system module, so narrative source inspection, a successful build, zero validation issues, soak, capture, package, and package audit cannot complete the task by themselves. Call rekall.runtime.inspect_scene after the latest scene/module mutation with representative authored input frames. Minimum passing runtime behavior assertions are: an exists assertion for an attached agent-owned component using its exact non-Rekall.* runtime identity, plus a strict assertion proving either a nonzero transform delta or a changed agent-owned component property."
            + stateRequirement
            + " Require additional passing assertions for the exact requested progress/contact, completion/HUD, and reset transitions. Subjects include component, component.property, delta.component.property, changed.component.property, transform.position2d.x/y, transform.position3d.x/y/z, delta.position2d.x/y, and delta.position3d.x/y/z. Do not weaken a failed assertion merely to make it pass; repair the authored behavior from the actual bounded values and rerun the same intended transition. Do not claim completion until qualifying runtime evidence passes after the latest mutation.";
        return true;
    }

    private static bool RequiresAgentStateTransitionProof(string task)
    {
        string[] statefulTerms =
        [
            "collect", "contact", "collision", "score", "progress", "reset", "restart",
            "health", "damage", "inventory", "quest", "timer", "cooldown", "spawn", "destroy"
        ];
        return statefulTerms.Any(term => task.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasFreshSuccessfulRuntimeCheckpoint(
        IReadOnlyList<RekallAgeLanguageModelToolExecution> executions,
        bool requireAgentStateTransition)
    {
        var latestMutation = executions
            .Where(execution => execution.Succeeded && InvalidatesRuntimeBehaviorEvidence(execution.Name))
            .Select(execution => execution.Sequence)
            .DefaultIfEmpty(0)
            .Max();
        return executions.Any(execution =>
            execution.Succeeded
            && execution.Sequence > latestMutation
            && execution.Name.Equals("rekall.runtime.inspect_scene", StringComparison.Ordinal)
            && HasRuntimeCheckpointCoverage(execution.Arguments, requireAgentStateTransition));
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
        return GetArrayArgument(arguments, name) is { Count: > 0 };
    }

    private static bool HasRuntimeCheckpointCoverage(
        JsonObject arguments,
        bool requireAgentStateTransition = false)
    {
        var coverage = EvaluateRuntimeCheckpointCoverage(arguments);
        return coverage.Inputs
            && coverage.AgentComponent
            && coverage.Transition
            && (!requireAgentStateTransition || coverage.AgentStateTransition);
    }

    private static RuntimeCheckpointCoverage EvaluateRuntimeCheckpointCoverage(JsonObject arguments)
    {
        var hasInputs = HasEffectiveRuntimeInputs(arguments);
        if (GetArrayArgument(arguments, "assertions") is not { } assertions)
        {
            return new RuntimeCheckpointCoverage(hasInputs, false, false, false);
        }

        var hasAgentComponent = assertions.OfType<JsonObject>().Any(assertion =>
            GetString(assertion, "subject").Equals("component", StringComparison.OrdinalIgnoreCase)
            && GetString(assertion, "operator").Equals("exists", StringComparison.OrdinalIgnoreCase)
            && IsAgentOwnedComponent(GetString(assertion, "componentType")));
        var hasTransition = assertions.OfType<JsonObject>().Any(IsMeaningfulRuntimeTransition);
        var hasAgentStateTransition = assertions.OfType<JsonObject>().Any(IsMeaningfulAgentStateTransition);
        return new RuntimeCheckpointCoverage(
            hasInputs,
            hasAgentComponent,
            hasTransition,
            hasAgentStateTransition);
    }

    private static bool HasEffectiveRuntimeInputs(JsonObject arguments)
    {
        if (GetArrayArgument(arguments, "inputs") is not { } inputs)
        {
            return false;
        }

        string[] rawArrayNames =
        [
            "pressedKeys",
            "pressedKeysThisFrame",
            "releasedKeysThisFrame",
            "pressedButtons",
            "pressedButtonsThisFrame",
            "releasedButtonsThisFrame",
            "xrPoses",
            "xrActions"
        ];
        string[] rawScalarNames =
        [
            "mouseX",
            "mouseY",
            "mouseDeltaX",
            "mouseDeltaY",
            "mouseWheelDelta"
        ];

        foreach (var frame in inputs.OfType<JsonObject>())
        {
            if (rawArrayNames.Any(name => GetArrayArgument(frame, name) is { Count: > 0 }))
            {
                return true;
            }

            if (rawScalarNames.Any(name =>
                    GetArgument(frame, name) is { } value
                    && TryGetNumber(value, out var number)
                    && Math.Abs(number) > double.Epsilon))
            {
                return true;
            }

            if (GetArrayArgument(frame, "semanticActions") is { } semanticActions
                && semanticActions.OfType<JsonObject>().Any(sample =>
                    !string.IsNullOrWhiteSpace(GetString(sample, "name"))))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMeaningfulRuntimeTransition(JsonObject assertion)
    {
        if (IsMeaningfulAgentStateTransition(assertion))
        {
            return true;
        }

        var subject = RekallAgeRuntimeAssertionSubjects.Normalize(GetString(assertion, "subject"));
        var comparison = GetString(assertion, "operator").ToLowerInvariant();
        var expected = GetArgument(assertion, "expected");
        var isTransformDelta = subject is "delta.position2d.x" or "delta.position2d.y"
            or "delta.position3d.x" or "delta.position3d.y" or "delta.position3d.z";
        return isTransformDelta
            && comparison is "greater-than" or "less-than"
            && expected is not null
            && TryGetNumber(expected, out var threshold)
            && Math.Abs(threshold) <= double.Epsilon;
    }

    private static bool IsMeaningfulAgentStateTransition(JsonObject assertion)
    {
        var subject = RekallAgeRuntimeAssertionSubjects.Normalize(GetString(assertion, "subject"));
        var comparison = GetString(assertion, "operator").ToLowerInvariant();
        var expected = GetArgument(assertion, "expected");
        if (subject == "changed.component.property")
        {
            return IsAgentOwnedComponent(GetString(assertion, "componentType"))
                && comparison == "equals"
                && expected is JsonValue changed
                && changed.TryGetValue<bool>(out var changedValue)
                && changedValue;
        }

        return subject == "delta.component.property"
            && IsAgentOwnedComponent(GetString(assertion, "componentType"))
            && comparison is "greater-than" or "less-than"
            && expected is not null
            && TryGetNumber(expected, out var threshold)
            && Math.Abs(threshold) <= double.Epsilon;
    }

    private static bool IsAgentOwnedComponent(string componentType) =>
        !string.IsNullOrWhiteSpace(componentType)
        && !componentType.StartsWith("Rekall.", StringComparison.OrdinalIgnoreCase);

    private static JsonNode? GetArgument(JsonObject arguments, string name) =>
        arguments.FirstOrDefault(property =>
            property.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    private static JsonArray? GetArrayArgument(JsonObject arguments, string name)
    {
        var node = GetArgument(arguments, name);
        if (node is JsonArray array)
        {
            return array;
        }

        if (node is not JsonValue scalar
            || !scalar.TryGetValue<string>(out var encoded)
            || encoded.Length > MaxEncodedStructuredArgumentCharacters)
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(encoded) as JsonArray;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GetString(JsonObject arguments, string name) =>
        GetArgument(arguments, name) is JsonValue value
        && value.TryGetValue<string>(out var text)
            ? text.Trim()
            : string.Empty;

    private static bool TryGetNumber(JsonNode node, out double number) =>
        double.TryParse(node.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
        && double.IsFinite(number);

    private sealed record RuntimeCheckpointCoverage(
        bool Inputs,
        bool AgentComponent,
        bool Transition,
        bool AgentStateTransition)
    {
        public IReadOnlyList<string> Missing => new[]
        {
            (Inputs, "effective raw-device input or declared semanticActions input"),
            (AgentComponent, "component/exists assertion for attached agent-owned state"),
            (Transition, "strict nonzero transform delta or changed agent-owned property assertion")
        }
        .Where(item => !item.Item1)
        .Select(item => item.Item2)
        .ToArray();
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
