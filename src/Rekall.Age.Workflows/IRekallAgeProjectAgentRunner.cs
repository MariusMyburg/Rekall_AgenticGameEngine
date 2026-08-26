using Rekall.Age.Agent.LanguageModels;

namespace Rekall.Age.Workflows;

public interface IRekallAgeProjectAgentRunner
{
    string ProviderId { get; }

    ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(
        CancellationToken cancellationToken);

    ValueTask<RekallAgeProjectAgentSessionResult> RunAsync(
        RekallAgeProjectAgentSessionRequest request,
        IProgress<RekallAgeLanguageModelAgentProgress>? progress,
        CancellationToken cancellationToken);
}
