using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Input;
using Rekall.Age.Editor.Contracts;
using Rekall.Age.Workflows;
using Serilog;

namespace Rekall.Age.Studio;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    public static RoutedUICommand OpenDocumentationCommand { get; } = new(
        "Open Documentation",
        nameof(OpenDocumentationCommand),
        typeof(MainWindow));

    private readonly RekallAgeStudioViewModel _viewModel;
    private readonly IRekallAgeStudioLayoutStore _layoutStore = new RekallAgeStudioLayoutStore();
    private readonly RekallAgeStudioExampleCatalog _exampleCatalog = RekallAgeStudioExampleCatalog.CreateDefault();
    private readonly RekallAgeStudioExampleLibrary _exampleLibrary = new();
    private readonly RekallAgeStudioProjectTransitionCoordinator _projectTransitions = new();
    private readonly RekallAgeCodexApprovalSession _codexApprovalSession = new();
    private readonly RekallAgeStudioLanguageModelSetupCoordinator _languageModelSetupCoordinator = new();
    private readonly DispatcherTimer _previewTimer;
    private readonly RekallAgeStudioViewportRecoveryState _viewportRecovery =
        new(TimeSpan.FromSeconds(1));
    private readonly RekallAgeStudioShutdownCoordinator _shutdownCoordinator = new(
        maximumAttempts: 2,
        retryDelay: TimeSpan.FromMilliseconds(100),
        static (delay, cancellationToken) => new ValueTask(Task.Delay(delay, cancellationToken)));
    private RekallAgeStudioLayout _layout = RekallAgeStudioLayout.Default;
    private bool _shutdownComplete;
    private bool _meshTransformDragging;
    private bool _sceneTransformDragging;
    private bool _initializing = true;
    private bool _hadProject;
    private bool _shutdownPrepared;
    private readonly CancellationTokenSource _contentDropCancellation = new();
    private readonly object _contentDropSync = new();
    private readonly HashSet<Task> _contentDropTasks = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsLanguageModelSetupIncomplete => _languageModelSetupCoordinator.IsSetupIncomplete;

    public bool IsLanguageModelSetupBusy => _languageModelSetupCoordinator.IsSetupBusy;

    public bool CanOpenLanguageModelSetup => !IsLanguageModelSetupBusy;

    public string LanguageModelSetupStatusText => _languageModelSetupCoordinator.SetupStatusText;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new RekallAgeStudioViewModel(
            new RekallAgeStudioVulkanPreviewSession(SceneVulkanViewportHost));
        _viewModel.CodexApprovalHandler = RequestCodexApprovalAsync;
        _viewModel.CodexAuthenticationLauncher = authenticationUri =>
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(authenticationUri.AbsoluteUri)
            {
                UseShellExecute = true
            });
            return ValueTask.CompletedTask;
        };
        DataContext = _viewModel;
        AuthorWorkspaceHost.FixSetupRequested = OpenLanguageModelSetupAsync;
        _languageModelSetupCoordinator.StateChanged += OnLanguageModelSetupStateChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SceneVulkanViewportHost.PointerFact += OnSceneViewportPointerFact;
        SceneVulkanViewportHost.MetricsChanged += OnSceneViewportMetricsChanged;
        _previewTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = RekallAgeStudioPreviewCadence.PresentationInterval
        };
        _previewTimer.Tick += OnPreviewTick;
        Loaded += OnLoaded;
        PopulateExamplesMenu();
        ApplyViewportAvailabilityVisual();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var args = Environment.GetCommandLineArgs();
            var projectIndex = Array.IndexOf(args, "--project");
            var sceneIndex = Array.IndexOf(args, "--scene");
            var projectRoot = projectIndex >= 0 && projectIndex + 1 < args.Length ? args[projectIndex + 1] : null;
            var sceneName = sceneIndex >= 0 && sceneIndex + 1 < args.Length ? args[sceneIndex + 1] : "Main";
            await RekallAgeStudioStartupSequence.RunAsync(
                async cancellationToken =>
                {
                    _layout = await _layoutStore.LoadAsync(cancellationToken);
                    ApplyLayout(_layout);
                },
                async cancellationToken =>
                {
                    await _languageModelSetupCoordinator.InitializeAsync(this, _viewModel, cancellationToken);
                    NotifyLanguageModelSetupChanged();
                },
                cancellationToken => _viewModel.InitializeAsync(projectRoot, sceneName),
                () => _viewModel.HasProject,
                () => SelectWorkspace("World"),
                QueueLanguageModelRefreshIfReady,
                CancellationToken.None);
            if (_viewModel.HasProject && SceneVulkanViewportHost.Metrics.IsPresentable)
            {
                await _viewModel.PresentViewportAtHostSizeAsync(SceneVulkanViewportHost.Metrics);
            }
            ApplyViewportAvailabilityVisual();
            _hadProject = _viewModel.HasProject;
            _initializing = false;
            _previewTimer.Start();
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Failed to initialize the Studio workspace.");
        }
    }

    private async void OnPreviewTick(object? sender, EventArgs e)
    {
        try
        {
            if (_viewModel.Mode == RekallAgeStudioMode.Play)
            {
                await _viewModel.AdvanceLivePreviewAsync();
                return;
            }

            var metrics = SceneVulkanViewportHost.Metrics;
            switch (_viewportRecovery.SelectTickAction(
                        _viewModel.HasProject,
                        _viewModel.ViewportAvailable,
                        _viewModel.IsSimulating,
                        DateTimeOffset.UtcNow))
            {
                case RekallAgeStudioViewportTickAction.RecoverPresentation when metrics.IsPresentable:
                    await _viewModel.PresentViewportAtHostSizeAsync(metrics);
                    break;
                case RekallAgeStudioViewportTickAction.RefreshEditDependencies:
                    await _viewModel.RefreshEditViewportDependenciesAsync(metrics);
                    break;
                case RekallAgeStudioViewportTickAction.AdvanceSimulation:
                    await _viewModel.AdvanceLivePreviewAsync();
                    break;
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Studio live preview failed to advance.");
        }
    }

    private async void OnSelectedEntityChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is RekallAgeSceneEntityNode entity)
        {
            await _viewModel.SelectEntityAsync(entity);
        }
    }

    private void OnInspectorTextEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RekallAgeStudioInspectorPropertyEditorModel row }) return;
        if (e.Key == Key.Escape)
        {
            row.RestoreOriginalDraft();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            CommitInspectorRow(row);
            e.Handled = true;
        }
    }

    private void OnInspectorJsonEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RekallAgeStudioInspectorPropertyEditorModel row }) return;
        if (e.Key == Key.Escape)
        {
            row.RestoreOriginalDraft();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            CommitInspectorRow(row);
            e.Handled = true;
        }
    }

    private void OnInspectorReferenceEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RekallAgeStudioInspectorPropertyEditorModel row }) return;
        if (e.Key == Key.Escape)
        {
            row.RestoreOriginalDraft();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            row.AcceptReferenceSearchText();
            CommitInspectorRow(row);
            e.Handled = true;
        }
    }

    private void OnInspectorReferenceEditorLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RekallAgeStudioInspectorPropertyEditorModel row }) return;
        if (e.NewFocus is FrameworkElement { DataContext: RekallAgeStudioInspectorPropertyEditorModel nextRow }
            && ReferenceEquals(row, nextRow))
        {
            return;
        }

        row.AcceptReferenceSearchText();
        CommitInspectorRow(row);
    }

    private void OnInspectorTextEditorLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RekallAgeStudioInspectorPropertyEditorModel row }) return;
        if (e.NewFocus is FrameworkElement { DataContext: RekallAgeStudioInspectorPropertyEditorModel nextRow }
            && ReferenceEquals(row, nextRow))
        {
            return;
        }

        CommitInspectorRow(row);
    }

    private void OnInspectorBooleanChanged(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RekallAgeStudioInspectorPropertyEditorModel row })
        {
            CommitInspectorRow(row);
        }
    }

    private void OnInspectorChoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RekallAgeStudioInspectorPropertyEditorModel row })
        {
            CommitInspectorRow(row);
        }
    }

    private void OnInspectorReferenceChoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox
            {
                DataContext: RekallAgeStudioInspectorPropertyEditorModel row,
                SelectedItem: RekallAgeStudioInspectorPropertyChoice choice
            })
        {
            return;
        }

        if (row.ReferenceValue.Equals(choice.Value, StringComparison.Ordinal)) return;
        row.SelectReferenceValue(choice.Value);
        CommitInspectorRow(row);
    }

    private void CommitInspectorRow(RekallAgeStudioInspectorPropertyEditorModel row)
    {
        row.TryCreateValue(out _, out _);
        if (_viewModel.CommitInspectorPropertyCommand.CanExecute(row))
        {
            _viewModel.CommitInspectorPropertyCommand.Execute(row);
        }
    }

    private void OnWorkspaceChanged(object sender, SelectionChangedEventArgs e)
        => ApplyWorkspaceVisibility(refreshModeling: true);

    private void ApplyWorkspaceVisibility(bool refreshModeling)
    {
        // InitializeComponent can raise SelectionChanged before the injected preview session exists.
        if (_viewModel is null) return;
        if (AuthorWorkspaceHost is null || WorldWorkspace is null || CodeWorkspaceHost is null || ModelingWorkspaceHost is null
            || ProjectBar is null || MainToolbar is null) return;
        var workspace = WorkspaceName();
        var author = workspace == "Author";
        var world = workspace == "World";
        var code = workspace == "Code";
        var modeling = workspace == "Modeling";
        AuthorWorkspaceHost.Visibility = author ? Visibility.Visible : Visibility.Collapsed;
        WorldWorkspace.Visibility = world ? Visibility.Visible : Visibility.Collapsed;
        CodeWorkspaceHost.Visibility = code ? Visibility.Visible : Visibility.Collapsed;
        ModelingWorkspaceHost.Visibility = modeling ? Visibility.Visible : Visibility.Collapsed;
        ProjectBar.Visibility = modeling ? Visibility.Collapsed : Visibility.Visible;
        MainToolbar.Visibility = world ? Visibility.Visible : Visibility.Collapsed;
        if (world) ApplyViewportAvailabilityVisual();
        if (modeling && refreshModeling)
        {
            if (_viewModel.RefreshMeshAssetsCommand.CanExecute(null)) _viewModel.RefreshMeshAssetsCommand.Execute(null);
            if (_viewModel.RefreshModelingGraphsCommand.CanExecute(null)) _viewModel.RefreshModelingGraphsCommand.Execute(null);
        }
        if (code && refreshModeling && _viewModel.RefreshCodeCommand.CanExecute(null))
        {
            _viewModel.RefreshCodeCommand.Execute(null);
        }
    }

    private async ValueTask<RekallAgeCodexApprovalDecision> RequestCodexApprovalAsync(
        RekallAgeCodexApprovalRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!RekallAgeCodexApprovalPresenter.TryFormat(request, out var summary))
        {
            return RekallAgeCodexApprovalDecision.Decline;
        }
        _codexApprovalSession.ApproveAll = _viewModel.PreapproveAllCodexActionsForSession;
        if (_codexApprovalSession.IsApproved(request)) return RekallAgeCodexApprovalDecision.Accept;

        var choice = await Dispatcher.InvokeAsync(() =>
        {
            var dialog = new CodexApprovalDialog(summary) { Owner = this };
            dialog.ShowDialog();
            return dialog.Choice;
        });
        if (choice == RekallAgeCodexApprovalChoice.AllowActionForSession)
        {
            _codexApprovalSession.ApproveAction(request);
        }
        else if (choice == RekallAgeCodexApprovalChoice.AllowAllForSession)
        {
            _codexApprovalSession.ApproveAll = true;
            _viewModel.PreapproveAllCodexActionsForSession = true;
        }
        return choice == RekallAgeCodexApprovalChoice.Deny
            ? RekallAgeCodexApprovalDecision.Decline
            : RekallAgeCodexApprovalDecision.Accept;
    }

    private async void OnCreateProjectClick(object sender, RoutedEventArgs e)
    {
        var initialParent = Directory.Exists(_viewModel.ProjectPathInput)
            ? Directory.GetParent(_viewModel.ProjectPathInput)?.FullName ?? _viewModel.ProjectPathInput
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var dialog = new CreateProjectDialog(initialParent) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Request is null) return;
        if (!await ResolveDirtyCodeAsync()) return;

        _viewModel.ProjectPathInput = dialog.Request.ProjectRoot;
        _viewModel.ProjectNameInput = dialog.Request.ProjectName;
        _viewModel.SceneNameInput = dialog.Request.SceneName;
        var transition = _projectTransitions.TryBegin();
        if (transition is null) return;
        SetProjectTransitionVisual(active: true);
        try
        {
            await ((RekallAgeAsyncCommand)_viewModel.CreateCommand).ExecuteAsync(null);
            if (_viewModel.HasProject) SelectWorkspace("World");
        }
        finally
        {
            transition.Dispose();
            SetProjectTransitionVisual(active: false);
        }
    }

    private async void OnOpenProjectClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Open a Rekall AGE project",
            Multiselect = false
        };
        if (Directory.Exists(_viewModel.ProjectPathInput)) dialog.InitialDirectory = _viewModel.ProjectPathInput;
        if (dialog.ShowDialog(this) != true) return;
        if (!await ResolveDirtyCodeAsync()) return;

        var transition = _projectTransitions.TryBegin();
        if (transition is null) return;
        SetProjectTransitionVisual(active: true);
        try
        {
            await _viewModel.OpenProjectAsync(dialog.FolderName);
            if (_viewModel.HasProject) SelectWorkspace("World");
        }
        finally
        {
            transition.Dispose();
            SetProjectTransitionVisual(active: false);
        }
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    private async void OnLanguageModelSetupClick(object sender, RoutedEventArgs e)
    {
        await OpenLanguageModelSetupAsync(CancellationToken.None);
    }

    private async Task OpenLanguageModelSetupAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _languageModelSetupCoordinator.ShowSetupAsync(this, _viewModel, cancellationToken);
            NotifyLanguageModelSetupChanged();
            QueueLanguageModelRefreshIfReady();
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Language-model setup could not be opened.");
        }
    }

    private void NotifyLanguageModelSetupChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLanguageModelSetupIncomplete)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLanguageModelSetupBusy)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanOpenLanguageModelSetup)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LanguageModelSetupStatusText)));
    }

    private void OnLanguageModelSetupStateChanged(object? sender, EventArgs e) =>
        NotifyLanguageModelSetupChanged();

    private void QueueLanguageModelRefreshIfReady()
    {
        if (!_languageModelSetupCoordinator.ShouldRefreshLanguageModels) return;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (_viewModel.LanguageModels.Count == 0
                && _viewModel.RefreshLanguageModelsCommand.CanExecute(null))
            {
                _viewModel.RefreshLanguageModelsCommand.Execute(null);
            }
        }));
    }

    private void OnOpenDocumentationExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        try
        {
            RekallAgeStudioDocumentation.Open(AppContext.BaseDirectory);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or Win32Exception)
        {
            Log.Error(exception, "Could not open the bundled Studio documentation");
            var message = exception is FileNotFoundException
                ? exception.Message
                : $"Windows could not open the documentation in your default browser. " +
                  $"You can open the file directly at:{Environment.NewLine}{Environment.NewLine}" +
                  $"{RekallAgeStudioDocumentation.ResolvePath(AppContext.BaseDirectory)}" +
                  $"{Environment.NewLine}{Environment.NewLine}{exception.Message}";
            MessageBox.Show(
                this,
                message,
                "Documentation unavailable",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void PopulateExamplesMenu()
    {
        ExamplesMenu.Items.Clear();
        var result = _exampleCatalog.Discover();
        foreach (var issue in result.Issues)
        {
            Log.Warning(
                "Bundled Studio example was ignored. Folder={Folder} Manifest={Manifest} Issue={Issue}",
                issue.FolderName,
                issue.ManifestPath,
                issue.Message);
        }

        if (result.Examples.Count == 0)
        {
            ExamplesMenu.Items.Add(new MenuItem
            {
                Header = "No bundled examples found",
                IsEnabled = false
            });
            return;
        }

        foreach (var example in result.Examples)
        {
            var capabilitySummary = example.Capabilities.Count == 0
                ? "AGE project"
                : string.Join(", ", example.Capabilities);
            var item = new MenuItem
            {
                Header = example.DisplayName.Replace("_", "__", StringComparison.Ordinal),
                ToolTip = $"Open a writable copy · {capabilitySummary}",
                Tag = example
            };
            item.Click += OnOpenExampleClick;
            ExamplesMenu.Items.Add(item);
        }
    }

    private async void OnOpenExampleClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: RekallAgeStudioExample example } menuItem) return;

        var destination = Path.Combine(RekallAgeStudioExampleLibrary.DefaultRoot, example.FolderName);
        if (RekallAgeStudioExampleLibrary.IsOccupied(destination))
        {
            if (Directory.Exists(destination) && File.Exists(Path.Combine(destination, "rekall.project.json")))
            {
                var choice = MessageBox.Show(
                    this,
                    $"A writable copy of {example.DisplayName} already exists at:\n\n{destination}\n\n" +
                    "Yes: open the existing copy\nNo: create and open a fresh copy\nCancel: do nothing",
                    "Open example",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);
                if (choice == MessageBoxResult.Cancel) return;
                if (choice == MessageBoxResult.No)
                {
                    destination = RekallAgeStudioExampleLibrary.FindFreshDestination(
                        RekallAgeStudioExampleLibrary.DefaultRoot,
                        example.FolderName);
                }
            }
            else
            {
                destination = RekallAgeStudioExampleLibrary.FindFreshDestination(
                    RekallAgeStudioExampleLibrary.DefaultRoot,
                    example.FolderName);
            }
        }

        if (!await ResolveDirtyCodeAsync()) return;
        var transition = _projectTransitions.TryBegin();
        if (transition is null) return;
        SetProjectTransitionVisual(active: true);

        var previousCursor = Cursor;
        menuItem.IsEnabled = false;
        Cursor = Cursors.Wait;
        try
        {
            if (!Directory.Exists(destination))
            {
                await _exampleLibrary.CopyAsync(example, destination, transition.CancellationToken);
            }

            transition.CancellationToken.ThrowIfCancellationRequested();
            await _viewModel.OpenProjectAsync(destination);
            if (_viewModel.HasProject) SelectWorkspace("World");
        }
        catch (OperationCanceledException) when (transition.CancellationToken.IsCancellationRequested)
        {
            Log.Information("Opening Studio example was cancelled. Example={Example}", example.FolderName);
        }
        catch (Exception exception)
        {
            Log.Error(
                exception,
                "Failed to create or open writable Studio example. Example={Example} Destination={Destination}",
                example.FolderName,
                destination);
            MessageBox.Show(
                this,
                $"Studio could not open the example.\n\n{exception.Message}",
                "Open example",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Cursor = previousCursor;
            menuItem.IsEnabled = true;
            transition.Dispose();
            SetProjectTransitionVisual(active: false);
        }
    }

    private void SetProjectTransitionVisual(bool active)
    {
        WorkbenchRoot.IsEnabled = !active;
    }

    private void ApplyLayout(RekallAgeStudioLayout layout)
    {
        Width = layout.WindowWidth;
        Height = layout.WindowHeight;
        if (double.IsFinite(layout.WindowX) && double.IsFinite(layout.WindowY))
        {
            Left = Math.Clamp(layout.WindowX, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100);
            Top = Math.Clamp(layout.WindowY, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80);
        }
        HierarchyColumn.Width = new GridLength(layout.Panel("Hierarchy").Visible ? layout.Panel("Hierarchy").Size : 0);
        InspectorColumn.Width = new GridLength(layout.Panel("Inspector").Visible ? layout.Panel("Inspector").Size : 0);
        var contentBrowser = layout.Panel("ContentBrowser");
        OutputRow.Height = new GridLength(layout.Panel("Output").Visible ? layout.Panel("Output").Size : 0);
        HierarchyPanel.Visibility = layout.Panel("Hierarchy").Visible ? Visibility.Visible : Visibility.Collapsed;
        InspectorPanel.Visibility = layout.Panel("Inspector").Visible ? Visibility.Visible : Visibility.Collapsed;
        OutputTabs.Visibility = layout.Panel("Output").Visible ? Visibility.Visible : Visibility.Collapsed;
        ContentBrowserPanel.Visibility = contentBrowser.Visible ? Visibility.Visible : Visibility.Collapsed;
        ContentBrowserSplitter.Visibility = layout.Panel("Output").Visible ? Visibility.Visible : Visibility.Collapsed;
        foreach (var item in OutputTabs.Items.OfType<TabItem>())
        {
            if (string.Equals(item.Header?.ToString(), layout.ActiveOutputTab, StringComparison.Ordinal))
            {
                OutputTabs.SelectedItem = item;
                break;
            }
        }
        SelectWorkspace(layout.ActiveWorkspace);
        ApplyWorkspaceVisibility(refreshModeling: false);
        WindowState = layout.WindowMaximized ? WindowState.Maximized : WindowState.Normal;
    }

    private void OnLayoutPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name }
            || !Enum.TryParse<RekallAgeStudioLayoutPreset>(name, out var preset)) return;
        var selected = RekallAgeStudioLayout.CreatePreset(preset);
        _layout = selected with
        {
            WindowX = Left,
            WindowY = Top,
            WindowWidth = ActualWidth,
            WindowHeight = ActualHeight,
            WindowMaximized = WindowState == WindowState.Maximized
        };
        ApplyLayout(_layout);
    }

    private void OnTogglePanelClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string panelId }) return;
        _layout = CaptureLayout();
        var panel = _layout.Panel(panelId);
        _layout = _layout with
        {
            Panels = _layout.Panels.Select(candidate => candidate.Id == panelId
                ? candidate with { Visible = !panel.Visible }
                : candidate).ToArray()
        };
        ApplyLayout(_layout);
    }

    private void OnShowContentBrowserClick(object sender, RoutedEventArgs e)
    {
        _layout = CaptureLayout();
        _layout = _layout with
        {
            ActiveOutputTab = "Content Browser",
            Panels = _layout.Panels.Select(panel => panel.Id switch
            {
                "Output" => panel with { Visible = true },
                "ContentBrowser" => panel with { Visible = true, Size = Math.Max(190, panel.Size) },
                _ => panel
            }).ToArray()
        };
        ApplyLayout(_layout);
        OutputTabs.SelectedItem = ContentBrowserPanel;
        ContentBrowserHost.Focus();
        ContentBrowserHost.FocusSearch();
    }

    private RekallAgeStudioLayout CaptureLayout()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        var activeOutput = (OutputTabs.SelectedItem as TabItem)?.Header?.ToString() ?? _layout.ActiveOutputTab;
        var sharedBottomHeight = OutputTabs.Visibility == Visibility.Visible
            ? Math.Max(190, OutputRow.ActualHeight)
            : _layout.Panel("Output").Size;
        return RekallAgeStudioLayout.Normalize(_layout with
        {
            WindowX = bounds.X,
            WindowY = bounds.Y,
            WindowWidth = bounds.Width,
            WindowHeight = bounds.Height,
            WindowMaximized = WindowState == WindowState.Maximized,
            ActiveOutputTab = activeOutput,
            ActiveWorkspace = WorkspaceName(),
            Panels =
            [
                _layout.Panel("Hierarchy") with { Visible = HierarchyPanel.Visibility == Visibility.Visible, Size = HierarchyPanel.Visibility == Visibility.Visible ? Math.Max(180, HierarchyColumn.ActualWidth) : _layout.Panel("Hierarchy").Size },
                _layout.Panel("Inspector") with { Visible = InspectorPanel.Visibility == Visibility.Visible, Size = InspectorPanel.Visibility == Visibility.Visible ? Math.Max(180, InspectorColumn.ActualWidth) : _layout.Panel("Inspector").Size },
                _layout.Panel("Output") with { Visible = OutputTabs.Visibility == Visibility.Visible, Size = sharedBottomHeight },
                _layout.Panel("ContentBrowser") with { Visible = ContentBrowserPanel.Visibility == Visibility.Visible, Size = sharedBottomHeight }
            ]
        }) ?? RekallAgeStudioLayout.Default;
    }

    private async void OnSceneViewportPointerFact(
        object? sender,
        RekallAgeStudioViewportPointerFact fact)
    {
        var metrics = SceneVulkanViewportHost.Metrics;
        if (!metrics.IsPresentable || metrics.DipWidth <= 0 || metrics.DipHeight <= 0) return;
        if (fact.Kind == RekallAgeStudioViewportPointerKind.Down
            && fact.Button == RekallAgeStudioViewportPointerButton.Left)
        {
            if (_viewModel.BeginSceneTransform(
                    metrics.DipWidth,
                    metrics.DipHeight,
                    fact.DisplayX,
                    fact.DisplayY))
            {
                _sceneTransformDragging = true;
                SceneVulkanViewportHost.CapturePointer();
                return;
            }

            await _viewModel.SelectViewportEntityAsync(
                metrics.DipWidth,
                metrics.DipHeight,
                fact.DisplayX,
                fact.DisplayY);
            return;
        }
        if (fact.Kind == RekallAgeStudioViewportPointerKind.Move && _sceneTransformDragging)
        {
            _viewModel.UpdateSceneTransform(
                metrics.DipWidth,
                metrics.DipHeight,
                fact.DisplayX,
                fact.DisplayY);
            return;
        }
        if (fact.Kind == RekallAgeStudioViewportPointerKind.Up && _sceneTransformDragging)
        {
            _sceneTransformDragging = false;
            _viewModel.UpdateSceneTransform(
                metrics.DipWidth,
                metrics.DipHeight,
                fact.DisplayX,
                fact.DisplayY);
            await _viewModel.CompleteSceneTransformAsync();
            return;
        }
        if (fact.Kind is RekallAgeStudioViewportPointerKind.FocusLost
            or RekallAgeStudioViewportPointerKind.CaptureLost)
        {
            if (!_sceneTransformDragging) return;
            _sceneTransformDragging = false;
            _viewModel.CancelSceneTransform();
        }
    }

    private async void OnSceneViewportMetricsChanged(
        object? sender,
        RekallAgeStudioViewportMetrics metrics)
    {
        if (!metrics.IsPresentable || !_viewModel.HasProject) return;
        try
        {
            await _viewModel.PresentViewportAtHostSizeAsync(metrics);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Studio Vulkan viewport failed to present after resize.");
        }
    }

    private void OnInspectorPropertyDragOver(object sender, DragEventArgs e)
    {
        e.Effects = sender is FrameworkElement { DataContext: RekallAgeStudioInspectorPropertyEditorModel row }
            && TryGetContentDragPayload(e.Data, out var payload)
            && _viewModel.CanAssignContent(payload, row)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnInspectorPropertyDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { DataContext: RekallAgeStudioInspectorPropertyEditorModel row }
            || !TryGetContentDragPayload(e.Data, out var payload)) return;
        StartContentDrop(token => _viewModel.AssignContentAsync(payload, row, token).AsTask());
    }

    private void OnSceneViewportDragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetContentDragPayload(e.Data, out var payload) && _viewModel.CanPlaceContent(payload)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnSceneViewportDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement element || element.ActualWidth <= 0 || element.ActualHeight <= 0
            || !TryGetContentDragPayload(e.Data, out var payload)) return;
        var point = e.GetPosition(element);
        StartContentDrop(token => _viewModel.PlaceContentAsync(
            payload,
            Math.Clamp(point.X / element.ActualWidth, 0, 1),
            Math.Clamp(point.Y / element.ActualHeight, 0, 1),
            element.ActualWidth / element.ActualHeight,
            token).AsTask());
    }

    private void StartContentDrop(Func<CancellationToken, Task<RekallAgeStudioContentDropResult>> operation)
    {
        if (_contentDropCancellation.IsCancellationRequested) return;
        var task = ExecuteContentDropUiAsync(operation, _contentDropCancellation.Token);
        lock (_contentDropSync) _contentDropTasks.Add(task);
        _ = ObserveContentDropAsync(task);
    }

    private async Task ObserveContentDropAsync(Task task)
    {
        try { await task; }
        finally { lock (_contentDropSync) _contentDropTasks.Remove(task); }
    }

    private async Task ExecuteContentDropUiAsync(
        Func<CancellationToken, Task<RekallAgeStudioContentDropResult>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await operation(cancellationToken);
            await _viewModel.ApplyContentDropResultAsync(result, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _viewModel.ReportContentBrowserFailure("REKALL_CONTENT_DROP_CANCELLED", "Content drop cancelled.");
        }
        catch (Exception exception) when (IsExpectedContentDropFailure(exception))
        {
            _viewModel.ReportContentBrowserFailure("REKALL_CONTENT_DROP_FAILED",
                "The content drop could not be completed. Inspect Studio logs for details.");
            Log.Warning(exception, "Studio content drop failed.");
        }
        catch (Exception exception)
        {
            _viewModel.ReportContentBrowserFailure("REKALL_CONTENT_DROP_FAILED",
                "The content drop could not be completed. Inspect Studio logs for details.");
            Log.Error(exception, "Unexpected Studio content drop failure was contained.");
        }
    }

    private static bool IsExpectedContentDropFailure(Exception exception) =>
        exception is System.Text.Json.JsonException or ArgumentException or InvalidOperationException
            or IOException or UnauthorizedAccessException;

    private static bool TryGetContentDragPayload(IDataObject data, out RekallAgeStudioContentDragPayload payload)
    {
        payload = null!;
        if (!data.GetDataPresent(RekallAgeStudioContentDragService.DataFormat)
            || data.GetData(RekallAgeStudioContentDragService.DataFormat) is not string json) return false;
        return RekallAgeStudioContentDragPayload.TryParse(json, out payload);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RekallAgeStudioViewModel.HasProject))
        {
            if (!_initializing && !_hadProject && _viewModel.HasProject) SelectWorkspace("World");
            _hadProject = _viewModel.HasProject;
            ApplyViewportAvailabilityVisual();
        }
        if (e.PropertyName == nameof(RekallAgeStudioViewModel.ViewportAvailable))
        {
            ApplyViewportAvailabilityVisual();
        }
        if (e.PropertyName == nameof(RekallAgeStudioViewModel.WorldViewportRenderStyle)
            && _viewModel.HasProject && SceneVulkanViewportHost.Metrics.IsPresentable)
        {
            _ = RefreshWorldViewportStyleAsync();
        }
        if (e.PropertyName == nameof(RekallAgeStudioViewModel.EntityNodes))
        {
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(RestoreSceneHierarchySelection));
        }
    }

    private async Task RefreshWorldViewportStyleAsync()
    {
        try
        {
            await _viewModel.PresentViewportAtHostSizeAsync(SceneVulkanViewportHost.Metrics);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Studio Vulkan viewport failed to apply render style.");
        }
    }

    private void RestoreSceneHierarchySelection()
    {
        if (_viewModel.SelectedEntityId is not { } entityId) return;
        SelectHierarchyItem(SceneHierarchyTree, SceneHierarchyTree.Items, entityId);
    }

    private static bool SelectHierarchyItem(
        ItemsControl parent,
        ItemCollection items,
        string entityId)
    {
        parent.UpdateLayout();
        foreach (var item in items.OfType<RekallAgeSceneEntityNode>())
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(item) is not TreeViewItem container) continue;
            if (item.EntityId.Equals(entityId, StringComparison.Ordinal))
            {
                container.IsSelected = true;
                container.BringIntoView();
                return true;
            }
            if (!ContainsEntity(item.Children, entityId)) continue;
            container.IsExpanded = true;
            container.UpdateLayout();
            return SelectHierarchyItem(container, container.Items, entityId);
        }
        return false;
    }

    private static bool ContainsEntity(
        IReadOnlyList<RekallAgeSceneEntityNode> nodes,
        string entityId) => nodes.Any(node =>
        node.EntityId.Equals(entityId, StringComparison.Ordinal)
        || ContainsEntity(node.Children, entityId));

    private async void OnEditLinkedModelClick(object sender, RoutedEventArgs e)
    {
        if (await _viewModel.OpenSelectedLinkedModelInModelingAsync())
        {
            SelectWorkspace("Modeling");
        }
    }

    private string WorkspaceName() => WorkspaceSelector.SelectedIndex switch
    {
        0 => "Author",
        2 => "Code",
        3 => "Modeling",
        _ => "World"
    };

    private void SelectWorkspace(string workspace) => WorkspaceSelector.SelectedIndex = workspace switch
    {
        "Modeling" => 3,
        "Code" => 2,
        "World" => 1,
        _ => 0
    };

    private void ApplyViewportAvailabilityVisual()
    {
        var visual = _viewportRecovery.Synchronize(
            _viewModel.HasProject,
            _viewModel.ViewportAvailable,
            SceneVulkanViewportHost.Metrics.IsPresentable,
            DateTimeOffset.UtcNow);
        // Hidden preserves layout metrics for automatic Vulkan recovery while removing the
        // native HWND's airspace and hit-test surface whenever WPF owns this viewport area.
        SceneVulkanViewportHost.Visibility = visual.NativeAirspaceVisible
            ? Visibility.Visible
            : Visibility.Hidden;
        SceneVulkanViewportHost.SetPresentationVisible(visual.PresentationSurfaceVisible);
        NoProjectViewportPlaceholder.Visibility = _viewModel.HasProject
            ? Visibility.Collapsed
            : Visibility.Visible;
        VulkanUnavailablePlaceholder.Visibility = visual.PlaceholderVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnMeshViewportMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Image image || image.ActualWidth <= 0 || image.ActualHeight <= 0) return;
        var position = e.GetPosition(image);
        if (_viewModel.BeginMeshViewportTransform(position.X / image.ActualWidth, position.Y / image.ActualHeight))
        {
            _meshTransformDragging = true;
            image.CaptureMouse();
            e.Handled = true;
            return;
        }
        var modifiers = Keyboard.Modifiers;
        _viewModel.SelectMeshViewportElement(
            position.X / image.ActualWidth,
            position.Y / image.ActualHeight,
            modifiers.HasFlag(ModifierKeys.Shift),
            modifiers.HasFlag(ModifierKeys.Control));
        e.Handled = true;
    }

    private void OnMeshViewportMouseMove(object sender, MouseEventArgs e)
    {
        if (!_meshTransformDragging || sender is not Image image || image.ActualWidth <= 0 || image.ActualHeight <= 0) return;
        var position = e.GetPosition(image);
        _viewModel.UpdateMeshViewportTransform(position.X / image.ActualWidth, position.Y / image.ActualHeight);
        e.Handled = true;
    }

    private async void OnMeshViewportMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_meshTransformDragging || sender is not Image image || image.ActualWidth <= 0 || image.ActualHeight <= 0) return;
        _meshTransformDragging = false;
        var position = e.GetPosition(image);
        image.ReleaseMouseCapture();
        await _viewModel.CompleteMeshViewportTransformAsync(position.X / image.ActualWidth, position.Y / image.ActualHeight);
        e.Handled = true;
    }

    private void OnMeshViewportLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_meshTransformDragging) return;
        _meshTransformDragging = false;
        _viewModel.CancelMeshViewportTransform();
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_shutdownComplete)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        if (_shutdownCoordinator.IsAttemptInProgress) return;
        await _projectTransitions.CancelAndWaitAsync();
        if (!_shutdownPrepared)
        {
            if (!await ResolveDirtyCodeAsync()) return;
            _contentDropCancellation.Cancel();
            Task[] contentDrops;
            lock (_contentDropSync) contentDrops = _contentDropTasks.ToArray();
            await Task.WhenAll(contentDrops);
            _previewTimer.Stop();
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            SceneVulkanViewportHost.PointerFact -= OnSceneViewportPointerFact;
            SceneVulkanViewportHost.MetricsChanged -= OnSceneViewportMetricsChanged;
            try
            {
                _layout = CaptureLayout();
                await _layoutStore.SaveAsync(_layout, CancellationToken.None);
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Failed to persist the Studio window layout.");
            }
            _shutdownPrepared = true;
        }

        var result = await _shutdownCoordinator.TryShutdownAsync(_viewModel, CancellationToken.None);
        if (!result.TerminalCleanupComplete)
        {
            SceneVulkanViewportHost.SetPresentationVisible(false);
            VulkanUnavailablePlaceholder.Visibility = Visibility.Visible;
            Log.Error(
                result.Failure,
                "Studio shutdown stopped before HWND destruction because Vulkan cleanup remains incomplete.");
            return;
        }

        if (result.Failure is not null)
        {
            Log.Warning(result.Failure, "Studio renderer cleanup completed with diagnostics.");
        }
        _shutdownComplete = true;
        Close();
    }

    private async Task<bool> ResolveDirtyCodeAsync()
    {
        if (!_viewModel.IsCodeDirty) return true;
        var choice = MessageBox.Show(
            this,
            "The current C# source has unsaved changes. Save them before continuing?",
            "Unsaved C# source",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);
        if (choice == MessageBoxResult.Cancel) return false;
        if (choice == MessageBoxResult.Yes)
        {
            await _viewModel.SaveCodeChangesAsync();
            return !_viewModel.IsCodeDirty;
        }

        await _viewModel.DiscardCodeChangesAsync();
        return !_viewModel.IsCodeDirty;
    }
}

