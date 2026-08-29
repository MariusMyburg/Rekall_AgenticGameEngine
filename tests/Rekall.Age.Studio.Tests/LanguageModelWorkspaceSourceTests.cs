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
