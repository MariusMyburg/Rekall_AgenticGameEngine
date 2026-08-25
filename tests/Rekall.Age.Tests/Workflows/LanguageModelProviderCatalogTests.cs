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
        var catalog = new RekallAgeLanguageModelProviderCatalog(
            new RekallAgeLanguageModelProviderSettings { OpenAiApiKey = " " });

        var ollama = Assert.Single(catalog.Providers, provider => provider.Id == "ollama");
        Assert.Equal("Local Ollama", ollama.DisplayName);
        Assert.Equal("qwen3.5:35b", ollama.DefaultModel);
        Assert.Equal("none", ollama.AuthenticationKind);
        Assert.Equal("not-required", ollama.AuthenticationState);
        Assert.True(ollama.IsAvailable);
        Assert.Equal("available", ollama.Availability);
        Assert.Empty(ollama.Diagnostics);

        var openAi = Assert.Single(catalog.Providers, provider => provider.Id == "openai");
        Assert.Equal("OpenAI API", openAi.DisplayName);
        Assert.Equal("gpt-5.6-sol", openAi.DefaultModel);
        Assert.Equal("api-key", openAi.AuthenticationKind);
        Assert.Equal("required", openAi.AuthenticationState);
        Assert.False(openAi.IsAvailable);
        Assert.Equal("unavailable", openAi.Availability);
        var diagnostic = Assert.Single(openAi.Diagnostics);
        Assert.Equal("REKALL_OPENAI_API_KEY_MISSING", diagnostic.Code);
        Assert.Equal("OpenAI requires OPENAI_API_KEY or a session-only API key.", diagnostic.Message);
        Assert.DoesNotContain("Authorization", string.Join('\n', catalog.Providers), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConfiguredOpenAiDescriptorIsAvailableWithoutExposingAuthenticationMaterial()
    {
        var sessionCredential = "credential-" + Guid.NewGuid().ToString("N");
        var catalog = new RekallAgeLanguageModelProviderCatalog(
            new RekallAgeLanguageModelProviderSettings { OpenAiApiKey = sessionCredential });

        var openAi = Assert.Single(catalog.Providers, provider => provider.Id == "openai");

        Assert.Equal("configured", openAi.AuthenticationState);
        Assert.True(openAi.IsAvailable);
        Assert.Equal("available", openAi.Availability);
        Assert.Empty(openAi.Diagnostics);
        Assert.False(openAi.ToString().Contains(sessionCredential, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExistingPositionalDescriptorConstructionRemainsSourceCompatible()
    {
        var descriptor = new RekallAgeLanguageModelProviderDescriptor(
            "custom",
            "Custom Provider",
            "model",
            "external");

        Assert.Equal("custom", descriptor.Id);
        Assert.Equal("Custom Provider", descriptor.DisplayName);
        Assert.Equal("model", descriptor.DefaultModel);
        Assert.Equal("external", descriptor.AuthenticationKind);
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

    [Fact]
    public async Task RepeatedAsyncLeaseDisposalAwaitsOneAsyncPreferredShutdownAndDisposesHttpOnce()
    {
        var handler = new DisposalTrackingHandler();
        var runner = new AsyncPreferredRunner(blockDisposal: true);
        var lease = new RekallAgeLanguageModelProviderLease(
            "test",
            new HttpClient(handler, disposeHandler: true),
            new TestModelClient(),
            runner);

        var first = lease.DisposeAsync().AsTask();
        await runner.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = lease.DisposeAsync().AsTask();

        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        Assert.Equal(1, runner.AsyncDisposeCount);
        Assert.Equal(0, runner.SyncDisposeCount);
        Assert.Equal(0, handler.DisposeCount);

        runner.ReleaseDispose.TrySetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, runner.AsyncDisposeCount);
        Assert.Equal(0, runner.SyncDisposeCount);
        Assert.Equal(1, handler.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => _ = lease.Runner);
    }

    [Fact]
    public void SynchronousLeaseDisposalStillPrefersAsyncRunnerAndCompletesExactlyOnce()
    {
        var handler = new DisposalTrackingHandler();
        var runner = new AsyncPreferredRunner(blockDisposal: false);
        var lease = new RekallAgeLanguageModelProviderLease(
            "test",
            new HttpClient(handler, disposeHandler: true),
            new TestModelClient(),
            runner);

        lease.Dispose();
        lease.Dispose();

        Assert.Equal(1, runner.AsyncDisposeCount);
        Assert.Equal(0, runner.SyncDisposeCount);
        Assert.Equal(1, handler.DisposeCount);
    }

    [Fact]
    public async Task RepeatedAsyncLeaseDisposalCompletesWhenHttpDisposalFails()
    {
        var handler = new DisposalTrackingHandler(throwOnDispose: true);
        var runner = new CountingRunner();
        var lease = new RekallAgeLanguageModelProviderLease(
            "test",
            new HttpClient(handler, disposeHandler: true),
            new TestModelClient(),
            runner);

        var first = lease.DisposeAsync().AsTask();
        var second = lease.DisposeAsync().AsTask();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal("Synthetic HTTP disposal failure.", exception.Message);
        Assert.Equal(1, runner.DisposeCount);
        Assert.Equal(1, handler.DisposeCount);
    }

    private sealed class DisposalTrackingHandler(bool throwOnDispose = false) : HttpMessageHandler
    {
        public int DisposeCount => _disposeCount;
        public bool Disposed => DisposeCount > 0;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

        protected override void Dispose(bool disposing)
        {
            Interlocked.Increment(ref _disposeCount);
            if (throwOnDispose)
            {
                throw new InvalidOperationException("Synthetic HTTP disposal failure.");
            }

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

    private sealed class AsyncPreferredRunner(bool blockDisposal) :
        IRekallAgeProjectAgentRunner,
        IDisposable,
        IAsyncDisposable
    {
        private int _asyncDisposeCount;
        private int _syncDisposeCount;

        public string ProviderId => "test";
        public int AsyncDisposeCount => Volatile.Read(ref _asyncDisposeCount);
        public int SyncDisposeCount => Volatile.Read(ref _syncDisposeCount);
        public TaskCompletionSource DisposeEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseDispose { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<RekallAgeLanguageModelInfo>>([]);

        public ValueTask<RekallAgeProjectAgentSessionResult> RunAsync(
            RekallAgeProjectAgentSessionRequest request,
            IProgress<RekallAgeLanguageModelAgentProgress>? progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public void Dispose() => Interlocked.Increment(ref _syncDisposeCount);

        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _asyncDisposeCount);
            DisposeEntered.TrySetResult();
            if (blockDisposal)
            {
                await ReleaseDispose.Task;
            }
        }
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
