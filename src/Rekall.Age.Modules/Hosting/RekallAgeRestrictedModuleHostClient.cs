using System.Diagnostics;
using System.Runtime.Versioning;
using Rekall.Age.Modules.Hosting.Windows;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Modules.Hosting;

[SupportedOSPlatform("windows")]
public sealed class RekallAgeRestrictedModuleHostClient : IAsyncDisposable
{
    private readonly RekallAgeModuleHostStagedSession _staged;
    private readonly RekallAgeAppContainerProfile _profile;
    private readonly RekallAgeAppContainerProcess _process;
    private readonly Task<int> _exitTask;
    private readonly RekallAgeModuleHostFrameCodec _codec = new();
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private long _sequence;
    private int _unavailable;
    private int _disposed;

    private RekallAgeRestrictedModuleHostClient(
        RekallAgeModuleHostStagedSession staged,
        RekallAgeAppContainerProfile profile,
        RekallAgeAppContainerProcess process)
    {
        _staged = staged;
        _profile = profile;
        _process = process;
        _exitTask = process.WaitForExitAsync(CancellationToken.None).AsTask();
    }

    public RekallAgeModuleHostInitializeResponse Initialization { get; private set; } = null!;

    public bool IsRunning => Volatile.Read(ref _unavailable) == 0 && _process.IsRunning;

    public static async Task<RekallAgeRestrictedModuleHostClient> StartAsync(
        string projectRoot,
        string hostRoot,
        string sessionsRoot,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new RekallAgeModuleHostException(
                "REKALL_MODULE_HOST_PLATFORM_UNSUPPORTED",
                "Restricted module hosting requires Windows AppContainer support.");
        }

