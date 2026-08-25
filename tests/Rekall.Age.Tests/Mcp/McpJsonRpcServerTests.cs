using System.Text.Json;
using Rekall.Age.Agent.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Mcp;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.Runtime.Commands;
using Rekall.Age.Workflows.Commands;
using Rekall.Age.World.Commands;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Mcp;

public sealed class McpJsonRpcServerTests
{
    [Fact]
    public async Task InitializeDescribesGenericAgentFirstWorkflow()
    {
        var server = CreateServer();
        var response = await server.HandleJsonLineAsync(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""",
            CreateContext());

        using var document = JsonDocument.Parse(response!);
        Assert.Equal("2025-06-18", document.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());
        Assert.True(document.RootElement.GetProperty("result").GetProperty("capabilities").TryGetProperty("tools", out _));
        var instructions = document.RootElement.GetProperty("result").GetProperty("instructions").GetString();
        Assert.Contains("rekall.context.engine_status", instructions, StringComparison.Ordinal);
        Assert.Contains("rekall.module.scaffold_runtime_system", instructions, StringComparison.Ordinal);
        Assert.Contains("rekall.workflow.package_playable_game", instructions, StringComparison.Ordinal);
        Assert.DoesNotContain("rekall.templates", instructions, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("template", instructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rekall.workflow.agent_authoring_gauntlet", instructions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToolsListReturnsRegisteredCommandsWithInputSchema()
    {
        var server = CreateServer();

        var response = await server.HandleJsonLineAsync(
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""",
            CreateContext());

        using var document = JsonDocument.Parse(response!);
        var tool = document.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().Single();
        Assert.Equal("rekall.test.echo", tool.GetProperty("name").GetString());
        Assert.Equal("object", tool.GetProperty("inputSchema").GetProperty("type").GetString());
        Assert.True(tool.GetProperty("inputSchema").GetProperty("properties").TryGetProperty("message", out _));
        Assert.Equal("unknown", tool.GetProperty("rekallCategory").GetString());
        Assert.False(tool.GetProperty("rekallRecommended").GetBoolean());
    }

