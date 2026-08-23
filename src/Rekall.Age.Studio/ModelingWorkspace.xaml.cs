using System.Windows.Controls;
using System.Windows.Input;

namespace Rekall.Age.Studio;

public partial class ModelingWorkspace : UserControl
{
    private bool _dragging;

    public ModelingWorkspace() => InitializeComponent();

    private RekallAgeStudioViewModel? ViewModel => DataContext as RekallAgeStudioViewModel;

    private void OnMeshMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is null || sender is not Image image || image.ActualWidth <= 0 || image.ActualHeight <= 0) return;
        var point = e.GetPosition(image);
        if (ViewModel.BeginMeshViewportTransform(point.X / image.ActualWidth, point.Y / image.ActualHeight))
        {
            _dragging = true;
            image.CaptureMouse();
        }
        else
        {
            var modifiers = Keyboard.Modifiers;
            ViewModel.SelectMeshViewportElement(
                point.X / image.ActualWidth,
                point.Y / image.ActualHeight,
                modifiers.HasFlag(ModifierKeys.Shift),
                modifiers.HasFlag(ModifierKeys.Control));
        }
        e.Handled = true;
    }

    private void OnMeshMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || ViewModel is null || sender is not Image image) return;
        var point = e.GetPosition(image);
        ViewModel.UpdateMeshViewportTransform(point.X / image.ActualWidth, point.Y / image.ActualHeight);
        e.Handled = true;
    }

    private async void OnMeshMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging || ViewModel is null || sender is not Image image) return;
        _dragging = false;
        var point = e.GetPosition(image);
        image.ReleaseMouseCapture();
        await ViewModel.CompleteMeshViewportTransformAsync(point.X / image.ActualWidth, point.Y / image.ActualHeight);
        e.Handled = true;
    }

    private void OnMeshLostCapture(object sender, MouseEventArgs e)
    {
        if (!_dragging || ViewModel is null) return;
        _dragging = false;
        ViewModel.CancelMeshViewportTransform();
    }
}
