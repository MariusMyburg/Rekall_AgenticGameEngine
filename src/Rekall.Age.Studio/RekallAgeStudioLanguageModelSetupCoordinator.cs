using System.Windows;
using Rekall.Age.Agent.LanguageModels;
using Rekall.Age.Workflows;

namespace Rekall.Age.Studio;

internal interface IRekallAgeStudioLanguageModelSetupWindowFactory
{
    ValueTask<RekallAgeStudioLanguageModelSetupWindowOutcome> ShowAsync(
        Window owner,
        RekallAgeStudioLanguageModelSetupViewModel viewModel,
        CancellationToken cancellationToken);
}

internal static class RekallAgeStudioStartupSequence
{
    public static async Task RunAsync(
        Func<CancellationToken, Task> loadAndApplyLayout,
        Func<CancellationToken, Task> initializeLanguageModelSetup,
        Func<CancellationToken, Task> initializeProject,
        Func<bool> hasProject,
        Action selectWorld,
        Action queueLanguageModelRefresh,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(loadAndApplyLayout);
        ArgumentNullException.ThrowIfNull(initializeLanguageModelSetup);
        ArgumentNullException.ThrowIfNull(initializeProject);
        ArgumentNullException.ThrowIfNull(hasProject);
        ArgumentNullException.ThrowIfNull(selectWorld);
        ArgumentNullException.ThrowIfNull(queueLanguageModelRefresh);
        cancellationToken.ThrowIfCancellationRequested();

        await loadAndApplyLayout(cancellationToken);
        await initializeLanguageModelSetup(cancellationToken);
        await initializeProject(cancellationToken);
        if (hasProject()) selectWorld();
        queueLanguageModelRefresh();
    }
}

internal interface IRekallAgeStudioLanguageModelSetupRestorer
{
    Task RestoreAsync(
        RekallAgeStudioViewModel studio,
        RekallAgeStudioLanguageModelSetup setup,
        string? openAiCredential,
        string? kimiCredential,
        CancellationToken cancellationToken);
}

internal sealed class RekallAgeStudioLanguageModelSetupCoordinator
{
    private static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(5);
    private readonly IRekallAgeStudioLanguageModelSetupStore _setupStore;
    private readonly IRekallAgeStudioCredentialStore _credentialStore;
    private readonly IRekallAgeLanguageModelReadinessProbe _readinessProbe;
    private readonly IRekallAgeStudioLanguageModelSetupWindowFactory _windowFactory;
    private readonly IRekallAgeEnvironmentValueSource _environment;
    private readonly IRekallAgeStudioLanguageModelSetupRestorer _studioRestorer;
    private readonly Func<bool> _isAutomation;
    private readonly TimeSpan _probeTimeout;

    public RekallAgeStudioLanguageModelSetupCoordinator()
        : this(
            new RekallAgeStudioLanguageModelSetupStore(),
            new RekallAgeStudioDpapiCredentialStore(),
            new RekallAgeLanguageModelReadinessProbe(new RekallAgeLanguageModelProviderCatalog()),
            SystemWindowFactory.Instance,
            SystemEnvironment.Instance,
            StudioRestorer.Instance,
            IsAutomationProcess,
            DefaultProbeTimeout)
    {
    }

    internal RekallAgeStudioLanguageModelSetupCoordinator(
        IRekallAgeStudioLanguageModelSetupStore setupStore,
        IRekallAgeStudioCredentialStore credentialStore,
        IRekallAgeLanguageModelReadinessProbe readinessProbe,
        IRekallAgeStudioLanguageModelSetupWindowFactory windowFactory,
        IRekallAgeEnvironmentValueSource environment,
        IRekallAgeStudioLanguageModelSetupRestorer studioRestorer,
        Func<bool>? isAutomation = null,
        TimeSpan? probeTimeout = null)
    {
        _setupStore = setupStore ?? throw new ArgumentNullException(nameof(setupStore));
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        _readinessProbe = readinessProbe ?? throw new ArgumentNullException(nameof(readinessProbe));
        _windowFactory = windowFactory ?? throw new ArgumentNullException(nameof(windowFactory));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _studioRestorer = studioRestorer ?? throw new ArgumentNullException(nameof(studioRestorer));
        _isAutomation = isAutomation ?? (() => false);
        _probeTimeout = probeTimeout ?? DefaultProbeTimeout;
        if (_probeTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(probeTimeout));
    }