    [Fact]
    public async Task MutationSchemaExposesOptionalExpectedRevision()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new CreateEntityCommand());
        var server = new RekallAgeMcpJsonRpcServer(registry);

        var response = await server.HandleJsonLineAsync(
            """{"jsonrpc":"2.0","id":21,"method":"tools/list"}""",
            CreateContext());

        using var document = JsonDocument.Parse(response!);
        var schema = document.RootElement.GetProperty("result").GetProperty("tools")[0].GetProperty("inputSchema");
        Assert.Equal("string", schema.GetProperty("properties").GetProperty("expectedRevision").GetProperty("type").GetString());
        Assert.DoesNotContain(
            schema.GetProperty("required").EnumerateArray(),
            item => item.GetString() == "expectedRevision");
    }

    [Fact]
    public async Task RuntimeInspectionSchemaExposesTypedSemanticActionSamples()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new InspectSceneRuntimeCommand());
        var server = new RekallAgeMcpJsonRpcServer(registry);

        var response = await server.HandleJsonLineAsync(
            """{"jsonrpc":"2.0","id":24,"method":"tools/list"}""",
            CreateContext());

        using var document = JsonDocument.Parse(response!);
        var schema = document.RootElement.GetProperty("result").GetProperty("tools")[0].GetProperty("inputSchema");
        var inputFrame = schema.GetProperty("properties").GetProperty("inputs").GetProperty("items");
        var semanticAction = inputFrame.GetProperty("properties").GetProperty("semanticActions").GetProperty("items");
        Assert.Equal("string", semanticAction.GetProperty("properties").GetProperty("name").GetProperty("type").GetString());
        Assert.Equal("number", semanticAction.GetProperty("properties").GetProperty("value").GetProperty("type").GetString());
        Assert.Equal("boolean", semanticAction.GetProperty("properties").GetProperty("isDown").GetProperty("type").GetString());
        Assert.Contains(semanticAction.GetProperty("required").EnumerateArray(), item => item.GetString() == "name");
        var controller = inputFrame.GetProperty("properties").GetProperty("controllers").GetProperty("items");
        Assert.Equal("string", controller.GetProperty("properties").GetProperty("deviceId").GetProperty("type").GetString());
        Assert.Equal("array", controller.GetProperty("properties").GetProperty("axes").GetProperty("type").GetString());
        Assert.Contains(controller.GetProperty("required").EnumerateArray(), item => item.GetString() == "deviceId");
    }

    [Fact]
    public async Task InputAuthoringCommandsAreTypedMcpTools()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new InspectInputBindingsCommand());
        registry.Register(new RebindInputActionCommand());
        var server = new RekallAgeMcpJsonRpcServer(registry);

        var response = await server.HandleJsonLineAsync(
            """{"jsonrpc":"2.0","id":25,"method":"tools/list"}""",
            CreateContext());

        using var document = JsonDocument.Parse(response!);
        var tools = document.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().ToArray();
        var inspect = tools.Single(tool => tool.GetProperty("name").GetString() == "rekall.input.inspect_bindings");
        var rebind = tools.Single(tool => tool.GetProperty("name").GetString() == "rekall.input.rebind_action");
        Assert.Equal("input", inspect.GetProperty("rekallCategory").GetString());
        Assert.True(inspect.GetProperty("rekallRecommended").GetBoolean());
        Assert.True(rebind.GetProperty("inputSchema").GetProperty("properties").TryGetProperty("binding", out var binding));
        Assert.Equal(JsonValueKind.Object, binding.ValueKind);
    }

    [Fact]
    public async Task RecoverySchemasExposeGenericTargetAndRevisionContracts()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new InspectDocumentRecoveryCommand());
        registry.Register(new RestoreDocumentRecoveryCommand());
        var server = new RekallAgeMcpJsonRpcServer(registry);

        var response = await server.HandleJsonLineAsync(
            """{"jsonrpc":"2.0","id":22,"method":"tools/list"}""",
            CreateContext());

        using var document = JsonDocument.Parse(response!);
        var tools = document.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().ToArray();
        var inspect = tools.Single(tool => tool.GetProperty("name").GetString() == "rekall.recovery.inspect_document");
        var restore = tools.Single(tool => tool.GetProperty("name").GetString() == "rekall.recovery.restore_document");
        Assert.Equal("recovery", inspect.GetProperty("rekallCategory").GetString());
        Assert.True(inspect.GetProperty("rekallRecommended").GetBoolean());
        Assert.Contains(inspect.GetProperty("inputSchema").GetProperty("required").EnumerateArray(), item => item.GetString() == "documentKind");
        Assert.DoesNotContain(inspect.GetProperty("inputSchema").GetProperty("required").EnumerateArray(), item => item.GetString() == "sceneName");
        Assert.Contains(restore.GetProperty("inputSchema").GetProperty("required").EnumerateArray(), item => item.GetString() == "expectedRevision");
        Assert.DoesNotContain(restore.GetProperty("inputSchema").GetProperty("required").EnumerateArray(), item => item.GetString() == "sceneName");
    }

    [Fact]
    public async Task McpExecutesReadOnlyRecoveryInspectionWithStructuredStatus()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeSceneStore();
        await store.SaveAsync(root, RekallAgeSceneDocument.Create("Main", ["world"]), CancellationToken.None);
        var loaded = await store.LoadVersionedAsync(root, "Main", CancellationToken.None);
        await store.SaveIfRevisionAsync(root, loaded.Value with { Id = "replacement" }, loaded.Revision, CancellationToken.None);
        await File.WriteAllTextAsync(store.GetScenePath(root, "Main"), "{ mcp damage");
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new InspectDocumentRecoveryCommand());
        var server = new RekallAgeMcpJsonRpcServer(registry);
        var request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 23,
            method = "tools/call",
            @params = new
            {
                name = "rekall.recovery.inspect_document",
                arguments = new { projectRoot = root, documentKind = "scene", sceneName = "Main" }
            }
        });

        var response = await server.HandleJsonLineAsync(request, CreateContext());

        using var document = JsonDocument.Parse(response!);
        var content = document.RootElement.GetProperty("result").GetProperty("structuredContent");
        Assert.True(content.GetProperty("ok").GetBoolean());
        Assert.True(content.GetProperty("value").GetProperty("recoverable").GetBoolean());
        Assert.Equal(
            "REKALL_DOCUMENT_JSON_MALFORMED",
            content.GetProperty("value").GetProperty("primary").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ToolsListExposesGenericWorkflowMetadataWithoutTemplates()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new GetEngineStatusCommand());
        registry.Register(new RunAgentAuthoringGauntletCommand());
        registry.Register(new PackagePlayableGameCommand());
        registry.Register(new AuditPlayablePackageCommand());
        registry.Register(new CreateRenderPlanCommand());
        var server = new RekallAgeMcpJsonRpcServer(registry);

        var response = await server.HandleJsonLineAsync(
            """{"jsonrpc":"2.0","id":20,"method":"tools/list"}""",
            CreateContext());

        using var document = JsonDocument.Parse(response!);
        var tools = document.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().ToArray();
        var engineStatus = tools.Single(tool => tool.GetProperty("name").GetString() == "rekall.context.engine_status");
        var gauntlet = tools.Single(tool => tool.GetProperty("name").GetString() == "rekall.workflow.agent_authoring_gauntlet");
        var package = tools.Single(tool => tool.GetProperty("name").GetString() == "rekall.workflow.package_playable_game");
        var audit = tools.Single(tool => tool.GetProperty("name").GetString() == "rekall.workflow.audit_playable_package");
        var render = tools.Single(tool => tool.GetProperty("name").GetString() == "rekall.render.plan.create");
        Assert.Equal("rekall.context.engine_status", tools[0].GetProperty("name").GetString());
        Assert.Equal("context", engineStatus.GetProperty("rekallCategory").GetString());
        Assert.True(engineStatus.GetProperty("rekallRecommended").GetBoolean());
        Assert.Equal("workflow", gauntlet.GetProperty("rekallCategory").GetString());
        Assert.True(gauntlet.GetProperty("rekallRecommended").GetBoolean());
        Assert.True(package.GetProperty("rekallAgentPriority").GetInt32() > gauntlet.GetProperty("rekallAgentPriority").GetInt32());
        Assert.Equal("workflow", package.GetProperty("rekallCategory").GetString());
        Assert.True(package.GetProperty("rekallRecommended").GetBoolean());
        Assert.True(audit.GetProperty("rekallAgentPriority").GetInt32() > package.GetProperty("rekallAgentPriority").GetInt32());
        Assert.True(render.GetProperty("rekallAgentPriority").GetInt32() > package.GetProperty("rekallAgentPriority").GetInt32());
        Assert.DoesNotContain(tools, tool => tool.GetProperty("name").GetString()?.Contains("template", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task ToolsCallExecutesRegisteredCommand()
    {
        var server = CreateServer();

        var response = await server.HandleJsonLineAsync(
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"rekall.test.echo","arguments":{"message":"hello"}}}""",
            CreateContext());

        using var document = JsonDocument.Parse(response!);
        var result = document.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("isError").GetBoolean());
        Assert.Equal("hello", result.GetProperty("structuredContent").GetProperty("value").GetProperty("message").GetString());
    }

    [Fact]
    public async Task ToolsCallKeepsStructuredAgeErrorsVisibleToMcpClients()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new FailingEchoCommand());
        var server = new RekallAgeMcpJsonRpcServer(registry);

        var response = await server.HandleJsonLineAsync(
            """{"jsonrpc":"2.0","id":31,"method":"tools/call","params":{"name":"rekall.test.failure","arguments":{"message":"strict delta was zero"}}}""",
            CreateContext());

        using var document = JsonDocument.Parse(response!);
        var result = document.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
        var structured = result.GetProperty("structuredContent");
        Assert.False(structured.GetProperty("ok").GetBoolean());
        Assert.Equal(
            "REKALL_RUNTIME_ASSERTION_FAILED",
            structured.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Contains(
            "REKALL_RUNTIME_ASSERTION_FAILED",
            result.GetProperty("content")[0].GetProperty("text").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToolsCallReturnsJsonRpcErrorForUnknownTool()
    {
        var server = CreateServer();

        var response = await server.HandleJsonLineAsync(
            """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"rekall.missing","arguments":{}}}""",
            CreateContext());

        using var document = JsonDocument.Parse(response!);
        Assert.Equal(-32602, document.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    private static RekallAgeMcpJsonRpcServer CreateServer()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new EchoCommand());
        return new RekallAgeMcpJsonRpcServer(registry);
    }

    private static RekallAgeCommandContext CreateContext()
    {
        return new RekallAgeCommandContext(
            "mcp-test",
            RekallAgeTransaction.Begin("mcp test"),
            CancellationToken.None);
    }

    private sealed record EchoRequest(string Message);

    private sealed record EchoResult(string Message);

    private sealed class EchoCommand : IRekallAgeCommand<EchoRequest, EchoResult>
    {
        public string Name => "rekall.test.echo";

        public RekallAgeCommandSchema Schema => new(
            Name,
            "Echoes a test message.",
            typeof(EchoRequest).FullName!,
            typeof(EchoResult).FullName!);

        public ValueTask<RekallAgeCommandResult<EchoResult>> ExecuteAsync(
            EchoRequest request,
            RekallAgeCommandContext context)
        {
            return ValueTask.FromResult(RekallAgeCommandResult<EchoResult>.Success(
                new EchoResult(request.Message),
                "Echoed test message."));
        }
    }

    private sealed class FailingEchoCommand : IRekallAgeCommand<EchoRequest, EchoResult>
    {
        public string Name => "rekall.test.failure";

        public RekallAgeCommandSchema Schema => new(
            Name,
            "Returns a deterministic structured AGE failure.",
            typeof(EchoRequest).FullName!,
            typeof(EchoResult).FullName!);

        public ValueTask<RekallAgeCommandResult<EchoResult>> ExecuteAsync(
            EchoRequest request,
            RekallAgeCommandContext context)
        {
            var error = new RekallAgeCommandError(
                "REKALL_RUNTIME_ASSERTION_FAILED",
                request.Message,
                "Main");
            return ValueTask.FromResult(RekallAgeCommandResult<EchoResult>.Failure(
                new EchoResult(request.Message),
                "Runtime assertion failed.",
                [error]));
        }
    }
}
