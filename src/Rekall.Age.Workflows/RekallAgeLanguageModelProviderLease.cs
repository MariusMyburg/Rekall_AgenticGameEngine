using Rekall.Age.Agent.LanguageModels;

namespace Rekall.Age.Workflows;

public sealed class RekallAgeLanguageModelProviderLease : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IDisposable? _disposableRunner;
    private bool _disposed;

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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _disposableRunner?.Dispose();
        }
        finally
        {
            _httpClient.Dispose();
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, nameof(RekallAgeLanguageModelProviderLease));
}