    public bool IsSetupIncomplete { get; private set; }

    public string SetupStatusText { get; private set; } = "AI setup not checked.";

    internal bool ShouldRefreshLanguageModels { get; private set; }

    public async Task InitializeAsync(
        Window owner,
        RekallAgeStudioViewModel studio,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(studio);
        cancellationToken.ThrowIfCancellationRequested();

        if (_isAutomation())
        {
            studio.SetLanguageModelSetupAvailability(true);
            IsSetupIncomplete = false;
            ShouldRefreshLanguageModels = false;
            SetupStatusText = "AI setup skipped for automation.";
            return;
        }

        var setup = RekallAgeStudioLanguageModelSetup.Normalize(
                await _setupStore.LoadAsync(cancellationToken))
            ?? RekallAgeStudioLanguageModelSetup.Incomplete;
        ResolvedCredentials credentials;
        RekallAgeLanguageModelReadinessResult readiness;
        try
        {
            credentials = await ResolveCredentialsAsync(setup.ProviderId, cancellationToken);
            readiness = await ProbeAsync(setup, credentials, cancellationToken);
        }
        catch (RekallAgeStudioCredentialStoreException)
        {
            credentials = default;
            readiness = Blocked(setup.ProviderId, "REKALL_CREDENTIAL_STORE_UNAVAILABLE");
        }

        var restored = true;
        using var restorationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        restorationCancellation.CancelAfter(_probeTimeout);
        try
        {
            await _studioRestorer.RestoreAsync(
                studio,
                setup,
                credentials.OpenAi,
                credentials.Kimi,
                restorationCancellation.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            restored = false;
        }

        var ready = restored
            && readiness.State == RekallAgeLanguageModelReadinessState.Ready
            && readiness.ProviderId.Equals(setup.ProviderId, StringComparison.Ordinal);
        ShouldRefreshLanguageModels = setup.IsComplete && ready;
        IsSetupIncomplete = !setup.IsComplete || !ready;
        studio.SetLanguageModelSetupAvailability(!IsSetupIncomplete);
        SetupStatusText = IsSetupIncomplete ? "AI setup incomplete." : "AI setup ready.";
        if (IsSetupIncomplete)
        {
            await ShowSetupAsync(owner, studio, cancellationToken);
        }
    }

    public async Task ShowSetupAsync(
        Window owner,
        RekallAgeStudioViewModel studio,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(studio);
        cancellationToken.ThrowIfCancellationRequested();

        await using var viewModel = new RekallAgeStudioLanguageModelSetupViewModel(
            _setupStore,
            _credentialStore,
            _readinessProbe,
            environment: _environment);
        await viewModel.InitializeAsync(cancellationToken);
        await viewModel.SelectProviderAsync(viewModel.SelectedProviderId);
        var outcome = await _windowFactory.ShowAsync(owner, viewModel, cancellationToken);
        if (outcome == RekallAgeStudioLanguageModelSetupWindowOutcome.Completed
            && viewModel.CompletedSetup is { IsComplete: true } completed)
        {
            try
            {
                using var restorationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                restorationCancellation.CancelAfter(_probeTimeout);
                await viewModel.RestoreCompletedSetupAsync(
                    _studioRestorer,
                    studio,
                    completed,
                    restorationCancellation.Token);
                IsSetupIncomplete = false;
                ShouldRefreshLanguageModels = true;
                SetupStatusText = "AI setup ready.";
                studio.SetLanguageModelSetupAvailability(true);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Preferences were saved, but the live provider could not be restored.
                // Keep manual editing available and leave the configure affordance visible.
            }
        }

        IsSetupIncomplete = true;
        ShouldRefreshLanguageModels = false;
        SetupStatusText = "AI setup incomplete.";
        studio.SetLanguageModelSetupAvailability(false);
    }

    private async Task<ResolvedCredentials> ResolveCredentialsAsync(
        string providerId,
        CancellationToken cancellationToken)
    {
        if (providerId is not ("openai" or "kimi")) return default;
        var remembered = await _credentialStore.ReadAsync(providerId, cancellationToken);
        var credential = string.IsNullOrWhiteSpace(remembered)
            ? EnvironmentCredential(providerId)
            : remembered;
        return providerId == "openai"
            ? new ResolvedCredentials(credential, null)
            : new ResolvedCredentials(null, credential);
    }

    private string? EnvironmentCredential(string providerId)
    {
        if (providerId == "openai") return NonBlank(_environment.GetValue("OPENAI_API_KEY"));
        return NonBlank(_environment.GetValue("KIMI_API_KEY"))
            ?? NonBlank(_environment.GetValue("MOONSHOT_API_KEY"));
    }

    private async Task<RekallAgeLanguageModelReadinessResult> ProbeAsync(
        RekallAgeStudioLanguageModelSetup setup,
        ResolvedCredentials credentials,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_probeTimeout);
        try
        {
            return await _readinessProbe.ProbeAsync(
                new RekallAgeLanguageModelReadinessRequest(
                    setup.ProviderId,
                    setup.ModelId,
                    new RekallAgeLanguageModelProviderSettings
                    {
                        OllamaUrl = setup.OllamaUrl,
                        OpenAiUrl = setup.OpenAiUrl,
                        OpenAiApiKey = credentials.OpenAi,
                        KimiUrl = setup.KimiUrl,
                        KimiApiKey = credentials.Kimi
                    }),
                timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Blocked(setup.ProviderId, "REKALL_ONBOARDING_TIMEOUT");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Blocked(setup.ProviderId, "REKALL_ONBOARDING_OPERATION_FAILED");
        }
    }

    private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static RekallAgeLanguageModelReadinessResult Blocked(string providerId, string code) => new(
        providerId,
        RekallAgeLanguageModelReadinessState.Blocked,
        code,
        "Language-model setup needs attention.",
        [],
        [],
        "retry",
        true);

    private static bool IsAutomationProcess() => Environment.GetCommandLineArgs().Contains(
        RekallAgeStudioAutomation.AutomationSwitch,
        StringComparer.Ordinal);

    private readonly record struct ResolvedCredentials(string? OpenAi, string? Kimi);

    private sealed class StudioRestorer : IRekallAgeStudioLanguageModelSetupRestorer
    {
        public static StudioRestorer Instance { get; } = new();

        public Task RestoreAsync(
            RekallAgeStudioViewModel studio,
            RekallAgeStudioLanguageModelSetup setup,
            string? openAiCredential,
            string? kimiCredential,
            CancellationToken cancellationToken) => studio.RestoreLanguageModelSetupAsync(
                setup,
                openAiCredential,
                kimiCredential,
                cancellationToken);
    }

    private sealed class SystemWindowFactory : IRekallAgeStudioLanguageModelSetupWindowFactory
    {
        public static SystemWindowFactory Instance { get; } = new();

        public ValueTask<RekallAgeStudioLanguageModelSetupWindowOutcome> ShowAsync(
            Window owner,
            RekallAgeStudioLanguageModelSetupViewModel viewModel,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var window = new LanguageModelSetupWindow(owner, viewModel);
            _ = window.ShowDialog();
            return ValueTask.FromResult(window.Outcome);
        }
    }

    private sealed class SystemEnvironment : IRekallAgeEnvironmentValueSource
    {
        public static SystemEnvironment Instance { get; } = new();
        public string? GetValue(string name) => Environment.GetEnvironmentVariable(name);
    }
}
