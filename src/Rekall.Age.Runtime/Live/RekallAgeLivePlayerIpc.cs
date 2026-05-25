using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rekall.Age.Runtime.Live;

public sealed record RekallAgeLivePlayerRequestEnvelope(
    string Operation,
    string RequestId,
    string ProjectRoot,
    string SceneName,
    JsonObject? Payload);

public sealed record RekallAgeLivePlayerResponseEnvelope(
    bool Ok,
    string? ErrorCode,
    string? ErrorMessage,
    JsonObject? Payload);

public static class RekallAgeLivePlayerEndpoint
{
    public static string ResolvePipeName(string projectRoot, string sceneName, string? explicitPipeName = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPipeName))
        {
            return explicitPipeName.Trim();
        }

        var canonical = $"{Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)}|{sceneName.Trim()}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..16].ToLowerInvariant();
        return $"rekall-age-live-{hash}";
    }
}

public sealed class RekallAgeLivePlayerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async ValueTask<RekallAgeLivePlayerResponseEnvelope> SendAsync(
        string pipeName,
        RekallAgeLivePlayerRequestEnvelope request,
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
                return new RekallAgeLivePlayerResponseEnvelope(
                    false,
                    "REKALL_LIVE_PLAYER_NO_RESPONSE",
                    "The live player did not return a response.",
                    null);
            }

            return JsonSerializer.Deserialize<RekallAgeLivePlayerResponseEnvelope>(responseJson, JsonOptions)
                ?? new RekallAgeLivePlayerResponseEnvelope(
                    false,
                    "REKALL_LIVE_PLAYER_RESPONSE_INVALID",
                    "The live player returned an invalid response.",
                    null);
        }
        catch (OperationCanceledException)
        {
            return new RekallAgeLivePlayerResponseEnvelope(
                false,
                "REKALL_LIVE_PLAYER_TIMEOUT",
                $"Timed out connecting to live player pipe '{pipeName}'.",
                null);
        }
        catch (TimeoutException)
        {
            return new RekallAgeLivePlayerResponseEnvelope(
                false,
                "REKALL_LIVE_PLAYER_TIMEOUT",
                $"Timed out connecting to live player pipe '{pipeName}'.",
                null);
        }
        catch (IOException ex)
        {
            return new RekallAgeLivePlayerResponseEnvelope(
                false,
                "REKALL_LIVE_PLAYER_IO_ERROR",
                ex.Message,
                null);
        }
    }
}

public sealed class RekallAgeLivePlayerNamedPipeServer : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly Func<RekallAgeLivePlayerRequestEnvelope, CancellationToken, ValueTask<JsonObject>> _handler;
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _loop;

    public RekallAgeLivePlayerNamedPipeServer(
        string pipeName,
        Func<RekallAgeLivePlayerRequestEnvelope, CancellationToken, ValueTask<JsonObject>> handler)
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
            await using var pipe = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            _listening.TrySetResult();
            await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            if (!cancellationToken.IsCancellationRequested)
            {
                await HandleConnectionAsync(pipe, cancellationToken).ConfigureAwait(false);
            }
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
        RekallAgeLivePlayerResponseEnvelope response;
        try
        {
            var requestJson = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(requestJson))
            {
                throw new JsonException("Live player request was empty.");
            }

            var request = JsonSerializer.Deserialize<RekallAgeLivePlayerRequestEnvelope>(requestJson, JsonOptions)
                ?? throw new JsonException("Live player request was null.");
            var payload = await _handler(request, cancellationToken).ConfigureAwait(false);
            response = new RekallAgeLivePlayerResponseEnvelope(true, null, null, payload);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException or ArgumentException)
        {
            response = new RekallAgeLivePlayerResponseEnvelope(
                false,
                "REKALL_LIVE_PLAYER_REQUEST_FAILED",
                ex.Message,
                null);
        }

        var responseJson = JsonSerializer.Serialize(response, JsonOptions);
        await writer.WriteLineAsync(responseJson.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
