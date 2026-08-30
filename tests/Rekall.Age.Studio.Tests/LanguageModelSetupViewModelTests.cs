using System.Reflection;
using System.IO;
using Rekall.Age.Agent.LanguageModels;

namespace Rekall.Age.Studio.Tests;

public sealed class LanguageModelSetupViewModelTests
{
    private const string SentinelSecret = "sentinel-wizard-secret-value";

    [Fact]
    public async Task InitialStateStartsAtWelcomeAndNavigatesTheFiveProviderNeutralSteps()
    {
        await using var viewModel = CreateViewModel();

        Assert.Equal(RekallAgeStudioLanguageModelSetupStep.Welcome, viewModel.CurrentStep);
        Assert.False(viewModel.BackCommand.CanExecute(null));

        await ExecuteAsync(viewModel.NextCommand);
        Assert.Equal(RekallAgeStudioLanguageModelSetupStep.Provider, viewModel.CurrentStep);
        await ExecuteAsync(viewModel.NextCommand);
        Assert.Equal(RekallAgeStudioLanguageModelSetupStep.Configuration, viewModel.CurrentStep);
        await ExecuteAsync(viewModel.NextCommand);
        Assert.Equal(RekallAgeStudioLanguageModelSetupStep.Model, viewModel.CurrentStep);
        await ExecuteAsync(viewModel.NextCommand);
        Assert.Equal(RekallAgeStudioLanguageModelSetupStep.Summary, viewModel.CurrentStep);
        Assert.False(viewModel.NextCommand.CanExecute(null));
    }

    [Fact]
    public async Task ProviderSelectionExposesOnlyTheSelectedProviderConfiguration()
    {
        await using var viewModel = CreateViewModel();

        await viewModel.SelectProviderAsync("openai");

        Assert.True(viewModel.IsOpenAiSelected);
        Assert.False(viewModel.IsOllamaSelected);
        Assert.False(viewModel.IsGgufSelected);
        Assert.False(viewModel.IsKimiSelected);
        Assert.False(viewModel.IsCodexSelected);
    }

    [Fact]
    public async Task ProviderGenerationRejectsALateProbeResultFromThePreviousProvider()
    {
        var staleRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new RecordingProbe(async (request, cancellationToken) =>
        {
            if (request.ProviderId == "openai")
            {
                await staleRelease.Task;
                return Ready("openai", "gpt-5.6-sol");
            }

            return Blocked("kimi", "REKALL_ONBOARDING_API_KEY_REQUIRED", "enter-api-key");
        });
        await using var viewModel = CreateViewModel(probe: probe);

        var stale = viewModel.SelectProviderAsync("openai");
        await probe.WaitForCallAsync("openai");
        var current = viewModel.SelectProviderAsync("kimi");
        staleRelease.SetResult();
        await Task.WhenAll(stale, current);

        Assert.Equal("kimi", viewModel.SelectedProviderId);
        Assert.Equal("REKALL_ONBOARDING_API_KEY_REQUIRED", viewModel.ReadinessCode);
        Assert.Empty(viewModel.CompatibleModels);
    }

    [Theory]
    [InlineData("REKALL_ONBOARDING_OLLAMA_RUNTIME_MISSING", "open-ollama-download")]
    [InlineData("REKALL_ONBOARDING_OLLAMA_SERVICE_STOPPED", "start-ollama")]
    [InlineData("REKALL_ONBOARDING_NO_MODELS", "pull-qwen3.8:27b")]
    public async Task OllamaFailuresExposeTheMatchingRemediationCommand(
        string code,
        string actionId)
    {
        var probe = new RecordingProbe((request, _) =>
            Task.FromResult(Blocked(request.ProviderId, code, actionId)));
        await using var viewModel = CreateViewModel(probe: probe);

        await viewModel.SelectProviderAsync("ollama");

        Assert.Equal(actionId, viewModel.RecommendedActionId);
        Assert.True(viewModel.RemediationCommand(actionId).CanExecute(null));
    }

