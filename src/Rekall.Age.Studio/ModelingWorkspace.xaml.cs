using System.Windows.Controls;
using System.Windows.Input;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Studio;

public partial class ModelingWorkspace : UserControl
{
    private bool _dragging;
    private bool _orbiting;
    private bool _panning;
    private bool _modalOperationDragging;
    private System.Windows.Point _lastNavigationPoint;
    private readonly RekallAgeStudioMeshVulkanPreviewSession _meshPreviewSession;
    private RekallAgeStudioViewModel? _attachedViewModel;

    public ModelingWorkspace()
    {
        InitializeComponent();
        _meshPreviewSession = new(MeshVulkanViewportHost);
        MeshVulkanViewportHost.PointerFact += OnMeshViewportPointerFact;
        MeshVulkanViewportHost.MetricsChanged += OnMeshViewportMetricsChanged;
        DataContextChanged += (_, _) => AttachMeshViewport();
        Loaded += (_, _) =>
        {
            AttachMeshViewport();
            _ = PresentMeshViewportAsync(MeshVulkanViewportHost.Metrics);
        };
    }

    private RekallAgeStudioViewModel? ViewModel => DataContext as RekallAgeStudioViewModel;

    private void OnLoopCutToolClicked(object sender, System.Windows.RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        ViewModel.MeshEditDomain = RekallAgeGeometryDomain.Edge;
        if (ViewModel.MeshOperationIds.Contains("loop_cut_edges", StringComparer.Ordinal))
            ViewModel.SelectedMeshOperationId = "loop_cut_edges";
    }

    private void AttachMeshViewport()
    {
        if (ViewModel is not { } viewModel || ReferenceEquals(viewModel, _attachedViewModel)) return;
        if (_attachedViewModel is not null)
        {
            throw new InvalidOperationException("The Modeling Vulkan viewport cannot change ViewModel ownership after attachment.");
        }
        _attachedViewModel = viewModel;
        viewModel.AttachMeshVulkanPreviewSession(_meshPreviewSession);
    }

    private async void OnMeshViewportMetricsChanged(object? sender, RekallAgeStudioViewportMetrics metrics) =>
        await PresentMeshViewportAsync(metrics);

