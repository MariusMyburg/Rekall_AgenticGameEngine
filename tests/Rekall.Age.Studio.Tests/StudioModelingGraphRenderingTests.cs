using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
                var evaluation = session.EvaluateAsync("mesh", CancellationToken.None).AsTask().GetAwaiter().GetResult();
                Assert.True(evaluation.Succeeded,
                    string.Join(Environment.NewLine, evaluation.Diagnostics.Select(item => $"{item.Code}: {item.Message}")));
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

                var workspaceSelector = Assert.IsType<TabControl>(window.FindName("WorkspaceSelector"));
                workspaceSelector.SelectedIndex = 1;
                window.UpdateLayout();
                var host = Assert.IsType<ModelingWorkspace>(window.FindName("ModelingWorkspaceHost"));
                var projectBar = Assert.IsType<Border>(window.FindName("ProjectBar"));
                Assert.Equal(Visibility.Visible, host.Visibility);
                Assert.Equal(Visibility.Collapsed, projectBar.Visibility);
                Assert.True(host.ActualWidth > 1400);
                Assert.True(host.ActualHeight > 700);

                var bitmap = new RenderTargetBitmap(1480, 820, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                var output = Path.Combine(repositoryRoot, "artifacts", "studio-acceptance", "modeling-workspace.png");
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(output)) encoder.Save(stream);
                Assert.True(new FileInfo(output).Length > 10_000);

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
