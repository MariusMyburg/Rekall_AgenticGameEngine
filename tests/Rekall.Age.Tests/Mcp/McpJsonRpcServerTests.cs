using System.Text.Json;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Mcp;
using Rekall.Age.Rendering;

namespace Rekall.Age.Tests.Mcp;

public sealed class McpJsonRpcServerTests
{
    [Fact]
    public async Task InitializeReturnsToolCapability()
    {
        var server = CreateServer();
        var response = await server.HandleJsonLineAsync(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""",
            CreateContext());

        using var document = JsonDocument.Parse(response!);
        Assert.Equal("2025-06-18", document.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());
        Assert.True(document.RootElement.GetProperty("result").GetProperty("capabilities").TryGetProperty("tools", out _));
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
    }

    [Fact]
    public async Task ToolsListDoesNotRequireOptionalRecordParameters()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new OptionalCommand());
        var server = new RekallAgeMcpJsonRpcServer(registry);

        var response = await server.HandleJsonLineAsync(
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""",
            CreateContext());

        using var document = JsonDocument.Parse(response!);
        var required = document.RootElement
            .GetProperty("result")
            .GetProperty("tools")[0]
            .GetProperty("inputSchema")
            .GetProperty("required")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        Assert.Contains("required", required);
        Assert.DoesNotContain("optional", required);
    }

    [Fact]
    public async Task ToolsListExposesNestedValueObjectProperties()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new ClearColorSchemaCommand());
        var server = new RekallAgeMcpJsonRpcServer(registry);

        var response = await server.HandleJsonLineAsync(
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""",
            CreateContext());

        using var document = JsonDocument.Parse(response!);
        var clearColor = document.RootElement
            .GetProperty("result")
            .GetProperty("tools")[0]
            .GetProperty("inputSchema")
            .GetProperty("properties")
            .GetProperty("clearColor");
        Assert.Equal("object", clearColor.GetProperty("type").GetString());
        var properties = clearColor.GetProperty("properties");
        Assert.Equal("number", properties.GetProperty("r").GetProperty("type").GetString());
        Assert.Equal("number", properties.GetProperty("g").GetProperty("type").GetString());
        Assert.Equal("number", properties.GetProperty("b").GetProperty("type").GetString());
        Assert.Equal("number", properties.GetProperty("a").GetProperty("type").GetString());
    }


    [Fact]
    public async Task JsonLinesCanStartWithUtf8Bom()
    {
        var server = CreateServer();

        var response = await server.HandleJsonLineAsync(
            "\uFEFF{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}",
            CreateContext());

        using var document = JsonDocument.Parse(response!);
        Assert.Equal("rekall.test.echo", document.RootElement.GetProperty("result").GetProperty("tools")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task ToolsCallExecutesRegisteredCommand()
    {
        var server = CreateServer();

        var response = await server.HandleJsonLineAsync(
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"rekall.test.echo","arguments":{"message":"ping"}}}""",
            CreateContext());

        using var document = JsonDocument.Parse(response!);
        var result = document.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("isError").GetBoolean());
        Assert.Contains("Echoed message.", result.GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("ping from command", result.GetProperty("structuredContent").GetProperty("value").GetProperty("message").GetString());
    }

    private static RekallAgeMcpJsonRpcServer CreateServer()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new EchoCommand());
        return new RekallAgeMcpJsonRpcServer(registry);
    }

    private static RekallAgeCommandContext CreateContext()
    {
        return new RekallAgeCommandContext("mcp-test", RekallAgeTransaction.Begin("mcp"), CancellationToken.None);
    }

    private sealed record EchoRequest(string Message);

    private sealed record EchoResult(string Message);

    private sealed record OptionalRequest(string Required, string? Optional = null);

    private sealed record OptionalResult(string Value);

    private sealed record ClearColorSchemaRequest(RekallAgeVulkanClearColor? ClearColor = null);

    private sealed record ClearColorSchemaResult(RekallAgeVulkanClearColor? ClearColor);

    private sealed class EchoCommand : IRekallAgeCommand<EchoRequest, EchoResult>
    {
        public string Name => "rekall.test.echo";

        public RekallAgeCommandSchema Schema => new(
            Name,
            "Echoes a message.",
            typeof(EchoRequest).FullName!,
            typeof(EchoResult).FullName!);

        public ValueTask<RekallAgeCommandResult<EchoResult>> ExecuteAsync(
            EchoRequest request,
            RekallAgeCommandContext context)
        {
            return ValueTask.FromResult(RekallAgeCommandResult<EchoResult>.Success(
                new EchoResult($"{request.Message} from command"),
                "Echoed message."));
        }
    }

    private sealed class OptionalCommand : IRekallAgeCommand<OptionalRequest, OptionalResult>
    {
        public string Name => "rekall.test.optional";

        public RekallAgeCommandSchema Schema => new(
            Name,
            "Tests optional schema fields.",
            typeof(OptionalRequest).FullName!,
            typeof(OptionalResult).FullName!);

        public ValueTask<RekallAgeCommandResult<OptionalResult>> ExecuteAsync(
            OptionalRequest request,
            RekallAgeCommandContext context)
        {
            return ValueTask.FromResult(RekallAgeCommandResult<OptionalResult>.Success(new OptionalResult(request.Required)));
        }
    }

    private sealed class ClearColorSchemaCommand : IRekallAgeCommand<ClearColorSchemaRequest, ClearColorSchemaResult>
    {
        public string Name => "rekall.test.clear_color_schema";

        public RekallAgeCommandSchema Schema => new(
            Name,
            "Tests nested value object schema fields.",
            typeof(ClearColorSchemaRequest).FullName!,
            typeof(ClearColorSchemaResult).FullName!);

        public ValueTask<RekallAgeCommandResult<ClearColorSchemaResult>> ExecuteAsync(
            ClearColorSchemaRequest request,
            RekallAgeCommandContext context)
        {
            return ValueTask.FromResult(RekallAgeCommandResult<ClearColorSchemaResult>.Success(new ClearColorSchemaResult(request.ClearColor)));
        }
    }
}
