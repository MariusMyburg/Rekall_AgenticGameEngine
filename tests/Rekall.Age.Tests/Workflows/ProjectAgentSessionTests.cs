using System.Text.Json.Nodes;
using Rekall.Age.Agent.LanguageModels;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Project.Commands;
using Rekall.Age.Workflows;
using Rekall.Age.World;
using Rekall.Age.World.Commands;

namespace Rekall.Age.Tests.Workflows;

public sealed class ProjectAgentSessionTests
{
    [Fact]
    public async Task ArbitraryTasksDoNotTreatTheFixedGauntletAsTerminalCompletion()
    {
        var root = TestPaths.CreateTempDirectory();
        var registry = CreateRegistry();
        registry.Register(new GauntletProofCommand());
        await CreateProjectAsync(registry, root);
        var model = new ScriptedModelClient(
            GauntletCall(root),
            new RekallAgeLanguageModelResponse("test", "model", "Now author the requested game.", "", [], "stop", new(1, 1, 1)));

        var result = await new RekallAgeProjectAgentSession(model, registry).RunAsync(
            new RekallAgeProjectAgentSessionRequest(root, "Main", "model", "Build my specific puzzle game"),
            progress: null,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Summary);
        Assert.True(result.AgentResult.Turns > 1);
        Assert.NotEqual("terminal_tool_success", result.AgentResult.StopReason);
    }

    [Fact]
    public async Task ExplicitProofTaskMayTreatTheGauntletAsTerminalCompletion()
    {
        var root = TestPaths.CreateTempDirectory();
        var registry = CreateRegistry();
        registry.Register(new GauntletProofCommand());
        await CreateProjectAsync(registry, root);

        var model = new ScriptedModelClient(GauntletCall(root));
        var result = await new RekallAgeProjectAgentSession(model, registry).RunAsync(
            new RekallAgeProjectAgentSessionRequest(root, "Main", "model", "Run the fixed proof")
            {
                TreatGauntletAsTerminalSuccess = true
            },
            progress: null,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Summary);
        Assert.Equal(1, result.AgentResult.Turns);
        Assert.Equal("terminal_tool_success", result.AgentResult.StopReason);
        Assert.Null(model.Requests[0].Think);
        Assert.Null(model.Requests[0].MaxOutputTokens);
        Assert.Null(new RekallAgeProjectAgentSessionRequest(root, "Main", "model", "task").MaxTurnDuration);
    }

    [Fact]
    public async Task DefaultSessionDoesNotCapStructuredAuthoringOutput()
    {
        var root = TestPaths.CreateTempDirectory();
        var registry = CreateRegistry();
        registry.Register(new GauntletProofCommand());
        await CreateProjectAsync(registry, root);
        var model = new UnboundedOutputModelClient(root);

        var result = await new RekallAgeProjectAgentSession(model, registry).RunAsync(
            new RekallAgeProjectAgentSessionRequest(root, "Main", "model", "Create the requested game")
            {
                MaxTurns = 1,
                TreatGauntletAsTerminalSuccess = true
            },
            progress: null,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Summary);
        Assert.Equal("terminal_tool_success", result.AgentResult.StopReason);
    }

    [Fact]
    public async Task DefaultSessionDoesNotStopAtAnArbitraryTurnCount()
    {
        var root = TestPaths.CreateTempDirectory();
        var registry = CreateRegistry();
        registry.Register(new GauntletProofCommand());
        await CreateProjectAsync(registry, root);
        var responses = Enumerable.Range(1, 24)
            .Select(_ => new RekallAgeLanguageModelResponse("test", "model", "", "", [], "stop", new(1, 1, 1)))
            .Append(GauntletCall(root))
            .ToArray();

        var result = await new RekallAgeProjectAgentSession(new ScriptedModelClient(responses), registry).RunAsync(
            new RekallAgeProjectAgentSessionRequest(root, "Main", "model", "Create the requested game")
            {
                TreatGauntletAsTerminalSuccess = true
            },
            progress: null,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Summary);
        Assert.Equal(25, result.AgentResult.Turns);
        Assert.Equal("terminal_tool_success", result.AgentResult.StopReason);
    }

