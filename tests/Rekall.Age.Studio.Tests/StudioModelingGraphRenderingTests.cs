using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioModelingGraphRenderingTests
{
    private static void VerifyRenderingWorkspaceSeparatesAuthoredControlsFromResolvedDiagnostics()
    {
        Exception? failure = null;
        {
            string? studioProjectRoot = null;
            try
            {
                var window = new MainWindow();
                var viewModel = Assert.IsType<RekallAgeStudioViewModel>(window.DataContext);
                var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
                studioProjectRoot = Path.Combine(Path.GetTempPath(), "rekall-age-rendering-workspace-" + Guid.NewGuid().ToString("N"));
                viewModel.ProjectPathInput = studioProjectRoot;
                viewModel.ProjectNameInput = "Rendering Workspace Probe";
                viewModel.SceneNameInput = "Main";
                ((RekallAgeAsyncCommand)viewModel.CreateCommand).ExecuteAsync(null).GetAwaiter().GetResult();
                ((RekallAgeAsyncCommand)viewModel.AddEntityCommand).ExecuteAsync(null).GetAwaiter().GetResult();
                Assert.True(viewModel.AttachQualityProfileCommand.CanExecute(null));
                ((RekallAgeAsyncCommand)viewModel.AttachQualityProfileCommand).ExecuteAsync(null).GetAwaiter().GetResult();
                Assert.True(viewModel.ApplyQualityCommand.CanExecute(null), viewModel.StatusText);

                window.Width = 1500;
                window.Height = 940;
                window.Show();
                window.UpdateLayout();
                var workspaceSelector = Assert.IsType<TabControl>(window.FindName("WorkspaceSelector"));
                workspaceSelector.SelectedItem = workspaceSelector.Items.OfType<TabItem>().Single(
                    item => string.Equals(item.Header as string, "World", StringComparison.Ordinal));
                window.UpdateLayout();
                var outputTabs = Assert.IsType<TabControl>(window.FindName("OutputTabs"));
                var renderingTab = outputTabs.Items.OfType<TabItem>().Single(item =>
                    item.Header?.ToString() == "Rendering");
                outputTabs.SelectedItem = renderingTab;
                window.UpdateLayout();

                var preset = Descendants<ComboBox>(window).Single(combo =>
                    combo.GetBindingExpression(ComboBox.SelectedItemProperty)?.ParentBinding.Path.Path
                    == nameof(RekallAgeStudioViewModel.SelectedQualityPreset));
                var buttons = Descendants<Button>(window).ToArray();
                var apply = Assert.Single(buttons, button => ReferenceEquals(button.Command, viewModel.ApplyQualityCommand));
                var capture = Assert.Single(buttons, button => ReferenceEquals(button.Command, viewModel.CaptureQualityCommand));
                var compare = Assert.Single(buttons, button => ReferenceEquals(button.Command, viewModel.CompareQualityCommand));
                Assert.Same(viewModel.ApplyQualityCommand, apply.Command);
                Assert.Same(viewModel.CaptureQualityCommand, capture.Command);
                Assert.Same(viewModel.CompareQualityCommand, compare.Command);
                Assert.NotNull(preset.GetBindingExpression(ComboBox.SelectedItemProperty));
                Assert.Equal("Unavailable", viewModel.TotalGpuMillisecondsText);
                var renderingSurface = Assert.IsAssignableFrom<FrameworkElement>(renderingTab.Content);
                Assert.True(renderingSurface.ActualWidth > 900);
                Assert.True(renderingSurface.ActualHeight > 100);

                var bitmap = new RenderTargetBitmap(1500, 940, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                var output = Path.Combine(repositoryRoot, "artifacts", "studio-acceptance", "rendering-workbench.png");
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(output)) encoder.Save(stream);
                Assert.True(new FileInfo(output).Length > 20_000);

                window.Hide();
                viewModel.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                if (studioProjectRoot is not null && Directory.Exists(studioProjectRoot))
                    Directory.Delete(studioProjectRoot, recursive: true);
            }
        }

        Assert.Null(failure);
    }

    [Fact]
    public void ProceduralGraphRendersReadOnlyNodeMetricsWithoutBindingBackToThem()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            App? app = null;
            try
            {
                app = new App();
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                VerifyRenderingWorkspaceSeparatesAuthoredControlsFromResolvedDiagnostics();
                var window = new MainWindow();
                var viewModel = Assert.IsType<RekallAgeStudioViewModel>(window.DataContext);
                var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
                var studioProjectRoot = Path.Combine(Path.GetTempPath(), "rekall-age-modeling-render-" + Guid.NewGuid().ToString("N"));
                viewModel.ProjectPathInput = studioProjectRoot;
                viewModel.ProjectNameInput = "Modeling Render Probe";
                viewModel.SceneNameInput = "Main";
                ((RekallAgeAsyncCommand)viewModel.CreateCommand).ExecuteAsync(null).GetAwaiter().GetResult();
                viewModel.SelectedMeshPrimitive = "box";
                viewModel.MeshPrimitiveAssetIdInput = "starter-cube";
                ((RekallAgeAsyncCommand)viewModel.CreateMeshPrimitiveCommand).ExecuteAsync(null).GetAwaiter().GetResult();
                Assert.NotNull(viewModel.MeshViewportImage);
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
                var workspaceSelector = Assert.IsType<TabControl>(window.FindName("WorkspaceSelector"));
                workspaceSelector.SelectedItem = workspaceSelector.Items.OfType<TabItem>().Single(
                    item => string.Equals(item.Header as string, "World", StringComparison.Ordinal));
                window.UpdateLayout();
                var graphTabs = Descendants<TabControl>(window).Single(item =>
                    item.Items.OfType<TabItem>().Any(tab => string.Equals(tab.Header as string, "Mesh Edit", StringComparison.Ordinal)));
                graphTabs.SelectedIndex = 1;
                window.UpdateLayout();
                workspaceSelector.SelectedItem = workspaceSelector.Items.OfType<TabItem>().Single(
                    item => string.Equals(item.Header as string, "Modeling", StringComparison.Ordinal));
                window.UpdateLayout();
                var host = Assert.IsType<ModelingWorkspace>(window.FindName("ModelingWorkspaceHost"));
                var projectBar = Assert.IsType<Border>(window.FindName("ProjectBar"));
                Assert.Equal(Visibility.Visible, host.Visibility);
                Assert.Equal(Visibility.Collapsed, projectBar.Visibility);
                Assert.True(host.ActualWidth > 1400);
                Assert.True(host.ActualHeight > 700);

                var publish = Assert.IsType<Button>(host.FindName("PublishModelButton"));
                var place = Assert.IsType<Button>(host.FindName("PlaceModelButton"));
                var publishAndPlace = Assert.IsType<Button>(host.FindName("PublishAndPlaceModelButton"));
                var modelAssetId = Assert.IsType<TextBox>(host.FindName("ModelAssetIdInput"));
                var positionX = Assert.IsType<TextBox>(host.FindName("ModelPositionXInput"));
                Assert.Same(viewModel.PublishModelCommand, publish.Command);
                Assert.Same(viewModel.PlaceModelCommand, place.Command);
                Assert.Same(viewModel.PublishAndPlaceModelCommand, publishAndPlace.Command);
                Assert.NotNull(modelAssetId.GetBindingExpression(TextBox.TextProperty));
                Assert.NotNull(positionX.GetBindingExpression(TextBox.TextProperty));
                Assert.Contains("Publish", publish.Content?.ToString(), StringComparison.OrdinalIgnoreCase);
                Assert.Contains("Place", place.Content?.ToString(), StringComparison.OrdinalIgnoreCase);
                Assert.True(viewModel.PublishAndPlaceModelCommand.CanExecute(null));
                positionX.Text = "not-a-number";
                positionX.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                Assert.False(viewModel.PublishAndPlaceModelCommand.CanExecute(null));

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
                Directory.Delete(studioProjectRoot, recursive: true);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                app?.Shutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Studio render thread did not complete.");

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
