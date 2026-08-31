using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Rekall.Age.Studio;

internal interface IRekallAgeStudioProviderPageLauncher
{
    void Open(Uri uri);
}

internal interface IRekallAgeStudioGgufFilePicker
{
    string? Pick(Window? owner, string title);
}

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
    private readonly IRekallAgeStudioProviderPageLauncher _providerPageLauncher;
    private readonly IRekallAgeStudioGgufFilePicker _ggufFilePicker;

    internal LanguageModelSetupWindow(
        Window owner,
        RekallAgeStudioLanguageModelSetupViewModel viewModel,
        IRekallAgeStudioProviderPageLauncher? providerPageLauncher = null,
        IRekallAgeStudioGgufFilePicker? ggufFilePicker = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _providerPageLauncher = providerPageLauncher ?? SystemProviderPageLauncher.Instance;
        _ggufFilePicker = ggufFilePicker ?? SystemGgufFilePicker.Instance;
        Owner = owner;
        DataContext = ViewModel;
        InitializeComponent();
    }

    internal RekallAgeStudioLanguageModelSetupViewModel ViewModel { get; }

    internal RekallAgeStudioLanguageModelSetupWindowOutcome Outcome { get; private set; } =
        RekallAgeStudioLanguageModelSetupWindowOutcome.ClosedIncomplete;

    internal void CloseForCancellation()
    {
        _allowClose = true;
        if (IsVisible) Close();
    }

    private void OnProviderCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string providerId }) ViewModel.SelectedProviderId = providerId;
    }

    private async void OnApplyOpenAiKeyClick(object sender, RoutedEventArgs e)
    {
        var key = OpenAiApiKeyInput.Password;
        OpenAiApiKeyInput.Clear();
        if (string.IsNullOrWhiteSpace(key)) return;
        try
        {
            await ViewModel.ApplyApiKeyAsync("openai", key, RememberOpenAiKey.IsChecked == true);
        }
        catch (OperationCanceledException) when (_closing)
        {
        }
        catch (Exception)
        {
            ShowUiError("The API key could not be applied. Check the selected provider and try again.");
        }
    }

    private async void OnApplyKimiKeyClick(object sender, RoutedEventArgs e)
    {
        var key = KimiApiKeyInput.Password;
        KimiApiKeyInput.Clear();
        if (string.IsNullOrWhiteSpace(key)) return;
        try
        {
            await ViewModel.ApplyApiKeyAsync("kimi", key, RememberKimiKey.IsChecked == true);
        }
        catch (OperationCanceledException) when (_closing)
        {
        }
        catch (Exception)
        {
            ShowUiError("The API key could not be applied. Check the selected provider and try again.");
        }
    }

    private void OnBrowseGgufClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanBrowseGguf) return;
        var path = _ggufFilePicker.Pick(this, "Choose a local GGUF model");
        if (path is not null) GgufSelectionStatus.Text = $"Selected: {path}";
    }

    private sealed class SystemGgufFilePicker : IRekallAgeStudioGgufFilePicker
    {
        public static SystemGgufFilePicker Instance { get; } = new();
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

    private void OnOpenProviderPageClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string url }) return;
        try
        {
            _providerPageLauncher.Open(new Uri(url, UriKind.Absolute));
        }
        catch (Exception)
        {
            ShowUiError("The provider page could not be opened. Check your browser and try again.");
        }
    }

    private async void OnFinishClick(object sender, RoutedEventArgs e)
    {
        await ((RekallAgeAsyncCommand)ViewModel.FinishCommand).ExecuteAsync(null);
        if (ViewModel.CompletedSetup is not { IsComplete: true }) return;
        Outcome = RekallAgeStudioLanguageModelSetupWindowOutcome.Completed;
        _allowClose = true;
        DialogResult = true;
    }

    private void OnSetUpLaterClick(object sender, RoutedEventArgs e)
    {
        _deferRequested = true;
        if (!_closing) _ = Dispatcher.BeginInvoke(Close);
    }

    private void ShowUiError(string message)
    {
        UiErrorText.Text = message;
        UiErrorText.Visibility = Visibility.Visible;
    }

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
            _ = Dispatcher.BeginInvoke(Close);
        }
    }

    private sealed class SystemProviderPageLauncher : IRekallAgeStudioProviderPageLauncher
    {
        public static SystemProviderPageLauncher Instance { get; } = new();

        public void Open(Uri uri) => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}
