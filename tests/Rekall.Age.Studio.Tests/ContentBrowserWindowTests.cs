using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Rekall.Age.Studio;
using Rekall.Age.AssetPipeline.Commands;
using Rekall.Age.AssetPipeline;
using Rekall.Age.Assets;

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

        Assert.Contains("ItemsSource=\"{Binding FilteredContentCards}\"", source, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedContentCard", source, StringComparison.Ordinal);
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
        Assert.Contains("SelectedContentPreview.Thumbnail", source, StringComparison.Ordinal);
        Assert.Contains("SelectedContentPreview.Health", source, StringComparison.Ordinal);
        Assert.Contains("SelectedContentPreview.Summary", source, StringComparison.Ordinal);
        Assert.Contains("DataTrigger Binding=\"{Binding Family}\"", source, StringComparison.Ordinal);
        Assert.Contains("IconContentAudio", source, StringComparison.Ordinal);
        Assert.Contains("IconContentModel", source, StringComparison.Ordinal);
        Assert.Contains("Source=\"{Binding Thumbnail}\"", source, StringComparison.Ordinal);
        Assert.Contains("Loaded=\"OnContentThumbnailLoaded\"", source, StringComparison.Ordinal);
        Assert.Contains("Unloaded=\"OnContentThumbnailUnloaded\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<ItemsPanelTemplate x:Key=\"ContentCardItemsPanel\"><WrapPanel", source, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.IsVirtualizing=\"True\"", source, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\"", source, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.ScrollUnit=\"Pixel\"", source, StringComparison.Ordinal);
        Assert.Contains("ContentCompactTemplate", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DropCodeAdvertisesOnlySupportedAbsolutePathsButPassesTheWholeBatchToTheSession()
    {
        var source = Source("ContentBrowser.xaml.cs");

        Assert.Contains("DataFormats.FileDrop", source, StringComparison.Ordinal);
        Assert.Contains("Path.IsPathFullyQualified", source, StringComparison.Ordinal);
        Assert.Contains("CanAdvertiseCopy", source, StringComparison.Ordinal);
        Assert.Contains("ImportContentAsync(files", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SafeCandidates", source, StringComparison.Ordinal);
        Assert.Contains("ImportContentAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Exists", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadAll", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EnumerateFiles", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SearchOption.AllDirectories", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MixedWpfDropPreservesSuccessUnsupportedAndModuleRouteJobs()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-wpf-drop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var image = Path.Combine(root, "image.png");
            var unsupported = Path.Combine(root, "notes.xyz");
            var module = Path.Combine(root, "Logic.cs");
            File.WriteAllBytes(image, [1]);
            File.WriteAllBytes(unsupported, [2]);
            File.WriteAllText(module, "class Logic {}");
            var data = new DataObject(DataFormats.FileDrop, new[] { image, unsupported, module });

            var candidates = ContentBrowser.DroppedCandidates(data);
            Assert.True(ContentBrowser.CanAdvertiseCopy(candidates));
            Assert.Equal(3, candidates.Length);
            var session = new RekallAgeStudioContentImportSession(
                new SuccessfulImporter(), _ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask);

            var jobs = await session.ImportAsync(root, candidates, CancellationToken.None);

            Assert.Contains(jobs, job => job.Code == "REKALL_CONTENT_IMPORT_SUCCEEDED");
            Assert.Contains(jobs, job => job.Code == "REKALL_CONTENT_IMPORT_UNSUPPORTED");
            Assert.Contains(jobs, job => job.Code == "REKALL_CONTENT_IMPORT_MODULE_ROUTE_REQUIRED");
        }
        finally { Directory.Delete(root, true); }
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

    private sealed class SuccessfulImporter : IRekallAgeStudioAssetImportCommand
    {
        public ValueTask<ImportAssetWithReportResult> ImportAsync(
            string projectRoot, string sourcePath, string kind, CancellationToken cancellationToken)
        {
            var report = new RekallAgeAssetImportReport(true, "asset-image", kind, sourcePath, "Assets/image.png", []);
            return ValueTask.FromResult(new ImportAssetWithReportResult(report, RekallAgeAssetPipelineDocument.Empty));
        }
    }
}
