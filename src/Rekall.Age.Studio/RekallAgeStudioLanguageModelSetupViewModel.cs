using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Rekall.Age.Agent.LanguageModels;
using Rekall.Age.Workflows;

namespace Rekall.Age.Studio;

internal enum RekallAgeStudioLanguageModelSetupStep
{
    Welcome,
    Provider,
    Configuration,
    Model,
    Summary
}

internal sealed record RekallAgeStudioLanguageModelReadinessRow(
    string Id,
    string StatusGlyph,
    string Label,
    string Detail,
    RekallAgeLanguageModelReadinessState State);

internal interface IRekallAgeStudioLanguageModelSetupActions
{
    ValueTask ExecuteAsync(
        string actionId,
        string providerId,
        CancellationToken cancellationToken);
}

internal sealed class RekallAgeStudioLanguageModelSetupViewModel :
    INotifyPropertyChanged,
    IAsyncDisposable
{
    private enum ActiveCredentialRetention
    {
        None,
        AppliedSession,
        ExternalSource
    }

    internal const string OpenOllamaDownloadActionId = "open-ollama-download";
    internal const string StartOllamaActionId = "start-ollama";
    internal const string PullRecommendedOllamaModelActionId = "pull-qwen3.8:27b";

    private static readonly IReadOnlySet<string> ProviderIds = new HashSet<string>(
        ["ollama", "gguf", "kimi", "openai", "codex"],
        StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> HostedProviderIds = new HashSet<string>(
        ["openai", "kimi"],
        StringComparer.Ordinal);
    private readonly IRekallAgeStudioLanguageModelSetupStore _setupStore;
    private readonly IRekallAgeStudioCredentialStore _credentialStore;
    private readonly IRekallAgeLanguageModelReadinessProbe _readinessProbe;
    private readonly IRekallAgeStudioLanguageModelSetupActions _actions;
    private readonly IRekallAgeEnvironmentValueSource _environment;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly SynchronizationContext? _synchronizationContext;
    private readonly CancellationTokenSource _lifecycleCancellation = new();
    private readonly object _operationSync = new();
    private readonly Dictionary<string, RekallAgeAsyncCommand> _remediationCommands;
    private CancellationTokenSource? _activeOperationCancellation;
    private Task _activeOperation = Task.CompletedTask;
    private long _operationGeneration;
    private RekallAgeLanguageModelProviderSettings _activeProviderSettings = new();
    private ActiveCredentialRetention _activeCredentialRetention;
    private RekallAgeStudioLanguageModelSetupStep _currentStep;
    private string _selectedProviderId = "ollama";
    private string _selectedModelId = RekallAgeStudioLanguageModelSetup.Incomplete.ModelId;
    private string _reasoningEffort = RekallAgeStudioLanguageModelSetup.Incomplete.ReasoningEffort;
    private string? _ollamaUrl;
    private string? _openAiUrl;
    private string? _kimiUrl;
    private RekallAgeLanguageModelReadinessState _readinessState = RekallAgeLanguageModelReadinessState.Blocked;
    private string _readinessCode = "REKALL_ONBOARDING_NOT_CHECKED";
    private string _readinessSummary = "Language-model setup has not been checked.";
    private string? _recommendedActionId;
    private string _credentialSourceLabel = "No credential required";
    private string _errorSummary = string.Empty;
    private RekallAgeStudioLanguageModelSetup? _completedSetup;
    private bool _setupStoreAvailable;
    private bool _disposed;

    internal RekallAgeStudioLanguageModelSetupViewModel(
        IRekallAgeStudioLanguageModelSetupStore setupStore,
        IRekallAgeStudioCredentialStore credentialStore,
        IRekallAgeLanguageModelReadinessProbe readinessProbe,
        IRekallAgeStudioLanguageModelSetupActions? actions = null,
        IRekallAgeEnvironmentValueSource? environment = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _setupStore = setupStore ?? throw new ArgumentNullException(nameof(setupStore));
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        _readinessProbe = readinessProbe ?? throw new ArgumentNullException(nameof(readinessProbe));
        _actions = actions ?? NoOpActions.Instance;
        _environment = environment ?? SystemEnvironment.Instance;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _synchronizationContext = SynchronizationContext.Current;

        NextCommand = CreateCommand(MoveNextAsync, () => CurrentStep < RekallAgeStudioLanguageModelSetupStep.Summary);
        BackCommand = CreateCommand(MoveBackAsync, () => CurrentStep > RekallAgeStudioLanguageModelSetupStep.Welcome);
        RetryCommand = CreateCommand(RetryAsync, () => !_disposed && !string.IsNullOrWhiteSpace(SelectedProviderId));
        SetUpLaterCommand = CreateCommand(SetUpLaterAsync, () => !_disposed);
        FinishCommand = CreateCommand(FinishAsync, () => CanFinish);
        OpenOllamaDownloadCommand = CreateRemediationCommand(OpenOllamaDownloadActionId);
        StartOllamaCommand = CreateRemediationCommand(StartOllamaActionId);
        PullRecommendedOllamaModelCommand = CreateRemediationCommand(PullRecommendedOllamaModelActionId);
        _remediationCommands = new Dictionary<string, RekallAgeAsyncCommand>(StringComparer.Ordinal)
        {
            [OpenOllamaDownloadActionId] = OpenOllamaDownloadCommand,
            [StartOllamaActionId] = StartOllamaCommand,
            [PullRecommendedOllamaModelActionId] = PullRecommendedOllamaModelCommand
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RekallAgeStudioLanguageModelSetupStep CurrentStep
    {
        get => _currentStep;
        private set
        {
            if (!Set(ref _currentStep, value)) return;
            RaiseCommands();
        }
    }

    public IReadOnlyList<string> ProviderIdsInDisplayOrder { get; } =
        ["ollama", "gguf", "kimi", "openai", "codex"];

    public string SelectedProviderId
    {
        get => _selectedProviderId;
        set
        {
            var normalized = NormalizeProviderId(value);
            if (normalized.Equals(_selectedProviderId, StringComparison.Ordinal)) return;
            _ = SelectProviderAsync(normalized);
        }
    }

    public bool IsOllamaSelected => SelectedProviderId == "ollama";
    public bool IsGgufSelected => SelectedProviderId == "gguf";
    public bool IsKimiSelected => SelectedProviderId == "kimi";
    public bool IsOpenAiSelected => SelectedProviderId == "openai";
    public bool IsCodexSelected => SelectedProviderId == "codex";

    public ObservableCollection<string> CompatibleModels { get; } = [];
    public ObservableCollection<RekallAgeStudioLanguageModelReadinessRow> ReadinessRows { get; } = [];
    public IReadOnlyList<string> ReasoningEfforts { get; } =
        ["none", "low", "medium", "high", "xhigh", "max"];

    public string SelectedModelId
    {
        get => _selectedModelId;
        set
        {
            var candidate = value?.Trim() ?? string.Empty;
            if (candidate.Length > 0 && !CompatibleModels.Contains(candidate, StringComparer.Ordinal)) return;
            if (Set(ref _selectedModelId, candidate)) RaiseCommands();
        }
    }

    public string ReasoningEffort
    {
        get => _reasoningEffort;
        set
        {
            var candidate = value?.Trim().ToLowerInvariant() ?? string.Empty;
            if (candidate is not ("none" or "low" or "medium" or "high" or "xhigh" or "max")) return;
            Set(ref _reasoningEffort, candidate);
        }
    }

    public string? OllamaUrl
    {
        get => _ollamaUrl;
        set => SetEndpoint(ref _ollamaUrl, NormalizeEndpoint(value));
    }

    public string? OpenAiUrl
    {
        get => _openAiUrl;
        set => SetEndpoint(ref _openAiUrl, NormalizeEndpoint(value));
    }

    public string? KimiUrl
    {
        get => _kimiUrl;
        set => SetEndpoint(ref _kimiUrl, NormalizeEndpoint(value));
    }

    public RekallAgeLanguageModelReadinessState ReadinessState
    {
        get => _readinessState;
        private set
        {
            if (Set(ref _readinessState, value)) RaiseCommands();
        }
    }

    public string ReadinessCode
    {
        get => _readinessCode;
        private set => Set(ref _readinessCode, value);
    }

    public string ReadinessSummary
    {
        get => _readinessSummary;
        private set => Set(ref _readinessSummary, value);
    }

    public string? RecommendedActionId
    {
        get => _recommendedActionId;
        private set
        {
            if (Set(ref _recommendedActionId, value)) RaiseCommands();
        }
    }

    public string CredentialSourceLabel
    {
        get => _credentialSourceLabel;
        private set => Set(ref _credentialSourceLabel, value);
    }

    public string ErrorSummary
    {
        get => _errorSummary;
        private set => Set(ref _errorSummary, value);
    }

    public bool CanFinish => !_disposed
        && _setupStoreAvailable
        && ReadinessState == RekallAgeLanguageModelReadinessState.Ready
        && CompatibleModels.Contains(SelectedModelId, StringComparer.Ordinal);

    public RekallAgeStudioLanguageModelSetup? CompletedSetup
    {
        get => _completedSetup;
        private set => Set(ref _completedSetup, value);
    }

    public ICommand NextCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand SetUpLaterCommand { get; }
    public ICommand FinishCommand { get; }
    public RekallAgeAsyncCommand OpenOllamaDownloadCommand { get; }
    public RekallAgeAsyncCommand StartOllamaCommand { get; }
    public RekallAgeAsyncCommand PullRecommendedOllamaModelCommand { get; }

    internal async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifecycleCancellation.Token);
        try
        {
            var loaded = await _setupStore.LoadAsync(linkedCancellation.Token).ConfigureAwait(false);
            var setup = RekallAgeStudioLanguageModelSetup.Normalize(loaded)
                ?? RekallAgeStudioLanguageModelSetup.Incomplete;
            await _setupStore.SaveAsync(setup, linkedCancellation.Token).ConfigureAwait(false);
            await PublishOnUiAsync(() =>
            {
                _setupStoreAvailable = true;
                _selectedProviderId = setup.ProviderId;
                _selectedModelId = setup.ModelId;
                _reasoningEffort = setup.ReasoningEffort;
                _ollamaUrl = setup.OllamaUrl;
                _openAiUrl = setup.OpenAiUrl;
                _kimiUrl = setup.KimiUrl;
                OnPropertyChanged(nameof(ReasoningEffort));
                OnPropertyChanged(nameof(OllamaUrl));
                OnPropertyChanged(nameof(OpenAiUrl));
                OnPropertyChanged(nameof(KimiUrl));
                OnProviderSelectionChanged();
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            await PublishOnUiAsync(() =>
            {
                _setupStoreAvailable = false;
                ErrorSummary = "Language-model setup settings are unavailable on this PC.";
                OnPropertyChanged(nameof(CanFinish));
                RaiseCommands();
            }).ConfigureAwait(false);
        }
    }

    internal Task SelectProviderAsync(string providerId)
    {
        ThrowIfDisposed();
        providerId = NormalizeProviderId(providerId);
        var preferredModel = DefaultModel(providerId);
        _selectedProviderId = providerId;
        _selectedModelId = preferredModel;
        _activeProviderSettings = CreateNonSecretSettings();
        _activeCredentialRetention = ActiveCredentialRetention.None;
        CredentialSourceLabel = HostedProviderIds.Contains(providerId)
            ? "Checking credential source"
            : "No credential required";
        ClearReadiness();
        OnProviderSelectionChanged();
        return QueueOperationAsync((generation, cancellationToken) =>
            LoadCredentialAndProbeAsync(providerId, preferredModel, generation, cancellationToken));
    }

    internal Task RefreshCurrentProviderAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        var providerId = SelectedProviderId;
        var preferredModel = SelectedModelId;
        _activeProviderSettings = CreateNonSecretSettings();
        _activeCredentialRetention = ActiveCredentialRetention.None;
        CredentialSourceLabel = HostedProviderIds.Contains(providerId)
            ? "Checking credential source"
            : "No credential required";
        ClearReadiness();
        return QueueOperationAsync(
            (generation, operationCancellation) => LoadCredentialAndProbeAsync(
                providerId,
                preferredModel,
                generation,
                operationCancellation),
            cancellationToken,
            propagateCallerCancellation: true);
    }

    internal Task ApplyApiKeyAsync(string providerId, string key, bool rememberSecurely)
    {
        ThrowIfDisposed();
        providerId = NormalizeProviderId(providerId);
        if (!HostedProviderIds.Contains(providerId))
        {
            throw new ArgumentException("Only hosted API providers accept API keys.", nameof(providerId));
        }
        if (!providerId.Equals(SelectedProviderId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A key can only be applied to the selected provider.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var settings = SettingsWithCredential(providerId, key);
        _activeProviderSettings = settings;
        _activeCredentialRetention = ActiveCredentialRetention.AppliedSession;
        CredentialSourceLabel = rememberSecurely
            ? "Verifying protected credential"
            : "Verifying session credential";
        ClearReadiness();

        return QueueOperationAsync(async (generation, cancellationToken) =>
        {
            if (rememberSecurely)
            {
                await _credentialStore.WriteAsync(providerId, key, cancellationToken).ConfigureAwait(false);
            }
            if (!IsCurrentOperation(providerId, generation)) return;
            if (!TrySetActiveProviderSettings(
                    providerId,
                    generation,
                    settings,
                    ActiveCredentialRetention.AppliedSession)) return;
            await PublishOnUiAsync(() =>
            {
                if (!IsCurrentOperation(providerId, generation)) return;
                CredentialSourceLabel = rememberSecurely
                    ? "Remembered securely on this PC"
                    : "This Studio session";
            }).ConfigureAwait(false);
            await ProbeAndPublishAsync(providerId, SelectedModelId, settings, generation, cancellationToken)
                .ConfigureAwait(false);
        });
    }

    internal Task RemoveRememberedApiKeyAsync(string providerId)
    {
        ThrowIfDisposed();
        providerId = NormalizeProviderId(providerId);
        if (!HostedProviderIds.Contains(providerId))
        {
            throw new ArgumentException("Only hosted API providers have remembered API keys.", nameof(providerId));
        }
        if (!providerId.Equals(SelectedProviderId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A key can only be removed from the selected provider.");
        }

        CredentialSourceLabel = "Removing remembered credential";
        ClearReadiness();

        return QueueOperationAsync(async (generation, cancellationToken) =>
        {
            await _credentialStore.RemoveAsync(providerId, cancellationToken).ConfigureAwait(false);
            if (!IsCurrentOperation(providerId, generation)) return;
            var (credential, sourceLabel) = EnvironmentCredential(providerId);
            var settings = SettingsWithCredential(providerId, credential);
            var retention = string.IsNullOrWhiteSpace(credential)
                ? ActiveCredentialRetention.None
                : ActiveCredentialRetention.ExternalSource;
            if (!TrySetActiveProviderSettings(providerId, generation, settings, retention)) return;
            await PublishOnUiAsync(() =>
            {
                if (IsCurrentOperation(providerId, generation)) CredentialSourceLabel = sourceLabel;
            }).ConfigureAwait(false);
            await ProbeAndPublishAsync(providerId, SelectedModelId, settings, generation, cancellationToken)
                .ConfigureAwait(false);
        });
    }

    internal ICommand RemediationCommand(string actionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        return _remediationCommands.TryGetValue(actionId, out var command)
            ? command
            : DisabledCommand.Instance;
    }

    internal Task ExecuteRemediationAsync(string actionId)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        if (!string.Equals(RecommendedActionId, actionId, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }
        var providerId = SelectedProviderId;
        ClearReadiness();
        return QueueOperationAsync(async (generation, cancellationToken) =>
        {
            await _actions.ExecuteAsync(actionId, providerId, cancellationToken).ConfigureAwait(false);
            if (!IsCurrentOperation(providerId, generation)) return;
            var settings = RebuildActiveProviderSettings();
            await ResolveCredentialAndProbeAsync(
                    providerId,
                    SelectedModelId,
                    settings,
                    generation,
                    cancellationToken)
                .ConfigureAwait(false);
        });
    }

    internal Task WaitForActiveOperationAsync()
    {
        lock (_operationSync) return _activeOperation;
    }

    internal Task RestoreCompletedSetupAsync(
        IRekallAgeStudioLanguageModelSetupRestorer restorer,
        RekallAgeStudioViewModel studio,
        RekallAgeStudioLanguageModelSetup setup,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(restorer);
        ArgumentNullException.ThrowIfNull(studio);
        ArgumentNullException.ThrowIfNull(setup);
        ThrowIfDisposed();
        return restorer.RestoreAsync(
            studio,
            setup,
            setup.ProviderId == "openai" ? _activeProviderSettings.OpenAiApiKey : null,
            setup.ProviderId == "kimi" ? _activeProviderSettings.KimiApiKey : null,
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Task operation;
        lock (_operationSync)
        {
            if (_disposed) return;
            _disposed = true;
            _lifecycleCancellation.Cancel();
            _activeOperationCancellation?.Cancel();
            operation = _activeOperation;
        }
        try
        {
            await operation.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _activeProviderSettings = new RekallAgeLanguageModelProviderSettings();
            _activeCredentialRetention = ActiveCredentialRetention.None;
            _activeOperationCancellation?.Dispose();
            _lifecycleCancellation.Dispose();
            await PublishOnUiAsync(RaiseCommands).ConfigureAwait(false);
        }
    }

    private Task QueueOperationAsync(
        Func<long, CancellationToken, Task> operation,
        CancellationToken callerCancellation = default,
        bool propagateCallerCancellation = false)
    {
        lock (_operationSync)
        {
            var previous = _activeOperation;
            var previousCancellation = _activeOperationCancellation;
            previousCancellation?.Cancel();
            var generation = checked(++_operationGeneration);
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _lifecycleCancellation.Token,
                callerCancellation);
            _activeOperationCancellation = cancellation;
            _activeOperation = RunOperationAfterAsync(
                previous,
                previousCancellation,
                operation,
                generation,
                cancellation,
                callerCancellation,
                propagateCallerCancellation);
            return _activeOperation;
        }
    }

    private async Task RunOperationAfterAsync(
        Task previous,
        CancellationTokenSource? previousCancellation,
        Func<long, CancellationToken, Task> operation,
        long generation,
        CancellationTokenSource cancellation,
        CancellationToken callerCancellation,
        bool propagateCallerCancellation)
    {
        try
        {
            try { await previous.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception) { }
            finally { previousCancellation?.Dispose(); }
            cancellation.Token.ThrowIfCancellationRequested();
            await operation(generation, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            propagateCallerCancellation && callerCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (RekallAgeStudioCredentialStoreException)
        {
            await PublishOnUiAsync(() => PublishCredentialStoreFailure(generation)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await PublishOnUiAsync(() => PublishOperationFailure(generation)).ConfigureAwait(false);
        }
    }

    private async Task LoadCredentialAndProbeAsync(
        string providerId,
        string preferredModel,
        long generation,
        CancellationToken cancellationToken)
    {
        string? credential = null;
        var sourceLabel = "No credential required";
        var retention = ActiveCredentialRetention.None;
        if (HostedProviderIds.Contains(providerId))
        {
            credential = await _credentialStore.ReadAsync(providerId, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(credential))
            {
                sourceLabel = "Remembered securely on this PC";
                retention = ActiveCredentialRetention.ExternalSource;
            }
            else
            {
                (credential, sourceLabel) = EnvironmentCredential(providerId);
                if (!string.IsNullOrWhiteSpace(credential))
                {
                    retention = ActiveCredentialRetention.ExternalSource;
                }
            }
        }
        if (!IsCurrentOperation(providerId, generation)) return;
        var settings = SettingsWithCredential(providerId, credential);
        if (!TrySetActiveProviderSettings(providerId, generation, settings, retention)) return;
        await PublishOnUiAsync(() =>
        {
            if (IsCurrentOperation(providerId, generation)) CredentialSourceLabel = sourceLabel;
        }).ConfigureAwait(false);
        await ProbeAndPublishAsync(providerId, preferredModel, settings, generation, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ProbeAndPublishAsync(
        string providerId,
        string preferredModel,
        RekallAgeLanguageModelProviderSettings settings,
        long generation,
        CancellationToken cancellationToken)
    {
        var result = await _readinessProbe.ProbeAsync(
                new RekallAgeLanguageModelReadinessRequest(providerId, preferredModel, settings),
                cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!providerId.Equals(result.ProviderId, StringComparison.Ordinal)
            || !IsCurrentOperation(providerId, generation)) return;
        var sensitiveValues = SensitiveValues(settings);
        await PublishOnUiAsync(() =>
        {
            if (IsCurrentOperation(providerId, generation)) PublishReadiness(result, sensitiveValues);
        }).ConfigureAwait(false);
    }

    private Task ResolveCredentialAndProbeAsync(
        string providerId,
        string preferredModel,
        RekallAgeLanguageModelProviderSettings settings,
        long generation,
        CancellationToken cancellationToken)
    {
        if (HostedProviderIds.Contains(providerId) && !HasCredential(providerId, settings))
        {
            return LoadCredentialAndProbeAsync(
                providerId,
                preferredModel,
                generation,
                cancellationToken);
        }

        var retention = HasCredential(providerId, settings)
            ? ActiveCredentialRetention.AppliedSession
            : ActiveCredentialRetention.None;
        return TrySetActiveProviderSettings(providerId, generation, settings, retention)
            ? ProbeAndPublishAsync(
                providerId,
                preferredModel,
                settings,
                generation,
                cancellationToken)
            : Task.CompletedTask;
    }

    private Task RetryAsync()
    {
        var providerId = SelectedProviderId;
        var settings = RebuildActiveProviderSettings();
        _activeProviderSettings = settings;
        ClearReadiness();
        return QueueOperationAsync((generation, cancellationToken) =>
            ResolveCredentialAndProbeAsync(
                providerId,
                SelectedModelId,
                settings,
                generation,
                cancellationToken));
    }

    private Task MoveNextAsync()
    {
        if (CurrentStep < RekallAgeStudioLanguageModelSetupStep.Summary) CurrentStep++;
        return Task.CompletedTask;
    }

    private Task MoveBackAsync()
    {
        if (CurrentStep > RekallAgeStudioLanguageModelSetupStep.Welcome) CurrentStep--;
        return Task.CompletedTask;
    }

    private async Task SetUpLaterAsync()
    {
        var setup = BuildSetup(isComplete: false, lastSuccessfulCheckUtc: null);
        try
        {
            await _setupStore.SaveAsync(setup, _lifecycleCancellation.Token).ConfigureAwait(false);
            await PublishOnUiAsync(() =>
            {
                _setupStoreAvailable = true;
                CompletedSetup = setup;
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifecycleCancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            await PublishOnUiAsync(() =>
            {
                _setupStoreAvailable = false;
                ErrorSummary = "Language-model setup could not be saved.";
            }).ConfigureAwait(false);
        }
        finally
        {
            await PublishOnUiAsync(() =>
            {
                OnPropertyChanged(nameof(CanFinish));
                RaiseCommands();
            }).ConfigureAwait(false);
        }
    }

    private async Task FinishAsync()
    {
        if (!CanFinish) return;
        var setup = BuildSetup(isComplete: true, lastSuccessfulCheckUtc: _utcNow());
        try
        {
            await _setupStore.SaveAsync(setup, _lifecycleCancellation.Token).ConfigureAwait(false);
            await PublishOnUiAsync(() =>
            {
                _setupStoreAvailable = true;
                CompletedSetup = setup;
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifecycleCancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            await PublishOnUiAsync(() =>
            {
                _setupStoreAvailable = false;
                ErrorSummary = "Language-model setup could not be saved.";
            }).ConfigureAwait(false);
        }
        finally
        {
            await PublishOnUiAsync(() =>
            {
                OnPropertyChanged(nameof(CanFinish));
                RaiseCommands();
            }).ConfigureAwait(false);
        }
    }

    private RekallAgeStudioLanguageModelSetup BuildSetup(
        bool isComplete,
        DateTimeOffset? lastSuccessfulCheckUtc) => new(
        RekallAgeStudioLanguageModelSetup.CurrentVersion,
        isComplete,
        SelectedProviderId,
        SelectedModelId.Length == 0 ? DefaultModel(SelectedProviderId) : SelectedModelId,
        ReasoningEffort,
        OllamaUrl,
        OpenAiUrl,
        KimiUrl,
        lastSuccessfulCheckUtc,
        RekallAgeStudioLanguageModelSetup.CurrentReadinessVersion);

    private void PublishReadiness(
        RekallAgeLanguageModelReadinessResult result,
        IReadOnlyList<string> sensitiveValues)
    {
        ReadinessState = result.State;
        ReadinessCode = Redact(result.Code, sensitiveValues);
        ReadinessSummary = Redact(result.Summary, sensitiveValues);
        var remediationActionId = NormalizeRemediationActionId(result);
        RecommendedActionId = remediationActionId is null
            ? null
            : Redact(remediationActionId, sensitiveValues);
        Replace(
            CompatibleModels,
            result.CompatibleModels
                .Where(model => !ContainsSensitiveValue(model, sensitiveValues))
                .Distinct(StringComparer.Ordinal));
        Replace(
            ReadinessRows,
            result.Checks.Select(check =>
            {
                var safeId = Redact(check.Id, sensitiveValues);
                return new RekallAgeStudioLanguageModelReadinessRow(
                    safeId,
                    Glyph(check.State),
                    CheckLabel(safeId),
                    Redact(check.Summary, sensitiveValues),
                    check.State);
            }));
        if (!CompatibleModels.Contains(SelectedModelId, StringComparer.Ordinal))
        {
            SelectedModelId = string.Empty;
        }
        OnPropertyChanged(nameof(CanFinish));
        RaiseCommands();
    }

    private void PublishCredentialStoreFailure(long generation)
    {
        if (!IsCurrentGeneration(generation)) return;
        ReadinessState = RekallAgeLanguageModelReadinessState.Blocked;
        ReadinessCode = "REKALL_CREDENTIAL_STORE_UNAVAILABLE";
        ReadinessSummary = "The protected credential store is unavailable.";
        CredentialSourceLabel = "Credential store unavailable";
        RecommendedActionId = "enter-api-key";
        Replace(CompatibleModels, []);
        Replace(ReadinessRows,
        [
            new RekallAgeStudioLanguageModelReadinessRow(
                "credential",
                Glyph(RekallAgeLanguageModelReadinessState.Blocked),
                "Credential",
                ReadinessSummary,
                RekallAgeLanguageModelReadinessState.Blocked)
        ]);
    }

    private void PublishOperationFailure(long generation)
    {
        if (!IsCurrentGeneration(generation)) return;
        ReadinessState = RekallAgeLanguageModelReadinessState.Blocked;
        ReadinessCode = "REKALL_ONBOARDING_OPERATION_FAILED";
        ReadinessSummary = "The setup operation could not be completed.";
        RecommendedActionId = "retry";
        Replace(CompatibleModels, []);
        Replace(ReadinessRows,
        [
            new RekallAgeStudioLanguageModelReadinessRow(
                "operation",
                Glyph(RekallAgeLanguageModelReadinessState.Blocked),
                "Setup operation",
                ReadinessSummary,
                RekallAgeLanguageModelReadinessState.Blocked)
        ]);
    }

    private void ClearReadiness()
    {
        ReadinessState = RekallAgeLanguageModelReadinessState.Blocked;
        ReadinessCode = "REKALL_ONBOARDING_CHECKING";
        ReadinessSummary = "Checking the selected language-model provider.";
        RecommendedActionId = null;
        Replace(CompatibleModels, []);
        Replace(ReadinessRows, []);
    }

    private RekallAgeLanguageModelProviderSettings CreateNonSecretSettings() => new()
    {
        OllamaUrl = OllamaUrl,
        OpenAiUrl = OpenAiUrl,
        KimiUrl = KimiUrl
    };

    private RekallAgeLanguageModelProviderSettings RebuildActiveProviderSettings() => new()
    {
        OllamaUrl = OllamaUrl,
        OpenAiUrl = OpenAiUrl,
        OpenAiApiKey = SelectedProviderId == "openai"
            && _activeCredentialRetention == ActiveCredentialRetention.AppliedSession
                ? _activeProviderSettings.OpenAiApiKey
                : null,
        KimiUrl = KimiUrl,
        KimiApiKey = SelectedProviderId == "kimi"
            && _activeCredentialRetention == ActiveCredentialRetention.AppliedSession
                ? _activeProviderSettings.KimiApiKey
                : null
    };

    private bool TrySetActiveProviderSettings(
        string providerId,
        long generation,
        RekallAgeLanguageModelProviderSettings settings,
        ActiveCredentialRetention retention)
    {
        lock (_operationSync)
        {
            if (_disposed
                || generation != _operationGeneration
                || !providerId.Equals(SelectedProviderId, StringComparison.Ordinal)) return false;
            _activeProviderSettings = settings;
            _activeCredentialRetention = retention;
            return true;
        }
    }

    private RekallAgeLanguageModelProviderSettings SettingsWithCredential(string providerId, string? credential) => new()
    {
        OllamaUrl = OllamaUrl,
        OpenAiUrl = OpenAiUrl,
        OpenAiApiKey = providerId == "openai" ? credential : null,
        KimiUrl = KimiUrl,
        KimiApiKey = providerId == "kimi" ? credential : null
    };

    private static bool HasCredential(
        string providerId,
        RekallAgeLanguageModelProviderSettings settings) =>
        !string.IsNullOrWhiteSpace(providerId == "openai"
            ? settings.OpenAiApiKey
            : settings.KimiApiKey);

    private (string? Credential, string SourceLabel) EnvironmentCredential(string providerId)
    {
        if (providerId == "openai")
        {
            var key = _environment.GetValue("OPENAI_API_KEY");
            return string.IsNullOrWhiteSpace(key)
                ? (null, "No credential configured")
                : (key, "Environment variable OPENAI_API_KEY");
        }

        var kimiKey = _environment.GetValue("KIMI_API_KEY");
        if (!string.IsNullOrWhiteSpace(kimiKey)) return (kimiKey, "Environment variable KIMI_API_KEY");
        var moonshotKey = _environment.GetValue("MOONSHOT_API_KEY");
        return string.IsNullOrWhiteSpace(moonshotKey)
            ? (null, "No credential configured")
            : (moonshotKey, "Environment variable MOONSHOT_API_KEY");
    }

    private void SetEndpoint(ref string? field, string? value, [CallerMemberName] string? propertyName = null)
    {
        if (!Set(ref field, value, propertyName)) return;
        if (_disposed) return;
        var providerId = SelectedProviderId;
        var settings = RebuildActiveProviderSettings();
        _activeProviderSettings = settings;
        if (HostedProviderIds.Contains(providerId) && !HasCredential(providerId, settings))
        {
            CredentialSourceLabel = "Checking credential source";
        }
        ClearReadiness();
        _ = QueueOperationAsync((generation, cancellationToken) =>
            ResolveCredentialAndProbeAsync(
                providerId,
                SelectedModelId,
                settings,
                generation,
                cancellationToken));
    }

    private bool IsCurrentOperation(string providerId, long generation) =>
        providerId.Equals(SelectedProviderId, StringComparison.Ordinal) && IsCurrentGeneration(generation);

    private bool IsCurrentGeneration(long generation)
    {
        lock (_operationSync) return !_disposed && generation == _operationGeneration;
    }

    private RekallAgeAsyncCommand CreateRemediationCommand(string actionId) => CreateCommand(
        () => ExecuteRemediationAsync(actionId),
        () => !_disposed && string.Equals(RecommendedActionId, actionId, StringComparison.Ordinal));

    private static RekallAgeAsyncCommand CreateCommand(Func<Task> execute, Func<bool> canExecute) =>
        new(execute, canExecute, _ => { });

    private void RaiseCommands()
    {
        (NextCommand as RekallAgeAsyncCommand)?.RaiseCanExecuteChanged();
        (BackCommand as RekallAgeAsyncCommand)?.RaiseCanExecuteChanged();
        (RetryCommand as RekallAgeAsyncCommand)?.RaiseCanExecuteChanged();
        (SetUpLaterCommand as RekallAgeAsyncCommand)?.RaiseCanExecuteChanged();
        (FinishCommand as RekallAgeAsyncCommand)?.RaiseCanExecuteChanged();
        OpenOllamaDownloadCommand?.RaiseCanExecuteChanged();
        StartOllamaCommand?.RaiseCanExecuteChanged();
        PullRecommendedOllamaModelCommand?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanFinish));
    }

    private void OnProviderSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedProviderId));
        OnPropertyChanged(nameof(SelectedModelId));
        OnPropertyChanged(nameof(IsOllamaSelected));
        OnPropertyChanged(nameof(IsGgufSelected));
        OnPropertyChanged(nameof(IsKimiSelected));
        OnPropertyChanged(nameof(IsOpenAiSelected));
        OnPropertyChanged(nameof(IsCodexSelected));
        RaiseCommands();
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private Task PublishOnUiAsync(Action publish)
    {
        ArgumentNullException.ThrowIfNull(publish);
        if (_synchronizationContext is null
            || ReferenceEquals(SynchronizationContext.Current, _synchronizationContext))
        {
            publish();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _synchronizationContext.Post(static state =>
        {
            var (action, signal) = ((Action, TaskCompletionSource))state!;
            try
            {
                action();
                signal.SetResult();
            }
            catch (Exception exception)
            {
                signal.SetException(exception);
            }
        }, (publish, completion));
        return completion.Task;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static string NormalizeProviderId(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        var normalized = providerId.Trim().ToLowerInvariant();
        return ProviderIds.Contains(normalized)
            ? normalized
            : throw new ArgumentException("Unsupported language-model provider.", nameof(providerId));
    }

    private static string DefaultModel(string providerId) => providerId switch
    {
        "ollama" or "gguf" => "qwen3.8:27b",
        "kimi" => "kimi-k3",
        "openai" => "gpt-5.6-sol",
        "codex" => RekallAgeCodexProjectAgentRunner.RequiredModel,
        _ => string.Empty
    };

    private static string? NormalizeRemediationActionId(RekallAgeLanguageModelReadinessResult result) =>
        result.ProviderId is "ollama" or "gguf"
            && result.Code is "REKALL_ONBOARDING_NO_MODELS" or "REKALL_ONBOARDING_NO_TOOL_MODEL"
            && result.RecommendedActionId == "download-default-model"
                ? PullRecommendedOllamaModelActionId
                : result.RecommendedActionId;

    private static string? NormalizeEndpoint(string? endpoint) =>
        string.IsNullOrWhiteSpace(endpoint) ? null : endpoint.Trim();

    private static string Glyph(RekallAgeLanguageModelReadinessState state) => state switch
    {
        RekallAgeLanguageModelReadinessState.Ready => "✓",
        RekallAgeLanguageModelReadinessState.Warning => "!",
        _ => "×"
    };

    private static string CheckLabel(string id) => id.Replace('-', ' ');

    private static IReadOnlyList<string> SensitiveValues(RekallAgeLanguageModelProviderSettings settings) =>
        new[] { settings.OpenAiApiKey, settings.KimiApiKey }
            .Where(value => !string.IsNullOrEmpty(value))
            .Select(value => value!)
            .ToArray();

    private static bool ContainsSensitiveValue(string value, IReadOnlyList<string> sensitiveValues) =>
        sensitiveValues.Any(secret => value.Contains(secret, StringComparison.Ordinal));

    private static string Redact(string value, IReadOnlyList<string> sensitiveValues)
    {
        foreach (var secret in sensitiveValues)
        {
            value = value.Replace(secret, "[redacted]", StringComparison.Ordinal);
        }
        return value;
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }

    private sealed class NoOpActions : IRekallAgeStudioLanguageModelSetupActions
    {
        public static NoOpActions Instance { get; } = new();

        public ValueTask ExecuteAsync(string actionId, string providerId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SystemEnvironment : IRekallAgeEnvironmentValueSource
    {
        public static SystemEnvironment Instance { get; } = new();
        public string? GetValue(string name) => Environment.GetEnvironmentVariable(name);
    }

    private sealed class DisabledCommand : ICommand
    {
        public static DisabledCommand Instance { get; } = new();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => false;
        public void Execute(object? parameter) { }
    }
}
