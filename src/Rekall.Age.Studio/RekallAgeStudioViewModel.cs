using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Rekall.Age.Agent.Codex;
using Rekall.Age.Agent.LanguageModels;
using Rekall.Age.Assets;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Editor;
using Rekall.Age.Editor.Contracts;
using Rekall.Age.LevelDesign.Commands;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Modules.Commands;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.Workflows;
using Rekall.Age.Workflows.Commands;
using Serilog;

namespace Rekall.Age.Studio;

public enum RekallAgeStudioMode
{
    Edit,
    Simulate,
    Play
}

public sealed class RekallAgeStudioViewModel : INotifyPropertyChanged, IAsyncDisposable, IRekallAgeStudioContentOpenTarget
{
    private readonly RekallAgeWorkbenchSession _session;
    private readonly RekallAgeCommandRegistry _agentRegistry;
    private readonly RekallAgeLanguageModelProviderCatalog _languageModelProviderCatalog;
    private readonly IRekallAgeStudioPreviewSession _previewSession;
    private readonly RekallAgeStudioSimulationCadence _simulationCadence;
    private readonly IRekallAgeGgufImporter _ggufImporter;
    private readonly SemaphoreSlim _modeTransitionGate = new(1, 1);
    private readonly CancellationTokenSource _lifecycleCancellation = new();
    private readonly object _disposeSync = new();
    private readonly object _languageModelLifecycleSync = new();
    private readonly object _renderingOperationsSync = new();
    private readonly HashSet<Task> _activeRenderingOperations = [];
    private readonly RekallAgeAsyncCommand _openCommand;
    private readonly RekallAgeAsyncCommand _createCommand;
    private readonly RekallAgeAsyncCommand _openSelectedContentCommand;
    private readonly RekallAgeAsyncCommand _importContentCommand;
    private readonly RekallAgeAsyncCommand _addEntityCommand;
    private readonly RekallAgeAsyncCommand _renameEntityCommand;
    private readonly RekallAgeAsyncCommand _duplicateEntityCommand;
    private readonly RekallAgeAsyncCommand _deleteEntityCommand;
    private readonly RekallAgeAsyncCommand _toggleEntityVisibleCommand;
    private readonly RekallAgeAsyncCommand _toggleEntityLockedCommand;
    private readonly RekallAgeAsyncCommand _reparentEntityCommand;
    private readonly RekallAgeAsyncCommand _clearEntityParentCommand;
    private readonly RekallAgeAsyncCommand _addComponentCommand;
    private readonly RekallAgeAsyncCommand _removeComponentCommand;
    private readonly RekallAgeAsyncCommand _setPropertyCommand;
    private readonly RekallAgeAsyncCommand _removePropertyCommand;
    private readonly RekallAgeAsyncCommand _commitInspectorPropertyCommand;
    private readonly RekallAgeAsyncCommand _resetInspectorPropertyCommand;
    private readonly RekallAgeAsyncCommand _validateCommand;
    private readonly RekallAgeAsyncCommand _captureCommand;
    private readonly RekallAgeAsyncCommand _attachQualityProfileCommand;
    private readonly RekallAgeAsyncCommand _applyQualityCommand;
    private readonly RekallAgeAsyncCommand _captureQualityCommand;
    private readonly RekallAgeAsyncCommand _compareQualityCommand;
    private readonly RekallAgeAsyncCommand _playCommand;
    private readonly RekallAgeAsyncCommand _simulateCommand;
    private readonly RekallAgeAsyncCommand _pauseSimulationCommand;
    private readonly RekallAgeAsyncCommand _stepSimulationCommand;
    private readonly RekallAgeAsyncCommand _stopCommand;
    private readonly RekallAgeAsyncCommand _switchSceneCommand;
    private readonly RekallAgeAsyncCommand _packageCommand;
    private readonly RekallAgeAsyncCommand _auditPackageCommand;
    private readonly RekallAgeAsyncCommand _openPackageFolderCommand;
    private readonly RekallAgeAsyncCommand _publishWebCommand;
    private readonly RekallAgeAsyncCommand _auditWebCommand;
    private readonly RekallAgeAsyncCommand _undoCommand;
    private readonly RekallAgeAsyncCommand _redoCommand;
    private readonly RekallAgeAsyncCommand _discoverModelsCommand;
    private readonly RekallAgeAsyncCommand _signInCodexCommand;
    private readonly RekallAgeAsyncCommand _cancelCodexSignInCommand;
    private readonly RekallAgeAsyncCommand _runAgentCommand;
    private readonly RekallAgeAsyncCommand _cancelAgentCommand;
    private readonly RekallAgeStudioCodeSession _codeSession = new();
    private readonly RekallAgeAsyncCommand _refreshCodeCommand;
    private readonly RekallAgeAsyncCommand _saveCodeCommand;
    private readonly RekallAgeAsyncCommand _buildCodeCommand;
    private readonly RekallAgeAsyncCommand _createAttachCodeComponentCommand;
    private readonly RekallAgeAsyncCommand _openCodeFileCommand;
    private readonly RekallAgeAsyncCommand _openCodeProjectCommand;
    private readonly RekallAgeAsyncCommand _openCodeSolutionCommand;
    private readonly RekallAgeAsyncCommand _openCodeInVsCodeCommand;
    private readonly RekallAgeStudioModelingSession _modeling = new();
    private readonly RekallAgeMeshPrimitiveFactory _meshPrimitiveFactory = new();
    private readonly RekallAgeStudioModelingGraphSession _modelingGraph = new();
    private readonly RekallAgeStudioMeshViewportRenderer _meshViewportRenderer = new();
    private readonly RekallAgeModelAssetStore _modelAssetStore = new();
    private readonly IRekallAgeStudioContentIndex _contentIndex = RekallAgeStudioContentIndex.CreateDefault();
    private readonly IRekallAgeStudioContentOpenRouter _contentOpenRouter;
    private readonly RekallAgeStudioContentImportSession _contentImportSession;
    private readonly RekallAgeStudioContentDragService _contentDragService;
    private readonly IRekallAgeStudioContentPreviewService _contentPreviewService;
    private readonly IRekallAgeStudioExternalContentLauncher _externalContentLauncher;
    private readonly Action<string> _openPackageFolder;
    private RekallAgeStudioMeshViewportFrame? _meshViewportFrame;
    private RekallAgeStudioMeshTransformGesture? _meshTransformGesture;
    private RekallAgeStudioViewportCamera _meshViewportCamera = RekallAgeStudioViewportCamera.Identity;
    private readonly RekallAgeStudioModelingGraphCanvasRenderer _modelingGraphCanvasRenderer = new();
    private readonly RekallAgeModelingNodeCatalog _modelingGraphCatalog = RekallAgeModelingNodeCatalog.CreateDefault();
    private readonly Dictionary<string, System.Windows.Point> _modelingGraphNodePositions = new(StringComparer.Ordinal);
    private readonly Dictionary<RekallAgeStudioInspectorPropertyEditorModel, InspectorPropertyEditorKey> _inspectorPropertyEditorKeys = [];
    private readonly List<RekallAgeStudioInspectorComponentEditorModel> _allInspectorComponentEditors = [];
    private RekallAgeStudioModelingGraphCanvasFrame? _modelingGraphCanvasFrame;
    private RekallAgeStudioModelingGraphCanvasView _modelingGraphCanvasView = RekallAgeStudioModelingGraphCanvasView.Identity;
    private bool _modelingGraphCanvasNeedsFrame;
    private string? _modelingGraphDragNodeId;
    private System.Windows.Point _modelingGraphDragOrigin;
    private System.Windows.Point _modelingGraphDragStart;
    private RekallAgeStudioModelingGraphPortKey? _modelingGraphPendingLinkPort;
    private RekallAgeStudioMeshParameterModel? _modalDragParameter;
    private double _modalDragOriginalValue;
    private double _modalDragStartNormalizedX;
    private readonly RekallAgeAsyncCommand _refreshMeshAssetsCommand;
    private readonly RekallAgeAsyncCommand _createMeshPrimitiveCommand;
    private readonly RekallAgeAsyncCommand _openMeshAssetCommand;
    private readonly RekallAgeAsyncCommand _frameSelectedMeshViewportCommand;
    private readonly RekallAgeAsyncCommand _toggleMeshViewportProjectionCommand;
    private readonly RekallAgeAsyncCommand _publishModelCommand;
    private readonly RekallAgeAsyncCommand _placeModelCommand;
    private readonly RekallAgeAsyncCommand _publishAndPlaceModelCommand;
    private readonly RekallAgeAsyncCommand _selectMeshElementCommand;
    private readonly RekallAgeAsyncCommand _clearMeshSelectionCommand;
    private readonly RekallAgeAsyncCommand _previewMeshOperationCommand;
    private readonly RekallAgeAsyncCommand _applyMeshOperationCommand;
    private readonly RekallAgeAsyncCommand _cancelMeshPreviewCommand;
    private readonly RekallAgeAsyncCommand _refreshModelingGraphsCommand;
    private readonly RekallAgeAsyncCommand _openModelingGraphCommand;
    private readonly RekallAgeAsyncCommand _evaluateModelingGraphCommand;
    private readonly RekallAgeAsyncCommand _applyModelingGraphParametersCommand;
    private readonly RekallAgeStudioMaterialGraphSession _materialGraph = new();
    private readonly RekallAgeAsyncCommand _refreshMaterialGraphsCommand;
    private readonly RekallAgeAsyncCommand _openMaterialGraphCommand;
    private readonly RekallAgeAsyncCommand _applyMaterialGraphParametersCommand;
    private readonly RekallAgeStudioAnimationMixerSession _animationMixer = new();
    private readonly RekallAgeAsyncCommand _openAnimationMixerCommand;
    private readonly RekallAgeAsyncCommand _applyAnimationMixerLayersCommand;
    private readonly RekallAgeAsyncCommand _addAnimationMixerLayerCommand;
    private Process? _player;
    private CancellationTokenSource? _agentCancellation;
    private RekallAgeLanguageModelProviderLease? _languageModelProviderLease;
    private IRekallAgeProjectAgentRunner? _languageModelRunner;
    private IDisposable? _fixedLanguageModelRunner;
    private CancellationTokenSource? _languageModelRefreshCancellation;
    private Task _languageModelProviderTransition = Task.CompletedTask;
    private long _languageModelProviderTransitionGeneration;
    private Task? _activeLanguageModelRefresh;
    private Task? _activeAgentRun;
    private Task? _activeCodexSignIn;
    private CancellationTokenSource? _codexSignInCancellation;
    private string? _sessionOpenAiApiKey;
    private string? _sessionKimiApiKey;
    private string? _sessionOllamaUrl;
    private string? _sessionOpenAiUrl;
    private string? _sessionKimiUrl;
    private bool _languageModelSetupAllowsAuthoring = true;
    private bool _localModelRuntimeReady;
    private bool _languageModelSetupBusy;
    private bool _isBusy;
    private bool _isAgentRunning;
    private bool _hasActiveContentImports;
    private int _contentImportActive;
    private bool _isLiveViewportEnabled = true;
    private bool _isSimulationPaused;
    private Task? _disposeTask;
    private bool _shutdownPrerequisitesComplete;
    private bool _disposalComplete;
    private int _previewAdvancing;
    private int _previewFrameIndex;
    private RekallAgeStudioMode _mode;
    private string _projectPathInput = string.Empty;
    private string _projectNameInput = "New Rekall Game";
    private string _sceneNameInput = "Main";
    private string _componentTypeInput = "Rekall.Transform";
    private string _inspectorSearchInput = string.Empty;
    private RekallAgeInspectorComponentModel? _selectedInspectorComponent;
    private string _entityNameInput = string.Empty;
    private string _parentEntityIdInput = string.Empty;
    private string _propertyNameInput = "position";
    private string _propertyValueInput = "[0, 0, 0]";
    private string _propertySchemaHelp = "Select a registered property to see its type and constraints.";
    private RekallAgeLanguageModelProviderDescriptor _selectedLanguageModelProvider = null!;
    private string _selectedLanguageModel = string.Empty;
    private string _selectedReasoningEffort = "medium";
    private bool _preapproveAllCodexActionsForSession;
    private string _providerStatus = string.Empty;
    private string _providerDisplayStatus = string.Empty;
    private string _agentTaskInput = string.Empty;
    private string _agentActivityText = "AI authoring is idle.";
    private RekallAgeModuleSourceInfo? _selectedCodeSource;
    private string _codeSourceText = string.Empty;
    private string _codeModuleNameInput = "GameRules";
    private string _codeComponentNameInput = "GameState";
    private string _codeSystemNameInput = "GameRulesSystem";
    private string _codeStatusText = "Open a project to inspect C# gameplay modules.";
    private string? _selectedMeshAssetId;
    private string _modelAssetIdInput = string.Empty;
    private string _modelAssetDisplayNameInput = string.Empty;
    private string _modelEntityIdInput = string.Empty;
    private string _modelEntityNameInput = string.Empty;
    private string _modelPlacementParentEntityIdInput = string.Empty;
    private double _modelPositionX;
    private double _modelPositionY;
    private double _modelPositionZ;
    private double _modelRotationX;
    private double _modelRotationY;
    private double _modelRotationZ;
    private double _modelScaleX = 1;
    private double _modelScaleY = 1;
    private double _modelScaleZ = 1;
    private string _modelPositionXInput = "0";
    private string _modelPositionYInput = "0";
    private string _modelPositionZInput = "0";
    private string _modelRotationXInput = "0";
    private string _modelRotationYInput = "0";
    private string _modelRotationZInput = "0";
    private string _modelScaleXInput = "1";
    private string _modelScaleYInput = "1";
    private string _modelScaleZInput = "1";
    private string? _lastPublishedModelAssetId;
    private string? _lastPlacedModelEntityId;
    private string _selectedMeshPrimitive = "box";
    private string _meshPrimitiveAssetIdInput = "mesh-box";
    private ulong? _selectedMeshElementId;
    private RekallAgeGeometryDomain _meshEditDomain = RekallAgeGeometryDomain.Face;
    private string? _selectedMeshOperationId;
    private string _meshOperationParameters = "{}";
    private string _meshSummary = "Open a persisted mesh asset to begin modeling.";
    private bool _extendMeshSelection;
    private bool _toggleMeshSelection;
    private BitmapSource? _meshViewportImage;
    private string _meshViewportRenderStyle = "Smooth shaded";
    private string? _selectedModelingGraphAssetId;
    private string? _selectedModelingGraphOutput;
    private string _modelingGraphSummary = "Open a procedural graph to inspect its nodes and evaluation evidence.";
    private BitmapSource? _modelingGraphViewportImage;
    private string _modelingGraphViewportRenderStyle = "Smooth shaded";
    private RekallAgeStudioModelingGraphNodeView? _selectedModelingGraphNode;
    private string? _selectedMaterialGraphAssetId;
    private string _materialGraphSummary = "Open a material graph to inspect its node contracts.";
    private RekallAgeStudioMaterialGraphNodeView? _selectedMaterialGraphNode;
    private bool _animationMixerIsOpen;
    private string _animationMixerSummary = "Select a scene entity, then open its Rekall.AnimationMixer component.";
    private string? _lastPackagePath;
    private string? _lastPackageOutputDirectory;
    private string? _lastPackageLaunchPath;
    private string _selectedPackageTarget = RekallAgePlayablePackageTargets.Windows;
    private string? _lastWebPublishPath;
    private string _statusText = "Create or open a Rekall AGE project to begin.";
    private string _viewportTitle = "Viewport";
    private string _viewportSummary = "Open or create a project to begin.";
    private string _viewportBackendLabel = "Vulkan · idle";
    private bool _viewportAvailable;
    private string _viewportUnavailableReason = string.Empty;
    private string _worldViewportRenderStyle = "Textured";
    private RekallAgeStudioViewportInteractionSnapshot? _viewportInteraction;
    private RekallAgeStudioViewportPlacementContext _viewportPlacementContext =
        RekallAgeStudioViewportPlacementContext.From(null);
    private RekallAgeStudioSceneGizmo? _sceneGizmo;
    private RekallAgeStudioTransformGesture? _sceneTransformGesture;
    private RekallAgeStudioTransformUpdate? _sceneTransformUpdate;
    private RekallAgeStudioTransformTool _transformTool = RekallAgeStudioTransformTool.Move;
    private RekallAgeStudioTransformSpace _transformSpace = RekallAgeStudioTransformSpace.World;
    private double _moveSnap = 0.25;
    private double _rotationSnap = 15;
    private double _scaleSnap = 0.1;
    private int _viewportRenderableCount;
    private bool _lastCaptureNonblank;
    private bool _viewportVisuallyInformative;
    private string _selectedQualityPreset = "High";
    private string _comparisonQualityPreset = "Performance";
    private string _qualityResolutionScaleInput = string.Empty;
    private string _qualityShadowCascadeCountInput = string.Empty;
    private string _qualityShadowResolutionInput = string.Empty;
    private string _qualityFogModeInput = string.Empty;
    private bool? _qualityBloomOverride;
    private bool? _qualitySsaoOverride;
    private string _qualityMaximumActiveParticlesInput = string.Empty;
    private string _requestedQualityPreset = "High";
    private string _resolvedQualityPreset = "Unavailable";
    private string _outputResolutionText = "Unavailable";
    private string _internalResolutionText = "Unavailable";
    private string _totalGpuMillisecondsText = "Unavailable";
    private string _gpuTimingStatusText = "REKALL_GPU_TIMESTAMPS_UNAVAILABLE · unavailable";
    private string _renderWorkloadText = "0 draws · 0 dispatches";
    private RekallAgeWorkbenchRenderDebugViewModel? _selectedRenderDebugView;
    private RekallAgeWorkbenchModel? _currentModel;
    private RekallAgeContentBrowserModel _contentModel = RekallAgeContentBrowserModel.Empty;
    private string _selectedContentCategory = "All";
    private string _contentSearchText = string.Empty;
    private RekallAgeContentBrowserItem? _selectedContentItem;
    private RekallAgeStudioContentPreview? _selectedContentPreview;
    private CancellationTokenSource? _contentPreviewCancellation;
    private string _contentStatusText = "Select project content to inspect or edit.";
    private string _selectedStudioWorkspace = "Author";
    private string _selectedModelingSurface = "mesh-edit";
    private readonly List<RekallAgeLanguageModelToolExecution> _lastAgentToolExecutions = [];
    internal bool TreatGauntletAsTerminalSuccess { get; set; }

    internal int? AgentMaxTurns { get; set; } = 64;

    public RekallAgeStudioViewModel()
        : this(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            new RekallAgeLanguageModelProviderCatalog(),
            null,
            new RekallAgeStudioPreviewSession(),
            null)
    {
    }

    internal RekallAgeStudioViewModel(IRekallAgeStudioPreviewSession previewSession)
        : this(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            new RekallAgeLanguageModelProviderCatalog(),
            null,
            previewSession,
            null)
    {
    }

    internal RekallAgeStudioViewModel(IRekallAgeStudioContentOpenRouter contentOpenRouter)
        : this()
    {
        _contentOpenRouter = contentOpenRouter ?? throw new ArgumentNullException(nameof(contentOpenRouter));
    }

    internal RekallAgeStudioViewModel(RekallAgeWorkbenchSession session)
        : this(
            session,
            new RekallAgeLanguageModelProviderCatalog(),
            null,
            new RekallAgeStudioPreviewSession(),
            null)
    {
    }

    internal RekallAgeStudioViewModel(
        RekallAgeWorkbenchSession session,
        IRekallAgeStudioExternalContentLauncher externalContentLauncher)
        : this(
            session,
            new RekallAgeLanguageModelProviderCatalog(),
            null,
            new RekallAgeStudioPreviewSession(),
            null,
            externalContentLauncher: externalContentLauncher)
    {
    }

    internal RekallAgeStudioViewModel(
        RekallAgeWorkbenchSession session,
        IRekallAgeStudioPreviewSession previewSession,
        RekallAgeStudioContentImportSession contentImportSession)
        : this(
            session,
            new RekallAgeLanguageModelProviderCatalog(),
            null,
            previewSession,
            null,
            contentImportSession: contentImportSession)
    {
    }

    internal RekallAgeStudioViewModel(Action<string> openPackageFolder)
        : this(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            new RekallAgeLanguageModelProviderCatalog(),
            null,
            new RekallAgeStudioPreviewSession(),
            openPackageFolder)
    {
    }

    internal RekallAgeStudioViewModel(
        RekallAgeWorkbenchSession session,
        IRekallAgeLanguageModelClient? languageModelClient)
        : this(
            session,
            new RekallAgeLanguageModelProviderCatalog(),
            languageModelClient,
            new RekallAgeStudioPreviewSession(),
            null)
    {
    }

    internal RekallAgeStudioViewModel(
        RekallAgeWorkbenchSession session,
        IRekallAgeLanguageModelClient? languageModelClient,
        IRekallAgeStudioPreviewSession previewSession,
        Action<string>? openPackageFolder = null)
        : this(
            session,
            new RekallAgeLanguageModelProviderCatalog(),
            languageModelClient,
            previewSession,
            openPackageFolder)
    {
    }

    internal RekallAgeStudioViewModel(
        RekallAgeWorkbenchSession session,
        IRekallAgeLanguageModelClient? languageModelClient,
        IRekallAgeStudioPreviewSession previewSession,
        IRekallAgeStudioMonotonicClock monotonicClock)
        : this(
            session,
            new RekallAgeLanguageModelProviderCatalog(),
            languageModelClient,
            previewSession,
            null,
            monotonicClock)
    {
    }

    internal RekallAgeStudioViewModel(
        RekallAgeWorkbenchSession session,
        RekallAgeLanguageModelProviderCatalog languageModelProviderCatalog,
        IRekallAgeStudioPreviewSession previewSession,
        Action<string>? openPackageFolder = null)
        : this(session, languageModelProviderCatalog, null, previewSession, openPackageFolder)
    {
    }

    internal RekallAgeStudioViewModel(
        RekallAgeWorkbenchSession session,
        RekallAgeLanguageModelProviderCatalog languageModelProviderCatalog,
        IRekallAgeStudioPreviewSession previewSession,
        IRekallAgeGgufImporter ggufImporter)
        : this(session, languageModelProviderCatalog, null, previewSession, null, null, ggufImporter)
    {
    }

