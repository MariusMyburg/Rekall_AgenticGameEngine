using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Rekall.Age.Editor;
using Rekall.Age.Editor.Contracts;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.Workflows;

namespace Rekall.Age.Studio;

public sealed class RekallAgeStudioViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly RekallAgeWorkbenchSession _session;
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
    private Process? _player;
    private bool _isBusy;
    private string _projectPathInput = string.Empty;
    private string _projectNameInput = "New Rekall Game";
    private string _sceneNameInput = "Main";
    private string _componentTypeInput = "Rekall.Transform";
    private string _propertyNameInput = "position";
    private string _propertyValueInput = "[0, 0, 0]";
    private string _statusText = "Create or open a Rekall AGE project to begin.";
    private string _viewportTitle = "Viewport";
    private string _viewportSummary = "No rendered frame yet.";
    private BitmapImage? _viewportImage;

    public RekallAgeStudioViewModel()
        : this(new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()))
    {
    }

    internal RekallAgeStudioViewModel(RekallAgeWorkbenchSession session)
    {
        _session = session;
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
        set => Set(ref _sceneNameInput, value);
    }

    public string ComponentTypeInput
    {
        get => _componentTypeInput;
        set
        {
            if (Set(ref _componentTypeInput, value)) RefreshCommands();
        }
    }

    public string PropertyNameInput
    {
        get => _propertyNameInput;
        set
        {
            if (Set(ref _propertyNameInput, value)) RefreshCommands();
        }
    }

    public string PropertyValueInput
    {
        get => _propertyValueInput;
        set => Set(ref _propertyValueInput, value);
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

    public async ValueTask DisposeAsync() => await StopAsync();

    private bool CanOpenOrCreate() => !IsBusy && !string.IsNullOrWhiteSpace(ProjectPathInput);
    private bool HasOpenProject() => !IsBusy && _session.Model is not null;
    private bool CanEditComponent() => HasOpenProject()
        && _session.SelectedEntityId is not null
        && !string.IsNullOrWhiteSpace(ComponentTypeInput);
    private bool CanEditProperty() => CanEditComponent() && !string.IsNullOrWhiteSpace(PropertyNameInput);

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
        Replace(EntityNodes, model.Scene.RootEntities);
        Replace(SceneNames, model.Project.Scenes.Select(scene => scene.Name));
        Replace(InspectorLines, model.Inspector.Components.SelectMany(component =>
            new[] { component.Type }.Concat(component.Properties.Select(property => $"  {property.Name}: {property.Value}"))));
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

    public async void Execute(object? parameter)
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
