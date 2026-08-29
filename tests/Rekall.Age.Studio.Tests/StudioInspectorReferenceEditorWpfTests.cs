using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Rekall.Age.Editor.Contracts;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioInspectorReferenceEditorWpfTests
{
    [Fact]
    public void EntityReferenceRenderPreservesStableValueAndUserSelectionProjectsStableIdOnce()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            App? app = null;
            MainWindow? window = null;
            try
            {
                app = new App();
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                window = new MainWindow();
                var row = new RekallAgeStudioInspectorPropertyEditorModel(
                    "Game.Targeting",
                    new RekallAgeInspectorPropertyModel("target", "entity-1", "string")
                    {
                        TypeName = "String",
                        EditorKind = "entityRef"
                    },
                    entityChoices:
                    [
                        new("Player (entity-1)", "entity-1"),
                        new("Target (entity-2)", "entity-2")
                    ]);
                var stableValueChanges = 0;
                row.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(RekallAgeStudioInspectorPropertyEditorModel.TextValue))
                    {
                        stableValueChanges++;
                    }
                };

                var presenter = new ContentControl
                {
                    Content = row,
                    ContentTemplate = Assert.IsType<DataTemplate>(window.Resources["InspectorEntityRefEditorTemplate"])
                };
                var inspector = Assert.IsType<Border>(window.FindName("InspectorPanel"));
                var inspectorGrid = Assert.IsType<Grid>(inspector.Child);
                Grid.SetRow(presenter, 2);
                inspectorGrid.Children.Add(presenter);

                window.Show();
                var workspaceSelector = Assert.IsType<TabControl>(window.FindName("WorkspaceSelector"));
                workspaceSelector.SelectedItem = workspaceSelector.Items.OfType<TabItem>().Single(
                    item => string.Equals(item.Header as string, "World", StringComparison.Ordinal));
                window.UpdateLayout();

                var combo = Assert.Single(Descendants<ComboBox>(presenter));
                Assert.Equal("entity-1", row.TextValue);
                Assert.False(row.IsDirty);
                Assert.Equal(0, stableValueChanges);

                combo.SelectedItem = row.ChoiceItems[1];
                window.UpdateLayout();

                Assert.Equal("entity-2", row.TextValue);
                Assert.True(row.IsDirty);
                Assert.Equal(1, stableValueChanges);
                Assert.True(row.TryCreateValue(out var value, out var error), error);
                Assert.Equal("\"entity-2\"", value!.ToJsonString());
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                window?.Close();
                app?.Shutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Inspector reference WPF thread did not complete.");
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
