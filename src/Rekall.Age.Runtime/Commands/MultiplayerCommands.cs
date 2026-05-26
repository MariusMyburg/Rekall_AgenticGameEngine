using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.Runtime.Multiplayer;
using MultiplayerPipeClient = Rekall.Age.Runtime.Multiplayer.RekallAgeMultiplayerClient;

namespace Rekall.Age.Runtime.Commands;

public sealed record MultiplayerStatusRequest(
    string ProjectRoot,
    string SceneName,
    string? PipeName = null,
    string? SessionId = null,
    int TimeoutMilliseconds = 2000,
    string Transport = "pipe",
    string? Endpoint = null);

public sealed record MultiplayerConnectRequest(
    string ProjectRoot,
    string SceneName,
    string ClientId,
    string? DisplayName = null,
    string? PipeName = null,
    string? SessionId = null,
    int TimeoutMilliseconds = 2000,
    string Transport = "pipe",
    string? Endpoint = null);

public sealed record MultiplayerDisconnectRequest(
    string ProjectRoot,
    string SceneName,
    string ClientId,
    string? PipeName = null,
    string? SessionId = null,
    int TimeoutMilliseconds = 2000,
    string Transport = "pipe",
    string? Endpoint = null);

public sealed record MultiplayerSubmitInputRequest(
    string ProjectRoot,
    string SceneName,
    string ClientId,
    int Sequence,
    string NetworkId,
    RekallAgeRuntimeInputFrame Input,
    double ClientTimeSeconds = 0,
    string? PipeName = null,
    string? SessionId = null,
    int TimeoutMilliseconds = 2000,
    string Transport = "pipe",
    string? Endpoint = null);

public sealed record MultiplayerTickRequest(
    string ProjectRoot,
    string SceneName,
    int Ticks = 1,
    string? PipeName = null,
    string? SessionId = null,
    int TimeoutMilliseconds = 5000,
    string Transport = "pipe",
    string? Endpoint = null);

public sealed record MultiplayerSnapshotRequest(
    string ProjectRoot,
    string SceneName,
    string? PipeName = null,
    string? SessionId = null,
    int TimeoutMilliseconds = 2000,
    string Transport = "pipe",
    string? Endpoint = null);

public sealed record MultiplayerDeltaRequest(
    string ProjectRoot,
    string SceneName,
    int FromServerTick,
    string? PipeName = null,
    string? SessionId = null,
    int TimeoutMilliseconds = 2000,
    string Transport = "pipe",
    string? Endpoint = null);

public sealed record MultiplayerHostRequest(
    string ProjectRoot,
    string SceneName,
    double DurationSeconds = 30,
    string? PipeName = null,
    string? SessionId = null,
    string Transport = "pipe",
    string Address = "127.0.0.1",
    int Port = 7777,
    string? Endpoint = null);

public sealed record MultiplayerCommandResult(
    bool Connected,
    string Transport,
    string PipeName,
    string Endpoint,
    string? SessionId,
    string Operation,
    bool Applied,
    bool Accepted,
    string? Reason,
    int ServerTick,
    double ServerTimeSeconds,
    int ClientCount,
    int EntityCount,
    RekallAgeMultiplayerSnapshot? Snapshot,
    RekallAgeMultiplayerSnapshotDelta? Delta,
    string Message);

public sealed record MultiplayerHostResult(
    bool Hosted,
    string Transport,
    string PipeName,
    string Endpoint,
    string SessionId,
    string SceneName,
    double DurationSeconds,
    int NetworkEntityCount,
    string Message);