    [Fact]
    public async Task GenericOllamaDownloadReadinessActionMapsToTheConcretePullCommandId()
    {
        var probe = new RecordingProbe((request, _) => Task.FromResult(
            Blocked(request.ProviderId, "REKALL_ONBOARDING_NO_MODELS", "download-default-model")));
        await using var viewModel = CreateViewModel(probe: probe);

        await viewModel.SelectProviderAsync("ollama");

        Assert.Equal("pull-qwen3.8:27b", viewModel.RecommendedActionId);
        Assert.True(viewModel.PullRecommendedOllamaModelCommand.CanExecute(null));
    }

    [Fact]
    public async Task ApplyingACloudKeyCanRememberItWithoutPublishingSecretMaterial()
    {
        var credentials = new RecordingCredentialStore();
        var probe = new RecordingProbe((request, _) => Task.FromResult(
            string.IsNullOrWhiteSpace(request.Settings.OpenAiApiKey)
                ? Blocked("openai", "REKALL_ONBOARDING_API_KEY_REQUIRED", "enter-api-key")
                : Ready("openai", "gpt-5.6-sol")));
        await using var viewModel = CreateViewModel(credentials: credentials, probe: probe);
        await viewModel.SelectProviderAsync("openai");

        await viewModel.ApplyApiKeyAsync("openai", SentinelSecret, rememberSecurely: true);

        Assert.Equal(SentinelSecret, credentials.Values["openai"]);
        Assert.Equal("Remembered securely on this PC", viewModel.CredentialSourceLabel);
        AssertNoSecretInInspectableState(viewModel, SentinelSecret);
    }

    [Fact]
    public async Task EnvironmentAndRememberedCredentialSourcesPublishOnlySourceLabels()
    {
        var credentials = new RecordingCredentialStore();
        credentials.Values["kimi"] = SentinelSecret;
        var environment = new RecordingEnvironment(new Dictionary<string, string>
        {
            ["OPENAI_API_KEY"] = SentinelSecret + "-environment"
        });
        var probe = new RecordingProbe((request, _) => Task.FromResult(
            Ready(request.ProviderId, request.ProviderId == "openai" ? "gpt-5.6-sol" : "kimi-k3")));
        await using var viewModel = CreateViewModel(credentials, probe, environment: environment);

        await viewModel.SelectProviderAsync("openai");
        Assert.Equal("Environment variable OPENAI_API_KEY", viewModel.CredentialSourceLabel);
        AssertNoSecretInInspectableState(viewModel, SentinelSecret + "-environment");

        await viewModel.SelectProviderAsync("kimi");
        Assert.Equal("Remembered securely on this PC", viewModel.CredentialSourceLabel);
        AssertNoSecretInInspectableState(viewModel, SentinelSecret);
    }

    [Fact]
    public async Task RemovingARememberedKeyClearsSessionCredentialAndRecomputesReadiness()
    {
        var credentials = new RecordingCredentialStore();
        credentials.Values["openai"] = SentinelSecret;
        var probe = new RecordingProbe((request, _) => Task.FromResult(
            string.IsNullOrWhiteSpace(request.Settings.OpenAiApiKey)
                ? Blocked("openai", "REKALL_ONBOARDING_API_KEY_REQUIRED", "enter-api-key")
                : Ready("openai", "gpt-5.6-sol")));
        await using var viewModel = CreateViewModel(credentials, probe);
        await viewModel.SelectProviderAsync("openai");
        Assert.Equal(RekallAgeLanguageModelReadinessState.Ready, viewModel.ReadinessState);

        await viewModel.RemoveRememberedApiKeyAsync("openai");

        Assert.False(credentials.Values.ContainsKey("openai"));
        Assert.Equal(RekallAgeLanguageModelReadinessState.Blocked, viewModel.ReadinessState);
        Assert.Equal("REKALL_ONBOARDING_API_KEY_REQUIRED", viewModel.ReadinessCode);
        Assert.Equal("No credential configured", viewModel.CredentialSourceLabel);
    }

    [Fact]
    public async Task SetUpLaterPersistsOnlyIncompleteState()
    {
        var store = new RecordingSetupStore();
        await using var viewModel = CreateViewModel(store: store);

        await ExecuteAsync(viewModel.SetUpLaterCommand);

        var saved = Assert.Single(store.Saved);
        Assert.False(saved.IsComplete);
        Assert.False(viewModel.CompletedSetup?.IsComplete);
    }

