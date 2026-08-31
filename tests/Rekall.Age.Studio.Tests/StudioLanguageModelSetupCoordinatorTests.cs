using System.Windows;

namespace Rekall.Age.Studio.Tests;

[Collection(WpfApplicationTestCollection.Name)]
public sealed class StudioLanguageModelSetupCoordinatorTests(WpfApplicationTestFixture wpf)
{
    [Fact]
    public Task MissingSettingsOpenTheOwnedWizardAfterStartupSetupInitialization() =>
        VerifyWizardDecisionAsync(RekallAgeStudioLanguageModelSetup.Incomplete, Blocked(), expectedWindows: 1);

    [Fact]
    public Task ExplicitlyIncompleteSettingsReopenTheOwnedWizardOnEveryOrdinaryLaunch() =>
        VerifyWizardDecisionAsync(
            RekallAgeStudioLanguageModelSetup.Incomplete with { ProviderId = "codex", ModelId = "gpt-5.6-sol" },
            Blocked("codex"),
            expectedWindows: 1);

    [Fact]
    public Task CompletedHealthySettingsSkipTheWizard() =>
        VerifyWizardDecisionAsync(CompletedSetup(), Ready(), expectedWindows: 0);

    [Fact]
    public Task CompletedSettingsWithBlockedSanityOpenRecovery() =>
        VerifyWizardDecisionAsync(CompletedSetup(), Blocked(), expectedWindows: 1);

    [Fact]
    public Task AutomationBypassesSettingsProbeRestoreAndWizard() => wpf.InvokeAsync(async () =>
    {
        var store = new RecordingSetupStore(CompletedSetup());
        var probe = new RecordingReadinessProbe(Ready());
        var restorer = new RecordingStudioRestorer();
        var windows = new RecordingWindowFactory();
        var coordinator = new RekallAgeStudioLanguageModelSetupCoordinator(
            store,
            new EmptyCredentialStore(),
            probe,
            windows,
            new EmptyEnvironment(),
            restorer,
            isAutomation: () => true);
        await using var studio = new RekallAgeStudioViewModel();
        var owner = new Window();

        await coordinator.InitializeAsync(owner, studio, CancellationToken.None);

        Assert.Equal(0, store.LoadCount);
        Assert.Empty(probe.Requests);
        Assert.Empty(restorer.Restores);
        Assert.Equal(0, windows.ShowCount);
        owner.Close();
    });

    [Theory]
    [InlineData("remembered-key", "environment-key", "remembered-key")]
    [InlineData(null, "environment-key", "environment-key")]
    [InlineData(null, null, null)]
    public Task StartupCredentialPriorityIsRememberedThenEnvironmentThenNone(
        string? remembered,
        string? environment,
        string? expected) => wpf.InvokeAsync(async () =>
    {
        var setup = CompletedSetup() with { ProviderId = "openai", ModelId = "gpt-5.6-sol" };
        var probe = new RecordingReadinessProbe(Ready("openai"));
        var restorer = new RecordingStudioRestorer();
        var coordinator = new RekallAgeStudioLanguageModelSetupCoordinator(
            new FixedSetupStore(setup),
            new FixedCredentialStore(remembered),
            probe,
            new RecordingWindowFactory(),
            new FixedEnvironment(environment),
            restorer);
        await using var studio = new RekallAgeStudioViewModel();
        var owner = new Window();

        await coordinator.InitializeAsync(owner, studio, CancellationToken.None);

        Assert.Equal(expected, Assert.Single(probe.Requests).Settings.OpenAiApiKey);
        Assert.Equal(expected, Assert.Single(restorer.Restores).OpenAiCredential);
        Assert.DoesNotContain(remembered ?? "unused-remembered", coordinator.SetupStatusText, StringComparison.Ordinal);
        Assert.DoesNotContain(environment ?? "unused-environment", coordinator.SetupStatusText, StringComparison.Ordinal);
        owner.Close();
    });

