using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Input;
using Rekall.Age.Editor.Contracts;
using Serilog;

namespace Rekall.Age.Studio;

public partial class MainWindow : Window
{
    private readonly RekallAgeStudioViewModel _viewModel = new();
    private readonly DispatcherTimer _previewTimer;
    private bool _shutdownComplete;
    private bool _meshTransformDragging;
    private Task? _shutdownTask;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _previewTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _previewTimer.Tick += OnPreviewTick;
        Loaded += OnLoaded;
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
            await _viewModel.InitializeAsync(projectRoot, sceneName);
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
            await _viewModel.AdvanceLivePreviewAsync();
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

    private async void OnSceneViewportMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Image image || image.ActualWidth <= 0 || image.ActualHeight <= 0) return;
        var position = e.GetPosition(image);
        await _viewModel.SelectViewportEntityAsync(
            image.ActualWidth,
            image.ActualHeight,
            position.X,
            position.Y);
        e.Handled = true;
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
        if (!_shutdownComplete)
        {
            e.Cancel = true;
            _previewTimer.Stop();
            _shutdownTask ??= _viewModel.DisposeAsync().AsTask();
            try
            {
                await _shutdownTask;
            }
            catch (Exception exception)
            {
                Log.Error(exception, "Failed to shut down the Studio workspace cleanly.");
            }
            if (!_shutdownComplete)
            {
                _shutdownComplete = true;
                Close();
            }
            return;
        }
        base.OnClosing(e);
    }
}
