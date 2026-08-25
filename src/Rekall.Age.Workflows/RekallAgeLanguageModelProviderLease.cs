using Rekall.Age.Agent.LanguageModels;

namespace Rekall.Age.Workflows;

public sealed class RekallAgeLanguageModelProviderLease : IDisposable, IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IAsyncDisposable? _asyncDisposableRunner;
    private readonly IDisposable? _disposableRunner;
    private readonly object _disposeSync = new();
    private Task? _disposeTask;

    internal RekallAgeLanguageModelProviderLease(
        string providerId,
        HttpClient httpClient,
        IRekallAgeLanguageModelClient modelClient,
        IRekallAgeProjectAgentRunner runner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ProviderId = providerId;
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _modelClient = modelClient ?? throw new ArgumentNullException(nameof(modelClient));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _asyncDisposableRunner = runner as IAsyncDisposable;
        _disposableRunner = runner as IDisposable;
    }

    public string ProviderId { get; }

    public IRekallAgeProjectAgentRunner Runner
    {
        get
        {
            ThrowIfDisposed();
            return _runner;
        }
    }

    public IRekallAgeLanguageModelClient ModelClient
    {
        get
        {
            ThrowIfDisposed();
            return _modelClient;
        }
    }

    private readonly IRekallAgeLanguageModelClient _modelClient;
    private readonly IRekallAgeProjectAgentRunner _runner;

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            if (_disposeTask is null)
            {
                var completion = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _disposeTask = completion.Task;
                _ = CompleteDisposeAsync(completion);
            }

            return new ValueTask(_disposeTask);
        }
    }

    private async Task CompleteDisposeAsync(TaskCompletionSource completion)
    {
        Exception? failure = null;
        try
        {
            if (_asyncDisposableRunner is not null)
            {
                await _asyncDisposableRunner.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                _disposableRunner?.Dispose();
            }
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try
        {
            _httpClient.Dispose();
        }
        catch (Exception exception)
        {
            failure = failure is null
                ? exception
                : new AggregateException(failure, exception);
        }

        if (failure is null)
        {
            completion.TrySetResult();
        }
        else
        {
            completion.TrySetException(failure);
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposeTask) is not null,
            nameof(RekallAgeLanguageModelProviderLease));
}
