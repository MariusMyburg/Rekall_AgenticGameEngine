using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioModelingGraphRenderingTests
{
    [Fact]
    public void ProceduralGraphRendersReadOnlyNodeMetricsWithoutBindingBackToThem()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App();
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var window = new MainWindow();
                var viewModel = Assert.IsType<RekallAgeStudioViewModel>(window.DataContext);
                var descriptor = RekallAgeModelingNodeCatalog.CreateDefault()
                    .Find("rekall.modeling.primitive.box", 1)!;
                viewModel.ModelingGraphNodes.Add(new RekallAgeStudioModelingGraphNodeView(
                    new("box", descriptor.TypeId, descriptor.TypeVersion, new JsonObject()),
                    descriptor,
                    [],
                    incomingLinkCount: 2,
                    outgoingLinkCount: 3));

                window.Width = 1480;
                window.Height = 820;
                window.Show();
                window.UpdateLayout();
                var graphTabs = Descendants<TabControl>(window).Single(item => item.Items.Count == 2);
                graphTabs.SelectedIndex = 1;
                window.UpdateLayout();

                window.Hide();
                viewModel.DisposeAsync().AsTask().GetAwaiter().GetResult();
                app.Shutdown();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Studio render thread did not complete.");

        Assert.Null(failure);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
