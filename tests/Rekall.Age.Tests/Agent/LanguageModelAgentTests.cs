using System.Text.Json.Nodes;
using Rekall.Age.Agent.LanguageModels;

namespace Rekall.Age.Tests.Agent;

public sealed class LanguageModelAgentTests
{
    [Fact]
    public void EmbeddedAgentContractExpandsOrdinaryUserIntentWithoutInventingRequirements()
    {
        var prompt = RekallAgeEmbeddedAgentContract.SystemPrompt;

        Assert.Contains("ordinary authoritative product intent", prompt, StringComparison.Ordinal);
        Assert.Contains("do not require the user to supply engine tool names", prompt, StringComparison.Ordinal);
        Assert.Contains("rekall.asset.search_remote_images", prompt, StringComparison.Ordinal);
        Assert.Contains("rekall.asset.import_remote", prompt, StringComparison.Ordinal);
        Assert.Contains("distinct-time frames", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not add unrelated gameplay requirements", prompt, StringComparison.Ordinal);
        Assert.Contains("unrotated camera faces +Z", prompt, StringComparison.Ordinal);
    }

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
        Assert.Contains("WithPosition2D", prompt, StringComparison.Ordinal);
        Assert.Contains("WithPosition3D", prompt, StringComparison.Ordinal);
        Assert.Contains("match the authored transform dimension", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not introduce JsonObject", prompt, StringComparison.Ordinal);
        Assert.Contains("two separate semantic scalar actions", prompt, StringComparison.Ordinal);
        Assert.Contains("InputActionValue returns double", prompt, StringComparison.Ordinal);
        Assert.Contains("does not create semantic bindings", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("register every agent-owned component", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bare world.Update... call is a gameplay no-op", prompt, StringComparison.Ordinal);
        Assert.Contains("continue from updatedWorld", prompt, StringComparison.Ordinal);
        Assert.Contains("never mutate an outer world inside an entity-update callback", prompt, StringComparison.Ordinal);
        Assert.Contains("never scaffold that module again", prompt, StringComparison.Ordinal);
        Assert.Contains("non-empty assertions array", prompt, StringComparison.Ordinal);
        Assert.Contains("first runnable gameplay checkpoint", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("before visual polish", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("package audit does not prove world gameplay", prompt, StringComparison.Ordinal);
        Assert.Contains("native tool directly", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("when false, call rekall.tools.execute", prompt, StringComparison.Ordinal);
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
        Assert.All(model.Requests, request => Assert.Equal(65_536, request.ContextWindowTokens));
        Assert.All(model.Requests, request => Assert.Equal(8_192, request.MaxOutputTokens));
        var execution = Assert.Single(tools.Executions);
        Assert.Equal("inspect", execution.Name);
        Assert.Equal("game", execution.Arguments["root"]!.GetValue<string>());
        Assert.Contains(model.Requests[1].Messages, message =>
            message.Role == "tool" && message.ToolName == "inspect" && message.Content.Contains("ready"));
        Assert.DoesNotContain(model.Requests[1].Messages, message =>
            message.Role == "system" && message.Content.StartsWith("Persistent Rekall tool ledger", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1, 1, 4_096, 512)]
    [InlineData(1_000_000, 1_000_000, 262_144, 65_536)]
    public async Task AgentClampsProviderTokenBudgets(
        int requestedContextTokens,
        int requestedOutputTokens,
        int expectedContextTokens,
        int expectedOutputTokens)
    {
        var model = new ScriptedModelClient(new RekallAgeLanguageModelResponse(
            "test", "model", "Done", "", [], "stop", new(1, 1, 1)));
        var agent = new RekallAgeLanguageModelAgent(model, new RecordingToolExecutor());

        await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 1,
                ContextWindowTokens = requestedContextTokens,
                MaxOutputTokens = requestedOutputTokens
            },
            CancellationToken.None);

        var request = Assert.Single(model.Requests);
        Assert.Equal(expectedContextTokens, request.ContextWindowTokens);
        Assert.Equal(expectedOutputTokens, request.MaxOutputTokens);
    }

    [Fact]
    public async Task AgentUsesLowReasoningForActionRecoveryAfterOutputLimit()
    {
        var model = new ScriptedModelClient(
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "long unfinished reasoning", [], "length", new(10, 8_192, 100)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "", [new RekallAgeLanguageModelToolCall("inspect", new JsonObject())], "tool_calls", new(10, 2, 100)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "Ready", "", [], "stop", new(10, 2, 100)));
        var agent = new RekallAgeLanguageModelAgent(model, new RecordingToolExecutor());

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 3,
                Think = "medium"
            },
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal(["medium", "low", "medium"], model.Requests.Select(request => request.Think));
        Assert.Contains(model.Requests[1].Messages, message =>
            message.Role == "user"
            && message.Content.Contains("immediately call the single next tool", StringComparison.Ordinal));
        Assert.Equal([8_192, 2_048, 8_192], model.Requests.Select(request => request.MaxOutputTokens));
    }

    [Fact]
    public async Task AgentRecoversTimedOutTurnWithLowReasoningAndSmallerActionBudget()
    {
        var model = new TimeoutThenActionModelClient();
        var progress = new RecordingProgress<RekallAgeLanguageModelAgentProgress>();
        var agent = new RekallAgeLanguageModelAgent(model, new RecordingToolExecutor());

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 3,
                Think = "medium",
                MaxTurnDuration = TimeSpan.FromMilliseconds(100),
                Progress = progress
            },
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal(["medium", "low", "medium"], model.Requests.Select(request => request.Think));
        Assert.Equal([8_192, 2_048, 8_192], model.Requests.Select(request => request.MaxOutputTokens));
        Assert.Contains(model.Requests[1].Messages, message =>
            message.Role == "user"
            && message.Content.Contains("exceeded the per-turn deadline", StringComparison.Ordinal));
        Assert.Contains(progress.Values, item => item.Phase == "turn.timeout");
    }

    [Fact]
    public async Task AgentEnforcesTurnDeadlineWhenProviderIgnoresCancellation()
    {
        var model = new NonCooperativeTimeoutThenActionModelClient();
        var agent = new RekallAgeLanguageModelAgent(model, new RecordingToolExecutor());

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 3,
                MaxTurnDuration = TimeSpan.FromMilliseconds(100)
            },
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal(3, model.Requests.Count);
    }

    [Fact]
    public async Task AgentPropagatesProviderSelfCancellationAsProviderFailure()
    {
        var agent = new RekallAgeLanguageModelAgent(
            new SelfCancellingModelClient(),
            new RecordingToolExecutor());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 2,
                MaxTurnDuration = TimeSpan.FromSeconds(1)
            },
            CancellationToken.None).AsTask());
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
    public async Task AgentInterruptsThreeIdenticalFailedCallsWithReturnedRecoveryAction()
    {
        var inspectTrust = new RekallAgeLanguageModelResponse(
            "test", "model", "", "",
            [new RekallAgeLanguageModelToolCall(
                "rekall.module.inspect_trust",
                new JsonObject { ["projectRoot"] = "game" })],
            "tool_calls", new(1, 1, 1));
        var model = new ScriptedModelClient(
            inspectTrust,
            inspectTrust,
            inspectTrust,
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall(
                    "rekall.build.modules",
                    new JsonObject { ["projectRoot"] = "game" })],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "Recovered.", "", [], "stop", new(1, 1, 1)));
        var tools = new RepeatedFailureToolExecutor();
        var agent = new RekallAgeLanguageModelAgent(model, tools);

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task") { MaxTurns = 5 },
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal(5, result.Turns);
        Assert.Contains(
            model.Requests[3].Messages,
            message => message.Role == "user"
                && message.Content.Contains("three consecutive times", StringComparison.Ordinal)
                && message.Content.Contains("rekall.module.inspect_trust", StringComparison.Ordinal)
                && message.Content.Contains("rekall.build.modules", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AgentStopsBroadBlueprintThrashAcrossDifferentInvalidArguments()
    {
        RekallAgeLanguageModelResponse Blueprint(string name) => new(
            "test", "model", "", "",
            [new RekallAgeLanguageModelToolCall(
                "rekall.scene.apply_blueprint",
                new JsonObject
                {
                    ["entities"] = new JsonArray(new JsonObject { ["name"] = name })
                })],
            "tool_calls", new(1, 1, 1));
        var model = new ScriptedModelClient(
            Blueprint("First"),
            Blueprint("Second"),
            Blueprint("Third"),
            new RekallAgeLanguageModelResponse(
                "test", "model", "Recovered with a targeted mutation.", "", [], "stop", new(1, 1, 1)));
        var agent = new RekallAgeLanguageModelAgent(model, new AlwaysFailsBlueprintToolExecutor());

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task") { MaxTurns = 4 },
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Contains(
            model.Requests[3].Messages,
            message => message.Role == "user"
                && message.Content.Contains("Stop broad blueprint retries", StringComparison.Ordinal)
                && message.Content.Contains("top-level entities", StringComparison.Ordinal)
                && message.Content.Contains("same logical entity", StringComparison.Ordinal)
                && message.Content.Contains("rekall.component.add", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AgentStopsRuntimeEvidenceShapeThrashAndRedirectsToAuthoredBehavior()
    {
        RekallAgeLanguageModelResponse Inspect(string propertyName) => new(
            "test", "model", "", "",
            [new RekallAgeLanguageModelToolCall(
                "rekall.runtime.inspect_scene",
                new JsonObject
                {
                    ["frames"] = 10,
                    ["inputs"] = new JsonArray(new JsonObject { ["pressedKeys"] = new JsonArray("D") }),
                    ["assertions"] = new JsonArray(new JsonObject
                    {
                        ["entityName"] = "Player",
                        ["subject"] = "component.property",
                        ["operator"] = "changed.component.property",
                        ["propertyName"] = propertyName
                    })
                })],
            "tool_calls", new(1, 1, 1));
        var model = new ScriptedModelClient(
            Inspect("First"),
            Inspect("Second"),
            Inspect("Third"),
            new RekallAgeLanguageModelResponse(
                "test", "model", "Repairing the authored rule.", "", [], "stop", new(1, 1, 1)));
        var agent = new RekallAgeLanguageModelAgent(model, new AlwaysFailsRuntimeInspectionToolExecutor());

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task") { MaxTurns = 4 },
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Contains(
            model.Requests[3].Messages,
            message => message.Role == "user"
                && message.Content.Contains("Stop runtime evidence-shape retries", StringComparison.Ordinal)
                && message.Content.Contains("Do not attach", StringComparison.Ordinal)
                && message.Content.Contains("EntitiesWithComponent", StringComparison.Ordinal)
                && message.Content.Contains("propertyName", StringComparison.Ordinal));
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
                    ["inputs"] = new JsonArray(new JsonObject { ["pressedKeys"] = new JsonArray("D") }),
                    ["assertions"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["entityName"] = "Player",
                            ["subject"] = "component",
                            ["operator"] = "exists",
                            ["componentType"] = "Game.PlayerState"
                        },
                        new JsonObject
                        {
                            ["entityName"] = "Player",
                            ["subject"] = "delta.position3d.x",
                            ["operator"] = "greater-than",
                            ["expected"] = 0
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
    public async Task StatefulGameplayTaskRejectsMovementOnlyCheckpointUntilAgentStateChanges()
    {
        var movementOnly = MeaningfulRuntimeCheckpointArguments();
        var stateful = MeaningfulRuntimeCheckpointArguments();
        ((JsonArray)stateful["assertions"]!).Add(new JsonObject
        {
            ["entityName"] = "Player",
            ["subject"] = "changed.component.property",
            ["operator"] = "equals",
            ["expected"] = true,
            ["componentType"] = "Game.PlayerState",
            ["propertyName"] = "sealsCollected"
        });
        var model = new ScriptedModelClient(
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.module.scaffold_runtime_system", new JsonObject())],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.runtime.inspect_scene", movementOnly)],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "Movement proves the game is complete.", "", [], "stop", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.runtime.inspect_scene", stateful)],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "Progress is now proven.", "", [], "stop", new(1, 1, 1)));
        var agent = new RekallAgeLanguageModelAgent(model, new RecordingToolExecutor());

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest(
                "model",
                "system",
                "Move the player, collect seals, record progress, and support reset.")
            {
                MaxTurns = 5,
                RequireRuntimeBehaviorAssertions = true
            },
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal(5, result.Turns);
        Assert.Contains(
            model.Requests[3].Messages,
            message => message.Role == "user"
                && message.Content.Contains("changed agent-owned component property", StringComparison.OrdinalIgnoreCase));
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
                    ["inputs"] = new JsonArray(new JsonObject { ["pressedKeys"] = new JsonArray("D") }),
                    ["assertions"] = new JsonArray(
                    new JsonObject
                    {
                        ["entityName"] = "Player",
                        ["subject"] = "component",
                        ["operator"] = "exists",
                        ["componentType"] = "Game.PlayerState"
                    },
                    new JsonObject
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
                    ["assertions"] = new JsonArray(
                        new JsonObject
                        {
                            ["entityName"] = "Player",
                            ["subject"] = "component",
                            ["operator"] = "exists",
                            ["componentType"] = "Game.PlayerState"
                        },
                        new JsonObject
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
    public async Task RuntimeCheckpointAllowsSceneAuthoringNeededToCreateItsEvidence()
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
                [new RekallAgeLanguageModelToolCall("rekall.scene.apply_blueprint", new JsonObject())],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.workflow.package_playable_game", new JsonObject())],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekal.runtime.inspect_scene", MeaningfulRuntimeCheckpointArguments())],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "Gameplay is proven.", "", [], "stop", new(1, 1, 1)));
        var tools = new RecordingToolExecutor();
        var agent = new RekallAgeLanguageModelAgent(model, tools);

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 6,
                RequireRuntimeBehaviorAssertions = true
            },
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Contains(tools.Executions, execution => execution.Name == "rekall.scene.apply_blueprint");
        Assert.Contains(tools.Executions, execution => execution.Name == "rekal.runtime.inspect_scene");
        Assert.Contains(result.ToolExecutions, execution => execution.Name == "rekall.runtime.inspect_scene");
        Assert.DoesNotContain(tools.Executions, execution => execution.Name == "rekall.workflow.package_playable_game");
        Assert.Contains(result.ToolExecutions, execution =>
            execution.Name == "rekall.workflow.package_playable_game"
            && !execution.Succeeded
            && execution.ResultPreview.Contains("REKALL_RUNTIME_CHECKPOINT_REQUIRED", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RuntimeCheckpointBlocksDestructiveSceneReplacementDuringEvidenceRepair()
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
                [new RekallAgeLanguageModelToolCall("rekall.scene.apply_blueprint", new JsonObject
                {
                    ["clearExisting"] = true,
                    ["entities"] = new JsonArray(new JsonObject { ["name"] = "Player" })
                })],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "Preserved the authored scene.", "", [], "stop", new(1, 1, 1)));
        var tools = new RecordingToolExecutor();
        var agent = new RekallAgeLanguageModelAgent(model, tools);

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 4,
                RequireRuntimeBehaviorAssertions = true
            },
            CancellationToken.None);

        Assert.False(result.Completed);
        Assert.DoesNotContain(tools.Executions, execution => execution.Name == "rekall.scene.apply_blueprint");
        Assert.Contains(result.ToolExecutions, execution =>
            execution.Name == "rekall.scene.apply_blueprint"
            && !execution.Succeeded
            && execution.ResultPreview.Contains(
                "REKALL_RUNTIME_CHECKPOINT_DESTRUCTIVE_REPLACEMENT_DEFERRED",
                StringComparison.Ordinal)
            && execution.ResultPreview.Contains("clearExisting=false", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RuntimeCheckpointBlocksEncodedGatewayDestructiveSceneReplacement()
    {
        var destructiveArguments = new JsonObject
        {
            ["clearExisting"] = true,
            ["entities"] = new JsonArray(new JsonObject { ["name"] = "Player" })
        }.ToJsonString();
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
                [new RekallAgeLanguageModelToolCall("rekall.tools.execute", new JsonObject
                {
                    ["name"] = "rekall.scene.apply_blueprint",
                    ["arguments"] = destructiveArguments
                })],
                "tool_calls", new(1, 1, 1)));
        var tools = new RecordingToolExecutor();

        var result = await new RekallAgeLanguageModelAgent(model, tools).RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 3,
                RequireRuntimeBehaviorAssertions = true
            },
            CancellationToken.None);

        Assert.DoesNotContain(tools.Executions, execution => execution.Name == "rekall.tools.execute");
        Assert.Contains(result.ToolExecutions, execution =>
            execution.Name == "rekall.scene.apply_blueprint"
            && !execution.Succeeded
            && execution.ResultPreview.Contains(
                "REKALL_RUNTIME_CHECKPOINT_DESTRUCTIVE_REPLACEMENT_DEFERRED",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task RuntimeCheckpointRejectsExistenceOnlyAssertionsAsInsufficientCoverage()
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
                    ["inputs"] = new JsonArray(new JsonObject { ["pressedKeys"] = new JsonArray("D") }),
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
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.runtime.inspect_scene", new JsonObject
                {
                    ["inputs"] = new JsonArray(new JsonObject { ["pressedKeys"] = new JsonArray("D") }),
                    ["assertions"] = new JsonArray(
                        new JsonObject
                        {
                            ["entityName"] = "Player",
                            ["subject"] = "component",
                            ["operator"] = "exists",
                            ["componentType"] = "Game.PlayerState"
                        },
                        new JsonObject
                        {
                            ["entityName"] = "Player",
                            ["subject"] = "delta.position3d.x",
                            ["operator"] = "greater-than-or-equal",
                            ["expected"] = 0
                        })
                })],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.runtime.inspect_scene", new JsonObject
                {
                    ["inputs"] = new JsonArray(new JsonObject { ["pressedKeys"] = new JsonArray("D") }),
                    ["assertions"] = new JsonArray(
                        new JsonObject
                        {
                            ["entityName"] = "Player",
                            ["subject"] = "component",
                            ["operator"] = "exists",
                            ["componentType"] = "Game.PlayerState"
                        },
                        new JsonObject
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
                MaxTurns = 6,
                RequireRuntimeBehaviorAssertions = true
            },
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Single(tools.Executions, execution => execution.Name == "rekall.runtime.inspect_scene");
        Assert.Equal(2, result.ToolExecutions.Count(execution =>
            execution.Name == "rekall.runtime.inspect_scene"
            && !execution.Succeeded
            && execution.ResultPreview.Contains("REKALL_RUNTIME_CHECKPOINT_COVERAGE_REQUIRED", StringComparison.Ordinal)));
        Assert.All(
            result.ToolExecutions.Where(execution =>
                execution.Name == "rekall.runtime.inspect_scene"
                && execution.ResultPreview.Contains("REKALL_RUNTIME_CHECKPOINT_COVERAGE_REQUIRED", StringComparison.Ordinal)),
            execution =>
            {
                Assert.Contains("\"inputs\":true", execution.ResultPreview, StringComparison.Ordinal);
                Assert.Contains("\"agentComponent\":true", execution.ResultPreview, StringComparison.Ordinal);
                Assert.Contains("\"transition\":false", execution.ResultPreview, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task RuntimeCheckpointRejectsUnknownFlatInputFieldsWithCopyableSemanticActionGuidance()
    {
        var checkpoint = MeaningfulRuntimeCheckpointArguments();
        checkpoint["inputs"] = new JsonArray(new JsonObject
        {
            ["move_horizontal"] = 1,
            ["move_vertical"] = -1
        });
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
                [new RekallAgeLanguageModelToolCall("rekall.runtime.inspect_scene", checkpoint)],
                "tool_calls", new(1, 1, 1)));
        var tools = new RecordingToolExecutor();
        var agent = new RekallAgeLanguageModelAgent(model, tools);

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 3,
                RequireRuntimeBehaviorAssertions = true,
                MaxToolResultCharacters = 12_000
            },
            CancellationToken.None);

        Assert.DoesNotContain(tools.Executions, execution => execution.Name == "rekall.runtime.inspect_scene");
        var failure = Assert.Single(result.ToolExecutions, execution =>
            execution.Name == "rekall.runtime.inspect_scene");
        Assert.False(failure.Succeeded);
        Assert.Contains("REKALL_RUNTIME_CHECKPOINT_COVERAGE_REQUIRED", failure.ResultPreview, StringComparison.Ordinal);
        Assert.Contains("\"inputs\":false", failure.ResultPreview, StringComparison.Ordinal);
        Assert.Contains("\"semanticActions\":[{\"name\":\"move.horizontal\",\"value\":1,\"isDown\":true}]", failure.ResultPreview, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RuntimeCheckpointForwardsUnknownTopLevelArgumentsToTypedBinding()
    {
        var checkpoint = MeaningfulRuntimeCheckpointArguments();
        var inputs = checkpoint["inputs"]!.DeepClone();
        checkpoint.Remove("inputs");
        checkpoint["inputFrames"] = inputs;
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
                [new RekallAgeLanguageModelToolCall("rekall.runtime.inspect_scene", checkpoint)],
                "tool_calls", new(1, 1, 1)));
        var tools = new RecordingToolExecutor();
        var agent = new RekallAgeLanguageModelAgent(model, tools);

        await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 3,
                RequireRuntimeBehaviorAssertions = true
            },
            CancellationToken.None);

        Assert.Single(tools.Executions, execution =>
            execution.Name == "rekall.runtime.inspect_scene"
            && execution.Arguments.ContainsKey("inputFrames"));
    }

    [Fact]
    public async Task RuntimeCheckpointAcceptsLosslesslyEncodedTypedArrays()
    {
        var checkpoint = MeaningfulRuntimeCheckpointArguments();
        checkpoint["inputs"] = checkpoint["inputs"]!.ToJsonString();
        checkpoint["assertions"] = checkpoint["assertions"]!.ToJsonString();
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
                [new RekallAgeLanguageModelToolCall("rekall.runtime.inspect_scene", checkpoint)],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "Gameplay is proven.", "", [], "stop", new(1, 1, 1)));
        var tools = new RecordingToolExecutor();
        var agent = new RekallAgeLanguageModelAgent(model, tools);

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 4,
                RequireRuntimeBehaviorAssertions = true
            },
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Single(tools.Executions, execution => execution.Name == "rekall.runtime.inspect_scene");
        Assert.DoesNotContain(result.ToolExecutions, execution =>
            execution.Name == "rekall.runtime.inspect_scene"
            && execution.ResultPreview.Contains("REKALL_RUNTIME_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteGatewayRuntimeInspectionQualifiesAsFreshImmediateEvidence()
    {
        var gatewayCheckpoint = new RekallAgeLanguageModelToolCall(
            "rekall.tools.execute",
            new JsonObject
            {
                ["name"] = "rekall.runtime.inspect_scene",
                ["arguments"] = MeaningfulRuntimeCheckpointArguments()
            });
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
                "test", "model", "", "", [gatewayCheckpoint], "tool_calls", new(1, 1, 1)),
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
        var inspection = Assert.Single(result.ToolExecutions, execution =>
            execution.Name == "rekall.runtime.inspect_scene");
        Assert.True(inspection.Succeeded);
        Assert.True(inspection.Arguments.ContainsKey("inputs"));
        Assert.True(inspection.Arguments.ContainsKey("assertions"));
    }

    [Fact]
    public async Task EncodedExecuteGatewayRuntimeInspectionQualifiesAsFreshImmediateEvidence()
    {
        var gatewayCheckpoint = new RekallAgeLanguageModelToolCall(
            "rekall.tools.execute",
            new JsonObject
            {
                ["name"] = "rekall.runtime.inspect_scene",
                ["arguments"] = MeaningfulRuntimeCheckpointArguments().ToJsonString()
            });
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
                "test", "model", "", "", [gatewayCheckpoint], "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "Gameplay is proven.", "", [], "stop", new(1, 1, 1)));

        var result = await new RekallAgeLanguageModelAgent(model, new RecordingToolExecutor()).RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 4,
                RequireRuntimeBehaviorAssertions = true
            },
            CancellationToken.None);

        Assert.True(result.Completed);
        var inspection = Assert.Single(result.ToolExecutions, execution =>
            execution.Name == "rekall.runtime.inspect_scene");
        Assert.True(inspection.Arguments.ContainsKey("inputs"));
        Assert.True(inspection.Arguments.ContainsKey("assertions"));
    }

    [Fact]
    public async Task RuntimeCheckpointRejectsMalformedScalarAndOversizedEncodedArrays()
    {
        var invalidInputs = new[]
        {
            "not-json",
            "{\"pressedKeys\":[\"D\"]}",
            new string(' ', 1_000_001)
        };

        foreach (var invalidInput in invalidInputs)
        {
            var checkpoint = MeaningfulRuntimeCheckpointArguments();
            checkpoint["inputs"] = invalidInput;
            checkpoint["assertions"] = checkpoint["assertions"]!.ToJsonString();
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
                    [new RekallAgeLanguageModelToolCall("rekall.runtime.inspect_scene", checkpoint)],
                    "tool_calls", new(1, 1, 1)));
            var tools = new RecordingToolExecutor();
            var agent = new RekallAgeLanguageModelAgent(model, tools);

            var result = await agent.RunAsync(
                new RekallAgeLanguageModelAgentRequest("model", "system", "task")
                {
                    MaxTurns = 3,
                    RequireRuntimeBehaviorAssertions = true
                },
                CancellationToken.None);

            Assert.DoesNotContain(tools.Executions, execution => execution.Name == "rekall.runtime.inspect_scene");
            Assert.Contains(result.ToolExecutions, execution =>
                execution.Name == "rekall.runtime.inspect_scene"
                && execution.ResultPreview.Contains("REKALL_RUNTIME_CHECKPOINT_COVERAGE_REQUIRED", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task RuntimeCheckpointAcceptsIntuitiveDeltaTransformSubjectAlias()
    {
        var checkpoint = new JsonObject
        {
            ["inputs"] = new JsonArray(new JsonObject { ["pressedKeys"] = new JsonArray("D") }),
            ["assertions"] = new JsonArray(
                new JsonObject
                {
                    ["entityName"] = "Player",
                    ["subject"] = "component",
                    ["operator"] = "exists",
                    ["componentType"] = "Game.PlayerState"
                },
                new JsonObject
                {
                    ["entityName"] = "Player",
                    ["subject"] = "delta.transform.position3d.x",
                    ["operator"] = "greater-than",
                    ["expected"] = 0
                })
        };
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
                [new RekallAgeLanguageModelToolCall("rekall.runtime.inspect_scene", checkpoint)],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "Gameplay is proven.", "", [], "stop", new(1, 1, 1)));
        var tools = new RecordingToolExecutor();
        var agent = new RekallAgeLanguageModelAgent(model, tools);

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 4,
                RequireRuntimeBehaviorAssertions = true
            },
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Single(tools.Executions, execution => execution.Name == "rekall.runtime.inspect_scene");
        Assert.DoesNotContain(result.ToolExecutions, execution =>
            execution.Name == "rekall.runtime.inspect_scene"
            && execution.ResultPreview.Contains("REKALL_RUNTIME_CHECKPOINT_COVERAGE_REQUIRED", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RuntimeCheckpointReturnsCopyableRepairForInvertedAgentComponentArguments()
    {
        var malformedCheckpoint = new JsonObject
        {
            ["inputs"] = new JsonArray(new JsonObject { ["pressedKeys"] = new JsonArray("D") }),
            ["assertions"] = new JsonArray(
                new JsonObject
                {
                    ["entityName"] = "PlayerOrb",
                    ["subject"] = "component",
                    ["operator"] = "exists"
                },
                new JsonObject
                {
                    ["entityName"] = "Game.Modules.Rules.PlayerState",
                    ["subject"] = "component.property",
                    ["propertyName"] = "IsMoving",
                    ["operator"] = "equals",
                    ["expected"] = true
                },
                new JsonObject
                {
                    ["entityName"] = "PlayerOrb",
                    ["subject"] = "delta.position3d.x",
                    ["operator"] = "greater-than",
                    ["expected"] = 0
                })
        };
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
                [new RekallAgeLanguageModelToolCall("rekall.runtime.inspect_scene", malformedCheckpoint)],
                "tool_calls", new(1, 1, 1)));
        var agent = new RekallAgeLanguageModelAgent(model, new RecordingToolExecutor());

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 3,
                RequireRuntimeBehaviorAssertions = true,
                MaxToolResultCharacters = 12_000
            },
            CancellationToken.None);

        var failure = Assert.Single(result.ToolExecutions, execution =>
            execution.Name == "rekall.runtime.inspect_scene");
        Assert.False(failure.Succeeded);
        Assert.Contains("candidateAgentComponentAssertion", failure.ResultPreview, StringComparison.Ordinal);
        Assert.Contains("Game.Modules.Rules.PlayerState", failure.ResultPreview, StringComparison.Ordinal);
        Assert.Contains("\"componentType\"", failure.ResultPreview, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RuntimeCheckpointAcceptsUnqualifiedAgentOwnedClrComponentIdentity()
    {
        var checkpoint = MeaningfulRuntimeCheckpointArguments();
        ((JsonObject)((JsonArray)checkpoint["assertions"]!)[0]!)["componentType"] = "OrbitMotion";
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
                [new RekallAgeLanguageModelToolCall("rekall.runtime.inspect_scene", checkpoint)],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse("test", "model", "Gameplay is proven.", "", [], "stop", new(1, 1, 1)));
        var tools = new RecordingToolExecutor();
        var agent = new RekallAgeLanguageModelAgent(model, tools);

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 4,
                RequireRuntimeBehaviorAssertions = true
            },
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Single(tools.Executions, execution => execution.Name == "rekall.runtime.inspect_scene");
    }

    [Fact]
    public async Task RuntimeCheckpointRejectsEngineOwnedComponentAsAgentStateProof()
    {
        var checkpoint = MeaningfulRuntimeCheckpointArguments();
        ((JsonObject)((JsonArray)checkpoint["assertions"]!)[0]!)["componentType"] = "Rekall.SphereCollider3D";
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
                [new RekallAgeLanguageModelToolCall("rekall.runtime.inspect_scene", checkpoint)],
                "tool_calls", new(1, 1, 1)));
        var tools = new RecordingToolExecutor();
        var agent = new RekallAgeLanguageModelAgent(model, tools);

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 3,
                RequireRuntimeBehaviorAssertions = true
            },
            CancellationToken.None);

        Assert.DoesNotContain(tools.Executions, execution => execution.Name == "rekall.runtime.inspect_scene");
        var failure = Assert.Single(result.ToolExecutions, execution =>
            execution.Name == "rekall.runtime.inspect_scene");
        Assert.Contains("\"agentComponent\":false", failure.ResultPreview, StringComparison.Ordinal);
    }

    private static JsonObject MeaningfulRuntimeCheckpointArguments() => new()
    {
        ["inputs"] = new JsonArray(new JsonObject { ["pressedKeys"] = new JsonArray("D") }),
        ["assertions"] = new JsonArray(
            new JsonObject
            {
                ["entityName"] = "Player",
                ["subject"] = "component",
                ["operator"] = "exists",
                ["componentType"] = "Game.PlayerState"
            },
            new JsonObject
            {
                ["entityName"] = "Player",
                ["subject"] = "delta.position3d.x",
                ["operator"] = "greater-than",
                ["expected"] = 0
            })
    };

    [Fact]
    public async Task FailedRuntimeAssertionUnlocksProtectedRepairAndRetestTurns()
    {
        var assertionArguments = new JsonObject
        {
            ["inputs"] = new JsonArray(new JsonObject { ["pressedKeys"] = new JsonArray("D") }),
            ["assertions"] = new JsonArray(
                new JsonObject
                {
                    ["entityName"] = "Player",
                    ["subject"] = "component",
                    ["operator"] = "exists",
                    ["componentType"] = "Game.PlayerState"
                },
                new JsonObject
                {
                    ["entityName"] = "Player",
                    ["subject"] = "delta.position3d.x",
                    ["operator"] = "greater-than",
                    ["expected"] = 0
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
    public async Task FailedRuntimeAssertionContinuesWithoutOverflowWhenTurnsAreUnbounded()
    {
        var assertionArguments = MeaningfulRuntimeCheckpointArguments();
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
                MaxTurns = null,
                MaxRuntimeBehaviorRepairTurns = 3,
                RequireRuntimeBehaviorAssertions = true
            },
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal(5, result.Turns);
        Assert.Contains(
            model.Requests[2].Messages,
            message => message.Role == "user"
                && message.Content.Contains("no configured overall turn limit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LateSuccessfulRuntimeCheckpointUnlocksBoundedDeliveryTurns()
    {
        var checkpoint = MeaningfulRuntimeCheckpointArguments();
        var model = new ScriptedModelClient(
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("inspect", new JsonObject())],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.runtime.inspect_scene", checkpoint)],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.validation.repair_project", new JsonObject())],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.workflow.package_playable_game", new JsonObject())],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "Delivered", "", [], "stop", new(1, 1, 1)));
        var agent = new RekallAgeLanguageModelAgent(model, new RecordingToolExecutor());

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 2,
                MaxPostRuntimeDeliveryTurns = 3,
                RequireRuntimeBehaviorAssertions = true
            },
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal(5, result.Turns);
        Assert.Contains(
            model.Requests[2].Messages,
            message => message.Role == "user"
                && message.Content.Contains("protected delivery", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            model.Requests[2].Messages,
            message => message.Role == "user"
                && message.Content.Contains("rekall.module.scaffold_playable", StringComparison.Ordinal)
                && message.Content.Contains("before the final build", StringComparison.OrdinalIgnoreCase)
                && message.Content.Contains("runtime-system module", StringComparison.OrdinalIgnoreCase)
                && message.Content.Contains("refresh runtime proof", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PostRuntimeDeliveryReserveActivatesOnlyOnce()
    {
        var checkpoint = MeaningfulRuntimeCheckpointArguments();
        var runtimeCall = new RekallAgeLanguageModelToolCall("rekall.runtime.inspect_scene", checkpoint);
        var model = new ScriptedModelClient(
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("inspect", new JsonObject())],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse("test", "model", "", "", [runtimeCall], "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse("test", "model", "", "", [runtimeCall], "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse("test", "model", "", "", [runtimeCall], "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse("test", "model", "", "", [runtimeCall], "tool_calls", new(1, 1, 1)));
        var agent = new RekallAgeLanguageModelAgent(model, new RecordingToolExecutor());

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 2,
                MaxPostRuntimeDeliveryTurns = 2,
                RequireRuntimeBehaviorAssertions = true
            },
            CancellationToken.None);

        Assert.False(result.Completed);
        Assert.Equal("turn_limit", result.StopReason);
        Assert.Equal(4, result.Turns);
        Assert.Equal(4, model.Requests.Count);
    }

    [Fact]
    public async Task DefaultPostRuntimeDeliveryReserveSupportsComplexBoundedDelivery()
    {
        var responses = new List<RekallAgeLanguageModelResponse>
        {
            new(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall(
                    "rekall.runtime.inspect_scene",
                    MeaningfulRuntimeCheckpointArguments())],
                "tool_calls", new(1, 1, 1))
        };
        responses.AddRange(Enumerable.Range(1, 15).Select(sequence =>
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall(
                    "inspect",
                    new JsonObject { ["deliveryStep"] = sequence })],
                "tool_calls", new(1, 1, 1))));
        responses.Add(new RekallAgeLanguageModelResponse(
            "test", "model", "Delivered", "", [], "stop", new(1, 1, 1)));
        var model = new ScriptedModelClient([.. responses]);
        var agent = new RekallAgeLanguageModelAgent(model, new RecordingToolExecutor());

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 1,
                RequireRuntimeBehaviorAssertions = true
            },
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal(17, result.Turns);
        Assert.Equal(17, model.Requests.Count);
    }

    [Fact]
    public async Task EarlyRuntimeCheckpointDoesNotArmDeliveryReserveLaterAsBudgetElapses()
    {
        var model = new ScriptedModelClient(
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall(
                    "rekall.runtime.inspect_scene",
                    MeaningfulRuntimeCheckpointArguments())],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("inspect", new JsonObject())],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("inspect", new JsonObject())],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("inspect", new JsonObject())],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse("test", "model", "Unexpected extra turn", "", [], "stop", new(1, 1, 1)));
        var agent = new RekallAgeLanguageModelAgent(model, new RecordingToolExecutor());

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 4,
                MaxPostRuntimeDeliveryTurns = 2,
                RequireRuntimeBehaviorAssertions = true
            },
            CancellationToken.None);

        Assert.False(result.Completed);
        Assert.Equal(4, result.Turns);
        Assert.Equal(4, model.Requests.Count);
    }

    [Fact]
    public async Task RuntimeTaskDefersExcessSceneAuthoringUntilModuleSliceBegins()
    {
        var sceneCall = new RekallAgeLanguageModelToolCall("rekall.scene.apply_blueprint", new JsonObject());
        var model = new ScriptedModelClient(
            new RekallAgeLanguageModelResponse("test", "model", "", "", [sceneCall], "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse("test", "model", "", "", [sceneCall], "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse("test", "model", "", "", [sceneCall], "tool_calls", new(1, 1, 1)),
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
                [new RekallAgeLanguageModelToolCall("rekall.runtime.inspect_scene", MeaningfulRuntimeCheckpointArguments())],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse("test", "model", "Complete", "", [], "stop", new(1, 1, 1)));
        var tools = new RecordingToolExecutor();
        var agent = new RekallAgeLanguageModelAgent(model, tools);

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 7,
                MaxPreRuntimeAuthoringMutations = 2,
                RequireRuntimeBehaviorAssertions = true
            },
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal(2, tools.Executions.Count(execution => execution.Name == "rekall.scene.apply_blueprint"));
        var deferred = Assert.Single(result.ToolExecutions, execution =>
            execution.Name == "rekall.scene.apply_blueprint" && !execution.Succeeded);
        Assert.Contains("REKALL_RUNTIME_AUTHORING_CHECKPOINT_REQUIRED", deferred.ResultPreview, StringComparison.Ordinal);
        Assert.Contains(model.Requests[2].Messages, message =>
            message.Role == "user" && message.Content.Contains("runtime module", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RuntimeAuthoringCheckpointDoesNotCountReadOnlyEntityInspectionAsMutation()
    {
        var inspect = new RekallAgeLanguageModelToolCall("rekall.entity.inspect", new JsonObject());
        var mutate = new RekallAgeLanguageModelToolCall("rekall.scene.apply_blueprint", new JsonObject());
        var model = new ScriptedModelClient(
            new RekallAgeLanguageModelResponse("test", "model", "", "", [inspect], "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse("test", "model", "", "", [inspect], "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse("test", "model", "", "", [mutate], "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse("test", "model", "Done", "", [], "stop", new(1, 1, 1)));
        var tools = new RecordingToolExecutor();

        var result = await new RekallAgeLanguageModelAgent(model, tools).RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 4,
                MaxPreRuntimeAuthoringMutations = 2,
                RequireRuntimeBehaviorAssertions = true
            },
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal(3, tools.Executions.Count);
        Assert.All(result.ToolExecutions, execution => Assert.True(execution.Succeeded));
    }

    [Fact]
    public async Task ArbitraryRuntimeSourceReadDoesNotSatisfyPreRuntimeAuthoringCheckpoint()
    {
        var read = new RekallAgeLanguageModelToolCall("rekall.module.read_source", new JsonObject());
        var mutate = new RekallAgeLanguageModelToolCall("rekall.scene.apply_blueprint", new JsonObject());
        var model = new ScriptedModelClient(
            new RekallAgeLanguageModelResponse("test", "model", "", "", [read], "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse("test", "model", "", "", [mutate], "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse("test", "model", "", "", [mutate], "tool_calls", new(1, 1, 1)));
        var tools = new RecordingToolExecutor();

        var result = await new RekallAgeLanguageModelAgent(model, tools).RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 3,
                MaxPreRuntimeAuthoringMutations = 1,
                RequireRuntimeBehaviorAssertions = true
            },
            CancellationToken.None);

        Assert.False(result.Completed);
        Assert.Equal(2, tools.Executions.Count);
        var deferred = Assert.Single(result.ToolExecutions, execution =>
            execution.Name == "rekall.scene.apply_blueprint" && !execution.Succeeded);
        Assert.Contains("REKALL_RUNTIME_AUTHORING_CHECKPOINT_REQUIRED", deferred.ResultPreview, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RuntimeAuthoringPolicyEvaluatesExecuteGatewayTargetAndArguments()
    {
        var mutate = new RekallAgeLanguageModelToolCall("rekall.scene.apply_blueprint", new JsonObject());
        var gatewayWrite = new RekallAgeLanguageModelToolCall(
            "rekall.tools.execute",
            new JsonObject
            {
                ["name"] = "rekall.module.write_source",
                ["arguments"] = new JsonObject
                {
                    ["projectRoot"] = "game",
                    ["moduleName"] = "Rules",
                    ["fileName"] = "Rules.cs",
                    ["content"] = "runtime source"
                }
            });
        var model = new ScriptedModelClient(
            new RekallAgeLanguageModelResponse("test", "model", "", "", [mutate], "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse("test", "model", "", "", [gatewayWrite], "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse("test", "model", "", "", [mutate], "tool_calls", new(1, 1, 1)));
        var tools = new RecordingToolExecutor();

        var result = await new RekallAgeLanguageModelAgent(model, tools).RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 3,
                MaxPreRuntimeAuthoringMutations = 1,
                RequireRuntimeBehaviorAssertions = true
            },
            CancellationToken.None);

        Assert.False(result.Completed);
        Assert.Equal(3, tools.Executions.Count);
        Assert.Contains(result.ToolExecutions, execution => execution.Name == "rekall.module.write_source" && execution.Succeeded);
        Assert.All(result.ToolExecutions, execution => Assert.True(execution.Succeeded));
    }

    [Fact]
    public async Task NonRuntimeTaskDoesNotApplyEarlyRuntimeAuthoringCheckpoint()
    {
        var sceneCall = new RekallAgeLanguageModelToolCall("rekall.scene.apply_blueprint", new JsonObject());
        var model = new ScriptedModelClient(
            new RekallAgeLanguageModelResponse("test", "model", "", "", [sceneCall], "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse("test", "model", "", "", [sceneCall], "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse("test", "model", "", "", [sceneCall], "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse("test", "model", "Done", "", [], "stop", new(1, 1, 1)));
        var tools = new RecordingToolExecutor();

        var result = await new RekallAgeLanguageModelAgent(model, tools).RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 4,
                MaxPreRuntimeAuthoringMutations = 1,
                RequireRuntimeBehaviorAssertions = false
            },
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal(3, tools.Executions.Count(execution => execution.Name == "rekall.scene.apply_blueprint"));
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
    public async Task StrictCompletionRejectsNarrativeClaimsUntilConfiguredAuditToolSucceeds()
    {
        var model = new ScriptedModelClient(
            new RekallAgeLanguageModelResponse(
                "test", "model", "Everything is complete.", "", [], "stop", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "I confirm completion.", "", [], "stop", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall(
                    "rekall.workflow.audit_playable_package",
                    new JsonObject { ["packagePath"] = "game.zip" })],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "Audit-backed completion.", "", [], "stop", new(1, 1, 1)));
        var agent = new RekallAgeLanguageModelAgent(model, new RecordingToolExecutor());

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest("model", "system", "task")
            {
                MaxTurns = 4,
                RequireCompletionAudit = true,
                RequireCompletionAuditToolEvidence = true,
                CompletionAuditPrimingTools = new HashSet<string>(
                    ["rekall.workflow.audit_playable_package"],
                    StringComparer.Ordinal)
            },
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal(4, result.Turns);
        Assert.Equal("Audit-backed completion.", result.FinalContent);
        Assert.All(model.Requests.Skip(1).Take(2), request =>
            Assert.Contains(request.Messages, message =>
                message.Role == "user"
                && message.Content.Contains(
                    "rekall.workflow.audit_playable_package",
                    StringComparison.Ordinal)));
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
    public async Task FailedPackageAuditAnchorsRecoveryToOriginalTaskInsteadOfFillerContent()
    {
        var model = new ScriptedModelClient(
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall(
                    "rekall.workflow.audit_playable_package",
                    new JsonObject { ["packagePath"] = "game.zip" })],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "Repairing the requested game.", "", [], "stop", new(1, 1, 1)));
        var agent = new RekallAgeLanguageModelAgent(model, new FailedPackageAuditToolExecutor());

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest(
                "model",
                "system",
                "Create an arena with a player, objectives, HUD, and completion behavior.")
            {
                MaxTurns = 2
            },
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Contains(
            model.Requests[1].Messages,
            message => message.Role == "user"
                && message.Content.Contains("original task", StringComparison.OrdinalIgnoreCase)
                && message.Content.Contains("Cube/Test/Demo/Fault", StringComparison.Ordinal)
                && message.Content.Contains("requested entities", StringComparison.OrdinalIgnoreCase)
                && message.Content.Contains("runtime assertions", StringComparison.OrdinalIgnoreCase)
                && message.Content.Contains("package", StringComparison.OrdinalIgnoreCase)
                && message.Content.Contains("audit", StringComparison.OrdinalIgnoreCase));
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
            message.Role == "user"
            && message.Content.StartsWith("Persistent Rekall tool ledger", StringComparison.Ordinal));
        Assert.DoesNotContain(
            model.Requests[1].Messages.SkipWhile(message => message != ledger),
            message => message.Role == "system");
        Assert.DoesNotContain(model.Requests[1].Messages, message => message.Role == "tool");
        Assert.Contains("#1 rekall.validation.project ok", ledger.Content, StringComparison.Ordinal);
        Assert.Contains("#14 noise.14 ok", ledger.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TaskSpecificCompletionRejectsPackageAuditWithoutRequestedRemoteImageVisualEvidence()
    {
        var model = new ScriptedModelClient(
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.workflow.audit_playable_package", new JsonObject())],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "Everything is complete.", "", [], "stop", new(1, 1, 1)));
        var agent = new RekallAgeLanguageModelAgent(model, new TaskEvidenceToolExecutor());

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest(
                "model",
                "system",
                "<user-request>Create a game with an openly licensed image from the internet as a full-window background. Implement the moving raindrops-on-glass effect as a custom shader.</user-request>")
            {
                MaxTurns = 3,
                RequireTaskSpecificEvidence = true,
                RequireCompletionAuditToolEvidence = true,
                CompletionAuditPrimingTools = new HashSet<string>(
                    ["rekall.workflow.audit_playable_package"],
                    StringComparer.Ordinal)
            },
            CancellationToken.None);

        Assert.False(result.Completed);
        Assert.Contains(
            model.Requests[2].Messages,
            message => message.Role == "user"
                && message.Content.Contains("Task-specific completion evidence is incomplete", StringComparison.Ordinal)
                && message.Content.Contains("rekall.asset.import_remote", StringComparison.Ordinal)
                && message.Content.Contains("custom shader", StringComparison.OrdinalIgnoreCase)
                && message.Content.Contains("REKALL_VIEWPORT_LOW_VISUAL_COVERAGE", StringComparison.Ordinal)
                && message.Content.Contains("distinct frame", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TaskSpecificCompletionAcceptsLicensedRemoteAssetFullCoverageAndDistinctFrameProof()
    {
        static RekallAgeLanguageModelResponse Call(string name, JsonObject? arguments = null) => new(
            "test", "model", "", "",
            [new RekallAgeLanguageModelToolCall(name, arguments ?? new JsonObject())],
            "tool_calls", new(1, 1, 1));
        var model = new ScriptedModelClient(
            Call("rekall.asset.search_remote_images"),
            Call("rekall.asset.import_remote", new JsonObject
            {
                ["license"] = "CC BY 2.0",
                ["licenseUrl"] = "https://creativecommons.org/licenses/by/2.0/",
                ["attribution"] = "Example Artist"
            }),
            Call("rekall.shader.write", new JsonObject { ["name"] = "agent/rain-glass" }),
            Call("rekall.shader.validate", new JsonObject { ["name"] = "agent/rain-glass" }),
            Call("rekall.shader.assign_pipeline", new JsonObject { ["entityName"] = "Background" }),
            Call("rekall.render.capture_runtime_viewport", new JsonObject { ["frames"] = 1 }),
            Call("rekall.render.capture_runtime_viewport", new JsonObject { ["frames"] = 60 }),
            Call("rekall.workflow.audit_playable_package"),
            new RekallAgeLanguageModelResponse(
                "test", "model", "Evidence-backed completion.", "", [], "stop", new(1, 1, 1)));
        var agent = new RekallAgeLanguageModelAgent(model, new TaskEvidenceToolExecutor());

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest(
                "model",
                "system",
                "<user-request>Create a game with an openly licensed image from the internet as a full-window background. Implement the moving raindrops-on-glass effect as a custom shader.</user-request>")
            {
                MaxTurns = 9,
                RequireTaskSpecificEvidence = true,
                RequireCompletionAuditToolEvidence = true,
                CompletionAuditPrimingTools = new HashSet<string>(
                    ["rekall.workflow.audit_playable_package"],
                    StringComparer.Ordinal)
            },
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal("Evidence-backed completion.", result.FinalContent);
    }

    [Fact]
    public async Task TaskSpecificCompletionRejectsPongWithoutRequestedUiAndGameplayTransitionEvidence()
    {
        var model = new ScriptedModelClient(
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.workflow.audit_playable_package", new JsonObject())],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "Pong is complete.", "", [], "stop", new(1, 1, 1)));
        var agent = new RekallAgeLanguageModelAgent(model, new TaskEvidenceToolExecutor());

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest(
                "model",
                "system",
                "<user-request>Create a fully playable two-player Pong game with paddle collisions, scoring, a serve and reset flow, and clear on-screen scores and control instructions.</user-request>")
            {
                MaxTurns = 3,
                RequireTaskSpecificEvidence = true,
                RequireCompletionAuditToolEvidence = true,
                CompletionAuditPrimingTools = new HashSet<string>(
                    ["rekall.workflow.audit_playable_package"],
                    StringComparer.Ordinal)
            },
            CancellationToken.None);

        Assert.False(result.Completed);
        Assert.Contains(
            model.Requests[2].Messages,
            message => message.Role == "user"
                && message.Content.Contains("UI renderable", StringComparison.OrdinalIgnoreCase)
                && message.Content.Contains("score transition", StringComparison.OrdinalIgnoreCase)
                && message.Content.Contains("reset", StringComparison.OrdinalIgnoreCase)
                && message.Content.Contains("two distinct semantic actions", StringComparison.OrdinalIgnoreCase)
                && message.Content.Contains("collision", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TaskSpecificCompletionAcceptsPongWithUiAndFocusedGameplayTransitionEvidence()
    {
        static RekallAgeLanguageModelResponse Call(string name, JsonObject? arguments = null) => new(
            "test", "model", "", "",
            [new RekallAgeLanguageModelToolCall(name, arguments ?? new JsonObject())],
            "tool_calls", new(1, 1, 1));
        static JsonObject Checkpoint(string action, string property) => new()
        {
            ["inputs"] = new JsonArray(new JsonObject
            {
                ["semanticActions"] = new JsonArray(new JsonObject
                {
                    ["name"] = action,
                    ["value"] = 1,
                    ["isDown"] = true
                })
            }),
            ["assertions"] = new JsonArray(new JsonObject
            {
                ["entityName"] = "Ball",
                ["subject"] = "changed.component.property",
                ["operator"] = "equals",
                ["expected"] = true,
                ["componentType"] = "Game.PongState",
                ["propertyName"] = property
            })
        };

        var model = new ScriptedModelClient(
            Call("rekall.render.capture_runtime_viewport", new JsonObject { ["frames"] = 1 }),
            Call("rekall.runtime.inspect_scene", Checkpoint("left.move", "LeftScore")),
            Call("rekall.runtime.inspect_scene", Checkpoint("right.move", "LastPaddleHit")),
            Call("rekall.runtime.inspect_scene", Checkpoint("game.reset", "Phase")),
            Call("rekall.workflow.audit_playable_package"),
            new RekallAgeLanguageModelResponse(
                "test", "model", "Pong is proven.", "", [], "stop", new(1, 1, 1)));
        var agent = new RekallAgeLanguageModelAgent(model, new TaskEvidenceToolExecutor());

        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest(
                "model",
                "system",
                "<user-request>Create a fully playable two-player Pong game with paddle collisions, scoring, a serve and reset flow, and clear on-screen scores and control instructions.</user-request>")
            {
                MaxTurns = 6,
                RequireTaskSpecificEvidence = true,
                RequireCompletionAuditToolEvidence = true,
                CompletionAuditPrimingTools = new HashSet<string>(
                    ["rekall.workflow.audit_playable_package"],
                    StringComparer.Ordinal)
            },
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal("Pong is proven.", result.FinalContent);
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

    private sealed class TimeoutThenActionModelClient : IRekallAgeLanguageModelClient
    {
        private int _index;
        public string ProviderId => "test";
        public List<RekallAgeLanguageModelRequest> Requests { get; } = [];
        public ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<RekallAgeLanguageModelInfo>>([]);

        public async ValueTask<RekallAgeLanguageModelResponse> ChatAsync(
            RekallAgeLanguageModelRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var index = _index++;
            if (index == 0)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return index == 1
                ? new RekallAgeLanguageModelResponse(
                    "test", "model", "", "",
                    [new RekallAgeLanguageModelToolCall("inspect", new JsonObject())],
                    "tool_calls", new(1, 1, 1))
                : new RekallAgeLanguageModelResponse("test", "model", "Ready", "", [], "stop", new(1, 1, 1));
        }
    }

    private sealed class NonCooperativeTimeoutThenActionModelClient : IRekallAgeLanguageModelClient
    {
        private int _index;
        public string ProviderId => "test";
        public List<RekallAgeLanguageModelRequest> Requests { get; } = [];
        public ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<RekallAgeLanguageModelInfo>>([]);

        public ValueTask<RekallAgeLanguageModelResponse> ChatAsync(
            RekallAgeLanguageModelRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var index = _index++;
            if (index == 0)
            {
                return new ValueTask<RekallAgeLanguageModelResponse>(
                    new TaskCompletionSource<RekallAgeLanguageModelResponse>(
                        TaskCreationOptions.RunContinuationsAsynchronously).Task);
            }

            return ValueTask.FromResult(index == 1
                ? new RekallAgeLanguageModelResponse(
                    "test", "model", "", "",
                    [new RekallAgeLanguageModelToolCall("inspect", new JsonObject())],
                    "tool_calls", new(1, 1, 1))
                : new RekallAgeLanguageModelResponse("test", "model", "Ready", "", [], "stop", new(1, 1, 1)));
        }
    }

    private sealed class SelfCancellingModelClient : IRekallAgeLanguageModelClient
    {
        public string ProviderId => "test";
        public ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<RekallAgeLanguageModelInfo>>([]);

        public ValueTask<RekallAgeLanguageModelResponse> ChatAsync(
            RekallAgeLanguageModelRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromCanceled<RekallAgeLanguageModelResponse>(new CancellationToken(canceled: true));
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

    private sealed class TaskEvidenceToolExecutor : IRekallAgeAgentToolExecutor
    {
        public IReadOnlyList<RekallAgeLanguageModelTool> Tools { get; } = [];

        public ValueTask<JsonNode> ExecuteAsync(
            string name,
            JsonObject arguments,
            CancellationToken cancellationToken)
        {
            if (name.Equals("rekall.render.capture_runtime_viewport", StringComparison.Ordinal))
            {
                var frame = arguments["frames"]?.GetValue<int>() ?? 1;
                return ValueTask.FromResult<JsonNode>(new JsonObject
                {
                    ["ok"] = true,
                    ["value"] = new JsonObject
                    {
                        ["captured"] = true,
                        ["frameIndex"] = frame,
                        ["renderableKinds"] = new JsonArray("mesh", "ui"),
                        ["assetBackedRenderableCount"] = 1,
                        ["frameAnalysis"] = new JsonObject
                        {
                            ["analyzed"] = true,
                            ["visuallyInformative"] = true,
                            ["dominantColorRatio"] = 0.72,
                            ["warningCodes"] = new JsonArray()
                        }
                    }
                });
            }

            return ValueTask.FromResult<JsonNode>(new JsonObject { ["ok"] = true });
        }
    }

    private sealed class RepeatedFailureToolExecutor : IRekallAgeAgentToolExecutor
    {
        public IReadOnlyList<RekallAgeLanguageModelTool> Tools { get; } =
        [
            new("rekall.module.inspect_trust", "Inspect trust", new JsonObject { ["type"] = "object" }),
            new("rekall.build.modules", "Build modules", new JsonObject { ["type"] = "object" })
        ];

        public ValueTask<JsonNode> ExecuteAsync(string name, JsonObject arguments, CancellationToken cancellationToken) =>
            ValueTask.FromResult<JsonNode>(name.Equals("rekall.module.inspect_trust", StringComparison.Ordinal)
                ? new JsonObject
                {
                    ["ok"] = false,
                    ["summary"] = "Module receipt is missing.",
                    ["value"] = new JsonObject
                    {
                        ["nextActions"] = new JsonArray(new JsonObject
                        {
                            ["tool"] = "rekall.build.modules",
                            ["arguments"] = new JsonObject { ["projectRoot"] = "game" }
                        })
                    }
                }
                : new JsonObject { ["ok"] = true });
    }

    private sealed class AlwaysFailsBlueprintToolExecutor : IRekallAgeAgentToolExecutor
    {
        public IReadOnlyList<RekallAgeLanguageModelTool> Tools { get; } =
            [new("rekall.scene.apply_blueprint", "Apply blueprint", new JsonObject { ["type"] = "object" })];

        public ValueTask<JsonNode> ExecuteAsync(
            string name,
            JsonObject arguments,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<JsonNode>(new JsonObject
            {
                ["ok"] = false,
                ["summary"] = "Invalid scene blueprint shape."
            });
    }

    private sealed class AlwaysFailsRuntimeInspectionToolExecutor : IRekallAgeAgentToolExecutor
    {
        public IReadOnlyList<RekallAgeLanguageModelTool> Tools { get; } =
            [new("rekall.runtime.inspect_scene", "Inspect runtime", new JsonObject { ["type"] = "object" })];

        public ValueTask<JsonNode> ExecuteAsync(
            string name,
            JsonObject arguments,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<JsonNode>(new JsonObject
            {
                ["ok"] = false,
                ["summary"] = "Runtime inspection completed, but an authored state assertion failed."
            });
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

    private sealed class FailedPackageAuditToolExecutor : IRekallAgeAgentToolExecutor
    {
        public IReadOnlyList<RekallAgeLanguageModelTool> Tools { get; } =
            [new("rekall.workflow.audit_playable_package", "Audit package", new JsonObject { ["type"] = "object" })];

        public ValueTask<JsonNode> ExecuteAsync(string name, JsonObject arguments, CancellationToken cancellationToken) =>
            ValueTask.FromResult<JsonNode>(new JsonObject
            {
                ["ok"] = false,
                ["summary"] = "Playable package audit failed.",
                ["errors"] = new JsonArray(new JsonObject
                {
                    ["code"] = "REKALL_PLAYABLE_PACKAGE_AUDIT_FAILED",
                    ["message"] = "Package proof frame is not informative.",
                    ["target"] = "informative-frame"
                })
            });
    }

    private sealed class RecordingProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];
        public void Report(T value) => Values.Add(value);
    }
}
