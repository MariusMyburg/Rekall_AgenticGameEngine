using System.IO;

namespace Rekall.Age.Studio.Tests;

public sealed class LanguageModelWorkspaceSourceTests
{
    [Fact]
    public void AuthorWorkspaceKeepsEveryCredentialOrImportControlProviderConditional()
    {
        var root = FindRepositoryRoot();
        var studio = Path.Combine(root, "src", "Rekall.Age.Studio");
        var xaml = File.ReadAllText(Path.Combine(studio, "AuthorWorkspace.xaml"));
        var code = File.ReadAllText(Path.Combine(studio, "AuthorWorkspace.xaml.cs"));

        Assert.Contains("IsKimiSelected", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"KimiApiKeyInput\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnApplyKimiApiKeyClick\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsGgufSelected", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnImportGgufClick\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Filter = \"GGUF models (*.gguf)|*.gguf\"", code, StringComparison.Ordinal);
        Assert.Contains("ApplyKimiApiKeyAsync", code, StringComparison.Ordinal);
        Assert.Contains("ImportGgufModelAsync", code, StringComparison.Ordinal);
        Assert.Contains("IsCodexSelected", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Sign in to Codex\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthorFixSetupUsesTheSharedCoordinatorAndKeepsProviderPanelsExclusive()
    {
        var root = FindRepositoryRoot();
        var studio = Path.Combine(root, "src", "Rekall.Age.Studio");
        var xaml = File.ReadAllText(Path.Combine(studio, "AuthorWorkspace.xaml"));
        var code = File.ReadAllText(Path.Combine(studio, "AuthorWorkspace.xaml.cs"));
        var main = File.ReadAllText(Path.Combine(studio, "MainWindow.xaml.cs"));
        var viewModel = File.ReadAllText(Path.Combine(studio, "RekallAgeStudioViewModel.cs"));

        Assert.Contains("Content=\"Fix setup\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FixSetupRequested", code, StringComparison.Ordinal);
        Assert.Contains("AuthorWorkspaceHost.FixSetupRequested", main, StringComparison.Ordinal);
        Assert.Contains("_languageModelSetupCoordinator.ShowSetupAsync", main, StringComparison.Ordinal);
        Assert.Contains("if (!CanBrowseGguf)", viewModel, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding CanBrowseGguf}\"", xaml, StringComparison.Ordinal);
        Assert.Equal(5, Count(xaml, "<DataTrigger Binding=\"{Binding Is"));
        Assert.DoesNotContain("Visibility=\"Visible\" Content=\"Sign in to Codex\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void CredentialLabelsDescribeSourcesWithoutBindingOrRenderingKeyValues()
    {
        var root = FindRepositoryRoot();
        var studio = Path.Combine(root, "src", "Rekall.Age.Studio");
        var xaml = File.ReadAllText(Path.Combine(studio, "AuthorWorkspace.xaml"));
        var viewModel = File.ReadAllText(Path.Combine(studio, "RekallAgeStudioViewModel.cs"));

        Assert.Contains("OpenAiCredentialSourceLabel", xaml, StringComparison.Ordinal);
        Assert.Contains("KimiCredentialSourceLabel", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenAiApiKey}", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("KimiApiKey}", xaml, StringComparison.Ordinal);
        Assert.Contains("_sessionOpenAiApiKey = null;", viewModel, StringComparison.Ordinal);
        Assert.Contains("_sessionKimiApiKey = null;", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void SetupWindowBindsExplicitOllamaRemediationAndProgressCommands()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", "LanguageModelSetupWindow.xaml"));

        Assert.Contains("Tag=\"https://ollama.com/download\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnOpenProviderPageClick\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding StartOllamaCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding PullRecommendedOllamaModelCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding RemediationProgress}\"", xaml, StringComparison.Ordinal);
    }

    private static int Count(string source, string value) =>
        (source.Length - source.Replace(value, string.Empty, StringComparison.Ordinal).Length) / value.Length;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Rekall.Age.Studio"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
