using Rekall.Age.Build.Distribution;

namespace Rekall.Age.Tests.Build;

public sealed class DistributionAssemblerTests
{
    [Fact]
    public async Task AssemblerCreatesHashedPortableWindowsDistribution()
    {
        var fixture = CreateFixture();

        var result = await new RekallAgeDistributionAssembler().AssembleAsync(
            fixture.Request,
            CancellationToken.None);

        Assert.Equal("0.1.0-preview.1", result.Manifest.ProductVersion);
        Assert.Equal("win-x64", result.Manifest.RuntimeIdentifier);
        Assert.Equal("preview", result.Manifest.Channel);
        Assert.NotEmpty(result.Manifest.Files);
        Assert.All(result.Manifest.Files, file =>
        {
            Assert.False(Path.IsPathRooted(file.Path));
            Assert.DoesNotContain('\\', file.Path);
            Assert.Matches("^[0-9a-f]{64}$", file.Sha256);
        });
        Assert.True(File.Exists(Path.Combine(result.Root, "tools", "cli", "Rekall.Age.Cli.exe")));
        Assert.True(File.Exists(Path.Combine(result.Root, "players", "windows", "Rekall.Age.Player.Windows.exe")));
        Assert.True(File.Exists(Path.Combine(result.Root, "sdk", "1", "Rekall.Age.Modules.dll")));
        Assert.True(File.Exists(Path.Combine(result.Root, "END-USER-LICENSE-AGREEMENT.md")));
        Assert.True(File.Exists(result.ManifestPath));
        Assert.True(File.Exists(result.ArchivePath));
    }

    [Theory]
    [InlineData("secret.env")]
    [InlineData("cli.log")]
    [InlineData("test.trx")]
    public async Task AssemblerRejectsForbiddenFiles(string fileName)
    {
        var fixture = CreateFixture();
        await File.WriteAllTextAsync(Path.Combine(fixture.Request.CliPublishRoot, fileName), "secret");

        var error = await Assert.ThrowsAsync<RekallAgeDistributionAssemblyException>(async () =>
            await new RekallAgeDistributionAssembler().AssembleAsync(fixture.Request, CancellationToken.None));

        Assert.Equal("REKALL_DISTRIBUTION_FORBIDDEN_FILE", error.Code);
        Assert.Equal(fileName, Path.GetFileName(error.Target));
    }

    private static DistributionFixture CreateFixture()
    {
        var root = TestPaths.CreateTempDirectory();
        var inputs = Path.Combine(root, "inputs");
        var cli = CreatePayload(inputs, "cli", "Rekall.Age.Cli.exe");
        var studio = CreatePayload(inputs, "studio", "Rekall.Age.Studio.exe");
        var headless = CreatePayload(inputs, "headless", "Rekall.Age.Player.exe");
        var windows = CreatePayload(inputs, "windows", "Rekall.Age.Player.Windows.exe");
        var sdk = Path.Combine(inputs, "sdk");
        Directory.CreateDirectory(sdk);
        foreach (var assembly in new[]
        {
            "Rekall.Age.Core.dll",
            "Rekall.Age.World.dll",
            "Rekall.Age.Runtime.Abstractions.dll",
            "Rekall.Age.Modules.dll",
            "Rekall.Age.Sdk.props",
            "rekall.sdk.json"
        })
        {
            File.WriteAllText(Path.Combine(sdk, assembly), assembly);
        }

        var readme = WriteFile(inputs, "README.md", "# Rekall AGE");
        var eula = WriteFile(inputs, "END-USER-LICENSE-AGREEMENT.md", "Licensed use and runtime redistribution.");
        var notice = WriteFile(inputs, "PROPRIETARY-NOTICE.md", "All rights reserved.");
        var thirdParty = WriteFile(inputs, "THIRD-PARTY-NOTICES.txt", "Dependency notices");
        var output = Path.Combine(root, "output", "Rekall-AGE-0.1.0-preview.1-win-x64");
        return new DistributionFixture(new AssembleDistributionRequest(
            output,
            cli,
            studio,
            headless,
            windows,
            sdk,
            readme,
            eula,
            notice,
            thirdParty));
    }

    private static string CreatePayload(string root, string name, string executable)
    {
        var directory = Path.Combine(root, name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, executable), $"payload:{name}");
        File.WriteAllText(Path.Combine(directory, $"{name}.dll"), $"assembly:{name}");
        return directory;
    }

    private static string WriteFile(string root, string name, string content)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private sealed record DistributionFixture(AssembleDistributionRequest Request);
}
