using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Rekall.Age.Studio;

public partial class ContentBrowser : UserControl
{
    private readonly RekallAgeStudioContentImportPolicy _importPolicy = new();

    public ContentBrowser() => InitializeComponent();

    internal void FocusSearch()
    {
        ContentSearchBox.Focus();
        Keyboard.Focus(ContentSearchBox);
    }

    private async void OnRefreshContentClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is RekallAgeStudioViewModel viewModel)
            await viewModel.RefreshContentBrowserAsync();
    }

    private async void OnImportContentClick(object sender, RoutedEventArgs e)
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
            await viewModel.ImportContentAsync(SafeFiles(picker.FileNames), CancellationToken.None);
    }

    private void OnFilesDragEnter(object sender, DragEventArgs e) => ApplyDropEffect(e);
    private void OnFilesDragOver(object sender, DragEventArgs e) => ApplyDropEffect(e);
    private void OnFilesDragLeave(object sender, DragEventArgs e) { }

    private async void OnFilesDropped(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not RekallAgeStudioViewModel viewModel) return;
        var files = DroppedFiles(e.Data);
        if (files.Length > 0) await viewModel.ImportContentAsync(files, CancellationToken.None);
    }

    private void ApplyDropEffect(DragEventArgs e)
    {
        e.Effects = DroppedFiles(e.Data).Any(path => _importPolicy.Classify(path).Accepted)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private static string[] DroppedFiles(IDataObject data) => data.GetDataPresent(DataFormats.FileDrop)
        ? SafeFiles(data.GetData(DataFormats.FileDrop) as string[] ?? [])
        : [];

    private static string[] SafeFiles(IEnumerable<string> paths) => paths
        .Where(path => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && File.Exists(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private void OnContentItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is RekallAgeStudioViewModel viewModel && viewModel.OpenSelectedContentCommand.CanExecute(null))
            viewModel.OpenSelectedContentCommand.Execute(null);
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