    private async Task PresentMeshViewportAsync(RekallAgeStudioViewportMetrics metrics)
    {
        if (ViewModel is null || !metrics.IsPresentable) return;
        try
        {
            await ViewModel.PresentMeshViewportAtHostSizeAsync(metrics);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async void OnMeshViewportPointerFact(object? sender, RekallAgeStudioViewportPointerFact fact)
    {
        var viewModel = ViewModel;
        var metrics = MeshVulkanViewportHost.Metrics;
        if (viewModel is null || !metrics.IsPresentable) return;
        var normalizedX = Math.Clamp(fact.DisplayX / Math.Max(1, metrics.DipWidth), 0, 1);
        var normalizedY = Math.Clamp(fact.DisplayY / Math.Max(1, metrics.DipHeight), 0, 1);

        if (fact.Kind == RekallAgeStudioViewportPointerKind.Down
            && fact.Button == RekallAgeStudioViewportPointerButton.Middle)
        {
            _lastNavigationPoint = new(fact.DisplayX, fact.DisplayY);
            _panning = fact.Modifiers.HasFlag(RekallAgeStudioViewportPointerModifiers.Shift);
            _orbiting = !_panning;
            MeshVulkanViewportHost.CapturePointer();
            return;
        }
        if (fact.Kind == RekallAgeStudioViewportPointerKind.Down
            && fact.Button == RekallAgeStudioViewportPointerButton.Left)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)
                && viewModel.BeginModalMeshOperationDrag(normalizedX))
            {
                _modalOperationDragging = true;
                MeshVulkanViewportHost.CapturePointer();
            }
            else if (viewModel.BeginMeshViewportTransform(normalizedX, normalizedY))
            {
                _dragging = true;
                MeshVulkanViewportHost.CapturePointer();
            }
            else
            {
                viewModel.SelectMeshViewportElement(
                    normalizedX,
                    normalizedY,
                    fact.Modifiers.HasFlag(RekallAgeStudioViewportPointerModifiers.Shift),
                    fact.Modifiers.HasFlag(RekallAgeStudioViewportPointerModifiers.Control));
            }
            return;
        }
        if (fact.Kind == RekallAgeStudioViewportPointerKind.Move && (_orbiting || _panning))
        {
            var point = new System.Windows.Point(fact.DisplayX, fact.DisplayY);
            var delta = point - _lastNavigationPoint;
            _lastNavigationPoint = point;
            if (_orbiting) viewModel.OrbitMeshViewport(delta.X * 0.01, delta.Y * 0.01);
            else viewModel.PanMeshViewport(delta.X, delta.Y);
            return;
        }
        if (fact.Kind == RekallAgeStudioViewportPointerKind.Move && _modalOperationDragging)
        {
            await viewModel.UpdateModalMeshOperationDragAsync(normalizedX);
            return;
        }
        if (fact.Kind == RekallAgeStudioViewportPointerKind.Move && _dragging)
        {
            viewModel.UpdateMeshViewportTransform(normalizedX, normalizedY);
            return;
        }
        if (fact.Kind == RekallAgeStudioViewportPointerKind.Up
            && fact.Button == RekallAgeStudioViewportPointerButton.Middle)
        {
            _orbiting = false;
            _panning = false;
            return;
        }
        if (fact.Kind == RekallAgeStudioViewportPointerKind.Up
            && fact.Button == RekallAgeStudioViewportPointerButton.Left)
        {
            if (_modalOperationDragging)
            {
                _modalOperationDragging = false;
                await viewModel.CompleteModalMeshOperationDragAsync(normalizedX);
            }
            else if (_dragging)
            {
                _dragging = false;
                await viewModel.CompleteMeshViewportTransformAsync(normalizedX, normalizedY);
            }
            return;
        }
        if (fact.Kind == RekallAgeStudioViewportPointerKind.Wheel)
        {
            viewModel.ZoomMeshViewport(fact.WheelDelta > 0 ? 1.1 : 1 / 1.1);
            return;
        }
        if (fact.Kind is RekallAgeStudioViewportPointerKind.CaptureLost or RekallAgeStudioViewportPointerKind.FocusLost)
        {
            _orbiting = false;
            _panning = false;
            if (_modalOperationDragging) viewModel.CancelModalMeshOperationDrag();
            if (_dragging) viewModel.CancelMeshViewportTransform();
            _modalOperationDragging = false;
            _dragging = false;
        }
    }

    private bool _draggingGraphNode;
    private bool _panningModelingGraph;
    private System.Windows.Point _lastModelingGraphPanPoint;

    /// <summary>A port hit arms/completes a link; a node-body hit selects and begins a drag; empty space clears both.</summary>
    private async void OnModelingGraphCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is null || sender is not Image image || image.ActualWidth <= 0 || image.ActualHeight <= 0) return;
        var point = e.GetPosition(image);
        if (e.ChangedButton is MouseButton.Middle or MouseButton.Right)
        {
            _panningModelingGraph = true;
            _lastModelingGraphPanPoint = point;
            image.CaptureMouse();
            e.Handled = true;
            return;
        }
        if (e.ChangedButton != MouseButton.Left) return;
        image.CaptureMouse();
        _draggingGraphNode = true;
        await ViewModel.ClickModelingGraphCanvasAsync(point.X / image.ActualWidth, point.Y / image.ActualHeight);
        e.Handled = true;
    }

    private void OnModelingGraphCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (ViewModel is null || sender is not Image image || image.ActualWidth <= 0 || image.ActualHeight <= 0) return;
        var point = e.GetPosition(image);
        if (_panningModelingGraph)
        {
            var delta = point - _lastModelingGraphPanPoint;
            _lastModelingGraphPanPoint = point;
            ViewModel.PanModelingGraphCanvas(delta.X / image.ActualWidth, delta.Y / image.ActualHeight);
            e.Handled = true;
            return;
        }
        if (!_draggingGraphNode) return;
        ViewModel.UpdateModelingGraphNodeDrag(point.X / image.ActualWidth, point.Y / image.ActualHeight);
        e.Handled = true;
    }

    private void OnModelingGraphCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Image image) return;
        if (_panningModelingGraph && (e.ChangedButton is MouseButton.Middle or MouseButton.Right))
        {
            _panningModelingGraph = false;
            image.ReleaseMouseCapture();
            e.Handled = true;
            return;
        }
        if (!_draggingGraphNode || e.ChangedButton != MouseButton.Left) return;
        _draggingGraphNode = false;
        image.ReleaseMouseCapture();
        ViewModel?.CompleteModelingGraphNodeDrag();
        e.Handled = true;
    }

    private void OnModelingGraphCanvasLostCapture(object sender, MouseEventArgs e)
    {
        _panningModelingGraph = false;
        if (!_draggingGraphNode) return;
        _draggingGraphNode = false;
        ViewModel?.CompleteModelingGraphNodeDrag();
    }

    private void OnModelingGraphCanvasMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ViewModel is null || sender is not Image image || image.ActualWidth <= 0 || image.ActualHeight <= 0) return;
        var point = e.GetPosition(image);
        ViewModel.ZoomModelingGraphCanvas(e.Delta > 0 ? 1.15 : 1 / 1.15,
            point.X / image.ActualWidth, point.Y / image.ActualHeight);
        e.Handled = true;
    }

    private void OnResetModelingGraphCanvasView(object sender, System.Windows.RoutedEventArgs e) =>
        ViewModel?.ResetModelingGraphCanvasView();
}