    private RekallAgeStudioViewModel(
        RekallAgeWorkbenchSession session,
        RekallAgeLanguageModelProviderCatalog languageModelProviderCatalog,
        IRekallAgeLanguageModelClient? fixedLanguageModelClient,
        IRekallAgeStudioPreviewSession previewSession,
        Action<string>? openPackageFolder,
        IRekallAgeStudioMonotonicClock? monotonicClock = null,
        IRekallAgeGgufImporter? ggufImporter = null,
        RekallAgeStudioContentImportSession? contentImportSession = null,
        IRekallAgeStudioExternalContentLauncher? externalContentLauncher = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _languageModelProviderCatalog = languageModelProviderCatalog
            ?? throw new ArgumentNullException(nameof(languageModelProviderCatalog));
        _previewSession = previewSession ?? throw new ArgumentNullException(nameof(previewSession));
        _simulationCadence = new RekallAgeStudioSimulationCadence(
            monotonicClock ?? new RekallAgeStudioStopwatchClock());
        _ggufImporter = ggufImporter ?? new RekallAgeOllamaGgufImporter();
        _openPackageFolder = openPackageFolder ?? OpenDirectoryInExplorer;
        _externalContentLauncher = externalContentLauncher ?? new RekallAgeStudioShellExternalContentLauncher();
        _contentOpenRouter = new RekallAgeStudioContentOpenRouter(this);
        _contentImportSession = contentImportSession ?? new RekallAgeStudioContentImportSession(
            new RekallAgeStudioAssetImportCommand(),
            async cancellationToken =>
            {
                if (_session.ProjectRoot is null) return;
                var content = await _contentIndex.RefreshAsync(_session.ProjectRoot, cancellationToken);
                ApplyContentModel(content);
            },
            cancellationToken => _previewSession.InvalidateAssetsAsync(cancellationToken));
        _contentDragService = new RekallAgeStudioContentDragService(
            new RekallAgeStudioContentPropertyMutationCommand(ExecuteContentPropertyMutationAsync),
            new RekallAgeStudioContentPlacementCommand(ExecuteContentPlacementAsync),
            new RekallAgeStudioContentDragResolver((contentId, contentKind, contentOrigin) =>
            {
                var item = _contentModel.Items.FirstOrDefault(candidate =>
                    candidate.Id.Equals(contentId, StringComparison.Ordinal)
                    && candidate.Kind.Equals(contentKind, StringComparison.OrdinalIgnoreCase)
                    && candidate.Origin.Equals(contentOrigin, StringComparison.OrdinalIgnoreCase));
                return item is null ? null : RekallAgeStudioContentDragPayload.FromItem(item);
            }));
        _contentPreviewService = RekallAgeStudioContentPreviewService.CreateDefault();
        _agentRegistry = RekallAgeDefaultCommandRegistry.Create();
        _selectedLanguageModelProvider = _languageModelProviderCatalog.Providers.Single(provider => provider.Id == "ollama");
        _selectedLanguageModel = _selectedLanguageModelProvider.DefaultModel;
        if (fixedLanguageModelClient is null)
        {
            _languageModelProviderLease = _languageModelProviderCatalog.Acquire("ollama", _agentRegistry);
            _languageModelRunner = _languageModelProviderLease.Runner;
            _providerStatus = "Local Ollama selected; setup not checked.";
            _providerDisplayStatus = _providerStatus;
        }
        else
        {
            var runner = new RekallAgeLanguageModelProjectAgentRunner(fixedLanguageModelClient, _agentRegistry);
            _languageModelRunner = runner;
            _fixedLanguageModelRunner = runner;
            _providerStatus = $"{fixedLanguageModelClient.ProviderId} test session ready.";
            _providerDisplayStatus = _providerStatus;
        }
        _openCommand = CreateAsyncCommand(OpenFromInputsAsync, CanOpenOrCreate);
        _createCommand = CreateAsyncCommand(CreateFromInputsAsync, CanOpenOrCreate);
        _openSelectedContentCommand = CreateAsyncCommand(OpenSelectedContentAsync, CanOpenSelectedContent);
        _importContentCommand = CreateAsyncCommand(
            ImportContentAsync,
            parameter => HasOpenProject() && !HasActiveContentImports && parameter is IEnumerable<string>);
        _addEntityCommand = CreateAsyncCommand(AddEntityAsync, HasEditableProject);
        _renameEntityCommand = CreateAsyncCommand(RenameEntityAsync, CanRenameEntity);
        _duplicateEntityCommand = CreateAsyncCommand(DuplicateEntityAsync, HasSelectedEntity);
        _deleteEntityCommand = CreateAsyncCommand(DeleteEntityAsync, HasSelectedEntity);
        _toggleEntityVisibleCommand = CreateAsyncCommand(ToggleEntityVisibleAsync, HasSelectedEntity);
        _toggleEntityLockedCommand = CreateAsyncCommand(ToggleEntityLockedAsync, HasSelectedEntity);
        _reparentEntityCommand = CreateAsyncCommand(ReparentEntityAsync, CanReparentEntity);
        _clearEntityParentCommand = CreateAsyncCommand(ClearEntityParentAsync, HasSelectedEntity);
        _addComponentCommand = CreateAsyncCommand(AddComponentAsync, CanEditComponent);
        _removeComponentCommand = CreateAsyncCommand(RemoveComponentAsync, CanEditComponent);
        _setPropertyCommand = CreateAsyncCommand(SetPropertyAsync, CanEditProperty);
        _removePropertyCommand = CreateAsyncCommand(RemovePropertyAsync, CanEditProperty);
        _commitInspectorPropertyCommand = CreateAsyncCommand(CommitInspectorPropertyAsync, parameter =>
            parameter is RekallAgeStudioInspectorPropertyEditorModel row && CanCommitInspectorProperty(row));
        _resetInspectorPropertyCommand = CreateAsyncCommand(ResetInspectorPropertyAsync, parameter =>
            parameter is RekallAgeStudioInspectorPropertyEditorModel row && CanResetInspectorProperty(row));
        _validateCommand = CreateAsyncCommand(ValidateAsync, HasOpenProject);
        _captureCommand = CreateAsyncCommand(CaptureAsync, HasEditableProject);
        _attachQualityProfileCommand = CreateAsyncCommand(AttachQualityProfileAsync, CanAttachQualityProfile);
        _applyQualityCommand = CreateAsyncCommand(() => RunAsync(ApplyRenderQualityAsync), HasQualityProfile);
        _captureQualityCommand = CreateAsyncCommand(
            () => RunRenderingAsync(CaptureQualityAsync),
            HasEditableProject);
        _compareQualityCommand = CreateAsyncCommand(
            () => RunRenderingAsync(CompareQualityAsync),
            CanCompareQuality);
        _simulateCommand = CreateAsyncCommand(StartSimulationAsync, () => HasOpenProject() && Mode == RekallAgeStudioMode.Edit);
        _pauseSimulationCommand = CreateAsyncCommand(ToggleSimulationPauseAsync, () => !IsBusy && IsSimulating);
        _stepSimulationCommand = CreateAsyncCommand(StepSimulationAsync, () => !IsBusy && IsSimulating && IsSimulationPaused);
        _playCommand = CreateAsyncCommand(PlayAsync, () => HasOpenProject() && Mode == RekallAgeStudioMode.Edit);
        _stopCommand = CreateAsyncCommand(StopAsync, () => !IsBusy && Mode != RekallAgeStudioMode.Edit);
        _switchSceneCommand = CreateAsyncCommand(SwitchSceneAsync, CanSwitchScene);
        _packageCommand = CreateAsyncCommand(PackageAsync, HasOpenProject);
        _auditPackageCommand = CreateAsyncCommand(AuditPackageAsync, CanAuditPackage);
        _openPackageFolderCommand = CreateAsyncCommand(OpenPackageFolderAsync, CanOpenPackageFolder);
        _publishWebCommand = CreateAsyncCommand(PublishWebAsync, HasOpenProject);
        _auditWebCommand = CreateAsyncCommand(AuditWebAsync, CanAuditWeb);
        _undoCommand = CreateAsyncCommand(UndoAsync, () => HasEditableProject() && _session.CanUndoSinceOpen);
        _redoCommand = CreateAsyncCommand(RedoAsync, () => HasEditableProject() && _session.CanRedo);
        _discoverModelsCommand = CreateAsyncCommand(
            () => DiscoverModelsAsync(),
            () => !IsBusy && !IsAgentRunning && _languageModelRunner is not null);
        _signInCodexCommand = CreateAsyncCommand(SignInCodexAsync,
            () => !IsBusy && !IsAgentRunning && _activeCodexSignIn is not { IsCompleted: false }
                && SelectedLanguageModelProvider.Id == "codex" && _languageModelRunner is IRekallAgeCodexProjectAgentRunner);
        _cancelCodexSignInCommand = CreateAsyncCommand(CancelCodexSignInAsync,
            () => _activeCodexSignIn is { IsCompleted: false });
        _runAgentCommand = CreateAsyncCommand(RunAgentAsync, CanRunAgent);
        _cancelAgentCommand = CreateAsyncCommand(CancelAgentAsync, () => IsAgentRunning);
        _refreshCodeCommand = CreateAsyncCommand(RefreshCodeSourcesAsync, () => HasOpenProject() && !IsCodeDirty);
        _saveCodeCommand = CreateAsyncCommand(SaveCodeSourceAsync, () => HasOpenProject() && IsCodeDirty);
        _buildCodeCommand = CreateAsyncCommand(BuildCodeAsync, () => HasOpenProject() && !IsCodeDirty);
        _createAttachCodeComponentCommand = CreateAsyncCommand(
            CreateAttachCodeComponentAsync,
            CanCreateAttachCodeComponent);
        _openCodeFileCommand = CreateAsyncCommand(OpenCodeFileAsync, () => SelectedCodeSource is not null);
        _openCodeProjectCommand = CreateAsyncCommand(OpenCodeProjectAsync, () => SelectedCodeSource is not null);
        _openCodeSolutionCommand = CreateAsyncCommand(OpenCodeSolutionAsync, HasOpenProject);
        _openCodeInVsCodeCommand = CreateAsyncCommand(OpenCodeInVsCodeAsync, HasOpenProject);
        _refreshMeshAssetsCommand = CreateAsyncCommand(RefreshMeshAssetsAsync, HasOpenProject);
        _createMeshPrimitiveCommand = CreateAsyncCommand(CreateMeshPrimitiveAsync, CanCreateMeshPrimitive);
        _openMeshAssetCommand = CreateAsyncCommand(OpenMeshAssetAsync, CanOpenMeshAsset);
        _frameSelectedMeshViewportCommand = CreateAsyncCommand(
            () => { FrameSelectedMeshViewport(); return Task.CompletedTask; },
            () => _meshViewportFrame is not null);
        _toggleMeshViewportProjectionCommand = CreateAsyncCommand(
            () => { ToggleMeshViewportProjection(); return Task.CompletedTask; },
            () => _meshViewportFrame is not null);
        _publishModelCommand = CreateAsyncCommand(PublishModelAsync, CanPublishModel);
        _placeModelCommand = CreateAsyncCommand(PlaceModelAsync, CanPlaceModel);
        _publishAndPlaceModelCommand = CreateAsyncCommand(PublishAndPlaceModelAsync, CanPublishAndPlaceModel);
        _selectMeshElementCommand = CreateAsyncCommand(SelectMeshElementAsync, CanSelectMeshElement);
        _clearMeshSelectionCommand = CreateAsyncCommand(ClearMeshSelectionAsync, HasOpenMesh);
        _previewMeshOperationCommand = CreateAsyncCommand(PreviewMeshOperationAsync, CanRunMeshOperation);
        _applyMeshOperationCommand = CreateAsyncCommand(ApplyMeshOperationAsync, CanRunMeshOperation);
        _cancelMeshPreviewCommand = CreateAsyncCommand(CancelMeshPreviewAsync, () => HasOpenMesh() && _modeling.Preview is not null);
        _refreshModelingGraphsCommand = CreateAsyncCommand(RefreshModelingGraphsAsync, HasOpenProject);
        _openModelingGraphCommand = CreateAsyncCommand(OpenModelingGraphAsync, CanOpenModelingGraph);
        _evaluateModelingGraphCommand = CreateAsyncCommand(EvaluateModelingGraphAsync, CanEvaluateModelingGraph);
        _applyModelingGraphParametersCommand = CreateAsyncCommand(ApplyModelingGraphParametersAsync, CanApplyModelingGraphParameters);
        _refreshMaterialGraphsCommand = CreateAsyncCommand(RefreshMaterialGraphsAsync, HasOpenProject);
        _openMaterialGraphCommand = CreateAsyncCommand(OpenMaterialGraphAsync, CanOpenMaterialGraph);
        _applyMaterialGraphParametersCommand = CreateAsyncCommand(ApplyMaterialGraphParametersAsync, CanApplyMaterialGraphParameters);
        _openAnimationMixerCommand = CreateAsyncCommand(OpenAnimationMixerAsync, CanOpenAnimationMixer);
        _applyAnimationMixerLayersCommand = CreateAsyncCommand(ApplyAnimationMixerLayersAsync, CanApplyAnimationMixerLayers);
        _addAnimationMixerLayerCommand = CreateAsyncCommand(
            () => { AnimationMixerLayers.Add(new RekallAgeStudioAnimationMixerLayerModel("new-layer", string.Empty, "1", "loop", "1")); RefreshCommands(); return Task.CompletedTask; },
            () => AnimationMixerIsOpen);
        RefreshMeshEditingState();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<RekallAgeSceneEntityNode> EntityNodes { get; } = [];
    public ObservableCollection<string> SceneNames { get; } = [];
    public ObservableCollection<string> InspectorLines { get; } = [];
    public ObservableCollection<RekallAgeInspectorComponentModel> InspectorComponents { get; } = [];
    public ObservableCollection<RekallAgeStudioInspectorComponentEditorModel> InspectorComponentEditors { get; } = [];
    public ObservableCollection<RekallAgeStudioInspectorPropertyEditorModel> InspectorPropertyEditors { get; } = [];
    public ObservableCollection<string> AssetLines { get; } = [];
    public ObservableCollection<RekallAgeContentBrowserItem> ContentItems { get; } = [];
    public ObservableCollection<RekallAgeContentBrowserItem> FilteredContentItems { get; } = [];
    public ObservableCollection<RekallAgeStudioContentCardModel> ContentCards { get; } = [];
    public ObservableCollection<RekallAgeStudioContentCardModel> FilteredContentCards { get; } = [];
    public ObservableCollection<RekallAgeContentBrowserWarning> ContentWarnings { get; } = [];
    public ObservableCollection<RekallAgeStudioContentImportJob> ImportJobs => _contentImportSession.Jobs;
    public ObservableCollection<string> ContentCategories { get; } = ["All"];

    public string SelectedContentCategory
    {
        get => _selectedContentCategory;
        set
        {
            if (Set(ref _selectedContentCategory, string.IsNullOrWhiteSpace(value) ? "All" : value))
                RefreshContentProjection();
        }
    }

    public string ContentSearchText
    {
        get => _contentSearchText;
        set
        {
            if (Set(ref _contentSearchText, value ?? string.Empty)) RefreshContentProjection();
        }
    }

    public bool HasActiveContentImports
    {
        get => _hasActiveContentImports;
        private set
        {
            if (Set(ref _hasActiveContentImports, value)) RefreshCommands();
        }
    }

    public string ContentImportSummary => ImportJobs.Count == 0
        ? "No content imports."
        : $"{ImportJobs.Count(job => job.Status == "Succeeded")} imported, {ImportJobs.Count(job => job.Status is "Failed" or "Rejected")} not imported.";

    public RekallAgeContentBrowserItem? SelectedContentItem
    {
        get => _selectedContentItem;
        set
        {
            if (Set(ref _selectedContentItem, value))
            {
                RefreshCommands();
                BeginSelectedContentPreview(value);
                OnPropertyChanged(nameof(SelectedContentCard));
            }
        }
    }

    public RekallAgeStudioContentCardModel? SelectedContentCard
    {
        get => SelectedContentItem is null ? null : ContentCards.FirstOrDefault(card =>
            card.Key == RekallAgeStudioContentKey.From(SelectedContentItem));
        set => SelectedContentItem = value?.Item;
    }

    public RekallAgeStudioContentPreview? SelectedContentPreview
    {
        get => _selectedContentPreview;
        private set => Set(ref _selectedContentPreview, value);
    }

    public string ContentStatusText
    {
        get => _contentStatusText;
        private set => Set(ref _contentStatusText, value);
    }
    public string SelectedStudioWorkspace
    {
        get => _selectedStudioWorkspace;
        set => Set(ref _selectedStudioWorkspace, value);
    }
    public string SelectedModelingSurface
    {
        get => _selectedModelingSurface;
        set => Set(ref _selectedModelingSurface, value);
    }
    public ObservableCollection<string> ValidationLines { get; } = [];
    public ObservableCollection<string> TransactionLines { get; } = [];
    public ObservableCollection<string> ImportLines { get; } = [];
    public ObservableCollection<string> SceneSummaryLines { get; } = [];
    public ObservableCollection<string> ActionLines { get; } = [];
    public ObservableCollection<string> RuntimeObservationLines { get; } = [];
    public ObservableCollection<RekallAgeModuleSourceInfo> CodeSources { get; } = [];
    public ObservableCollection<string> CodeOutputLines { get; } = [];
    public IReadOnlyList<RekallAgeLanguageModelProviderDescriptor> LanguageModelProviders =>
        _languageModelProviderCatalog.DescribeProviders(SessionLanguageModelProviderSettings());
    public ObservableCollection<string> LanguageModels { get; } = [];
    public IReadOnlyList<string> ReasoningEfforts { get; } =
        ["none", "low", "medium", "high", "xhigh", "max"];
    public ObservableCollection<string> AgentLines { get; } = [];

    public ObservableCollection<string> AgentMessageLines { get; } = [];
    public IReadOnlyList<string> PackageTargets { get; } =
        [RekallAgePlayablePackageTargets.Windows, RekallAgePlayablePackageTargets.Headless];
    public ObservableCollection<string> MeshAssetIds { get; } = [];
    public ObservableCollection<ulong> MeshElementIds { get; } = [];
    public ObservableCollection<string> MeshOperationIds { get; } = [];
    public ObservableCollection<string> MeshSelectionLines { get; } = [];
    public ObservableCollection<string> MeshDiagnosticLines { get; } = [];
    public ObservableCollection<RekallAgeStudioMeshParameterModel> MeshParameterEditors { get; } = [];
    public ObservableCollection<string> ModelingGraphAssetIds { get; } = [];
    public ObservableCollection<string> ModelingGraphOutputNames { get; } = [];
    public ObservableCollection<RekallAgeStudioModelingGraphNodeView> ModelingGraphNodes { get; } = [];
    public ObservableCollection<string> ModelingGraphDiagnosticLines { get; } = [];
    public ObservableCollection<RekallAgeStudioModelingGraphParameterModel> ModelingGraphParameterEditors { get; } = [];
    public ObservableCollection<string> MaterialGraphAssetIds { get; } = [];
    public ObservableCollection<RekallAgeStudioMaterialGraphNodeView> MaterialGraphNodes { get; } = [];
    public ObservableCollection<RekallAgeStudioMaterialGraphParameterModel> MaterialGraphParameterEditors { get; } = [];
    public ObservableCollection<string> MeshAttributeSummaries { get; } = [];
    public ObservableCollection<string> MeshMaterialSlotSummaries { get; } = [];
    public ObservableCollection<RekallAgeStudioAnimationMixerLayerModel> AnimationMixerLayers { get; } = [];
    public IReadOnlyList<string> AnimationMixerLoopModes { get; } = ["loop", "pingpong", "clamp"];
    public IReadOnlyList<RekallAgeGeometryDomain> MeshEditDomains { get; } =
        [RekallAgeGeometryDomain.Point, RekallAgeGeometryDomain.Edge, RekallAgeGeometryDomain.Face, RekallAgeGeometryDomain.Corner];
    public IReadOnlyList<RekallAgeLanguageModelToolExecution> LastAgentToolExecutions => _lastAgentToolExecutions;
    public ObservableCollection<RekallAgeInspectorComponentSchemaModel> ComponentSchemas { get; } = [];
    public ObservableCollection<RekallAgeInspectorPropertySchemaModel> PropertySchemas { get; } = [];
    public ObservableCollection<string> PropertyValueChoices { get; } = [];
    public ObservableCollection<RekallAgeWorkbenchRenderPassTimingModel> RenderPassTimings { get; } = [];
    public ObservableCollection<RekallAgeWorkbenchRenderResourceModel> RenderResources { get; } = [];
    public ObservableCollection<RekallAgeWorkbenchRenderDegradationModel> RenderDegradations { get; } = [];
    public ObservableCollection<RekallAgeWorkbenchRenderQualityComparisonModel> RenderQualityComparisons { get; } = [];
    public ObservableCollection<RekallAgeWorkbenchRenderDebugViewModel> RenderDebugViews { get; } = [];
    public ObservableCollection<string> RenderSuggestedActions { get; } = [];
    public IReadOnlyList<string> QualityPresets { get; } =
        ["Performance", "Low", "Medium", "High", "Ultra", "Epic"];
    public IReadOnlyList<string> QualityFogModes { get; } =
        ["analytic", "froxel-low", "froxel", "froxel-high", "froxel-epic"];

    public ICommand OpenCommand => _openCommand;
    public ICommand CreateCommand => _createCommand;
    public ICommand OpenSelectedContentCommand => _openSelectedContentCommand;
    public ICommand ImportContentCommand => _importContentCommand;
    public ICommand AddEntityCommand => _addEntityCommand;
    public ICommand RenameEntityCommand => _renameEntityCommand;
    public ICommand DuplicateEntityCommand => _duplicateEntityCommand;
    public ICommand DeleteEntityCommand => _deleteEntityCommand;
    public ICommand ToggleEntityVisibleCommand => _toggleEntityVisibleCommand;
    public ICommand ToggleEntityLockedCommand => _toggleEntityLockedCommand;
    public ICommand ReparentEntityCommand => _reparentEntityCommand;
    public ICommand ClearEntityParentCommand => _clearEntityParentCommand;
    public ICommand AddComponentCommand => _addComponentCommand;
    public ICommand RemoveComponentCommand => _removeComponentCommand;
    public ICommand SetPropertyCommand => _setPropertyCommand;
    public ICommand RemovePropertyCommand => _removePropertyCommand;
    public ICommand CommitInspectorPropertyCommand => _commitInspectorPropertyCommand;
    public ICommand ResetInspectorPropertyCommand => _resetInspectorPropertyCommand;
    public ICommand ValidateCommand => _validateCommand;
    public ICommand CaptureCommand => _captureCommand;
    public ICommand AttachQualityProfileCommand => _attachQualityProfileCommand;
    public ICommand ApplyQualityCommand => _applyQualityCommand;
    public ICommand CaptureQualityCommand => _captureQualityCommand;
    public ICommand CompareQualityCommand => _compareQualityCommand;
    public ICommand SimulateCommand => _simulateCommand;
    public ICommand PauseSimulationCommand => _pauseSimulationCommand;
    public ICommand StepSimulationCommand => _stepSimulationCommand;
    public ICommand PlayCommand => _playCommand;
    public ICommand StopCommand => _stopCommand;
    public ICommand SwitchSceneCommand => _switchSceneCommand;
    public ICommand PackageCommand => _packageCommand;
    public ICommand AuditPackageCommand => _auditPackageCommand;
    public ICommand OpenPackageFolderCommand => _openPackageFolderCommand;
    public ICommand PublishWebCommand => _publishWebCommand;
    public ICommand AuditWebCommand => _auditWebCommand;
    public ICommand UndoCommand => _undoCommand;
    public ICommand RedoCommand => _redoCommand;
    public ICommand RefreshLanguageModelsCommand => _discoverModelsCommand;

    public ICommand SignInCodexCommand => _signInCodexCommand;

    public ICommand CancelCodexSignInCommand => _cancelCodexSignInCommand;
    public ICommand RunAgentCommand => _runAgentCommand;
    public ICommand CancelAgentCommand => _cancelAgentCommand;
    public ICommand RefreshCodeCommand => _refreshCodeCommand;
    public ICommand SaveCodeCommand => _saveCodeCommand;
    public ICommand BuildCodeCommand => _buildCodeCommand;
    public ICommand CreateAttachCodeComponentCommand => _createAttachCodeComponentCommand;
    public ICommand OpenCodeFileCommand => _openCodeFileCommand;
    public ICommand OpenCodeProjectCommand => _openCodeProjectCommand;
    public ICommand OpenCodeSolutionCommand => _openCodeSolutionCommand;
    public ICommand OpenCodeInVsCodeCommand => _openCodeInVsCodeCommand;
    public ICommand RefreshMeshAssetsCommand => _refreshMeshAssetsCommand;
    public ICommand CreateMeshPrimitiveCommand => _createMeshPrimitiveCommand;
    public ICommand OpenMeshAssetCommand => _openMeshAssetCommand;

    public ICommand FrameSelectedMeshViewportCommand => _frameSelectedMeshViewportCommand;

    public ICommand ToggleMeshViewportProjectionCommand => _toggleMeshViewportProjectionCommand;
    public ICommand PublishModelCommand => _publishModelCommand;
    public ICommand PlaceModelCommand => _placeModelCommand;
    public ICommand PublishAndPlaceModelCommand => _publishAndPlaceModelCommand;
    public ICommand SelectMeshElementCommand => _selectMeshElementCommand;
    public ICommand ClearMeshSelectionCommand => _clearMeshSelectionCommand;
    public ICommand PreviewMeshOperationCommand => _previewMeshOperationCommand;
    public ICommand ApplyMeshOperationCommand => _applyMeshOperationCommand;
    public ICommand CancelMeshPreviewCommand => _cancelMeshPreviewCommand;
    public ICommand RefreshModelingGraphsCommand => _refreshModelingGraphsCommand;
    public ICommand OpenModelingGraphCommand => _openModelingGraphCommand;
    public ICommand EvaluateModelingGraphCommand => _evaluateModelingGraphCommand;
    public ICommand ApplyModelingGraphParametersCommand => _applyModelingGraphParametersCommand;
    public ICommand RefreshMaterialGraphsCommand => _refreshMaterialGraphsCommand;
    public ICommand OpenMaterialGraphCommand => _openMaterialGraphCommand;
    public ICommand ApplyMaterialGraphParametersCommand => _applyMaterialGraphParametersCommand;
    public ICommand OpenAnimationMixerCommand => _openAnimationMixerCommand;
    public ICommand ApplyAnimationMixerLayersCommand => _applyAnimationMixerLayersCommand;
    public ICommand AddAnimationMixerLayerCommand => _addAnimationMixerLayerCommand;

    public string SelectedQualityPreset
    {
        get => _selectedQualityPreset;
        set
        {
            if (Set(ref _selectedQualityPreset, value)) RefreshCommands();
        }
    }

    public string ComparisonQualityPreset
    {
        get => _comparisonQualityPreset;
        set
        {
            if (Set(ref _comparisonQualityPreset, value)) RefreshCommands();
        }
    }

    public string QualityResolutionScaleInput
    {
        get => _qualityResolutionScaleInput;
        set
        {
            if (Set(ref _qualityResolutionScaleInput, value)) RefreshCommands();
        }
    }

    public string QualityShadowCascadeCountInput
    {
        get => _qualityShadowCascadeCountInput;
        set
        {
            if (Set(ref _qualityShadowCascadeCountInput, value)) RefreshCommands();
        }
    }

    public string QualityShadowResolutionInput
    {
        get => _qualityShadowResolutionInput;
        set
        {
            if (Set(ref _qualityShadowResolutionInput, value)) RefreshCommands();
        }
    }

    public string QualityFogModeInput
    {
        get => _qualityFogModeInput;
        set
        {
            if (Set(ref _qualityFogModeInput, value)) RefreshCommands();
        }
    }

    public bool? QualityBloomOverride
    {
        get => _qualityBloomOverride;
        set
        {
            if (Set(ref _qualityBloomOverride, value)) RefreshCommands();
        }
    }

    public bool? QualitySsaoOverride
    {
        get => _qualitySsaoOverride;
        set
        {
            if (Set(ref _qualitySsaoOverride, value)) RefreshCommands();
        }
    }

    public string QualityMaximumActiveParticlesInput
    {
        get => _qualityMaximumActiveParticlesInput;
        set
        {
            if (Set(ref _qualityMaximumActiveParticlesInput, value)) RefreshCommands();
        }
    }

    public string RequestedQualityPreset
    {
        get => _requestedQualityPreset;
        private set => Set(ref _requestedQualityPreset, value);
    }

    public string ResolvedQualityPreset
    {
        get => _resolvedQualityPreset;
        private set => Set(ref _resolvedQualityPreset, value);
    }

    public string OutputResolutionText
    {
        get => _outputResolutionText;
        private set => Set(ref _outputResolutionText, value);
    }

    public string InternalResolutionText
    {
        get => _internalResolutionText;
        private set => Set(ref _internalResolutionText, value);
    }

    public string TotalGpuMillisecondsText
    {
        get => _totalGpuMillisecondsText;
        private set => Set(ref _totalGpuMillisecondsText, value);
    }

    public string GpuTimingStatusText
    {
        get => _gpuTimingStatusText;
        private set => Set(ref _gpuTimingStatusText, value);
    }

    public string RenderWorkloadText
    {
        get => _renderWorkloadText;
        private set => Set(ref _renderWorkloadText, value);
    }

    public RekallAgeWorkbenchRenderDebugViewModel? SelectedRenderDebugView
    {
        get => _selectedRenderDebugView;
        set
        {
            if (!Set(ref _selectedRenderDebugView, value) || value is null || !File.Exists(value.OutputPath)) return;
            ViewportSummary = $"{value.Label} · {(value.NonBlank ? "nonblank" : "blank")}";
        }
    }

    public string ProjectPathInput
    {
        get => _projectPathInput;
        set
        {
            if (Set(ref _projectPathInput, value))
            {
                OnPropertyChanged(nameof(ProjectContextText));
                RefreshCommands();
            }
        }
    }

    public string ProjectContextText => _currentModel?.Project.Name
        ?? (string.IsNullOrWhiteSpace(ProjectPathInput)
            ? "No project open"
            : Path.GetFileName(Path.TrimEndingDirectorySeparator(ProjectPathInput)));

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
                SynchronizeInspectorSelectionToComponentType();
                RefreshCommands();
            }
        }
    }

    public string InspectorSearchInput
    {
        get => _inspectorSearchInput;
        set
        {
            if (Set(ref _inspectorSearchInput, value)) RefreshInspectorComponents();
        }
    }

    public RekallAgeInspectorComponentModel? SelectedInspectorComponent
    {
        get => _selectedInspectorComponent;
        set
        {
            if (!Set(ref _selectedInspectorComponent, value)) return;
            OnPropertyChanged(nameof(SelectedInspectorComponentDescription));
            if (value is not null && !ComponentTypeInput.Equals(value.Type, StringComparison.Ordinal))
            {
                ComponentTypeInput = value.Type;
            }
        }
    }

    public string InspectorSelectionName => _currentModel?.Inspector.SelectedEntityName ?? "No entity selected";

    public string InspectorSelectionId => _currentModel?.Inspector.SelectedEntityId ?? "No stable entity ID";

    public string InspectorComponentCountText
    {
        get
        {
            var count = _currentModel?.Inspector.Components.Count ?? 0;
            return count == 1 ? "1 component" : $"{count} components";
        }
    }

    public string InspectorComponentBrowserEmptyText => !HasInspectorSelection
        ? InspectorEmptyStateText
        : (_currentModel?.Inspector.Components.Count ?? 0) == 0
            ? "This entity has no attached components. Add one below."
            : $"No attached components match ‘{InspectorSearchInput.Trim()}’.";

    public string SelectedInspectorComponentDescription => SelectedInspectorComponent?.Description
        ?? ComponentSchemas.FirstOrDefault(schema => schema.Type.Equals(ComponentTypeInput, StringComparison.Ordinal))?.Description
        ?? "Custom or unregistered component. Properties remain editable as JSON.";

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

    public RekallAgeLanguageModelProviderDescriptor SelectedLanguageModelProvider
    {
        get => _selectedLanguageModelProvider;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            lock (_languageModelLifecycleSync)
            {
                if (!LanguageModelProviders.Any(provider => provider.Id == value.Id)
                    || !Set(ref _selectedLanguageModelProvider, value)) return;
                _codexSignInCancellation?.Cancel();
                Replace(LanguageModels, []);
                SelectedLanguageModel = string.Empty;
                ProviderStatus = $"Switching to {value.DisplayName}…";
                ProviderDisplayStatus = ProviderStatus;
                OnPropertyChanged(nameof(IsOllamaSelected));
                OnPropertyChanged(nameof(IsGgufSelected));
                OnPropertyChanged(nameof(IsKimiSelected));
                OnPropertyChanged(nameof(IsOpenAiSelected));
                OnPropertyChanged(nameof(IsCodexSelected));
                _localModelRuntimeReady = false;
                OnPropertyChanged(nameof(CanBrowseGguf));
                QueueLanguageModelProviderTransition(value);
            }
            RefreshCommands();
        }
    }

    public string SelectedLanguageModel
    {
        get => _selectedLanguageModel;
        set
        {
            var candidate = value ?? string.Empty;
            if (candidate.Length > 0 && !LanguageModels.Contains(candidate, StringComparer.Ordinal)) return;
            if (Set(ref _selectedLanguageModel, candidate))
            {
                if (candidate.Length > 0)
                {
                    ProviderDisplayStatus = $"Using {candidate} with {SelectedLanguageModelProvider.DisplayName}.";
                }
                OnPropertyChanged(nameof(HasUsableLanguageModel));
                RefreshCommands();
            }
        }
    }

    internal void SelectAutomationLanguageModel(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (Set(ref _selectedLanguageModel, model.Trim()))
        {
            OnPropertyChanged(nameof(HasUsableLanguageModel));
            RefreshCommands();
        }
    }

    public bool IsOllamaSelected => SelectedLanguageModelProvider.Id == "ollama";

    public bool IsGgufSelected => SelectedLanguageModelProvider.Id == "gguf";

    public bool IsKimiSelected => SelectedLanguageModelProvider.Id == "kimi";

    public bool IsOpenAiSelected => SelectedLanguageModelProvider.Id == "openai";

    public bool IsCodexSelected => SelectedLanguageModelProvider.Id == "codex";

    public bool PreapproveAllCodexActionsForSession
    {
        get => _preapproveAllCodexActionsForSession;
        set => Set(ref _preapproveAllCodexActionsForSession, value);
    }

    public bool CanBrowseGguf => RekallAgeStudioLocalModelReadiness.CanBrowseGguf(
        SelectedLanguageModelProvider.Id,
        _localModelRuntimeReady);

    public bool HasUsableLanguageModel => LanguageModels.Contains(SelectedLanguageModel, StringComparer.Ordinal);

    public string ProviderStatus
    {
        get => _providerStatus;
        private set => Set(ref _providerStatus, value);
    }

    public string ProviderDisplayStatus
    {
        get => _providerDisplayStatus;
        private set => Set(ref _providerDisplayStatus, value);
    }

    public bool HasSessionOpenAiCredential => _sessionOpenAiApiKey is not null;

    public bool HasSessionKimiCredential => _sessionKimiApiKey is not null;

    public string OpenAiCredentialSourceLabel => HasSessionOpenAiCredential
        ? "Session key configured in memory"
        : "No session key; Studio may use OPENAI_API_KEY or a remembered protected key";

    public string KimiCredentialSourceLabel => HasSessionKimiCredential
        ? "Session key configured in memory"
        : "No session key; Studio may use KIMI_API_KEY, MOONSHOT_API_KEY, or a remembered protected key";

    internal bool LanguageModelSetupAllowsAuthoring => _languageModelSetupAllowsAuthoring;

    internal bool LanguageModelSetupBusy => _languageModelSetupBusy;

    internal void SetLanguageModelSetupBusy(bool busy)
    {
        if (_languageModelSetupBusy == busy) return;
        _languageModelSetupBusy = busy;
        RefreshCommands();
    }

    internal void SetLanguageModelSetupAvailability(bool allowsAuthoring)
    {
        if (_languageModelSetupAllowsAuthoring == allowsAuthoring) return;
        _languageModelSetupAllowsAuthoring = allowsAuthoring;
        RefreshCommands();
    }

    internal void SetLocalModelPrerequisiteAvailability(bool available)
    {
        if (_localModelRuntimeReady == available) return;
        _localModelRuntimeReady = available;
        OnPropertyChanged(nameof(CanBrowseGguf));
    }

    internal void SetLocalModelPrerequisiteReadiness(RekallAgeLanguageModelReadinessResult readiness)
    {
        ArgumentNullException.ThrowIfNull(readiness);
        SetLocalModelPrerequisiteAvailability(
            RekallAgeStudioLocalModelReadiness.CanBrowseGguf(readiness.ProviderId, readiness.Checks));
    }

    public RekallAgeCodexApprovalCallback? CodexApprovalHandler { get; set; }

    public RekallAgeCodexAuthenticationLauncher? CodexAuthenticationLauncher { get; set; }

    internal ValueTask<RekallAgeCodexApprovalDecision> RouteCodexApprovalAsync(
        RekallAgeCodexApprovalRequest request,
        CancellationToken cancellationToken) =>
        CodexApprovalHandler?.Invoke(request, cancellationToken)
        ?? ValueTask.FromResult(RekallAgeCodexApprovalDecision.Decline);

    public string SelectedReasoningEffort
    {
        get => _selectedReasoningEffort;
        set
        {
            if (!ReasoningEfforts.Contains(value, StringComparer.Ordinal)) return;
            Set(ref _selectedReasoningEffort, value);
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

    public string EntityNameInput
    {
        get => _entityNameInput;
        set
        {
            if (Set(ref _entityNameInput, value)) RefreshCommands();
        }
    }

    public string ParentEntityIdInput
    {
        get => _parentEntityIdInput;
        set
        {
            if (Set(ref _parentEntityIdInput, value)) RefreshCommands();
        }
    }

    public string? SelectedMeshAssetId
    {
        get => _selectedMeshAssetId;
        set
        {
            if (!Set(ref _selectedMeshAssetId, value)) return;
            if (!string.IsNullOrWhiteSpace(value))
            {
                var displayName = HumanizeAssetId(value);
                ModelAssetIdInput = value;
                ModelAssetDisplayNameInput = displayName;
                ModelEntityIdInput = value + "-instance";
                ModelEntityNameInput = displayName;
            }
            RefreshCommands();
        }
    }

    public string ModelAssetIdInput { get => _modelAssetIdInput; set { if (Set(ref _modelAssetIdInput, value)) RefreshCommands(); } }
    public string ModelAssetDisplayNameInput { get => _modelAssetDisplayNameInput; set { if (Set(ref _modelAssetDisplayNameInput, value)) RefreshCommands(); } }
    public string ModelEntityIdInput { get => _modelEntityIdInput; set { if (Set(ref _modelEntityIdInput, value)) RefreshCommands(); } }
    public string ModelEntityNameInput { get => _modelEntityNameInput; set => Set(ref _modelEntityNameInput, value); }
    public string ModelPlacementParentEntityIdInput { get => _modelPlacementParentEntityIdInput; set => Set(ref _modelPlacementParentEntityIdInput, value); }
    public double ModelPositionX { get => _modelPositionX; set => SetModelNumber(ref _modelPositionX, ref _modelPositionXInput, value, nameof(ModelPositionX), nameof(ModelPositionXInput)); }
    public double ModelPositionY { get => _modelPositionY; set => SetModelNumber(ref _modelPositionY, ref _modelPositionYInput, value, nameof(ModelPositionY), nameof(ModelPositionYInput)); }
    public double ModelPositionZ { get => _modelPositionZ; set => SetModelNumber(ref _modelPositionZ, ref _modelPositionZInput, value, nameof(ModelPositionZ), nameof(ModelPositionZInput)); }
    public double ModelRotationX { get => _modelRotationX; set => SetModelNumber(ref _modelRotationX, ref _modelRotationXInput, value, nameof(ModelRotationX), nameof(ModelRotationXInput)); }
    public double ModelRotationY { get => _modelRotationY; set => SetModelNumber(ref _modelRotationY, ref _modelRotationYInput, value, nameof(ModelRotationY), nameof(ModelRotationYInput)); }
    public double ModelRotationZ { get => _modelRotationZ; set => SetModelNumber(ref _modelRotationZ, ref _modelRotationZInput, value, nameof(ModelRotationZ), nameof(ModelRotationZInput)); }
    public double ModelScaleX { get => _modelScaleX; set => SetModelNumber(ref _modelScaleX, ref _modelScaleXInput, value, nameof(ModelScaleX), nameof(ModelScaleXInput)); }
    public double ModelScaleY { get => _modelScaleY; set => SetModelNumber(ref _modelScaleY, ref _modelScaleYInput, value, nameof(ModelScaleY), nameof(ModelScaleYInput)); }
    public double ModelScaleZ { get => _modelScaleZ; set => SetModelNumber(ref _modelScaleZ, ref _modelScaleZInput, value, nameof(ModelScaleZ), nameof(ModelScaleZInput)); }
    public string ModelPositionXInput { get => _modelPositionXInput; set => SetModelNumberInput(ref _modelPositionXInput, ref _modelPositionX, value, nameof(ModelPositionXInput), nameof(ModelPositionX)); }
    public string ModelPositionYInput { get => _modelPositionYInput; set => SetModelNumberInput(ref _modelPositionYInput, ref _modelPositionY, value, nameof(ModelPositionYInput), nameof(ModelPositionY)); }
    public string ModelPositionZInput { get => _modelPositionZInput; set => SetModelNumberInput(ref _modelPositionZInput, ref _modelPositionZ, value, nameof(ModelPositionZInput), nameof(ModelPositionZ)); }
    public string ModelRotationXInput { get => _modelRotationXInput; set => SetModelNumberInput(ref _modelRotationXInput, ref _modelRotationX, value, nameof(ModelRotationXInput), nameof(ModelRotationX)); }
    public string ModelRotationYInput { get => _modelRotationYInput; set => SetModelNumberInput(ref _modelRotationYInput, ref _modelRotationY, value, nameof(ModelRotationYInput), nameof(ModelRotationY)); }
    public string ModelRotationZInput { get => _modelRotationZInput; set => SetModelNumberInput(ref _modelRotationZInput, ref _modelRotationZ, value, nameof(ModelRotationZInput), nameof(ModelRotationZ)); }
    public string ModelScaleXInput { get => _modelScaleXInput; set => SetModelNumberInput(ref _modelScaleXInput, ref _modelScaleX, value, nameof(ModelScaleXInput), nameof(ModelScaleX)); }
    public string ModelScaleYInput { get => _modelScaleYInput; set => SetModelNumberInput(ref _modelScaleYInput, ref _modelScaleY, value, nameof(ModelScaleYInput), nameof(ModelScaleY)); }
    public string ModelScaleZInput { get => _modelScaleZInput; set => SetModelNumberInput(ref _modelScaleZInput, ref _modelScaleZ, value, nameof(ModelScaleZInput), nameof(ModelScaleZ)); }
    public string? LastPublishedModelAssetId { get => _lastPublishedModelAssetId; private set => Set(ref _lastPublishedModelAssetId, value); }
    public string? LastPlacedModelEntityId { get => _lastPlacedModelEntityId; private set => Set(ref _lastPlacedModelEntityId, value); }

    private static string HumanizeAssetId(string assetId) => string.Join(
        ' ',
        assetId.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private void SetModelNumber(
        ref double numberField,
        ref string inputField,
        double value,
        string numberProperty,
        string inputProperty)
    {
        Set(ref numberField, value, numberProperty);
        Set(ref inputField, value.ToString("R", CultureInfo.InvariantCulture), inputProperty);
        RefreshCommands();
    }

    private void SetModelNumberInput(
        ref string inputField,
        ref double numberField,
        string value,
        string inputProperty,
        string numberProperty)
    {
        if (!Set(ref inputField, value, inputProperty)) return;
        if (TryParseModelNumber(value, out var parsed)) Set(ref numberField, parsed, numberProperty);
        RefreshCommands();
    }

    private bool TryGetModelPlacement(
        out RekallAgePlacementVector3 position,
        out RekallAgePlacementVector3 rotation,
        out RekallAgePlacementVector3 scale)
    {
        var valid = TryParseModelNumber(ModelPositionXInput, out var positionX)
            & TryParseModelNumber(ModelPositionYInput, out var positionY)
            & TryParseModelNumber(ModelPositionZInput, out var positionZ)
            & TryParseModelNumber(ModelRotationXInput, out var rotationX)
            & TryParseModelNumber(ModelRotationYInput, out var rotationY)
            & TryParseModelNumber(ModelRotationZInput, out var rotationZ)
            & TryParseModelNumber(ModelScaleXInput, out var scaleX)
            & TryParseModelNumber(ModelScaleYInput, out var scaleY)
            & TryParseModelNumber(ModelScaleZInput, out var scaleZ);
        position = new(positionX, positionY, positionZ);
        rotation = new(rotationX, rotationY, rotationZ);
        scale = new(scaleX, scaleY, scaleZ);
        return valid;
    }

    private static bool TryParseModelNumber(string value, out double parsed) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
        || double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed);

    public IReadOnlyList<string> MeshPrimitiveTypes => _meshPrimitiveFactory.SupportedPrimitives;

    public string SelectedMeshPrimitive
    {
        get => _selectedMeshPrimitive;
        set
        {
            if (!Set(ref _selectedMeshPrimitive, value)) return;
            if (string.IsNullOrWhiteSpace(MeshPrimitiveAssetIdInput)
                || MeshPrimitiveAssetIdInput.StartsWith("mesh-", StringComparison.Ordinal))
            {
                MeshPrimitiveAssetIdInput = $"mesh-{value.Trim().ToLowerInvariant()}";
            }
            RefreshCommands();
        }
    }

    public string MeshPrimitiveAssetIdInput
    {
        get => _meshPrimitiveAssetIdInput;
        set
        {
            if (Set(ref _meshPrimitiveAssetIdInput, value)) RefreshCommands();
        }
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

    public IReadOnlyList<string> ViewportRenderStyles => RekallAgeStudioViewportRenderStyles.Labels;

    public string WorldViewportRenderStyle
    {
        get => _worldViewportRenderStyle;
        set
        {
            if (!Set(ref _worldViewportRenderStyle, value)) return;
            _previewSession.SetRenderStyle(RekallAgeStudioViewportRenderStyles.Parse(value));
        }
    }

    public string MeshViewportRenderStyle
    {
        get => _meshViewportRenderStyle;
        set
        {
            if (!Set(ref _meshViewportRenderStyle, value)) return;
            RefreshMeshEditingState();
        }
    }

    public string? SelectedModelingGraphAssetId
    {
        get => _selectedModelingGraphAssetId;
        set { if (Set(ref _selectedModelingGraphAssetId, value)) RefreshCommands(); }
    }

    public string? SelectedModelingGraphOutput
    {
        get => _selectedModelingGraphOutput;
        set { if (Set(ref _selectedModelingGraphOutput, value)) RefreshCommands(); }
    }

    public string ModelingGraphSummary
    {
        get => _modelingGraphSummary;
        private set => Set(ref _modelingGraphSummary, value);
    }

    public BitmapSource? ModelingGraphViewportImage
    {
        get => _modelingGraphViewportImage;
        private set => Set(ref _modelingGraphViewportImage, value);
    }

    public string ModelingGraphViewportRenderStyle
    {
        get => _modelingGraphViewportRenderStyle;
        set
        {
            if (!Set(ref _modelingGraphViewportRenderStyle, value)) return;
            RefreshModelingGraphOutputViewport();
        }
    }

    public RekallAgeStudioModelingGraphNodeView? SelectedModelingGraphNode
    {
        get => _selectedModelingGraphNode;
        set
        {
            if (!Set(ref _selectedModelingGraphNode, value)) return;
            RefreshModelingGraphParameterEditors();
        }
    }

    public string? SelectedMaterialGraphAssetId
    {
        get => _selectedMaterialGraphAssetId;
        set { if (Set(ref _selectedMaterialGraphAssetId, value)) RefreshCommands(); }
    }

    public string MaterialGraphSummary
    {
        get => _materialGraphSummary;
        private set => Set(ref _materialGraphSummary, value);
    }

    public RekallAgeStudioMaterialGraphNodeView? SelectedMaterialGraphNode
    {
        get => _selectedMaterialGraphNode;
        set
        {
            if (!Set(ref _selectedMaterialGraphNode, value)) return;
            RefreshMaterialGraphParameterEditors();
        }
    }

    public bool AnimationMixerIsOpen
    {
        get => _animationMixerIsOpen;
        private set { if (Set(ref _animationMixerIsOpen, value)) RefreshCommands(); }
    }

    public string AnimationMixerSummary
    {
        get => _animationMixerSummary;
        private set => Set(ref _animationMixerSummary, value);
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

    /// <summary>Orbits the mesh viewport camera by the given yaw/pitch deltas, in radians.</summary>
    public void OrbitMeshViewport(double deltaYaw, double deltaPitch)
    {
        _meshViewportCamera = _meshViewportCamera with
        {
            Yaw = _meshViewportCamera.Yaw + deltaYaw,
            Pitch = Math.Clamp(_meshViewportCamera.Pitch + deltaPitch, -Math.PI / 2 + 0.01, Math.PI / 2 - 0.01)
        };
        RefreshMeshEditingState();
    }

    /// <summary>Pans the mesh viewport camera by the given screen-space pixel deltas.</summary>
    public void PanMeshViewport(double deltaX, double deltaY)
    {
        _meshViewportCamera = _meshViewportCamera with
        {
            PanX = _meshViewportCamera.PanX + deltaX,
            PanY = _meshViewportCamera.PanY + deltaY
        };
        RefreshMeshEditingState();
    }

    /// <summary>Multiplies the mesh viewport camera's zoom by <paramref name="factor"/> (greater than 1 zooms in).</summary>
    public void ZoomMeshViewport(double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0) return;
        _meshViewportCamera = _meshViewportCamera with
        {
            Zoom = Math.Clamp(_meshViewportCamera.Zoom * factor, 0.05, 50)
        };
        RefreshMeshEditingState();
    }

    /// <summary>Toggles the mesh viewport camera between orthographic and perspective projection.</summary>
    public void ToggleMeshViewportProjection()
    {
        _meshViewportCamera = _meshViewportCamera with { Orthographic = !_meshViewportCamera.Orthographic };
        RefreshMeshEditingState();
    }

    /// <summary>Resets the mesh viewport camera's pan/zoom (keeping its current orbit angle), re-centering on the mesh.</summary>
    public void FrameSelectedMeshViewport()
    {
        _meshViewportCamera = _meshViewportCamera with { PanX = 0, PanY = 0, Zoom = 1 };
        RefreshMeshEditingState();
    }

    public string? LastPackagePath
    {
        get => _lastPackagePath;
        private set
        {
            if (Set(ref _lastPackagePath, value)) RefreshCommands();
        }
    }

    public string? LastPackageOutputDirectory
    {
        get => _lastPackageOutputDirectory;
        private set
        {
            if (Set(ref _lastPackageOutputDirectory, value)) RefreshCommands();
        }
    }

    public string? LastPackageLaunchPath
    {
        get => _lastPackageLaunchPath;
        private set => Set(ref _lastPackageLaunchPath, value);
    }

    public string SelectedPackageTarget
    {
        get => _selectedPackageTarget;
        set => Set(ref _selectedPackageTarget, value);
    }

    public string? LastWebPublishPath
    {
        get => _lastWebPublishPath;
        private set
        {
            if (Set(ref _lastWebPublishPath, value)) RefreshCommands();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public string AgentActivityText
    {
        get => _agentActivityText;
        private set => Set(ref _agentActivityText, value);
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

    public string ViewportBackendLabel
    {
        get => _viewportBackendLabel;
        private set => Set(ref _viewportBackendLabel, value);
    }

    public bool ViewportAvailable
    {
        get => _viewportAvailable;
        private set => Set(ref _viewportAvailable, value);
    }

    public string ViewportUnavailableReason
    {
        get => _viewportUnavailableReason;
        private set => Set(ref _viewportUnavailableReason, value);
    }

    public int ViewportRenderableCount
    {
        get => _viewportRenderableCount;
        private set => Set(ref _viewportRenderableCount, value);
    }

    internal bool LastCaptureNonblank
    {
        get => _lastCaptureNonblank;
        private set => Set(ref _lastCaptureNonblank, value);
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
    public bool IsSimulationPaused
    {
        get => _isSimulationPaused;
        private set
        {
            if (!Set(ref _isSimulationPaused, value)) return;
            OnPropertyChanged(nameof(PauseSimulationLabel));
            RefreshCommands();
        }
    }

    public string PauseSimulationLabel => IsSimulationPaused ? "Resume" : "Pause";
    public string? SelectedEntityId => _session.SelectedEntityId;

    public bool HasProject => _session.Model is not null;

    public bool HasInspectorSelection => SelectedEntityId is not null;

    public bool CanEditSelectedLinkedModel => SelectedLinkedModelAssetId() is not null;

    public string InspectorEmptyStateText => "Select an entity to inspect components.";

    public IReadOnlyList<RekallAgeStudioTransformTool> TransformTools { get; } =
        [RekallAgeStudioTransformTool.Select, RekallAgeStudioTransformTool.Move, RekallAgeStudioTransformTool.Rotate, RekallAgeStudioTransformTool.Scale];

    public IReadOnlyList<RekallAgeStudioTransformSpace> TransformSpaces { get; } =
        [RekallAgeStudioTransformSpace.World, RekallAgeStudioTransformSpace.Local];

    public RekallAgeStudioTransformTool TransformTool
    {
        get => _transformTool;
        set
        {
            if (Set(ref _transformTool, value)) RefreshSceneGizmo();
        }
    }

    public RekallAgeStudioTransformSpace TransformSpace
    {
        get => _transformSpace;
        set
        {
            if (Set(ref _transformSpace, value)) RefreshSceneGizmo();
        }
    }

    public double MoveSnap
    {
        get => _moveSnap;
        set => Set(ref _moveSnap, ValidateSnap(value, nameof(MoveSnap)));
    }

    public double RotationSnap
    {
        get => _rotationSnap;
        set => Set(ref _rotationSnap, ValidateSnap(value, nameof(RotationSnap)));
    }

    public double ScaleSnap
    {
        get => _scaleSnap;
        set => Set(ref _scaleSnap, ValidateSnap(value, nameof(ScaleSnap)));
    }

    internal IReadOnlyList<RekallAgeStudioGizmoHandle> SceneGizmoHandles =>
        TransformTool is RekallAgeStudioTransformTool.Select ? [] : _sceneGizmo?.Handles ?? [];

    public IReadOnlyList<RekallAgeStudioGizmoDisplayLine> GetSceneGizmoDisplayLines(
        double displayWidth,
        double displayHeight)
    {
        if (_viewportInteraction is null || displayWidth <= 0 || displayHeight <= 0
            || SceneGizmoHandles.Count == 0)
        {
            return [];
        }

        var scale = Math.Min(
            displayWidth / _viewportInteraction.FrameWidth,
            displayHeight / _viewportInteraction.FrameHeight);
        var offsetX = (displayWidth - (_viewportInteraction.FrameWidth * scale)) * 0.5;
        var offsetY = (displayHeight - (_viewportInteraction.FrameHeight * scale)) * 0.5;
        return SceneGizmoHandles.Select(handle => new RekallAgeStudioGizmoDisplayLine(
            handle.Axis,
            offsetX + (handle.Start.X * scale),
            offsetY + (handle.Start.Y * scale),
            offsetX + (handle.End.X * scale),
            offsetY + (handle.End.Y * scale))).ToArray();
    }

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

    public async Task<bool> SelectViewportEntityAsync(
        double displayWidth,
        double displayHeight,
        double displayX,
        double displayY)
    {
        if (IsBusy || _viewportInteraction is null) return false;
        var point = _viewportInteraction.MapDisplayPoint(displayWidth, displayHeight, displayX, displayY);
        if (point is null) return false;
        var entityId = _viewportInteraction.Pick(point.Value.X, point.Value.Y);
        if (entityId is null) return false;
        await RunAsync(() => _session.SelectEntityAsync(entityId, CancellationToken.None).AsTask());
        OnPropertyChanged(nameof(SelectedEntityId));
        return string.Equals(_session.SelectedEntityId, entityId, StringComparison.Ordinal);
    }

    public bool BeginSceneTransform(
        double displayWidth,
        double displayHeight,
        double displayX,
        double displayY)
    {
        if (IsBusy || Mode != RekallAgeStudioMode.Edit || TransformTool is RekallAgeStudioTransformTool.Select
            || _viewportInteraction is null || _sceneGizmo is null)
        {
            return false;
        }

        var point = _viewportInteraction.MapDisplayPoint(displayWidth, displayHeight, displayX, displayY);
        if (point is null) return false;
        var axis = _sceneGizmo.HitTest(point.Value.X, point.Value.Y);
        if (axis is null) return false;

        var propertyName = TransformPropertyName(TransformTool, axis.Value);
        var initialValue = InspectorNumber("Rekall.Transform3D", propertyName,
            TransformTool is RekallAgeStudioTransformTool.Scale ? 1 : 0);
        var snap = TransformTool switch
        {
            RekallAgeStudioTransformTool.Move => MoveSnap,
            RekallAgeStudioTransformTool.Rotate => RotationSnap,
            RekallAgeStudioTransformTool.Scale => ScaleSnap,
            _ => 0
        };
        _sceneTransformGesture = _sceneGizmo.Begin(
            TransformTool, axis.Value, point.Value.X, point.Value.Y, initialValue, snap);
        _sceneTransformUpdate = null;
        return true;
    }

    public bool UpdateSceneTransform(
        double displayWidth,
        double displayHeight,
        double displayX,
        double displayY)
    {
        if (_sceneTransformGesture is null || _viewportInteraction is null) return false;
        var point = _viewportInteraction.MapDisplayPoint(displayWidth, displayHeight, displayX, displayY);
        if (point is null) return false;
        _sceneTransformUpdate = _sceneTransformGesture.Update(point.Value.X, point.Value.Y);
        return true;
    }

    public async Task<bool> CompleteSceneTransformAsync()
    {
        var update = _sceneTransformUpdate;
        _sceneTransformGesture = null;
        _sceneTransformUpdate = null;
        if (update is null || _session.ProjectRoot is null || _session.SceneName is null
            || _session.SelectedEntityId is null)
        {
            return false;
        }

        await ExecuteComponentCommandAsync(
            "rekall.component.set_property",
            new
            {
                projectRoot = _session.ProjectRoot,
                sceneName = _session.SceneName,
                entityId = _session.SelectedEntityId,
                componentType = update.ComponentType,
                propertyName = update.PropertyName,
                value = update.Value
            },
            $"{TransformTool} {_session.SelectedEntityId} {update.PropertyName}");
        return true;
    }

    public void CancelSceneTransform()
    {
        _sceneTransformGesture = null;
        _sceneTransformUpdate = null;
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            if (_disposalComplete) return ValueTask.CompletedTask;
            if (_disposeTask is not { IsCompleted: false }) _disposeTask = DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    internal bool IsDisposalComplete
    {
        get
        {
            lock (_disposeSync)
            {
                return _disposalComplete;
            }
        }
    }

    private async Task DisposeCoreAsync()
    {
        if (!_shutdownPrerequisitesComplete)
        {
            await DisposeShutdownPrerequisitesAsync();
            _shutdownPrerequisitesComplete = true;
        }

        Exception? previewFailure = null;
        try
        {
            await _previewSession.DisposeAsync();
        }
        catch (Exception exception)
        {
            previewFailure = exception;
        }

        var terminalCleanupComplete = _previewSession.IsDisposalComplete;
        if (terminalCleanupComplete)
        {
            lock (_disposeSync)
            {
                _disposalComplete = true;
            }
        }

        if (previewFailure is null && terminalCleanupComplete) return;

        const string incompleteCode = "REKALL_STUDIO_VULKAN_SHUTDOWN_INCOMPLETE";
        var diagnostic = terminalCleanupComplete
            ? "REKALL_STUDIO_VULKAN_SHUTDOWN_DIAGNOSTIC: Renderer cleanup completed with diagnostics."
            : $"{incompleteCode}: Renderer cleanup is incomplete; the Vulkan child window must remain alive.";
        var failure = new AggregateException(
            diagnostic,
            previewFailure ?? new InvalidOperationException(
                "The preview session did not prove terminal renderer cleanup."));
        if (!terminalCleanupComplete)
        {
            ViewportAvailable = false;
            ViewportBackendLabel = "Vulkan · cleanup incomplete";
            ViewportUnavailableReason = diagnostic;
        }
        StatusText = diagnostic;
        Replace(ValidationLines, [$"error: {diagnostic}"]);
        throw failure;
    }

    private async Task DisposeShutdownPrerequisitesAsync()
    {
        try
        {
            try
            {
                _lifecycleCancellation.Cancel();
            }
            catch (Exception)
            {
                ReportLanguageModelShutdownFailure();
            }

            try
            {
                _agentCancellation?.Cancel();
                _codexSignInCancellation?.Cancel();
            }
            catch (Exception)
            {
                ReportLanguageModelShutdownFailure();
            }

            Task providerTransition;
            Task? activeLanguageModelRefresh;
            Task? activeAgentRun;
            Task? activeCodexSignIn;
            lock (_languageModelLifecycleSync)
            {
                providerTransition = _languageModelProviderTransition;
                activeLanguageModelRefresh = _activeLanguageModelRefresh;
                activeAgentRun = _activeAgentRun;
                activeCodexSignIn = _activeCodexSignIn;
            }
            Task[] renderingOperations;
            lock (_renderingOperationsSync)
            {
                renderingOperations = [.. _activeRenderingOperations];
            }

            try
            {
                await Task.WhenAll(renderingOperations).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_lifecycleCancellation.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                ReportUnexpectedFailure(exception);
            }

            var languageModelOperations = new List<Task> { providerTransition };
            if (activeLanguageModelRefresh is not null) languageModelOperations.Add(activeLanguageModelRefresh);
            if (activeAgentRun is not null) languageModelOperations.Add(activeAgentRun);
            if (activeCodexSignIn is not null) languageModelOperations.Add(activeCodexSignIn);
            try
            {
                await Task.WhenAll(languageModelOperations).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_lifecycleCancellation.IsCancellationRequested)
            {
            }
            catch (Exception)
            {
                ReportLanguageModelShutdownFailure();
            }

            try
            {
                await StopCoreAsync(resetEditPreview: false, CancellationToken.None);
            }
            catch (Exception)
            {
                ReportLanguageModelShutdownFailure();
            }
        }
        finally
        {
            try
            {
                _agentCancellation?.Dispose();
            }
            catch (Exception)
            {
                ReportLanguageModelShutdownFailure();
            }
            _agentCancellation = null;

            try
            {
                await ReleaseLanguageModelRunnerAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                ReportLanguageModelShutdownFailure();
            }

            _sessionOpenAiApiKey = null;
            _sessionKimiApiKey = null;
            _sessionOllamaUrl = null;
            _sessionOpenAiUrl = null;
            _sessionKimiUrl = null;
            OnPropertyChanged(nameof(HasSessionOpenAiCredential));
            OnPropertyChanged(nameof(HasSessionKimiCredential));
            OnPropertyChanged(nameof(OpenAiCredentialSourceLabel));
            OnPropertyChanged(nameof(KimiCredentialSourceLabel));
            try
            {
                _lifecycleCancellation.Dispose();
            }
            catch (Exception)
            {
                ReportLanguageModelShutdownFailure();
            }

            try
            {
                _modeTransitionGate.Dispose();
            }
            catch (Exception)
            {
                ReportLanguageModelShutdownFailure();
            }
        }
    }

    private bool CanOpenOrCreate() => !IsBusy && Mode == RekallAgeStudioMode.Edit && !string.IsNullOrWhiteSpace(ProjectPathInput);
    private bool HasOpenProject() => !IsBusy && _session.Model is not null;
    private bool HasSelectedEntity() => HasEditableProject() && _session.SelectedEntityId is not null;
    private bool CanRenameEntity() => HasSelectedEntity() && !string.IsNullOrWhiteSpace(EntityNameInput);
    private bool CanReparentEntity() => HasSelectedEntity()
        && !string.IsNullOrWhiteSpace(ParentEntityIdInput)
        && !ParentEntityIdInput.Trim().Equals(_session.SelectedEntityId, StringComparison.Ordinal);
    private bool HasEditableProject() => HasOpenProject() && Mode == RekallAgeStudioMode.Edit;
    private bool HasQualityProfile() => HasEditableProject()
        && _currentModel?.Rendering.Authoring is not null
        && QualityPresets.Contains(SelectedQualityPreset, StringComparer.Ordinal);
    private bool CanAttachQualityProfile() => HasSelectedEntity()
        && _currentModel?.Rendering.Authoring is null
        && QualityPresets.Contains(SelectedQualityPreset, StringComparer.Ordinal);
    private bool CanCompareQuality() => HasEditableProject()
        && QualityPresets.Contains(SelectedQualityPreset, StringComparer.Ordinal)
        && QualityPresets.Contains(ComparisonQualityPreset, StringComparer.Ordinal)
        && !SelectedQualityPreset.Equals(ComparisonQualityPreset, StringComparison.Ordinal);
    private bool CanEditComponent() => HasEditableProject()
        && _session.SelectedEntityId is not null
        && !string.IsNullOrWhiteSpace(ComponentTypeInput);
    private bool CanEditProperty() => CanEditComponent() && !string.IsNullOrWhiteSpace(PropertyNameInput);
    private bool CanCommitInspectorProperty(RekallAgeStudioInspectorPropertyEditorModel row) =>
        HasEditableProject()
        && IsInspectorPropertyEditorForSelection(row)
        && row.IsDirty
        && row.IsValid;
    private bool CanResetInspectorProperty(RekallAgeStudioInspectorPropertyEditorModel row) =>
        HasEditableProject()
        && IsInspectorPropertyEditorForSelection(row)
        && row.IsDefined;
    private bool IsInspectorPropertyEditorForSelection(RekallAgeStudioInspectorPropertyEditorModel row) =>
        _session.SelectedEntityId is { } entityId
        && _inspectorPropertyEditorKeys.TryGetValue(row, out var key)
        && key.EntityId.Equals(entityId, StringComparison.Ordinal);
    private bool CanCreateAttachCodeComponent() => HasSelectedEntity()
        && !IsCodeDirty
        && !string.IsNullOrWhiteSpace(CodeModuleNameInput)
        && !string.IsNullOrWhiteSpace(CodeComponentNameInput)
        && !string.IsNullOrWhiteSpace(CodeSystemNameInput);
    private bool CanRunAgent() => HasEditableProject()
        && !IsAgentRunning
        && _languageModelSetupAllowsAuthoring
        && _languageModelRunner is not null
        && !string.IsNullOrWhiteSpace(SelectedLanguageModel)
        && !string.IsNullOrWhiteSpace(AgentTaskInput);
    private bool CanSwitchScene() => HasEditableProject()
        && !string.IsNullOrWhiteSpace(SceneNameInput)
        && !_session.SceneName!.Equals(SceneNameInput.Trim(), StringComparison.Ordinal);
    private bool CanAuditPackage() => HasOpenProject()
        && LastPackagePath is not null
        && (File.Exists(LastPackagePath) || Directory.Exists(LastPackagePath));
    private bool CanOpenPackageFolder() => LastPackageOutputDirectory is not null
        && Directory.Exists(LastPackageOutputDirectory);
    // Unlike CanAuditPackage, rekall.game.audit_web is self-contained -- it republishes the project itself before
    // verifying it (the same shape as AuditPlayablePackageCommand's own inspect/run/capture, just for the web
    // target) -- so it does not depend on a prior successful Publish Web click.
    private bool CanAuditWeb() => HasOpenProject();
    private bool HasOpenMesh() => !IsBusy && Mode == RekallAgeStudioMode.Edit && _modeling.Mesh is not null;
    private bool CanOpenMeshAsset() => HasEditableProject() && !string.IsNullOrWhiteSpace(SelectedMeshAssetId);
    private bool CanPublishModel() => CanOpenMeshAsset()
        && !string.IsNullOrWhiteSpace(ModelAssetIdInput)
        && !string.IsNullOrWhiteSpace(ModelAssetDisplayNameInput)
        && IsModelAssetIdValid();
    private bool CanPlaceModel() => CanPublishModel()
        && CanUseModelPlacementInputs()
        && File.Exists(_modelAssetStore.GetModelPath(_session.ProjectRoot!, ModelAssetIdInput.Trim()));
    private bool CanPublishAndPlaceModel() => CanPublishModel() && CanUseModelPlacementInputs();
    private bool CanUseModelPlacementInputs() =>
        (string.IsNullOrWhiteSpace(ModelEntityIdInput)
            || InstantiateModelAssetCommand.IsValidEntityId(ModelEntityIdInput.Trim()))
        && TryGetModelPlacement(out _, out _, out _);
    private bool IsModelAssetIdValid()
    {
        try
        {
            _ = _modelAssetStore.GetModelPath(_session.ProjectRoot!, ModelAssetIdInput.Trim());
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
    private bool CanCreateMeshPrimitive() => HasEditableProject()
        && !string.IsNullOrWhiteSpace(SelectedMeshPrimitive)
        && !string.IsNullOrWhiteSpace(MeshPrimitiveAssetIdInput);
    private bool CanSelectMeshElement() => HasOpenMesh() && SelectedMeshElementId.HasValue;
    private bool CanRunMeshOperation() => HasOpenMesh() && !string.IsNullOrWhiteSpace(SelectedMeshOperationId)
        && MeshParameterEditors.All(item => item.IsValid);
    private bool CanOpenModelingGraph() => HasEditableProject() && !string.IsNullOrWhiteSpace(SelectedModelingGraphAssetId);
    private bool CanEvaluateModelingGraph() => HasEditableProject() && _modelingGraph.Graph is not null
        && !string.IsNullOrWhiteSpace(SelectedModelingGraphOutput);
    private bool CanApplyModelingGraphParameters() => HasEditableProject()
        && SelectedModelingGraphNode is not null
        && ModelingGraphParameterEditors.Count > 0
        && ModelingGraphParameterEditors.All(item => item.IsValid)
        && ModelingGraphParameterEditors.Any(item => item.IsModified);

    private Task RefreshMeshAssetsAsync() => RunModelingAsync(() =>
    {
        if (_session.ProjectRoot is null) throw new InvalidOperationException("Open a project before browsing mesh assets.");
        Replace(MeshAssetIds, _modeling.ListAssets(_session.ProjectRoot));
        if (SelectedMeshAssetId is null || !MeshAssetIds.Contains(SelectedMeshAssetId)) SelectedMeshAssetId = MeshAssetIds.FirstOrDefault();
        MeshSummary = MeshAssetIds.Count == 0 ? "No persisted mesh assets are present in Modeling/Meshes." : $"{MeshAssetIds.Count} mesh asset(s) available.";
        return Task.CompletedTask;
    });

    private async Task CreateMeshPrimitiveAsync()
    {
        if (_session.ProjectRoot is null) return;
        var primitive = SelectedMeshPrimitive.Trim().ToLowerInvariant();
        var assetId = MeshPrimitiveAssetIdInput.Trim();
        var name = $"{char.ToUpperInvariant(primitive[0])}{primitive[1..]}";
        var mesh = await _meshPrimitiveFactory.CreateAsync(
            primitive,
            assetId,
            name,
            _lifecycleCancellation.Token);
        await RunAsync(() => _session.ExecuteAsync(
            "rekall.mesh.create_asset",
            JsonSerializer.Serialize(new
            {
                projectRoot = _session.ProjectRoot,
                assetId,
                name,
                topology = mesh.Topology,
                attributes = mesh.Attributes,
                materialSlots = mesh.MaterialSlots,
                selectionSets = mesh.SelectionSets
            }),
            $"Create editable {primitive} mesh {assetId}",
            "studio",
            CancellationToken.None).AsTask());
        if (!_modeling.ListAssets(_session.ProjectRoot).Contains(assetId, StringComparer.Ordinal)) return;
        Replace(MeshAssetIds, _modeling.ListAssets(_session.ProjectRoot));
        SelectedMeshAssetId = assetId;
        await OpenMeshAssetAsync();
    }

    private Task OpenMeshAssetAsync() => RunModelingAsync(async () =>
    {
        _meshViewportCamera = RekallAgeStudioViewportCamera.Identity;
        await _modeling.OpenAsync(_session.ProjectRoot!, SelectedMeshAssetId!, _lifecycleCancellation.Token);
        _modeling.SetDomain(MeshEditDomain);
        RefreshMeshEditingState();
    });

    public async Task<bool> OpenSelectedLinkedModelInModelingAsync()
    {
        var modelAssetId = SelectedLinkedModelAssetId();
        if (_session.ProjectRoot is null || modelAssetId is null || IsBusy) return false;

        IsBusy = true;
        try
        {
            var model = await _modelAssetStore.LoadAsync(
                _session.ProjectRoot,
                modelAssetId,
                _lifecycleCancellation.Token);
            if (model.Source.Kind != RekallAgeModelSourceKind.Mesh)
            {
                StatusText = $"Model Asset '{modelAssetId}' does not have an editable mesh source.";
                return false;
            }

            Replace(MeshAssetIds, _modeling.ListAssets(_session.ProjectRoot));
            if (!MeshAssetIds.Contains(model.Source.AssetId))
            {
                StatusText = $"The source mesh '{model.Source.AssetId}' for Model Asset '{modelAssetId}' is unavailable.";
                return false;
            }

            SelectedMeshAssetId = model.Source.AssetId;
            ModelAssetIdInput = model.AssetId;
            ModelAssetDisplayNameInput = model.DisplayName;
            _meshViewportCamera = RekallAgeStudioViewportCamera.Identity;
            await _modeling.OpenAsync(_session.ProjectRoot, model.Source.AssetId, _lifecycleCancellation.Token);
            _modeling.SetDomain(MeshEditDomain);
            RefreshMeshEditingState();
            StatusText = $"Editing source mesh '{model.Source.AssetId}' for linked Model Asset '{modelAssetId}'.";
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            StatusText = exception.Message;
            Replace(MeshDiagnosticLines, [$"error: REKALL_STUDIO_LINKED_MODEL_OPEN_FAILED - {exception.Message}"]);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public RekallAgeModuleSourceInfo? SelectedCodeSource
    {
        get => _selectedCodeSource;
        private set
        {
            if (!Set(ref _selectedCodeSource, value)) return;
            OnPropertyChanged(nameof(SelectedCodeSourcePath));
            OnPropertyChanged(nameof(SelectedCodeProjectPath));
            RefreshCommands();
        }
    }

    public string CodeSourceText
    {
        get => _codeSourceText;
        set
        {
            if (!Set(ref _codeSourceText, value)) return;
            _codeSession.SourceText = value;
            OnPropertyChanged(nameof(IsCodeDirty));
            RefreshCommands();
        }
    }

    public bool IsCodeDirty => _codeSession.IsDirty;

    public string? SelectedCodeSourcePath => SelectedCodeSource?.SourcePath;

    public string? SelectedCodeProjectPath => _codeSession.SelectedProjectPath;

    public string? CodeSolutionPath => _codeSession.DevelopmentWorkspace?.SolutionPath;

    public string? CodeVsCodeLaunchPath => _codeSession.DevelopmentWorkspace?.VsCodeLaunchPath;

    public string CodeModuleNameInput
    {
        get => _codeModuleNameInput;
        set { if (Set(ref _codeModuleNameInput, value)) RefreshCommands(); }
    }

    public string CodeComponentNameInput
    {
        get => _codeComponentNameInput;
        set { if (Set(ref _codeComponentNameInput, value)) RefreshCommands(); }
    }

    public string CodeSystemNameInput
    {
        get => _codeSystemNameInput;
        set { if (Set(ref _codeSystemNameInput, value)) RefreshCommands(); }
    }

    public string CodeStatusText
    {
        get => _codeStatusText;
        private set => Set(ref _codeStatusText, value);
    }

    private Task PublishModelAsync() => RunAsync(PublishModelOperationAsync, refreshPreviewAfter: true);

    private Task PlaceModelAsync() => RunAsync(PlaceModelOperationAsync, refreshPreviewAfter: true);

    private Task PublishAndPlaceModelAsync() => RunAsync(async () =>
    {
        var published = await PublishModelOperationAsync();
        return published.Ok ? await PlaceModelOperationAsync() : published;
    }, refreshPreviewAfter: true);

    private async Task<RekallAgeWorkbenchOperationResult> PublishModelOperationAsync()
    {
        var modelAssetId = ModelAssetIdInput.Trim();
        var modelPath = _modelAssetStore.GetModelPath(_session.ProjectRoot!, modelAssetId);
        RekallAgeWorkbenchOperationResult published;
        if (File.Exists(modelPath))
        {
            var current = await _modelAssetStore.LoadVersionedAsync(
                _session.ProjectRoot!,
                modelAssetId,
                CancellationToken.None);
            if (current.Value.Source.Kind != RekallAgeModelSourceKind.Mesh
                || !current.Value.Source.AssetId.Equals(SelectedMeshAssetId, StringComparison.Ordinal))
            {
                const string code = "REKALL_STUDIO_MODEL_SOURCE_MISMATCH";
                var message = $"Model Asset '{modelAssetId}' is linked to mesh '{current.Value.Source.AssetId}', not selected mesh '{SelectedMeshAssetId}'. Choose a new Model Asset ID or select the linked mesh.";
                return new RekallAgeWorkbenchOperationResult(
                    false,
                    message,
                    null,
                    [new RekallAgeCommandError(code, message, modelAssetId)]);
            }
            published = await _session.ExecuteAsync(
                "rekall.asset.model.rebuild",
                JsonSerializer.Serialize(new
                {
                    projectRoot = _session.ProjectRoot,
                    assetId = modelAssetId,
                    displayName = ModelAssetDisplayNameInput.Trim(),
                    expectedModelFileRevision = current.Revision
                }),
                $"Rebuild Model Asset {modelAssetId}",
                "studio",
                CancellationToken.None);
        }
        else
        {
            published = await _session.ExecuteAsync(
                "rekall.asset.model.publish",
                JsonSerializer.Serialize(new
                {
                    projectRoot = _session.ProjectRoot,
                    assetId = modelAssetId,
                    displayName = ModelAssetDisplayNameInput.Trim(),
                    source = new
                    {
                        kind = "Mesh",
                        assetId = SelectedMeshAssetId,
                        outputName = (string?)null
                    },
                    expectedModelFileRevision = RekallAgeDocumentRevision.Missing
                }),
                $"Publish Model Asset {modelAssetId}",
                "studio",
                CancellationToken.None);
        }

        if (!published.Ok)
        {
            return published;
        }

        LastPublishedModelAssetId = modelAssetId;
        return published;
    }

    private async Task<RekallAgeWorkbenchOperationResult> PlaceModelOperationAsync()
    {
        var modelAssetId = ModelAssetIdInput.Trim();
        if (!TryGetModelPlacement(out var position, out var rotation, out var scale))
        {
            const string code = "REKALL_STUDIO_MODEL_TRANSFORM_INVALID";
            const string message = "Position, rotation, and scale must contain valid numbers before placing a Model Asset.";
            return new(false, message, null, [new(code, message, "transform")]);
        }
        var placed = await _session.ExecuteAsync(
            "rekall.scene.instantiate_asset",
            JsonSerializer.Serialize(new
            {
                projectRoot = _session.ProjectRoot,
                sceneName = _session.SceneName,
                modelAssetId,
                entityId = string.IsNullOrWhiteSpace(ModelEntityIdInput) ? null : ModelEntityIdInput.Trim(),
                name = string.IsNullOrWhiteSpace(ModelEntityNameInput) ? null : ModelEntityNameInput.Trim(),
                position = new { x = position.X, y = position.Y, z = position.Z },
                rotationDegrees = new { x = rotation.X, y = rotation.Y, z = rotation.Z },
                scale = new { x = scale.X, y = scale.Y, z = scale.Z },
                parentEntityId = string.IsNullOrWhiteSpace(ModelPlacementParentEntityIdInput)
                    ? null
                    : ModelPlacementParentEntityIdInput.Trim()
            }),
            $"Place Model Asset {modelAssetId}",
            "studio",
            CancellationToken.None);
        if (!placed.Ok || placed.Value is not InstantiateModelAssetResult placement)
        {
            return placed;
        }

        LastPlacedModelEntityId = placement.EntityId;
        var selected = await _session.SelectEntityAsync(placement.EntityId, CancellationToken.None);
        if (selected.Ok) ModelEntityIdInput = string.Empty;
        return selected.Ok
            ? new RekallAgeWorkbenchOperationResult(
                true,
                placed.Summary,
                placement,
                placement.Warnings.Select(warning => new RekallAgeCommandError(
                    warning.Code,
                    warning.Message,
                    warning.Target)).ToArray())
            : selected;
    }

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

    /// <summary>
    /// One normalized-width viewport drag maps to this many world units for the modal operation
    /// parameter drag (Alt+drag) — chosen so a comfortable half-viewport drag produces a readable
    /// few-unit extrude/inset/bevel amount rather than requiring pixel-precise typing.
    /// </summary>
    private const double ModalDragWorldUnitsPerNormalizedWidth = 6.0;

    /// <summary>
    /// Starts a Blender-style modal drag (Alt+left-drag in the mesh viewport) that live-previews
    /// the selected operation's first numeric (Float) parameter as the mouse moves, reusing the
    /// existing Preview/Apply pipeline exactly as the typed-parameter form does — this is an
    /// alternate input path onto the same operation, not a parallel one. Returns false (and starts
    /// nothing) when the selected operation has no numeric parameter to drive.
    /// </summary>
    public bool BeginModalMeshOperationDrag(double normalizedX)
    {
        var parameter = MeshParameterEditors.FirstOrDefault(editor => editor.Descriptor.ValueType == RekallAgeGeometryValueType.Float);
        if (parameter is null) return false;
        _modalDragOriginalValue = double.TryParse(parameter.ValueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var current) ? current : 0;
        _modalDragParameter = parameter;
        _modalDragStartNormalizedX = normalizedX;
        return true;
    }

    public Task UpdateModalMeshOperationDragAsync(double normalizedX)
    {
        if (_modalDragParameter is null) return Task.CompletedTask;
        SetModalDragValue(normalizedX);
        return PreviewMeshOperationAsync();
    }

    public async Task CompleteModalMeshOperationDragAsync(double normalizedX)
    {
        if (_modalDragParameter is null) return;
        SetModalDragValue(normalizedX);
        await ApplyMeshOperationAsync();
        _modalDragParameter = null;
    }

    public void CancelModalMeshOperationDrag()
    {
        if (_modalDragParameter is null) return;
        _modalDragParameter.ValueText = _modalDragOriginalValue.ToString("0.###", CultureInfo.InvariantCulture);
        _modalDragParameter = null;
        _ = CancelMeshPreviewAsync();
    }

    private void SetModalDragValue(double normalizedX)
    {
        var value = _modalDragOriginalValue + (normalizedX - _modalDragStartNormalizedX) * ModalDragWorldUnitsPerNormalizedWidth;
        _modalDragParameter!.ValueText = value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private Task RefreshModelingGraphsAsync() => RunGraphModelingAsync(() =>
    {
        if (_session.ProjectRoot is null) throw new InvalidOperationException("Open a project before browsing procedural graphs.");
        Replace(ModelingGraphAssetIds, _modelingGraph.ListAssets(_session.ProjectRoot));
        if (SelectedModelingGraphAssetId is null || !ModelingGraphAssetIds.Contains(SelectedModelingGraphAssetId))
            SelectedModelingGraphAssetId = ModelingGraphAssetIds.FirstOrDefault();
        ModelingGraphSummary = ModelingGraphAssetIds.Count == 0
            ? "No persisted procedural graphs are present in Modeling/Graphs."
            : $"{ModelingGraphAssetIds.Count} procedural graph asset(s) available.";
        return Task.CompletedTask;
    });

    private Task OpenModelingGraphAsync() => RunGraphModelingAsync(async () =>
    {
        await _modelingGraph.OpenAsync(_session.ProjectRoot!, SelectedModelingGraphAssetId!, _lifecycleCancellation.Token);
        Replace(ModelingGraphNodes, _modelingGraph.Nodes);
        Replace(ModelingGraphOutputNames, _modelingGraph.OutputNames);
        SelectedModelingGraphOutput = _modelingGraph.SelectedOutputName;
        Replace(ModelingGraphDiagnosticLines, []);
        ModelingGraphViewportImage = null;
        ModelingGraphSummary = _modelingGraph.EvaluationSummary;
        SelectedModelingGraphNode = ModelingGraphNodes.FirstOrDefault();
        _modelingGraphNodePositions.Clear();
        _modelingGraphCanvasNeedsFrame = true;
        _modelingGraphPendingLinkPort = null;
        RefreshModelingGraphCanvas();
    });

    private Task EvaluateModelingGraphAsync() => RunGraphModelingAsync(async () =>
    {
        var report = await _modelingGraph.EvaluateAsync(SelectedModelingGraphOutput!, _lifecycleCancellation.Token);
        Replace(ModelingGraphNodes, _modelingGraph.Nodes);
        Replace(ModelingGraphDiagnosticLines, report.Diagnostics.Select(item =>
            $"{item.Severity}: {item.Code}{(item.NodeId is null ? string.Empty : $" [{item.NodeId}]")} - {item.Message}"));
        ModelingGraphSummary = _modelingGraph.EvaluationSummary;
        RefreshModelingGraphOutputViewport();
        RefreshModelingGraphCanvas();
    });

    private Task ApplyModelingGraphParametersAsync() => RunGraphModelingAsync(async () =>
    {
        var selectedNodeId = SelectedModelingGraphNode!.NodeId;
        var result = await _modelingGraph.ApplyParameterEditsAsync(
            selectedNodeId,
            ModelingGraphParameterEditors,
            "studio",
            _lifecycleCancellation.Token);
        Replace(ModelingGraphNodes, _modelingGraph.Nodes);
        Replace(ModelingGraphOutputNames, _modelingGraph.OutputNames);
        SelectedModelingGraphOutput = _modelingGraph.SelectedOutputName;
        SelectedModelingGraphNode = ModelingGraphNodes.FirstOrDefault(item => item.NodeId == selectedNodeId);
        Replace(ModelingGraphDiagnosticLines, result.Validation.Diagnostics.Select(item =>
            $"{item.Severity}: {item.Code}{(item.NodeId is null ? string.Empty : $" [{item.NodeId}]")} - {item.Message}"));
        ModelingGraphSummary = _modelingGraph.EvaluationSummary;
        if (SelectedModelingGraphOutput is not null)
        {
            var evaluation = await _modelingGraph.EvaluateAsync(SelectedModelingGraphOutput, _lifecycleCancellation.Token);
            Replace(ModelingGraphNodes, _modelingGraph.Nodes);
            foreach (var diagnostic in evaluation.Diagnostics)
                ModelingGraphDiagnosticLines.Add($"{diagnostic.Severity}: {diagnostic.Code}{(diagnostic.NodeId is null ? string.Empty : $" [{diagnostic.NodeId}]")} - {diagnostic.Message}");
            ModelingGraphSummary = _modelingGraph.EvaluationSummary;
            RefreshModelingGraphOutputViewport();
        }
        RefreshModelingGraphCanvas();
    });

    private void RefreshModelingGraphOutputViewport()
    {
        ModelingGraphViewportImage = _modelingGraph.OutputMesh is null
            ? null
            : _meshViewportRenderer.Render(
                _modelingGraph.OutputMesh,
                RekallAgeGeometryDomain.Face,
                [],
                640,
                360,
                preview: false,
                style: RekallAgeStudioViewportRenderStyles.Parse(ModelingGraphViewportRenderStyle)).Image;
    }

    /// <summary>Re-renders the node-graph canvas from the current graph, auto-laying-out any node without a remembered position.</summary>
    private void RefreshModelingGraphCanvas()
    {
        var graph = _modelingGraph.Graph;
        if (graph is null)
        {
            _modelingGraphCanvasFrame = null;
            OnPropertyChanged(nameof(ModelingGraphCanvasImage));
            return;
        }
        var liveNodeIds = graph.Nodes.Select(node => node.NodeId).ToHashSet(StringComparer.Ordinal);
        foreach (var staleId in _modelingGraphNodePositions.Keys.Where(id => !liveNodeIds.Contains(id)).ToArray())
            _modelingGraphNodePositions.Remove(staleId);
        foreach (var (nodeId, position) in RekallAgeStudioModelingGraphLayout.ComputeDefaultPositions(graph.Nodes, graph.Links))
            _modelingGraphNodePositions.TryAdd(nodeId, position);
        if (_modelingGraphCanvasNeedsFrame)
        {
            FrameModelingGraphCanvasView();
            _modelingGraphCanvasNeedsFrame = false;
        }
        _modelingGraphCanvasFrame = _modelingGraphCanvasRenderer.Render(
            graph.Nodes, graph.Links, _modelingGraphNodePositions, _modelingGraphCatalog,
            SelectedModelingGraphNode?.NodeId, 640, 360, _modelingGraphCanvasView);
        OnPropertyChanged(nameof(ModelingGraphCanvasImage));
        OnPropertyChanged(nameof(ModelingGraphCanvasZoomLabel));
    }

    public BitmapSource? ModelingGraphCanvasImage => _modelingGraphCanvasFrame?.Image;
    public string ModelingGraphCanvasZoomLabel => $"{_modelingGraphCanvasView.Zoom * 100:0}%";

    public void PanModelingGraphCanvas(double normalizedDeltaX, double normalizedDeltaY)
    {
        _modelingGraphCanvasView = _modelingGraphCanvasView.PanBy(
            new System.Windows.Vector(normalizedDeltaX * 640, normalizedDeltaY * 360));
        RefreshModelingGraphCanvas();
    }

    public void ZoomModelingGraphCanvas(double factor, double normalizedAnchorX, double normalizedAnchorY)
    {
        _modelingGraphCanvasView = _modelingGraphCanvasView.ZoomAt(
            new System.Windows.Point(normalizedAnchorX * 640, normalizedAnchorY * 360), factor);
        RefreshModelingGraphCanvas();
    }

    public void ResetModelingGraphCanvasView()
    {
        FrameModelingGraphCanvasView();
        RefreshModelingGraphCanvas();
    }

    private void FrameModelingGraphCanvasView()
    {
        if (_modelingGraphNodePositions.Count == 0)
        {
            _modelingGraphCanvasView = RekallAgeStudioModelingGraphCanvasView.Identity;
            return;
        }
        var minX = _modelingGraphNodePositions.Values.Min(point => point.X);
        var minY = _modelingGraphNodePositions.Values.Min(point => point.Y);
        var maxX = _modelingGraphNodePositions.Values.Max(point => point.X) + 220;
        var maxY = _modelingGraphNodePositions.Values.Max(point => point.Y) + 150;
        _modelingGraphCanvasView = RekallAgeStudioModelingGraphCanvasView.FitBounds(
            new System.Windows.Rect(minX, minY, maxX - minX, maxY - minY), 640, 360, 18);
    }

    /// <summary>
    /// Handles a mouse-down on the node-graph canvas: a port hit arms or completes a link
    /// (a second click on a compatible-direction port issues an AddLink patch), a node-body hit
    /// selects that node (driving the existing parameter panel) and begins a reposition drag, and
    /// an empty-space hit clears the current selection and any pending link.
    /// </summary>
    public async Task ClickModelingGraphCanvasAsync(double normalizedX, double normalizedY)
    {
        if (_modelingGraphCanvasFrame is null) return;
        var (x, y) = DenormalizeGraphCanvasPoint(normalizedX, normalizedY);
        var port = _modelingGraphCanvasRenderer.PickPort(_modelingGraphCanvasFrame, x, y);
        if (port is { } hitPort)
        {
            if (_modelingGraphPendingLinkPort is { } pending && pending.IsOutput != hitPort.IsOutput
                && !pending.Equals(hitPort))
            {
                var from = pending.IsOutput ? pending : hitPort;
                var to = pending.IsOutput ? hitPort : pending;
                _modelingGraphPendingLinkPort = null;
                await AddModelingGraphLinkAsync(from, to);
            }
            else
            {
                _modelingGraphPendingLinkPort = hitPort;
                RefreshModelingGraphCanvas();
            }
            return;
        }

        _modelingGraphPendingLinkPort = null;
        var nodeId = _modelingGraphCanvasRenderer.PickNode(_modelingGraphCanvasFrame, x, y);
        SelectedModelingGraphNode = ModelingGraphNodes.FirstOrDefault(node => node.NodeId == nodeId);
        if (nodeId is not null)
        {
            _modelingGraphDragNodeId = nodeId;
            _modelingGraphDragOrigin = _modelingGraphNodePositions[nodeId];
            _modelingGraphDragStart = new System.Windows.Point(x, y);
        }
        RefreshModelingGraphCanvas();
    }

    public void UpdateModelingGraphNodeDrag(double normalizedX, double normalizedY)
    {
        if (_modelingGraphDragNodeId is not { } nodeId) return;
        var (x, y) = DenormalizeGraphCanvasPoint(normalizedX, normalizedY);
        var delta = (new System.Windows.Point(x, y) - _modelingGraphDragStart) / _modelingGraphCanvasView.Zoom;
        _modelingGraphNodePositions[nodeId] = _modelingGraphDragOrigin + delta;
        RefreshModelingGraphCanvas();
    }

    public void CompleteModelingGraphNodeDrag() => _modelingGraphDragNodeId = null;

    private (double X, double Y) DenormalizeGraphCanvasPoint(double normalizedX, double normalizedY) =>
        (normalizedX * 640, normalizedY * 360);

    private async Task AddModelingGraphLinkAsync(RekallAgeStudioModelingGraphPortKey from, RekallAgeStudioModelingGraphPortKey to)
    {
        var link = new RekallAgeModelingGraphLink($"link-{Guid.NewGuid():N}", from.NodeId, from.PortId, to.NodeId, to.PortId);
        var operation = new RekallAgeModelingGraphPatchOperation(RekallAgeModelingGraphPatchKind.AddLink, Link: link);
        await RunGraphModelingAsync(async () =>
        {
            await _modelingGraph.ApplyPatchAsync(new([operation]), "studio", _lifecycleCancellation.Token);
            Replace(ModelingGraphNodes, _modelingGraph.Nodes);
            ModelingGraphSummary = _modelingGraph.EvaluationSummary;
            RefreshModelingGraphCanvas();
        });
    }

    private async Task RunModelingAsync(Func<Task> operation)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await operation(); StatusText = MeshSummary; }
        catch (Exception exception) when (exception is IOException
                                            or InvalidOperationException
                                            or ArgumentException
                                            or JsonException
                                            or RekallAgeDocumentRevisionException
                                            or RekallAgeModelingGraphPatchException)
        {
            StatusText = exception.Message;
            Replace(MeshDiagnosticLines, [$"error: REKALL_STUDIO_MODELING_OPERATION_FAILED - {exception.Message}"]);
        }
        finally { IsBusy = false; }
    }

    private async Task RunGraphModelingAsync(Func<Task> operation)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await operation(); StatusText = ModelingGraphSummary; }
        catch (Exception exception) when (exception is IOException
                                            or InvalidOperationException
                                            or ArgumentException
                                            or JsonException
                                            or RekallAgeDocumentRevisionException
                                            or RekallAgeModelingGraphPatchException)
        {
            StatusText = exception.Message;
            Replace(ModelingGraphDiagnosticLines, [$"error: REKALL_STUDIO_MODELING_GRAPH_FAILED - {exception.Message}"]);
        }
        finally { IsBusy = false; }
    }

    private async Task RunMaterialGraphAsync(Func<Task> operation)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await operation(); StatusText = MaterialGraphSummary; }
        catch (Exception exception) when (exception is IOException
                                            or InvalidOperationException
                                            or InvalidDataException
                                            or ArgumentException
                                            or JsonException
                                            or RekallAgeDocumentRevisionException)
        {
            StatusText = exception.Message;
        }
        finally { IsBusy = false; }
    }

    private Task RefreshMaterialGraphsAsync() => RunMaterialGraphAsync(() =>
    {
        if (_session.ProjectRoot is null) throw new InvalidOperationException("Open a project before browsing material graphs.");
        Replace(MaterialGraphAssetIds, _materialGraph.ListAssets(_session.ProjectRoot));
        if (SelectedMaterialGraphAssetId is null || !MaterialGraphAssetIds.Contains(SelectedMaterialGraphAssetId))
            SelectedMaterialGraphAssetId = MaterialGraphAssetIds.FirstOrDefault();
        MaterialGraphSummary = MaterialGraphAssetIds.Count == 0
            ? "No persisted material graphs are present in Materials/Graphs."
            : $"{MaterialGraphAssetIds.Count} material graph asset(s) available.";
        return Task.CompletedTask;
    });

    private Task OpenMaterialGraphAsync() => RunMaterialGraphAsync(async () =>
    {
        await _materialGraph.OpenAsync(_session.ProjectRoot!, SelectedMaterialGraphAssetId!, _lifecycleCancellation.Token);
        Replace(MaterialGraphNodes, _materialGraph.Nodes);
        MaterialGraphSummary = _materialGraph.EvaluationSummary;
        SelectedMaterialGraphNode = MaterialGraphNodes.FirstOrDefault();
    });

    private Task ApplyMaterialGraphParametersAsync() => RunMaterialGraphAsync(async () =>
    {
        var selectedNodeId = SelectedMaterialGraphNode!.NodeId;
        await _materialGraph.ApplyParameterEditsAsync(selectedNodeId, MaterialGraphParameterEditors, "studio", _lifecycleCancellation.Token);
        Replace(MaterialGraphNodes, _materialGraph.Nodes);
        SelectedMaterialGraphNode = MaterialGraphNodes.FirstOrDefault(item => item.NodeId == selectedNodeId);
        MaterialGraphSummary = _materialGraph.EvaluationSummary;
    });

    private void RefreshMaterialGraphParameterEditors()
    {
        Replace(MaterialGraphParameterEditors, SelectedMaterialGraphNode is null
            ? []
            : _materialGraph.CreateParameterEditors(SelectedMaterialGraphNode.NodeId));
        foreach (var editor in MaterialGraphParameterEditors)
            editor.PropertyChanged += (_, _) => RefreshCommands();
        RefreshCommands();
    }

    private bool CanOpenMaterialGraph() => HasEditableProject() && !string.IsNullOrWhiteSpace(SelectedMaterialGraphAssetId);

    private bool CanApplyMaterialGraphParameters() =>
        HasEditableProject()
        && SelectedMaterialGraphNode is not null
        && MaterialGraphParameterEditors.Count > 0
        && MaterialGraphParameterEditors.All(item => item.IsValid)
        && MaterialGraphParameterEditors.Any(item => item.IsModified);

    private bool CanOpenAnimationMixer() => HasEditableProject() && _session.SelectedEntityId is not null;
    private bool CanApplyAnimationMixerLayers() => HasEditableProject() && AnimationMixerIsOpen && AnimationMixerLayers.Count > 0;

    private Task OpenAnimationMixerAsync() => RunModelingAsync(async () =>
    {
        await _animationMixer.OpenAsync(_session.ProjectRoot!, _session.SceneName!, _session.SelectedEntityId!, _lifecycleCancellation.Token);
        Replace(AnimationMixerLayers, _animationMixer.Layers);
        AnimationMixerIsOpen = _animationMixer.HasMixer;
        AnimationMixerSummary = _animationMixer.HasMixer
            ? $"{_animationMixer.EntityName}: {AnimationMixerLayers.Count} layer(s) in its authored Rekall.AnimationMixer."
            : $"{_animationMixer.EntityName} has no Rekall.AnimationMixer component to edit.";
    });

    private Task ApplyAnimationMixerLayersAsync() => RunModelingAsync(async () =>
    {
        await _animationMixer.ApplyAsync(AnimationMixerLayers, _lifecycleCancellation.Token);
        AnimationMixerSummary = $"{_animationMixer.EntityName}: applied {AnimationMixerLayers.Count} layer(s).";
    });

    private void RefreshModelingGraphParameterEditors()
    {
        Replace(ModelingGraphParameterEditors, SelectedModelingGraphNode is null
            ? []
            : _modelingGraph.CreateParameterEditors(SelectedModelingGraphNode.NodeId));
        foreach (var editor in ModelingGraphParameterEditors)
            editor.PropertyChanged += (_, _) => RefreshCommands();
        RefreshCommands();
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
            Replace(MeshAttributeSummaries, []); Replace(MeshMaterialSlotSummaries, []);
            _meshViewportFrame = null;
            MeshViewportImage = _meshViewportRenderer.RenderEmpty(640, 360, _meshViewportCamera);
            RefreshCommands();
            return;
        }
        var mesh = _modeling.Preview?.Mesh ?? _modeling.Mesh;
        Replace(MeshAttributeSummaries, mesh.Attributes.Select(attribute =>
            $"{attribute.Name} · {attribute.Domain} · {attribute.ValueType}{(attribute.Semantic is null ? string.Empty : $" · {attribute.Semantic}")} · {attribute.Values.Count} value(s)"));
        Replace(MeshMaterialSlotSummaries, mesh.MaterialSlots.Select(slot =>
            $"{slot.Name} · {(slot.MaterialAssetId is null ? "(unassigned)" : slot.MaterialAssetId)}"));
        var ids = MeshEditDomain switch
        {
            RekallAgeGeometryDomain.Point => mesh.Topology.PointIds,
            RekallAgeGeometryDomain.Edge => mesh.Topology.EdgeIds,
            RekallAgeGeometryDomain.Face => mesh.Topology.FaceIds,
            RekallAgeGeometryDomain.Corner => mesh.Topology.CornerIds,
            _ => []
        };
        Replace(MeshElementIds, ids);
        var selectedOperationId = SelectedMeshOperationId;
        Replace(MeshOperationIds, _modeling.AvailableOperations.Select(item => item.OperationId));
        SelectedMeshOperationId = selectedOperationId is not null && MeshOperationIds.Contains(selectedOperationId)
            ? selectedOperationId
            : MeshOperationIds.FirstOrDefault();
        if (SelectedMeshElementId is null || !MeshElementIds.Contains(SelectedMeshElementId.Value)) SelectedMeshElementId = MeshElementIds.Count == 0 ? null : MeshElementIds[0];
        Replace(MeshSelectionLines, _modeling.SelectedElementIds.Select((id, index) => $"{index + 1}. {MeshEditDomain} {id}{(id == _modeling.ActiveElementId ? " (active)" : string.Empty)}"));
        MeshSummary = $"{mesh.Name} r{mesh.Revision} · {mesh.Topology.PointIds.Count} points · {mesh.Topology.EdgeIds.Count} edges · {mesh.Topology.FaceIds.Count} faces · {_modeling.SelectedElementIds.Count} selected{(_modeling.Preview is null ? string.Empty : " · PREVIEW")}";
        _meshViewportFrame = _meshViewportRenderer.Render(
            mesh,
            MeshEditDomain,
            _modeling.SelectedElementIds,
            640,
            360,
            _modeling.Preview is not null,
            _meshViewportCamera,
            RekallAgeStudioViewportRenderStyles.Parse(MeshViewportRenderStyle));
        MeshViewportImage = _meshViewportFrame.Image;
        OnPropertyChanged(nameof(MeshEditDomain)); RefreshCommands();
    }

    private Task OpenFromInputsAsync() => RunAsync(
        () => _session.OpenAsync(ProjectPathInput, NormalizeSceneName(), CancellationToken.None).AsTask(),
        refreshPreviewAfter: true);

    internal Task OpenProjectAsync(string projectRoot)
    {
        ProjectPathInput = projectRoot;
        return RunAsync(
            () => _session.OpenAsync(projectRoot, CancellationToken.None).AsTask(),
            refreshPreviewAfter: true);
    }

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
        () => _session.UndoSinceOpenAsync("studio", CancellationToken.None).AsTask(),
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

    private Task RenameEntityAsync() => ExecuteSelectedEntityCommandAsync(
        "rekall.scene.entity.update_metadata",
        new { name = EntityNameInput.Trim() },
        $"Rename entity to {EntityNameInput.Trim()}");

    private Task DuplicateEntityAsync() => ExecuteSelectedEntityCommandAsync(
        "rekall.level.entity.duplicate",
        new { },
        $"Duplicate {_session.SelectedEntityId}");

    private Task DeleteEntityAsync() => ExecuteSelectedEntityCommandAsync(
        "rekall.entity.delete",
        new { },
        $"Delete {_session.SelectedEntityId}");

    private Task ToggleEntityVisibleAsync()
    {
        var selected = SelectedEntityNode();
        return selected is null
            ? Task.CompletedTask
            : ExecuteSelectedEntityCommandAsync(
                "rekall.scene.entity.update_metadata",
                new { visible = !selected.Visible },
                $"{(selected.Visible ? "Hide" : "Show")} {selected.Name}");
    }

    private Task ToggleEntityLockedAsync()
    {
        var selected = SelectedEntityNode();
        return selected is null
            ? Task.CompletedTask
            : ExecuteSelectedEntityCommandAsync(
                "rekall.scene.entity.update_metadata",
                new { locked = !selected.Locked },
                $"{(selected.Locked ? "Unlock" : "Lock")} {selected.Name}");
    }

    private Task ReparentEntityAsync() => ExecuteSelectedEntityCommandAsync(
        "rekall.scene.entity.update_metadata",
        new { parentId = ParentEntityIdInput.Trim() },
        $"Reparent {_session.SelectedEntityId}");

    private Task ClearEntityParentAsync() => ExecuteSelectedEntityCommandAsync(
        "rekall.scene.entity.update_metadata",
        new { clearParent = true },
        $"Unparent {_session.SelectedEntityId}");

    private Task ExecuteSelectedEntityCommandAsync(string commandName, object commandArguments, string transactionName)
    {
        if (_session.ProjectRoot is null || _session.SceneName is null || _session.SelectedEntityId is null)
            return Task.CompletedTask;
        var arguments = JsonSerializer.SerializeToNode(commandArguments)!.AsObject();
        arguments["projectRoot"] = _session.ProjectRoot;
        arguments["sceneName"] = _session.SceneName;
        arguments["entityId"] = _session.SelectedEntityId;
        return RunAsync(() => _session.ExecuteAsync(
            commandName,
            arguments.ToJsonString(),
            transactionName,
            "studio",
            CancellationToken.None).AsTask(), refreshPreviewAfter: true);
    }

    private RekallAgeSceneEntityNode? SelectedEntityNode() =>
        _session.SelectedEntityId is null ? null : FindEntityNode(EntityNodes, _session.SelectedEntityId);

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

    private Task CommitInspectorPropertyAsync(object? parameter)
    {
        if (parameter is not RekallAgeStudioInspectorPropertyEditorModel row
            || !row.TryCreateValue(out var value, out _))
        {
            return Task.CompletedTask;
        }

        return ExecuteComponentCommandAsync(
            "rekall.component.set_property",
            new
            {
                projectRoot = _session.ProjectRoot,
                sceneName = _session.SceneName,
                entityId = _session.SelectedEntityId,
                componentType = row.ComponentType,
                propertyName = row.Name,
                value
            },
            $"Set {row.ComponentType}.{row.Name}",
            row);
    }

    private Task ResetInspectorPropertyAsync(object? parameter)
    {
        if (parameter is not RekallAgeStudioInspectorPropertyEditorModel row) return Task.CompletedTask;
        return ExecuteComponentCommandAsync(
            "rekall.component.remove_property",
            new
            {
                projectRoot = _session.ProjectRoot,
                sceneName = _session.SceneName,
                entityId = _session.SelectedEntityId,
                componentType = row.ComponentType,
                propertyName = row.Name
            },
            $"Remove {row.ComponentType}.{row.Name}",
            row);
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

    public async Task OpenCodeSourceAsync(RekallAgeModuleSourceInfo source)
    {
        ArgumentNullException.ThrowIfNull(source);
        await RunCodeActionAsync(async () =>
        {
            await _codeSession.OpenAsync(source, _lifecycleCancellation.Token);
            SelectedCodeSource = _codeSession.SelectedSource;
            Set(ref _codeSourceText, _codeSession.SourceText, nameof(CodeSourceText));
            OnPropertyChanged(nameof(IsCodeDirty));
            OnPropertyChanged(nameof(SelectedCodeProjectPath));
            CodeStatusText = $"Editing {source.ModuleName}/{source.FileName}.";
        });
    }

    private Task RefreshCodeSourcesAsync() => RunCodeActionAsync(() => RefreshCodeSourcesCoreAsync());

    private async Task RefreshCodeSourcesCoreAsync(string? selectSourcePath = null)
    {
        if (_session.ProjectRoot is null) return;
        var sources = await _codeSession.RefreshAsync(_session.ProjectRoot, _lifecycleCancellation.Token);
        Replace(CodeSources, sources);
        var requestedPath = selectSourcePath ?? SelectedCodeSource?.SourcePath;
        var source = requestedPath is null
            ? CodeSources.FirstOrDefault()
            : CodeSources.FirstOrDefault(candidate => PathsEqual(candidate.SourcePath, requestedPath));
        if (source is not null)
        {
            await _codeSession.OpenAsync(source, _lifecycleCancellation.Token);
            SelectedCodeSource = _codeSession.SelectedSource;
            Set(ref _codeSourceText, _codeSession.SourceText, nameof(CodeSourceText));
        }
        else
        {
            SelectedCodeSource = null;
            Set(ref _codeSourceText, string.Empty, nameof(CodeSourceText));
        }
        OnPropertyChanged(nameof(IsCodeDirty));
        OnPropertyChanged(nameof(SelectedCodeProjectPath));
        CodeStatusText = CodeSources.Count == 0
            ? "No C# gameplay module sources exist yet."
            : $"Found {CodeSources.Count} C# gameplay source file(s).";
    }

    private Task SaveCodeSourceAsync() => RunCodeActionAsync(async () =>
    {
        await _codeSession.SaveAsync(_session.ProjectRoot!, _lifecycleCancellation.Token);
        OnPropertyChanged(nameof(IsCodeDirty));
        CodeStatusText = $"Saved {SelectedCodeSource?.FileName}.";
    });

    internal async Task SaveCodeChangesAsync()
    {
        await SaveCodeSourceAsync();
    }

    internal async Task DiscardCodeChangesAsync()
    {
        if (SelectedCodeSource is not null)
        {
            await OpenCodeSourceAsync(SelectedCodeSource);
        }
    }

    private Task BuildCodeAsync() => RunCodeActionAsync(async () =>
    {
        if (_session.ProjectRoot is null) return;
        CodeOutputLines.Clear();
        var result = await _session.ExecuteAsync(
            "rekall.build.modules",
            JsonSerializer.Serialize(new { projectRoot = _session.ProjectRoot }),
            "Build C# gameplay modules",
            "studio-code",
            _lifecycleCancellation.Token);
        AddCodeOperationResult("Build", result);
        if (_session.Model is not null) ApplyModel(_session.Model);
        CodeStatusText = result.Summary;
    });

    private async Task CreateAttachCodeComponentAsync()
    {
        if (_session.ProjectRoot is null || _session.SceneName is null || _session.SelectedEntityId is null) return;
        IsBusy = true;
        CodeOutputLines.Clear();
        try
        {
            var moduleName = CodeModuleNameInput.Trim();
            var componentName = CodeComponentNameInput.Trim();
            var systemName = CodeSystemNameInput.Trim();
            var scaffold = await _session.ExecuteAsync(
                "rekall.module.scaffold_runtime_system",
                JsonSerializer.Serialize(new
                {
                    projectRoot = _session.ProjectRoot,
                    moduleId = ToModuleId(moduleName),
                    displayName = HumanizeIdentifier(moduleName),
                    moduleName,
                    componentName,
                    systemName
                }),
                $"Scaffold {moduleName}",
                "studio-code",
                _lifecycleCancellation.Token);
            AddCodeOperationResult("Scaffold", scaffold);
            var scaffoldValue = scaffold.Value as ScaffoldRuntimeSystemModuleResult;
            if (!scaffold.Ok)
            {
                await RefreshCodeSourcesCoreAsync(scaffoldValue?.SourcePath);
                CodeStatusText = scaffold.Summary;
                return;
            }
            if (scaffoldValue is null)
            {
                throw new InvalidOperationException("The runtime module scaffold did not return its generated component contract.");
            }

            var build = await _session.ExecuteAsync(
                "rekall.build.modules",
                JsonSerializer.Serialize(new { projectRoot = _session.ProjectRoot }),
                $"Build {moduleName}",
                "studio-code",
                _lifecycleCancellation.Token);
            AddCodeOperationResult("Build", build);
            if (!build.Ok)
            {
                await RefreshCodeSourcesCoreAsync(scaffoldValue.SourcePath);
                CodeStatusText = build.Summary;
                return;
            }

            var componentType = $"{scaffoldValue.Namespace}.{scaffoldValue.ComponentClass}";
            var attach = await _session.ExecuteAsync(
                "rekall.component.add",
                JsonSerializer.Serialize(new
                {
                    projectRoot = _session.ProjectRoot,
                    sceneName = _session.SceneName,
                    entityId = _session.SelectedEntityId,
                    componentType,
                    properties = new { enabled = true, valuePerSecond = 1d }
                }),
                $"Attach {componentType}",
                "studio-code",
                _lifecycleCancellation.Token);
            AddCodeOperationResult("Attach", attach);
            if (_session.Model is not null) ApplyModel(_session.Model);
            await RefreshCodeSourcesCoreAsync(scaffoldValue.SourcePath);
            CodeStatusText = attach.Ok
                ? $"Created, built, and attached {componentType}."
                : attach.Summary;
            if (attach.Ok && IsLiveViewportEnabled && Mode == RekallAgeStudioMode.Edit)
            {
                await RefreshEditPreviewAsync(CodeStatusText);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            ReportCodeFailure(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task OpenCodeFileAsync() => RunCodeActionAsync(() =>
    {
        _codeSession.OpenSelectedFile();
        CodeStatusText = $"Opened {SelectedCodeSourcePath}.";
        return Task.CompletedTask;
    });

    private Task OpenCodeProjectAsync() => RunCodeActionAsync(() =>
    {
        _codeSession.OpenSelectedProject();
        CodeStatusText = $"Opened {SelectedCodeProjectPath}.";
        return Task.CompletedTask;
    });

    private Task OpenCodeSolutionAsync() => OpenGeneratedCodeWorkspaceAsync(openVsCode: false);

    private Task OpenCodeInVsCodeAsync() => OpenGeneratedCodeWorkspaceAsync(openVsCode: true);

    private Task OpenGeneratedCodeWorkspaceAsync(bool openVsCode) => RunCodeActionAsync(async () =>
    {
        var player = ResolvePlayerExecutable()
            ?? throw new InvalidOperationException("Player executable was not found. Build or install Rekall.Age.Player.Windows first.");
        var workspace = await _codeSession.GenerateDevelopmentWorkspaceAsync(
            _session.ProjectRoot!,
            _session.SceneName!,
            player,
            ResolveCliExecutable(),
            _lifecycleCancellation.Token);
        OnPropertyChanged(nameof(CodeSolutionPath));
        OnPropertyChanged(nameof(CodeVsCodeLaunchPath));
        CodeStatusText = $"Generated IDE workspace at {workspace.SolutionPath}.";
        try
        {
            if (openVsCode) _codeSession.OpenInVsCode();
            else _codeSession.OpenSolution();
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new InvalidOperationException(
                $"IDE workspace generated at {workspace.SolutionPath}. Automatic launch failed: {exception.Message}",
                exception);
        }
        CodeStatusText = openVsCode
            ? $"Opened VS Code at {_session.ProjectRoot}."
            : $"Opened {workspace.SolutionPath}.";
    });

    private async Task RunCodeActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            ReportCodeFailure(ex);
        }
        finally
        {
            RefreshCommands();
        }
    }

    private void AddCodeOperationResult(string stage, RekallAgeWorkbenchOperationResult result)
    {
        CodeOutputLines.Add($"{stage}: {result.Summary}");
        foreach (var error in result.Errors)
        {
            CodeOutputLines.Add($"{error.Code}: {error.Message}{(error.Target is null ? string.Empty : $" ({error.Target})")}");
        }
        if (result.Value is Rekall.Age.Build.Commands.BuildModulesResult built)
        {
            foreach (var module in built.Modules)
            {
                CodeOutputLines.Add($"{module.ModuleName}: {(module.Succeeded ? "succeeded" : "failed")} (exit {module.ExitCode})");
                foreach (var line in module.Output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
                             .Where(line => !string.IsNullOrWhiteSpace(line)))
                {
                    CodeOutputLines.Add(line);
                }
            }
        }
    }

    private void ReportCodeFailure(Exception exception)
    {
        CodeStatusText = exception.Message;
        CodeOutputLines.Add($"REKALL_STUDIO_CODE_FAILED: {exception.Message}");
    }

    private Task ExecuteComponentCommandAsync(
        string commandName,
        object arguments,
        string transactionName,
        RekallAgeStudioInspectorPropertyEditorModel? rejectedInspectorRow = null) =>
        RunAsync(() => _session.ExecuteAsync(
            commandName,
            JsonSerializer.Serialize(arguments),
            transactionName,
            "studio",
            CancellationToken.None).AsTask(), refreshPreviewAfter: true, rejectedInspectorRow: rejectedInspectorRow);

    private Task AttachQualityProfileAsync() => RunAsync(() => _session.ExecuteAsync(
        "rekall.component.add",
        JsonSerializer.Serialize(new
        {
            projectRoot = _session.ProjectRoot,
            sceneName = _session.SceneName,
            entityId = _session.SelectedEntityId,
            componentType = "Rekall.RenderQualityProfile",
            properties = new JsonObject { ["preset"] = SelectedQualityPreset }
        }),
        "Attach render quality profile",
        "studio",
        CancellationToken.None).AsTask(), refreshPreviewAfter: true);

    private async Task<RekallAgeWorkbenchOperationResult> ApplyRenderQualityAsync()
    {
        if (_currentModel?.Rendering.Authoring is not { } authoring)
        {
            return InvalidQualityInput("Attach a Rekall.RenderQualityProfile before applying authored quality settings.");
        }
        if (!TryBuildQualityOverrides(out var overrides, out var parseError))
        {
            return InvalidQualityInput(parseError);
        }

        var mutations = new (string Property, JsonNode? Value)[]
        {
            ("preset", JsonValue.Create(SelectedQualityPreset)),
            ("resolutionScale", overrides.ResolutionScale.HasValue ? JsonValue.Create(overrides.ResolutionScale.Value) : null),
            ("shadowCascadeCount", overrides.ShadowCascadeCount.HasValue ? JsonValue.Create(overrides.ShadowCascadeCount.Value) : null),
            ("shadowResolution", overrides.ShadowResolution.HasValue ? JsonValue.Create(overrides.ShadowResolution.Value) : null),
            ("fogMode", string.IsNullOrWhiteSpace(overrides.FogMode) ? null : JsonValue.Create(overrides.FogMode)),
            ("bloom", overrides.Bloom.HasValue ? JsonValue.Create(overrides.Bloom.Value) : null),
            ("ssao", overrides.Ssao.HasValue ? JsonValue.Create(overrides.Ssao.Value) : null),
            ("maximumActiveParticles", overrides.MaximumActiveParticles.HasValue ? JsonValue.Create(overrides.MaximumActiveParticles.Value) : null)
        };

        RekallAgeWorkbenchOperationResult? result = null;
        foreach (var mutation in mutations)
        {
            var remove = mutation.Value is null;
            result = await _session.ExecuteAsync(
                remove ? "rekall.component.remove_property" : "rekall.component.set_property",
                remove
                    ? JsonSerializer.Serialize(new
                    {
                        projectRoot = _session.ProjectRoot,
                        sceneName = _session.SceneName,
                        entityId = authoring.EntityId,
                        componentType = "Rekall.RenderQualityProfile",
                        propertyName = mutation.Property
                    })
                    : JsonSerializer.Serialize(new
                    {
                        projectRoot = _session.ProjectRoot,
                        sceneName = _session.SceneName,
                        entityId = authoring.EntityId,
                        componentType = "Rekall.RenderQualityProfile",
                        propertyName = mutation.Property,
                        value = mutation.Value
                    }),
                $"Set render quality {mutation.Property}",
                "studio",
                CancellationToken.None);
            if (!result.Ok) return result;
        }

        return result! with { Summary = $"Set requested render quality to '{SelectedQualityPreset}' through generic component mutations." };
    }

    private async Task<RekallAgeWorkbenchOperationResult> CaptureQualityAsync(
        CancellationToken cancellationToken)
    {
        if (!TryBuildQualityOverrides(out var overrides, out var parseError))
        {
            return InvalidQualityInput(parseError);
        }

        return await _session.ExecuteAsync(
            "rekall.render.capture_runtime_viewport",
            JsonSerializer.Serialize(new
            {
                projectRoot = _session.ProjectRoot,
                sceneName = _session.SceneName,
                frames = 1,
                outputDirectory = Path.Combine(_session.ProjectRoot!, "Artifacts", "Studio", "HighFidelity"),
                width = 960,
                height = 540,
                debugOverlay = false,
                backendId = "vulkan",
                qualityPreset = SelectedQualityPreset,
                qualityOverrides = overrides,
                includeGpuTimings = true
            }),
            $"Capture {SelectedQualityPreset} render quality",
            "studio",
            cancellationToken);
    }

    private async Task<RekallAgeWorkbenchOperationResult> CompareQualityAsync(
        CancellationToken cancellationToken)
    {
        if (!TryBuildQualityOverrides(out var overrides, out var parseError))
        {
            return InvalidQualityInput(parseError);
        }

        return await _session.ExecuteAsync(
            "rekall.render.compare_quality_presets",
            JsonSerializer.Serialize(new
            {
                projectRoot = _session.ProjectRoot,
                sceneName = _session.SceneName,
                presets = new[] { SelectedQualityPreset, ComparisonQualityPreset },
                frames = 1,
                outputDirectory = Path.Combine(_session.ProjectRoot!, "Artifacts", "Studio", "QualityComparison"),
                width = 960,
                height = 540,
                backendId = "vulkan",
                overrides,
                includeGpuTimings = true
            }),
            $"Compare {SelectedQualityPreset} and {ComparisonQualityPreset} render quality",
            "studio",
            cancellationToken);
    }

    private bool TryBuildQualityOverrides(
        out RekallAgeRenderQualityOverrides overrides,
        out string? error)
    {
        if (!TryParseOptionalDouble(QualityResolutionScaleInput, out var resolutionScale))
        {
            overrides = new();
            error = "Resolution scale must be a finite number or blank.";
            return false;
        }
        if (!TryParseOptionalInt32(QualityShadowCascadeCountInput, out var shadowCascadeCount))
        {
            overrides = new();
            error = "Shadow cascade count must be an integer or blank.";
            return false;
        }
        if (!TryParseOptionalInt32(QualityShadowResolutionInput, out var shadowResolution))
        {
            overrides = new();
            error = "Shadow resolution must be an integer or blank.";
            return false;
        }
        if (!TryParseOptionalInt32(QualityMaximumActiveParticlesInput, out var maximumActiveParticles))
        {
            overrides = new();
            error = "Maximum active particles must be an integer or blank.";
            return false;
        }

        overrides = new RekallAgeRenderQualityOverrides(
            resolutionScale,
            shadowCascadeCount,
            shadowResolution,
            string.IsNullOrWhiteSpace(QualityFogModeInput) ? null : QualityFogModeInput.Trim(),
            QualityBloomOverride,
            QualitySsaoOverride,
            maximumActiveParticles);
        error = null;
        return true;
    }

    private static bool TryParseOptionalDouble(string input, out double? value)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            value = null;
            return true;
        }

        if ((double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
             || double.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
            && double.IsFinite(parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryParseOptionalInt32(string input, out int? value)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            value = null;
            return true;
        }

        if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            || int.TryParse(input, NumberStyles.Integer, CultureInfo.CurrentCulture, out parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    private static RekallAgeWorkbenchOperationResult InvalidQualityInput(string? message)
    {
        const string code = "REKALL_STUDIO_RENDER_QUALITY_INPUT_INVALID";
        message ??= "Render-quality controls contain an invalid value.";
        return new(false, message, null, [new RekallAgeCommandError(code, message, "render-quality")]);
    }

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
            CreatePackageRequestJson(_session.ProjectRoot!, _session.SceneName!),
            "Package playable game",
            "studio",
            CancellationToken.None);
        if (result.Ok && result.Value is PackagePlayableGameResult package && package.Ready)
        {
            ApplyPackageResult(package);
            AppendAgentLine($"package: {package.ArchivePath}");
        }
        return result;
    }

    internal string CreatePackageRequestJson(string projectRoot, string sceneName) =>
        JsonSerializer.Serialize(new
        {
            projectRoot,
            sceneName,
            outputDirectory = Path.Combine(projectRoot, "Builds", "StudioPackage"),
            frames = 2,
            target = SelectedPackageTarget
        });

    internal void ApplyPackageResult(PackagePlayableGameResult package)
    {
        LastPackageOutputDirectory = package.OutputDirectory;
        LastPackageLaunchPath = package.LaunchPath;
        LastPackagePath = package.ArchivePath;
    }

    private Task OpenPackageFolderAsync()
    {
        _openPackageFolder(LastPackageOutputDirectory!);
        return Task.CompletedTask;
    }

    private static void OpenDirectoryInExplorer(string path)
    {
        Process.Start(new ProcessStartInfo("explorer.exe")
        {
            ArgumentList = { path },
            UseShellExecute = true
        });
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

    private Task PublishWebAsync() => RunAsync(PublishWebOperationAsync);

    private async Task<RekallAgeWorkbenchOperationResult> PublishWebOperationAsync()
    {
        var result = await _session.ExecuteAsync(
            "rekall.game.publish_web",
            JsonSerializer.Serialize(new
            {
                projectRoot = _session.ProjectRoot,
                sceneName = _session.SceneName,
                outputDirectory = Path.Combine(_session.ProjectRoot!, "Builds", "StudioWebPublish")
            }),
            "Publish web game",
            "studio",
            CancellationToken.None);
        if (result.Ok && result.Value is PublishWebGameResult publish && publish.Ready)
        {
            LastWebPublishPath = publish.OutputDirectory;
            AppendAgentLine($"publish-web: {publish.OutputDirectory}");
        }
        return result;
    }

    private Task AuditWebAsync() => RunAsync(() => _session.ExecuteAsync(
        "rekall.game.audit_web",
        JsonSerializer.Serialize(new
        {
            projectRoot = _session.ProjectRoot,
            sceneName = _session.SceneName,
            outputDirectory = Path.Combine(_session.ProjectRoot!, "Builds", "StudioWebPublish")
        }),
        "Audit web game",
        "studio",
        CancellationToken.None).AsTask());

    private async Task SignInCodexAsync()
    {
        if (_languageModelRunner is not IRekallAgeCodexProjectAgentRunner runner
            || CodexAuthenticationLauncher is null)
        {
            ProviderStatus = "REKALL_CODEX_LOGIN_LAUNCH_UNAVAILABLE: Studio cannot open the Codex sign-in page.";
            return;
        }

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifecycleCancellation.Token);
        _codexSignInCancellation = cancellation;
        ProviderStatus = "Opening Codex ChatGPT sign-in…";
        RefreshCommands();
        try
        {
            var signIn = runner.SignInWithChatGptAsync(
                CodexAuthenticationLauncher, cancellation.Token).AsTask();
            _activeCodexSignIn = signIn;
            var account = await signIn;
            ProviderStatus = $"Codex sign-in completed via {CodexAuthenticationLabel(account.AuthenticationType ?? string.Empty)}. Refreshing models…";
            await DiscoverModelsAsync();
        }
        catch (RekallAgeLanguageModelProviderException error)
        {
            ProviderStatus = $"{error.Code}: {error.Message}";
            StatusText = ProviderStatus;
        }
        finally
        {
            if (ReferenceEquals(_codexSignInCancellation, cancellation)) _codexSignInCancellation = null;
            RefreshCommands();
        }
    }

    private async Task CancelCodexSignInAsync()
    {
        _codexSignInCancellation?.Cancel();
        if (_activeCodexSignIn is not null)
        {
            try { await _activeCodexSignIn; }
            catch (RekallAgeLanguageModelProviderException) { }
        }
    }

    private Task DiscoverModelsAsync(bool propagateProviderFailure = false)
    {
        lock (_languageModelLifecycleSync)
        {
            if (_activeLanguageModelRefresh is { IsCompleted: false }) return _activeLanguageModelRefresh;
            var runner = _languageModelRunner;
            if (runner is null) return Task.CompletedTask;
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifecycleCancellation.Token);
            _languageModelRefreshCancellation = cancellation;
            _activeLanguageModelRefresh = DiscoverModelsCoreAsync(
                cancellation,
                SelectedLanguageModelProvider,
                runner,
                _languageModelProviderTransitionGeneration,
                propagateProviderFailure);
            return _activeLanguageModelRefresh;
        }
    }

    private async Task DiscoverModelsCoreAsync(
        CancellationTokenSource operationCancellation,
        RekallAgeLanguageModelProviderDescriptor provider,
        IRekallAgeProjectAgentRunner runner,
        long generation,
        bool propagateProviderFailure)
    {
        IsBusy = true;
        try
        {
            var models = await runner.ListModelsAsync(operationCancellation.Token);
            TryApplyLanguageModels(provider, runner, generation, models);
        }
        catch (RekallAgeLanguageModelProviderException exception)
        {
            ReportLanguageModelProviderFailureIfCurrent(provider, runner, generation, exception);
            if (propagateProviderFailure) throw;
        }
        catch (OperationCanceledException) when (operationCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_languageModelLifecycleSync)
            {
                if (ReferenceEquals(_languageModelRefreshCancellation, operationCancellation))
                {
                    _languageModelRefreshCancellation = null;
                }
            }
            operationCancellation.Dispose();
            IsBusy = false;
        }
    }

    private Task RunAgentAsync()
    {
        Task operationTask;
        lock (_languageModelLifecycleSync)
        {
            if (_activeAgentRun is { IsCompleted: false }) return _activeAgentRun;
            var runner = _languageModelRunner;
            if (runner is null) return Task.CompletedTask;
            operationTask = RunAgentCoreAsync(
                runner,
                runner.ProviderId,
                SelectedLanguageModel);
            _activeAgentRun = operationTask;
        }

        return AwaitAgentRunAsync(operationTask);
    }

    private async Task AwaitAgentRunAsync(Task operationTask)
    {
        try
        {
            await operationTask.ConfigureAwait(false);
        }
        finally
        {
            lock (_languageModelLifecycleSync)
            {
                if (ReferenceEquals(_activeAgentRun, operationTask) && !operationTask.IsFaulted)
                {
                    _activeAgentRun = null;
                }
            }
        }
    }

    private async Task RunAgentCoreAsync(
        IRekallAgeProjectAgentRunner runner,
        string providerId,
        string model)
    {
        if (_session.ProjectRoot is null || _session.SceneName is null) return;
        _agentCancellation?.Dispose();
        _agentCancellation = new CancellationTokenSource();
        var cancellationToken = _agentCancellation.Token;
        IsAgentRunning = true;
        IsBusy = true;
        AgentActivityText = $"Starting {providerId} · {model}…";
        AgentLines.Clear();
        AgentMessageLines.Clear();
        _lastAgentToolExecutions.Clear();
        AppendAgentLine($"provider: {providerId}");
        AppendAgentLine($"model: {model}");
        AppendAgentLine($"task: {AgentTaskInput.Trim()}");
        Log.Information(
            "Studio authoring started. Provider={ProviderId} Model={Model} ProjectRoot={ProjectRoot} Scene={SceneName} MaxTurns={MaxTurns}",
            providerId,
            model,
            _session.ProjectRoot,
            _session.SceneName,
            AgentMaxTurns);
        var agentStopwatch = Stopwatch.StartNew();
        try
        {
            IProgress<RekallAgeLanguageModelAgentProgress> progress = SynchronizationContext.Current is null
                ? new ImmediateProgress<RekallAgeLanguageModelAgentProgress>(ReportAgentProgress)
                : new Progress<RekallAgeLanguageModelAgentProgress>(ReportAgentProgress);
            var result = await runner.RunAsync(
                new RekallAgeProjectAgentSessionRequest(
                    _session.ProjectRoot,
                    _session.SceneName,
                    model,
                    AgentTaskInput)
                {
                    MaxTurns = AgentMaxTurns,
                    Think = SelectedReasoningEffort,
                    RequireCompletionAudit = true,
                    RequireCompletionAuditToolEvidence = !TreatGauntletAsTerminalSuccess,
                    TreatGauntletAsTerminalSuccess = TreatGauntletAsTerminalSuccess
                },
                progress,
                cancellationToken);
            agentStopwatch.Stop();
            _lastAgentToolExecutions.Clear();
            _lastAgentToolExecutions.AddRange(result.AgentResult.ToolExecutions);
            if (!string.IsNullOrWhiteSpace(result.AgentResult.ResponseId))
            {
                AppendAgentLine($"response: {result.AgentResult.ResponseId}");
            }
            AppendAgentLine(
                $"usage: input={result.AgentResult.Usage.PromptTokens} output={result.AgentResult.Usage.CompletionTokens} "
                + $"cached={result.AgentResult.Usage.CachedInputTokens?.ToString(CultureInfo.InvariantCulture) ?? "unavailable"} "
                + $"reasoning={result.AgentResult.Usage.ReasoningTokens?.ToString(CultureInfo.InvariantCulture) ?? "unavailable"}");
            AppendAgentLine($"tools: {result.AgentResult.ToolCallCount}");
            AppendAgentLine($"elapsed: {agentStopwatch.Elapsed.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)} ms");
            AppendAgentLine(result.Summary);
            Log.Information(
                "Studio authoring completed. Provider={ProviderId} Model={Model} Succeeded={Succeeded} Turns={Turns} ToolCalls={ToolCalls} ElapsedMilliseconds={ElapsedMilliseconds}",
                providerId,
                model,
                result.Succeeded,
                result.AgentResult.Turns,
                result.AgentResult.ToolCallCount,
                agentStopwatch.Elapsed.TotalMilliseconds);
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
            var capture = await CaptureOperationAsync(cancellationToken);
            StatusText = result.Succeeded && validation.Ok && capture.Ok
                ? result.Summary
                : $"{result.Summary} Review Validation and AI Agent output.";
            AgentActivityText = StatusText;
        }
        catch (RekallAgeLanguageModelProviderException exception)
        {
            Log.Warning(
                "Studio authoring provider failure. Provider={ProviderId} Code={Code} HttpStatus={HttpStatus} Retryable={Retryable}",
                exception.ProviderId,
                exception.Code,
                exception.HttpStatus,
                exception.Retryable);
            ReportLanguageModelProviderFailure(exception);
            AppendAgentLine(ProviderStatus);
            AgentActivityText = ProviderStatus;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Log.Information(
                "Studio authoring cancelled. Provider={ProviderId} Model={Model} ElapsedMilliseconds={ElapsedMilliseconds}",
                providerId,
                model,
                agentStopwatch.Elapsed.TotalMilliseconds);
            var reload = await _session.ReloadAsync(CancellationToken.None);
            if (reload.Ok && _session.Model is not null) ApplyModel(_session.Model);
            StatusText = "AI authoring cancelled.";
            AgentActivityText = StatusText;
            AppendAgentLine("cancelled by user");
        }
        catch (Exception)
        {
            AgentActivityText = "AI authoring failed. See Validation for details.";
            throw;
        }
        finally
        {
            _agentCancellation?.Dispose();
            _agentCancellation = null;
            IsAgentRunning = false;
            IsBusy = false;
        }
    }

    private async Task CancelAgentAsync()
    {
        _agentCancellation?.Cancel();
        Task? activeRun;
        lock (_languageModelLifecycleSync)
        {
            activeRun = _activeAgentRun;
        }
        if (activeRun is null) return;
        StatusText = "Cancelling AI authoring…";
        AgentActivityText = StatusText;
        try
        {
            await activeRun;
        }
        catch (Exception)
        {
            // The command boundary already records the bounded, redacted failure.
            // Provider transition must still release the old runner and acquire the selected provider.
        }
        finally
        {
            lock (_languageModelLifecycleSync)
            {
                if (ReferenceEquals(_activeAgentRun, activeRun)) _activeAgentRun = null;
            }
        }
    }

    public Task ApplyOpenAiApiKeyAsync(string? apiKey)
    {
        lock (_languageModelLifecycleSync)
        {
            _sessionOpenAiApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
            OnPropertyChanged(nameof(HasSessionOpenAiCredential));
            OnPropertyChanged(nameof(OpenAiCredentialSourceLabel));
            OnPropertyChanged(nameof(LanguageModelProviders));
            _selectedLanguageModelProvider = LanguageModelProviders.Single(
                provider => provider.Id == _selectedLanguageModelProvider.Id);
            OnPropertyChanged(nameof(SelectedLanguageModelProvider));
            if (SelectedLanguageModelProvider.Id != "openai")
            {
                ProviderStatus = _sessionOpenAiApiKey is null
                    ? "OpenAI session key cleared."
                    : "OpenAI session key accepted in memory for this Studio session.";
                return Task.CompletedTask;
            }

            Replace(LanguageModels, []);
            SelectedLanguageModel = string.Empty;
            ProviderStatus = "Refreshing OpenAI session authentication…";
            QueueLanguageModelProviderTransition(SelectedLanguageModelProvider);
            return _languageModelProviderTransition;
        }
    }

    internal async Task RestoreLanguageModelSetupAsync(
        RekallAgeStudioLanguageModelSetup setup,
        string? openAiSessionKey,
        string? kimiSessionKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(setup);
        var normalized = RekallAgeStudioLanguageModelSetup.Normalize(setup)
            ?? throw new ArgumentException("Language-model setup is incompatible.", nameof(setup));
        cancellationToken.ThrowIfCancellationRequested();

        Task requestedTransition;
        var provider = LanguageModelProviders.SingleOrDefault(candidate =>
            candidate.Id.Equals(normalized.ProviderId, StringComparison.Ordinal))
            ?? throw new ArgumentException("Language-model setup provider is unavailable.", nameof(setup));
        lock (_languageModelLifecycleSync)
        {
            _sessionOpenAiApiKey = string.IsNullOrWhiteSpace(openAiSessionKey) ? null : openAiSessionKey;
            _sessionKimiApiKey = string.IsNullOrWhiteSpace(kimiSessionKey) ? null : kimiSessionKey;
            _sessionOllamaUrl = normalized.OllamaUrl;
            _sessionOpenAiUrl = normalized.OpenAiUrl;
            _sessionKimiUrl = normalized.KimiUrl;
            OnPropertyChanged(nameof(HasSessionOpenAiCredential));
            OnPropertyChanged(nameof(HasSessionKimiCredential));
            OnPropertyChanged(nameof(OpenAiCredentialSourceLabel));
            OnPropertyChanged(nameof(KimiCredentialSourceLabel));
            OnPropertyChanged(nameof(LanguageModelProviders));
            provider = LanguageModelProviders.Single(candidate =>
                candidate.Id.Equals(normalized.ProviderId, StringComparison.Ordinal));

            if (!SelectedLanguageModelProvider.Id.Equals(provider.Id, StringComparison.Ordinal))
            {
                SelectedLanguageModelProvider = provider;
            }
            else
            {
                _selectedLanguageModelProvider = provider;
                OnPropertyChanged(nameof(SelectedLanguageModelProvider));
                Replace(LanguageModels, []);
                SelectedLanguageModel = string.Empty;
                ProviderStatus = $"Restoring {provider.DisplayName} setup…";
                ProviderDisplayStatus = ProviderStatus;
                QueueLanguageModelProviderTransition(provider);
            }
            requestedTransition = _languageModelProviderTransition;
        }

        await requestedTransition.WaitAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_languageModelLifecycleSync)
        {
            if (!ReferenceEquals(requestedTransition, _languageModelProviderTransition)
                || !SelectedLanguageModelProvider.Id.Equals(provider.Id, StringComparison.Ordinal))
            {
                return;
            }

            if (LanguageModels.Contains(normalized.ModelId, StringComparer.Ordinal))
            {
                SelectedLanguageModel = normalized.ModelId;
            }
            SelectedReasoningEffort = normalized.ReasoningEffort;
        }
    }

    internal Task WaitForLanguageModelProviderTransitionAsync()
    {
        lock (_languageModelLifecycleSync)
        {
            return _languageModelProviderTransition;
        }
    }

    private void QueueLanguageModelProviderTransition(
        RekallAgeLanguageModelProviderDescriptor provider)
    {
        lock (_languageModelLifecycleSync)
        {
            var generation = checked(++_languageModelProviderTransitionGeneration);
            _languageModelProviderTransition = TransitionLanguageModelProviderAfterAsync(
                _languageModelProviderTransition,
                provider,
                generation,
                SessionLanguageModelProviderSettings());
        }
    }

    private async Task TransitionLanguageModelProviderAfterAsync(
        Task previousTransition,
        RekallAgeLanguageModelProviderDescriptor provider,
        long generation,
        RekallAgeLanguageModelProviderSettings? sessionSettings)
    {
        RekallAgeLanguageModelProviderLease? acquiredLease = null;
        try
        {
            await previousTransition;
            if (!IsCurrentLanguageModelTransition(provider, generation)) return;
            await CancelAndAwaitLanguageModelRefreshAsync();
            await CancelAndAwaitCodexSignInAsync();
            await CancelAgentAsync();
            await ReleaseLanguageModelRunnerAsync();
            _lifecycleCancellation.Token.ThrowIfCancellationRequested();
            acquiredLease = _languageModelProviderCatalog.Acquire(
                provider.Id,
                _agentRegistry,
                sessionSettings);
            var runner = acquiredLease.Runner;
            if (runner is IRekallAgeCodexProjectAgentRunner codexRunner)
            {
                codexRunner.ApprovalCallback = RouteCodexApprovalAsync;
            }
            IReadOnlyList<RekallAgeLanguageModelInfo> models;
            try
            {
                models = await runner.ListModelsAsync(_lifecycleCancellation.Token);
            }
            catch (RekallAgeLanguageModelProviderException exception)
                when (exception.Code == RekallAgeCodexErrorCodes.AuthenticationRequired
                    && runner is IRekallAgeCodexProjectAgentRunner)
            {
                lock (_languageModelLifecycleSync)
                {
                    if (!IsCurrentLanguageModelTransitionLocked(provider, generation)) return;
                    _languageModelProviderLease = acquiredLease;
                    _languageModelRunner = runner;
                    acquiredLease = null;
                }
                ReportLanguageModelTransitionFailureIfCurrent(provider, generation, exception);
                return;
            }
            lock (_languageModelLifecycleSync)
            {
                if (!IsCurrentLanguageModelTransitionLocked(provider, generation)) return;
                _languageModelProviderLease = acquiredLease;
                _languageModelRunner = runner;
                acquiredLease = null;
                ApplyLanguageModels(EffectiveProviderDescriptor(provider, runner), models);
            }
        }
        catch (RekallAgeLanguageModelProviderException exception)
        {
            ReportLanguageModelTransitionFailureIfCurrent(provider, generation, exception);
        }
        catch (OperationCanceledException) when (_lifecycleCancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            ReportUnexpectedLanguageModelTransitionFailureIfCurrent(provider, generation);
        }
        finally
        {
            try
            {
                if (acquiredLease is not null)
                {
                    await acquiredLease.DisposeAsync();
                }
            }
            catch (Exception)
            {
                ReportUnexpectedLanguageModelTransitionFailureIfCurrent(provider, generation);
            }
            RefreshCommands();
        }
    }

    private async Task CancelAndAwaitLanguageModelRefreshAsync()
    {
        CancellationTokenSource? cancellation;
        Task? refresh;
        lock (_languageModelLifecycleSync)
        {
            cancellation = _languageModelRefreshCancellation;
            refresh = _activeLanguageModelRefresh;
        }
        cancellation?.Cancel();
        if (refresh is null) return;
        try
        {
            await refresh;
        }
        catch (Exception)
        {
            // The command boundary already records the bounded, redacted failure.
            // Provider transition must still release the old runner and acquire the selected provider.
        }
        finally
        {
            lock (_languageModelLifecycleSync)
            {
                if (ReferenceEquals(_activeLanguageModelRefresh, refresh)) _activeLanguageModelRefresh = null;
            }
        }
    }

    private async Task CancelAndAwaitCodexSignInAsync()
    {
        _codexSignInCancellation?.Cancel();
        var signIn = _activeCodexSignIn;
        if (signIn is null) return;
        try { await signIn; }
        catch (RekallAgeLanguageModelProviderException) { }
    }

    private bool TryApplyLanguageModels(
        RekallAgeLanguageModelProviderDescriptor provider,
        IRekallAgeProjectAgentRunner runner,
        long generation,
        IReadOnlyList<RekallAgeLanguageModelInfo> models)
    {
        lock (_languageModelLifecycleSync)
        {
            if (!IsCurrentLanguageModelTransitionLocked(provider, generation)
                || !ReferenceEquals(_languageModelRunner, runner)) return false;
            ApplyLanguageModels(EffectiveProviderDescriptor(provider, runner), models);
            return true;
        }
    }

    private static RekallAgeLanguageModelProviderDescriptor EffectiveProviderDescriptor(
        RekallAgeLanguageModelProviderDescriptor provider,
        IRekallAgeProjectAgentRunner runner) =>
        runner is IRekallAgeCodexProjectAgentRunner codexRunner
            ? codexRunner.CurrentProviderDescriptor
            : provider;

    private void ApplyLanguageModels(
        RekallAgeLanguageModelProviderDescriptor provider,
        IReadOnlyList<RekallAgeLanguageModelInfo> models)
    {
        var previousSelection = SelectedLanguageModel;
        var isOllamaBacked = provider.Id is "ollama" or "gguf";
        var authoringModels = isOllamaBacked
            ? models.Where(model => model.SupportsTools is not false).ToArray()
            : models.ToArray();
        Replace(LanguageModels, authoringModels.Select(model => model.Id));
        OnPropertyChanged(nameof(HasUsableLanguageModel));
        if (LanguageModels.Contains(previousSelection, StringComparer.Ordinal))
        {
            SelectedLanguageModel = previousSelection;
            SetLanguageModelProviderReadyStatus(provider);
            return;
        }

        if (!LanguageModels.Contains(provider.DefaultModel))
        {
            if (LanguageModels.Count == 0)
            {
                SelectedLanguageModel = string.Empty;
                ReportLanguageModelProviderFailure(new RekallAgeLanguageModelProviderException(
                    "REKALL_LANGUAGE_MODEL_DEFAULT_UNAVAILABLE",
                    provider.Id,
                    $"{provider.DisplayName} did not return its configured default model.",
                    requestedValue: provider.DefaultModel,
                    resolvedValue: "none"));
                return;
            }

            var fallback = isOllamaBacked
                ? authoringModels
                    .Where(model => !model.Id.EndsWith("-cloud", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(model => model.SizeBytes)
                    .ThenBy(model => model.Id, StringComparer.Ordinal)
                    .Select(model => model.Id)
                    .FirstOrDefault()
                    ?? LanguageModels[0]
                : LanguageModels[0];
            SelectedLanguageModel = fallback;
            var exception = new RekallAgeLanguageModelProviderException(
                "REKALL_LANGUAGE_MODEL_DEFAULT_UNAVAILABLE",
                provider.Id,
                $"{provider.DisplayName} did not return its configured default model.",
                requestedValue: provider.DefaultModel,
                resolvedValue: string.Join(',', LanguageModels));
            ProviderStatus = FormatLanguageModelProviderDiagnostic(exception);
            ProviderDisplayStatus = $"Configured default {provider.DefaultModel} unavailable; using {fallback}.";
            StatusText = ProviderDisplayStatus;
            Replace(ValidationLines, [$"warning: {ProviderStatus}"]);
            return;
        }

        SelectedLanguageModel = provider.DefaultModel;
        SetLanguageModelProviderReadyStatus(provider);
    }

    private void SetLanguageModelProviderReadyStatus(RekallAgeLanguageModelProviderDescriptor provider)
    {
        var authentication = provider.Id == "codex"
            ? $" via {CodexAuthenticationLabel(provider.AuthenticationState)}"
            : string.Empty;
        ProviderStatus = $"{provider.DisplayName} ready{authentication} with {LanguageModels.Count} model{(LanguageModels.Count == 1 ? string.Empty : "s")}.";
        ProviderDisplayStatus = ProviderStatus;
        StatusText = ProviderStatus;
    }

    private static string CodexAuthenticationLabel(string authenticationState) =>
        authenticationState switch
        {
            "chatgpt" => "ChatGPT",
            "api-key" => "API key",
            _ => "Codex authentication"
        };

    private bool IsCurrentLanguageModelTransition(
        RekallAgeLanguageModelProviderDescriptor provider,
        long generation)
    {
        lock (_languageModelLifecycleSync)
        {
            return IsCurrentLanguageModelTransitionLocked(provider, generation);
        }
    }

    private bool IsCurrentLanguageModelTransitionLocked(
        RekallAgeLanguageModelProviderDescriptor provider,
        long generation) =>
        generation == _languageModelProviderTransitionGeneration
        && provider.Id.Equals(SelectedLanguageModelProvider.Id, StringComparison.Ordinal);

    private void ReportLanguageModelProviderFailureIfCurrent(
        RekallAgeLanguageModelProviderDescriptor provider,
        IRekallAgeProjectAgentRunner runner,
        long generation,
        RekallAgeLanguageModelProviderException exception)
    {
        lock (_languageModelLifecycleSync)
        {
            if (!IsCurrentLanguageModelTransitionLocked(provider, generation)
                || !ReferenceEquals(_languageModelRunner, runner)) return;
            Replace(LanguageModels, []);
            SelectedLanguageModel = string.Empty;
            InvalidateLocalModelReadiness(provider.Id);
            ReportLanguageModelProviderFailure(exception);
        }
    }

    private void ReportLanguageModelTransitionFailureIfCurrent(
        RekallAgeLanguageModelProviderDescriptor provider,
        long generation,
        RekallAgeLanguageModelProviderException exception)
    {
        lock (_languageModelLifecycleSync)
        {
            if (!IsCurrentLanguageModelTransitionLocked(provider, generation)) return;
            Replace(LanguageModels, []);
            SelectedLanguageModel = string.Empty;
            InvalidateLocalModelReadiness(provider.Id);
            ReportLanguageModelProviderFailure(exception);
        }
    }

    private void ReportUnexpectedLanguageModelTransitionFailureIfCurrent(
        RekallAgeLanguageModelProviderDescriptor provider,
        long generation) =>
        ReportLanguageModelTransitionFailureIfCurrent(
            provider,
            generation,
            new RekallAgeLanguageModelProviderException(
                "REKALL_LANGUAGE_MODEL_PROVIDER_TRANSITION_FAILED",
                provider.Id,
                "Language-model provider transition failed unexpectedly."));

    private RekallAgeLanguageModelProviderSettings? SessionLanguageModelProviderSettings()
    {
        if (_sessionOpenAiApiKey is null
            && _sessionKimiApiKey is null
            && _sessionOllamaUrl is null
            && _sessionOpenAiUrl is null
            && _sessionKimiUrl is null) return null;
        return new RekallAgeLanguageModelProviderSettings
        {
            OllamaUrl = _sessionOllamaUrl ?? Environment.GetEnvironmentVariable("REKALL_AGE_OLLAMA_URL"),
            OpenAiApiKey = _sessionOpenAiApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
            OpenAiUrl = _sessionOpenAiUrl ?? Environment.GetEnvironmentVariable("REKALL_AGE_OPENAI_URL"),
            KimiApiKey = _sessionKimiApiKey ?? ReadFirstEnvironmentValue("KIMI_API_KEY", "MOONSHOT_API_KEY"),
            KimiUrl = _sessionKimiUrl ?? Environment.GetEnvironmentVariable("REKALL_AGE_KIMI_URL")
        };
    }

    private void InvalidateLocalModelReadiness(string providerId)
    {
        if (providerId is not ("ollama" or "gguf")) return;
        _localModelRuntimeReady = false;
        OnPropertyChanged(nameof(CanBrowseGguf));
    }

    private static string? ReadFirstEnvironmentValue(params string[] names)
    {
        foreach (var name in names)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    private void ReportLanguageModelProviderFailure(
        RekallAgeLanguageModelProviderException exception)
    {
        ProviderStatus = FormatLanguageModelProviderDiagnostic(exception);
        ProviderDisplayStatus = exception.Message;
        StatusText = ProviderStatus;
        Replace(ValidationLines, [$"error: {ProviderStatus}"]);
    }

    private void ReportLanguageModelShutdownFailure()
    {
        const string diagnostic =
            "REKALL_STUDIO_LANGUAGE_MODEL_SHUTDOWN_FAILED: Language-model shutdown encountered an unexpected failure.";
        ProviderStatus = diagnostic;
        StatusText = diagnostic;
        Replace(ValidationLines, [$"error: {diagnostic}"]);
    }

    private static string FormatLanguageModelProviderDiagnostic(
        RekallAgeLanguageModelProviderException exception)
    {
        var facts = new List<string> { $"{exception.Code}: {exception.Message}" };
        if (!string.IsNullOrWhiteSpace(exception.RequestedValue))
        {
            facts.Add($"Requested: {Bound(exception.RequestedValue, 256)}.");
        }
        if (!string.IsNullOrWhiteSpace(exception.ResolvedValue))
        {
            facts.Add($"Resolved: {Bound(exception.ResolvedValue, 256)}.");
        }
        return string.Join(' ', facts);
    }

    private async Task ReleaseLanguageModelRunnerAsync()
    {
        _languageModelRunner = null;
        var providerLease = _languageModelProviderLease;
        _languageModelProviderLease = null;
        var fixedRunner = _fixedLanguageModelRunner;
        _fixedLanguageModelRunner = null;
        if (providerLease is not null)
        {
            await providerLease.DisposeAsync().ConfigureAwait(false);
        }
        if (fixedRunner is IAsyncDisposable asyncDisposableRunner)
        {
            await asyncDisposableRunner.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            fixedRunner?.Dispose();
        }
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
        if (progress.ToolExecution is { } toolExecution)
        {
            if (toolExecution.Succeeded)
            {
                Log.Information(
                    "Studio authoring tool progress. Turn={Turn} Phase={Phase} Tool={Tool} Sequence={Sequence} Succeeded={Succeeded} Message={Message}",
                    progress.Turn,
                    progress.Phase,
                    toolExecution.Name,
                    toolExecution.Sequence,
                    toolExecution.Succeeded,
                    progress.Message);
            }
            else
            {
                Log.Warning(
                    "Studio authoring tool progress. Turn={Turn} Phase={Phase} Tool={Tool} Sequence={Sequence} Succeeded={Succeeded} Message={Message}",
                    progress.Turn,
                    progress.Phase,
                    toolExecution.Name,
                    toolExecution.Sequence,
                    toolExecution.Succeeded,
                    progress.Message);
            }
        }
        else
        {
            Log.Information(
                "Studio authoring progress. Turn={Turn} Phase={Phase} Message={Message}",
                progress.Turn,
                progress.Phase,
                progress.Message);
        }
        AppendAgentLine($"turn {progress.Turn}: {progress.Phase}{suffix} — {progress.Message}");
        if (progress.Phase.Equals("agent.message", StringComparison.OrdinalIgnoreCase))
        {
            AgentMessageLines.Add(progress.Message);
            while (AgentMessageLines.Count > 50) AgentMessageLines.RemoveAt(0);
        }
        if (progress.ToolExecution is { Succeeded: false } failed)
        {
            AppendAgentLine($"tool failure: {AgentToolFailureSummary(failed.ResultPreview)}");
        }
        StatusText = progress.Message;
        var operation = progress.ToolExecution is { } activeTool
            ? $"{progress.Phase} · {activeTool.Name} #{activeTool.Sequence}"
            : progress.Phase;
        AgentActivityText = $"Turn {progress.Turn} · {operation} — {progress.Message}";
    }

    private static string AgentToolFailureSummary(string resultPreview)
    {
        try
        {
            var root = JsonNode.Parse(resultPreview) as JsonObject;
            var summary = root?["summary"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(summary))
            {
                return Bound(summary, 320);
            }
        }
        catch (JsonException)
        {
        }

        return Bound(resultPreview, 320);
    }

    private void AppendAgentLine(string value)
    {
        AgentLines.Add(Bound(value, 2_400));
        while (AgentLines.Count > 200) AgentLines.RemoveAt(0);
    }

    private static string Bound(string value, int maxCharacters) =>
        value.Length <= maxCharacters ? value : value[..maxCharacters] + "…";

    private Task CaptureAsync() => RunRenderingAsync(CaptureOperationAsync);

    private async Task<RekallAgeWorkbenchOperationResult> CaptureOperationAsync(
        CancellationToken cancellationToken)
    {
        LastCaptureNonblank = false;
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
            cancellationToken);
        if (result.Ok && result.Value is CaptureRuntimeViewportResult capture && capture.Captured)
        {
            ViewportRenderableCount = capture.RenderableCount;
            LastCaptureNonblank = capture.NonBlank;
            ViewportVisuallyInformative = IsStudioVisualProofAcceptable(
                capture.FrameAnalysis,
                capture.LayoutDiagnostics.WarningCodes);
            ViewportSummary = $"{capture.Width}×{capture.Height} · frame {capture.FrameIndex} · {capture.RenderableCount} renderables · "
                + (ViewportVisuallyInformative ? "visually informative" : "visual repair required");
        }
        return result;
    }

    internal static bool IsStudioVisualProofAcceptable(RekallAgeViewportFrameAnalysis analysis) =>
        IsStudioVisualProofAcceptable(analysis, []);

    internal static bool IsStudioVisualProofAcceptable(
        RekallAgeViewportFrameAnalysis analysis,
        IReadOnlyList<string> layoutWarningCodes) =>
        analysis.Analyzed
        && analysis.VisuallyInformative
        && !analysis.WarningCodes.Contains(
            "REKALL_VIEWPORT_LOW_VISUAL_COVERAGE",
            StringComparer.Ordinal)
        && !layoutWarningCodes.Contains(
            "REKALL_VIEWPORT_CAMERA_FACES_AWAY_FROM_CONTENT",
            StringComparer.Ordinal)
        && !layoutWarningCodes.Contains(
            "REKALL_VIEWPORT_UI_LARGE_COVERAGE",
            StringComparer.Ordinal);

    private async Task StartSimulationAsync()
    {
        if (_session.ProjectRoot is null || _session.SceneName is null) return;
        if (!TryGetPreviewMetrics(out var metrics)) return;
        IsBusy = true;
        var transitionEntered = false;
        try
        {
            await _modeTransitionGate.WaitAsync(_lifecycleCancellation.Token);
            transitionEntered = true;
            var frame = await _previewSession.ResetAsync(
                _session.ProjectRoot,
                _session.SceneName,
                metrics.PixelWidth,
                metrics.PixelHeight,
                _lifecycleCancellation.Token);
            frame = await RecoverPreviewResizeRaceAsync(
                frame,
                _session.ProjectRoot,
                _session.SceneName);
            ApplyPreviewFrame(frame);
            ResetSimulationCadence();
            if (!ViewportAvailable) return;
            if (frame.ProjectModuleDiagnostic is { } rebuildDiagnostic
                && CanAutomaticallyRebuildModules(rebuildDiagnostic.Code))
            {
                StatusText = $"Rebuilding gameplay modules before simulation ({rebuildDiagnostic.Code})…";
                var build = await _session.ExecuteAsync(
                    "rekall.build.modules",
                    JsonSerializer.Serialize(new { projectRoot = _session.ProjectRoot }),
                    "Rebuild gameplay modules for simulation",
                    "studio-simulate",
                    _lifecycleCancellation.Token);
                if (!build.Ok && IsMissingProjectModuleSdk(build.Errors))
                {
                    StatusText = "Installing the project-local gameplay module SDK before simulation…";
                    var installSdk = await _session.ExecuteAsync(
                        "rekall.module.install_sdk",
                        JsonSerializer.Serialize(new { projectRoot = _session.ProjectRoot }),
                        "Install gameplay module SDK for simulation",
                        "studio-simulate",
                        _lifecycleCancellation.Token);
                    if (!installSdk.Ok)
                    {
                        build = installSdk;
                    }
                    else
                    {
                        StatusText = "Rebuilding gameplay modules before simulation…";
                        build = await _session.ExecuteAsync(
                            "rekall.build.modules",
                            JsonSerializer.Serialize(new { projectRoot = _session.ProjectRoot }),
                            "Rebuild gameplay modules for simulation",
                            "studio-simulate",
                            _lifecycleCancellation.Token);
                    }
                }
                if (_session.Model is not null) ApplyModel(_session.Model);
                if (!build.Ok)
                {
                    StatusText = $"Simulation blocked: {build.Summary}";
                    foreach (var error in build.Errors)
                    {
                        var buildDiagnostic = $"blocking: {error.Code} - {error.Message}";
                        if (!ValidationLines.Contains(buildDiagnostic, StringComparer.Ordinal))
                        {
                            ValidationLines.Insert(0, buildDiagnostic);
                        }
                    }
                    return;
                }

                frame = await _previewSession.ResetAsync(
                    _session.ProjectRoot,
                    _session.SceneName,
                    metrics.PixelWidth,
                    metrics.PixelHeight,
                    _lifecycleCancellation.Token);
                frame = await RecoverPreviewResizeRaceAsync(
                    frame,
                    _session.ProjectRoot,
                    _session.SceneName);
                ApplyPreviewFrame(frame);
                ResetSimulationCadence();
                if (!ViewportAvailable) return;
            }
            if (frame.ProjectModuleDiagnostic is { } moduleDiagnostic)
            {
                Mode = RekallAgeStudioMode.Edit;
                StatusText = $"Simulation blocked: {moduleDiagnostic.Code} - {moduleDiagnostic.Message}";
                var blockingDiagnostic = $"blocking: {moduleDiagnostic.Code} - {moduleDiagnostic.Message}";
                if (!ValidationLines.Contains(blockingDiagnostic, StringComparer.Ordinal))
                {
                    ValidationLines.Insert(0, blockingDiagnostic);
                }
                return;
            }
            RemoveProjectModuleBlockingDiagnostics();
            IsSimulationPaused = false;
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

    private static bool CanAutomaticallyRebuildModules(string code) => code is
        "REKALL_MODULE_RECEIPT_MISSING"
        or "REKALL_MODULE_RECEIPT_MALFORMED"
        or "REKALL_MODULE_RECEIPT_INCOMPATIBLE"
        or "REKALL_MODULE_RECEIPT_HOST_POSTURE_MISMATCH"
        or "REKALL_MODULE_SOURCE_STALE"
        or "REKALL_MODULE_ASSEMBLY_MISSING"
        or "REKALL_MODULE_ASSEMBLY_IDENTITY_MISMATCH"
        or "REKALL_MODULE_OUTPUT_HASH_MISMATCH"
        or "REKALL_MODULE_OUTPUT_SET_MISMATCH"
        or "REKALL_MODULE_OUTPUT_SIZE_MISMATCH";

    private static bool IsMissingProjectModuleSdk(IReadOnlyList<RekallAgeCommandError> errors) =>
        errors.Count > 0
        && errors.All(error => error.Code.Equals("REKALL_MODULE_SDK_INTEGRITY_FAILED", StringComparison.Ordinal))
        && errors.Any(error => error.Message.Equals(
            "Project-local module SDK is missing.",
            StringComparison.Ordinal));

    private async ValueTask<RekallAgeStudioPreviewFrame> RecoverPreviewResizeRaceAsync(
        RekallAgeStudioPreviewFrame frame,
        string projectRoot,
        string sceneName)
    {
        if (frame.Presentation.PresentedFrame
            || frame.Presentation.FailureReason?.Contains(
                "surface resized from",
                StringComparison.OrdinalIgnoreCase) != true)
        {
            return frame;
        }

        var currentMetrics = _previewSession.Metrics;
        if (!currentMetrics.IsPresentable) return frame;
        return await _previewSession.ResetAsync(
            projectRoot,
            sceneName,
            currentMetrics.PixelWidth,
            currentMetrics.PixelHeight,
            _lifecycleCancellation.Token);
    }

    private void RemoveProjectModuleBlockingDiagnostics()
    {
        for (var index = ValidationLines.Count - 1; index >= 0; index--)
        {
            if (ValidationLines[index].StartsWith("blocking: REKALL_MODULE_", StringComparison.Ordinal))
            {
                ValidationLines.RemoveAt(index);
            }
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
        if (!IsSimulating || IsSimulationPaused || !ViewportAvailable || !IsLiveViewportEnabled || IsBusy
            || Interlocked.Exchange(ref _previewAdvancing, 1) != 0)
        {
            return;
        }
        try
        {
            var simulationFrames = _simulationCadence.ConsumeSimulationFrames();
            if (simulationFrames == 0) return;
            ApplyPreviewFrame(await _previewSession.StepAsync(
                simulationFrames,
                _lifecycleCancellation.Token));
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

    private Task ToggleSimulationPauseAsync()
    {
        IsSimulationPaused = !IsSimulationPaused;
        ResetSimulationCadence();
        StatusText = IsSimulationPaused
            ? $"Simulation paused at frame {PreviewFrameIndex}."
            : $"Simulation resumed from frame {PreviewFrameIndex}.";
        return Task.CompletedTask;
    }

    private async Task StepSimulationAsync()
    {
        if (!IsSimulating || !IsSimulationPaused) return;
        IsBusy = true;
        try
        {
            ApplyPreviewFrame(await _previewSession.StepAsync(1, _lifecycleCancellation.Token));
            ResetSimulationCadence();
            if (ViewportAvailable) StatusText = $"Simulation advanced exactly one frame to {PreviewFrameIndex}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task ApplyKimiApiKeyAsync(string? apiKey)
    {
        lock (_languageModelLifecycleSync)
        {
            _sessionKimiApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
            OnPropertyChanged(nameof(HasSessionKimiCredential));
            OnPropertyChanged(nameof(KimiCredentialSourceLabel));
            OnPropertyChanged(nameof(LanguageModelProviders));
            _selectedLanguageModelProvider = LanguageModelProviders.Single(
                provider => provider.Id == _selectedLanguageModelProvider.Id);
            OnPropertyChanged(nameof(SelectedLanguageModelProvider));
            if (SelectedLanguageModelProvider.Id != "kimi")
            {
                ProviderStatus = _sessionKimiApiKey is null
                    ? "Kimi session key cleared."
                    : "Kimi session key accepted in memory for this Studio session.";
                return Task.CompletedTask;
            }

            Replace(LanguageModels, []);
            SelectedLanguageModel = string.Empty;
            ProviderStatus = "Refreshing Kimi session authentication…";
            QueueLanguageModelProviderTransition(SelectedLanguageModelProvider);
            return _languageModelProviderTransition;
        }
    }

    public async Task ImportGgufModelAsync(string? ggufPath)
    {
        if (!CanBrowseGguf)
        {
            ProviderStatus = "Local model setup needs attention. Choose Fix setup before importing a GGUF model.";
            ProviderDisplayStatus = ProviderStatus;
            return;
        }
        if (!IsGgufSelected)
        {
            ReportLanguageModelProviderFailure(new RekallAgeLanguageModelProviderException(
                "REKALL_GGUF_PROVIDER_REQUIRED",
                SelectedLanguageModelProvider.Id,
                "Select Local GGUF before importing a GGUF model.",
                resolvedValue: "gguf"));
            return;
        }

        await CancelAndAwaitLanguageModelRefreshAsync();
        IsBusy = true;
        ProviderStatus = "Importing the selected GGUF model through Ollama…";
        ProviderDisplayStatus = ProviderStatus;
        RekallAgeGgufImportResult result;
        try
        {
            result = await _ggufImporter.ImportAsync(ggufPath ?? string.Empty, _lifecycleCancellation.Token);
        }
        catch (RekallAgeLanguageModelProviderException exception)
        {
            ReportLanguageModelProviderFailure(exception);
            return;
        }
        catch (OperationCanceledException) when (_lifecycleCancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
            ReportLanguageModelProviderFailure(new RekallAgeLanguageModelProviderException(
                "REKALL_GGUF_IMPORT_FAILED",
                "gguf",
                "The GGUF import failed unexpectedly."));
            return;
        }
        finally
        {
            IsBusy = false;
        }

        if (!IsGgufSelected) return;
        try
        {
            await DiscoverModelsAsync(propagateProviderFailure: true);
        }
        catch (RekallAgeLanguageModelProviderException exception)
        {
            ReportLanguageModelProviderFailure(exception);
            return;
        }
        if (!LanguageModels.Contains(result.ModelName, StringComparer.Ordinal))
        {
            ReportLanguageModelProviderFailure(new RekallAgeLanguageModelProviderException(
                "REKALL_GGUF_TOOL_CAPABILITY_REQUIRED",
                "gguf",
                "Ollama imported the model, but it does not advertise the tool capability required for game authoring."));
            return;
        }
        SelectedLanguageModel = result.ModelName;
        ProviderStatus = "Local GGUF model imported and ready through Ollama.";
        ProviderDisplayStatus = $"Using {result.ModelName} with {SelectedLanguageModelProvider.DisplayName}.";
        StatusText = ProviderStatus;
    }

    private void ResetSimulationCadence() => _simulationCadence.Reset();

    private void ApplyPreviewFrame(RekallAgeStudioPreviewFrame frame)
    {
        var wasViewportAvailable = ViewportAvailable;
        _viewportInteraction = frame.Interaction;
        _viewportPlacementContext = frame.PlacementContext ?? RekallAgeStudioViewportPlacementContext.From(null);
        RefreshSceneGizmo();
        PreviewFrameIndex = frame.FrameIndex;
        ViewportRenderableCount = frame.RenderableCount;
        var validVulkanFrame = frame.Presentation.PresentedFrame
            && frame.Backend.Equals("vulkan", StringComparison.Ordinal)
            && frame.HardwareAccelerated;
        ViewportAvailable = validVulkanFrame;
        if (wasViewportAvailable != validVulkanFrame) ResetSimulationCadence();
        ViewportBackendLabel = validVulkanFrame
            ? $"Vulkan · hardware{FormatDeviceSuffix(frame.Presentation.SelectedDeviceName)}"
            : "Vulkan · unavailable";
        RemoveVulkanUnavailableDiagnostics();
        if (validVulkanFrame)
        {
            ViewportUnavailableReason = string.Empty;
            if (StatusText.Contains("Vulkan is unavailable", StringComparison.OrdinalIgnoreCase))
            {
                StatusText = "Vulkan viewport ready.";
            }
        }
        else
        {
            var failure = frame.Presentation.FailureReason ?? "Vulkan presentation failed.";
            ViewportUnavailableReason = failure.Contains("Vulkan is unavailable", StringComparison.OrdinalIgnoreCase)
                ? failure
                : $"Vulkan is unavailable: {failure}";
            var details = frame.Presentation.Errors.Count == 0
                ? ViewportUnavailableReason
                : $"{ViewportUnavailableReason} ({string.Join("; ", frame.Presentation.Errors)})";
            ValidationLines.Insert(0, $"error: REKALL_STUDIO_VULKAN_UNAVAILABLE - {details}");
            StatusText = ViewportUnavailableReason;
        }
        ViewportSummary = $"{frame.Presentation.Width}×{frame.Presentation.Height} · frame {frame.FrameIndex} · "
            + $"{frame.RenderableCount} renderables · {frame.ObservationCount} observations · "
            + (validVulkanFrame ? "Vulkan hardware" : "Vulkan unavailable");
    }

    internal async Task PresentViewportAtHostSizeAsync(RekallAgeStudioViewportMetrics metrics)
    {
        if (!metrics.IsPresentable || IsBusy || !IsLiveViewportEnabled
            || _session.ProjectRoot is null || _session.SceneName is null)
        {
            return;
        }

        try
        {
            ApplyPreviewFrame(await _previewSession.PresentCurrentAsync(
                metrics.PixelWidth,
                metrics.PixelHeight,
                _lifecycleCancellation.Token));
        }
        catch (InvalidOperationException)
        {
            await RefreshEditPreviewAsync(StatusText);
        }
        catch (OperationCanceledException) when (_lifecycleCancellation.IsCancellationRequested)
        {
        }
    }

    internal async Task RefreshEditViewportDependenciesAsync(RekallAgeStudioViewportMetrics metrics)
    {
        if (Mode != RekallAgeStudioMode.Edit || !ViewportAvailable || !metrics.IsPresentable
            || IsBusy || !IsLiveViewportEnabled || _session.ProjectRoot is null || _session.SceneName is null
            || Interlocked.Exchange(ref _previewAdvancing, 1) != 0)
        {
            return;
        }

        try
        {
            var frame = await _previewSession.RefreshExternalDependenciesAsync(
                metrics.PixelWidth,
                metrics.PixelHeight,
                _lifecycleCancellation.Token);
            if (frame is not null) ApplyPreviewFrame(frame);
        }
        catch (OperationCanceledException) when (_lifecycleCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            StatusText = $"Viewport dependency refresh failed: {exception.Message}";
            Replace(ValidationLines, [$"error: REKALL_STUDIO_PREVIEW_FAILED - {exception.Message}"]);
        }
        finally
        {
            Volatile.Write(ref _previewAdvancing, 0);
        }
    }

    private bool TryGetPreviewMetrics(out RekallAgeStudioViewportMetrics metrics)
    {
        metrics = _previewSession.Metrics;
        if (metrics.IsPresentable) return true;
        if (ViewportAvailable)
        {
            // A WPF workspace transition can briefly report a zero-sized host after the
            // most recent Vulkan frame was already presented successfully. Keep that
            // valid state visible; the host will supply fresh metrics on its resize tick.
            return false;
        }
        ViewportAvailable = false;
        ViewportBackendLabel = "Vulkan · unavailable";
        ViewportUnavailableReason = "Vulkan is unavailable until the World viewport surface has a positive physical size.";
        StatusText = ViewportUnavailableReason;
        return false;
    }

    private void RemoveVulkanUnavailableDiagnostics()
    {
        for (var index = ValidationLines.Count - 1; index >= 0; index--)
        {
            if (ValidationLines[index].Contains("REKALL_STUDIO_VULKAN_UNAVAILABLE", StringComparison.Ordinal))
            {
                ValidationLines.RemoveAt(index);
            }
        }
    }

    private static string FormatDeviceSuffix(string? deviceName) =>
        string.IsNullOrWhiteSpace(deviceName) ? string.Empty : $" · {deviceName.Trim()}";

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
                ResetSimulationCadence();
                IsSimulationPaused = false;
                Mode = RekallAgeStudioMode.Edit;
                if (resetEditPreview && _session.ProjectRoot is not null && _session.SceneName is not null)
                {
                    if (TryGetPreviewMetrics(out var metrics))
                    {
                        ApplyPreviewFrame(await _previewSession.ResetAsync(
                            _session.ProjectRoot,
                            _session.SceneName,
                            metrics.PixelWidth,
                            metrics.PixelHeight,
                            cancellationToken));
                        ResetSimulationCadence();
                    }
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

    private async Task RunAsync(
        Func<Task<RekallAgeWorkbenchOperationResult>> operation,
        bool refreshPreviewAfter = false,
        RekallAgeStudioInspectorPropertyEditorModel? rejectedInspectorRow = null)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var result = await operation();
            StatusText = result.Summary;
            if (_session.Model is not null)
            {
                ApplyModel(
                    _session.Model,
                    preserveInspectorDrafts: !result.Ok || rejectedInspectorRow is not null,
                    discardInspectorDraft: result.Ok ? rejectedInspectorRow : null);
                await RefreshContentAsync();
            }
            if (result.Ok)
            {
                foreach (var warning in result.Errors)
                {
                    ValidationLines.Add($"warning: {warning.Code} - {warning.Message}");
                }
                if (refreshPreviewAfter && IsLiveViewportEnabled && Mode == RekallAgeStudioMode.Edit)
                {
                    await RefreshEditPreviewAsync(result.Summary);
                }
            }
            else if (!result.Ok)
            {
                Replace(ValidationLines, result.Errors.Select(error => $"error: {error.Code} - {error.Message}"));
                FindCurrentInspectorPropertyEditor(rejectedInspectorRow)?.SetServerValidation(
                    string.Join(Environment.NewLine, result.Errors.Select(error => $"{error.Code} - {error.Message}")));
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            StatusText = ex.Message;
            Replace(ValidationLines, [$"error: REKALL_STUDIO_OPERATION_FAILED - {ex.Message}"]);
            FindCurrentInspectorPropertyEditor(rejectedInspectorRow)?.SetServerValidation(ex.Message);
        }
        catch (OperationCanceledException) when (_lifecycleCancellation.IsCancellationRequested)
        {
            StatusText = "Studio rendering operation canceled during shutdown.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task RunRenderingAsync(
        Func<CancellationToken, Task<RekallAgeWorkbenchOperationResult>> operation,
        bool refreshPreviewAfter = false)
    {
        CancellationTokenSource operationCancellation;
        Task operationTask;
        lock (_renderingOperationsSync)
        {
            if (_lifecycleCancellation.IsCancellationRequested)
            {
                return Task.CompletedTask;
            }

            operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _lifecycleCancellation.Token);
            operationTask = RunAsync(
                () => operation(operationCancellation.Token),
                refreshPreviewAfter);
            _activeRenderingOperations.Add(operationTask);
        }

        return AwaitRenderingOperationAsync(operationTask, operationCancellation);
    }

    private async Task AwaitRenderingOperationAsync(
        Task operationTask,
        CancellationTokenSource operationCancellation)
    {
        try
        {
            await operationTask.ConfigureAwait(false);
        }
        finally
        {
            operationCancellation.Dispose();
            lock (_renderingOperationsSync)
            {
                _activeRenderingOperations.Remove(operationTask);
            }
        }
    }

    private async Task RefreshEditPreviewAsync(string operationSummary)
    {
        if (_session.ProjectRoot is null || _session.SceneName is null) return;
        if (!TryGetPreviewMetrics(out var metrics)) return;
        try
        {
            ApplyPreviewFrame(await _previewSession.ResetAsync(
                _session.ProjectRoot,
                _session.SceneName,
                metrics.PixelWidth,
                metrics.PixelHeight,
                _lifecycleCancellation.Token));
            ResetSimulationCadence();
            if (ViewportAvailable) StatusText = operationSummary;
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

    private void ApplyModel(
        RekallAgeWorkbenchModel model,
        bool preserveInspectorDrafts = false,
        RekallAgeStudioInspectorPropertyEditorModel? discardInspectorDraft = null)
    {
        _currentModel = model;
        OnPropertyChanged(nameof(SelectedEntityId));
        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(ProjectContextText));
        OnPropertyChanged(nameof(HasInspectorSelection));
        OnPropertyChanged(nameof(CanEditSelectedLinkedModel));
        OnPropertyChanged(nameof(InspectorSelectionName));
        OnPropertyChanged(nameof(InspectorSelectionId));
        OnPropertyChanged(nameof(InspectorComponentCountText));
        OnPropertyChanged(nameof(InspectorComponentBrowserEmptyText));
        // Populate the choices before assigning the selected value. Replacing an
        // ItemsSource first prevents WPF's editable ComboBox from writing a
        // transient null selection back into SceneNameInput during model refreshes.
        Replace(SceneNames, model.Project.Scenes.Select(scene => scene.Name));
        SceneNameInput = model.Scene.Name;
        // WPF's TreeView owns selection on its generated item containers. Replacing
        // equivalent nodes during an inspector-only refresh destroys those
        // containers, so a click appears to select and immediately unselect an
        // entity. Keep the existing graph objects until the authored hierarchy or
        // one of its visible labels actually changes.
        if (!EntityNodeListsEqual(EntityNodes, model.Scene.RootEntities))
        {
            Replace(EntityNodes, model.Scene.RootEntities);
            OnPropertyChanged(nameof(EntityNodes));
        }
        var selectedNode = SelectedEntityNode();
        EntityNameInput = selectedNode?.Name ?? string.Empty;
        ParentEntityIdInput = selectedNode?.ParentId ?? string.Empty;
        Replace(InspectorLines, model.Inspector.Components.SelectMany(component =>
            new[] { $"{component.DisplayName} ({component.Type})" }.Concat(component.Properties
                .Where(property => property.IsDefined)
                .Select(property => $"  {property.Name}: {property.Value}"))));
        Replace(ComponentSchemas, model.Inspector.AvailableComponents);
        if (selectedNode is null)
        {
            ComponentTypeInput = string.Empty;
            PropertyNameInput = string.Empty;
            PropertyValueInput = string.Empty;
            Replace(PropertySchemas, []);
            Replace(PropertyValueChoices, []);
            PropertySchemaHelp = InspectorEmptyStateText;
        }
        else if (!ComponentSchemas.Any(component => component.Type.Equals(ComponentTypeInput, StringComparison.Ordinal)))
        {
            ComponentTypeInput = model.Inspector.Components.FirstOrDefault()?.Type
                ?? ComponentSchemas.FirstOrDefault()?.Type
                ?? ComponentTypeInput;
        }
        if (selectedNode is not null) RefreshPropertySchemas();
        RefreshInspectorComponents();
        RebuildInspectorPropertyEditors(model, preserveInspectorDrafts, discardInspectorDraft);
        ApplyContentModel(model.Content);
        if (_session.ProjectRoot is not null)
        {
            Replace(MeshAssetIds, _modeling.ListAssets(_session.ProjectRoot));
            if (SelectedMeshAssetId is null || !MeshAssetIds.Contains(SelectedMeshAssetId)) SelectedMeshAssetId = MeshAssetIds.FirstOrDefault();
            Replace(ModelingGraphAssetIds, _modelingGraph.ListAssets(_session.ProjectRoot));
            if (SelectedModelingGraphAssetId is null || !ModelingGraphAssetIds.Contains(SelectedModelingGraphAssetId))
                SelectedModelingGraphAssetId = ModelingGraphAssetIds.FirstOrDefault();
        }
        Replace(ValidationLines, model.Diagnostics.Issues.Select(issue => $"{issue.Severity}: {issue.Code} - {issue.Message}"));
        Replace(TransactionLines, model.Transactions.Transactions.Select(transaction => $"{transaction.Name}: {transaction.ChangedResources.Count} changes"));
        Replace(ImportLines, model.ImportQueue.Jobs.Select(job => $"{job.Status}: {job.SourcePath}"));
        Replace(SceneSummaryLines, BuildSceneSummaryLines(model.SceneSummary));
        Replace(ActionLines, model.Actions.Actions.Select(action => $"{action.Category}: {action.Label} ({action.Tool})"));
        Replace(RuntimeObservationLines, model.Runtime.Observations.Select(observation =>
            $"{observation.Severity}: {observation.Code} - {observation.Message}"));
        ApplyRendering(model.Rendering, synchronizeAuthoring: true);
        ViewportTitle = $"{model.Scene.Name} Viewport";
        ViewportRenderableCount = model.Runtime.RenderableCount;
        if (!ViewportAvailable)
        {
            ViewportSummary = $"Camera {model.Runtime.ActiveCameraName ?? "none"} · {model.Runtime.RenderableCount} renderables";
        }
        RefreshSceneGizmo();
    }

    private string? SelectedLinkedModelAssetId() => _currentModel?.Inspector.Components
        .FirstOrDefault(component => component.Type.Equals("Rekall.ModelAssetReference", StringComparison.Ordinal))?
        .Properties.FirstOrDefault(property => property.IsDefined
            && property.Name.Equals("assetId", StringComparison.OrdinalIgnoreCase))?
        .Value;

    internal async Task RefreshContentBrowserAsync(CancellationToken cancellationToken)
    {
        if (_session.ProjectRoot is not null)
        {
            var content = await _contentIndex.RefreshAsync(_session.ProjectRoot, cancellationToken);
            ApplyContentModel(content);
        }
        ContentStatusText = $"Content refreshed · {ContentItems.Count} item(s).";
        StatusText = ContentStatusText;
    }

    internal void ReportContentBrowserFailure(string code, string summary)
    {
        ContentStatusText = $"{code} · {summary}";
        StatusText = ContentStatusText;
    }

    internal bool CanAssignContent(
        RekallAgeStudioContentDragPayload payload,
        RekallAgeStudioInspectorPropertyEditorModel row)
    {
        var target = ContentPropertyTarget(row);
        return target is not null && _contentDragService.CanAssign(payload, target);
    }

    internal ValueTask<RekallAgeStudioContentDropResult> AssignContentAsync(
        RekallAgeStudioContentDragPayload payload,
        RekallAgeStudioInspectorPropertyEditorModel row,
        CancellationToken cancellationToken)
    {
        var target = ContentPropertyTarget(row);
        return target is null
            ? ValueTask.FromResult(new RekallAgeStudioContentDropResult(false,
                "REKALL_CONTENT_DROP_TARGET_UNAVAILABLE", "Select an editable asset-reference property first."))
            : _contentDragService.AssignAsync(payload, target, cancellationToken);
    }

    internal bool CanPlaceContent(RekallAgeStudioContentDragPayload payload) =>
        HasEditableProject() && _contentDragService.CanPlace(payload);

    internal ValueTask<RekallAgeStudioContentDropResult> PlaceContentAsync(
        RekallAgeStudioContentDragPayload payload,
        double normalizedX,
        double normalizedY,
        double aspectRatio,
        CancellationToken cancellationToken) =>
        _contentDragService.PlaceAsync(payload,
            _viewportPlacementContext.TargetAt(normalizedX, normalizedY, aspectRatio), cancellationToken);

    internal async Task ApplyContentDropResultAsync(
        RekallAgeStudioContentDropResult result,
        CancellationToken cancellationToken)
    {
        ContentStatusText = $"{result.Code} · {result.Summary}";
        StatusText = ContentStatusText;
        if (!result.Applied) return;
        if (_session.Model is not null) ApplyModel(_session.Model);
        if (IsLiveViewportEnabled && Mode == RekallAgeStudioMode.Edit)
            await RefreshEditPreviewAsync(result.Summary);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private RekallAgeStudioContentPropertyTarget? ContentPropertyTarget(
        RekallAgeStudioInspectorPropertyEditorModel row)
    {
        if (_session.SelectedEntityId is not { } entityId || string.IsNullOrWhiteSpace(row.AssetKind)) return null;
        var entity = SelectedEntityNode();
        return new(entityId, row.ComponentType, row.Name, row.AssetKind, entity?.Locked == true, PropertyLocked: false);
    }

    private async ValueTask<RekallAgeStudioContentCommandEvidence> ExecuteContentPropertyMutationAsync(
        RekallAgeStudioContentPropertyMutation request,
        CancellationToken cancellationToken)
    {
        var result = await _session.ExecuteAsync(
            request.Tool,
            JsonSerializer.Serialize(new
            {
                projectRoot = _session.ProjectRoot,
                sceneName = _session.SceneName,
                entityId = request.EntityId,
                componentType = request.ComponentType,
                propertyName = request.PropertyName,
                value = request.PropertyValue
            }),
            $"Assign content to {request.ComponentType}.{request.PropertyName}", "studio", cancellationToken);
        return ContentCommandEvidence(result);
    }

    private async ValueTask<RekallAgeStudioContentCommandEvidence> ExecuteContentPlacementAsync(
        RekallAgeStudioContentPlacement request,
        CancellationToken cancellationToken)
    {
        var content = _contentModel.Items.FirstOrDefault(item => item.Id.Equals(request.ModelAssetId, StringComparison.Ordinal));
        var modelAssetId = content switch
        {
            { Kind: var kind } when kind.Equals("model-asset", StringComparison.OrdinalIgnoreCase) => content.DisplayName,
            { Origin: "Imported", Family: "model" } =>
                await RekallAgeStudioImportedModelPublisher.EnsurePublishedAsync(_session, content, cancellationToken),
            _ => request.ModelAssetId
        };
        var result = await _session.ExecuteAsync(
            "rekall.scene.instantiate_asset",
            JsonSerializer.Serialize(new
            {
                projectRoot = _session.ProjectRoot,
                sceneName = _session.SceneName,
                modelAssetId,
                name = content?.DisplayName ?? modelAssetId,
                position = new { x = request.Position.X, y = request.Position.Y, z = request.Position.Z },
                rotationDegrees = new { x = 0, y = 0, z = 0 },
                scale = new { x = 1, y = 1, z = 1 }
            }),
            $"Place Model Asset {modelAssetId}", "studio", cancellationToken);
        return ContentCommandEvidence(result);
    }

    private static RekallAgeStudioContentCommandEvidence ContentCommandEvidence(
        RekallAgeWorkbenchOperationResult result) => new(
            result.Ok,
            result.Ok ? "REKALL_CONTENT_DROP_APPLIED" : result.Errors.FirstOrDefault()?.Code ?? "REKALL_CONTENT_DROP_FAILED",
            result.Summary,
            result.TransactionId);

    private async Task RefreshContentAsync()
    {
        if (_session.ProjectRoot is null) return;
        var content = await _contentIndex.RefreshAsync(_session.ProjectRoot, _lifecycleCancellation.Token);
        ApplyContentModel(content);
    }

    internal async Task<IReadOnlyList<RekallAgeStudioContentImportJob>> ImportContentAsync(
        IEnumerable<string> sourcePaths,
        CancellationToken cancellationToken)
    {
        if (_session.ProjectRoot is null) return [];
        if (Interlocked.CompareExchange(ref _contentImportActive, 1, 0) != 0)
            return [new(string.Empty, "other", "Rejected", "REKALL_CONTENT_IMPORT_ALREADY_ACTIVE",
                "Another content import batch is already running.")];
        HasActiveContentImports = true;
        try
        {
            var jobs = await _contentImportSession.ImportAsync(_session.ProjectRoot, sourcePaths, cancellationToken);
            OnPropertyChanged(nameof(ContentImportSummary));
            var succeeded = jobs.Count(job => job.Status == "Succeeded");
            ContentStatusText = succeeded > 0
                ? $"Imported {succeeded} content file(s)."
                : "No content files were imported.";
            StatusText = ContentStatusText;
            return jobs;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ContentStatusText = "REKALL_CONTENT_IMPORT_CANCELLED · Content import cancelled; completed results were retained.";
            StatusText = ContentStatusText;
            return ImportJobs.ToArray();
        }
        finally
        {
            OnPropertyChanged(nameof(ContentImportSummary));
            HasActiveContentImports = false;
            Interlocked.Exchange(ref _contentImportActive, 0);
        }
    }

    private Task ImportContentAsync(object? parameter) => parameter is IEnumerable<string> paths
        ? ImportContentAsync(paths, _lifecycleCancellation.Token)
        : Task.CompletedTask;

    private bool CanOpenSelectedContent() => HasOpenProject()
        && SelectedContentItem is { } item
        && _contentOpenRouter.CanOpen(item);

    private async Task OpenSelectedContentAsync()
    {
        if (SelectedContentItem is null) return;
        var result = await _contentOpenRouter.OpenAsync(
            SelectedContentItem, _lifecycleCancellation.Token);
        ContentStatusText = $"{result.Code} · {result.Summary}";
        StatusText = ContentStatusText;
        if (result.Opened && result.WorkspaceId is { } workspace)
        {
            SelectedStudioWorkspace = workspace switch
            {
                "modeling" => "Modeling",
                "code" => "Code",
                "world" => "World",
                _ => SelectedStudioWorkspace
            };
            if (workspace.Equals("modeling", StringComparison.OrdinalIgnoreCase)
                && result.SurfaceId is { } surface)
                SelectedModelingSurface = surface;
        }
    }

    async ValueTask IRekallAgeStudioContentOpenTarget.SelectMeshAsync(
        RekallAgeContentBrowserItem item, CancellationToken cancellationToken)
    {
        if (_session.ProjectRoot is null) throw new InvalidOperationException("Open a project first.");
        var assetId = item.DisplayName;
        if (item.Origin.Equals("Imported", StringComparison.OrdinalIgnoreCase)
            && item.Family.Equals("model", StringComparison.OrdinalIgnoreCase))
        {
            var publishedId = await RekallAgeStudioImportedModelPublisher.EnsurePublishedAsync(_session, item, cancellationToken);
            var model = await _modelAssetStore.LoadAsync(_session.ProjectRoot, publishedId, cancellationToken);
            assetId = model.Source.AssetId;
        }
        else if (item.Kind.Equals("model-asset", StringComparison.OrdinalIgnoreCase))
        {
            var model = await _modelAssetStore.LoadAsync(_session.ProjectRoot, assetId, cancellationToken);
            if (model.Source.Kind != RekallAgeModelSourceKind.Mesh)
                throw new InvalidOperationException("The model does not have an editable mesh source.");
            assetId = model.Source.AssetId;
        }

        await _modeling.OpenAsync(_session.ProjectRoot, assetId, cancellationToken);
        SelectedMeshAssetId = assetId;
        _modeling.SetDomain(MeshEditDomain);
        RefreshMeshEditingState();
    }

    async ValueTask IRekallAgeStudioContentOpenTarget.SelectGraphAsync(
        RekallAgeContentBrowserItem item, CancellationToken cancellationToken)
    {
        if (_session.ProjectRoot is null) throw new InvalidOperationException("Open a project first.");
        SelectedModelingGraphAssetId = item.DisplayName;
        await _modelingGraph.OpenAsync(_session.ProjectRoot, SelectedModelingGraphAssetId, cancellationToken);
        Replace(ModelingGraphNodes, _modelingGraph.Nodes);
        Replace(ModelingGraphOutputNames, _modelingGraph.OutputNames);
        SelectedModelingGraphOutput = _modelingGraph.SelectedOutputName;
        ModelingGraphSummary = _modelingGraph.EvaluationSummary;
        SelectedModelingGraphNode = ModelingGraphNodes.FirstOrDefault();
        RefreshModelingGraphCanvas();
    }

    async ValueTask IRekallAgeStudioContentOpenTarget.SelectMaterialAsync(
        RekallAgeContentBrowserItem item, CancellationToken cancellationToken)
    {
        if (_session.ProjectRoot is null) throw new InvalidOperationException("Open a project first.");
        var graphId = item.DisplayName;
        if (item.Kind.Equals("material-instance", StringComparison.OrdinalIgnoreCase))
        {
            var instance = await new RekallAgeMaterialInstanceAssetStore()
                .LoadVersionedAsync(_session.ProjectRoot, item.DisplayName, cancellationToken);
            graphId = instance.Value.GraphAssetId;
        }

        SelectedMaterialGraphAssetId = graphId;
        await _materialGraph.OpenAsync(_session.ProjectRoot, graphId, cancellationToken);
        Replace(MaterialGraphNodes, _materialGraph.Nodes);
        MaterialGraphSummary = _materialGraph.EvaluationSummary;
        SelectedMaterialGraphNode = MaterialGraphNodes.FirstOrDefault();
    }

    async ValueTask IRekallAgeStudioContentOpenTarget.SelectModuleSourceAsync(
        RekallAgeContentBrowserItem item, CancellationToken cancellationToken)
    {
        if (_session.ProjectRoot is null) throw new InvalidOperationException("Open a project first.");
        var sources = await _codeSession.RefreshAsync(_session.ProjectRoot, cancellationToken);
        var source = sources.FirstOrDefault(candidate => PathsEqual(candidate.SourcePath, item.Path!))
            ?? throw new InvalidOperationException("The module source is no longer available.");
        await _codeSession.OpenAsync(source, cancellationToken);
        Replace(CodeSources, sources);
        SelectedCodeSource = _codeSession.SelectedSource;
        Set(ref _codeSourceText, _codeSession.SourceText, nameof(CodeSourceText));
        OnPropertyChanged(nameof(IsCodeDirty));
        OnPropertyChanged(nameof(SelectedCodeProjectPath));
        CodeStatusText = $"Editing {source.ModuleName}/{source.FileName}.";
    }

    ValueTask IRekallAgeStudioContentOpenTarget.OpenAssociatedAsync(
        RekallAgeContentBrowserItem item, CancellationToken cancellationToken) =>
        _externalContentLauncher.OpenAsync(item.Path!, cancellationToken);


    private void ApplyContentModel(RekallAgeContentBrowserModel content)
    {
        _contentModel = content;
        Replace(ContentItems, content.Items);
        var existingCards = ContentCards.ToDictionary(card => card.Key);
        var cards = content.Items.Select(item =>
        {
            if (existingCards.TryGetValue(RekallAgeStudioContentKey.From(item), out var card)) { card.Update(item); return card; }
            return new RekallAgeStudioContentCardModel(item);
        }).ToArray();
        Replace(ContentCards, cards);
        Replace(ContentWarnings, content.Warnings);
        Replace(ContentCategories, RekallAgeStudioContentProjection.Categories(content.Items));
        if (!ContentCategories.Contains(SelectedContentCategory, StringComparer.OrdinalIgnoreCase))
            SelectedContentCategory = "All";
        RefreshContentProjection();
        Replace(AssetLines, content.Items.Select(item => $"{item.Kind}: {item.DisplayName} ({item.Id})"));
    }

    private void BeginSelectedContentPreview(RekallAgeContentBrowserItem? item)
    {
        _contentPreviewCancellation?.Cancel();
        _contentPreviewCancellation?.Dispose();
        _contentPreviewCancellation = null;
        SelectedContentPreview = null;
        if (item is null) return;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifecycleCancellation.Token);
        _contentPreviewCancellation = cancellation;
        _ = LoadSelectedContentPreviewAsync(item, cancellation);
    }

    private async Task LoadSelectedContentPreviewAsync(
        RekallAgeContentBrowserItem item, CancellationTokenSource cancellation)
    {
        try
        {
            var preview = await _contentPreviewService.GetAsync(item, cancellation.Token);
            if (!cancellation.IsCancellationRequested
                && ReferenceEquals(_contentPreviewCancellation, cancellation)
                && SelectedContentItem is { } selected
                && RekallAgeStudioContentKey.From(selected) == RekallAgeStudioContentKey.From(item)
                && SelectedContentItem?.Revision == item.Revision)
            {
                SelectedContentPreview = preview;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_contentPreviewCancellation, cancellation))
                _contentPreviewCancellation = null;
            cancellation.Dispose();
        }
    }

    private void RefreshContentProjection()
    {
        var selectedKey = SelectedContentItem is null ? (RekallAgeStudioContentKey?)null : RekallAgeStudioContentKey.From(SelectedContentItem);
        Replace(FilteredContentItems, RekallAgeStudioContentProjection.Filter(
            _contentModel.Items, SelectedContentCategory, ContentSearchText));
        var visibleKeys = FilteredContentItems.Select(RekallAgeStudioContentKey.From).ToHashSet();
        Replace(FilteredContentCards, ContentCards.Where(card => visibleKeys.Contains(card.Key)));
        SelectedContentItem = selectedKey is null
            ? null
            : FilteredContentItems.FirstOrDefault(item => RekallAgeStudioContentKey.From(item) == selectedKey.Value);
        OnPropertyChanged(nameof(SelectedContentCard));
    }

    internal Task LoadContentCardPreviewAsync(
        RekallAgeStudioContentCardModel card, CancellationToken cancellationToken) =>
        card.LoadPreviewAsync(_contentPreviewService, cancellationToken);

    private void ApplyRendering(
        RekallAgeWorkbenchRenderQualityModel rendering,
        bool synchronizeAuthoring)
    {
        if (synchronizeAuthoring && rendering.Authoring is { } authoring)
        {
            SelectedQualityPreset = authoring.Preset;
            QualityResolutionScaleInput = ToQualityInput(authoring.ResolutionScale);
            QualityShadowCascadeCountInput = ToQualityInput(authoring.ShadowCascadeCount);
            QualityShadowResolutionInput = ToQualityInput(authoring.ShadowResolution);
            QualityFogModeInput = authoring.FogMode ?? string.Empty;
            QualityBloomOverride = authoring.Bloom;
            QualitySsaoOverride = authoring.Ssao;
            QualityMaximumActiveParticlesInput = ToQualityInput(authoring.MaximumActiveParticles);
        }

        var runtime = rendering.Runtime;
        RequestedQualityPreset = runtime.RequestedPreset;
        ResolvedQualityPreset = runtime.ResolvedPreset ?? "Unavailable";
        OutputResolutionText = FormatResolution(runtime.OutputWidth, runtime.OutputHeight);
        InternalResolutionText = FormatResolution(runtime.RenderWidth, runtime.RenderHeight);
        TotalGpuMillisecondsText = runtime.TotalGpuMillisecondsText;
        GpuTimingStatusText = $"{runtime.GpuTimingCode ?? "available"} · {runtime.GpuTimingProvenance}";
        RenderWorkloadText = $"{runtime.DrawCount} draws · {runtime.DispatchCount} dispatches";
        Replace(RenderPassTimings, runtime.PassTimings);
        Replace(RenderResources, runtime.Resources);
        Replace(RenderDegradations, runtime.Degradations);
        Replace(RenderQualityComparisons, rendering.Comparisons);
        Replace(RenderDebugViews, rendering.DebugViews);
        Replace(RenderSuggestedActions, runtime.SuggestedActions);
        SelectedRenderDebugView = RenderDebugViews.FirstOrDefault();
        RefreshCommands();
    }

    private static string FormatResolution(int? width, int? height) =>
        width.HasValue && height.HasValue ? $"{width.Value}×{height.Value}" : "Unavailable";

    private static string ToQualityInput<T>(T? value) where T : struct, IFormattable =>
        value.HasValue ? value.Value.ToString(null, CultureInfo.InvariantCulture) : string.Empty;

    private void RefreshSceneGizmo()
    {
        var selected = SelectedEntityId is null ? null : FindEntityNode(EntityNodes, SelectedEntityId);
        var hasTransform3D = _currentModel?.Inspector.Components.Any(
            component => component.Type.Equals("Rekall.Transform3D", StringComparison.Ordinal)) == true;
        _sceneGizmo = _viewportInteraction is null || selected is null || !hasTransform3D
            ? null
            : RekallAgeStudioSceneGizmo.Create(_viewportInteraction, selected.EntityId, selected.Locked);
        _previewSession.SetEditorRenderables(_sceneGizmo is null || TransformTool is RekallAgeStudioTransformTool.Select
            ? []
            : RekallAgeStudioSceneGizmoRenderables.Create(
                TransformTool,
                TransformSpace,
                InspectorNumber("Rekall.Transform3D", "x", 0),
                InspectorNumber("Rekall.Transform3D", "y", 0),
                InspectorNumber("Rekall.Transform3D", "z", 0),
                InspectorNumber("Rekall.Transform3D", "pitch", 0),
                InspectorNumber("Rekall.Transform3D", "yaw", 0),
                InspectorNumber("Rekall.Transform3D", "roll", 0)));
        OnPropertyChanged(nameof(SceneGizmoHandles));
    }

    private double InspectorNumber(string componentType, string propertyName, double fallback)
    {
        var property = _currentModel?.Inspector.Components
            .FirstOrDefault(component => component.Type.Equals(componentType, StringComparison.Ordinal))?
            .Properties.FirstOrDefault(candidate => candidate.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
        return property is { IsDefined: true }
            && double.TryParse(property.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && double.IsFinite(value)
                ? value
                : fallback;
    }

    private static RekallAgeSceneEntityNode? FindEntityNode(
        IEnumerable<RekallAgeSceneEntityNode> roots,
        string entityId)
    {
        foreach (var node in roots)
        {
            if (node.EntityId.Equals(entityId, StringComparison.Ordinal)) return node;
            var child = FindEntityNode(node.Children, entityId);
            if (child is not null) return child;
        }

        return null;
    }

    private static string TransformPropertyName(
        RekallAgeStudioTransformTool tool,
        RekallAgeStudioTransformAxis axis) =>
        (tool, axis) switch
        {
            (RekallAgeStudioTransformTool.Move, RekallAgeStudioTransformAxis.X) => "x",
            (RekallAgeStudioTransformTool.Move, RekallAgeStudioTransformAxis.Y) => "y",
            (RekallAgeStudioTransformTool.Move, RekallAgeStudioTransformAxis.Z) => "z",
            (RekallAgeStudioTransformTool.Rotate, RekallAgeStudioTransformAxis.X) => "pitch",
            (RekallAgeStudioTransformTool.Rotate, RekallAgeStudioTransformAxis.Y) => "yaw",
            (RekallAgeStudioTransformTool.Rotate, RekallAgeStudioTransformAxis.Z) => "roll",
            (RekallAgeStudioTransformTool.Scale, RekallAgeStudioTransformAxis.X) => "scaleX",
            (RekallAgeStudioTransformTool.Scale, RekallAgeStudioTransformAxis.Y) => "scaleY",
            (RekallAgeStudioTransformTool.Scale, RekallAgeStudioTransformAxis.Z) => "scaleZ",
            _ => throw new ArgumentOutOfRangeException()
        };

    private static double ValidateSnap(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(parameterName);
        return value;
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

    private void RefreshInspectorComponents()
    {
        var result = RekallAgeStudioInspectorBrowser.Project(
            _currentModel?.Inspector.Components ?? [],
            InspectorSearchInput,
            SelectedInspectorComponent?.Type ?? ComponentTypeInput);
        Replace(InspectorComponents, result.Components);
        var visibleTypes = result.Components.Select(component => component.Type).ToHashSet(StringComparer.Ordinal);
        Replace(InspectorComponentEditors, _allInspectorComponentEditors.Where(group => visibleTypes.Contains(group.Type)));
        SelectedInspectorComponent = result.SelectedComponent;
        OnPropertyChanged(nameof(InspectorComponentBrowserEmptyText));
    }

    private void RebuildInspectorPropertyEditors(
        RekallAgeWorkbenchModel model,
        bool preserveDirtyDrafts,
        RekallAgeStudioInspectorPropertyEditorModel? discardDraft)
    {
        var preserved = preserveDirtyDrafts
            ? _inspectorPropertyEditorKeys
                .Where(entry => entry.Key.IsDirty && !ReferenceEquals(entry.Key, discardDraft))
                .ToDictionary(entry => entry.Value, entry => entry.Key)
            : [];
        var entityChoices = FlattenEntityNodes(model.Scene.RootEntities)
            .Select(entity => new RekallAgeStudioInspectorPropertyChoice(
                $"{entity.Name} ({entity.EntityId})",
                entity.EntityId))
            .ToArray();
        var entityId = model.Inspector.SelectedEntityId ?? string.Empty;
        var editors = new List<RekallAgeStudioInspectorPropertyEditorModel>();
        var groups = new List<RekallAgeStudioInspectorComponentEditorModel>();
        var keys = new Dictionary<RekallAgeStudioInspectorPropertyEditorModel, InspectorPropertyEditorKey>();

        foreach (var component in model.Inspector.Components)
        {
            var componentEditors = new List<RekallAgeStudioInspectorPropertyEditorModel>();
            foreach (var property in component.Properties)
            {
                var key = new InspectorPropertyEditorKey(entityId, component.Type, property.Name);
                if (!preserved.TryGetValue(key, out var editor))
                {
                    var assetChoices = model.Assets.Assets
                        .Where(asset => property.AssetKind is null
                            || asset.Kind.Equals(property.AssetKind, StringComparison.OrdinalIgnoreCase))
                        .Select(asset => asset.AssetId)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    editor = new RekallAgeStudioInspectorPropertyEditorModel(
                        component.Type,
                        property,
                        assetChoices,
                        entityChoices);
                    editor.PropertyChanged += InspectorPropertyEditorChanged;
                }

                componentEditors.Add(editor);
                editors.Add(editor);
                keys[editor] = key;
            }

            groups.Add(new RekallAgeStudioInspectorComponentEditorModel(component, componentEditors));
        }

        Replace(InspectorPropertyEditors, editors);
        _allInspectorComponentEditors.Clear();
        _allInspectorComponentEditors.AddRange(groups);
        var visibleTypes = InspectorComponents.Select(component => component.Type).ToHashSet(StringComparer.Ordinal);
        Replace(InspectorComponentEditors, groups.Where(group => visibleTypes.Contains(group.Type)));
        _inspectorPropertyEditorKeys.Clear();
        foreach (var entry in keys) _inspectorPropertyEditorKeys.Add(entry.Key, entry.Value);
        _commitInspectorPropertyCommand.RaiseCanExecuteChanged();
        _resetInspectorPropertyCommand.RaiseCanExecuteChanged();
    }

    private void InspectorPropertyEditorChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(RekallAgeStudioInspectorPropertyEditorModel.IsDirty)
            or nameof(RekallAgeStudioInspectorPropertyEditorModel.IsValid)
            or nameof(RekallAgeStudioInspectorPropertyEditorModel.IsDefined))
        {
            _commitInspectorPropertyCommand.RaiseCanExecuteChanged();
            _resetInspectorPropertyCommand.RaiseCanExecuteChanged();
        }
    }

    private RekallAgeStudioInspectorPropertyEditorModel? FindCurrentInspectorPropertyEditor(
        RekallAgeStudioInspectorPropertyEditorModel? requested)
    {
        if (requested is null) return null;
        return InspectorPropertyEditors.FirstOrDefault(row =>
            row.ComponentType.Equals(requested.ComponentType, StringComparison.Ordinal)
            && row.Name.Equals(requested.Name, StringComparison.Ordinal));
    }

    private static IEnumerable<RekallAgeSceneEntityNode> FlattenEntityNodes(
        IEnumerable<RekallAgeSceneEntityNode> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in FlattenEntityNodes(root.Children)) yield return child;
        }
    }

    private void SynchronizeInspectorSelectionToComponentType()
    {
        var matching = InspectorComponents.FirstOrDefault(component =>
            component.Type.Equals(ComponentTypeInput, StringComparison.Ordinal));
        if (matching is not null && !ReferenceEquals(matching, SelectedInspectorComponent))
        {
            SelectedInspectorComponent = matching;
        }
        else
        {
            OnPropertyChanged(nameof(SelectedInspectorComponentDescription));
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

    private static string ToModuleId(string moduleName)
    {
        var characters = moduleName.Trim().SelectMany((character, index) =>
            index > 0 && char.IsUpper(character)
                ? new[] { '-', char.ToLowerInvariant(character) }
                : new[] { char.ToLowerInvariant(character) });
        return "game." + new string(characters.ToArray()).Replace(' ', '-');
    }

    private static string HumanizeIdentifier(string value)
    {
        var text = new System.Text.StringBuilder();
        foreach (var character in value.Trim())
        {
            if (text.Length > 0 && char.IsUpper(character) && text[^1] != ' ') text.Append(' ');
            text.Append(character is '-' or '_' ? ' ' : character);
        }
        return text.ToString().Trim();
    }

    private static bool PathsEqual(string left, string right) => Path.GetFullPath(left).Equals(
        Path.GetFullPath(right),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

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

    internal static string? ResolvePlayerExecutable(string? baseDirectory = null)
    {
        var studioDirectory = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);
        var candidates = new[]
        {
            Path.Combine(studioDirectory, "Rekall.Age.Player.Windows.exe"),
            Path.GetFullPath(Path.Combine(studioDirectory, "..", "..", "players", "windows", "Rekall.Age.Player.Windows.exe")),
            Path.GetFullPath(Path.Combine(studioDirectory, "..", "..", "..", "..",
                "Rekall.Age.Player.Windows", "bin", "Debug", "net10.0-windows", "Rekall.Age.Player.Windows.exe"))
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    internal static string? ResolveCliExecutable(string? baseDirectory = null)
    {
        var studioDirectory = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);
        var candidates = new[]
        {
            Path.Combine(studioDirectory, "Rekall.Age.Cli.exe"),
            Path.GetFullPath(Path.Combine(studioDirectory, "..", "cli", "Rekall.Age.Cli.exe")),
            Path.GetFullPath(Path.Combine(studioDirectory, "..", "..", "..", "..",
                "Rekall.Age.Cli", "bin", "Debug", "net10.0", "Rekall.Age.Cli.exe"))
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

    private static bool EntityNodeListsEqual(
        IReadOnlyList<RekallAgeSceneEntityNode> current,
        IReadOnlyList<RekallAgeSceneEntityNode> updated)
    {
        if (current.Count != updated.Count) return false;
        for (var index = 0; index < current.Count; index++)
        {
            if (!EntityNodesEqual(current[index], updated[index])) return false;
        }
        return true;
    }

    private static bool EntityNodesEqual(
        RekallAgeSceneEntityNode current,
        RekallAgeSceneEntityNode updated) =>
        current.EntityId.Equals(updated.EntityId, StringComparison.Ordinal)
        && current.Name.Equals(updated.Name, StringComparison.Ordinal)
        && string.Equals(current.ParentId, updated.ParentId, StringComparison.Ordinal)
        && current.Visible == updated.Visible
        && current.Locked == updated.Locked
        && current.Tags.SequenceEqual(updated.Tags, StringComparer.Ordinal)
        && EntityNodeListsEqual(current.Children, updated.Children);

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
        _openSelectedContentCommand.RaiseCanExecuteChanged();
        _importContentCommand.RaiseCanExecuteChanged();
        _addEntityCommand.RaiseCanExecuteChanged();
        _renameEntityCommand.RaiseCanExecuteChanged();
        _duplicateEntityCommand.RaiseCanExecuteChanged();
        _deleteEntityCommand.RaiseCanExecuteChanged();
        _toggleEntityVisibleCommand.RaiseCanExecuteChanged();
        _toggleEntityLockedCommand.RaiseCanExecuteChanged();
        _reparentEntityCommand.RaiseCanExecuteChanged();
        _clearEntityParentCommand.RaiseCanExecuteChanged();
        _addComponentCommand.RaiseCanExecuteChanged();
        _removeComponentCommand.RaiseCanExecuteChanged();
        _setPropertyCommand.RaiseCanExecuteChanged();
        _removePropertyCommand.RaiseCanExecuteChanged();
        _commitInspectorPropertyCommand.RaiseCanExecuteChanged();
        _resetInspectorPropertyCommand.RaiseCanExecuteChanged();
        _validateCommand.RaiseCanExecuteChanged();
        _captureCommand.RaiseCanExecuteChanged();
        _attachQualityProfileCommand.RaiseCanExecuteChanged();
        _applyQualityCommand.RaiseCanExecuteChanged();
        _captureQualityCommand.RaiseCanExecuteChanged();
        _compareQualityCommand.RaiseCanExecuteChanged();
        _simulateCommand.RaiseCanExecuteChanged();
        _pauseSimulationCommand.RaiseCanExecuteChanged();
        _stepSimulationCommand.RaiseCanExecuteChanged();
        _playCommand.RaiseCanExecuteChanged();
        _stopCommand.RaiseCanExecuteChanged();
        _switchSceneCommand.RaiseCanExecuteChanged();
        _packageCommand.RaiseCanExecuteChanged();
        _auditPackageCommand.RaiseCanExecuteChanged();
        _openPackageFolderCommand.RaiseCanExecuteChanged();
        _publishWebCommand.RaiseCanExecuteChanged();
        _auditWebCommand.RaiseCanExecuteChanged();
        _undoCommand.RaiseCanExecuteChanged();
        _redoCommand.RaiseCanExecuteChanged();
        _discoverModelsCommand.RaiseCanExecuteChanged();
        _signInCodexCommand.RaiseCanExecuteChanged();
        _cancelCodexSignInCommand.RaiseCanExecuteChanged();
        _runAgentCommand.RaiseCanExecuteChanged();
        _cancelAgentCommand.RaiseCanExecuteChanged();
        _refreshCodeCommand.RaiseCanExecuteChanged();
        _saveCodeCommand.RaiseCanExecuteChanged();
        _buildCodeCommand.RaiseCanExecuteChanged();
        _createAttachCodeComponentCommand.RaiseCanExecuteChanged();
        _openCodeFileCommand.RaiseCanExecuteChanged();
        _openCodeProjectCommand.RaiseCanExecuteChanged();
        _openCodeSolutionCommand.RaiseCanExecuteChanged();
        _openCodeInVsCodeCommand.RaiseCanExecuteChanged();
        _refreshMeshAssetsCommand.RaiseCanExecuteChanged();
        _createMeshPrimitiveCommand.RaiseCanExecuteChanged();
        _openMeshAssetCommand.RaiseCanExecuteChanged();
        _frameSelectedMeshViewportCommand.RaiseCanExecuteChanged();
        _toggleMeshViewportProjectionCommand.RaiseCanExecuteChanged();
        _publishModelCommand.RaiseCanExecuteChanged();
        _placeModelCommand.RaiseCanExecuteChanged();
        _publishAndPlaceModelCommand.RaiseCanExecuteChanged();
        _selectMeshElementCommand.RaiseCanExecuteChanged();
        _clearMeshSelectionCommand.RaiseCanExecuteChanged();
        _previewMeshOperationCommand.RaiseCanExecuteChanged();
        _applyMeshOperationCommand.RaiseCanExecuteChanged();
        _cancelMeshPreviewCommand.RaiseCanExecuteChanged();
        _refreshModelingGraphsCommand.RaiseCanExecuteChanged();
        _openModelingGraphCommand.RaiseCanExecuteChanged();
        _evaluateModelingGraphCommand.RaiseCanExecuteChanged();
        _applyModelingGraphParametersCommand.RaiseCanExecuteChanged();
        _refreshMaterialGraphsCommand.RaiseCanExecuteChanged();
        _openMaterialGraphCommand.RaiseCanExecuteChanged();
        _applyMaterialGraphParametersCommand.RaiseCanExecuteChanged();
        _openAnimationMixerCommand.RaiseCanExecuteChanged();
        _applyAnimationMixerLayersCommand.RaiseCanExecuteChanged();
        _addAnimationMixerLayerCommand.RaiseCanExecuteChanged();
    }

    private RekallAgeAsyncCommand CreateAsyncCommand(Func<Task> execute, Func<bool> canExecute) =>
        new(execute, canExecute, ReportUnexpectedFailure);

    private RekallAgeAsyncCommand CreateAsyncCommand(
        Func<object?, Task> execute,
        Func<object?, bool> canExecute) =>
        new(execute, canExecute, ReportUnexpectedFailure);

    private readonly record struct InspectorPropertyEditorKey(
        string EntityId,
        string ComponentType,
        string PropertyName);

    private void ReportUnexpectedFailure(Exception exception)
    {
        Log.Error(exception, "Studio operation failed unexpectedly.");
        StatusText = "Studio operation failed. See Validation for details.";
        Replace(
            ValidationLines,
            [$"error: REKALL_STUDIO_UNEXPECTED_FAILURE - {exception.GetType().Name}. See the protected local Studio log for details."]);
        IsBusy = false;
    }
}

internal sealed class ImmediateProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}

internal sealed class RekallAgeAsyncCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool> _canExecute;
    private readonly Action<Exception> _onError;
    private bool _executing;

    public RekallAgeAsyncCommand(
        Func<Task> execute,
        Func<bool> canExecute,
        Action<Exception> onError)
        : this(_ => execute(), _ => canExecute(), onError)
    {
    }

    public RekallAgeAsyncCommand(
        Func<object?, Task> execute,
        Func<object?, bool> canExecute,
        Action<Exception> onError)
    {
        _execute = execute;
        _canExecute = canExecute;
        _onError = onError;
    }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_executing && _canExecute(parameter);

    public async void Execute(object? parameter) => await ExecuteAsync(parameter);

    internal async Task ExecuteAsync(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _executing = true;
        RaiseCanExecuteChanged();
        try
        {
            await _execute(parameter);
        }
        catch (Exception ex)
        {
            _onError(ex);
        }
        finally
        {
            _executing = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
