using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Agent.Codex;
using Rekall.Age.Tests.Agent;
using Rekall.Age.Workflows;

namespace Rekall.Age.Tests.Workflows;

public sealed class CodexProjectAgentRunnerTests
{
    [Fact]
    public void McpConfigurationResolvesTheExactPackagedCliExecutable()
    {
        var fixture = Directory.CreateTempSubdirectory("rekall-codex-distribution-");
        try
        {
            File.WriteAllText(Path.Combine(fixture.FullName, "rekall.distribution.json"), "{}");
            var cliRoot = Directory.CreateDirectory(Path.Combine(fixture.FullName, "tools", "cli"));
            var expectedPath = Path.Combine(
                cliRoot.FullName,
                OperatingSystem.IsWindows() ? "Rekall.Age.Cli.exe" : "Rekall.Age.Cli");
            File.WriteAllText(expectedPath, "packaged-cli-fixture");

            var configuration = RekallAgeCodexMcpConfiguration.Resolve(fixture.FullName);
            var server = configuration.CreateValidatedServer();

            Assert.Equal(Path.GetFullPath(expectedPath), configuration.CliExecutablePath);
            Assert.Equal("rekall-age", server.Name);
            Assert.Equal(Path.GetFullPath(expectedPath), server.Command);
            Assert.Equal(["mcp", "stdio"], server.Arguments);
        }
        finally
        {
            Directory.Delete(fixture.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task MissingPackagedCliFailsBeforeStartingCodex()
    {
        var clientFactoryCalls = 0;
        await using var runner = new RekallAgeCodexProjectAgentRunner(
            new RekallAgeCodexMcpConfiguration(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe")),
            clientFactory: _ =>
            {
                clientFactoryCalls++;
                throw new InvalidOperationException("The client factory must not run.");
            });

        var error = await Assert.ThrowsAsync<Rekall.Age.Agent.LanguageModels.RekallAgeLanguageModelProviderException>(() =>
            runner.ListModelsAsync(CancellationToken.None).AsTask());

        Assert.Equal(RekallAgeCodexErrorCodes.RuntimeMissing, error.Code);
        Assert.Equal(0, clientFactoryCalls);
    }

    [Fact]
    public async Task ProjectRunStartsARestrictedThreadWithThePackagedAgeMcpExecutable()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var fixture = Directory.CreateTempSubdirectory("rekall-codex-runner-");
        try
        {
            var projectRoot = Directory.CreateDirectory(Path.Combine(fixture.FullName, "Project")).FullName;
            var cliPath = Path.Combine(fixture.FullName, "Rekall.Age.Cli.exe");
            await File.WriteAllTextAsync(cliPath, "packaged-cli-fixture", timeout.Token);
            var process = new FakeCodexProcess();
            var processFactory = new FakeCodexProcessFactory(process);
            await using var runner = new RekallAgeCodexProjectAgentRunner(
                new RekallAgeCodexMcpConfiguration(cliPath),
                clientFactory: cancellationToken => RekallAgeCodexAppServerClient.StartAsync(
                    TestAppServerOptions(),
                    processFactory,
                    cancellationToken));

            var run = runner.RunAsync(
                new RekallAgeProjectAgentSessionRequest(projectRoot, "Main", "gpt-5.6-sol", "Author a game."),
                progress: null,
                timeout.Token).AsTask();

            await CompleteInitializeAsync(process, timeout.Token);
            AssertJson(
                await process.ReadClientLineAsync(timeout.Token),
                """{"id":2,"method":"account/read","params":{"refreshToken":false}}""");
            await process.WriteServerLineAsync(
                """{"id":2,"result":{"account":{"type":"chatgpt"},"requiresOpenaiAuth":false}}""");
            AssertJson(
                await process.ReadClientLineAsync(timeout.Token),
                """{"id":3,"method":"model/list","params":{"includeHidden":false,"limit":100}}""");
            await process.WriteServerLineAsync(ModelListResponse(3));

            var threadStart = JsonNode.Parse(await process.ReadClientLineAsync(timeout.Token))!.AsObject();
            Assert.Equal("thread/start", threadStart["method"]!.GetValue<string>());
            var parameters = threadStart["params"]!.AsObject();
            Assert.Equal("on-request", parameters["approvalPolicy"]!.GetValue<string>());
            Assert.Equal(Path.GetFullPath(projectRoot), parameters["cwd"]!.GetValue<string>());
            Assert.True(parameters["ephemeral"]!.GetValue<bool>());
            Assert.Equal("gpt-5.6-sol", parameters["model"]!.GetValue<string>());
            Assert.Equal("workspace-write", parameters["sandbox"]!.GetValue<string>());
            var config = parameters["config"]!.AsObject();
            var workspace = config["sandbox_workspace_write"]!.AsObject();
            Assert.False(workspace["network_access"]!.GetValue<bool>());
            Assert.Equal(
                [Path.GetFullPath(projectRoot)],
                workspace["writable_roots"]!.AsArray().Select(item => item!.GetValue<string>()));
            var mcp = config["mcp_servers"]!["rekall-age"]!.AsObject();
            Assert.Equal(Path.GetFullPath(cliPath), mcp["command"]!.GetValue<string>());
            Assert.Equal(
                ["mcp", "stdio"],
                mcp["args"]!.AsArray().Select(item => item!.GetValue<string>()));
            Assert.DoesNotContain("mcp stdio", mcp["command"]!.GetValue<string>(), StringComparison.Ordinal);
            var developerInstructions = parameters["developerInstructions"]!.GetValue<string>();
            Assert.Contains("Rekall.InputActionMap", developerInstructions, StringComparison.Ordinal);
            Assert.Contains("InputActionValue", developerInstructions, StringComparison.Ordinal);
            Assert.Contains("DeltaSeconds", developerInstructions, StringComparison.Ordinal);
            Assert.Contains("Game.*", developerInstructions, StringComparison.Ordinal);
            Assert.Contains("EmitObservation", developerInstructions, StringComparison.Ordinal);
            Assert.Contains("after the latest", developerInstructions, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("nonzero transform delta", developerInstructions, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("rekall.runtime.inspect_scene", developerInstructions, StringComparison.Ordinal);
            Assert.Contains("rekall.workflow.agent_authoring_gauntlet", developerInstructions, StringComparison.Ordinal);
            Assert.Contains("Never ask AGE to author content", developerInstructions, StringComparison.Ordinal);

            await process.WriteServerLineAsync(
                "{\"id\":4,\"result\":{\"thread\":{\"id\":\"thread-1\"},\"model\":\"gpt-5.6-sol\",\"cwd\":"
                + JsonValue.Create(Path.GetFullPath(projectRoot))!.ToJsonString()
                + "}}" );
            var turnStart = JsonNode.Parse(await process.ReadClientLineAsync(timeout.Token))!.AsObject();
            Assert.Equal("turn/start", turnStart["method"]!.GetValue<string>());
            Assert.Contains("Author a game.", turnStart["params"]!["input"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
            await process.WriteServerLineAsync(
                """{"id":5,"result":{"turn":{"id":"turn-1","status":"inProgress","items":[]}}}""");
            await process.WriteServerLineAsync(
                """{"method":"item/completed","params":{"threadId":"thread-1","turnId":"turn-1","item":{"id":"message-1","type":"agentMessage","text":"Authored through AGE."}}}""");
            await process.WriteServerLineAsync(
                """{"method":"turn/completed","params":{"threadId":"thread-1","turn":{"id":"turn-1","status":"completed","items":[]}}}""");

            var result = await run;
            Assert.True(result.Succeeded);
            Assert.True(result.AgentResult.Completed);
            Assert.Equal("thread-1", result.AgentResult.ResponseId);
            Assert.Contains("Authored through AGE.", result.AgentResult.FinalContent, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(fixture.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task AuthenticatedAccountAndModelStateUseTheSharedProviderDescriptor()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var fixture = Directory.CreateTempSubdirectory("rekall-codex-status-");
        try
        {
            var cliPath = Path.Combine(fixture.FullName, "Rekall.Age.Cli.exe");
            await File.WriteAllTextAsync(cliPath, "packaged-cli-fixture", timeout.Token);
            var process = new FakeCodexProcess();
            await using var runner = new RekallAgeCodexProjectAgentRunner(
                new RekallAgeCodexMcpConfiguration(cliPath),
                clientFactory: cancellationToken => RekallAgeCodexAppServerClient.StartAsync(
                    TestAppServerOptions(),
                    new FakeCodexProcessFactory(process),
                    cancellationToken));

            var describe = runner.DescribeProviderAsync(timeout.Token).AsTask();
            await CompleteInitializeAsync(process, timeout.Token);
            _ = await process.ReadClientLineAsync(timeout.Token);
            await process.WriteServerLineAsync(
                """{"id":2,"result":{"account":{"type":"chatgpt","email":"must-not-surface@example.invalid"},"requiresOpenaiAuth":false}}""");
            _ = await process.ReadClientLineAsync(timeout.Token);
            await process.WriteServerLineAsync(ModelListResponse(3));

            var descriptor = await describe;
            Assert.Equal("codex", descriptor.Id);
            Assert.Equal("codex-managed", descriptor.AuthenticationKind);
            Assert.Equal("chatgpt", descriptor.AuthenticationState);
            Assert.Equal("gpt-5.6-sol", descriptor.DefaultModel);
            Assert.True(descriptor.IsAvailable);
            Assert.Empty(descriptor.Diagnostics);
            Assert.DoesNotContain("must-not-surface@example.invalid", descriptor.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(fixture.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task ToolProgressUsageAndMcpErrorsRemainVisibleInTheProjectResult()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var fixture = Directory.CreateTempSubdirectory("rekall-codex-evidence-");
        try
        {
            var process = new FakeCodexProcess();
            await using var runner = CreateRunner(fixture.FullName, process);
            var progress = new RecordingProgress<Rekall.Age.Agent.LanguageModels.RekallAgeLanguageModelAgentProgress>();
            var run = await StartRunThroughTurnAsync(
                runner,
                process,
                fixture.FullName,
                progress,
                timeout.Token,
                timeout.Token);

            await process.WriteServerLineAsync(
                """{"method":"item/started","params":{"threadId":"thread-1","turnId":"turn-1","item":{"id":"tool-1","type":"mcpToolCall","server":"rekall-age","tool":"rekall.runtime.inspect_scene","status":"inProgress","arguments":{"sceneName":"Main"}}}}""");
            await process.WriteServerLineAsync(
                """{"method":"item/completed","params":{"threadId":"thread-1","turnId":"turn-1","item":{"id":"tool-1","type":"mcpToolCall","server":"rekall-age","tool":"rekall.runtime.inspect_scene","status":"failed","arguments":{"sceneName":"Main"},"error":{"message":"REKALL_RUNTIME_ASSERTION_FAILED: strict delta was zero"}}}}""");
            await process.WriteServerLineAsync(
                """{"method":"thread/tokenUsage/updated","params":{"threadId":"thread-1","turnId":"turn-1","tokenUsage":{"total":{"inputTokens":17,"cachedInputTokens":5,"outputTokens":9,"reasoningOutputTokens":4,"totalTokens":26},"last":{"inputTokens":7,"cachedInputTokens":2,"outputTokens":3,"reasoningOutputTokens":1,"totalTokens":10},"modelContextWindow":128000}}}""");
            await process.WriteServerLineAsync(
                """{"method":"item/completed","params":{"threadId":"thread-1","turnId":"turn-1","item":{"id":"message-1","type":"agentMessage","text":"Repaired the runtime and completed delivery."}}}""");
            await CompleteTurnAsync(process, "completed");

            var result = await run;
            Assert.True(result.Succeeded);
            Assert.Equal(1, result.AgentResult.ToolCallCount);
            var tool = Assert.Single(result.AgentResult.ToolExecutions);
            Assert.Equal("rekall.runtime.inspect_scene", tool.Name);
            Assert.False(tool.Succeeded);
            Assert.Contains("REKALL_RUNTIME_ASSERTION_FAILED", tool.ResultPreview, StringComparison.Ordinal);
            Assert.Equal(17, result.AgentResult.Usage.PromptTokens);
            Assert.Equal(9, result.AgentResult.Usage.CompletionTokens);
            Assert.Equal(5, result.AgentResult.Usage.CachedInputTokens);
            Assert.Equal(4, result.AgentResult.Usage.ReasoningTokens);
            Assert.True(result.AgentResult.Usage.TotalDurationNanoseconds > 0);
            Assert.Contains(progress.Values, item =>
                item.Phase == "tool.failed"
                && item.Message.Contains("REKALL_RUNTIME_ASSERTION_FAILED", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(fixture.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task NoninteractiveApprovalRequestsAreDeniedByDefault()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var fixture = Directory.CreateTempSubdirectory("rekall-codex-denial-");
        try
        {
            var process = new FakeCodexProcess();
            await using var runner = CreateRunner(fixture.FullName, process);
            var run = await StartRunThroughTurnAsync(
                runner,
                process,
                fixture.FullName,
                progress: null,
                timeout.Token,
                timeout.Token);

            await process.WriteServerLineAsync(
                """{"id":"approval-1","method":"item/commandExecution/requestApproval","params":{"threadId":"thread-1","turnId":"turn-1","itemId":"command-1"}}""");
            AssertJson(
                await process.ReadClientLineAsync(timeout.Token),
                """{"id":"approval-1","result":{"decision":"decline"}}""");
            await CompleteTurnAsync(process, "completed");

            var result = await run;
            Assert.True(result.Succeeded);
            Assert.Contains(
                "item/commandExecution/requestApproval: decline",
                result.Summary,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(fixture.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task ExplicitApprovalCallbackControlsTheExactServerResponse()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var fixture = Directory.CreateTempSubdirectory("rekall-codex-approval-");
        try
        {
            var process = new FakeCodexProcess();
            RekallAgeCodexApprovalRequest? observed = null;
            await using var runner = CreateRunner(
                fixture.FullName,
                process,
                approvalCallback: (request, _) =>
                {
                    observed = request;
                    return ValueTask.FromResult(RekallAgeCodexApprovalDecision.Accept);
                });
            var run = await StartRunThroughTurnAsync(
                runner,
                process,
                fixture.FullName,
                progress: null,
                timeout.Token,
                timeout.Token);

            await process.WriteServerLineAsync(
                """{"id":17,"method":"item/fileChange/requestApproval","params":{"threadId":"thread-1","turnId":"turn-1","itemId":"change-1"}}""");
            AssertJson(
                await process.ReadClientLineAsync(timeout.Token),
                """{"id":17,"result":{"decision":"accept"}}""");
            Assert.NotNull(observed);
            Assert.Equal("item/fileChange/requestApproval", observed.Method);
            await CompleteTurnAsync(process, "completed");

            var result = await run;
            Assert.True(result.Succeeded);
            Assert.Contains(
                "item/fileChange/requestApproval: accept",
                result.Summary,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(fixture.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task McpElicitationApprovalUsesTheAppServerActionResponseContract()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var fixture = Directory.CreateTempSubdirectory("rekall-codex-mcp-approval-");
        try
        {
            var process = new FakeCodexProcess();
            await using var runner = CreateRunner(
                fixture.FullName,
                process,
                approvalCallback: (_, _) =>
                    ValueTask.FromResult(RekallAgeCodexApprovalDecision.Accept));
            var run = await StartRunThroughTurnAsync(
                runner,
                process,
                fixture.FullName,
                progress: null,
                timeout.Token,
                timeout.Token);

            await process.WriteServerLineAsync(
                """{"id":"mcp-approval-1","method":"mcpServer/elicitation/request","params":{"serverName":"rekall-age","threadId":"thread-1","turnId":"turn-1","mode":"form","message":"Approve tool call","requestedSchema":{"type":"object","properties":{}}}}""");
            AssertJson(
                await process.ReadClientLineAsync(timeout.Token),
                """{"id":"mcp-approval-1","result":{"action":"accept","content":{}}}""");
            await CompleteTurnAsync(process, "completed");

            var result = await run;
            Assert.True(result.Succeeded);
            Assert.Contains("mcpServer/elicitation/request: accept", result.Summary, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(fixture.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task ExplicitNeverApprovalPolicyIsProjectedWithoutInstallingAnApprovalCallback()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var fixture = Directory.CreateTempSubdirectory("rekall-codex-never-");
        try
        {
            var process = new FakeCodexProcess();
            await using var runner = CreateRunner(
                fixture.FullName,
                process,
                approvalPolicy: "never");
            var run = await StartRunThroughTurnAsync(
                runner,
                process,
                fixture.FullName,
                progress: null,
                timeout.Token,
                timeout.Token);
            await CompleteTurnAsync(process, "completed");

            Assert.True((await run).Succeeded);
        }
        finally
        {
            Directory.Delete(fixture.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task CancellationInterruptsAndWaitsForTerminalCompletionBeforeReturningAStableResult()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var runCancellation = new CancellationTokenSource();
        var fixture = Directory.CreateTempSubdirectory("rekall-codex-cancel-");
        try
        {
            var process = new FakeCodexProcess();
            await using var runner = CreateRunner(fixture.FullName, process);
            var run = await StartRunThroughTurnAsync(
                runner,
                process,
                fixture.FullName,
                progress: null,
                runCancellation.Token,
                timeout.Token);

            runCancellation.Cancel();
            AssertJson(
                await process.ReadClientLineAsync(timeout.Token),
                """{"id":6,"method":"turn/interrupt","params":{"threadId":"thread-1","turnId":"turn-1"}}""");
            await process.WriteServerLineAsync("""{"id":6,"result":{}}""");
            await Task.Delay(50, timeout.Token);
            Assert.False(run.IsCompleted);

            await CompleteTurnAsync(process, "interrupted");
            var result = await run;
            Assert.False(result.Succeeded);
            Assert.False(result.AgentResult.Completed);
            Assert.Equal(RekallAgeCodexErrorCodes.Cancelled, result.AgentResult.StopReason);
            Assert.Contains(RekallAgeCodexErrorCodes.Cancelled, result.Summary, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(fixture.FullName, recursive: true);
        }
    }

    private static async Task CompleteInitializeAsync(
        FakeCodexProcess process,
        CancellationToken cancellationToken)
    {
        AssertJson(
            await process.ReadClientLineAsync(cancellationToken),
            """
            {"id":1,"method":"initialize","params":{"clientInfo":{"name":"rekall-age","title":"Rekall AGE","version":"test"},"capabilities":{"experimentalApi":false}}}
            """);
        await process.WriteServerLineAsync(
            """{"id":1,"result":{"userAgent":"codex-cli/test","platformFamily":"windows","platformOs":"windows","codexHome":"C:\\bounded"}}""");
        AssertJson(
            await process.ReadClientLineAsync(cancellationToken),
            """{"method":"initialized"}""");
    }

    private static RekallAgeCodexProjectAgentRunner CreateRunner(
        string fixtureRoot,
        FakeCodexProcess process,
        RekallAgeCodexApprovalCallback? approvalCallback = null,
        string approvalPolicy = "on-request")
    {
        var cliPath = Path.Combine(fixtureRoot, "Rekall.Age.Cli.exe");
        File.WriteAllText(cliPath, "packaged-cli-fixture");
        return new RekallAgeCodexProjectAgentRunner(
            new RekallAgeCodexMcpConfiguration(cliPath),
            clientFactory: cancellationToken => RekallAgeCodexAppServerClient.StartAsync(
                TestAppServerOptions(),
                new FakeCodexProcessFactory(process),
                cancellationToken),
            approvalPolicy: approvalPolicy,
            approvalCallback: approvalCallback);
    }

    private static async Task<Task<RekallAgeProjectAgentSessionResult>> StartRunThroughTurnAsync(
        RekallAgeCodexProjectAgentRunner runner,
        FakeCodexProcess process,
        string projectRoot,
        IProgress<Rekall.Age.Agent.LanguageModels.RekallAgeLanguageModelAgentProgress>? progress,
        CancellationToken runCancellationToken,
        CancellationToken transcriptCancellationToken)
    {
        var run = runner.RunAsync(
            new RekallAgeProjectAgentSessionRequest(projectRoot, "Main", "gpt-5.6-sol", "Author a game."),
            progress,
            runCancellationToken).AsTask();
        await CompleteInitializeAsync(process, transcriptCancellationToken);
        AssertJson(
            await process.ReadClientLineAsync(transcriptCancellationToken),
            """{"id":2,"method":"account/read","params":{"refreshToken":false}}""");
        await process.WriteServerLineAsync(
            """{"id":2,"result":{"account":{"type":"chatgpt"},"requiresOpenaiAuth":false}}""");
        AssertJson(
            await process.ReadClientLineAsync(transcriptCancellationToken),
            """{"id":3,"method":"model/list","params":{"includeHidden":false,"limit":100}}""");
        await process.WriteServerLineAsync(ModelListResponse(3));
        var threadStart = JsonNode.Parse(
            await process.ReadClientLineAsync(transcriptCancellationToken))!.AsObject();
        Assert.Equal("thread/start", threadStart["method"]!.GetValue<string>());
        Assert.Equal(
            runner.ApprovalPolicy,
            threadStart["params"]!["approvalPolicy"]!.GetValue<string>());
        await process.WriteServerLineAsync(
            "{\"id\":4,\"result\":{\"thread\":{\"id\":\"thread-1\"},\"model\":\"gpt-5.6-sol\",\"cwd\":"
            + JsonValue.Create(Path.GetFullPath(projectRoot))!.ToJsonString()
            + "}}" );
        Assert.Equal(
            "turn/start",
            JsonNode.Parse(await process.ReadClientLineAsync(transcriptCancellationToken))!["method"]!.GetValue<string>());
        await process.WriteServerLineAsync(
            """{"id":5,"result":{"turn":{"id":"turn-1","status":"inProgress","items":[]}}}""");
        return run;
    }

    private static ValueTask CompleteTurnAsync(FakeCodexProcess process, string status) =>
        process.WriteServerLineAsync(
            "{\"method\":\"turn/completed\",\"params\":{\"threadId\":\"thread-1\",\"turn\":{\"id\":\"turn-1\",\"status\":"
            + JsonValue.Create(status)!.ToJsonString()
            + ",\"items\":[]}}}");

    private static string ModelListResponse(long id) =>
        "{\"id\":" + id
        + ",\"result\":{\"data\":[{\"id\":\"gpt-5.6-sol\",\"model\":\"gpt-5.6-sol\",\"displayName\":\"GPT-5.6 Sol\",\"description\":\"Agentic coding\",\"hidden\":false,\"isDefault\":true,\"defaultReasoningEffort\":\"medium\",\"supportedReasoningEfforts\":[{\"reasoningEffort\":\"medium\",\"description\":\"Balanced\"}]}],\"nextCursor\":null}}";

    private static RekallAgeCodexAppServerOptions TestAppServerOptions() => new()
    {
        ExecutablePath = "codex-test",
        ClientVersion = "test",
        ShutdownTimeout = TimeSpan.FromMilliseconds(40),
        InterruptTimeout = TimeSpan.FromMilliseconds(40)
    };

    private static void AssertJson(string actual, string expected)
    {
        var actualNode = JsonNode.Parse(actual);
        var expectedNode = JsonNode.Parse(expected);
        Assert.True(
            JsonNode.DeepEquals(expectedNode, actualNode),
            $"Expected JSON:{Environment.NewLine}{expectedNode}{Environment.NewLine}Actual JSON:{Environment.NewLine}{actualNode}");
    }

    private sealed class RecordingProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];

        public void Report(T value) => Values.Add(value);
    }
}