        RekallAgeModuleHostStagedSession? staged = null;
        RekallAgeAppContainerProfile? profile = null;
        RekallAgeAppContainerProcess? process = null;
        try
        {
            staged = await new RekallAgeModuleHostStager(sessionsRoot).StageAsync(
                projectRoot,
                hostRoot,
                cancellationToken);
            profile = RekallAgeAppContainerProfile.OpenOrCreate();
            profile.GrantReadExecute(staged.Root);
            process = RekallAgeAppContainerProcess.Start(
                staged,
                profile,
                RekallAgeModuleHostJobLimits.RestrictedDefault);
            var client = new RekallAgeRestrictedModuleHostClient(staged, profile, process);
            client.Initialization = await client.SendAsync<RekallAgeModuleHostInitializeResponse>(
                RekallAgeModuleHostOperations.Initialize,
                new RekallAgeModuleHostInitializeRequest(staged.LoadPlanPath),
                RekallAgeModuleHostProtocol.StartupTimeout,
                "REKALL_MODULE_HOST_STARTUP_TIMEOUT",
                cancellationToken);
            return client;
        }
        catch
        {
            if (process is not null)
            {
                await process.DisposeAsync();
            }

            profile?.Dispose();
            if (staged is not null)
            {
                await staged.DisposeAsync();
            }

            throw;
        }
    }

    public Task<RekallAgeModuleHostRuntimeUpdateResponse> UpdateRuntimeAsync(
        RekallAgeModuleHostRuntimeUpdateRequest request,
        CancellationToken cancellationToken) =>
        RequestAsync<RekallAgeModuleHostRuntimeUpdateResponse>(
            RekallAgeModuleHostOperations.RuntimeUpdate,
            request,
            cancellationToken);

    public Task<RekallAgeModuleHostPlayableCreateResponse> CreatePlayableAsync(
        RekallAgePlayableModuleContext context,
        CancellationToken cancellationToken) =>
        RequestAsync<RekallAgeModuleHostPlayableCreateResponse>(
            RekallAgeModuleHostOperations.PlayableCreate,
            new RekallAgeModuleHostPlayableCreateRequest(context),
            cancellationToken);

    public async Task TickPlayableAsync(RekallAgePlayableModuleInput input, CancellationToken cancellationToken) =>
        _ = await RequestAsync<object>(
            RekallAgeModuleHostOperations.PlayableTick,
            new RekallAgeModuleHostPlayableTickRequest(input),
            cancellationToken);

    public Task<RekallAgeModuleHostPlayableRenderResponse> RenderPlayableAsync(CancellationToken cancellationToken) =>
        RequestAsync<RekallAgeModuleHostPlayableRenderResponse>(
            RekallAgeModuleHostOperations.PlayableRender,
            new { },
            cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _unavailable, 1);

        try
        {
            if (_process.IsRunning)
            {
                await SendCoreAsync<object>(
                    RekallAgeModuleHostOperations.Shutdown,
                    new { },
                    RekallAgeModuleHostProtocol.RequestTimeout,
                    "REKALL_MODULE_HOST_REQUEST_TIMEOUT",
                    CancellationToken.None,
                    allowTerminal: true);
                _process.CloseInput();
                using var exitTimeout = new CancellationTokenSource(RekallAgeModuleHostProtocol.StartupTimeout);
                await _process.WaitForExitAsync(exitTimeout.Token);
            }
        }
        catch
        {
            // Disposal is fail-closed: process/job teardown below is authoritative.
        }
        finally
        {
            await _process.DisposeAsync();
            _profile.Dispose();
            await _staged.DisposeAsync();
            _requestGate.Dispose();
        }
    }

    private Task<T> RequestAsync<T>(string operation, object payload, CancellationToken cancellationToken) =>
        SendAsync<T>(
            operation,
            payload,
            RekallAgeModuleHostProtocol.RequestTimeout,
            "REKALL_MODULE_HOST_REQUEST_TIMEOUT",
            cancellationToken);

    private Task<T> SendAsync<T>(
        string operation,
        object payload,
        TimeSpan timeout,
        string timeoutCode,
        CancellationToken cancellationToken) =>
        SendCoreAsync<T>(operation, payload, timeout, timeoutCode, cancellationToken, allowTerminal: false);

    private async Task<T> SendCoreAsync<T>(
        string operation,
        object payload,
        TimeSpan timeout,
        string timeoutCode,
        CancellationToken cancellationToken,
        bool allowTerminal)
    {
        if (!allowTerminal && Volatile.Read(ref _unavailable) != 0)
        {
            throw new RekallAgeModuleHostException(
                "REKALL_MODULE_HOST_UNAVAILABLE",
                "The restricted module-host session is no longer available.");
        }

        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            var sequence = Interlocked.Increment(ref _sequence);
            var requestStartedAt = Stopwatch.GetTimestamp();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            try
            {
                await _codec.WriteAsync(
                    _process.StandardInput,
                    RekallAgeModuleHostEnvelope.Request(sequence, operation, payload),
                    deadline.Token);
                await _process.StandardInput.FlushAsync(deadline.Token);
                var responseTask = _codec.ReadAsync(_process.StandardOutput, deadline.Token).AsTask();
                var completed = await Task.WhenAny(responseTask, _exitTask).WaitAsync(deadline.Token);
                if (completed == _exitTask && !responseTask.IsCompletedSuccessfully)
                {
                    Interlocked.Exchange(ref _unavailable, 1);
                    await _process.DisposeAsync();
                    throw new RekallAgeModuleHostException(
                        "REKALL_MODULE_HOST_CRASHED",
                        $"The module host terminated while processing '{operation}'.",
                        operation);
                }

                var response = await responseTask;
                if (Stopwatch.GetElapsedTime(requestStartedAt) > timeout
                    && !cancellationToken.IsCancellationRequested)
                {
                    Interlocked.Exchange(ref _unavailable, 1);
                    await _process.DisposeAsync();
                    throw new RekallAgeModuleHostException(
                        timeoutCode,
                        $"The module-host operation '{operation}' exceeded its {timeout.TotalMilliseconds:0}-millisecond deadline.",
                        operation);
                }

                if (response.Sequence != sequence || !string.Equals(response.Operation, operation, StringComparison.Ordinal))
                {
                    Interlocked.Exchange(ref _unavailable, 1);
                    await _process.DisposeAsync();
                    throw new RekallAgeModuleHostException(
                        "REKALL_MODULE_HOST_PROTOCOL_INVALID",
                        "The module host returned a response for a different request.");
                }

                if (response.Ok is not true)
                {
                    var error = response.Error ?? new RekallAgeModuleHostError(
                        "REKALL_MODULE_HOST_PROTOCOL_INVALID",
                        nameof(RekallAgeModuleHostException),
                        "The module host returned an unsuccessful response without an error.");
                    Interlocked.Exchange(ref _unavailable, 1);
                    await _process.DisposeAsync();
                    throw new RekallAgeModuleHostException(error.Code, error.Message, error.ModuleId ?? operation);
                }

                try
                {
                    return response.DeserializePayload<T>();
                }
                catch (RekallAgeModuleHostException)
                {
                    Interlocked.Exchange(ref _unavailable, 1);
                    await _process.DisposeAsync();
                    throw;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                var exitedBeforeTermination = _exitTask.IsCompleted;
                Interlocked.Exchange(ref _unavailable, 1);
                await _process.DisposeAsync();
                if (exitedBeforeTermination)
                {
                    throw new RekallAgeModuleHostException(
                        "REKALL_MODULE_HOST_CRASHED",
                        $"The module host terminated while processing '{operation}'.",
                        operation);
                }

                throw new RekallAgeModuleHostException(
                    timeoutCode,
                    $"The module-host operation '{operation}' exceeded its {timeout.TotalMilliseconds:0}-millisecond deadline.",
                    operation);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Exchange(ref _unavailable, 1);
                await _process.DisposeAsync();
                throw;
            }
            catch (RekallAgeModuleHostException ex) when (ex.InnerException is EndOfStreamException)
            {
                Interlocked.Exchange(ref _unavailable, 1);
                await _process.DisposeAsync();
                throw new RekallAgeModuleHostException(
                    "REKALL_MODULE_HOST_CRASHED",
                    $"The module host terminated while processing '{operation}'.",
                    operation,
                    ex);
            }
            catch (IOException ex)
            {
                Interlocked.Exchange(ref _unavailable, 1);
                await _process.DisposeAsync();
                throw new RekallAgeModuleHostException(
                    "REKALL_MODULE_HOST_CRASHED",
                    $"The module host terminated while processing '{operation}'.",
                    operation,
                    ex);
            }
        }
        finally
        {
            _requestGate.Release();
        }
    }
}
