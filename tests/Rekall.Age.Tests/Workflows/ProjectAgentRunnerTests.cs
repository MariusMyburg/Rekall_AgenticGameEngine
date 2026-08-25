using Rekall.Age.Agent.LanguageModels;
using Rekall.Age.Core.Commands;
using Rekall.Age.Workflows;

namespace Rekall.Age.Tests.Workflows;

public sealed class ProjectAgentRunnerTests
{
    [Fact]
    public async Task LanguageModelRunnerPreservesProviderDiscoverySessionResultAndProgress()
    {
        var projectRoot = TestPaths.CreateTempDirectory();
        var request = new RekallAgeProjectAgentSessionRequest(
            projectRoot,
            "Main",
            "model",
            "Describe the completed project.")
        {
            MaxTurns = 1,
            TreatGauntletAsTerminalSuccess = true
        };
        RekallAgeLanguageModelInfo[] models = [new("model", 17)];
        var expectedProgress = new RecordingProgress<RekallAgeLanguageModelAgentProgress>();
        var expected = await new RekallAgeProjectAgentSession(
                new ScriptedModelClient("provider-test", models),
                new RekallAgeCommandRegistry())
            .RunAsync(request, expectedProgress, CancellationToken.None);
        var actualProgress = new RecordingProgress<RekallAgeLanguageModelAgentProgress>();
        IRekallAgeProjectAgentRunner runner = new RekallAgeLanguageModelProjectAgentRunner(
            new ScriptedModelClient("provider-test", models),
            new RekallAgeCommandRegistry());

        var discovered = await runner.ListModelsAsync(CancellationToken.None);
        var actual = await runner.RunAsync(request, actualProgress, CancellationToken.None);

        Assert.Equal("provider-test", runner.ProviderId);
        Assert.Same(models, discovered);
        Assert.Equivalent(expected, actual, strict: true);
        Assert.Equal(expectedProgress.Values, actualProgress.Values);
    }

    private sealed class ScriptedModelClient(
        string providerId,
        IReadOnlyList<RekallAgeLanguageModelInfo> models) : IRekallAgeLanguageModelClient
    {
        public string ProviderId => providerId;

        public ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(models);

        public ValueTask<RekallAgeLanguageModelResponse> ChatAsync(
            RekallAgeLanguageModelRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new RekallAgeLanguageModelResponse(
                providerId,
                request.Model,
                "Project complete.",
                string.Empty,
                [],
                "stop",
                new RekallAgeLanguageModelUsage(3, 2, 1)));
    }

    private sealed class RecordingProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];

        public void Report(T value) => Values.Add(value);
    }
}
