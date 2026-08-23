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
using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.Workflows;
using Rekall.Age.Workflows.Commands;

namespace Rekall.Age.Studio;

public enum RekallAgeStudioMode
{
    Edit,
    Simulate,
    Play
}

public sealed class RekallAgeStudioViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly RekallAgeWorkbenchSession _session;
    private readonly HttpClient? _ollamaHttpClient;
    private readonly RekallAgeProjectAgentSession _agentSession;
    private readonly IRekallAgeStudioPreviewSession _previewSession;
    private readonly SemaphoreSlim _modeTransitionGate = new(1, 1);
    private readonly CancellationTokenSource _lifecycleCancellation = new();
    private readonly object _disposeSync = new();
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
    private readonly RekallAgeAsyncCommand _simulateCommand;
    private readonly RekallAgeAsyncCommand _stopCommand;
    private readonly RekallAgeAsyncCommand _switchSceneCommand;
    private readonly RekallAgeAsyncCommand _packageCommand;
    private readonly RekallAgeAsyncCommand _auditPackageCommand;
    private readonly RekallAgeAsyncCommand _undoCommand;
    private readonly RekallAgeAsyncCommand _redoCommand;
    private readonly RekallAgeAsyncCommand _discoverModelsCommand;
    private readonly RekallAgeAsyncCommand _runAgentCommand;
    private readonly RekallAgeAsyncCommand _cancelAgentCommand;
    private readonly RekallAgeStudioModelingSession _modeling = new();
    private readonly RekallAgeStudioMeshViewportRenderer _meshViewportRenderer = new();
    private RekallAgeStudioMeshViewportFrame? _meshViewportFrame;
    private RekallAgeStudioMeshTransformGesture? _meshTransformGesture;
    private readonly RekallAgeAsyncCommand _refreshMeshAssetsCommand;
    private readonly RekallAgeAsyncCommand _openMeshAssetCommand;
    private readonly RekallAgeAsyncCommand _selectMeshElementCommand;
    private readonly RekallAgeAsyncCommand _clearMeshSelectionCommand;
    private readonly RekallAgeAsyncCommand _previewMeshOperationCommand;
    private readonly RekallAgeAsyncCommand _applyMeshOperationCommand;
    private readonly RekallAgeAsyncCommand _cancelMeshPreviewCommand;
    private Process? _player;
    private CancellationTokenSource? _agentCancellation;
    private bool _isBusy;
    private bool _isAgentRunning;
    private bool _isLiveViewportEnabled = true;
    private Task? _disposeTask;
    private int _previewAdvancing;
    private int _previewFrameIndex;
    private RekallAgeStudioMode _mode;
    private string _projectPathInput = string.Empty;
    private string _projectNameInput = "New Rekall Game";
    private string _sceneNameInput = "Main";
    private string _componentTypeInput = "Rekall.Transform";
    private string _propertyNameInput = "position";
    private string _propertyValueInput = "[0, 0, 0]";
    private string _propertySchemaHelp = "Select a registered property to see its type and constraints.";
    private string _selectedOllamaModel = "qwen3.5:35b";
    private string _agentTaskInput = string.Empty;
    private string? _selectedMeshAssetId;
    private ulong? _selectedMeshElementId;
    private RekallAgeGeometryDomain _meshEditDomain = RekallAgeGeometryDomain.Face;
    private string? _selectedMeshOperationId;
    private string _meshOperationParameters = "{}";
    private string _meshSummary = "Open a persisted mesh asset to begin modeling.";
    private bool _extendMeshSelection;
    private bool _toggleMeshSelection;
    private BitmapSource? _meshViewportImage;
    private string? _lastPackagePath;
    private string _statusText = "Create or open a Rekall AGE project to begin.";
    private string _viewportTitle = "Viewport";
    private string _viewportSummary = "No rendered frame yet.";
    private BitmapSource? _viewportImage;
    private int _viewportRenderableCount;
    private bool _viewportVisuallyInformative;
    private RekallAgeWorkbenchModel? _currentModel;
    private readonly List<RekallAgeLanguageModelToolExecution> _lastAgentToolExecutions = [];
    internal bool TreatGauntletAsTerminalSuccess { get; set; }

    internal int? AgentMaxTurns { get; set; }

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
        : this(session, languageModelClient, new RekallAgeStudioPreviewSession())
    {
    }

    internal RekallAgeStudioViewModel(
        RekallAgeWorkbenchSession session,
        IRekallAgeLanguageModelClient? languageModelClient,
        IRekallAgeStudioPreviewSession previewSession)
    {
        _session = session;
        _previewSession = previewSession;
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
        _addEntityCommand = CreateAsyncCommand(AddEntityAsync, HasEditableProject);
        _addComponentCommand = CreateAsyncCommand(AddComponentAsync, CanEditComponent);
        _removeComponentCommand = CreateAsyncCommand(RemoveComponentAsync, CanEditComponent);
        _setPropertyCommand = CreateAsyncCommand(SetPropertyAsync, CanEditProperty);
        _removePropertyCommand = CreateAsyncCommand(RemovePropertyAsync, CanEditProperty);
        _validateCommand = CreateAsyncCommand(ValidateAsync, HasOpenProject);
        _captureCommand = CreateAsyncCommand(CaptureAsync, HasEditableProject);
        _simulateCommand = CreateAsyncCommand(StartSimulationAsync, () => HasOpenProject() && Mode == RekallAgeStudioMode.Edit);
        _playCommand = CreateAsyncCommand(PlayAsync, () => HasOpenProject() && Mode == RekallAgeStudioMode.Edit);
        _stopCommand = CreateAsyncCommand(StopAsync, () => !IsBusy && Mode != RekallAgeStudioMode.Edit);
        _switchSceneCommand = CreateAsyncCommand(SwitchSceneAsync, CanSwitchScene);
        _packageCommand = CreateAsyncCommand(PackageAsync, HasOpenProject);
        _auditPackageCommand = CreateAsyncCommand(AuditPackageAsync, CanAuditPackage);
        _undoCommand = CreateAsyncCommand(UndoAsync, () => HasEditableProject() && _session.CanUndo);
        _redoCommand = CreateAsyncCommand(RedoAsync, () => HasEditableProject() && _session.CanRedo);
        _discoverModelsCommand = CreateAsyncCommand(DiscoverModelsAsync, () => !IsBusy && !IsAgentRunning);
        _runAgentCommand = CreateAsyncCommand(RunAgentAsync, CanRunAgent);
        _cancelAgentCommand = CreateAsyncCommand(CancelAgentAsync, () => IsAgentRunning);
        _refreshMeshAssetsCommand = CreateAsyncCommand(RefreshMeshAssetsAsync, HasOpenProject);
        _openMeshAssetCommand = CreateAsyncCommand(OpenMeshAssetAsync, CanOpenMeshAsset);
        _selectMeshElementCommand = CreateAsyncCommand(SelectMeshElementAsync, CanSelectMeshElement);
        _clearMeshSelectionCommand = CreateAsyncCommand(ClearMeshSelectionAsync, HasOpenMesh);
        _previewMeshOperationCommand = CreateAsyncCommand(PreviewMeshOperationAsync, CanRunMeshOperation);
        _applyMeshOperationCommand = CreateAsyncCommand(ApplyMeshOperationAsync, CanRunMeshOperation);
        _cancelMeshPreviewCommand = CreateAsyncCommand(CancelMeshPreviewAsync, () => HasOpenMesh() && _modeling.Preview is not null);
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
    public ObservableCollection<string> MeshAssetIds { get; } = [];
    public ObservableCollection<ulong> MeshElementIds { get; } = [];
    public ObservableCollection<string> MeshOperationIds { get; } = [];
    public ObservableCollection<string> MeshSelectionLines { get; } = [];
    public ObservableCollection<string> MeshDiagnosticLines { get; } = [];
    public ObservableCollection<RekallAgeStudioMeshParameterModel> MeshParameterEditors { get; } = [];
    public IReadOnlyList<RekallAgeGeometryDomain> MeshEditDomains { get; } =
        [RekallAgeGeometryDomain.Point, RekallAgeGeometryDomain.Edge, RekallAgeGeometryDomain.Face, RekallAgeGeometryDomain.Corner];
    public IReadOnlyList<RekallAgeLanguageModelToolExecution> LastAgentToolExecutions => _lastAgentToolExecutions;
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
    public ICommand SimulateCommand => _simulateCommand;
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
    public ICommand RefreshMeshAssetsCommand => _refreshMeshAssetsCommand;
    public ICommand OpenMeshAssetCommand => _openMeshAssetCommand;
    public ICommand SelectMeshElementCommand => _selectMeshElementCommand;
    public ICommand ClearMeshSelectionCommand => _clearMeshSelectionCommand;
    public ICommand PreviewMeshOperationCommand => _previewMeshOperationCommand;
    public ICommand ApplyMeshOperationCommand => _applyMeshOperationCommand;
    public ICommand CancelMeshPreviewCommand => _cancelMeshPreviewCommand;

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

    public string? SelectedMeshAssetId
    {
        get => _selectedMeshAssetId;
        set { if (Set(ref _selectedMeshAssetId, value)) RefreshCommands(); }
    }

    public ulong? SelectedMeshElementId
    {
        get => _selectedMeshElementId;
        set { if (Set(ref _selectedMeshElementId, value)) RefreshCommands(); }
    }

    public RekallAgeGeometryDomain MeshEditDomain
    {
        get => _meshEditDomain;
        set
        {
            if (!Set(ref _meshEditDomain, value)) return;
            if (_modeling.Mesh is not null) _modeling.SetDomain(value);
            RefreshMeshEditingState();
        }
    }

    public string? SelectedMeshOperationId
    {
        get => _selectedMeshOperationId;
        set { if (Set(ref _selectedMeshOperationId, value)) RefreshMeshParameterEditors(); }
    }

    public string MeshOperationParameters
    {
        get => _meshOperationParameters;
        set { if (Set(ref _meshOperationParameters, value)) RefreshCommands(); }
    }

    public string MeshSummary
    {
        get => _meshSummary;
        private set => Set(ref _meshSummary, value);
    }

    public bool ExtendMeshSelection
    {
        get => _extendMeshSelection;
        set => Set(ref _extendMeshSelection, value);
    }

    public bool ToggleMeshSelection
    {
        get => _toggleMeshSelection;
        set => Set(ref _toggleMeshSelection, value);
    }

    public BitmapSource? MeshViewportImage
    {
        get => _meshViewportImage;
        private set => Set(ref _meshViewportImage, value);
    }

    public void SelectMeshViewportElement(double normalizedX, double normalizedY, bool extend, bool toggle)
    {
        if (_meshViewportFrame is null || _modeling.Mesh is null || normalizedX is < 0 or > 1 || normalizedY is < 0 or > 1) return;
        var id = _meshViewportRenderer.Pick(_meshViewportFrame, MeshEditDomain,
            normalizedX * _meshViewportFrame.Image.PixelWidth,
            normalizedY * _meshViewportFrame.Image.PixelHeight);
        if (!id.HasValue) return;
        SelectedMeshElementId = id;
        _modeling.Select(id.Value, extend || ExtendMeshSelection, toggle || ToggleMeshSelection);
        RefreshMeshEditingState();
    }

    public bool BeginMeshViewportTransform(double normalizedX, double normalizedY)
    {
        if (IsBusy || MeshEditDomain != RekallAgeGeometryDomain.Point || _meshViewportFrame is null ||
            normalizedX is < 0 or > 1 || normalizedY is < 0 or > 1) return false;
        _meshTransformGesture = _meshViewportRenderer.BeginTransform(
            _meshViewportFrame,
            normalizedX * _meshViewportFrame.Image.PixelWidth,
            normalizedY * _meshViewportFrame.Image.PixelHeight);
        return _meshTransformGesture is not null;
    }

    public void UpdateMeshViewportTransform(double normalizedX, double normalizedY)
    {
        if (_meshViewportFrame is null || _meshTransformGesture is null) return;
        var translation = _meshViewportRenderer.ResolveTranslation(
            _meshViewportFrame,
            _meshTransformGesture,
            normalizedX * _meshViewportFrame.Image.PixelWidth,
            normalizedY * _meshViewportFrame.Image.PixelHeight);
        StatusText = $"Move {_meshTransformGesture.Axis}: {translation.X:0.###}, {translation.Y:0.###}, {translation.Z:0.###}";
    }

    public Task CompleteMeshViewportTransformAsync(double normalizedX, double normalizedY)
    {
        var frame = _meshViewportFrame;
        var gesture = _meshTransformGesture;
        _meshTransformGesture = null;
        if (frame is null || gesture is null) return Task.CompletedTask;
        var translation = _meshViewportRenderer.ResolveTranslation(
            frame,
            gesture,
            normalizedX * frame.Image.PixelWidth,
            normalizedY * frame.Image.PixelHeight);
        if (Math.Abs(translation.X) + Math.Abs(translation.Y) + Math.Abs(translation.Z) <= 1e-9)
        {
            StatusText = MeshSummary;
            return Task.CompletedTask;
        }
        return RunModelingAsync(async () =>
        {
            var parameters = new JsonObject { ["x"] = translation.X, ["y"] = translation.Y, ["z"] = translation.Z };
            var result = await _modeling.ApplyAsync("transform", parameters, "studio-gizmo", _lifecycleCancellation.Token);
            RefreshMeshEditingState();
            Replace(MeshDiagnosticLines, result.Validation.Diagnostics.Select(item => $"{item.Severity}: {item.Code} - {item.Message}"));
            if (IsLiveViewportEnabled) await RefreshEditPreviewAsync($"Moved mesh {SelectedMeshAssetId} along {gesture.Axis}.");
        });
    }

    public void CancelMeshViewportTransform()
    {
        _meshTransformGesture = null;
        StatusText = MeshSummary;
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

    public BitmapSource? ViewportImage
    {
        get => _viewportImage;
        private set => Set(ref _viewportImage, value);
    }

    public int ViewportRenderableCount
    {
        get => _viewportRenderableCount;
        private set => Set(ref _viewportRenderableCount, value);
    }

    public bool ViewportVisuallyInformative
    {
        get => _viewportVisuallyInformative;
        private set => Set(ref _viewportVisuallyInformative, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value)) RefreshCommands();
        }
    }

    public RekallAgeStudioMode Mode
    {
        get => _mode;
        private set
        {
            if (!Set(ref _mode, value)) return;
            OnPropertyChanged(nameof(ModeLabel));
            OnPropertyChanged(nameof(IsPlaying));
            OnPropertyChanged(nameof(IsSimulating));
            RefreshCommands();
        }
    }

    public string ModeLabel => Mode switch
    {
        RekallAgeStudioMode.Simulate => "SIMULATE",
        RekallAgeStudioMode.Play => "PLAY",
        _ => "EDIT"
    };

    public bool IsPlaying => Mode == RekallAgeStudioMode.Play && _player is { HasExited: false };
    public bool IsSimulating => Mode == RekallAgeStudioMode.Simulate;

    public bool IsLiveViewportEnabled
    {
        get => _isLiveViewportEnabled;
        set => Set(ref _isLiveViewportEnabled, value);
    }

    public int PreviewFrameIndex
    {
        get => _previewFrameIndex;
        private set => Set(ref _previewFrameIndex, value);
    }

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
        await RunAsync(() => _session.OpenAsync(projectRoot, sceneName, CancellationToken.None).AsTask(), refreshPreviewAfter: true);
    }

    public async Task SelectEntityAsync(RekallAgeSceneEntityNode entity)
    {
        if (IsBusy) return;
        await RunAsync(() => _session.SelectEntityAsync(entity.EntityId, CancellationToken.None).AsTask());
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        _lifecycleCancellation.Cancel();
        _agentCancellation?.Cancel();
        await StopCoreAsync(resetEditPreview: false, CancellationToken.None);
        await _previewSession.DisposeAsync();
        _agentCancellation?.Dispose();
        _ollamaHttpClient?.Dispose();
        _lifecycleCancellation.Dispose();
        _modeTransitionGate.Dispose();
    }

    private bool CanOpenOrCreate() => !IsBusy && Mode == RekallAgeStudioMode.Edit && !string.IsNullOrWhiteSpace(ProjectPathInput);
    private bool HasOpenProject() => !IsBusy && _session.Model is not null;
    private bool HasEditableProject() => HasOpenProject() && Mode == RekallAgeStudioMode.Edit;
    private bool CanEditComponent() => HasEditableProject()
        && _session.SelectedEntityId is not null
        && !string.IsNullOrWhiteSpace(ComponentTypeInput);
    private bool CanEditProperty() => CanEditComponent() && !string.IsNullOrWhiteSpace(PropertyNameInput);
    private bool CanRunAgent() => HasEditableProject()
        && !IsAgentRunning
        && !string.IsNullOrWhiteSpace(SelectedOllamaModel)
        && !string.IsNullOrWhiteSpace(AgentTaskInput);
    private bool CanSwitchScene() => HasEditableProject()
        && !string.IsNullOrWhiteSpace(SceneNameInput)
        && !_session.SceneName!.Equals(SceneNameInput.Trim(), StringComparison.Ordinal);
    private bool CanAuditPackage() => HasOpenProject()
        && LastPackagePath is not null
        && (File.Exists(LastPackagePath) || Directory.Exists(LastPackagePath));
    private bool HasOpenMesh() => !IsBusy && Mode == RekallAgeStudioMode.Edit && _modeling.Mesh is not null;
    private bool CanOpenMeshAsset() => HasEditableProject() && !string.IsNullOrWhiteSpace(SelectedMeshAssetId);
    private bool CanSelectMeshElement() => HasOpenMesh() && SelectedMeshElementId.HasValue;
    private bool CanRunMeshOperation() => HasOpenMesh() && !string.IsNullOrWhiteSpace(SelectedMeshOperationId)
        && MeshParameterEditors.All(item => item.IsValid);

    private Task RefreshMeshAssetsAsync() => RunModelingAsync(() =>
    {
        if (_session.ProjectRoot is null) throw new InvalidOperationException("Open a project before browsing mesh assets.");
        Replace(MeshAssetIds, _modeling.ListAssets(_session.ProjectRoot));
        if (SelectedMeshAssetId is null || !MeshAssetIds.Contains(SelectedMeshAssetId)) SelectedMeshAssetId = MeshAssetIds.FirstOrDefault();
        MeshSummary = MeshAssetIds.Count == 0 ? "No persisted mesh assets are present in Modeling/Meshes." : $"{MeshAssetIds.Count} mesh asset(s) available.";
        return Task.CompletedTask;
    });

    private Task OpenMeshAssetAsync() => RunModelingAsync(async () =>
    {
        await _modeling.OpenAsync(_session.ProjectRoot!, SelectedMeshAssetId!, _lifecycleCancellation.Token);
        _modeling.SetDomain(MeshEditDomain);
        RefreshMeshEditingState();
    });

    private Task SelectMeshElementAsync() => RunModelingAsync(() =>
    {
        _modeling.Select(SelectedMeshElementId!.Value, ExtendMeshSelection, ToggleMeshSelection);
        RefreshMeshEditingState();
        return Task.CompletedTask;
    });

    private Task ClearMeshSelectionAsync() => RunModelingAsync(() =>
    {
        _modeling.ClearSelection(); RefreshMeshEditingState(); return Task.CompletedTask;
    });

    private Task PreviewMeshOperationAsync() => RunModelingAsync(async () =>
    {
        var result = await _modeling.PreviewAsync(SelectedMeshOperationId!, ParseMeshParameters(), _lifecycleCancellation.Token);
        RefreshMeshEditingState();
        Replace(MeshDiagnosticLines, result.Validation.Diagnostics.Select(item => $"{item.Severity}: {item.Code} - {item.Message}"));
    });

    private Task ApplyMeshOperationAsync() => RunModelingAsync(async () =>
    {
        var result = await _modeling.ApplyAsync(SelectedMeshOperationId!, ParseMeshParameters(), "studio", _lifecycleCancellation.Token);
        RefreshMeshEditingState();
        Replace(MeshDiagnosticLines, result.Validation.Diagnostics.Select(item => $"{item.Severity}: {item.Code} - {item.Message}"));
        if (IsLiveViewportEnabled) await RefreshEditPreviewAsync($"Applied {SelectedMeshOperationId} to mesh {SelectedMeshAssetId}.");
    });

    private Task CancelMeshPreviewAsync() => RunModelingAsync(() =>
    {
        _modeling.CancelPreview(); RefreshMeshEditingState(); return Task.CompletedTask;
    });

    private async Task RunModelingAsync(Func<Task> operation)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await operation(); StatusText = MeshSummary; }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException or JsonException)
        {
            StatusText = exception.Message;
            Replace(MeshDiagnosticLines, [$"error: REKALL_STUDIO_MODELING_OPERATION_FAILED - {exception.Message}"]);
        }
        finally { IsBusy = false; }
    }

    private JsonObject ParseMeshParameters()
    {
        var result = new JsonObject();
        foreach (var editor in MeshParameterEditors)
        {
            if (!editor.TryGetValue(out var value)) throw new ArgumentException($"Parameter '{editor.Name}' must be a valid {editor.TypeLabel} value.");
            if (value is not null) result[editor.Name] = value;
        }
        return result;
    }

    private void RefreshMeshParameterEditors()
    {
        var descriptor = _modeling.AvailableOperations.FirstOrDefault(item => item.OperationId == SelectedMeshOperationId);
        Replace(MeshParameterEditors, descriptor?.Parameters.Select(item => new RekallAgeStudioMeshParameterModel(item)) ?? []);
        foreach (var editor in MeshParameterEditors) editor.PropertyChanged += (_, _) => RefreshCommands();
        MeshOperationParameters = "{" + string.Join(", ", MeshParameterEditors.Select(item => $"\"{item.Name}\": {JsonSerializer.Serialize(item.ValueText)}")) + "}";
        RefreshCommands();
    }

    private void RefreshMeshEditingState()
    {
        if (_modeling.Mesh is null)
        {
            Replace(MeshElementIds, []); Replace(MeshOperationIds, []); Replace(MeshSelectionLines, []);
            _meshViewportFrame = null; MeshViewportImage = null; RefreshCommands(); return;
        }
        var mesh = _modeling.Preview?.Mesh ?? _modeling.Mesh;
        var ids = MeshEditDomain switch
        {
            RekallAgeGeometryDomain.Point => mesh.Topology.PointIds,
            RekallAgeGeometryDomain.Edge => mesh.Topology.EdgeIds,
            RekallAgeGeometryDomain.Face => mesh.Topology.FaceIds,
            RekallAgeGeometryDomain.Corner => mesh.Topology.CornerIds,
            _ => []
        };
        Replace(MeshElementIds, ids);
        Replace(MeshOperationIds, _modeling.AvailableOperations.Select(item => item.OperationId));
        if (SelectedMeshOperationId is null || !MeshOperationIds.Contains(SelectedMeshOperationId)) SelectedMeshOperationId = MeshOperationIds.FirstOrDefault();
        if (SelectedMeshElementId is null || !MeshElementIds.Contains(SelectedMeshElementId.Value)) SelectedMeshElementId = MeshElementIds.Count == 0 ? null : MeshElementIds[0];
        Replace(MeshSelectionLines, _modeling.SelectedElementIds.Select((id, index) => $"{index + 1}. {MeshEditDomain} {id}{(id == _modeling.ActiveElementId ? " (active)" : string.Empty)}"));
        MeshSummary = $"{mesh.Name} r{mesh.Revision} · {mesh.Topology.PointIds.Count} points · {mesh.Topology.EdgeIds.Count} edges · {mesh.Topology.FaceIds.Count} faces · {_modeling.SelectedElementIds.Count} selected{(_modeling.Preview is null ? string.Empty : " · PREVIEW")}";
        _meshViewportFrame = _meshViewportRenderer.Render(mesh, MeshEditDomain, _modeling.SelectedElementIds, 640, 360, _modeling.Preview is not null);
        MeshViewportImage = _meshViewportFrame.Image;
        OnPropertyChanged(nameof(MeshEditDomain)); RefreshCommands();
    }

    private Task OpenFromInputsAsync() => RunAsync(
        () => _session.OpenAsync(ProjectPathInput, NormalizeSceneName(), CancellationToken.None).AsTask(),
        refreshPreviewAfter: true);

    private Task CreateFromInputsAsync() => RunAsync(
        () => _session.CreateProjectAsync(
            ProjectPathInput,
            string.IsNullOrWhiteSpace(ProjectNameInput) ? "Rekall Game" : ProjectNameInput.Trim(),
            NormalizeSceneName(),
            ["world", "rendering2d", "rendering3d", "input", "audio", "ui", "animation", "physics", "modules"],
            ["world", "rendering2d", "rendering3d", "input", "audio", "ui", "animation", "physics"],
            "studio",
            CancellationToken.None).AsTask(),
        refreshPreviewAfter: true);

    private Task SwitchSceneAsync() => RunAsync(
        () => _session.OpenSceneAsync(NormalizeSceneName(), CancellationToken.None).AsTask(),
        refreshPreviewAfter: true);

    private Task UndoAsync() => RunAsync(
        () => _session.UndoAsync("studio", CancellationToken.None).AsTask(),
        refreshPreviewAfter: true);

    private Task RedoAsync() => RunAsync(
        () => _session.RedoAsync("studio", CancellationToken.None).AsTask(),
        refreshPreviewAfter: true);

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
            CancellationToken.None).AsTask(), refreshPreviewAfter: true);
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
            CancellationToken.None).AsTask(), refreshPreviewAfter: true);

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
        _lastAgentToolExecutions.Clear();
        AppendAgentLine($"model: {SelectedOllamaModel}");
        AppendAgentLine($"task: {AgentTaskInput.Trim()}");
        try
        {
            IProgress<RekallAgeLanguageModelAgentProgress> progress = SynchronizationContext.Current is null
                ? new ImmediateProgress<RekallAgeLanguageModelAgentProgress>(ReportAgentProgress)
                : new Progress<RekallAgeLanguageModelAgentProgress>(ReportAgentProgress);
            var result = await _agentSession.RunAsync(
                new RekallAgeProjectAgentSessionRequest(
                    _session.ProjectRoot,
                    _session.SceneName,
                    SelectedOllamaModel,
                    AgentTaskInput)
                {
                    MaxTurns = AgentMaxTurns,
                    RequireCompletionAudit = true,
                    RequireCompletionAuditToolEvidence = !TreatGauntletAsTerminalSuccess,
                    TreatGauntletAsTerminalSuccess = TreatGauntletAsTerminalSuccess
                },
                progress,
                cancellationToken);
            _lastAgentToolExecutions.Clear();
            _lastAgentToolExecutions.AddRange(result.AgentResult.ToolExecutions);
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
        if (progress.ToolExecution is { } execution
            && !_lastAgentToolExecutions.Any(existing => existing.Sequence == execution.Sequence))
        {
            _lastAgentToolExecutions.Add(execution);
        }

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
        ViewportVisuallyInformative = false;
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
            ViewportRenderableCount = capture.RenderableCount;
            ViewportVisuallyInformative = IsStudioVisualProofAcceptable(capture.FrameAnalysis);
            ViewportSummary = $"{capture.Width}×{capture.Height} · frame {capture.FrameIndex} · {capture.RenderableCount} renderables · "
                + (ViewportVisuallyInformative ? "visually informative" : "visual repair required");
        }
        return result;
    }

    internal static bool IsStudioVisualProofAcceptable(RekallAgeViewportFrameAnalysis analysis) =>
        analysis.Analyzed
        && analysis.VisuallyInformative
        && !analysis.WarningCodes.Contains(
            "REKALL_VIEWPORT_LOW_VISUAL_COVERAGE",
            StringComparer.Ordinal);

    private async Task StartSimulationAsync()
    {
        if (_session.ProjectRoot is null || _session.SceneName is null) return;
        IsBusy = true;
        var transitionEntered = false;
        try
        {
            await _modeTransitionGate.WaitAsync(_lifecycleCancellation.Token);
            transitionEntered = true;
            var frame = await _previewSession.ResetAsync(
                _session.ProjectRoot,
                _session.SceneName,
                960,
                540,
                _lifecycleCancellation.Token);
            ApplyPreviewFrame(frame);
            Mode = RekallAgeStudioMode.Simulate;
            StatusText = $"Simulating {_session.SceneName} in the live Studio viewport.";
        }
        catch (OperationCanceledException) when (_lifecycleCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (transitionEntered) _modeTransitionGate.Release();
            IsBusy = false;
        }
    }

    internal async Task AdvanceLivePreviewAsync()
    {
        if (!IsBusy && Mode == RekallAgeStudioMode.Play && _player is { HasExited: true })
        {
            _player.Dispose();
            _player = null;
            Mode = RekallAgeStudioMode.Edit;
            StatusText = "Player exited; returned to Edit mode.";
            if (IsLiveViewportEnabled)
            {
                await RefreshEditPreviewAsync(StatusText);
            }
        }
        if (!IsSimulating || !IsLiveViewportEnabled || IsBusy
            || Interlocked.Exchange(ref _previewAdvancing, 1) != 0)
        {
            return;
        }
        try
        {
            ApplyPreviewFrame(await _previewSession.StepAsync(6, _lifecycleCancellation.Token));
        }
        catch (OperationCanceledException) when (_lifecycleCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            Mode = RekallAgeStudioMode.Edit;
            StatusText = $"Live simulation stopped: {exception.Message}";
            Replace(ValidationLines, [$"error: REKALL_STUDIO_PREVIEW_FAILED - {exception.Message}"]);
        }
        finally
        {
            Volatile.Write(ref _previewAdvancing, 0);
        }
    }

    private void ApplyPreviewFrame(RekallAgeStudioPreviewFrame frame)
    {
        ViewportImage = frame.Image;
        PreviewFrameIndex = frame.FrameIndex;
        ViewportRenderableCount = frame.RenderableCount;
        ViewportSummary = $"{frame.Image.PixelWidth}×{frame.Image.PixelHeight} · frame {frame.FrameIndex} · "
            + $"{frame.RenderableCount} renderables · {frame.ObservationCount} observations · live preview";
    }

    private async Task PlayAsync()
    {
        if (_session.ProjectRoot is null || _session.SceneName is null) return;
        IsBusy = true;
        var transitionEntered = false;
        try
        {
            await _modeTransitionGate.WaitAsync(_lifecycleCancellation.Token);
            transitionEntered = true;
            await _previewSession.ClearAsync(_lifecycleCancellation.Token);
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
            if (_player is null)
            {
                StatusText = "Player could not be started.";
                return;
            }
            Mode = RekallAgeStudioMode.Play;
            StatusText = $"Playing {_session.SceneName} in the production Player window.";
        }
        catch (OperationCanceledException) when (_lifecycleCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (transitionEntered) _modeTransitionGate.Release();
            IsBusy = false;
        }
    }

    private Task StopAsync() => StopCoreAsync(resetEditPreview: true, _lifecycleCancellation.Token);

    private async Task StopCoreAsync(bool resetEditPreview, CancellationToken cancellationToken)
    {
        IsBusy = true;
        var transitionEntered = false;
        try
        {
            await _modeTransitionGate.WaitAsync(cancellationToken);
            transitionEntered = true;
            if (Mode == RekallAgeStudioMode.Simulate)
            {
                Mode = RekallAgeStudioMode.Edit;
                if (resetEditPreview && _session.ProjectRoot is not null && _session.SceneName is not null)
                {
                    ApplyPreviewFrame(await _previewSession.ResetAsync(
                        _session.ProjectRoot,
                        _session.SceneName,
                        960,
                        540,
                        cancellationToken));
                }
                StatusText = "Simulation stopped; authored scene state is unchanged.";
                return;
            }
            if (_player is null)
            {
                Mode = RekallAgeStudioMode.Edit;
                return;
            }
            try
            {
                if (!_player.HasExited)
                {
                    _player.Kill(entireProcessTree: true);
                    await _player.WaitForExitAsync(cancellationToken);
                }
            }
            finally
            {
                _player.Dispose();
                _player = null;
                Mode = RekallAgeStudioMode.Edit;
                StatusText = "Play mode stopped; returned to Edit mode.";
            }
        }
        finally
        {
            if (transitionEntered) _modeTransitionGate.Release();
            IsBusy = false;
        }
    }

    private async Task RunAsync(Func<Task<RekallAgeWorkbenchOperationResult>> operation, bool refreshPreviewAfter = false)
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
                if (refreshPreviewAfter && IsLiveViewportEnabled && Mode == RekallAgeStudioMode.Edit)
                {
                    await RefreshEditPreviewAsync(result.Summary);
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

    private async Task RefreshEditPreviewAsync(string operationSummary)
    {
        if (_session.ProjectRoot is null || _session.SceneName is null) return;
        try
        {
            ApplyPreviewFrame(await _previewSession.ResetAsync(
                _session.ProjectRoot,
                _session.SceneName,
                960,
                540,
                _lifecycleCancellation.Token));
            StatusText = operationSummary;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            StatusText = $"{operationSummary} Live preview unavailable: {exception.Message}";
            ValidationLines.Insert(0, $"warning: REKALL_STUDIO_EDIT_PREVIEW_FAILED - {exception.Message}");
        }
        catch (OperationCanceledException) when (_lifecycleCancellation.IsCancellationRequested)
        {
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
        if (_session.ProjectRoot is not null)
        {
            Replace(MeshAssetIds, _modeling.ListAssets(_session.ProjectRoot));
            if (SelectedMeshAssetId is null || !MeshAssetIds.Contains(SelectedMeshAssetId)) SelectedMeshAssetId = MeshAssetIds.FirstOrDefault();
        }
        Replace(ValidationLines, model.Diagnostics.Issues.Select(issue => $"{issue.Severity}: {issue.Code} - {issue.Message}"));
        Replace(TransactionLines, model.Transactions.Transactions.Select(transaction => $"{transaction.Name}: {transaction.ChangedResources.Count} changes"));
        Replace(ImportLines, model.ImportQueue.Jobs.Select(job => $"{job.Status}: {job.SourcePath}"));
        Replace(SceneSummaryLines, BuildSceneSummaryLines(model.SceneSummary));
        Replace(ActionLines, model.Actions.Actions.Select(action => $"{action.Category}: {action.Label} ({action.Tool})"));
        Replace(RuntimeObservationLines, model.Runtime.Observations.Select(observation =>
            $"{observation.Severity}: {observation.Code} - {observation.Message}"));
        ViewportTitle = $"{model.Scene.Name} Viewport";
        ViewportRenderableCount = model.Runtime.RenderableCount;
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
        _simulateCommand.RaiseCanExecuteChanged();
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
        _refreshMeshAssetsCommand.RaiseCanExecuteChanged();
        _openMeshAssetCommand.RaiseCanExecuteChanged();
        _selectMeshElementCommand.RaiseCanExecuteChanged();
        _clearMeshSelectionCommand.RaiseCanExecuteChanged();
        _previewMeshOperationCommand.RaiseCanExecuteChanged();
        _applyMeshOperationCommand.RaiseCanExecuteChanged();
        _cancelMeshPreviewCommand.RaiseCanExecuteChanged();
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

internal sealed class ImmediateProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
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
