using System.Text.Json;
using Rekall.Age.Agent.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Mcp;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.Workflows.Commands;

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
}
