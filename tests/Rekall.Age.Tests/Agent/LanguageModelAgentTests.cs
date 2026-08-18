using System.Text.Json.Nodes;
using Rekall.Age.Agent.LanguageModels;

namespace Rekall.Age.Tests.Agent;

public sealed class LanguageModelAgentTests
{
    [Fact]
    public void EmbeddedAgentContractOrdersAuthoringEvidenceBeforeDeliverableProof()
    {
        var prompt = RekallAgeEmbeddedAgentContract.SystemPrompt;

        Assert.Contains("repair every deliberate fault", prompt, StringComparison.Ordinal);
        Assert.Contains("PositionDelta2D", prompt, StringComparison.Ordinal);
        Assert.Contains("PositionDelta3D", prompt, StringComparison.Ordinal);
        Assert.Contains("do not reopen authoring after package proof", prompt, StringComparison.Ordinal);
        Assert.True(
            prompt.IndexOf("repair every deliberate fault", StringComparison.Ordinal)
            < prompt.IndexOf("package proof", StringComparison.Ordinal));
    }

    [Fact]
    public void EmbeddedAgentContractPreventsKnownBroadDeliveryWaste()
    {
        var prompt = RekallAgeEmbeddedAgentContract.SystemPrompt;

        Assert.Contains("exact tool is rekall.module.search_component_schemas", prompt, StringComparison.Ordinal);
        Assert.Contains("do not call it more than once", prompt, StringComparison.Ordinal);
        Assert.Contains("every requested visible dynamic body has a renderer", prompt, StringComparison.Ordinal);
        Assert.Contains("scaffold the required playable module before the first packaging call", prompt, StringComparison.Ordinal);
        Assert.Contains("rekall.validation.repair_project", prompt, StringComparison.Ordinal);
        Assert.Contains("immediately after the first complete scene authoring", prompt, StringComparison.Ordinal);
        Assert.Contains("never add new entities merely to exercise validation", prompt, StringComparison.Ordinal);
        Assert.Contains("retry once with the same named scenes and empty entities arrays", prompt, StringComparison.Ordinal);
        Assert.Contains("Never repeat substantially the same failed blueprint arguments", prompt, StringComparison.Ordinal);
        Assert.True(
            prompt.IndexOf("scaffold the required playable module", StringComparison.Ordinal)
            < prompt.IndexOf("first packaging call", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AgentExecutesToolCallsAndReturnsMeasuredFinalResponse()
    {
        var model = new ScriptedModelClient(
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "", [new RekallAgeLanguageModelToolCall("inspect", new JsonObject { ["root"] = "game" })], "tool_calls", new(10, 2, 100)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "Ready", "", [], "stop", new(20, 3, 200)));
        var tools = new RecordingToolExecutor();
        var agent = new RekallAgeLanguageModelAgent(model, tools);

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "You are an engine agent.", "Inspect the game") { MaxTurns = 4 },
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal("Ready", result.FinalContent);
        Assert.Equal(2, result.Turns);
        Assert.Equal(1, result.ToolCallCount);
        Assert.Equal(30, result.Usage.PromptTokens);
        Assert.Equal(5, result.Usage.CompletionTokens);
        Assert.Equal(300, result.Usage.TotalDurationNanoseconds);
        var execution = Assert.Single(tools.Executions);
        Assert.Equal("inspect", execution.Name);
        Assert.Equal("game", execution.Arguments["root"]!.GetValue<string>());
        Assert.Contains(model.Requests[1].Messages, message =>
            message.Role == "tool" && message.ToolName == "inspect" && message.Content.Contains("ready"));
        Assert.DoesNotContain(model.Requests[1].Messages, message =>
            message.Role == "system" && message.Content.StartsWith("Persistent Rekall tool ledger", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AgentStopsAtConfiguredTurnLimit()
    {
        var repeated = new RekallAgeLanguageModelResponse(
            "test", "model", "", "", [new RekallAgeLanguageModelToolCall("inspect", new JsonObject())], "tool_calls", new(1, 1, 1));
        var model = new ScriptedModelClient(repeated, repeated, repeated);
        var agent = new RekallAgeLanguageModelAgent(model, new RecordingToolExecutor());

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task") { MaxTurns = 2 },
            CancellationToken.None);

        Assert.False(result.Completed);
        Assert.Equal("turn_limit", result.StopReason);
        Assert.Equal(2, result.Turns);
    }

    [Fact]
    public async Task AgentContinuesAfterEmptyFinalResponseInsteadOfClaimingCompletion()
    {
        var model = new ScriptedModelClient(
            new RekallAgeLanguageModelResponse(
                "test", "model", "   ", "", [], "stop", new(3, 1, 10)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "All requested evidence is complete.", "", [], "stop", new(4, 2, 20)));
        var agent = new RekallAgeLanguageModelAgent(model, new RecordingToolExecutor());

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task") { MaxTurns = 3 },
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal(2, result.Turns);
        Assert.Equal("All requested evidence is complete.", result.FinalContent);
        Assert.Contains(
            model.Requests[1].Messages,
            message => message.Role == "user" &&
                message.Content.Contains("empty response cannot complete", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AgentRequiresEvidenceAuditBeforeAcceptingCompletion()
    {
        var model = new ScriptedModelClient(
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "", [new RekallAgeLanguageModelToolCall("inspect", new JsonObject())], "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "Everything is complete.", "", [], "stop", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "Evidence is contradictory; repairing.", "", [new RekallAgeLanguageModelToolCall("inspect", new JsonObject { ["repair"] = true })], "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "Everything is now complete.", "", [], "stop", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "Confirmed with direct evidence.", "", [], "stop", new(1, 1, 1)));
        var tools = new RecordingToolExecutor();
        var agent = new RekallAgeLanguageModelAgent(model, tools);

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 5,
                RequireCompletionAudit = true
            },
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal(5, result.Turns);
        Assert.Equal(2, result.ToolCallCount);
        Assert.Equal("Confirmed with direct evidence.", result.FinalContent);
        Assert.Contains(
            model.Requests[2].Messages,
            message => message.Role == "user"
                && message.Content.Contains("Completion audit required", StringComparison.Ordinal));
        Assert.Contains(
            model.Requests[4].Messages,
            message => message.Role == "user"
                && message.Content.Contains("missing components", StringComparison.Ordinal));
        Assert.Contains(
            model.Requests[4].Messages,
            message => message.Role == "user"
                && message.Content.Contains("do not repeat a passing operation", StringComparison.Ordinal));
        Assert.Contains(
            model.Requests[4].Messages,
            message => message.Role == "user"
                && message.Content.Contains("do not relocate an already proven relocated package again", StringComparison.Ordinal));
        Assert.Contains(
            model.Requests[4].Messages,
            message => message.Role == "user"
                && message.Content.Contains("do not redesign or wholesale replace a scene", StringComparison.Ordinal));
        Assert.Contains(
            model.Requests[4].Messages,
            message => message.Role == "user"
                && message.Content.Contains("targeted canonical mutation", StringComparison.Ordinal));
        Assert.Contains(
            model.Requests[4].Messages,
            message => message.Role == "user"
                && message.Content.Contains("do not add new entities merely to exercise validation", StringComparison.Ordinal));
    }

    [Fact]
    public void DiagnosticsExposeBoundedFailedToolResultsInsteadOfOnlyToolNames()
    {
        var failures = RekallAgeLanguageModelAgentDiagnostics.FormatFailures(
        [
            new RekallAgeLanguageModelToolExecution(1, "ready", new JsonObject(), true, "{\"ok\":true}"),
            new RekallAgeLanguageModelToolExecution(
                2,
                "rekall.workflow.create_blueprint_project",
                new JsonObject { ["projectName"] = "Agent Project" },
                false,
                "{\"ok\":false,\"summary\":\"Blueprint properties were invalid.\"}")
        ]);

        Assert.DoesNotContain("#1", failures, StringComparison.Ordinal);
        Assert.Contains("#2 rekall.workflow.create_blueprint_project", failures, StringComparison.Ordinal);
        Assert.Contains("projectName", failures, StringComparison.Ordinal);
        Assert.Contains("Blueprint properties were invalid", failures, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrunedLedgerRetainsOlderDurableEvidenceAlongsideRecentExecutions()
    {
        var calls = new List<RekallAgeLanguageModelToolCall>
        {
            new(
                "rekall.validation.project",
                new JsonObject { ["ProjectRoot"] = "game" })
        };
        calls.AddRange(Enumerable.Range(2, 13).Select(sequence =>
            new RekallAgeLanguageModelToolCall(
                $"noise.{sequence}",
                new JsonObject { ["sequence"] = sequence })));
        var model = new ScriptedModelClient(
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "", calls, "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "Complete", "", [], "stop", new(1, 1, 1)));
        var agent = new RekallAgeLanguageModelAgent(model, new RecordingToolExecutor());

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 2,
                MaxContextMessages = 4
            },
            CancellationToken.None);

        Assert.True(result.Completed);
        var ledger = Assert.Single(model.Requests[1].Messages, message =>
            message.Role == "system"
            && message.Content.StartsWith("Persistent Rekall tool ledger", StringComparison.Ordinal));
        Assert.Contains("#1 rekall.validation.project ok", ledger.Content, StringComparison.Ordinal);
        Assert.Contains("#14 noise.14 ok", ledger.Content, StringComparison.Ordinal);
    }

    private sealed class ScriptedModelClient(params RekallAgeLanguageModelResponse[] responses) : IRekallAgeLanguageModelClient
    {
        private int _index;
        public string ProviderId => "test";
        public List<RekallAgeLanguageModelRequest> Requests { get; } = [];
        public ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<RekallAgeLanguageModelInfo>>([]);
        public ValueTask<RekallAgeLanguageModelResponse> ChatAsync(RekallAgeLanguageModelRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return ValueTask.FromResult(responses[Math.Min(_index++, responses.Length - 1)]);
        }
    }

    private sealed class RecordingToolExecutor : IRekallAgeAgentToolExecutor
    {
        public IReadOnlyList<RekallAgeLanguageModelTool> Tools { get; } =
            [new("inspect", "Inspect", new JsonObject { ["type"] = "object" })];
        public List<(string Name, JsonObject Arguments)> Executions { get; } = [];
        public ValueTask<JsonNode> ExecuteAsync(string name, JsonObject arguments, CancellationToken cancellationToken)
        {
            Executions.Add((name, arguments));
            return ValueTask.FromResult<JsonNode>(new JsonObject { ["ready"] = true });
        }
    }
}
