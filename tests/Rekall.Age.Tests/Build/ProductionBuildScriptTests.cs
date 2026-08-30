using System.IO;

namespace Rekall.Age.Tests.Build;

public sealed class ProductionBuildScriptTests
{
    [Fact]
    public async Task StudioPublishExcludesUnapprovedLocalExampleContent()
    {
        var root = FindRepositoryRoot();
        var project = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "Rekall.Age.Studio",
            "Rekall.Age.Studio.csproj"));

        Assert.Contains(@"Examples\Stride\**", project, StringComparison.Ordinal);
        Assert.Contains(@"Examples\Prowl\**", project, StringComparison.Ordinal);
        Assert.Contains("asset_menu-theme_e91ed2cf.mp3", project, StringComparison.Ordinal);
        Assert.Contains(@"Examples\GlbStationTest\**", project, StringComparison.Ordinal);
        Assert.Contains(@"Examples\**\Assets\audio\**", project, StringComparison.Ordinal);
        Assert.Contains(@"Examples\**\Assets\texture\**", project, StringComparison.Ordinal);
        Assert.Contains(@"Examples\**\Assets\Models\Compiled\**", project, StringComparison.Ordinal);
        Assert.Contains(@"Examples\**\Transactions\**", project, StringComparison.Ordinal);
        Assert.Contains(@"Examples\**\Artifacts\**", project, StringComparison.Ordinal);
        Assert.Contains(@"Examples\**\__pycache__\**", project, StringComparison.Ordinal);
        Assert.Contains("asset_little-redfish-lake-sunrise_58618c91.jpg", project, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DistributionIncludesPurchaserLicense()
    {
        var root = FindRepositoryRoot();
        var script = await File.ReadAllTextAsync(Path.Combine(root, "eng", "build.ps1"));

        Assert.Contains("END-USER-LICENSE-AGREEMENT.md", script, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "END-USER-LICENSE-AGREEMENT.md")));
    }

    [Fact]
    public async Task SolutionRestoreDoesNotOverrideProjectRuntimeIdentifiers()
    {
        var root = FindRepositoryRoot();
        var script = await File.ReadAllTextAsync(Path.Combine(root, "eng", "build.ps1"));

        Assert.Contains(
            "Invoke-Checked dotnet @('restore', $solution, '--locked-mode', '/nr:false')",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "'restore', $solution, '--locked-mode', '-r', $RuntimeIdentifier",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "'restore', $project, '--locked-mode', '-r', $RuntimeIdentifier",
            script,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rekall.AGE.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
