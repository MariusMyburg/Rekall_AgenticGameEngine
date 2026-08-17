using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using System.Text.Json.Nodes;

namespace Rekall.Age.Tests.Core;

public sealed class DynamicCommandDispatchTests
{
    [Fact]
    public async Task RegistryExecutesRegisteredCommandFromJsonArguments()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new EchoCommand());
        var context = new RekallAgeCommandContext("mcp", RekallAgeTransaction.Begin("dynamic"), CancellationToken.None);

        var result = await registry.ExecuteJsonAsync("rekall.test.echo", """{"message":"hello"}""", context);

        Assert.True(result.Ok, result.Summary);
        var value = Assert.IsType<EchoResult>(result.Value);
        Assert.Equal("hello from command", value.Message);
        Assert.Equal(context.Transaction.Id, result.Transaction.Id);
        Assert.Equal("dynamic", result.Transaction.Name);
        Assert.Equal("mcp", result.Transaction.Actor);
        Assert.Contains("echo:hello", result.Transaction.ChangedResources);
    }

    [Fact]
    public void RegistryExposesRegisteredCommandTypesForProtocolAdapters()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new EchoCommand());

        var command = Assert.Single(registry.RegisteredCommands);

        Assert.Equal("rekall.test.echo", command.Schema.Name);
        Assert.Equal(typeof(EchoRequest), command.RequestType);
        Assert.Equal(typeof(EchoResult), command.ResultType);
    }

    [Fact]
    public async Task RegistryReportsMissingRequiredJsonFieldsBeforeCommandExecution()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new EchoCommand());
        var context = new RekallAgeCommandContext("mcp", RekallAgeTransaction.Begin("missing argument"), CancellationToken.None);

        var result = await registry.ExecuteJsonAsync("rekall.test.echo", "{}", context);

        Assert.False(result.Ok);
        var error = Assert.Single(result.Errors);
        Assert.Equal("REKALL_COMMAND_ARGUMENT_REQUIRED", error.Code);
        Assert.Contains("message", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(context.Transaction.ChangedResources);
    }

    [Fact]
    public async Task RegistryNormalizesModelEncodedTypedFieldsWithoutRewritingStrings()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new NormalizeCommand());
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("normalize arguments"), CancellationToken.None);

        var result = await registry.ExecuteJsonAsync(
            "rekall.test.normalize",
            """{"limit":"50","active":"true","tags":"[\"ui\",\"audio\"]","entities":"[{\"name\":\"HUD\"}]","settings":"{\"speed\":7}","points":"[1,2,3]","literal":"[\"must-remain-a-string\"]"}""",
            context);

        Assert.True(result.Ok, result.Summary);
        var value = Assert.IsType<NormalizeResult>(result.Value);
        Assert.Equal(50, value.Limit);
        Assert.True(value.Active);
        Assert.Equal(["ui", "audio"], value.Tags);
        Assert.Equal("HUD", Assert.Single(value.Entities).Name);
        Assert.Equal(7, value.Settings["speed"]!.GetValue<int>());
        Assert.Equal(3, value.Points.Count);
        Assert.Equal("[\"must-remain-a-string\"]", value.Literal);
    }

    private sealed record EchoRequest(string Message);

    private sealed record EchoResult(string Message);

    private sealed record NormalizeRequest(
        int Limit,
        bool Active,
        IReadOnlyList<string> Tags,
        IReadOnlyList<NormalizeEntity> Entities,
        JsonObject Settings,
        JsonArray Points,
        string Literal);

    private sealed record NormalizeEntity(string Name);

    private sealed record NormalizeResult(
        int Limit,
        bool Active,
        IReadOnlyList<string> Tags,
        IReadOnlyList<NormalizeEntity> Entities,
        JsonObject Settings,
        JsonArray Points,
        string Literal);

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
            context.Transaction.RecordChangedResource($"echo:{request.Message}");
            return ValueTask.FromResult(RekallAgeCommandResult<EchoResult>.Success(
                new EchoResult($"{request.Message} from command"),
                "Echoed message."));
        }
    }

    private sealed class NormalizeCommand : IRekallAgeCommand<NormalizeRequest, NormalizeResult>
    {
        public string Name => "rekall.test.normalize";

        public RekallAgeCommandSchema Schema => new(
            Name,
            "Normalizes model arguments.",
            typeof(NormalizeRequest).FullName!,
            typeof(NormalizeResult).FullName!);

        public ValueTask<RekallAgeCommandResult<NormalizeResult>> ExecuteAsync(
            NormalizeRequest request,
            RekallAgeCommandContext context)
        {
            return ValueTask.FromResult(RekallAgeCommandResult<NormalizeResult>.Success(
                new NormalizeResult(
                    request.Limit,
                    request.Active,
                    request.Tags,
                    request.Entities,
                    request.Settings,
                    request.Points,
                    request.Literal),
                "Normalized arguments."));
        }
    }
}
