using System.Windows;
using System.Windows.Controls;
using Rekall.Age.Modules.Commands;

namespace Rekall.Age.Studio;

public partial class CodeWorkspace : UserControl
{
    private bool _synchronizingSelection;

    public CodeWorkspace()
    {
        InitializeComponent();
    }

    private RekallAgeStudioViewModel? ViewModel => DataContext as RekallAgeStudioViewModel;

    private async void OnCodeSourceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingSelection || ViewModel is null
            || CodeSourceList.SelectedItem is not RekallAgeModuleSourceInfo source
            || ReferenceEquals(source, ViewModel.SelectedCodeSource))
        {
            return;
        }

        if (!await ResolveDirtyEditorAsync())
        {
            SynchronizeSelection();
            return;
        }
        await ViewModel.OpenCodeSourceAsync(source);
    }

    private async void OnRefreshSourcesClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || !await ResolveDirtyEditorAsync()) return;
        if (ViewModel.RefreshCodeCommand.CanExecute(null)) ViewModel.RefreshCodeCommand.Execute(null);
    }

    private async Task<bool> ResolveDirtyEditorAsync()
    {
        if (ViewModel is null || !ViewModel.IsCodeDirty) return true;
        var choice = MessageBox.Show(
            Window.GetWindow(this),
            "The current C# source has unsaved changes. Save before continuing?",
            "Unsaved C# source",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);
        if (choice == MessageBoxResult.Cancel) return false;
        if (choice == MessageBoxResult.Yes)
        {
            await ((RekallAgeAsyncCommand)ViewModel.SaveCodeCommand).ExecuteAsync(null);
            return !ViewModel.IsCodeDirty;
        }
        if (ViewModel.SelectedCodeSource is { } selected)
        {
            await ViewModel.OpenCodeSourceAsync(selected);
        }
        return !ViewModel.IsCodeDirty;
    }

    private void SynchronizeSelection()
    {
        _synchronizingSelection = true;
        try
        {
            CodeSourceList.SelectedItem = ViewModel?.SelectedCodeSource;
        }
        finally
        {
            _synchronizingSelection = false;
        }
    }
}