public sealed class MultiplayerHostCommand : IRekallAgeCommand<MultiplayerHostRequest, MultiplayerHostResult>
{
    public string Name => "rekall.multiplayer.host";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Hosts a local server-authoritative multiplayer session for a Rekall AGE scene over IPC.",
        typeof(MultiplayerHostRequest).FullName!,
        typeof(MultiplayerHostResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<MultiplayerHostResult>> ExecuteAsync(
        MultiplayerHostRequest request,
        RekallAgeCommandContext context)
    {
        var duration = TimeSpan.FromSeconds(Math.Clamp(request.DurationSeconds, 0.1, 86400));
        var sessionId = string.IsNullOrWhiteSpace(request.SessionId) ? "default" : request.SessionId.Trim();
        var transport = NormalizeTransport(request.Transport);
        var pipeName = RekallAgeMultiplayerEndpoint.ResolvePipeName(
            request.ProjectRoot,
            request.SceneName,
            request.PipeName,
            sessionId);
        var endpoint = ResolveEndpoint(request, transport);
        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            request.ProjectRoot,
            request.SceneName,
            0,
            context.CancellationToken).ConfigureAwait(false);
        var session = new RekallAgeAuthoritativeMultiplayerSession(
            world,
            RekallAgeRuntimeExecutionLoop.CreateDefault(request.ProjectRoot));
        var host = new RekallAgeMultiplayerAuthorityHost(sessionId, session);
        if (transport.Equals("websocket", StringComparison.Ordinal))
        {
            await using var server = new RekallAgeMultiplayerWebSocketServer(endpoint, host.HandleAsync);
            server.Start();
            await server.WaitUntilListeningAsync(TimeSpan.FromSeconds(5), context.CancellationToken).ConfigureAwait(false);
            await Task.Delay(duration, context.CancellationToken).ConfigureAwait(false);
        }
        else
        {
            await using var server = new RekallAgeMultiplayerNamedPipeServer(pipeName, host.HandleAsync);
            server.Start();
            await server.WaitUntilListeningAsync(TimeSpan.FromSeconds(5), context.CancellationToken).ConfigureAwait(false);
            await Task.Delay(duration, context.CancellationToken).ConfigureAwait(false);
        }

        var value = new MultiplayerHostResult(
            true,
            transport,
            pipeName,
            endpoint.ToString(),
            sessionId,
            request.SceneName,
            duration.TotalSeconds,
            world.Subsystems.Multiplayer.Entities.Count,
            $"Hosted multiplayer session '{sessionId}' for scene '{request.SceneName}'.");
        return RekallAgeCommandResult<MultiplayerHostResult>.Success(
            value,
            $"Multiplayer host listened on {transport} endpoint '{(transport.Equals("websocket", StringComparison.Ordinal) ? endpoint : pipeName)}' for {duration.TotalSeconds:F1}s.");
    }

    private static Uri ResolveEndpoint(MultiplayerHostRequest request, string transport)
    {
        if (!string.IsNullOrWhiteSpace(request.Endpoint)
            && Uri.TryCreate(request.Endpoint, UriKind.Absolute, out var endpoint))
        {
            return endpoint;
        }

        return transport.Equals("websocket", StringComparison.Ordinal)
            ? RekallAgeMultiplayerEndpoint.ResolveWebSocketUri(request.Address, request.Port)
            : new Uri($"pipe://localhost/{Uri.EscapeDataString(RekallAgeMultiplayerEndpoint.ResolvePipeName(request.ProjectRoot, request.SceneName, request.PipeName, request.SessionId))}");
    }

    private static string NormalizeTransport(string? transport)
    {
        return string.Equals(transport, "websocket", StringComparison.OrdinalIgnoreCase)
            || string.Equals(transport, "ws", StringComparison.OrdinalIgnoreCase)
            ? "websocket"
            : "pipe";
    }
}

public sealed class MultiplayerStatusCommand
    : RekallAgeMultiplayerCommandBase<MultiplayerStatusRequest>
{
    public override string Name => "rekall.multiplayer.status";

    protected override string Operation => "status";

    protected override JsonObject? CreatePayload(MultiplayerStatusRequest request)
    {
        return null;
    }

    protected override RekallAgeMultiplayerConnection GetConnection(MultiplayerStatusRequest request)
    {
        return new RekallAgeMultiplayerConnection(
            request.ProjectRoot,
            request.SceneName,
            request.PipeName,
            request.SessionId,
            request.TimeoutMilliseconds,
            request.Transport,
            request.Endpoint);
    }
}

