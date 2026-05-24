using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;

namespace Rekall.Age.Tests.Core;

public sealed class CommandBusTests
{
    [Fact]
    public async Task RegisteredCommandExecutesInsideTransaction()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new EchoCommand());

        var transaction = RekallAgeTransaction.Begin("echo test");
        var context = new RekallAgeCommandContext("agent", transaction, CancellationToken.None);

        var result = await registry.ExecuteAsync<EchoRequest, EchoResult>(
            "test.echo",
            new EchoRequest("hello"),
            context);

        Assert.True(result.Ok);
        Assert.Equal("hello", result.Value.Message);
        Assert.Contains("echo:hello", transaction.ChangedResources);
    }

    [Fact]
    public async Task UnknownCommandReturnsStructuredFailure()
    {
        var registry = new RekallAgeCommandRegistry();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("missing"), CancellationToken.None);

        var result = await registry.ExecuteAsync<EchoRequest, EchoResult>(
            "test.missing",
            new EchoRequest("hello"),
            context);

        Assert.False(result.Ok);
        Assert.Equal("REKALL_COMMAND_NOT_FOUND", Assert.Single(result.Errors).Code);
    }

    private sealed record EchoRequest(string Message);

    private sealed record EchoResult(string Message);

    private sealed class EchoCommand : IRekallAgeCommand<EchoRequest, EchoResult>
    {
        public string Name => "test.echo";

        public RekallAgeCommandSchema Schema => new(
            Name,
            "Echoes a message for command-bus tests.",
            typeof(EchoRequest).FullName!,
            typeof(EchoResult).FullName!);

        public ValueTask<RekallAgeCommandResult<EchoResult>> ExecuteAsync(
            EchoRequest request,
            RekallAgeCommandContext context)
        {
            context.Transaction.RecordChangedResource($"echo:{request.Message}");
            return ValueTask.FromResult(RekallAgeCommandResult<EchoResult>.Success(new EchoResult(request.Message)));
        }
    }
}
