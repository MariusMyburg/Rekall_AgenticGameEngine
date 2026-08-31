using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Rekall.Age.Studio;

namespace Rekall.Age.Studio.Tests;

[Collection(WpfApplicationTestCollection.Name)]
public sealed class StudioWorldTransformControlsWpfTests(WpfApplicationTestFixture wpf)
{
    [Fact]
    public void SnapEditorsMeasureAtReadableWidthAndExposeAccessibleNames()
    {
        wpf.Invoke(() =>
        {
            var window = new MainWindow();
            Assert.IsType<TabControl>(window.FindName("WorkspaceSelector")).SelectedIndex = 1;
            AssertEditor(window, "MoveSnapEditor", "Move snap distance");
            AssertEditor(window, "RotationSnapEditor", "Rotation snap angle");
            AssertEditor(window, "ScaleSnapEditor", "Scale snap increment");
        });
    }

    private static void AssertEditor(Window window, string name, string accessibleName)
    {
        var editor = Assert.IsType<TextBox>(window.FindName(name));
        editor.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        editor.Arrange(new Rect(0, 0, editor.DesiredSize.Width, editor.DesiredSize.Height));
        Assert.True(editor.DesiredSize.Width >= 64, $"{name} desired width was {editor.DesiredSize.Width} DIPs.");
        Assert.True(editor.ActualWidth >= 64, $"{name} arranged width was {editor.ActualWidth} DIPs.");
        Assert.Equal(accessibleName, AutomationProperties.GetName(editor));
    }
}
