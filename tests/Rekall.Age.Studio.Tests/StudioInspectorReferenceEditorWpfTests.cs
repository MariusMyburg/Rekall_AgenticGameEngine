using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Rekall.Age.Editor.Contracts;

namespace Rekall.Age.Studio.Tests;

[Collection(WpfApplicationTestCollection.Name)]
public sealed class StudioInspectorReferenceEditorWpfTests
{
    private readonly WpfApplicationTestFixture _wpf;

    public StudioInspectorReferenceEditorWpfTests(WpfApplicationTestFixture wpf) => _wpf = wpf;

    [Fact]
    public void EntityReferenceRenderPreservesStableValueAndUserSelectionProjectsStableIdOnce()
    {
        _wpf.Invoke(() =>
        {
            MainWindow? window = null;
            try
            {
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
            finally
            {
                window?.Close();
            }
        });
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