    [Fact]
    public Task FutureSettingsOpenIncompleteRecovery() => wpf.InvokeAsync(async () =>
    {
        var windows = new RecordingWindowFactory();
        var coordinator = new RekallAgeStudioLanguageModelSetupCoordinator(
            new FixedSetupStore(CompletedSetup() with { Version = RekallAgeStudioLanguageModelSetup.CurrentVersion + 1 }),
            new EmptyCredentialStore(),
            new FixedReadinessProbe(Blocked()),
            windows,
            new EmptyEnvironment(),
            new RecordingStudioRestorer());
        await using var studio = new RekallAgeStudioViewModel();
        var owner = new Window();

        await coordinator.InitializeAsync(owner, studio, CancellationToken.None);

        Assert.True(coordinator.IsSetupIncomplete);
        Assert.Equal(1, windows.ShowCount);
        owner.Close();
    });

    [Fact]
    public Task SettingsAlwaysReopensSetupEvenAfterHealthyInitialization() => wpf.InvokeAsync(async () =>
    {
        var windows = new RecordingWindowFactory();
        var coordinator = new RekallAgeStudioLanguageModelSetupCoordinator(
            new FixedSetupStore(CompletedSetup()),
            new EmptyCredentialStore(),
            new FixedReadinessProbe(Ready()),
            windows,
            new EmptyEnvironment(),
            new RecordingStudioRestorer());
        await using var studio = new RekallAgeStudioViewModel();
        var owner = new Window();
        await coordinator.InitializeAsync(owner, studio, CancellationToken.None);

        await coordinator.ShowSetupAsync(owner, studio, CancellationToken.None);

        Assert.Equal(1, windows.ShowCount);
        Assert.True(coordinator.IsSetupIncomplete);
        owner.Close();
    });

    [Fact]
    public Task ExplicitSessionKeyEnteredInWizardIsAppliedToStudioAfterFinish() => wpf.InvokeAsync(async () =>
    {
        const string sessionKey = "explicit-session-only-key";
        var setup = RekallAgeStudioLanguageModelSetup.Incomplete with
        {
            ProviderId = "openai",
            ModelId = "gpt-5.6-sol"
        };
        var restorer = new RecordingStudioRestorer();
        var coordinator = new RekallAgeStudioLanguageModelSetupCoordinator(
            new FixedSetupStore(setup),
            new EmptyCredentialStore(),
            new FixedReadinessProbe(Ready("openai")),
            new ExplicitSessionWindowFactory(sessionKey),
            new EmptyEnvironment(),
            restorer);
        await using var studio = new RekallAgeStudioViewModel();
        var owner = new Window();

        await coordinator.ShowSetupAsync(owner, studio, CancellationToken.None);

        Assert.Equal(sessionKey, Assert.Single(restorer.Restores).OpenAiCredential);
        Assert.False(coordinator.IsSetupIncomplete);
        Assert.DoesNotContain(sessionKey, coordinator.SetupStatusText, StringComparison.Ordinal);
        owner.Close();
    });

    [Fact]
    public Task CredentialStoreFailureOpensRedactedRecoveryWithoutBlockingStudioRestore() => wpf.InvokeAsync(async () =>
    {
        const string secret = "credential-store-secret-payload";
        var setup = CompletedSetup() with { ProviderId = "openai", ModelId = "gpt-5.6-sol" };
        var probe = new RecordingReadinessProbe(Ready("openai"));
        var restorer = new RecordingStudioRestorer();
        var windows = new RecordingWindowFactory();
        var coordinator = new RekallAgeStudioLanguageModelSetupCoordinator(
            new FixedSetupStore(setup),
            new ThrowingCredentialStore(secret),
            probe,
            windows,
            new FixedEnvironment("environment-key-must-not-mask-store-failure"),
            restorer);
        await using var studio = new RekallAgeStudioViewModel();
        var owner = new Window();

        await coordinator.InitializeAsync(owner, studio, CancellationToken.None);

        Assert.Empty(probe.Requests);
        Assert.Null(Assert.Single(restorer.Restores).OpenAiCredential);
        Assert.Equal(1, windows.ShowCount);
        Assert.True(coordinator.IsSetupIncomplete);
        Assert.DoesNotContain(secret, coordinator.SetupStatusText, StringComparison.Ordinal);
        owner.Close();
    });