    [Fact]
    public async Task SessionUsesCanonicalToolsToMutateOnlyTheOpenProject()
    {
        var root = TestPaths.CreateTempDirectory();
        var registry = CreateRegistry();
        await CreateProjectAsync(registry, root);
        var model = new ScriptedModelClient(
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.entity.create", new JsonObject
                {
                    ["projectRoot"] = root,
                    ["sceneName"] = "Main",
                    ["name"] = "Agent Authored",
                    ["tags"] = new JsonArray("agent-authored")
                })],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse("test", "model", "Created the entity.", "", [], "stop", new(1, 1, 1)));
        var progress = new RecordingProgress<RekallAgeLanguageModelAgentProgress>();
        var session = new RekallAgeProjectAgentSession(model, registry);

        var result = await session.RunAsync(
            new RekallAgeProjectAgentSessionRequest(root, "Main", "model", "Add one entity") { MaxTurns = 4 },
            progress,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Summary);
        Assert.Equal(1, result.AgentResult.ToolCallCount);
        Assert.Contains((await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None)).Entities,
            entity => entity.Name == "Agent Authored");
        Assert.Contains(progress.Values, item => item.Phase == "tool.completed");
    }

    [Fact]
    public async Task ExistingRuntimeSystemSourceSatisfiesThePreRuntimeAuthoringCheckpoint()
    {
        var root = TestPaths.CreateTempDirectory();
        var registry = CreateRegistry();
        await CreateProjectAsync(registry, root);
        var moduleDirectory = Path.Combine(root, "Modules", "ExistingRules");
        Directory.CreateDirectory(moduleDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(moduleDirectory, "ExistingRulesModule.cs"),
            "public sealed class ExistingRulesSystem : IRekallAgeRuntimeModuleSystem { }");
        var responses = Enumerable.Range(1, 5)
            .Select(index => new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.entity.create", new JsonObject
                {
                    ["name"] = $"Revision {index}",
                    ["tags"] = new JsonArray()
                })],
                "tool_calls", new(1, 1, 1)))
            .Append(new RekallAgeLanguageModelResponse(
                "test", "model", "Revision complete.", "", [], "stop", new(1, 1, 1)))
            .ToArray();

        var result = await new RekallAgeProjectAgentSession(new ScriptedModelClient(responses), registry).RunAsync(
            new RekallAgeProjectAgentSessionRequest(root, "Main", "model", "Revise the existing game")
            {
                MaxTurns = 8,
                RequireCompletionAudit = false
            },
            progress: null,
            CancellationToken.None);

        Assert.Equal(5, result.AgentResult.ToolExecutions.Count(execution => execution.Succeeded));
        Assert.DoesNotContain(result.AgentResult.ToolExecutions, execution =>
            execution.ResultPreview.Contains("REKALL_RUNTIME_AUTHORING_CHECKPOINT_REQUIRED", StringComparison.Ordinal));
        var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
        Assert.All(Enumerable.Range(1, 5), index =>
            Assert.Contains(scene.Entities, entity => entity.Name == $"Revision {index}"));
    }

    [Fact]
    public async Task CommentsAndStringLiteralsDoNotPretendToBeExistingRuntimeSystems()
    {
        var root = TestPaths.CreateTempDirectory();
        var registry = CreateRegistry();
        await CreateProjectAsync(registry, root);
        var moduleDirectory = Path.Combine(root, "Modules", "NotesOnly");
        Directory.CreateDirectory(moduleDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(moduleDirectory, "NotesOnly.cs"),
            "// class Fake : IRekallAgeRuntimeModuleSystem { }\npublic static class Notes { public const string Text = \"IRekallAgeRuntimeModuleSystem\"; }");
        var responses = Enumerable.Range(1, 5)
            .Select(index => new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.entity.create", new JsonObject
                {
                    ["name"] = $"Revision {index}",
                    ["tags"] = new JsonArray()
                })],
                "tool_calls", new(1, 1, 1)))
            .ToArray();

        var result = await new RekallAgeProjectAgentSession(new ScriptedModelClient(responses), registry).RunAsync(
            new RekallAgeProjectAgentSessionRequest(root, "Main", "model", "Revise the game")
            {
                MaxTurns = 5,
                RequireCompletionAudit = false
            },
            progress: null,
            CancellationToken.None);

        Assert.Equal(4, result.AgentResult.ToolExecutions.Count(execution => execution.Succeeded));
        Assert.Contains(result.AgentResult.ToolExecutions, execution =>
            execution.ResultPreview.Contains("REKALL_RUNTIME_AUTHORING_CHECKPOINT_REQUIRED", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("public sealed class Helper : Generic<IRekallAgeRuntimeModuleSystem> { }")]
    [InlineData("public sealed class Helper<T> : Base where T : IRekallAgeRuntimeModuleSystem { }")]
    public async Task IndirectRuntimeInterfaceReferencesDoNotPretendToBeRuntimeSystems(string source)
    {
        var root = TestPaths.CreateTempDirectory();
        var registry = CreateRegistry();
        await CreateProjectAsync(registry, root);
        var moduleDirectory = Path.Combine(root, "Modules", "IndirectReference");
        Directory.CreateDirectory(moduleDirectory);
        await File.WriteAllTextAsync(Path.Combine(moduleDirectory, "IndirectReference.cs"), source);
        var responses = Enumerable.Range(1, 5)
            .Select(index => new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.entity.create", new JsonObject
                {
                    ["name"] = $"Revision {index}",
                    ["tags"] = new JsonArray()
                })],
                "tool_calls", new(1, 1, 1)))
            .ToArray();

        var result = await new RekallAgeProjectAgentSession(new ScriptedModelClient(responses), registry).RunAsync(
            new RekallAgeProjectAgentSessionRequest(root, "Main", "model", "Revise the game")
            {
                MaxTurns = 5,
                RequireCompletionAudit = false
            },
            progress: null,
            CancellationToken.None);

        Assert.Equal(4, result.AgentResult.ToolExecutions.Count(execution => execution.Succeeded));
        Assert.Contains(result.AgentResult.ToolExecutions, execution =>
            execution.ResultPreview.Contains("REKALL_RUNTIME_AUTHORING_CHECKPOINT_REQUIRED", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReparsePointModulesRootCannotSatisfyTheRuntimeAuthoringCheckpointFromOutsideTheProject()
    {
        var root = TestPaths.CreateTempDirectory();
        var outside = TestPaths.CreateTempDirectory();
        var registry = CreateRegistry();
        await CreateProjectAsync(registry, root);
        var outsideModule = Path.Combine(outside, "OutsideRules");
        Directory.CreateDirectory(outsideModule);
        await File.WriteAllTextAsync(
            Path.Combine(outsideModule, "OutsideRules.cs"),
            "public sealed class OutsideRules : IRekallAgeRuntimeModuleSystem { }");
        var modulesRoot = Path.Combine(root, "Modules");
        if (Directory.Exists(modulesRoot)) Directory.Delete(modulesRoot, recursive: true);
        try
        {
            Directory.CreateSymbolicLink(modulesRoot, outside);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        var responses = Enumerable.Range(1, 5)
            .Select(index => new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.entity.create", new JsonObject
                {
                    ["name"] = $"Revision {index}",
                    ["tags"] = new JsonArray()
                })],
                "tool_calls", new(1, 1, 1)))
            .ToArray();

        var result = await new RekallAgeProjectAgentSession(new ScriptedModelClient(responses), registry).RunAsync(
            new RekallAgeProjectAgentSessionRequest(root, "Main", "model", "Revise the game")
            {
                MaxTurns = 5,
                RequireCompletionAudit = false
            },
            progress: null,
            CancellationToken.None);

        Assert.Equal(4, result.AgentResult.ToolExecutions.Count(execution => execution.Succeeded));
        Assert.Contains(result.AgentResult.ToolExecutions, execution =>
            execution.ResultPreview.Contains("REKALL_RUNTIME_AUTHORING_CHECKPOINT_REQUIRED", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SessionSuppliesItsOwnedProjectAndSceneScopeToNativeTools()
    {
        var root = TestPaths.CreateTempDirectory();
        var registry = CreateRegistry();
        await CreateProjectAsync(registry, root);
        var model = new ScriptedModelClient(
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.entity.create", new JsonObject
                {
                    ["name"] = "Scope Defaulted",
                    ["tags"] = new JsonArray("agent-authored")
                })],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse("test", "model", "Created in the open scene.", "", [], "stop", new(1, 1, 1)));

        var result = await new RekallAgeProjectAgentSession(model, registry).RunAsync(
            new RekallAgeProjectAgentSessionRequest(root, "Main", "model", "Add one entity")
            {
                MaxTurns = 2,
                RequireCompletionAudit = false
            },
            progress: null,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Summary);
        Assert.Contains(
            (await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None)).Entities,
            entity => entity.Name == "Scope Defaulted");
        var execution = Assert.Single(result.AgentResult.ToolExecutions);
        Assert.DoesNotContain("projectRoot", execution.Arguments.Select(property => property.Key));
        Assert.DoesNotContain("sceneName", execution.Arguments.Select(property => property.Key));
    }

    [Fact]
    public async Task SessionSuppliesItsOwnedProjectAndSceneScopeToGatewayTargets()
    {
        var root = TestPaths.CreateTempDirectory();
        var registry = CreateRegistry();
        await CreateProjectAsync(registry, root);
        var model = new ScriptedModelClient(
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.tools.execute", new JsonObject
                {
                    ["name"] = "rekall.entity.create",
                    ["arguments"] = new JsonObject
                    {
                        ["name"] = "Gateway Scope Defaulted",
                        ["tags"] = new JsonArray("agent-authored")
                    }
                })],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse("test", "model", "Created in the open scene.", "", [], "stop", new(1, 1, 1)));

        var result = await new RekallAgeProjectAgentSession(model, registry).RunAsync(
            new RekallAgeProjectAgentSessionRequest(root, "Main", "model", "Add one entity")
            {
                MaxTurns = 2,
                RequireCompletionAudit = false
            },
            progress: null,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Summary);
        Assert.Contains(
            (await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None)).Entities,
            entity => entity.Name == "Gateway Scope Defaulted");
    }

    [Fact]
    public async Task SessionRejectsToolArgumentsThatEscapeTheOpenProject()
    {
        var root = TestPaths.CreateTempDirectory();
        var outside = TestPaths.CreateTempDirectory();
        var registry = CreateRegistry();
        await CreateProjectAsync(registry, root);
        await CreateProjectAsync(registry, outside);
        var model = new ScriptedModelClient(
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.tools.execute", new JsonObject
                {
                    ["name"] = "rekall.entity.create",
                    ["arguments"] = new JsonObject
                    {
                        ["projectRoot"] = outside,
                        ["sceneName"] = "Main",
                        ["name"] = "Escaped",
                        ["tags"] = new JsonArray()
                    }.ToJsonString()
                })],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse("test", "model", "Done", "", [], "stop", new(1, 1, 1)));

        var result = await new RekallAgeProjectAgentSession(model, registry).RunAsync(
            new RekallAgeProjectAgentSessionRequest(root, "Main", "model", "Try to escape"),
            progress: null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        var failed = Assert.Single(result.AgentResult.ToolExecutions);
        Assert.False(failed.Succeeded);
        Assert.Contains("REKALL_AGENT_PROJECT_SCOPE_VIOLATION", failed.ResultPreview, StringComparison.Ordinal);
        Assert.DoesNotContain((await new RekallAgeSceneStore().LoadAsync(outside, "Main", CancellationToken.None)).Entities,
            entity => entity.Name == "Escaped");
    }

    [Fact]
    public async Task SessionRejectsOversizedEncodedGatewayArgumentsBeforeScopeInspection()
    {
        var root = TestPaths.CreateTempDirectory();
        var outside = TestPaths.CreateTempDirectory();
        var registry = CreateRegistry();
        await CreateProjectAsync(registry, root);
        await CreateProjectAsync(registry, outside);
        var encoded = new JsonObject
        {
            ["projectRoot"] = outside,
            ["sceneName"] = "Main",
            ["name"] = "Escaped Oversized",
            ["tags"] = new JsonArray(),
            ["padding"] = new string('x', 1_000_001)
        }.ToJsonString();
        var model = new ScriptedModelClient(new RekallAgeLanguageModelResponse(
            "test", "model", "", "",
            [new RekallAgeLanguageModelToolCall("rekall.tools.execute", new JsonObject
            {
                ["name"] = "rekall.entity.create",
                ["arguments"] = encoded
            })],
            "tool_calls", new(1, 1, 1)));

        var result = await new RekallAgeProjectAgentSession(model, registry).RunAsync(
            new RekallAgeProjectAgentSessionRequest(root, "Main", "model", "Try to escape")
            {
                MaxTurns = 1,
                RequireCompletionAudit = false
            },
            progress: null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.AgentResult.ToolExecutions, execution =>
            execution.ResultPreview.Contains("REKALL_AGENT_ARGUMENTS_TOO_LARGE", StringComparison.Ordinal));
        Assert.DoesNotContain(
            (await new RekallAgeSceneStore().LoadAsync(outside, "Main", CancellationToken.None)).Entities,
            entity => entity.Name == "Escaped Oversized");
    }

    [Fact]
    public async Task CompletionAuditMayAcceptARecoveredNonSecurityToolFailure()
    {
        var root = TestPaths.CreateTempDirectory();
        var registry = CreateRegistry();
        await CreateProjectAsync(registry, root);
        var model = new ScriptedModelClient(
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.entity.create", new JsonObject
                {
                    ["tags"] = new JsonArray()
                })],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.entity.create", new JsonObject
                {
                    ["projectRoot"] = root,
                    ["sceneName"] = "Main",
                    ["name"] = "Recovered",
                    ["tags"] = new JsonArray()
                })],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse("test", "model", "The corrected entity exists.", "", [], "stop", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse("test", "model", "Audit confirms the corrected entity.", "", [], "stop", new(1, 1, 1)));

        var result = await new RekallAgeProjectAgentSession(model, registry).RunAsync(
            new RekallAgeProjectAgentSessionRequest(root, "Main", "model", "Create one entity") { MaxTurns = 4 },
            progress: null,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Summary);
        Assert.Contains(result.AgentResult.ToolExecutions, execution => !execution.Succeeded);
        Assert.Contains(
            (await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None)).Entities,
            entity => entity.Name == "Recovered");
    }

    private static RekallAgeCommandRegistry CreateRegistry()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new CreateProjectCommand());
        registry.Register(new CreateSceneCommand());
        registry.Register(new CreateEntityCommand());
        return registry;
    }

    private static RekallAgeLanguageModelResponse GauntletCall(string root) => new(
        "test", "model", "", "",
        [new RekallAgeLanguageModelToolCall("rekall.workflow.agent_authoring_gauntlet", new JsonObject
        {
            ["projectRoot"] = root
        })],
        "tool_calls", new(1, 1, 1));

    private static async Task CreateProjectAsync(RekallAgeCommandRegistry registry, string root)
    {
        var context = new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("create"), CancellationToken.None);
        Assert.True((await registry.ExecuteAsync<CreateProjectRequest, CreateProjectResult>(
            "rekall.project.create", new CreateProjectRequest(root, "Game", ["world"]), context)).Ok);
        Assert.True((await registry.ExecuteAsync<CreateSceneRequest, CreateSceneResult>(
            "rekall.scene.create", new CreateSceneRequest(root, "Main", ["world"]), context)).Ok);
    }

    private sealed class ScriptedModelClient(params RekallAgeLanguageModelResponse[] responses) : IRekallAgeLanguageModelClient
    {
        private int _index;
        public string ProviderId => "test";
        public List<RekallAgeLanguageModelRequest> Requests { get; } = [];
        public ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<RekallAgeLanguageModelInfo>>([new("model", 1)]);
        public ValueTask<RekallAgeLanguageModelResponse> ChatAsync(RekallAgeLanguageModelRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return ValueTask.FromResult(responses[Math.Min(_index++, responses.Length - 1)]);
        }
    }

    private sealed class UnboundedOutputModelClient(string projectRoot)
        : IRekallAgeLanguageModelClient
    {
        public string ProviderId => "test";

        public ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<RekallAgeLanguageModelInfo>>([new("model", 1)]);

        public ValueTask<RekallAgeLanguageModelResponse> ChatAsync(
            RekallAgeLanguageModelRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(request.MaxOutputTokens is null
                ? GauntletCall(projectRoot)
                : new RekallAgeLanguageModelResponse(
                    "test",
                    "model",
                    "",
                    "unfinished structured authoring call",
                    [],
                    "length",
                    new(1, request.MaxOutputTokens ?? 0, 1)));
    }

    private sealed class RecordingProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];
        public void Report(T value) => Values.Add(value);
    }

    private sealed record GauntletProofRequest(string ProjectRoot);
    private sealed record GauntletProofResult(bool Ready);

    private sealed class GauntletProofCommand : IRekallAgeCommand<GauntletProofRequest, GauntletProofResult>
    {
        public string Name => "rekall.workflow.agent_authoring_gauntlet";
        public RekallAgeCommandSchema Schema => new(Name, "Test proof.", typeof(GauntletProofRequest).FullName!, typeof(GauntletProofResult).FullName!);
        public ValueTask<RekallAgeCommandResult<GauntletProofResult>> ExecuteAsync(
            GauntletProofRequest request,
            RekallAgeCommandContext context) =>
            ValueTask.FromResult(RekallAgeCommandResult<GauntletProofResult>.Success(new(true), "Proof passed."));
    }
}
