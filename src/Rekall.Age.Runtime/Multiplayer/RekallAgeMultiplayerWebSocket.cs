using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rekall.Age.Runtime.Multiplayer;

public sealed class RekallAgeMultiplayerWebSocketClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async ValueTask<RekallAgeMultiplayerResponseEnvelope> SendAsync(
        Uri endpoint,
        RekallAgeMultiplayerRequestEnvelope request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            using var socket = await ConnectWithRetryAsync(endpoint, timeout, timeoutSource.Token).ConfigureAwait(false);
            var requestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, JsonOptions));
            await socket.SendAsync(
                requestBytes,
                WebSocketMessageType.Text,
                WebSocketMessageFlags.EndOfMessage,
                timeoutSource.Token).ConfigureAwait(false);

            var responseJson = await ReceiveTextAsync(socket, timeoutSource.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return new RekallAgeMultiplayerResponseEnvelope(
                    false,
                    "REKALL_MULTIPLAYER_NO_RESPONSE",
                    "The multiplayer WebSocket session did not return a response.",
                    null);
            }

            return JsonSerializer.Deserialize<RekallAgeMultiplayerResponseEnvelope>(responseJson, JsonOptions)
                ?? new RekallAgeMultiplayerResponseEnvelope(
                    false,
                    "REKALL_MULTIPLAYER_RESPONSE_INVALID",
                    "The multiplayer WebSocket session returned an invalid response.",
                    null);
        }
        catch (OperationCanceledException)
        {
            return new RekallAgeMultiplayerResponseEnvelope(
                false,
                "REKALL_MULTIPLAYER_TIMEOUT",
                $"Timed out connecting to multiplayer WebSocket '{endpoint}'.",
                null);
        }
        catch (WebSocketException ex)
        {
            return new RekallAgeMultiplayerResponseEnvelope(
                false,
                "REKALL_MULTIPLAYER_WEBSOCKET_ERROR",
                ex.Message,
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

    private static async ValueTask<ClientWebSocket> ConnectWithRetryAsync(
        Uri endpoint,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        Exception? lastError = null;
        while (true)
        {
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                throw new OperationCanceledException("Timed out connecting to multiplayer WebSocket.", lastError, cancellationToken);
            }

            var socket = new ClientWebSocket();
            try
            {
                await ConnectAttemptAsync(
                    socket,
                    endpoint,
                    ClampConnectAttemptTimeout(remaining),
                    cancellationToken).ConfigureAwait(false);
                return socket;
            }
            catch (Exception ex) when (IsRetryableConnectFailure(ex) && !cancellationToken.IsCancellationRequested)
            {
                lastError = ex;
                socket.Dispose();
                var delay = remaining > TimeSpan.FromMilliseconds(75)
                    ? TimeSpan.FromMilliseconds(50)
                    : TimeSpan.FromMilliseconds(Math.Max(1, remaining.TotalMilliseconds * 0.5));
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    }

    private static TimeSpan ClampConnectAttemptTimeout(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.FromMilliseconds(250))
        {
            return remaining;
        }

        return TimeSpan.FromMilliseconds(Math.Min(500, remaining.TotalMilliseconds));
    }

    private static async ValueTask ConnectAttemptAsync(
        ClientWebSocket socket,
        Uri endpoint,
        TimeSpan attemptTimeout,
        CancellationToken cancellationToken)
    {
        using var attemptSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var connectTask = socket.ConnectAsync(endpoint, attemptSource.Token);
        var timeoutTask = Task.Delay(attemptTimeout, cancellationToken);
        var completed = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);
        if (completed != connectTask)
        {
            await attemptSource.CancelAsync().ConfigureAwait(false);
            socket.Dispose();
            _ = connectTask.ContinueWith(
                task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw new OperationCanceledException("Timed out during one WebSocket connection attempt.", cancellationToken);
        }

        await connectTask.ConfigureAwait(false);
    }

    private static bool IsRetryableConnectFailure(Exception ex)
    {
        return ex is WebSocketException or IOException or OperationCanceledException;
    }

    private static async ValueTask<string> ReceiveTextAsync(
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        await using var output = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return string.Empty;
            }

            output.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }
    }
}

