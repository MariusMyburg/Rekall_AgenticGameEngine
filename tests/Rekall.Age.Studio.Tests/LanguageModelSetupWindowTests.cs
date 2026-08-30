using System.IO;
using System.Windows;
using Rekall.Age.Agent.LanguageModels;
using Rekall.Age.Studio;

namespace Rekall.Age.Studio.Tests;

[Collection(WpfApplicationTestCollection.Name)]
public sealed class LanguageModelSetupWindowTests(WpfApplicationTestFixture wpf)
{
    [Fact]
    public void XamlProvidesAccessibleFiveStepProviderSetupFlow()
    {
        var xaml = File.ReadAllText(SourcePath("src", "Rekall.Age.Studio", "LanguageModelSetupWindow.xaml"));

        Assert.Contains("x:Name=\"WelcomeStepPanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ProviderStepPanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ConfigurationStepPanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ModelStepPanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SummaryStepPanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("RekallAgeStudioLanguageModelSetupStep.Welcome", xaml, StringComparison.Ordinal);
        Assert.Contains("RekallAgeStudioLanguageModelSetupStep.Provider", xaml, StringComparison.Ordinal);
        Assert.Contains("RekallAgeStudioLanguageModelSetupStep.Configuration", xaml, StringComparison.Ordinal);
        Assert.Contains("RekallAgeStudioLanguageModelSetupStep.Model", xaml, StringComparison.Ordinal);
        Assert.Contains("RekallAgeStudioLanguageModelSetupStep.Summary", xaml, StringComparison.Ordinal);
        Assert.Contains("LOCAL", xaml, StringComparison.Ordinal);
        Assert.Contains("API", xaml, StringComparison.Ordinal);
        Assert.Contains("ACCOUNT", xaml, StringComparison.Ordinal);
        Assert.Contains("IsOllamaSelected", xaml, StringComparison.Ordinal);
        Assert.Contains("IsGgufSelected", xaml, StringComparison.Ordinal);
        Assert.Contains("IsOpenAiSelected", xaml, StringComparison.Ordinal);
        Assert.Contains("IsKimiSelected", xaml, StringComparison.Ordinal);
        Assert.Contains("IsCodexSelected", xaml, StringComparison.Ordinal);
        Assert.Contains("PasswordBox x:Name=\"OpenAiApiKeyInput\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PasswordBox x:Name=\"KimiApiKeyInput\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RememberOpenAiKey\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RememberKimiKey\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer", xaml, StringComparison.Ordinal);
        var windowOpeningTag = xaml[..xaml.IndexOf('>')];
        Assert.DoesNotContain(" Height=", windowOpeningTag, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Padding=\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsDefault=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsCancel=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("StatusGlyph", xaml, StringComparison.Ordinal);
        Assert.Contains("Label", xaml, StringComparison.Ordinal);
        Assert.Contains("Retry", xaml, StringComparison.Ordinal);
        Assert.Contains("Set up later", xaml, StringComparison.Ordinal);
        Assert.Contains("Finish", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceClearsUnboundPasswordsAndUsesExactGgufFilter()
    {
        var source = File.ReadAllText(SourcePath("src", "Rekall.Age.Studio", "LanguageModelSetupWindow.xaml.cs"));

        Assert.Contains("var key = OpenAiApiKeyInput.Password;", source, StringComparison.Ordinal);
        Assert.Contains("OpenAiApiKeyInput.Clear();", source, StringComparison.Ordinal);
        Assert.Contains("var key = KimiApiKeyInput.Password;", source, StringComparison.Ordinal);
        Assert.Contains("KimiApiKeyInput.Clear();", source, StringComparison.Ordinal);
        Assert.Contains("GGUF models (*.gguf)|*.gguf", source, StringComparison.Ordinal);
        Assert.DoesNotContain("string _openAi", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("string _kimi", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OwnedWindowShowsOnlySelectedProviderConfiguration()
    {
        wpf.Invoke(() =>
        {
            var owner = new Window();
            owner.Show();
            var viewModel = CreateViewModel();
            var window = new LanguageModelSetupWindow(owner, viewModel);

            viewModel.NextCommand.Execute(null);
            viewModel.NextCommand.Execute(null);

            Assert.Same(owner, window.Owner);
            Assert.Equal(Visibility.Visible, Panel(window, "OllamaConfigurationPanel").Visibility);
            Assert.Equal(Visibility.Collapsed, Panel(window, "OpenAiConfigurationPanel").Visibility);

            viewModel.SelectedProviderId = "openai";

            Assert.Equal(Visibility.Collapsed, Panel(window, "OllamaConfigurationPanel").Visibility);
            Assert.Equal(Visibility.Visible, Panel(window, "OpenAiConfigurationPanel").Visibility);
            window.Close();
            owner.Close();
        });
    }

    [Fact]
    public void ClosingAnIncompleteWindowReturnsClosedIncompleteOutcome()
    {
        var outcome = wpf.Invoke(() =>
        {
            var owner = new Window();
            owner.Show();
            var window = new LanguageModelSetupWindow(owner, CreateViewModel());

            window.Close();

            var result = window.Outcome;
            owner.Close();
            return result;
        });

        Assert.Equal(RekallAgeStudioLanguageModelSetupWindowOutcome.ClosedIncomplete, outcome);
    }

    private static FrameworkElement Panel(FrameworkElement root, string name) =>
        Assert.IsAssignableFrom<FrameworkElement>(root.FindName(name));

    private static RekallAgeStudioLanguageModelSetupViewModel CreateViewModel() => new(
        new TestSetupStore(),
        new TestCredentialStore(),
        new TestReadinessProbe());

    private static string SourcePath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Unable to locate the Studio source file.");
    }

    private sealed class TestSetupStore : IRekallAgeStudioLanguageModelSetupStore
    {
        public ValueTask<RekallAgeStudioLanguageModelSetup> LoadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(RekallAgeStudioLanguageModelSetup.Incomplete);

        public ValueTask SaveAsync(RekallAgeStudioLanguageModelSetup setup, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class TestCredentialStore : IRekallAgeStudioCredentialStore
    {
        public ValueTask<string?> ReadAsync(string providerId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(null);

        public ValueTask WriteAsync(string providerId, string credential, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask RemoveAsync(string providerId, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class TestReadinessProbe : IRekallAgeLanguageModelReadinessProbe
    {
        public ValueTask<RekallAgeLanguageModelReadinessResult> ProbeAsync(
            RekallAgeLanguageModelReadinessRequest request,
            CancellationToken cancellationToken) => ValueTask.FromResult(new RekallAgeLanguageModelReadinessResult(
                request.ProviderId,
                RekallAgeLanguageModelReadinessState.Blocked,
                "REKALL_ONBOARDING_NOT_CHECKED",
                "Provider has not been checked.",
                [],
                [],
                "retry",
                true));
    }
}
