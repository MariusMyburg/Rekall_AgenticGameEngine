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
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.SelectProviderAsync("ollama");
        Assert.True(viewModel.CanFinish);
        viewModel.SelectedModelId = "invented-model";
        Assert.True(viewModel.CanFinish);

        await ExecuteAsync(viewModel.FinishCommand);

        Assert.Equal(2, store.Saved.Count);
        Assert.False(store.Saved[0].IsComplete);
        var saved = store.Saved[^1];
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
    public async Task StoreLoadSuccessButWriteVerificationFailureKeepsFinishDisabled()
    {
        var store = new RecordingSetupStore { SaveFailure = new IOException("read-only setup root") };
        var probe = new RecordingProbe((request, _) => Task.FromResult(Ready("ollama", "qwen3.8:27b")));
        await using var viewModel = CreateViewModel(store: store, probe: probe);

        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.SelectProviderAsync("ollama");

        Assert.False(viewModel.CanFinish);
        Assert.False(viewModel.FinishCommand.CanExecute(null));
        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task EndpointMutationImmediatelyInvalidatesReadinessAndFreshProbeUsesCurrentEndpoint()
    {
        var freshProbeStarted = new TaskCompletionSource<RekallAgeLanguageModelReadinessRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var freshProbeRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var probe = new RecordingProbe(async (request, cancellationToken) =>
        {
            if (Interlocked.Increment(ref calls) == 1) return Ready("ollama", "qwen3.8:27b");
            freshProbeStarted.SetResult(request);
            await freshProbeRelease.Task.WaitAsync(cancellationToken);
            return Ready("ollama", "qwen3.8:27b");
        });
        await using var viewModel = CreateViewModel(probe: probe);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.SelectProviderAsync("ollama");
        Assert.True(viewModel.CanFinish);

        viewModel.OllamaUrl = "http://127.0.0.1:22444";

        Assert.False(viewModel.CanFinish);
        var request = await freshProbeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("http://127.0.0.1:22444", request.Settings.OllamaUrl);
        freshProbeRelease.SetResult();
        await viewModel.WaitForActiveOperationAsync();
        Assert.True(viewModel.CanFinish);
    }

    [Fact]
    public async Task HostedEndpointEditDuringCredentialReadReResolvesRememberedKeyAndRetryStaysCredentialed()
    {
        const string rememberedKey = "remembered-race-key";
        var credentials = new BlockingRememberedCredentialStore("openai", rememberedKey);
        var probe = new RecordingProbe((request, _) => Task.FromResult(Ready("openai", "gpt-5.6-sol")));
        await using var viewModel = CreateViewModel(credentials: credentials, probe: probe);
        await viewModel.InitializeAsync(CancellationToken.None);

        var staleSelection = viewModel.SelectProviderAsync("openai");
        await credentials.FirstReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.OpenAiUrl = "https://fresh-openai.example/v1";
        Assert.False(viewModel.CanFinish);
        credentials.ReleaseReads();
        await Task.WhenAll(staleSelection, viewModel.WaitForActiveOperationAsync());

        var freshRequest = Assert.Single(probe.Requests);
        Assert.Equal("https://fresh-openai.example/v1", freshRequest.Settings.OpenAiUrl);
        Assert.Equal(rememberedKey, freshRequest.Settings.OpenAiApiKey);
        Assert.Equal("Remembered securely on this PC", viewModel.CredentialSourceLabel);
        Assert.Equal(RekallAgeLanguageModelReadinessState.Ready, viewModel.ReadinessState);

        await ExecuteAsync(viewModel.RetryCommand);

        Assert.Equal(2, probe.Requests.Count);
        Assert.Equal(rememberedKey, probe.Requests[^1].Settings.OpenAiApiKey);
        Assert.Equal("https://fresh-openai.example/v1", probe.Requests[^1].Settings.OpenAiUrl);
        Assert.DoesNotContain(probe.Requests, request =>
            request.Settings.OpenAiUrl != "https://fresh-openai.example/v1");
    }

    [Fact]
    public async Task HostedEndpointEditReReadsAuthoritativeRememberedCredentialUnlessKeyWasAppliedForSession()
    {
        var credentials = new RecordingCredentialStore();
        credentials.Values["openai"] = "remembered-before-endpoint-edit";
        var probe = new RecordingProbe((request, _) => Task.FromResult(Ready("openai", "gpt-5.6-sol")));
        await using var viewModel = CreateViewModel(credentials: credentials, probe: probe);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.SelectProviderAsync("openai");
        credentials.Values["openai"] = "remembered-after-endpoint-edit";

        viewModel.OpenAiUrl = "https://rotated-openai.example/v1";
        await viewModel.WaitForActiveOperationAsync();

        Assert.Equal("remembered-after-endpoint-edit", probe.Requests[^1].Settings.OpenAiApiKey);
        Assert.Equal("https://rotated-openai.example/v1", probe.Requests[^1].Settings.OpenAiUrl);
    }

    [Fact]
    public async Task SlowCredentialApplyAndRemoveWindowsKeepFinishDisabledUntilFreshResultsPublish()
    {
        var credentials = new RecordingCredentialStore();
        credentials.Values["openai"] = "remembered-old-key";
        credentials.BlockWrites();
        var probe = new RecordingProbe((request, _) => Task.FromResult(
            string.IsNullOrWhiteSpace(request.Settings.OpenAiApiKey)
                ? Blocked("openai", "REKALL_ONBOARDING_API_KEY_REQUIRED", "enter-api-key")
                : Ready("openai", "gpt-5.6-sol")));
        await using var viewModel = CreateViewModel(credentials: credentials, probe: probe);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.SelectProviderAsync("openai");
        Assert.True(viewModel.CanFinish);

        var apply = viewModel.ApplyApiKeyAsync("openai", "replacement-key", rememberSecurely: true);
        Assert.False(viewModel.CanFinish);
        await credentials.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        credentials.ReleaseWrites();
        await apply;
        Assert.True(viewModel.CanFinish);

        viewModel.OpenAiUrl = "https://session-key-endpoint.example/v1";
        await viewModel.WaitForActiveOperationAsync();
        Assert.Equal("replacement-key", probe.Requests[^1].Settings.OpenAiApiKey);
        Assert.Equal("https://session-key-endpoint.example/v1", probe.Requests[^1].Settings.OpenAiUrl);

        credentials.BlockRemoves();
        var remove = viewModel.RemoveRememberedApiKeyAsync("openai");
        Assert.False(viewModel.CanFinish);
        await credentials.RemoveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        credentials.ReleaseRemoves();
        await remove;
        Assert.False(viewModel.CanFinish);
    }

    [Fact]
    public async Task SlowRemediationImmediatelyInvalidatesReadyStateAndRebuildsCurrentSettings()
    {
        var actionRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var probe = new RecordingProbe((request, _) => Task.FromResult(
            Interlocked.Increment(ref calls) == 1
                ? Ready(request.ProviderId, "qwen3.8:27b") with
                {
                    RecommendedActionId = "pull-qwen3.8:27b"
                }
                : Ready(request.ProviderId, "qwen3.8:27b")));
        var actions = new RecordingActions(async (_, _, cancellationToken) =>
            await actionRelease.Task.WaitAsync(cancellationToken));
        await using var viewModel = CreateViewModel(probe: probe, actions: actions);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.SelectProviderAsync("ollama");
        Assert.True(viewModel.CanFinish);

        var action = viewModel.ExecuteRemediationAsync("pull-qwen3.8:27b");

        Assert.False(viewModel.CanFinish);
        await actions.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.OllamaUrl = "http://127.0.0.1:33444";
        Assert.False(viewModel.CanFinish);
        actionRelease.SetResult();
        await action;
        await viewModel.WaitForActiveOperationAsync();
        Assert.Equal("http://127.0.0.1:33444", probe.Requests.Last().Settings.OllamaUrl);
    }

    [Fact]
    public async Task HostileReadinessStringsAreSanitizedBeforeAnyPublicProjection()
    {
        var hostile = $"hostile-{SentinelSecret}";
        var environment = new RecordingEnvironment(new Dictionary<string, string>
        {
            ["OPENAI_API_KEY"] = SentinelSecret
        });
        var probe = new RecordingProbe((request, _) => Task.FromResult(new RekallAgeLanguageModelReadinessResult(
            request.ProviderId,
            RekallAgeLanguageModelReadinessState.Blocked,
            hostile,
            hostile,
            [new RekallAgeLanguageModelReadinessCheck(hostile, RekallAgeLanguageModelReadinessState.Blocked, hostile, hostile)],
            [hostile],
            hostile,
            true)));
        await using var viewModel = CreateViewModel(probe: probe, environment: environment);

        await viewModel.SelectProviderAsync("openai");

        AssertNoSecretInInspectableState(viewModel, SentinelSecret);
        Assert.All(viewModel.ReadinessRows, row =>
            Assert.DoesNotContain(SentinelSecret, row.ToString(), StringComparison.Ordinal));
        Assert.DoesNotContain(SentinelSecret, viewModel.RecommendedActionId ?? string.Empty, StringComparison.Ordinal);
        Assert.False(viewModel.RemediationCommand(hostile).CanExecute(null));
    }

    [Fact]
    public async Task AsyncReadinessPublicationIsMarshaledThroughCapturedSynchronizationContext()
    {
        var context = new QueueingSynchronizationContext();
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        RekallAgeStudioLanguageModelSetupViewModel viewModel;
        try
        {
            var probe = new RecordingProbe(async (request, _) =>
            {
                await Task.Run(static () => { });
                return Ready(request.ProviderId, "qwen3.8:27b");
            });
            viewModel = CreateViewModel(probe: probe);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        try
        {
            var transition = viewModel.SelectProviderAsync("ollama");
            await context.WaitForPostAsync();
            Assert.Equal(RekallAgeLanguageModelReadinessState.Blocked, viewModel.ReadinessState);
            Assert.False(transition.IsCompleted);

            await context.DrainUntilCompletedAsync(transition);

            Assert.True(context.PostCount > 0);
            Assert.Equal(RekallAgeLanguageModelReadinessState.Ready, viewModel.ReadinessState);
        }
        finally
        {
            await context.DrainUntilCompletedAsync(viewModel.DisposeAsync().AsTask());
        }
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
        IRekallAgeStudioCredentialStore? credentials = null,
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
        public Exception? SaveFailure { get; set; }
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
            if (SaveFailure is not null) return ValueTask.FromException(SaveFailure);
            Saved.Add(setup);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingCredentialStore : IRekallAgeStudioCredentialStore
    {
        private TaskCompletionSource? _writeRelease;
        private TaskCompletionSource? _removeRelease;
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
        public TaskCompletionSource WriteStarted { get; private set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RemoveStarted { get; private set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<string?> ReadAsync(string providerId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Values.GetValueOrDefault(providerId));
        }

        public async ValueTask WriteAsync(string providerId, string credential, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteStarted.TrySetResult();
            if (_writeRelease is not null) await _writeRelease.Task.WaitAsync(cancellationToken);
            Values[providerId] = credential;
        }

        public async ValueTask RemoveAsync(string providerId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RemoveStarted.TrySetResult();
            if (_removeRelease is not null) await _removeRelease.Task.WaitAsync(cancellationToken);
            Values.Remove(providerId);
        }

        public void BlockWrites()
        {
            WriteStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _writeRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void ReleaseWrites() => _writeRelease?.TrySetResult();

        public void BlockRemoves()
        {
            RemoveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _removeRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void ReleaseRemoves() => _removeRelease?.TrySetResult();
    }

    private sealed class BlockingRememberedCredentialStore(string providerId, string credential)
        : IRekallAgeStudioCredentialStore
    {
        private readonly TaskCompletionSource _releaseReads =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _readCount;

        public TaskCompletionSource FirstReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<string?> ReadAsync(string requestedProviderId, CancellationToken cancellationToken)
        {
            Assert.Equal(providerId, requestedProviderId);
            if (Interlocked.Increment(ref _readCount) == 1) FirstReadStarted.SetResult();
            await _releaseReads.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return credential;
        }

        public ValueTask WriteAsync(string requestedProviderId, string value, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask RemoveAsync(string requestedProviderId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void ReleaseReads() => _releaseReads.TrySetResult();
    }

    private sealed class RecordingProbe(
        Func<RekallAgeLanguageModelReadinessRequest, CancellationToken, Task<RekallAgeLanguageModelReadinessResult>> probe)
        : IRekallAgeLanguageModelReadinessProbe
    {
        private readonly Dictionary<string, TaskCompletionSource> _calls = new(StringComparer.Ordinal);
        public int CallCount { get; private set; }
        public List<RekallAgeLanguageModelReadinessRequest> Requests { get; } = [];

        public async ValueTask<RekallAgeLanguageModelReadinessResult> ProbeAsync(
            RekallAgeLanguageModelReadinessRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Requests.Add(request);
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

    private sealed class QueueingSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _work = new();
        private TaskCompletionSource _posted = NewSignal();

        public int PostCount { get; private set; }

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (_work)
            {
                _work.Enqueue((d, state));
                PostCount++;
                _posted.TrySetResult();
            }
        }

        public Task WaitForPostAsync()
        {
            lock (_work)
            {
                return _work.Count > 0
                    ? Task.CompletedTask
                    : _posted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        public async Task DrainUntilCompletedAsync(Task task)
        {
            while (!task.IsCompleted)
            {
                Task posted;
                lock (_work)
                {
                    posted = _work.Count > 0 ? Task.CompletedTask : _posted.Task;
                }
                await Task.WhenAny(task, posted).WaitAsync(TimeSpan.FromSeconds(5));
                Drain();
                await Task.Yield();
            }
            await task;
        }

        private void Drain()
        {
            while (true)
            {
                (SendOrPostCallback Callback, object? State) work;
                lock (_work)
                {
                    if (_work.Count == 0)
                    {
                        _posted = NewSignal();
                        return;
                    }
                    work = _work.Dequeue();
                }
                work.Callback(work.State);
            }
        }

        private static TaskCompletionSource NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
