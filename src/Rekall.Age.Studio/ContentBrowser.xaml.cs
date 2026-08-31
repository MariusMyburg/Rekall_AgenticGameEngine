using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Rekall.Age.Editor.Contracts;

namespace Rekall.Age.Studio;

public partial class ContentBrowser : UserControl
{
    private readonly RekallAgeStudioContentImportPolicy _importPolicy = new();
    private CancellationTokenSource _lifetime = new();
    private Point? _contentDragOrigin;

    public ContentBrowser()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    internal void FocusSearch()
    {
        ContentSearchBox.Focus();
        Keyboard.Focus(ContentSearchBox);
    }

    private async void OnRefreshContentClick(object sender, RoutedEventArgs e) =>
        await ExecuteUiAsync(token => DataContext is RekallAgeStudioViewModel viewModel
            ? viewModel.RefreshContentBrowserAsync(token)
            : Task.CompletedTask);

    private async void OnImportContentClick(object sender, RoutedEventArgs e) =>
        await ExecuteUiAsync(async token =>
    {
        if (DataContext is not RekallAgeStudioViewModel viewModel) return;
        var picker = new OpenFileDialog
        {
            Title = "Import project content",
            Multiselect = true,
            CheckFileExists = true,
            Filter = "Supported content|*.glb;*.gltf;*.png;*.jpg;*.jpeg;*.dds;*.ktx2;*.wav;*.mp3;*.glsl;*.vert;*.frag;*.comp;*.hlsl|All files|*.*"
        };
        if (picker.ShowDialog(Window.GetWindow(this)) == true)
            await viewModel.ImportContentAsync(SafeCandidates(picker.FileNames), token);
    });

    private void OnFilesDragEnter(object sender, DragEventArgs e) => ApplyDropEffect(e);
    private void OnFilesDragOver(object sender, DragEventArgs e) => ApplyDropEffect(e);
    private void OnFilesDragLeave(object sender, DragEventArgs e) { }

    private async void OnFilesDropped(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var files = DroppedCandidates(e.Data);
        await ExecuteUiAsync(token => DataContext is RekallAgeStudioViewModel viewModel && files.Length > 0
            ? viewModel.ImportContentAsync(files, token)
            : Task.CompletedTask);
    }

    private void ApplyDropEffect(DragEventArgs e)
    {
        e.Effects = DroppedCandidates(e.Data).Length > 0
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private string[] DroppedCandidates(IDataObject data) => data.GetDataPresent(DataFormats.FileDrop)
        ? SafeCandidates(data.GetData(DataFormats.FileDrop) as string[] ?? [])
        : [];

    private string[] SafeCandidates(IEnumerable<string> paths) => paths
        .Where(path => !string.IsNullOrWhiteSpace(path)
            && Path.IsPathFullyQualified(path)
            && _importPolicy.Classify(path).Accepted)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_lifetime.IsCancellationRequested) return;
        _lifetime.Dispose();
        _lifetime = new CancellationTokenSource();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _lifetime.Cancel();

    private Task ExecuteUiAsync(Func<CancellationToken, Task> operation) => ExecuteUiOperationAsync(
        operation,
        _lifetime.Token,
        (code, summary) => (DataContext as RekallAgeStudioViewModel)?.ReportContentBrowserFailure(code, summary));

    internal static async Task ExecuteUiOperationAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken,
        Action<string, string> reportFailure)
    {
        try
        {
            await operation(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            reportFailure("REKALL_CONTENT_BROWSER_UI_FAILED",
                "The Content Browser action could not be completed. Retry or inspect Studio logs.");
        }
    }

    private void OnBrowserSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var showCategories = e.NewSize.Width >= 480;
        CategoryColumn.Width = new GridLength(showCategories ? 150 : 0);
        CategorySplitterColumn.Width = new GridLength(showCategories ? 5 : 0);
        CategoryPanel.Visibility = showCategories ? Visibility.Visible : Visibility.Collapsed;
        CategorySplitter.Visibility = CategoryPanel.Visibility;

        var showDetails = e.NewSize.Width >= 650;
        DetailsColumn.Width = new GridLength(showDetails ? Math.Min(250, e.NewSize.Width * 0.34) : 0);
        DetailsSplitterColumn.Width = new GridLength(showDetails ? 5 : 0);
        DetailsPanel.Visibility = showDetails ? Visibility.Visible : Visibility.Collapsed;
        DetailsSplitter.Visibility = DetailsPanel.Visibility;
    }

    private void OnContentItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is RekallAgeStudioViewModel viewModel && viewModel.OpenSelectedContentCommand.CanExecute(null))
            viewModel.OpenSelectedContentCommand.Execute(null);
    }

    private void OnContentItemMouseDown(object sender, MouseButtonEventArgs e) =>
        _contentDragOrigin = e.GetPosition(ContentItemList);

    private void OnContentItemMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _contentDragOrigin is not { } origin
            || ContentItemList.SelectedItem is not RekallAgeContentBrowserItem item)
        {
            return;
        }

        var current = e.GetPosition(ContentItemList);
        if (Math.Abs(current.X - origin.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - origin.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _contentDragOrigin = null;
        var payload = RekallAgeStudioContentDragPayload.FromItem(item);
        if (payload.Operations.Count == 0) return;
        var data = new DataObject();
        data.SetData(RekallAgeStudioContentDragService.DataFormat, payload.ToJson());
        DragDrop.DoDragDrop(ContentItemList, data, DragDropEffects.Copy);
    }

    private void OnCardViewClick(object sender, RoutedEventArgs e)
    {
        CardContentViewButton.IsChecked = true;
        CompactContentViewButton.IsChecked = false;
    }

    private void OnCompactViewClick(object sender, RoutedEventArgs e)
    {
        CompactContentViewButton.IsChecked = true;
        CardContentViewButton.IsChecked = false;
    }
}
