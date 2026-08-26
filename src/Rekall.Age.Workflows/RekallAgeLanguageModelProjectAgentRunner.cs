using Rekall.Age.Agent.LanguageModels;
using Rekall.Age.Core.Commands;

namespace Rekall.Age.Workflows;

public sealed class RekallAgeLanguageModelProjectAgentRunner : IRekallAgeProjectAgentRunner, IDisposable
{
    private readonly IRekallAgeLanguageModelClient _modelClient;
    private readonly RekallAgeProjectAgentSession _session;
    private bool _disposed;

    public RekallAgeLanguageModelProjectAgentRunner(
        IRekallAgeLanguageModelClient modelClient,
        RekallAgeCommandRegistry registry)
    {
        _modelClient = modelClient ?? throw new ArgumentNullException(nameof(modelClient));
        ArgumentNullException.ThrowIfNull(registry);
        _session = new RekallAgeProjectAgentSession(
            modelClient,
            registry,
            $"rekall-{modelClient.ProviderId}-agent");
    }

    public string ProviderId
    {
        get
        {
            ThrowIfDisposed();
            return _modelClient.ProviderId;
        }
    }

    public ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _session.ListModelsAsync(cancellationToken);
    }

    public ValueTask<RekallAgeProjectAgentSessionResult> RunAsync(
        RekallAgeProjectAgentSessionRequest request,
        IProgress<RekallAgeLanguageModelAgentProgress>? progress,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _session.RunAsync(request, progress, cancellationToken);
    }

    public void Dispose() => _disposed = true;

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, nameof(RekallAgeLanguageModelProjectAgentRunner));
}