    [Fact]
    public Task ProviderRestoreFailureStillOpensRecoveryAndDoesNotBlockManualStartup() => wpf.InvokeAsync(async () =>
    {
        const string secret = "provider-restore-secret-payload";
        var windows = new RecordingWindowFactory();
        var coordinator = new RekallAgeStudioLanguageModelSetupCoordinator(
            new FixedSetupStore(CompletedSetup()),
            new EmptyCredentialStore(),
            new FixedReadinessProbe(Ready()),
            windows,
            new EmptyEnvironment(),
            new ThrowingStudioRestorer(secret));
        await using var studio = new RekallAgeStudioViewModel();
        var owner = new Window();

        await coordinator.InitializeAsync(owner, studio, CancellationToken.None);

        Assert.Equal(1, windows.ShowCount);
        Assert.True(coordinator.IsSetupIncomplete);
        Assert.DoesNotContain(secret, coordinator.SetupStatusText, StringComparison.Ordinal);
        owner.Close();
    });

    [Fact]
    public Task UnexpectedReadinessFailureOpensRecoveryWithoutPublishingFailureText() => wpf.InvokeAsync(async () =>
    {
        const string secret = "probe-secret-payload";
        var windows = new RecordingWindowFactory();
        var coordinator = new RekallAgeStudioLanguageModelSetupCoordinator(
            new FixedSetupStore(CompletedSetup()),
            new EmptyCredentialStore(),
            new ThrowingReadinessProbe(secret),
            windows,
            new EmptyEnvironment(),
            new RecordingStudioRestorer());
        await using var studio = new RekallAgeStudioViewModel();
        var owner = new Window();

        await coordinator.InitializeAsync(owner, studio, CancellationToken.None);

        Assert.Equal(1, windows.ShowCount);
        Assert.True(coordinator.IsSetupIncomplete);
        Assert.DoesNotContain(secret, coordinator.SetupStatusText, StringComparison.Ordinal);
        owner.Close();
    });

    [Fact]
    public Task FinishedWizardKeepsRecoveryVisibleWhenStudioRestoreFails() => wpf.InvokeAsync(async () =>
    {
        var setup = RekallAgeStudioLanguageModelSetup.Incomplete with
        {
            ProviderId = "openai",
            ModelId = "gpt-5.6-sol"
        };
        var coordinator = new RekallAgeStudioLanguageModelSetupCoordinator(
            new FixedSetupStore(setup),
            new EmptyCredentialStore(),
            new FixedReadinessProbe(Ready("openai")),
            new ExplicitSessionWindowFactory("session-key"),
            new EmptyEnvironment(),
            new ThrowingStudioRestorer("restore failed"));
        await using var studio = new RekallAgeStudioViewModel();
        var owner = new Window();

        await coordinator.ShowSetupAsync(owner, studio, CancellationToken.None);

        Assert.True(coordinator.IsSetupIncomplete);
        Assert.False(studio.LanguageModelSetupAllowsAuthoring);
        owner.Close();
    });

    [Fact]
    public Task StartupProviderRestoreUsesABoundedCancellationToken() => wpf.InvokeAsync(async () =>
    {
        var restorer = new CancellationRecordingStudioRestorer();
        var coordinator = new RekallAgeStudioLanguageModelSetupCoordinator(
            new FixedSetupStore(CompletedSetup()),
            new EmptyCredentialStore(),
            new FixedReadinessProbe(Ready()),
            new RecordingWindowFactory(),
            new EmptyEnvironment(),
            restorer,
            probeTimeout: TimeSpan.FromMilliseconds(50));
        await using var studio = new RekallAgeStudioViewModel();
        var owner = new Window();

        await coordinator.InitializeAsync(owner, studio, CancellationToken.None);

        Assert.True(restorer.CanBeCanceled);
        owner.Close();
    });

