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
        Assert.Contains("_importPolicy.Classify", source, StringComparison.Ordinal);
        Assert.Contains("ImportContentAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Exists", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadAll", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EnumerateFiles", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SearchOption.AllDirectories", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FallibleUiWorkUsesOneLifetimeBoundGuard()
    {
        var source = Source("ContentBrowser.xaml.cs");

        Assert.Contains("CancellationTokenSource _lifetime", source, StringComparison.Ordinal);
        Assert.Contains("Unloaded +=", source, StringComparison.Ordinal);
        Assert.Contains("ExecuteUiAsync", source, StringComparison.Ordinal);
        Assert.Contains("OperationCanceledException", source, StringComparison.Ordinal);
        Assert.Contains("ReportContentBrowserFailure", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CancellationToken.None", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UiOperationGuardContainsCancellationAndReportsBoundedFailures()
    {
        var reports = new List<(string Code, string Summary)>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await ContentBrowser.ExecuteUiOperationAsync(
            token => Task.FromCanceled(token), cancellation.Token,
            (code, summary) => reports.Add((code, summary)));
        Assert.Empty(reports);

        await ContentBrowser.ExecuteUiOperationAsync(
            _ => Task.FromException(new IOException("sentinel-private-path")), CancellationToken.None,
            (code, summary) => reports.Add((code, summary)));
        var report = Assert.Single(reports);
        Assert.Equal("REKALL_CONTENT_BROWSER_UI_FAILED", report.Code);
        Assert.DoesNotContain("sentinel", report.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(320)]
    [InlineData(700)]
    public void ToolbarKeepsPrimaryLabeledActionsVisibleAtNarrowWidths(double width)
    {
        wpf.Invoke(() =>
        {
            var browser = new ContentBrowser();
            browser.Measure(new Size(width, 500));
            browser.Arrange(new Rect(0, 0, width, 500));
            browser.UpdateLayout();

            foreach (var name in new[] { "ContentSearchBox", "RefreshContentButton", "ImportContentButton" })
            {
                var control = Assert.IsAssignableFrom<FrameworkElement>(browser.FindName(name));
                Assert.True(control.ActualWidth > 0, $"{name} collapsed at {width} DIPs.");
                var origin = control.TranslatePoint(new Point(), browser);
                Assert.True(origin.X >= 0 && origin.X + control.ActualWidth <= width + 0.5,
                    $"{name} clipped at {width} DIPs: x={origin.X}, width={control.ActualWidth}.");
            }
        });
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
