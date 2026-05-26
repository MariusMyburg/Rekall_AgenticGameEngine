using System.Text.Json.Nodes;
using System.Net;
using System.Net.Sockets;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.Runtime.Commands;
using Rekall.Age.Runtime.Multiplayer;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Runtime;

public sealed class MultiplayerIpcTests
{
    [Fact]
    public async Task NamedPipeServerHandlesAuthoritativeMultiplayerRoundTrip()
    {
        var pipeName = $"rekall-age-multiplayer-test-{Guid.NewGuid():N}";
        var host = new RekallAgeMultiplayerAuthorityHost("session-1", CreateSession());
        await using var server = new RekallAgeMultiplayerNamedPipeServer(pipeName, host.HandleAsync);
        server.Start();
        await server.WaitUntilListeningAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        var client = new Rekall.Age.Runtime.Multiplayer.RekallAgeMultiplayerClient();
        var connect = await client.SendAsync(
            pipeName,
            new RekallAgeMultiplayerRequestEnvelope(
                "connect",
                Guid.NewGuid().ToString("N"),
                "F:/Game",
                "Main",
                new JsonObject
                {
                    ["clientId"] = "client-a",
                    ["displayName"] = "Alice"
                }),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);
        var input = await client.SendAsync(
            pipeName,
            new RekallAgeMultiplayerRequestEnvelope(
                "submit_input",
                Guid.NewGuid().ToString("N"),
                "F:/Game",
                "Main",
                new JsonObject
                {
                    ["clientId"] = "client-a",
                    ["sequence"] = 1,
                    ["networkId"] = "ship-1",
                    ["input"] = new JsonObject
                    {
                        ["pressedKeys"] = new JsonArray("W")
                    }
                }),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);
        var tick = await client.SendAsync(
            pipeName,
            new RekallAgeMultiplayerRequestEnvelope(
                "tick",
                Guid.NewGuid().ToString("N"),
                "F:/Game",
                "Main",
                new JsonObject { ["ticks"] = 2 }),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.True(connect.Ok, connect.ErrorMessage);
        Assert.True(input.Ok, input.ErrorMessage);
        Assert.True(input.Payload!["accepted"]!.GetValue<bool>());
        Assert.True(tick.Ok, tick.ErrorMessage);
        Assert.Equal(2, tick.Payload!["serverTick"]!.GetValue<int>());
        Assert.Equal(1, tick.Payload["clientCount"]!.GetValue<int>());
        Assert.Equal("ship-1", tick.Payload["snapshot"]!["entities"]![0]!["networkId"]!.GetValue<string>());
    }

    [Fact]
    public async Task WebSocketServerHandlesAuthoritativeMultiplayerRoundTrip()
    {
        var endpoint = RekallAgeMultiplayerEndpoint.ResolveWebSocketUri("127.0.0.1", GetFreeTcpPort());
        var host = new RekallAgeMultiplayerAuthorityHost("session-ws", CreateSession());
        await using var server = new RekallAgeMultiplayerWebSocketServer(endpoint, host.HandleAsync);
        server.Start();
        await server.WaitUntilListeningAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        var client = new RekallAgeMultiplayerWebSocketClient();
        var connect = await client.SendAsync(
            endpoint,
            new RekallAgeMultiplayerRequestEnvelope(
                "connect",
                Guid.NewGuid().ToString("N"),
                "F:/Game",
                "Main",
                new JsonObject
                {
                    ["clientId"] = "client-a",
                    ["displayName"] = "Alice"
                }),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);
        var snapshot = await client.SendAsync(
            endpoint,
            new RekallAgeMultiplayerRequestEnvelope(
                "snapshot",
                Guid.NewGuid().ToString("N"),
                "F:/Game",
                "Main",
                null),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.True(connect.Ok, connect.ErrorMessage);
        Assert.True(snapshot.Ok, snapshot.ErrorMessage);
        Assert.Equal("session-ws", snapshot.Payload!["sessionId"]!.GetValue<string>());
        Assert.Equal("ship-1", snapshot.Payload["snapshot"]!["entities"]![0]!["networkId"]!.GetValue<string>());
    }

    [Fact]
    public async Task WebSocketClientRetriesUntilServerStartsWithinTimeout()
    {
        var endpoint = RekallAgeMultiplayerEndpoint.ResolveWebSocketUri("127.0.0.1", GetFreeTcpPort());
        var host = new RekallAgeMultiplayerAuthorityHost("session-delayed-ws", CreateSession());
        await using var server = new RekallAgeMultiplayerWebSocketServer(endpoint, host.HandleAsync);
        var startTask = Task.Run(async () =>
        {
            await Task.Delay(1500);
            server.Start();
            await server.WaitUntilListeningAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        });

        var client = new RekallAgeMultiplayerWebSocketClient();
        var response = await client.SendAsync(
            endpoint,
            new RekallAgeMultiplayerRequestEnvelope(
                "snapshot",
                Guid.NewGuid().ToString("N"),
                "F:/Game",
                "Main",
                null),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        await startTask;
        Assert.True(response.Ok, response.ErrorMessage);
        Assert.Equal("session-delayed-ws", response.Payload!["sessionId"]!.GetValue<string>());
    }

    [Fact]
    public async Task MultiplayerCommandsCanTargetWebSocketAuthoritativeSession()
    {
        var endpoint = RekallAgeMultiplayerEndpoint.ResolveWebSocketUri("127.0.0.1", GetFreeTcpPort());
        var host = new RekallAgeMultiplayerAuthorityHost("session-command-ws", CreateSession());
        await using var server = new RekallAgeMultiplayerWebSocketServer(endpoint, host.HandleAsync);
        server.Start();
        await server.WaitUntilListeningAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        var context = new RekallAgeCommandContext(
            "test",
            RekallAgeTransaction.Begin("websocket multiplayer commands"),
            CancellationToken.None);

        var connect = await new MultiplayerConnectCommand().ExecuteAsync(
            new MultiplayerConnectRequest(
                "F:/Game",
                "Main",
                "client-a",
                "Alice",
                Transport: "websocket",
                Endpoint: endpoint.ToString(),
                TimeoutMilliseconds: 5000),
            context);
        var tick = await new MultiplayerTickCommand().ExecuteAsync(
            new MultiplayerTickRequest(
                "F:/Game",
                "Main",
                1,
                Transport: "websocket",
                Endpoint: endpoint.ToString(),
                TimeoutMilliseconds: 5000),
            context);

        Assert.True(connect.Ok, connect.Summary);
        Assert.Equal("websocket", connect.Value.Transport);
        Assert.Equal(endpoint.ToString(), connect.Value.Endpoint);
        Assert.Equal("session-command-ws", connect.Value.SessionId);
        Assert.True(tick.Ok, tick.Summary);
        Assert.Equal(1, tick.Value.ServerTick);
    }

    [Fact]
    public async Task MultiplayerCommandsTargetRunningAuthoritativeSession()
    {
        var pipeName = $"rekall-age-multiplayer-test-{Guid.NewGuid():N}";
        var host = new RekallAgeMultiplayerAuthorityHost("session-2", CreateSession());
        await using var server = new RekallAgeMultiplayerNamedPipeServer(pipeName, host.HandleAsync);
        server.Start();
        await server.WaitUntilListeningAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        var context = new RekallAgeCommandContext(
            "test",
            RekallAgeTransaction.Begin("multiplayer commands"),
            CancellationToken.None);

        var connect = await new MultiplayerConnectCommand().ExecuteAsync(
            new MultiplayerConnectRequest("F:/Game", "Main", "client-a", "Alice", pipeName, TimeoutMilliseconds: 5000),
            context);
        var submit = await new MultiplayerSubmitInputCommand().ExecuteAsync(
            new MultiplayerSubmitInputRequest(
                "F:/Game",
                "Main",
                "client-a",
                1,
                "ship-1",
                new RekallAgeRuntimeInputFrame(PressedKeys: ["W"]),
                PipeName: pipeName,
                TimeoutMilliseconds: 5000),
            context);
        var tick = await new MultiplayerTickCommand().ExecuteAsync(
            new MultiplayerTickRequest("F:/Game", "Main", 1, pipeName, TimeoutMilliseconds: 5000),
            context);

        Assert.True(connect.Ok, connect.Summary);
        Assert.Equal("session-2", connect.Value.SessionId);
        Assert.True(submit.Ok, submit.Summary);
        Assert.True(submit.Value.Accepted);
        Assert.Equal("accepted", submit.Value.Reason);
        Assert.True(tick.Ok, tick.Summary);
        Assert.Equal(1, tick.Value.ServerTick);
        Assert.NotNull(tick.Value.Snapshot);
        Assert.Equal("ship-1", Assert.Single(tick.Value.Snapshot.Entities).NetworkId);
    }

    private static RekallAgeAuthoritativeMultiplayerSession CreateSession()
    {
        var scene = RekallAgeSceneDocument.Create("Arena", ["world", "multiplayer"])
            .AddEntity(RekallAgeEntityDocument.Create("Player Ship", ["player"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject { ["x"] = 4, ["z"] = -2 }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.NetworkIdentity", new JsonObject
                {
                    ["networkId"] = "ship-1",
                    ["ownerClientId"] = "client-a",
                    ["authority"] = "server"
                }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.NetworkTransform", new JsonObject())));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);
        return new RekallAgeAuthoritativeMultiplayerSession(world, RekallAgeRuntimeExecutionLoop.CreateDefault());
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