    [Fact]
    public async Task FinishRequiresReadyCompatibleModelAndWritableStoreThenPersistsVersionedCompletion()
    {
        var now = new DateTimeOffset(2026, 8, 30, 10, 11, 12, TimeSpan.Zero);
        var store = new RecordingSetupStore();
        var probe = new RecordingProbe((request, _) => Task.FromResult(Ready("ollama", "qwen3.8:27b")));
        await using var viewModel = CreateViewModel(store: store, probe: probe, utcNow: () => now);

        Assert.False(viewModel.CanFinish);
        await viewModel.SelectProviderAsync("ollama");
        Assert.True(viewModel.CanFinish);
        viewModel.SelectedModelId = "invented-model";
        Assert.True(viewModel.CanFinish);

        await ExecuteAsync(viewModel.FinishCommand);

        var saved = Assert.Single(store.Saved);
        Assert.True(saved.IsComplete);
        Assert.Equal(RekallAgeStudioLanguageModelSetup.CurrentVersion, saved.Version);
        Assert.Equal(RekallAgeStudioLanguageModelSetup.CurrentReadinessVersion, saved.ReadinessVersion);
        Assert.Equal(now, saved.LastSuccessfulCheckUtc);
        Assert.Equal("qwen3.8:27b", saved.ModelId);
        Assert.Equal(saved, viewModel.CompletedSetup);
    }

    [Fact]
    public async Task StoreLoadFailureKeepsFinishDisabledEvenWhenProviderIsReady()
    {
        var store = new RecordingSetupStore { LoadFailure = new IOException("read-only setup root") };
        var probe = new RecordingProbe((request, _) => Task.FromResult(Ready("ollama", "qwen3.8:27b")));
        await using var viewModel = CreateViewModel(store: store, probe: probe);

        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.SelectProviderAsync("ollama");

        Assert.False(viewModel.CanFinish);
        Assert.False(viewModel.FinishCommand.CanExecute(null));
    }

    [Fact]
    public async Task ShutdownCancelsAndAwaitsActiveProbeAndPullOperations()
    {
        var pullCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new RecordingProbe(async (request, cancellationToken) =>
        {
            await Task.Yield();
            return Blocked("ollama", "REKALL_ONBOARDING_NO_MODELS", "pull-qwen3.8:27b");
        });
        var actions = new RecordingActions(async (actionId, _, cancellationToken) =>
        {
            Assert.Equal("pull-qwen3.8:27b", actionId);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { pullCancelled.SetResult(); throw; }
        });
        var viewModel = CreateViewModel(probe: probe, actions: actions);
        await viewModel.SelectProviderAsync("ollama");
        var pull = viewModel.ExecuteRemediationAsync("pull-qwen3.8:27b");
        await actions.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await viewModel.DisposeAsync();

        await pullCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(pull.IsCompleted);
        Assert.Equal(1, probe.CallCount);
    }

    [Fact]
    public async Task ShutdownCancelsAnActiveReadinessProbe()
    {
        var probeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probeCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new RecordingProbe(async (request, cancellationToken) =>
        {
            probeStarted.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { probeCancelled.SetResult(); throw; }
            throw new InvalidOperationException("The cancelled readiness probe unexpectedly resumed.");
        });
        var viewModel = CreateViewModel(probe: probe);
        var activeProbe = viewModel.SelectProviderAsync("ollama");
        await probeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await viewModel.DisposeAsync();

        await probeCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(activeProbe.IsCompleted);
    }

    private static RekallAgeStudioLanguageModelSetupViewModel CreateViewModel(
        RecordingCredentialStore? credentials = null,
        RecordingProbe? probe = null,
        RecordingSetupStore? store = null,
        RecordingActions? actions = null,
        RecordingEnvironment? environment = null,
        Func<DateTimeOffset>? utcNow = null) => new(
            store ?? new RecordingSetupStore(),
            credentials ?? new RecordingCredentialStore(),
            probe ?? new RecordingProbe((request, _) => Task.FromResult(
                Blocked(request.ProviderId, "REKALL_ONBOARDING_NOT_CHECKED", "retry"))),
            actions ?? new RecordingActions((_, _, _) => Task.CompletedTask),
            environment ?? new RecordingEnvironment(new Dictionary<string, string>()),
            utcNow ?? (() => DateTimeOffset.UtcNow));

