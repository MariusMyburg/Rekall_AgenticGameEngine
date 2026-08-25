using System.Net;
using System.Text.Json;
using Rekall.Age.Agent.LanguageModels;
using Rekall.Age.Core.Commands;
using Rekall.Age.Workflows;

namespace Rekall.Age.Tests.Workflows;

public sealed class LanguageModelProviderCatalogTests
{
    [Fact]
    public void DefaultCatalogPublishesInspectableProviderDescriptorsWithoutCredentials()
    {
        var catalog = new RekallAgeLanguageModelProviderCatalog();

        Assert.Equal(
        [
            new RekallAgeLanguageModelProviderDescriptor("ollama", "Ollama", "qwen3.5:35b", "none"),
            new RekallAgeLanguageModelProviderDescriptor("openai", "OpenAI", "gpt-5.6-sol", "api-key")
        ],
        catalog.Providers);
        Assert.DoesNotContain("OPENAI_API_KEY", string.Join('\n', catalog.Providers));
    }

    [Fact]
    public void OpenAiWithoutASessionKeyFailsBeforeCreatingAHttpClient()
    {
        var factoryCalls = 0;
        var catalog = new RekallAgeLanguageModelProviderCatalog(
            new RekallAgeLanguageModelProviderSettings { OpenAiApiKey = " " },
            () =>
            {
                factoryCalls++;
                return new HttpClient();
            });

        var error = Assert.Throws<RekallAgeLanguageModelProviderException>(() =>
            catalog.Acquire("openai", new RekallAgeCommandRegistry()));

        Assert.Equal("REKALL_OPENAI_API_KEY_MISSING", error.Code);
        Assert.Equal("openai", error.ProviderId);
        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public void UnsupportedProviderReportsRequestedAndSupportedProviderValues()
    {
        var catalog = new RekallAgeLanguageModelProviderCatalog();

        var error = Assert.Throws<RekallAgeLanguageModelProviderException>(() =>
            catalog.Acquire("missing-provider", new RekallAgeCommandRegistry()));

        Assert.Equal("REKALL_LANGUAGE_MODEL_PROVIDER_UNSUPPORTED", error.Code);
        Assert.Equal("missing-provider", error.RequestedValue);
        Assert.Equal("ollama,openai", error.ResolvedValue);
    }

    [Fact]
    public void SessionSettingsSerializationRedactsTheOpenAiKey()
    {
        var settings = new RekallAgeLanguageModelProviderSettings
        {
            OpenAiApiKey = "session-key-must-not-serialize",
            OpenAiUrl = "https://gateway.example.test/v1/"
        };

        var serialized = JsonSerializer.Serialize(settings);

        Assert.DoesNotContain("session-key-must-not-serialize", serialized, StringComparison.Ordinal);
        Assert.Contains("gateway.example.test", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderLeasesOwnTheirExactClientAndRunnerAndNeverReuseDisposedSessions()
    {
        var firstHandler = new DisposalTrackingHandler();
        var secondHandler = new DisposalTrackingHandler();
        var handlers = new Queue<DisposalTrackingHandler>([firstHandler, secondHandler]);
        var catalog = new RekallAgeLanguageModelProviderCatalog(
            new RekallAgeLanguageModelProviderSettings { OllamaUrl = "http://127.0.0.1:11434" },
            () => new HttpClient(handlers.Dequeue(), disposeHandler: true));

        var first = catalog.Acquire("ollama", new RekallAgeCommandRegistry());
        var firstRunner = first.Runner;
        first.Dispose();
        first.Dispose();

        Assert.True(firstHandler.Disposed);
        Assert.Throws<ObjectDisposedException>(() => _ = first.Runner);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => firstRunner.ListModelsAsync(CancellationToken.None).AsTask());

        using var second = catalog.Acquire("openai", new RekallAgeCommandRegistry(),
            new RekallAgeLanguageModelProviderSettings { OpenAiApiKey = "session-test-key" });

        Assert.NotSame(firstRunner, second.Runner);
        Assert.Equal("ollama", first.ProviderId);
        Assert.Equal("openai", second.ProviderId);
        Assert.False(secondHandler.Disposed);
    }

    [Fact]
    public async Task ConcurrentLeaseDisposalDisposesEachOwnedResourceExactlyOnce()
    {
        var handler = new DisposalTrackingHandler();
        var runner = new CountingRunner();
        var lease = new RekallAgeLanguageModelProviderLease(
            "test",
            new HttpClient(handler, disposeHandler: true),
            new TestModelClient(),
            runner);
        using var start = new Barrier(33);
        var disposals = Enumerable.Range(0, 32)
            .Select(_ => Task.Factory.StartNew(() =>
            {
                start.SignalAndWait();
                lease.Dispose();
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default))
            .ToArray();

        start.SignalAndWait();
        await Task.WhenAll(disposals);

        Assert.Equal(1, runner.DisposeCount);
        Assert.Equal(1, handler.DisposeCount);
    }

    private sealed class DisposalTrackingHandler : HttpMessageHandler
    {
        public int DisposeCount => _disposeCount;
        public bool Disposed => DisposeCount > 0;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

        protected override void Dispose(bool disposing)
        {
            Interlocked.Increment(ref _disposeCount);
            base.Dispose(disposing);
        }

        private int _disposeCount;
    }

    private sealed class CountingRunner : IRekallAgeProjectAgentRunner, IDisposable
    {
        private int _disposeCount;
        public string ProviderId => "test";
        public int DisposeCount => _disposeCount;
        public ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<RekallAgeLanguageModelInfo>>([]);
        public ValueTask<RekallAgeProjectAgentSessionResult> RunAsync(
            RekallAgeProjectAgentSessionRequest request,
            IProgress<RekallAgeLanguageModelAgentProgress>? progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public void Dispose() => Interlocked.Increment(ref _disposeCount);
    }

    private sealed class TestModelClient : IRekallAgeLanguageModelClient
    {
        public string ProviderId => "test";
        public ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<RekallAgeLanguageModelInfo>>([]);
        public ValueTask<RekallAgeLanguageModelResponse> ChatAsync(
            RekallAgeLanguageModelRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
