using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Rekall.Age.Studio;

namespace Rekall.Age.Studio.Tests;

[Collection(WpfApplicationTestCollection.Name)]
public sealed class ContentBrowserWindowTests(WpfApplicationTestFixture wpf)
{
    [Fact]
    public void ContentBrowserLoadsAsAReusableAccessibleDropSurface()
    {
        wpf.Invoke(() =>
        {
            var browser = new ContentBrowser();

            Assert.True(browser.AllowDrop);
            Assert.Equal("Content Browser", AutomationProperties.GetName(browser));
            AssertControl<TextBox>(browser, "ContentSearchBox", "Search project content");
            AssertControl<ComboBox>(browser, "ContentCategoryPicker", "Filter content category");
            AssertControl<Button>(browser, "RefreshContentButton", "Refresh project content");
            AssertControl<Button>(browser, "ImportContentButton", "Import content files");
            AssertControl<ToggleButton>(browser, "CardContentViewButton", "Card content view");
            AssertControl<ToggleButton>(browser, "CompactContentViewButton", "Compact content view");
        });
    }

    [Fact]
    public void ContentBrowserSourceBindsTheCompleteWorkflowAndKeepsActivationConsistent()
    {
        var source = Source("ContentBrowser.xaml");

        Assert.Contains("ItemsSource=\"{Binding FilteredContentItems}\"", source, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedContentItem", source, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding OpenSelectedContentCommand}\"", source, StringComparison.Ordinal);
        Assert.Contains("Key=\"Enter\" Command=\"{Binding OpenSelectedContentCommand}\"", source, StringComparison.Ordinal);
        Assert.Contains("MouseDoubleClick=\"OnContentItemDoubleClick\"", source, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding ContentWarnings}\"", source, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding ImportJobs}\"", source, StringComparison.Ordinal);
        Assert.Contains("ContentStatusText", source, StringComparison.Ordinal);
        Assert.Contains("ContentImportSummary", source, StringComparison.Ordinal);
        Assert.Contains("Import files or drag and drop", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DragEnter=\"OnFilesDragEnter\"", source, StringComparison.Ordinal);
        Assert.Contains("DragOver=\"OnFilesDragOver\"", source, StringComparison.Ordinal);
        Assert.Contains("DragLeave=\"OnFilesDragLeave\"", source, StringComparison.Ordinal);
        Assert.Contains("Drop=\"OnFilesDropped\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DropCodeOnlyProjectsSafeAbsoluteFilePathsIntoTheImportSession()
    {
        var source = Source("ContentBrowser.xaml.cs");

        Assert.Contains("DataFormats.FileDrop", source, StringComparison.Ordinal);
        Assert.Contains("Path.IsPathFullyQualified", source, StringComparison.Ordinal);
        Assert.Contains("File.Exists", source, StringComparison.Ordinal);
        Assert.Contains("ImportContentAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadAll", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EnumerateFiles", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SearchOption.AllDirectories", source, StringComparison.Ordinal);
    }

    private static void AssertControl<T>(FrameworkElement browser, string name, string automationName) where T : FrameworkElement
    {
        var control = Assert.IsType<T>(browser.FindName(name));
        Assert.Equal(automationName, AutomationProperties.GetName(control));
        Assert.NotNull(control.ToolTip);
    }

    private static string Source(string fileName)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", fileName));
    }
}
