using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules.Commands;
using Rekall.Age.Modules.Security;
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
    public async Task InstallSdkCommandExplicitlyRepairsAStaleProjectLocalSdk()
    {
        var root = TestPaths.CreateTempDirectory();
        var installed = await new RekallAgeModuleSdkInstaller().InstallAsync(root, CancellationToken.None);
        await File.AppendAllTextAsync(installed.PropsPath, "<!-- stale -->");
        Assert.False(new RekallAgeModuleSdkIntegrityVerifier().Verify(root).Ready);

        var result = await new InstallModuleSdkCommand().ExecuteAsync(
            new InstallModuleSdkRequest(root),
            new RekallAgeCommandContext(
                "agent",
                RekallAgeTransaction.Begin("refresh module sdk"),
                CancellationToken.None));

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(installed.SdkRoot, result.Value.SdkRoot);
        Assert.True(new RekallAgeModuleSdkIntegrityVerifier().Verify(root).Ready);
    }

    [Fact]
    public async Task InstallSdkCommandRejectsAMissingProjectRootWithoutCreatingIt()
    {
        var root = Path.Combine(TestPaths.CreateTempDirectory(), "missing");

        var result = await new InstallModuleSdkCommand().ExecuteAsync(
            new InstallModuleSdkRequest(root),
            new RekallAgeCommandContext(
                "agent",
                RekallAgeTransaction.Begin("missing module sdk"),
                CancellationToken.None));

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_PROJECT_NOT_FOUND");
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task InstallerDoesNotWriteThroughAProjectSdkReparsePoint()
    {
        var root = TestPaths.CreateTempDirectory();
        var outside = TestPaths.CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, ".rekall"));
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(root, ".rekall", "sdk"), outside);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        var installError = await Assert.ThrowsAsync<IOException>(() =>
            new RekallAgeModuleSdkInstaller().InstallAsync(root, CancellationToken.None).AsTask());

        Assert.Contains("reparse point", installError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
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
