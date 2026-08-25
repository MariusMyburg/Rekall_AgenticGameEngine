using Rekall.Age.Agent.LanguageModels;
using Rekall.Age.Core.Commands;

namespace Rekall.Age.Workflows;

public sealed class RekallAgeLanguageModelProjectAgentRunner : IRekallAgeProjectAgentRunner
{
    private readonly IRekallAgeLanguageModelClient _modelClient;
    private readonly RekallAgeProjectAgentSession _session;

    public RekallAgeLanguageModelProjectAgentRunner(
        IRekallAgeLanguageModelClient modelClient,
        RekallAgeCommandRegistry registry)
    {
        _modelClient = modelClient ?? throw new ArgumentNullException(nameof(modelClient));
        ArgumentNullException.ThrowIfNull(registry);
        _session = new RekallAgeProjectAgentSession(modelClient, registry);
    }

    public string ProviderId => _modelClient.ProviderId;

    public ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(
        CancellationToken cancellationToken) =>
        _session.ListModelsAsync(cancellationToken);

    public ValueTask<RekallAgeProjectAgentSessionResult> RunAsync(
        RekallAgeProjectAgentSessionRequest request,
        IProgress<RekallAgeLanguageModelAgentProgress>? progress,
        CancellationToken cancellationToken) =>
        _session.RunAsync(request, progress, cancellationToken);
}