    private static RekallAgeLanguageModelReadinessResult Ready(string providerId, string model) => new(
        providerId,
        RekallAgeLanguageModelReadinessState.Ready,
        "REKALL_ONBOARDING_READY",
        "Provider ready.",
        [new RekallAgeLanguageModelReadinessCheck("provider", RekallAgeLanguageModelReadinessState.Ready, "Provider ready.")],
        [model],
        null,
        false);

    private static RekallAgeLanguageModelReadinessResult Blocked(
        string providerId,
        string code,
        string actionId) => new(
        providerId,
        RekallAgeLanguageModelReadinessState.Blocked,
        code,
        "Provider needs attention.",
        [new RekallAgeLanguageModelReadinessCheck("provider", RekallAgeLanguageModelReadinessState.Blocked, "Provider needs attention.", actionId)],
        [],
        actionId,
        true);

    private static async Task ExecuteAsync(System.Windows.Input.ICommand command)
    {
        var asyncCommand = Assert.IsType<RekallAgeAsyncCommand>(command);
        await asyncCommand.ExecuteAsync(null);
    }

    private static void AssertNoSecretInInspectableState(
        RekallAgeStudioLanguageModelSetupViewModel viewModel,
        string secret)
    {
        var strings = viewModel.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.PropertyType == typeof(string) && property.GetIndexParameters().Length == 0)
            .Select(property => property.GetValue(viewModel) as string)
            .Where(value => value is not null)
            .Cast<string>()
            .Concat(viewModel.ReadinessRows.SelectMany(row => new[] { row.Id, row.StatusGlyph, row.Label, row.Detail }));
        Assert.All(strings, value => Assert.DoesNotContain(secret, value, StringComparison.Ordinal));
    }

    private sealed class RecordingSetupStore : IRekallAgeStudioLanguageModelSetupStore
    {
        public Exception? LoadFailure { get; set; }
        public List<RekallAgeStudioLanguageModelSetup> Saved { get; } = [];

        public ValueTask<RekallAgeStudioLanguageModelSetup> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return LoadFailure is null
                ? ValueTask.FromResult(RekallAgeStudioLanguageModelSetup.Incomplete)
                : ValueTask.FromException<RekallAgeStudioLanguageModelSetup>(LoadFailure);
        }

        public ValueTask SaveAsync(RekallAgeStudioLanguageModelSetup setup, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Saved.Add(setup);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingCredentialStore : IRekallAgeStudioCredentialStore
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

        public ValueTask<string?> ReadAsync(string providerId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Values.GetValueOrDefault(providerId));
        }

        public ValueTask WriteAsync(string providerId, string credential, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Values[providerId] = credential;
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveAsync(string providerId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Values.Remove(providerId);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingProbe(
        Func<RekallAgeLanguageModelReadinessRequest, CancellationToken, Task<RekallAgeLanguageModelReadinessResult>> probe)
        : IRekallAgeLanguageModelReadinessProbe
    {
        private readonly Dictionary<string, TaskCompletionSource> _calls = new(StringComparer.Ordinal);
        public int CallCount { get; private set; }

        public async ValueTask<RekallAgeLanguageModelReadinessResult> ProbeAsync(
            RekallAgeLanguageModelReadinessRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            lock (_calls)
            {
                if (!_calls.TryGetValue(request.ProviderId, out var call))
                {
                    call = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _calls.Add(request.ProviderId, call);
                }
                call.TrySetResult();
            }
            return await probe(request, cancellationToken);
        }

        public Task WaitForCallAsync(string providerId)
        {
            lock (_calls)
            {
                if (!_calls.TryGetValue(providerId, out var call))
                {
                    call = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _calls.Add(providerId, call);
                }
                return call.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    private sealed class RecordingActions(
        Func<string, string, CancellationToken, Task> execute)
        : IRekallAgeStudioLanguageModelSetupActions
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask ExecuteAsync(string actionId, string providerId, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await execute(actionId, providerId, cancellationToken);
        }
    }

    private sealed class RecordingEnvironment(IReadOnlyDictionary<string, string> values)
        : IRekallAgeEnvironmentValueSource
    {
        public string? GetValue(string name) => values.GetValueOrDefault(name);
    }
}