internal static class RekallAgeCodexApprovalPresenter
{
    private const int MaximumSummaryCharacters = 1_200;
    private static readonly IReadOnlyDictionary<string, string> Labels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["command"] = "Command", ["cwd"] = "Working directory",
            ["path"] = "Path", ["filePath"] = "Path", ["reason"] = "Reason",
            ["server"] = "MCP server", ["serverName"] = "MCP server", ["mcpServer"] = "MCP server",
            ["tool"] = "MCP tool", ["toolName"] = "MCP tool", ["message"] = "Message"
        };

    internal static bool TryFormat(RekallAgeCodexApprovalRequest request, out string summary)
    {
        var supported = request.Method.Equals("item/commandExecution/requestApproval", StringComparison.Ordinal)
            || request.Method.Equals("item/fileChange/requestApproval", StringComparison.Ordinal)
            || request.Method.Equals("mcpServer/elicitation/request", StringComparison.Ordinal);
        if (!supported || request.Parameters.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            summary = string.Empty;
            return false;
        }

        var facts = new List<string>();
        Collect(request.Parameters, facts);
        summary = string.Join(Environment.NewLine, facts.Distinct(StringComparer.Ordinal));
        if (summary.Length > MaximumSummaryCharacters)
        {
            summary = summary[..(MaximumSummaryCharacters - 1)] + "…";
        }
        return summary.Length > 0;
    }

    private static void Collect(System.Text.Json.JsonElement element, List<string> facts)
    {
        if (facts.Sum(item => item.Length) >= MaximumSummaryCharacters) return;
        if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (Labels.TryGetValue(property.Name, out var label)
                    && TryDisplayValue(property.Value, out var value))
                {
                    facts.Add($"{label}: {value}");
                }
                if (property.Value.ValueKind is System.Text.Json.JsonValueKind.Object or System.Text.Json.JsonValueKind.Array)
                {
                    Collect(property.Value, facts);
                }
            }
        }
        else if (element.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) Collect(item, facts);
        }
    }

    private static bool TryDisplayValue(System.Text.Json.JsonElement element, out string value)
    {
        value = element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => element.GetString() ?? string.Empty,
            System.Text.Json.JsonValueKind.Array => string.Join(" ", element.EnumerateArray()
                .Where(item => item.ValueKind == System.Text.Json.JsonValueKind.String)
                .Select(item => item.GetString())),
            _ => string.Empty
        };
        value = value.ReplaceLineEndings(" ");
        if (value.Length > 300) value = value[..299] + "…";
        return !string.IsNullOrWhiteSpace(value);
    }
}
