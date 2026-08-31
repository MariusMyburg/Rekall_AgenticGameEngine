using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Rekall.Age.Studio;

public partial class AuthorWorkspace : UserControl
{
    internal Func<CancellationToken, Task>? FixSetupRequested { get; set; }

    public AuthorWorkspace()
    {
        InitializeComponent();
    }

    private async void OnFixSetupClick(object sender, RoutedEventArgs e)
    {
        if (FixSetupRequested is not null) await FixSetupRequested(CancellationToken.None);
    }

    private async void OnApplyOpenAiApiKeyClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RekallAgeStudioViewModel viewModel) return;
        var sessionKey = OpenAiApiKeyInput.Password;
        OpenAiApiKeyInput.Clear();
        await viewModel.ApplyOpenAiApiKeyAsync(sessionKey);
    }

    private async void OnApplyKimiApiKeyClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RekallAgeStudioViewModel viewModel) return;
        var sessionKey = KimiApiKeyInput.Password;
        KimiApiKeyInput.Clear();
        await viewModel.ApplyKimiApiKeyAsync(sessionKey);
    }

    private async void OnImportGgufClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RekallAgeStudioViewModel viewModel) return;
        var dialog = new OpenFileDialog
        {
            Title = "Import a local GGUF model",
            Filter = "GGUF models (*.gguf)|*.gguf",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        await viewModel.ImportGgufModelAsync(dialog.FileName);
    }
}
