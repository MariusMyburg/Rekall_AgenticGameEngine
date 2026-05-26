using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime.Multiplayer;

public sealed record RekallAgeMultiplayerRequestEnvelope(
    string Operation,
    string RequestId,
    string ProjectRoot,
    string SceneName,
    JsonObject? Payload);

public sealed record RekallAgeMultiplayerResponseEnvelope(
    bool Ok,
    string? ErrorCode,
    string? ErrorMessage,
    JsonObject? Payload);

public static class RekallAgeMultiplayerEndpoint
{
    public static string ResolvePipeName(
        string projectRoot,
        string sceneName,
        string? explicitPipeName = null,
        string? sessionId = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPipeName))
        {
            return explicitPipeName.Trim();
        }

        var canonical = $"{Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)}|{sceneName.Trim()}|{sessionId?.Trim() ?? "default"}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..16].ToLowerInvariant();
        return $"rekall-age-multiplayer-{hash}";
    }

    public static Uri ResolveWebSocketUri(
        string address,
        int port,
        string? path = null)
    {
        var host = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();
        var normalizedPath = string.IsNullOrWhiteSpace(path)
            ? "/rekall-age/multiplayer"
            : path.Trim();
        if (!normalizedPath.StartsWith('/'))
        {
            normalizedPath = "/" + normalizedPath;
        }

        return new Uri($"ws://{host}:{Math.Clamp(port, 1, 65535)}{normalizedPath}");
    }
}

public sealed class RekallAgeMultiplayerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async ValueTask<RekallAgeMultiplayerResponseEnvelope> SendAsync(
        string pipeName,
        RekallAgeMultiplayerRequestEnvelope request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            pipe.Connect(checked((int)Math.Clamp(timeout.TotalMilliseconds, 1, int.MaxValue)));
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = true
            };
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            var requestJson = JsonSerializer.Serialize(request, JsonOptions);
            await writer.WriteLineAsync(requestJson).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            var responseJson = await reader.ReadLineAsync(cancellationToken)
                .AsTask()
                .WaitAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return new RekallAgeMultiplayerResponseEnvelope(
                    false,
                    "REKALL_MULTIPLAYER_NO_RESPONSE",
                    "The multiplayer session did not return a response.",
                    null);
            }

            return JsonSerializer.Deserialize<RekallAgeMultiplayerResponseEnvelope>(responseJson, JsonOptions)
                ?? new RekallAgeMultiplayerResponseEnvelope(
                    false,
                    "REKALL_MULTIPLAYER_RESPONSE_INVALID",
                    "The multiplayer session returned an invalid response.",
                    null);
        }
        catch (OperationCanceledException)
        {
            return new RekallAgeMultiplayerResponseEnvelope(
                false,
                "REKALL_MULTIPLAYER_TIMEOUT",
                $"Timed out connecting to multiplayer pipe '{pipeName}'.",
                null);
        }
        catch (TimeoutException)
        {
            return new RekallAgeMultiplayerResponseEnvelope(
                false,
                "REKALL_MULTIPLAYER_TIMEOUT",
                $"Timed out connecting to multiplayer pipe '{pipeName}'.",
                null);
        }
        catch (IOException ex)
        {
            return new RekallAgeMultiplayerResponseEnvelope(
                false,
                "REKALL_MULTIPLAYER_IO_ERROR",
                ex.Message,
                null);
        }
    }
}