public sealed class MultiplayerConnectCommand
    : RekallAgeMultiplayerCommandBase<MultiplayerConnectRequest>
{
    public override string Name => "rekall.multiplayer.connect";

    protected override string Operation => "connect";

    protected override JsonObject? CreatePayload(MultiplayerConnectRequest request)
    {
        return new JsonObject
        {
            ["clientId"] = request.ClientId,
            ["displayName"] = request.DisplayName
        };
    }

    protected override RekallAgeMultiplayerConnection GetConnection(MultiplayerConnectRequest request)
    {
        return new RekallAgeMultiplayerConnection(
            request.ProjectRoot,
            request.SceneName,
            request.PipeName,
            request.SessionId,
            request.TimeoutMilliseconds,
            request.Transport,
            request.Endpoint);
    }
}

public sealed class MultiplayerDisconnectCommand
    : RekallAgeMultiplayerCommandBase<MultiplayerDisconnectRequest>
{
    public override string Name => "rekall.multiplayer.disconnect";

    protected override string Operation => "disconnect";

    protected override JsonObject? CreatePayload(MultiplayerDisconnectRequest request)
    {
        return new JsonObject
        {
            ["clientId"] = request.ClientId
        };
    }

    protected override RekallAgeMultiplayerConnection GetConnection(MultiplayerDisconnectRequest request)
    {
        return new RekallAgeMultiplayerConnection(
            request.ProjectRoot,
            request.SceneName,
            request.PipeName,
            request.SessionId,
            request.TimeoutMilliseconds,
            request.Transport,
            request.Endpoint);
    }
}

public sealed class MultiplayerSubmitInputCommand
    : RekallAgeMultiplayerCommandBase<MultiplayerSubmitInputRequest>
{
    public override string Name => "rekall.multiplayer.submit_input";

    protected override string Operation => "submit_input";

    protected override JsonObject? CreatePayload(MultiplayerSubmitInputRequest request)
    {
        return JsonSerializer.SerializeToNode(
            new
            {
                request.ClientId,
                request.Sequence,
                request.NetworkId,
                request.Input,
                request.ClientTimeSeconds
            },
            JsonOptions)!.AsObject();
    }

    protected override RekallAgeMultiplayerConnection GetConnection(MultiplayerSubmitInputRequest request)
    {
        return new RekallAgeMultiplayerConnection(
            request.ProjectRoot,
            request.SceneName,
            request.PipeName,
            request.SessionId,
            request.TimeoutMilliseconds,
            request.Transport,
            request.Endpoint);
    }
}

public sealed class MultiplayerTickCommand
    : RekallAgeMultiplayerCommandBase<MultiplayerTickRequest>
{
    public override string Name => "rekall.multiplayer.tick";

    protected override string Operation => "tick";

    protected override JsonObject? CreatePayload(MultiplayerTickRequest request)
    {
        return new JsonObject
        {
            ["ticks"] = request.Ticks
        };
    }

    protected override RekallAgeMultiplayerConnection GetConnection(MultiplayerTickRequest request)
    {
        return new RekallAgeMultiplayerConnection(
            request.ProjectRoot,
            request.SceneName,
            request.PipeName,
            request.SessionId,
            request.TimeoutMilliseconds,
            request.Transport,
            request.Endpoint);
    }
}

public sealed class MultiplayerSnapshotCommand
    : RekallAgeMultiplayerCommandBase<MultiplayerSnapshotRequest>
{
    public override string Name => "rekall.multiplayer.snapshot";

    protected override string Operation => "snapshot";

    protected override JsonObject? CreatePayload(MultiplayerSnapshotRequest request)
    {
        return null;
    }

    protected override RekallAgeMultiplayerConnection GetConnection(MultiplayerSnapshotRequest request)
    {
        return new RekallAgeMultiplayerConnection(
            request.ProjectRoot,
            request.SceneName,
            request.PipeName,
            request.SessionId,
            request.TimeoutMilliseconds,
            request.Transport,
            request.Endpoint);
    }
}

