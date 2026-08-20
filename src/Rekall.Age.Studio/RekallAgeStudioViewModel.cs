using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Rekall.Age.Agent.LanguageModels;
using Rekall.Age.Editor;
using Rekall.Age.Editor.Contracts;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.Workflows;
using Rekall.Age.Workflows.Commands;

namespace Rekall.Age.Studio;

public sealed class RekallAgeStudioViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly RekallAgeWorkbenchSession _session;
    private readonly HttpClient? _ollamaHttpClient;
    private readonly RekallAgeProjectAgentSession _agentSession;
    private readonly RekallAgeAsyncCommand _openCommand;
    private readonly RekallAgeAsyncCommand _createCommand;
    private readonly RekallAgeAsyncCommand _addEntityCommand;
    private readonly RekallAgeAsyncCommand _addComponentCommand;
    private readonly RekallAgeAsyncCommand _removeComponentCommand;
    private readonly RekallAgeAsyncCommand _setPropertyCommand;
    private readonly RekallAgeAsyncCommand _removePropertyCommand;
    private readonly RekallAgeAsyncCommand _validateCommand;
    private readonly RekallAgeAsyncCommand _captureCommand;
    private readonly RekallAgeAsyncCommand _playCommand;
    private readonly RekallAgeAsyncCommand _stopCommand;
    private readonly RekallAgeAsyncCommand _switchSceneCommand;
    private readonly RekallAgeAsyncCommand _packageCommand;
    private readonly RekallAgeAsyncCommand _auditPackageCommand;
    private readonly RekallAgeAsyncCommand _undoCommand;
    private readonly RekallAgeAsyncCommand _redoCommand;
    private readonly RekallAgeAsyncCommand _discoverModelsCommand;
    private readonly RekallAgeAsyncCommand _runAgentCommand;
    private readonly RekallAgeAsyncCommand _cancelAgentCommand;
    private Process? _player;
    private CancellationTokenSource? _agentCancellation;
    private bool _isBusy;
    private bool _isAgentRunning;
    private string _projectPathInput = string.Empty;
    private string _projectNameInput = "New Rekall Game";
    private string _sceneNameInput = "Main";
    private string _componentTypeInput = "Rekall.Transform";
    private string _propertyNameInput = "position";
    private string _propertyValueInput = "[0, 0, 0]";
    private string _propertySchemaHelp = "Select a registered property to see its type and constraints.";
    private string _selectedOllamaModel = "qwen3.5:35b";
    private string _agentTaskInput = "Describe the game feature you want the AI agent to create or revise.";
    private string? _lastPackagePath;
    private string _statusText = "Create or open a Rekall AGE project to begin.";
    private string _viewportTitle = "Viewport";
    private string _viewportSummary = "No rendered frame yet.";
    private BitmapImage? _viewportImage;
    private RekallAgeWorkbenchModel? _currentModel;

    public RekallAgeStudioViewModel()
        : this(new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()), null)
    {
    }

    internal RekallAgeStudioViewModel(RekallAgeWorkbenchSession session)
        : this(session, null)
    {
    }

    internal RekallAgeStudioViewModel(
        RekallAgeWorkbenchSession session,
        IRekallAgeLanguageModelClient? languageModelClient)
    {
        _session = session;
        if (languageModelClient is null)
        {
            _ollamaHttpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            var configuredOllamaUrl = Environment.GetEnvironmentVariable("REKALL_AGE_OLLAMA_URL");
            languageModelClient = new RekallAgeOllamaLanguageModelClient(
                _ollamaHttpClient,
                new Uri(string.IsNullOrWhiteSpace(configuredOllamaUrl) ? "http://127.0.0.1:11434" : configuredOllamaUrl));
        }
        _agentSession = new RekallAgeProjectAgentSession(languageModelClient, RekallAgeDefaultCommandRegistry.Create());
        _openCommand = CreateAsyncCommand(OpenFromInputsAsync, CanOpenOrCreate);
        _createCommand = CreateAsyncCommand(CreateFromInputsAsync, CanOpenOrCreate);
        _addEntityCommand = CreateAsyncCommand(AddEntityAsync, HasOpenProject);
        _addComponentCommand = CreateAsyncCommand(AddComponentAsync, CanEditComponent);
        _removeComponentCommand = CreateAsyncCommand(RemoveComponentAsync, CanEditComponent);
        _setPropertyCommand = CreateAsyncCommand(SetPropertyAsync, CanEditProperty);
        _removePropertyCommand = CreateAsyncCommand(RemovePropertyAsync, CanEditProperty);
        _validateCommand = CreateAsyncCommand(ValidateAsync, HasOpenProject);
        _captureCommand = CreateAsyncCommand(CaptureAsync, HasOpenProject);
        _playCommand = CreateAsyncCommand(PlayAsync, () => HasOpenProject() && !IsPlaying);
        _stopCommand = CreateAsyncCommand(StopAsync, () => IsPlaying);
        _switchSceneCommand = CreateAsyncCommand(SwitchSceneAsync, CanSwitchScene);
        _packageCommand = CreateAsyncCommand(PackageAsync, HasOpenProject);
        _auditPackageCommand = CreateAsyncCommand(AuditPackageAsync, CanAuditPackage);
        _undoCommand = CreateAsyncCommand(UndoAsync, () => HasOpenProject() && _session.CanUndo);
        _redoCommand = CreateAsyncCommand(RedoAsync, () => HasOpenProject() && _session.CanRedo);
        _discoverModelsCommand = CreateAsyncCommand(DiscoverModelsAsync, () => !IsBusy && !IsAgentRunning);
        _runAgentCommand = CreateAsyncCommand(RunAgentAsync, CanRunAgent);
        _cancelAgentCommand = CreateAsyncCommand(CancelAgentAsync, () => IsAgentRunning);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<RekallAgeSceneEntityNode> EntityNodes { get; } = [];
    public ObservableCollection<string> SceneNames { get; } = [];
    public ObservableCollection<string> InspectorLines { get; } = [];
    public ObservableCollection<string> AssetLines { get; } = [];
    public ObservableCollection<string> ValidationLines { get; } = [];
    public ObservableCollection<string> TransactionLines { get; } = [];
    public ObservableCollection<string> ImportLines { get; } = [];
    public ObservableCollection<string> SceneSummaryLines { get; } = [];
    public ObservableCollection<string> ActionLines { get; } = [];
    public ObservableCollection<string> RuntimeObservationLines { get; } = [];
    public ObservableCollection<string> OllamaModels { get; } = [];
    public ObservableCollection<string> AgentLines { get; } = [];
    public ObservableCollection<RekallAgeInspectorComponentSchemaModel> ComponentSchemas { get; } = [];
    public ObservableCollection<RekallAgeInspectorPropertySchemaModel> PropertySchemas { get; } = [];
    public ObservableCollection<string> PropertyValueChoices { get; } = [];

    public ICommand OpenCommand => _openCommand;
    public ICommand CreateCommand => _createCommand;
    public ICommand AddEntityCommand => _addEntityCommand;
    public ICommand AddComponentCommand => _addComponentCommand;
    public ICommand RemoveComponentCommand => _removeComponentCommand;
    public ICommand SetPropertyCommand => _setPropertyCommand;
    public ICommand RemovePropertyCommand => _removePropertyCommand;
    public ICommand ValidateCommand => _validateCommand;
    public ICommand CaptureCommand => _captureCommand;
    public ICommand PlayCommand => _playCommand;
    public ICommand StopCommand => _stopCommand;
    public ICommand SwitchSceneCommand => _switchSceneCommand;
    public ICommand PackageCommand => _packageCommand;
    public ICommand AuditPackageCommand => _auditPackageCommand;
    public ICommand UndoCommand => _undoCommand;
    public ICommand RedoCommand => _redoCommand;
    public ICommand DiscoverModelsCommand => _discoverModelsCommand;
    public ICommand RunAgentCommand => _runAgentCommand;
    public ICommand CancelAgentCommand => _cancelAgentCommand;

    public string ProjectPathInput
    {
        get => _projectPathInput;
        set
        {
            if (Set(ref _projectPathInput, value)) RefreshCommands();
        }
    }

    public string ProjectNameInput
    {
        get => _projectNameInput;
        set => Set(ref _projectNameInput, value);
    }

    public string SceneNameInput
    {
        get => _sceneNameInput;
        set
        {
            if (Set(ref _sceneNameInput, value)) RefreshCommands();
        }
    }

    public string ComponentTypeInput
    {
        get => _componentTypeInput;
        set
        {
            if (Set(ref _componentTypeInput, value))
            {
                RefreshPropertySchemas();
                RefreshCommands();
            }
        }
    }

    public string PropertyNameInput
    {
        get => _propertyNameInput;
        set
        {
            if (Set(ref _propertyNameInput, value))
            {
                RefreshSelectedPropertySchema();
                RefreshCommands();
            }
        }
    }

    public string PropertyValueInput
    {
        get => _propertyValueInput;
        set => Set(ref _propertyValueInput, value);
    }

    public RekallAgeInspectorPropertySchemaModel? SelectedPropertySchema => PropertySchemas.FirstOrDefault(
        property => property.Name.Equals(PropertyNameInput, StringComparison.OrdinalIgnoreCase));

    public string PropertySchemaHelp
    {
        get => _propertySchemaHelp;
        private set => Set(ref _propertySchemaHelp, value);
    }

    public string SelectedOllamaModel
    {
        get => _selectedOllamaModel;
        set
        {
            if (Set(ref _selectedOllamaModel, value)) RefreshCommands();
        }
    }

    public string AgentTaskInput
    {
        get => _agentTaskInput;
        set
        {
            if (Set(ref _agentTaskInput, value)) RefreshCommands();
        }
    }

    public string? LastPackagePath
    {
        get => _lastPackagePath;
        private set
        {
            if (Set(ref _lastPackagePath, value)) RefreshCommands();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public string ViewportTitle
    {
        get => _viewportTitle;
        private set => Set(ref _viewportTitle, value);
    }

    public string ViewportSummary
    {
        get => _viewportSummary;
        private set => Set(ref _viewportSummary, value);
    }

    public BitmapImage? ViewportImage
    {
        get => _viewportImage;
        private set => Set(ref _viewportImage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value)) RefreshCommands();
        }
    }

    public bool IsPlaying => _player is { HasExited: false };

    public bool IsAgentRunning
    {
        get => _isAgentRunning;
        private set
        {
            if (Set(ref _isAgentRunning, value)) RefreshCommands();
        }
    }

    public async Task InitializeAsync(string? projectRoot, string sceneName)
    {
        if (string.IsNullOrWhiteSpace(projectRoot)) return;
        ProjectPathInput = projectRoot;
        SceneNameInput = sceneName;
        await RunAsync(() => _session.OpenAsync(projectRoot, sceneName, CancellationToken.None).AsTask(), captureAfter: true);
    }

    public async Task SelectEntityAsync(RekallAgeSceneEntityNode entity)
    {
        if (IsBusy) return;
        await RunAsync(() => _session.SelectEntityAsync(entity.EntityId, CancellationToken.None).AsTask());
    }

    public async ValueTask DisposeAsync()
    {
        _agentCancellation?.Cancel();
        await StopAsync();
        _agentCancellation?.Dispose();
        _ollamaHttpClient?.Dispose();
    }

    private bool CanOpenOrCreate() => !IsBusy && !string.IsNullOrWhiteSpace(ProjectPathInput);
    private bool HasOpenProject() => !IsBusy && _session.Model is not null;
    private bool CanEditComponent() => HasOpenProject()
        && _session.SelectedEntityId is not null
        && !string.IsNullOrWhiteSpace(ComponentTypeInput);
    private bool CanEditProperty() => CanEditComponent() && !string.IsNullOrWhiteSpace(PropertyNameInput);
    private bool CanRunAgent() => HasOpenProject()
        && !IsAgentRunning
        && !string.IsNullOrWhiteSpace(SelectedOllamaModel)
        && !string.IsNullOrWhiteSpace(AgentTaskInput);
    private bool CanSwitchScene() => HasOpenProject()
        && !string.IsNullOrWhiteSpace(SceneNameInput)
        && !_session.SceneName!.Equals(SceneNameInput.Trim(), StringComparison.Ordinal);
    private bool CanAuditPackage() => HasOpenProject()
        && LastPackagePath is not null
        && (File.Exists(LastPackagePath) || Directory.Exists(LastPackagePath));

    private Task OpenFromInputsAsync() => RunAsync(
        () => _session.OpenAsync(ProjectPathInput, NormalizeSceneName(), CancellationToken.None).AsTask(),
        captureAfter: true);

    private Task CreateFromInputsAsync() => RunAsync(
        () => _session.CreateProjectAsync(
            ProjectPathInput,
            string.IsNullOrWhiteSpace(ProjectNameInput) ? "Rekall Game" : ProjectNameInput.Trim(),
            NormalizeSceneName(),
            ["world", "rendering2d", "rendering3d", "input", "audio", "ui", "animation", "physics", "modules"],
            ["world", "rendering2d", "rendering3d", "input", "audio", "ui", "animation", "physics"],
            "studio",
            CancellationToken.None).AsTask());

    private Task SwitchSceneAsync() => RunAsync(
        () => _session.OpenSceneAsync(NormalizeSceneName(), CancellationToken.None).AsTask(),
        captureAfter: true);

    private Task UndoAsync() => RunAsync(
        () => _session.UndoAsync("studio", CancellationToken.None).AsTask(),
        captureAfter: true);

    private Task RedoAsync() => RunAsync(
        () => _session.RedoAsync("studio", CancellationToken.None).AsTask(),
        captureAfter: true);

    private Task AddEntityAsync()
    {
        var name = $"Entity {(_session.Model?.SceneSummary.EntityCount ?? 0) + 1}";
        return RunAsync(() => _session.ExecuteAsync(
            "rekall.entity.create",
            JsonSerializer.Serialize(new
            {
                projectRoot = _session.ProjectRoot,
                sceneName = _session.SceneName,
                name,
                tags = Array.Empty<string>()
            }),
            $"Create {name}",
            "studio",
            CancellationToken.None).AsTask(), captureAfter: true);
    }

    private Task AddComponentAsync() => ExecuteComponentCommandAsync(
        "rekall.component.add",
        new
        {
            projectRoot = _session.ProjectRoot,
            sceneName = _session.SceneName,
            entityId = _session.SelectedEntityId,
            componentType = ComponentTypeInput.Trim(),
            properties = new JsonObject()
        },
        $"Add {ComponentTypeInput.Trim()}");

    private Task RemoveComponentAsync() => ExecuteComponentCommandAsync(
        "rekall.component.remove",
        new
        {
            projectRoot = _session.ProjectRoot,
            sceneName = _session.SceneName,
            entityId = _session.SelectedEntityId,
            componentType = ComponentTypeInput.Trim()
        },
        $"Remove {ComponentTypeInput.Trim()}");

    private Task SetPropertyAsync()
    {
        var value = ParsePropertyValue(PropertyValueInput);
        return ExecuteComponentCommandAsync(
            "rekall.component.set_property",
            new
            {
                projectRoot = _session.ProjectRoot,
                sceneName = _session.SceneName,
                entityId = _session.SelectedEntityId,
                componentType = ComponentTypeInput.Trim(),
                propertyName = PropertyNameInput.Trim(),
                value
            },
            $"Set {ComponentTypeInput.Trim()}.{PropertyNameInput.Trim()}");
    }

    private Task RemovePropertyAsync() => ExecuteComponentCommandAsync(
        "rekall.component.remove_property",
        new
        {
            projectRoot = _session.ProjectRoot,
            sceneName = _session.SceneName,
            entityId = _session.SelectedEntityId,
            componentType = ComponentTypeInput.Trim(),
            propertyName = PropertyNameInput.Trim()
        },
        $"Remove {ComponentTypeInput.Trim()}.{PropertyNameInput.Trim()}");

    private Task ExecuteComponentCommandAsync(string commandName, object arguments, string transactionName) =>
        RunAsync(() => _session.ExecuteAsync(
            commandName,
            JsonSerializer.Serialize(arguments),
            transactionName,
            "studio",
            CancellationToken.None).AsTask(), captureAfter: true);

    private Task ValidateAsync() => RunAsync(() => _session.ExecuteAsync(
        "rekall.validation.scene",
        JsonSerializer.Serialize(new { projectRoot = _session.ProjectRoot, sceneName = _session.SceneName }),
        "Validate Scene",
        "studio",
        CancellationToken.None).AsTask());

    private Task PackageAsync() => RunAsync(PackageOperationAsync);

    private async Task<RekallAgeWorkbenchOperationResult> PackageOperationAsync()
    {
        var result = await _session.ExecuteAsync(
            "rekall.workflow.package_playable_game",
            JsonSerializer.Serialize(new
            {
                projectRoot = _session.ProjectRoot,
                sceneName = _session.SceneName,
                outputDirectory = Path.Combine(_session.ProjectRoot!, "Builds", "StudioPackage"),
                frames = 2,
                graphics = false
            }),
            "Package playable game",
            "studio",
            CancellationToken.None);
        if (result.Ok && result.Value is PackagePlayableGameResult package && package.Ready)
        {
            LastPackagePath = package.ArchivePath;
            AppendAgentLine($"package: {package.ArchivePath}");
        }
        return result;
    }

    private Task AuditPackageAsync() => RunAsync(() => _session.ExecuteAsync(
        "rekall.workflow.audit_playable_package",
        JsonSerializer.Serialize(new
        {
            packagePath = LastPackagePath,
            outputDirectory = Path.Combine(_session.ProjectRoot!, "Builds", "StudioPackageAudit"),
            frames = 1,
            frameIndex = 1,
            width = 960,
            height = 540
        }),
        "Audit playable package",
        "studio",
        CancellationToken.None).AsTask());

    private async Task DiscoverModelsAsync()
    {
        IsBusy = true;
        try
        {
            var models = await _agentSession.ListModelsAsync(CancellationToken.None);
            Replace(OllamaModels, models.Select(model => model.Id));
            if (OllamaModels.Contains("qwen3.5:35b"))
            {
                SelectedOllamaModel = "qwen3.5:35b";
            }
            else if (OllamaModels.Count > 0 && !OllamaModels.Contains(SelectedOllamaModel))
            {
                SelectedOllamaModel = OllamaModels[0];
            }
            StatusText = OllamaModels.Count == 0
                ? "Ollama is reachable but no local models are installed."
                : $"Found {OllamaModels.Count} local Ollama model{(OllamaModels.Count == 1 ? string.Empty : "s")}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunAgentAsync()
    {
        if (_session.ProjectRoot is null || _session.SceneName is null) return;
        _agentCancellation?.Dispose();
        _agentCancellation = new CancellationTokenSource();
        var cancellationToken = _agentCancellation.Token;
        IsAgentRunning = true;
        IsBusy = true;
        AgentLines.Clear();
        AppendAgentLine($"model: {SelectedOllamaModel}");
        AppendAgentLine($"task: {AgentTaskInput.Trim()}");
        try
        {
            var progress = new Progress<RekallAgeLanguageModelAgentProgress>(ReportAgentProgress);
            var result = await _agentSession.RunAsync(
                new RekallAgeProjectAgentSessionRequest(
                    _session.ProjectRoot,
                    _session.SceneName,
                    SelectedOllamaModel,
                    AgentTaskInput)
                {
                    MaxTurns = 24,
                    RequireCompletionAudit = true
                },
                progress,
                cancellationToken);
            AppendAgentLine(result.Summary);
            if (!string.IsNullOrWhiteSpace(result.AgentResult.FinalContent))
            {
                AppendAgentLine($"final: {Bound(result.AgentResult.FinalContent, 2_000)}");
            }

            var reload = await _session.ReloadAsync(cancellationToken);
            if (reload.Ok && _session.Model is not null) ApplyModel(_session.Model);
            var validation = await _session.ExecuteAsync(
                "rekall.validation.scene",
                JsonSerializer.Serialize(new { projectRoot = _session.ProjectRoot, sceneName = _session.SceneName }),
                "Validate AI authoring",
                "studio-agent",
                cancellationToken);
            if (_session.Model is not null) ApplyModel(_session.Model);
            var capture = await CaptureOperationAsync();
            StatusText = result.Succeeded && validation.Ok && capture.Ok
                ? result.Summary
                : $"{result.Summary} Review Validation and AI Agent output.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = "AI authoring cancelled.";
            AppendAgentLine("cancelled by user");
        }
        finally
        {
            _agentCancellation?.Dispose();
            _agentCancellation = null;
            IsAgentRunning = false;
            IsBusy = false;
        }
    }

    private Task CancelAgentAsync()
    {
        _agentCancellation?.Cancel();
        StatusText = "Cancelling AI authoring…";
        return Task.CompletedTask;
    }

    private void ReportAgentProgress(RekallAgeLanguageModelAgentProgress progress)
    {
        var suffix = progress.ToolExecution is null
            ? string.Empty
            : $" #{progress.ToolExecution.Sequence} {(progress.ToolExecution.Succeeded ? "ok" : "failed")}";
        AppendAgentLine($"turn {progress.Turn}: {progress.Phase}{suffix} — {progress.Message}");
        if (progress.ToolExecution is { Succeeded: false } failed)
        {
            AppendAgentLine($"tool failure: {Bound(failed.ResultPreview, 1_200)}");
        }
        StatusText = progress.Message;
    }

    private void AppendAgentLine(string value)
    {
        AgentLines.Add(Bound(value, 2_400));
        while (AgentLines.Count > 200) AgentLines.RemoveAt(0);
    }

    private static string Bound(string value, int maxCharacters) =>
        value.Length <= maxCharacters ? value : value[..maxCharacters] + "…";

    private Task CaptureAsync() => RunAsync(CaptureOperationAsync);

    private async Task<RekallAgeWorkbenchOperationResult> CaptureOperationAsync()
    {
        var result = await _session.ExecuteAsync(
            "rekall.render.capture_runtime_viewport",
            JsonSerializer.Serialize(new
            {
                projectRoot = _session.ProjectRoot,
                sceneName = _session.SceneName,
                frames = 1,
                outputDirectory = Path.Combine(_session.ProjectRoot!, "Artifacts", "Studio", "Viewport"),
                width = 960,
                height = 540,
                debugOverlay = true,
                backendId = "software"
            }),
            "Capture Viewport",
            "studio",
            CancellationToken.None);
        if (result.Ok && result.Value is CaptureRuntimeViewportResult capture && capture.Captured)
        {
            ViewportImage = LoadBitmap(capture.ScreenshotPath);
            ViewportSummary = $"{capture.Width}×{capture.Height} · frame {capture.FrameIndex} · {capture.RenderableCount} renderables";
        }
        return result;
    }

    private async Task PlayAsync()
    {
        if (_session.ProjectRoot is null || _session.SceneName is null) return;
        var executable = ResolvePlayerExecutable();
        if (executable is null)
        {
            StatusText = "Player executable was not found. Build or install Rekall.Age.Player.Windows.";
            return;
        }

        var startInfo = new ProcessStartInfo(executable) { UseShellExecute = false };
        startInfo.ArgumentList.Add(_session.ProjectRoot);
        startInfo.ArgumentList.Add(_session.SceneName);
        startInfo.ArgumentList.Add("--graphics");
        startInfo.ArgumentList.Add("--backend");
        startInfo.ArgumentList.Add("vulkan");
        _player = Process.Start(startInfo);
        StatusText = _player is null ? "Player could not be started." : $"Playing {_session.SceneName}.";
        OnPropertyChanged(nameof(IsPlaying));
        RefreshCommands();
        await Task.CompletedTask;
    }

    private async Task StopAsync()
    {
        if (_player is null) return;
        try
        {
            if (!_player.HasExited)
            {
                _player.Kill(entireProcessTree: true);
                await _player.WaitForExitAsync();
            }
        }
        finally
        {
            _player.Dispose();
            _player = null;
            StatusText = "Play mode stopped.";
            OnPropertyChanged(nameof(IsPlaying));
            RefreshCommands();
        }
    }

    private async Task RunAsync(Func<Task<RekallAgeWorkbenchOperationResult>> operation, bool captureAfter = false)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var result = await operation();
            StatusText = result.Summary;
            if (result.Ok && _session.Model is not null)
            {
                ApplyModel(_session.Model);
                if (captureAfter)
                {
                    var capture = await CaptureOperationAsync();
                    StatusText = capture.Ok ? result.Summary : capture.Summary;
                    ApplyModel(_session.Model!);
                }
            }
            else if (!result.Ok)
            {
                Replace(ValidationLines, result.Errors.Select(error => $"error: {error.Code} - {error.Message}"));
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            StatusText = ex.Message;
            Replace(ValidationLines, [$"error: REKALL_STUDIO_OPERATION_FAILED - {ex.Message}"]);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyModel(RekallAgeWorkbenchModel model)
    {
        _currentModel = model;
        SceneNameInput = model.Scene.Name;
        Replace(EntityNodes, model.Scene.RootEntities);
        Replace(SceneNames, model.Project.Scenes.Select(scene => scene.Name));
        Replace(InspectorLines, model.Inspector.Components.SelectMany(component =>
            new[] { $"{component.DisplayName} ({component.Type})" }.Concat(component.Properties
                .Where(property => property.IsDefined)
                .Select(property => $"  {property.Name}: {property.Value}"))));
        Replace(ComponentSchemas, model.Inspector.AvailableComponents);
        if (!ComponentSchemas.Any(component => component.Type.Equals(ComponentTypeInput, StringComparison.Ordinal)))
        {
            ComponentTypeInput = model.Inspector.Components.FirstOrDefault()?.Type
                ?? ComponentSchemas.FirstOrDefault()?.Type
                ?? ComponentTypeInput;
        }
        RefreshPropertySchemas();
        Replace(AssetLines, model.Assets.Assets.Select(asset => $"{asset.Kind}: {asset.DisplayName} ({asset.AssetId})"));
        Replace(ValidationLines, model.Diagnostics.Issues.Select(issue => $"{issue.Severity}: {issue.Code} - {issue.Message}"));
        Replace(TransactionLines, model.Transactions.Transactions.Select(transaction => $"{transaction.Name}: {transaction.ChangedResources.Count} changes"));
        Replace(ImportLines, model.ImportQueue.Jobs.Select(job => $"{job.Status}: {job.SourcePath}"));
        Replace(SceneSummaryLines, BuildSceneSummaryLines(model.SceneSummary));
        Replace(ActionLines, model.Actions.Actions.Select(action => $"{action.Category}: {action.Label} ({action.Tool})"));
        Replace(RuntimeObservationLines, model.Runtime.Observations.Select(observation =>
            $"{observation.Severity}: {observation.Code} - {observation.Message}"));
        ViewportTitle = $"{model.Scene.Name} Viewport";
        if (ViewportImage is null)
        {
            ViewportSummary = $"Camera {model.Runtime.ActiveCameraName ?? "none"} · {model.Runtime.RenderableCount} renderables";
        }
    }

    private void RefreshPropertySchemas()
    {
        var component = ComponentSchemas.FirstOrDefault(
            candidate => candidate.Type.Equals(ComponentTypeInput, StringComparison.Ordinal));
        Replace(PropertySchemas, component?.Properties ?? []);
        if (PropertySchemas.Count > 0
            && !PropertySchemas.Any(property => property.Name.Equals(PropertyNameInput, StringComparison.OrdinalIgnoreCase)))
        {
            PropertyNameInput = PropertySchemas[0].Name;
        }
        else
        {
            RefreshSelectedPropertySchema();
        }
    }

    private void RefreshSelectedPropertySchema()
    {
        OnPropertyChanged(nameof(SelectedPropertySchema));
        var schema = SelectedPropertySchema;
        if (schema is null)
        {
            PropertySchemaHelp = "Unregistered property: enter a JSON value. Validation will report unsupported fields.";
            Replace(PropertyValueChoices, []);
            return;
        }

        var range = schema.Minimum is not null || schema.Maximum is not null
            ? $" Range: {schema.Minimum?.ToString() ?? "-∞"} to {schema.Maximum?.ToString() ?? "+∞"}."
            : string.Empty;
        var asset = schema.AssetKind is null ? string.Empty : $" Asset kind: {schema.AssetKind}.";
        var allowed = schema.AllowedValues.Count == 0 ? string.Empty : $" Allowed: {string.Join(", ", schema.AllowedValues)}.";
        PropertySchemaHelp = $"{schema.EditorKind} ({schema.TypeName}).{range}{asset}{allowed} {schema.Description}".Trim();

        IEnumerable<string> choices = schema.AllowedValues;
        if (schema.EditorKind.Equals("boolean", StringComparison.OrdinalIgnoreCase))
        {
            choices = ["true", "false"];
        }
        else if (schema.EditorKind.Equals("assetRef", StringComparison.OrdinalIgnoreCase) && _currentModel is not null)
        {
            choices = _currentModel.Assets.Assets
                .Where(item => schema.AssetKind is null || item.Kind.Equals(schema.AssetKind, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.AssetId);
        }
        Replace(PropertyValueChoices, choices.Distinct(StringComparer.Ordinal));

        var currentProperty = _currentModel?.Inspector.Components
            .FirstOrDefault(component => component.Type.Equals(ComponentTypeInput, StringComparison.Ordinal))?
            .Properties.FirstOrDefault(property => property.Name.Equals(schema.Name, StringComparison.OrdinalIgnoreCase));
        if (currentProperty is { IsDefined: true })
        {
            PropertyValueInput = currentProperty.Value;
        }
    }

    private string NormalizeSceneName() => string.IsNullOrWhiteSpace(SceneNameInput) ? "Main" : SceneNameInput.Trim();

    private static BitmapImage LoadBitmap(string path)
    {
        using var stream = new MemoryStream(File.ReadAllBytes(path));
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static JsonNode? ParsePropertyValue(string text)
    {
        var value = text.Trim();
        if (value.Length == 0) return JsonValue.Create(string.Empty);
        try
        {
            return JsonNode.Parse(value);
        }
        catch (JsonException)
        {
            return JsonValue.Create(text);
        }
    }

    private static string? ResolvePlayerExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Rekall.Age.Player.Windows.exe"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                "Rekall.Age.Player.Windows", "bin", "Debug", "net10.0-windows", "Rekall.Age.Player.Windows.exe"))
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static IEnumerable<string> BuildSceneSummaryLines(RekallAgeWorkbenchSceneSummaryModel summary)
    {
        yield return $"Entities: {summary.EntityCount}";
        yield return $"Components: {summary.ComponentCount}";
        foreach (var component in summary.ComponentTypes) yield return $"{component.Type}: {component.Count}";
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
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

    private void RefreshCommands()
    {
        _openCommand.RaiseCanExecuteChanged();
        _createCommand.RaiseCanExecuteChanged();
        _addEntityCommand.RaiseCanExecuteChanged();
        _addComponentCommand.RaiseCanExecuteChanged();
        _removeComponentCommand.RaiseCanExecuteChanged();
        _setPropertyCommand.RaiseCanExecuteChanged();
        _removePropertyCommand.RaiseCanExecuteChanged();
        _validateCommand.RaiseCanExecuteChanged();
        _captureCommand.RaiseCanExecuteChanged();
        _playCommand.RaiseCanExecuteChanged();
        _stopCommand.RaiseCanExecuteChanged();
        _switchSceneCommand.RaiseCanExecuteChanged();
        _packageCommand.RaiseCanExecuteChanged();
        _auditPackageCommand.RaiseCanExecuteChanged();
        _undoCommand.RaiseCanExecuteChanged();
        _redoCommand.RaiseCanExecuteChanged();
        _discoverModelsCommand.RaiseCanExecuteChanged();
        _runAgentCommand.RaiseCanExecuteChanged();
        _cancelAgentCommand.RaiseCanExecuteChanged();
    }

    private RekallAgeAsyncCommand CreateAsyncCommand(Func<Task> execute, Func<bool> canExecute) =>
        new(execute, canExecute, ReportUnexpectedFailure);

    private void ReportUnexpectedFailure(Exception exception)
    {
        StatusText = "Studio operation failed. See Validation for details.";
        Replace(ValidationLines, [$"error: REKALL_STUDIO_UNEXPECTED_FAILURE - {exception.Message}"]);
        IsBusy = false;
    }
}

internal sealed class RekallAgeAsyncCommand(
    Func<Task> execute,
    Func<bool> canExecute,
    Action<Exception> onError) : ICommand
{
    private bool _executing;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_executing && canExecute();

    public async void Execute(object? parameter) => await ExecuteAsync(parameter);

    internal async Task ExecuteAsync(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _executing = true;
        RaiseCanExecuteChanged();
        try
        {
            await execute();
        }
        catch (Exception ex)
        {
            onError(ex);
        }
        finally
        {
            _executing = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
