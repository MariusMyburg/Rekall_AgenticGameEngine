using System.Text.Json;
using System.IO;
using Rekall.Age.Studio;
using Rekall.Age.Workflows.Commands;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioPackagingTests
{
    [Fact]
    public async Task StudioDefaultsToWindowsDeliveryAndBuildsExplicitTargetRequest()
    {
        await using var viewModel = new RekallAgeStudioViewModel();

        Assert.Equal(RekallAgePlayablePackageTargets.Windows, viewModel.SelectedPackageTarget);
        Assert.Equal(
            [RekallAgePlayablePackageTargets.Windows, RekallAgePlayablePackageTargets.Headless],
            viewModel.PackageTargets);

        using var request = JsonDocument.Parse(viewModel.CreatePackageRequestJson("C:\\Game", "Main"));
        Assert.Equal("windows", request.RootElement.GetProperty("target").GetString());
        Assert.False(request.RootElement.TryGetProperty("graphics", out _));
    }

    [Fact]
    public async Task SuccessfulPackageExposesArtifactsAndOpensExactOutputDirectory()
    {
        var output = Path.Combine(Path.GetTempPath(), $"rekall-studio-package-{Guid.NewGuid():N}");
        Directory.CreateDirectory(output);
        string? opened = null;
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel(openPackageFolder: path => opened = path);
            var result = new PackagePlayableGameResult(
                true,
                output,
                Path.Combine(output, "Play.exe"),
                Path.Combine(output, "rekall.package.json"),
                output + ".zip",
                [],
                [],
                string.Empty)
            {
                Target = RekallAgePlayablePackageTargets.Windows
            };

            viewModel.ApplyPackageResult(result);

            Assert.Equal(output, viewModel.LastPackageOutputDirectory);
            Assert.Equal(Path.Combine(output, "Play.exe"), viewModel.LastPackageLaunchPath);
            Assert.Equal(output + ".zip", viewModel.LastPackagePath);
            Assert.True(viewModel.OpenPackageFolderCommand.CanExecute(null));

            viewModel.OpenPackageFolderCommand.Execute(null);
            Assert.Equal(output, opened);
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public async Task DeliveryUiExposesTargetArtifactsAndFolderAction()
    {
        var root = FindRepositoryRoot();
        var xaml = await File.ReadAllTextAsync(Path.Combine(root, "src", "Rekall.Age.Studio", "MainWindow.xaml"));

        Assert.Contains("Header=\"Delivery\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding PackageTargets}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedPackageTarget}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding LastPackageLaunchPath, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding LastPackagePath, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding LastPackageOutputDirectory, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding OpenPackageFolderCommand}\"", xaml, StringComparison.Ordinal);
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

        throw new InvalidOperationException("Could not locate the Rekall AGE repository root.");
    }
}
