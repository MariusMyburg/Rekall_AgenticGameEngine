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
        Assert.Contains("scaffold rekall.module.scaffold_playable early", prompt, StringComparison.Ordinal);
        Assert.Contains("generic deterministic package-proof adapter", prompt, StringComparison.Ordinal);
        Assert.Contains("rekall.module.scaffold_runtime_system", prompt, StringComparison.Ordinal);
        Assert.Contains("rekall.module.inspect_runtime_sdk", prompt, StringComparison.Ordinal);
        Assert.Contains("module source topology", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ComponentNumber", prompt, StringComparison.Ordinal);
        Assert.Contains("entity.Transform.Position3D", prompt, StringComparison.Ordinal);
        Assert.Contains("do not introduce JsonObject", prompt, StringComparison.Ordinal);
        Assert.Contains("two separate semantic scalar actions", prompt, StringComparison.Ordinal);
        Assert.Contains("InputActionValue returns double", prompt, StringComparison.Ordinal);
        Assert.Contains("does not create semantic bindings", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("register every agent-owned component", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never scaffold that module again", prompt, StringComparison.Ordinal);
        Assert.Contains("non-empty assertions array", prompt, StringComparison.Ordinal);
        Assert.Contains("first runnable gameplay checkpoint", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("before visual polish", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("package audit does not prove world gameplay", prompt, StringComparison.Ordinal);
        Assert.Contains("call the matched native tool directly", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("then call them through rekall.tools.execute", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not replace its compilable SDK types", prompt, StringComparison.Ordinal);
        Assert.Contains("not a substitute for world gameplay", prompt, StringComparison.Ordinal);
        Assert.Contains("rekall.validation.repair_project", prompt, StringComparison.Ordinal);
        Assert.Contains("immediately after the first complete scene authoring", prompt, StringComparison.Ordinal);
        Assert.Contains("never add new entities merely to exercise validation", prompt, StringComparison.Ordinal);
        Assert.Contains("retry once with the same named scenes and empty entities arrays", prompt, StringComparison.Ordinal);
        Assert.Contains("Never repeat substantially the same failed blueprint arguments", prompt, StringComparison.Ordinal);
        Assert.True(
            prompt.IndexOf("scaffold rekall.module.scaffold_playable early", StringComparison.Ordinal)
            < prompt.IndexOf("rekall.workflow.package_playable_game", StringComparison.Ordinal));
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
    public async Task AgentReportsBoundedTurnAndToolProgress()
    {
        var model = new ScriptedModelClient(
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "", [new RekallAgeLanguageModelToolCall("inspect", new JsonObject())], "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse("test", "model", "Done", "", [], "stop", new(1, 1, 1)));
        var progress = new RecordingProgress<RekallAgeLanguageModelAgentProgress>();
        var agent = new RekallAgeLanguageModelAgent(model, new RecordingToolExecutor());

        await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 2,
                Progress = progress
            },
            CancellationToken.None);

        Assert.Contains(progress.Values, item => item.Phase == "turn.started" && item.Turn == 1);
        Assert.Contains(progress.Values, item => item.Phase == "tool.completed" && item.ToolExecution?.Name == "inspect");
        Assert.Contains(progress.Values, item => item.Phase == "run.completed" && item.Turn == 2);
    }

    [Fact]
    public async Task SuccessfulConfiguredTerminalWorkflowStopsWithoutAnotherModelTurn()
    {
        var model = new ScriptedModelClient(new RekallAgeLanguageModelResponse(
            "test", "model", "", "",
            [new RekallAgeLanguageModelToolCall("gateway", new JsonObject
            {
                ["name"] = "rekall.workflow.agent_authoring_gauntlet",
                ["arguments"] = new JsonObject()
            })],
            "tool_calls", new(1, 1, 1)));
        var agent = new RekallAgeLanguageModelAgent(model, new RecordingToolExecutor());

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 8,
                TerminalSuccessTools = new HashSet<string>(["rekall.workflow.agent_authoring_gauntlet"], StringComparer.Ordinal)
            },
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal("terminal_tool_success", result.StopReason);
        Assert.Equal(1, result.Turns);
        Assert.Single(model.Requests);
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
    public async Task AgentRequiresFreshPassingRuntimeAssertionsAfterAuthoringWorldBehavior()
    {
        var model = new ScriptedModelClient(
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.module.scaffold_runtime_system", new JsonObject())],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "The gameplay is complete.", "", [], "stop", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.runtime.inspect_scene", new JsonObject
                {
                    ["assertions"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["entityName"] = "Player",
                            ["subject"] = "component",
                            ["operator"] = "exists",
                            ["componentType"] = "Game.PlayerState"
                        }
                    }
                })],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "The asserted gameplay is complete.", "", [], "stop", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "Confirmed from direct runtime assertions.", "", [], "stop", new(1, 1, 1)));
        var agent = new RekallAgeLanguageModelAgent(model, new RecordingToolExecutor());

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 5,
                RequireCompletionAudit = true,
                RequireRuntimeBehaviorAssertions = true
            },
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal(5, result.Turns);
        Assert.Contains(
            model.Requests[2].Messages,
            message => message.Role == "user"
                && message.Content.Contains("passing runtime behavior assertions", StringComparison.Ordinal));
        Assert.Contains(
            model.Requests[4].Messages,
            message => message.Role == "user"
                && message.Content.Contains("Completion audit required", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SuccessfulRuntimeBuildPromptsAnImmediateGameplayCheckpoint()
    {
        var model = new ScriptedModelClient(
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.module.scaffold_runtime_system", new JsonObject())],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.build.modules", new JsonObject())],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.runtime.inspect_scene", new JsonObject
                {
                    ["assertions"] = new JsonArray(new JsonObject
                    {
                        ["entityName"] = "Player",
                        ["subject"] = "component",
                        ["operator"] = "exists",
                        ["componentType"] = "Game.PlayerState"
                    })
                })],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "Gameplay is proven.", "", [], "stop", new(1, 1, 1)));
        var agent = new RekallAgeLanguageModelAgent(model, new RecordingToolExecutor());

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 4,
                RequireRuntimeBehaviorAssertions = true
            },
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Contains(
            model.Requests[2].Messages,
            message => message.Role == "user"
                && message.Content.Contains("first runnable gameplay checkpoint", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RuntimeBuildBlocksUnrelatedWorkUntilAssertionCheckpointRuns()
    {
        var model = new ScriptedModelClient(
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.module.scaffold_runtime_system", new JsonObject())],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.build.modules", new JsonObject())],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.validation.project", new JsonObject())],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.runtime.inspect_scene", new JsonObject
                {
                    ["inputs"] = new JsonArray(new JsonObject { ["pressedKeys"] = new JsonArray("D") }),
                    ["assertions"] = new JsonArray(new JsonObject
                    {
                        ["entityName"] = "Player",
                        ["subject"] = "delta.position3d.x",
                        ["operator"] = "greater-than",
                        ["expected"] = 0
                    })
                })],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "Gameplay is proven.", "", [], "stop", new(1, 1, 1)));
        var tools = new RecordingToolExecutor();
        var agent = new RekallAgeLanguageModelAgent(model, tools);

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 5,
                RequireRuntimeBehaviorAssertions = true
            },
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.DoesNotContain(tools.Executions, execution => execution.Name == "rekall.validation.project");
        Assert.Contains(result.ToolExecutions, execution =>
            execution.Name == "rekall.validation.project"
            && !execution.Succeeded
            && execution.ResultPreview.Contains("REKALL_RUNTIME_CHECKPOINT_REQUIRED", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailedRuntimeAssertionUnlocksProtectedRepairAndRetestTurns()
    {
        var assertionArguments = new JsonObject
        {
            ["assertions"] = new JsonArray(new JsonObject
            {
                ["entityName"] = "Player",
                ["subject"] = "component",
                ["operator"] = "exists",
                ["componentType"] = "Game.PlayerState"
            })
        };
        var model = new ScriptedModelClient(
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.module.scaffold_runtime_system", new JsonObject())],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.runtime.inspect_scene", assertionArguments)],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.module.write_source", new JsonObject())],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.runtime.inspect_scene", assertionArguments)],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "Gameplay now passes.", "", [], "stop", new(1, 1, 1)));
        var agent = new RekallAgeLanguageModelAgent(model, new FailsFirstRuntimeAssertionToolExecutor());

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 3,
                MaxRuntimeBehaviorRepairTurns = 3,
                RequireRuntimeBehaviorAssertions = true
            },
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal(5, result.Turns);
        Assert.Contains(
            model.Requests[2].Messages,
            message => message.Role == "user"
                && message.Content.Contains("protected repair", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SuccessfulDeliveryAuditPrimesTheNextEvidenceBackedFinalResponse()
    {
        var model = new ScriptedModelClient(
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.workflow.audit_playable_package", new JsonObject())],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "The playable package and its evidence are complete.", "", [], "stop", new(1, 1, 1)));
        var agent = new RekallAgeLanguageModelAgent(model, new RecordingToolExecutor());

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 2,
                RequireCompletionAudit = true,
                CompletionAuditPrimingTools = new HashSet<string>(
                    ["rekall.workflow.audit_playable_package"],
                    StringComparer.Ordinal)
            },
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal(2, result.Turns);
        Assert.Equal("The playable package and its evidence are complete.", result.FinalContent);
        Assert.DoesNotContain(
            model.Requests[1].Messages,
            message => message.Role == "user"
                && message.Content.Contains("Completion audit required", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ToolUseAfterPrimedDeliveryAuditInvalidatesTheCompletionProof()
    {
        var model = new ScriptedModelClient(
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.workflow.audit_playable_package", new JsonObject())],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "Inspecting one more detail.", "",
                [new RekallAgeLanguageModelToolCall("inspect", new JsonObject())],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "Everything is complete.", "", [], "stop", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "Confirmed after the fresh audit.", "", [], "stop", new(1, 1, 1)));
        var agent = new RekallAgeLanguageModelAgent(model, new RecordingToolExecutor());

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 4,
                RequireCompletionAudit = true,
                CompletionAuditPrimingTools = new HashSet<string>(
                    ["rekall.workflow.audit_playable_package"],
                    StringComparer.Ordinal)
            },
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal(4, result.Turns);
        Assert.Equal("Confirmed after the fresh audit.", result.FinalContent);
        Assert.Contains(
            model.Requests[3].Messages,
            message => message.Role == "user"
                && message.Content.Contains("Completion audit required", StringComparison.Ordinal));
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

    private sealed class FailsFirstRuntimeAssertionToolExecutor : IRekallAgeAgentToolExecutor
    {
        private bool _failed;

        public IReadOnlyList<RekallAgeLanguageModelTool> Tools { get; } =
            [new("inspect", "Inspect", new JsonObject { ["type"] = "object" })];

        public ValueTask<JsonNode> ExecuteAsync(string name, JsonObject arguments, CancellationToken cancellationToken)
        {
            if (name.Equals("rekall.runtime.inspect_scene", StringComparison.Ordinal) && !_failed)
            {
                _failed = true;
                return ValueTask.FromResult<JsonNode>(new JsonObject
                {
                    ["ok"] = false,
                    ["summary"] = "Runtime inspection completed, but one behavior assertion failed."
                });
            }

            return ValueTask.FromResult<JsonNode>(new JsonObject { ["ok"] = true });
        }
    }

    private sealed class RecordingProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];
        public void Report(T value) => Values.Add(value);
    }
}
