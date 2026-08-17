using Rekall.Age.Modules.Sdk;

namespace Rekall.Age.Tests.Modules;

public sealed class ModuleSdkInstallerTests
{
    [Fact]
    public async Task InstallerCreatesVersionedProjectLocalSdkWithRelativeProps()
    {
        var root = TestPaths.CreateTempDirectory();

        var result = await new RekallAgeModuleSdkInstaller().InstallAsync(root, CancellationToken.None);

        Assert.Equal(1, result.CompatibilityVersion);
        Assert.Equal(Path.Combine(root, ".rekall", "sdk", "1"), result.SdkRoot);
        Assert.All(result.Assemblies, path => Assert.True(File.Exists(path), path));
        Assert.True(File.Exists(result.PropsPath));
        Assert.True(File.Exists(result.ManifestPath));
        var props = await File.ReadAllTextAsync(result.PropsPath);
        Assert.Contains("Rekall.Age.Modules.dll", props);
        Assert.DoesNotContain(Path.GetFullPath("."), props, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProjectFileImportsProjectLocalSdkWithoutAbsolutePaths()
    {
        var project = RekallAgeModuleProjectFile.Create("AgentModule");

        Assert.Contains("..\\..\\.rekall\\sdk\\1\\Rekall.Age.Sdk.props", project);
        Assert.DoesNotContain("ProjectReference", project);
        Assert.DoesNotContain(Path.GetPathRoot(Environment.CurrentDirectory)!, project);
    }
}
