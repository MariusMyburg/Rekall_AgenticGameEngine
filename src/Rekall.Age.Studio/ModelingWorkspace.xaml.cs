using System.Windows.Controls;
using System.Windows.Input;

namespace Rekall.Age.Studio;

public partial class ModelingWorkspace : UserControl
{
    private bool _dragging;
    private bool _orbiting;
    private bool _panning;
    private bool _modalOperationDragging;
    private System.Windows.Point _lastNavigationPoint;

    public ModelingWorkspace()
    {
        InitializeComponent();
        // Attached in code-behind rather than XAML: WPF XAML cannot declare two handlers for the
        // same routed event (MouseMove) on one element, but a routed event supports any number of
        // CLR subscribers, so the orbit/pan/zoom navigation handlers are added alongside the
        // existing gizmo-drag/selection handlers already declared in the markup.
        MeshViewportImage.MouseDown += OnMeshViewportMouseDown;
        MeshViewportImage.MouseMove += OnMeshViewportMouseMoveForNavigation;
        MeshViewportImage.MouseUp += OnMeshViewportMouseUpForNavigation;
        MeshViewportImage.MouseWheel += OnMeshViewportWheel;
    }

    private RekallAgeStudioViewModel? ViewModel => DataContext as RekallAgeStudioViewModel;

    private void OnMeshMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is null || sender is not Image image || image.ActualWidth <= 0 || image.ActualHeight <= 0) return;
        var point = e.GetPosition(image);
        var modifiers = Keyboard.Modifiers;
        if (modifiers.HasFlag(ModifierKeys.Alt) && ViewModel.BeginModalMeshOperationDrag(point.X / image.ActualWidth))
        {
            // Blender-style modal parameter drag (Alt+left-drag): live-previews the selected
            // operation's numeric parameter as the mouse moves instead of gizmo-dragging a point
            // or selecting an element, so it must claim the gesture before either of those do.
            _modalOperationDragging = true;
            image.CaptureMouse();
        }
        else if (ViewModel.BeginMeshViewportTransform(point.X / image.ActualWidth, point.Y / image.ActualHeight))
        {
            _dragging = true;
            image.CaptureMouse();
        }
        else
        {
            ViewModel.SelectMeshViewportElement(
                point.X / image.ActualWidth,
                point.Y / image.ActualHeight,
                modifiers.HasFlag(ModifierKeys.Shift),
                modifiers.HasFlag(ModifierKeys.Control));
        }
        e.Handled = true;
    }

    private async void OnMeshMouseMove(object sender, MouseEventArgs e)
    {
        if (_orbiting || _panning || ViewModel is null || sender is not Image image) return;
        var point = e.GetPosition(image);
        if (_modalOperationDragging)
        {
            await ViewModel.UpdateModalMeshOperationDragAsync(point.X / image.ActualWidth);
            e.Handled = true;
            return;
        }
        if (!_dragging) return;
        ViewModel.UpdateMeshViewportTransform(point.X / image.ActualWidth, point.Y / image.ActualHeight);
        e.Handled = true;
    }

    private async void OnMeshMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is null || sender is not Image image) return;
        var point = e.GetPosition(image);
        if (_modalOperationDragging)
        {
            _modalOperationDragging = false;
            image.ReleaseMouseCapture();
            await ViewModel.CompleteModalMeshOperationDragAsync(point.X / image.ActualWidth);
            e.Handled = true;
            return;
        }
        if (!_dragging) return;
        _dragging = false;
        image.ReleaseMouseCapture();
        await ViewModel.CompleteMeshViewportTransformAsync(point.X / image.ActualWidth, point.Y / image.ActualHeight);
        e.Handled = true;
    }

    private void OnMeshLostCapture(object sender, MouseEventArgs e)
    {
        if (_modalOperationDragging)
        {
            _modalOperationDragging = false;
            ViewModel?.CancelModalMeshOperationDrag();
            return;
        }
        if (!_dragging || ViewModel is null) return;
        _dragging = false;
        ViewModel.CancelMeshViewportTransform();
    }

    /// <summary>Starts an orbit (middle-drag) or pan (shift + middle-drag) navigation gesture.</summary>
    private void OnMeshViewportMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is null || sender is not Image image || e.ChangedButton != MouseButton.Middle) return;
        _lastNavigationPoint = e.GetPosition(image);
        _panning = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        _orbiting = !_panning;
        image.CaptureMouse();
        e.Handled = true;
    }

    /// <summary>Applies an in-progress orbit/pan gesture's mouse-delta to the viewport camera.</summary>
    private void OnMeshViewportMouseMoveForNavigation(object sender, MouseEventArgs e)
    {
        if (ViewModel is null || sender is not Image image || (!_orbiting && !_panning)) return;
        var point = e.GetPosition(image);
        var delta = point - _lastNavigationPoint;
        _lastNavigationPoint = point;
        if (_orbiting) ViewModel.OrbitMeshViewport(delta.X * 0.01, delta.Y * 0.01);
        else ViewModel.PanMeshViewport(delta.X, delta.Y);
        e.Handled = true;
    }

    private void OnMeshViewportMouseUpForNavigation(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Image image || e.ChangedButton != MouseButton.Middle) return;
        _orbiting = false;
        _panning = false;
        image.ReleaseMouseCapture();
        e.Handled = true;
    }

    /// <summary>Scroll-wheel dolly zoom.</summary>
    private void OnMeshViewportWheel(object sender, MouseWheelEventArgs e)
    {
        if (ViewModel is null) return;
        ViewModel.ZoomMeshViewport(e.Delta > 0 ? 1.1 : 1 / 1.1);
        e.Handled = true;
    }

    private bool _draggingGraphNode;

    /// <summary>A port hit arms/completes a link; a node-body hit selects and begins a drag; empty space clears both.</summary>
    private async void OnModelingGraphCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is null || sender is not Image image || image.ActualWidth <= 0 || image.ActualHeight <= 0) return;
        var point = e.GetPosition(image);
        image.CaptureMouse();
        _draggingGraphNode = true;
        await ViewModel.ClickModelingGraphCanvasAsync(point.X / image.ActualWidth, point.Y / image.ActualHeight);
        e.Handled = true;
    }

    private void OnModelingGraphCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (!_draggingGraphNode || ViewModel is null || sender is not Image image) return;
        var point = e.GetPosition(image);
        ViewModel.UpdateModelingGraphNodeDrag(point.X / image.ActualWidth, point.Y / image.ActualHeight);
        e.Handled = true;
    }

    private void OnModelingGraphCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_draggingGraphNode || sender is not Image image) return;
        _draggingGraphNode = false;
        image.ReleaseMouseCapture();
        ViewModel?.CompleteModelingGraphNodeDrag();
        e.Handled = true;
    }

    private void OnModelingGraphCanvasLostCapture(object sender, MouseEventArgs e)
    {
        if (!_draggingGraphNode) return;
        _draggingGraphNode = false;
        ViewModel?.CompleteModelingGraphNodeDrag();
    }
}
