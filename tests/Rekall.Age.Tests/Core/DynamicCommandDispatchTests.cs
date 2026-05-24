using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;

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

    private sealed record EchoRequest(string Message);

    private sealed record EchoResult(string Message);

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
}
