using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
                var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
                var projectRoot = Path.Combine(repositoryRoot, "Examples", "ProceduralModelingProbe");
                var session = new RekallAgeStudioModelingGraphSession();
                session.OpenAsync(projectRoot, "hero-form", CancellationToken.None).AsTask().GetAwaiter().GetResult();
                session.EvaluateAsync("mesh", CancellationToken.None).AsTask().GetAwaiter().GetResult();
                foreach (var node in session.Nodes) viewModel.ModelingGraphNodes.Add(node);
                foreach (var parameter in session.CreateParameterEditors("box"))
                    viewModel.ModelingGraphParameterEditors.Add(parameter);

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
