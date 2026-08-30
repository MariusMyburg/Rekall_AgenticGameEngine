using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Rekall.Age.Studio;

internal enum RekallAgeStudioLanguageModelSetupWindowOutcome
{
    Completed,
    Deferred,
    ClosedIncomplete
}

public partial class LanguageModelSetupWindow : Window
{
    private bool _allowClose;
    private bool _closing;
    private bool _deferRequested;

    internal LanguageModelSetupWindow(Window owner, RekallAgeStudioLanguageModelSetupViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        Owner = owner;
        DataContext = ViewModel;
        InitializeComponent();
    }

    internal RekallAgeStudioLanguageModelSetupViewModel ViewModel { get; }

    internal RekallAgeStudioLanguageModelSetupWindowOutcome Outcome { get; private set; } =
        RekallAgeStudioLanguageModelSetupWindowOutcome.ClosedIncomplete;

    private void OnProviderCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string providerId }) ViewModel.SelectedProviderId = providerId;
    }

    private async void OnApplyOpenAiKeyClick(object sender, RoutedEventArgs e)
    {
        var key = OpenAiApiKeyInput.Password;
        OpenAiApiKeyInput.Clear();
        if (string.IsNullOrWhiteSpace(key)) return;
        await ViewModel.ApplyApiKeyAsync("openai", key, RememberOpenAiKey.IsChecked == true);
    }

    private async void OnApplyKimiKeyClick(object sender, RoutedEventArgs e)
    {
        var key = KimiApiKeyInput.Password;
        KimiApiKeyInput.Clear();
        if (string.IsNullOrWhiteSpace(key)) return;
        await ViewModel.ApplyApiKeyAsync("kimi", key, RememberKimiKey.IsChecked == true);
    }

    private void OnBrowseGgufClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a local GGUF model",
            Filter = "GGUF models (*.gguf)|*.gguf",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            GgufSelectionStatus.Text = $"Selected: {dialog.FileName}";
        }
    }

    private void OnOpenProviderPageClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string url }) return;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private async void OnFinishClick(object sender, RoutedEventArgs e)
    {
        await ((RekallAgeAsyncCommand)ViewModel.FinishCommand).ExecuteAsync(null);
        if (ViewModel.CompletedSetup is not { IsComplete: true }) return;
        Outcome = RekallAgeStudioLanguageModelSetupWindowOutcome.Completed;
        _allowClose = true;
        DialogResult = true;
    }

    private void OnSetUpLaterClick(object sender, RoutedEventArgs e) => _deferRequested = true;

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        try
        {
            await ((RekallAgeAsyncCommand)ViewModel.SetUpLaterCommand).ExecuteAsync(null);
            if (ViewModel.CompletedSetup is { IsComplete: false })
            {
                Outcome = _deferRequested
                    ? RekallAgeStudioLanguageModelSetupWindowOutcome.Deferred
                    : RekallAgeStudioLanguageModelSetupWindowOutcome.ClosedIncomplete;
            }
            await ViewModel.DisposeAsync();
        }
        finally
        {
            _allowClose = true;
            Close();
        }
    }
}