public sealed class MultiplayerDeltaCommand
    : RekallAgeMultiplayerCommandBase<MultiplayerDeltaRequest>
{
    public override string Name => "rekall.multiplayer.delta";

    protected override string Operation => "delta";

    protected override JsonObject? CreatePayload(MultiplayerDeltaRequest request)
    {
        return new JsonObject
        {
            ["fromServerTick"] = request.FromServerTick
        };
    }

    protected override RekallAgeMultiplayerConnection GetConnection(MultiplayerDeltaRequest request)
    {
        return new RekallAgeMultiplayerConnection(
            request.ProjectRoot,
            request.SceneName,
            request.PipeName,
            request.SessionId,
            request.TimeoutMilliseconds,
            request.Transport,
            request.Endpoint);
    }
}

public abstract class RekallAgeMultiplayerCommandBase<TRequest>
    : IRekallAgeCommand<TRequest, MultiplayerCommandResult>
{
    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly MultiplayerPipeClient _client;
    private readonly RekallAgeMultiplayerWebSocketClient _webSocketClient;

    protected RekallAgeMultiplayerCommandBase()
        : this(new MultiplayerPipeClient(), new RekallAgeMultiplayerWebSocketClient())
    {
    }

    protected RekallAgeMultiplayerCommandBase(
        MultiplayerPipeClient client,
        RekallAgeMultiplayerWebSocketClient webSocketClient)
    {
        _client = client;
        _webSocketClient = webSocketClient;
    }

    public abstract string Name { get; }

    protected abstract string Operation { get; }

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Targets a running server-authoritative Rekall AGE multiplayer session over local IPC.",
        typeof(TRequest).FullName!,
        typeof(MultiplayerCommandResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<MultiplayerCommandResult>> ExecuteAsync(
        TRequest request,
        RekallAgeCommandContext context)
    {
        var connection = GetConnection(request);
        var pipeName = RekallAgeMultiplayerEndpoint.ResolvePipeName(
            connection.ProjectRoot,
            connection.SceneName,
            connection.PipeName,
            connection.SessionId);
        var transport = NormalizeTransport(connection.Transport);
        var endpoint = ResolveEndpoint(connection, pipeName, transport);
        var envelope = new RekallAgeMultiplayerRequestEnvelope(
            Operation,
            context.Transaction.Id,
            connection.ProjectRoot,
            connection.SceneName,
            CreatePayload(request));
        var timeout = TimeSpan.FromMilliseconds(Math.Max(1, connection.TimeoutMilliseconds));
        var response = transport.Equals("websocket", StringComparison.Ordinal)
            ? await _webSocketClient.SendAsync(endpoint, envelope, timeout, context.CancellationToken).ConfigureAwait(false)
            : await _client.SendAsync(pipeName, envelope, timeout, context.CancellationToken).ConfigureAwait(false);

        if (!response.Ok)
        {
            var value = Empty(transport, pipeName, endpoint.ToString(), Operation, response.ErrorMessage ?? "Multiplayer request failed.");
            var error = new RekallAgeCommandError(
                response.ErrorCode ?? "REKALL_MULTIPLAYER_UNAVAILABLE",
                response.ErrorMessage ?? "Multiplayer request failed.",
                transport.Equals("websocket", StringComparison.Ordinal) ? endpoint.ToString() : pipeName);
            return RekallAgeCommandResult<MultiplayerCommandResult>.Failure(value, error.Message, [error]);
        }

        var result = ToResult(transport, pipeName, endpoint.ToString(), Operation, response.Payload);
        return RekallAgeCommandResult<MultiplayerCommandResult>.Success(
            result,
            $"Multiplayer '{Operation}' completed through {transport} endpoint '{(transport.Equals("websocket", StringComparison.Ordinal) ? endpoint : pipeName)}'.");
    }

    protected abstract JsonObject? CreatePayload(TRequest request);

    protected abstract RekallAgeMultiplayerConnection GetConnection(TRequest request);

    private static MultiplayerCommandResult Empty(
        string transport,
        string pipeName,
        string endpoint,
        string operation,
        string message)
    {
        return new MultiplayerCommandResult(
            false,
            transport,
            pipeName,
            endpoint,
            null,
            operation,
            false,
            false,
            null,
            0,
            0,
            0,
            0,
            null,
            null,
            message);
    }

    private static MultiplayerCommandResult ToResult(
        string transport,
        string pipeName,
        string endpoint,
        string operation,
        JsonObject? payload)
    {
        return new MultiplayerCommandResult(
            true,
            transport,
            pipeName,
            endpoint,
            ReadString(payload, "sessionId"),
            operation,
            ReadBoolean(payload, "applied", true),
            ReadBoolean(payload, "accepted", false),
            ReadString(payload, "reason"),
            ReadInt32(payload, "serverTick"),
            ReadDouble(payload, "serverTimeSeconds"),
            ReadInt32(payload, "clientCount"),
            ReadInt32(payload, "entityCount"),
            ReadSnapshot(payload),
            ReadDelta(payload),
            ReadString(payload, "message") ?? "OK");
    }

    private static Uri ResolveEndpoint(
        RekallAgeMultiplayerConnection connection,
        string pipeName,
        string transport)
    {
        if (!string.IsNullOrWhiteSpace(connection.Endpoint)
            && Uri.TryCreate(connection.Endpoint, UriKind.Absolute, out var endpoint))
        {
            return endpoint;
        }

        return transport.Equals("websocket", StringComparison.Ordinal)
            ? RekallAgeMultiplayerEndpoint.ResolveWebSocketUri("127.0.0.1", 7777)
            : new Uri($"pipe://localhost/{Uri.EscapeDataString(pipeName)}");
    }

    private static string NormalizeTransport(string? transport)
    {
        return string.Equals(transport, "websocket", StringComparison.OrdinalIgnoreCase)
            || string.Equals(transport, "ws", StringComparison.OrdinalIgnoreCase)
            ? "websocket"
            : "pipe";
    }

    private static RekallAgeMultiplayerSnapshot? ReadSnapshot(JsonObject? payload)
    {
        return payload is not null
            && payload.TryGetPropertyValue("snapshot", out var node)
            && node is not null
            ? node.Deserialize<RekallAgeMultiplayerSnapshot>(JsonOptions)
            : null;
    }

    private static RekallAgeMultiplayerSnapshotDelta? ReadDelta(JsonObject? payload)
    {
        return payload is not null
            && payload.TryGetPropertyValue("delta", out var node)
            && node is not null
            ? node.Deserialize<RekallAgeMultiplayerSnapshotDelta>(JsonOptions)
            : null;
    }

    private static string? ReadString(JsonObject? payload, string name)
    {
        return payload is not null
            && payload.TryGetPropertyValue(name, out var node)
            && node is JsonValue value
            && value.TryGetValue<string>(out var text)
            ? text
            : null;
    }

    private static int ReadInt32(JsonObject? payload, string name)
    {
        if (payload is null
            || !payload.TryGetPropertyValue(name, out var node)
            || node is not JsonValue value)
        {
            return 0;
        }

        if (value.TryGetValue<int>(out var integer))
        {
            return integer;
        }

        return value.TryGetValue<long>(out var longValue) ? checked((int)longValue) : 0;
    }

    private static double ReadDouble(JsonObject? payload, string name)
    {
        if (payload is null
            || !payload.TryGetPropertyValue(name, out var node)
            || node is not JsonValue value)
        {
            return 0;
        }

        if (value.TryGetValue<double>(out var number))
        {
            return number;
        }

        return value.TryGetValue<int>(out var integer) ? integer : 0;
    }

    private static bool ReadBoolean(JsonObject? payload, string name, bool fallback)
    {
        return payload is not null
            && payload.TryGetPropertyValue(name, out var node)
            && node is JsonValue value
            && value.TryGetValue<bool>(out var boolean)
            ? boolean
            : fallback;
    }
}

public sealed record RekallAgeMultiplayerConnection(
    string ProjectRoot,
    string SceneName,
    string? PipeName,
    string? SessionId,
    int TimeoutMilliseconds,
    string Transport,
    string? Endpoint);