    private Task VerifyWizardDecisionAsync(
        RekallAgeStudioLanguageModelSetup setup,
        RekallAgeLanguageModelReadinessResult readiness,
        int expectedWindows) => wpf.InvokeAsync(async () =>
    {
        var windows = new RecordingWindowFactory();
        var coordinator = new RekallAgeStudioLanguageModelSetupCoordinator(
            new FixedSetupStore(setup),
            new EmptyCredentialStore(),
            new FixedReadinessProbe(readiness),
            windows,
            new EmptyEnvironment(),
            new RecordingStudioRestorer());
        await using var studio = new RekallAgeStudioViewModel();
        var owner = new Window();

        await coordinator.InitializeAsync(owner, studio, CancellationToken.None);

        Assert.Equal(expectedWindows, windows.ShowCount);
        Assert.Equal(expectedWindows != 0, coordinator.IsSetupIncomplete);
        Assert.Equal(expectedWindows == 0, studio.LanguageModelSetupAllowsAuthoring);
        owner.Close();
    });

    private static RekallAgeStudioLanguageModelSetup CompletedSetup() =>
        RekallAgeStudioLanguageModelSetup.Incomplete with
        {
            IsComplete = true,
            LastSuccessfulCheckUtc = new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero)
        };

    private static RekallAgeLanguageModelReadinessResult Ready(string providerId = "ollama") => new(
        providerId,
        RekallAgeLanguageModelReadinessState.Ready,
        "REKALL_ONBOARDING_READY",
        "Provider is ready.",
        [],
        [providerId == "ollama" ? "qwen3.8:27b" : "gpt-5.6-sol"],
        null,
        true);

    private static RekallAgeLanguageModelReadinessResult Blocked(string providerId = "ollama") => new(
        providerId,
        RekallAgeLanguageModelReadinessState.Blocked,
        "REKALL_ONBOARDING_OFFLINE",
        "Provider is offline.",
        [],
        [],
        "retry",
        true);

    private sealed class FixedSetupStore(RekallAgeStudioLanguageModelSetup setup)
        : IRekallAgeStudioLanguageModelSetupStore
    {
        public ValueTask<RekallAgeStudioLanguageModelSetup> LoadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(setup);

        public ValueTask SaveAsync(
            RekallAgeStudioLanguageModelSetup value,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class EmptyCredentialStore : IRekallAgeStudioCredentialStore
    {
        public ValueTask<string?> ReadAsync(string providerId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(null);

        public ValueTask WriteAsync(string providerId, string credential, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask RemoveAsync(string providerId, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class FixedCredentialStore(string? credential) : IRekallAgeStudioCredentialStore
    {
        public ValueTask<string?> ReadAsync(string providerId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(credential);

        public ValueTask WriteAsync(string providerId, string value, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask RemoveAsync(string providerId, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class ThrowingCredentialStore(string message) : IRekallAgeStudioCredentialStore
    {
        public ValueTask<string?> ReadAsync(string providerId, CancellationToken cancellationToken) =>
            throw new RekallAgeStudioCredentialStoreException("REKALL_CREDENTIAL_STORE_CORRUPT", message);

        public ValueTask WriteAsync(string providerId, string credential, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask RemoveAsync(string providerId, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class FixedReadinessProbe(RekallAgeLanguageModelReadinessResult result)
        : IRekallAgeLanguageModelReadinessProbe
    {
        public ValueTask<RekallAgeLanguageModelReadinessResult> ProbeAsync(
            RekallAgeLanguageModelReadinessRequest request,
            CancellationToken cancellationToken) => ValueTask.FromResult(result);
    }

    private sealed class RecordingReadinessProbe(RekallAgeLanguageModelReadinessResult result)
        : IRekallAgeLanguageModelReadinessProbe
    {
        public List<RekallAgeLanguageModelReadinessRequest> Requests { get; } = [];

        public ValueTask<RekallAgeLanguageModelReadinessResult> ProbeAsync(
            RekallAgeLanguageModelReadinessRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ThrowingReadinessProbe(string message) : IRekallAgeLanguageModelReadinessProbe
    {
        public ValueTask<RekallAgeLanguageModelReadinessResult> ProbeAsync(
            RekallAgeLanguageModelReadinessRequest request,
            CancellationToken cancellationToken) => throw new InvalidOperationException(message);
    }

    private sealed class EmptyEnvironment : IRekallAgeEnvironmentValueSource
    {
        public string? GetValue(string name) => null;
    }

    private sealed class FixedEnvironment(string? openAiKey) : IRekallAgeEnvironmentValueSource
    {
        public string? GetValue(string name) => name == "OPENAI_API_KEY" ? openAiKey : null;
    }

    private sealed class RecordingWindowFactory : IRekallAgeStudioLanguageModelSetupWindowFactory
    {
        public int ShowCount { get; private set; }

        public ValueTask<RekallAgeStudioLanguageModelSetupWindowOutcome> ShowAsync(
            Window owner,
            RekallAgeStudioLanguageModelSetupViewModel viewModel,
            CancellationToken cancellationToken)
        {
            ShowCount++;
            return ValueTask.FromResult(RekallAgeStudioLanguageModelSetupWindowOutcome.Deferred);
        }
    }

    private sealed class ExplicitSessionWindowFactory(string sessionKey)
        : IRekallAgeStudioLanguageModelSetupWindowFactory
    {
        public async ValueTask<RekallAgeStudioLanguageModelSetupWindowOutcome> ShowAsync(
            Window owner,
            RekallAgeStudioLanguageModelSetupViewModel viewModel,
            CancellationToken cancellationToken)
        {
            await viewModel.ApplyApiKeyAsync("openai", sessionKey, rememberSecurely: false);
            await ((RekallAgeAsyncCommand)viewModel.FinishCommand).ExecuteAsync(null);
            return RekallAgeStudioLanguageModelSetupWindowOutcome.Completed;
        }
    }

    private sealed class RecordingStudioRestorer : IRekallAgeStudioLanguageModelSetupRestorer
    {
        public List<RestoreCall> Restores { get; } = [];

        public Task RestoreAsync(
            RekallAgeStudioViewModel studio,
            RekallAgeStudioLanguageModelSetup setup,
            string? openAiCredential,
            string? kimiCredential,
            CancellationToken cancellationToken)
        {
            Restores.Add(new RestoreCall(setup, openAiCredential, kimiCredential));
            return Task.CompletedTask;
        }
    }

    private sealed record RestoreCall(
        RekallAgeStudioLanguageModelSetup Setup,
        string? OpenAiCredential,
        string? KimiCredential);

    private sealed class ThrowingStudioRestorer(string message) : IRekallAgeStudioLanguageModelSetupRestorer
    {
        public Task RestoreAsync(
            RekallAgeStudioViewModel studio,
            RekallAgeStudioLanguageModelSetup setup,
            string? openAiCredential,
            string? kimiCredential,
            CancellationToken cancellationToken) => throw new InvalidOperationException(message);
    }

    private sealed class CancellationRecordingStudioRestorer : IRekallAgeStudioLanguageModelSetupRestorer
    {
        public bool CanBeCanceled { get; private set; }

        public Task RestoreAsync(
            RekallAgeStudioViewModel studio,
            RekallAgeStudioLanguageModelSetup setup,
            string? openAiCredential,
            string? kimiCredential,
            CancellationToken cancellationToken)
        {
            CanBeCanceled = cancellationToken.CanBeCanceled;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSetupStore(RekallAgeStudioLanguageModelSetup setup)
        : IRekallAgeStudioLanguageModelSetupStore
    {
        public int LoadCount { get; private set; }

        public ValueTask<RekallAgeStudioLanguageModelSetup> LoadAsync(CancellationToken cancellationToken)
        {
            LoadCount++;
            return ValueTask.FromResult(setup);
        }

        public ValueTask SaveAsync(
            RekallAgeStudioLanguageModelSetup value,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