public sealed class RekallAgeMultiplayerNamedPipeServer : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly Func<RekallAgeMultiplayerRequestEnvelope, CancellationToken, ValueTask<JsonObject>> _handler;
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<Task> _connections = [];
    private readonly object _connectionsLock = new();
    private Task? _loop;

    public RekallAgeMultiplayerNamedPipeServer(
        string pipeName,
        Func<RekallAgeMultiplayerRequestEnvelope, CancellationToken, ValueTask<JsonObject>> handler)
    {
        PipeName = pipeName;
        _handler = handler;
    }

    public string PipeName { get; }

    public void Start()
    {
        _loop ??= Task.Run(() => RunAsync(_stop.Token));
    }

    public async ValueTask WaitUntilListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        await _listening.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        await TryUnblockPendingConnectionAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            try
            {
                await _loop.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
            {
            }
        }

        Task[] connections;
        lock (_connectionsLock)
        {
            connections = _connections.ToArray();
        }

        try
        {
            await Task.WhenAll(connections).WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
        }

        _stop.Dispose();
    }

    private async ValueTask TryUnblockPendingConnectionAsync()
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException)
        {
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                16,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            _listening.TrySetResult();
            await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                break;
            }

            var connection = HandleConnectionAndDisposeAsync(pipe, cancellationToken);
            lock (_connectionsLock)
            {
                _connections.RemoveAll(task => task.IsCompleted);
                _connections.Add(connection);
            }
        }
    }

    private async Task HandleConnectionAndDisposeAsync(
        NamedPipeServerStream pipe,
        CancellationToken cancellationToken)
    {
        await using (pipe.ConfigureAwait(false))
        {
            await HandleConnectionAsync(pipe, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask HandleConnectionAsync(
        Stream pipe,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true
        };
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        RekallAgeMultiplayerResponseEnvelope response;
        try
        {
            var requestJson = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(requestJson))
            {
                throw new JsonException("Multiplayer request was empty.");
            }

            var request = JsonSerializer.Deserialize<RekallAgeMultiplayerRequestEnvelope>(requestJson, JsonOptions)
                ?? throw new JsonException("Multiplayer request was null.");
            var payload = await _handler(request, cancellationToken).ConfigureAwait(false);
            response = new RekallAgeMultiplayerResponseEnvelope(true, null, null, payload);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException or ArgumentException)
        {
            response = new RekallAgeMultiplayerResponseEnvelope(
                false,
                "REKALL_MULTIPLAYER_REQUEST_FAILED",
                ex.Message,
                null);
        }

        var responseJson = JsonSerializer.Serialize(response, JsonOptions);
        await writer.WriteLineAsync(responseJson.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class RekallAgeMultiplayerAuthorityHost
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly RekallAgeAuthoritativeMultiplayerSession _session;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RekallAgeMultiplayerAuthorityHost(
        string sessionId,
        RekallAgeAuthoritativeMultiplayerSession session)
    {
        SessionId = string.IsNullOrWhiteSpace(sessionId) ? "default" : sessionId.Trim();
        _session = session;
    }

    public string SessionId { get; }

    public async ValueTask<JsonObject> HandleAsync(
        RekallAgeMultiplayerRequestEnvelope request,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return request.Operation switch
            {
                "status" => Status("status", false, "Authoritative multiplayer session is running."),
                "connect" => Connect(request.Payload),
                "disconnect" => Disconnect(request.Payload),
                "submit_input" => SubmitInput(request.Payload),
                "tick" => await TickAsync(request.Payload, cancellationToken).ConfigureAwait(false),
                "snapshot" => Snapshot("snapshot", false, "Authoritative snapshot returned."),
                _ => throw new InvalidOperationException($"Multiplayer operation '{request.Operation}' is not supported.")
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    private JsonObject Connect(JsonObject? payload)
    {
        var clientId = ReadRequiredString(payload, "clientId");
        var displayName = ReadString(payload, "displayName") ?? clientId;
        _session.ConnectClient(clientId, displayName);
        return Snapshot("connect", true, $"Client '{clientId}' connected.");
    }

    private JsonObject Disconnect(JsonObject? payload)
    {
        var clientId = ReadRequiredString(payload, "clientId");
        _session.DisconnectClient(clientId);
        return Snapshot("disconnect", true, $"Client '{clientId}' disconnected.");
    }

    private JsonObject SubmitInput(JsonObject? payload)
    {
        var input = new RekallAgeMultiplayerInputCommand(
            ReadRequiredString(payload, "clientId"),
            ReadInt32(payload, "sequence", required: true),
            ReadRequiredString(payload, "networkId"),
            ReadInputFrame(payload),
            ReadDouble(payload, "clientTimeSeconds"));
        var result = _session.EnqueueInput(input);
        var output = Status("submit_input", result.Accepted, result.Reason);
        output["accepted"] = result.Accepted;
        output["reason"] = result.Reason;
        return output;
    }

    private async ValueTask<JsonObject> TickAsync(
        JsonObject? payload,
        CancellationToken cancellationToken)
    {
        var ticks = Math.Clamp(ReadInt32(payload, "ticks", required: false, fallback: 1), 1, 240);
        RekallAgeMultiplayerSnapshot snapshot = _session.BuildSnapshot();
        for (var i = 0; i < ticks; i++)
        {
            snapshot = await _session.TickAsync(cancellationToken).ConfigureAwait(false);
        }

        return Snapshot("tick", true, $"Advanced authoritative simulation by {ticks} tick(s).", snapshot);
    }

    private JsonObject Snapshot(
        string operation,
        bool applied,
        string message,
        RekallAgeMultiplayerSnapshot? snapshot = null)
    {
        snapshot ??= _session.BuildSnapshot();
        var output = Status(operation, applied, message);
        output["snapshot"] = JsonSerializer.SerializeToNode(snapshot, JsonOptions);
        return output;
    }

    private JsonObject Status(string operation, bool applied, string message)
    {
        return new JsonObject
        {
            ["sessionId"] = SessionId,
            ["operation"] = operation,
            ["applied"] = applied,
            ["serverTick"] = _session.ServerTick,
            ["serverTimeSeconds"] = _session.World.ElapsedTime.TotalSeconds,
            ["clientCount"] = _session.Clients.Count,
            ["entityCount"] = _session.World.Subsystems.Multiplayer.Entities.Count,
            ["message"] = message
        };
    }

    private static RekallAgeRuntimeInputFrame ReadInputFrame(JsonObject? payload)
    {
        if (payload is null
            || !payload.TryGetPropertyValue("input", out var node)
            || node is null)
        {
            return new RekallAgeRuntimeInputFrame();
        }

        return node.Deserialize<RekallAgeRuntimeInputFrame>(JsonOptions) ?? new RekallAgeRuntimeInputFrame();
    }

    private static string ReadRequiredString(JsonObject? payload, string name)
    {
        return ReadString(payload, name)
            ?? throw new InvalidOperationException($"Multiplayer payload must include '{name}'.");
    }

    private static string? ReadString(JsonObject? payload, string name)
    {
        return payload is not null
            && payload.TryGetPropertyValue(name, out var node)
            && node is JsonValue value
            && value.TryGetValue<string>(out var text)
            && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;
    }

    private static int ReadInt32(
        JsonObject? payload,
        string name,
        bool required,
        int fallback = 0)
    {
        if (payload is null
            || !payload.TryGetPropertyValue(name, out var node)
            || node is not JsonValue value)
        {
            if (required)
            {
                throw new InvalidOperationException($"Multiplayer payload must include '{name}'.");
            }

            return fallback;
        }

        if (value.TryGetValue<int>(out var integer))
        {
            return integer;
        }

        if (value.TryGetValue<long>(out var longValue))
        {
            return checked((int)longValue);
        }

        return required
            ? throw new InvalidOperationException($"Multiplayer payload '{name}' must be an integer.")
            : fallback;
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
}