public sealed class RekallAgeMultiplayerWebSocketServer : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly Func<RekallAgeMultiplayerRequestEnvelope, CancellationToken, ValueTask<JsonObject>> _handler;
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<Task> _connections = [];
    private readonly object _connectionsLock = new();
    private Task? _loop;

    public RekallAgeMultiplayerWebSocketServer(
        Uri endpoint,
        Func<RekallAgeMultiplayerRequestEnvelope, CancellationToken, ValueTask<JsonObject>> handler)
    {
        if (endpoint.Scheme is not "ws" and not "http")
        {
            throw new ArgumentException("Multiplayer WebSocket endpoint must use ws:// or http://.", nameof(endpoint));
        }

        Endpoint = endpoint;
        _handler = handler;
        _listener.Prefixes.Add(ToHttpPrefix(endpoint));
    }

    public Uri Endpoint { get; }

    public void Start()
    {
        _loop ??= Task.Factory.StartNew(
            () => RunAsync(_stop.Token),
            _stop.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
    }

    public async ValueTask WaitUntilListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        await _listening.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_listener.IsListening)
        {
            _listener.Stop();
        }

        if (_loop is not null)
        {
            try
            {
                await _loop.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or HttpListenerException)
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

        _listener.Close();
        _stop.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            _listener.Start();
            _listening.TrySetResult();
            while (!cancellationToken.IsCancellationRequested && _listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or HttpListenerException)
                {
                    break;
                }

                var connection = HandleContextAsync(context, cancellationToken);
                lock (_connectionsLock)
                {
                    _connections.RemoveAll(task => task.IsCompleted);
                    _connections.Add(connection);
                }
            }
        }
        catch (Exception ex) when (ex is HttpListenerException or InvalidOperationException)
        {
            _listening.TrySetException(ex);
            throw;
        }
    }

    private async Task HandleContextAsync(
        HttpListenerContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Request.IsWebSocketRequest)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await WriteHttpErrorAsync(context, "Expected a WebSocket upgrade request.", cancellationToken).ConfigureAwait(false);
            return;
        }

        WebSocketContext webSocketContext;
        try
        {
            webSocketContext = await context.AcceptWebSocketAsync(subProtocol: null).ConfigureAwait(false);
        }
        catch (HttpListenerException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.Close();
            return;
        }

        using var socket = webSocketContext.WebSocket;
        RekallAgeMultiplayerResponseEnvelope response;
        try
        {
            var requestJson = await ReceiveTextAsync(socket, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(requestJson))
            {
                throw new JsonException("Multiplayer WebSocket request was empty.");
            }

            var request = JsonSerializer.Deserialize<RekallAgeMultiplayerRequestEnvelope>(requestJson, JsonOptions)
                ?? throw new JsonException("Multiplayer WebSocket request was null.");
            var payload = await _handler(request, cancellationToken).ConfigureAwait(false);
            response = new RekallAgeMultiplayerResponseEnvelope(true, null, null, payload);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException or ArgumentException or WebSocketException)
        {
            response = new RekallAgeMultiplayerResponseEnvelope(
                false,
                "REKALL_MULTIPLAYER_REQUEST_FAILED",
                ex.Message,
                null);
        }

        var responseBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response, JsonOptions));
        await socket.SendAsync(
            responseBytes,
            WebSocketMessageType.Text,
            WebSocketMessageFlags.EndOfMessage,
            cancellationToken).ConfigureAwait(false);
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "response-sent",
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
            }
            catch (HttpListenerException)
            {
            }
        }
    }

    private static async ValueTask WriteHttpErrorAsync(
        HttpListenerContext context,
        string message,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        context.Response.ContentType = "text/plain; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        context.Response.Close();
    }

    private static async ValueTask<string> ReceiveTextAsync(
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        await using var output = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return string.Empty;
            }

            output.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }
    }

    private static string ToHttpPrefix(Uri endpoint)
    {
        var builder = new UriBuilder(endpoint)
        {
            Scheme = "http"
        };
        var prefix = builder.Uri.ToString();
        return prefix.EndsWith('/') ? prefix : prefix + "/";
    }
}
