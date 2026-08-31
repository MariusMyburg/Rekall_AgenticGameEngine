using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Rekall.Age.Studio;

public partial class AuthorWorkspace : UserControl
{
    private readonly IRekallAgeStudioGgufFilePicker _ggufFilePicker;
    internal Func<CancellationToken, Task>? FixSetupRequested { get; set; }

    public AuthorWorkspace() : this(null)
    {
    }

    internal AuthorWorkspace(
        IRekallAgeStudioGgufFilePicker? ggufFilePicker)
    {
        _ggufFilePicker = ggufFilePicker ?? SystemAuthorGgufFilePicker.Instance;
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
        if (!viewModel.CanBrowseGguf) return;
        var path = _ggufFilePicker.Pick(Window.GetWindow(this), "Import a local GGUF model");
        if (path is not null) await viewModel.ImportGgufModelAsync(path);
    }

    private sealed class SystemAuthorGgufFilePicker : IRekallAgeStudioGgufFilePicker
    {
        public static SystemAuthorGgufFilePicker Instance { get; } = new();
        public string? Pick(Window? owner, string title)
        {
            var dialog = new OpenFileDialog
            {
                Title = title,
                Filter = "GGUF models (*.gguf)|*.gguf",
                CheckFileExists = true,
                Multiselect = false
            };
            return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
        }
    }
}
