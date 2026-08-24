using System.Text.Json;
using Rekall.Age.Core.Product;

namespace Rekall.Age.Tests.Core;

public sealed class PackagedLaunchResolverTests
{
    [Fact]
    public void ExplicitArgumentsRemainUnchanged()
    {
        var supplied = new[] { @"C:\Games\Authored", "Arena", "--vr" };

        var resolved = RekallAgePackagedLaunchResolver.Resolve(@"C:\Missing\Play.exe", supplied);

        Assert.Equal(supplied, resolved);
    }

    [Fact]
    public async Task NoArgumentsResolveAdjacentManifestFromRelocatedPathWithSpaces()
    {
        var packageRoot = Path.Combine(TestPaths.CreateTempDirectory(), "Relocated Game Package");
        Directory.CreateDirectory(Path.Combine(packageRoot, "Game"));
        var executable = Path.Combine(packageRoot, "Play.exe");
        await WriteManifestAsync(packageRoot, "Game", "Main", ["Game", "Main", "--graphics", "--backend", "vulkan"]);

        var resolved = RekallAgePackagedLaunchResolver.Resolve(executable, []);

        Assert.Equal(Path.Combine(packageRoot, "Game"), resolved[0]);
        Assert.Equal(["Main", "--graphics", "--backend", "vulkan"], resolved[1..]);
    }

    [Fact]
    public void NoArgumentsRequireAdjacentManifest()
    {
        var executable = Path.Combine(TestPaths.CreateTempDirectory(), "Play.exe");

        var exception = Assert.Throws<InvalidDataException>(
            () => RekallAgePackagedLaunchResolver.Resolve(executable, []));

        Assert.Contains("rekall.package.json", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("wrong.package.kind", "Game")]
    [InlineData("rekall.age.playable.package", "../Outside")]
    public async Task InvalidOrEscapingManifestIsRejected(string kind, string gameRoot)
    {
        var packageRoot = TestPaths.CreateTempDirectory();
        var executable = Path.Combine(packageRoot, "Play.exe");
        await WriteManifestAsync(packageRoot, gameRoot, "Main", [gameRoot, "Main", "--graphics"], kind);

        Assert.Throws<InvalidDataException>(
            () => RekallAgePackagedLaunchResolver.Resolve(executable, []));
    }

    [Fact]
    public async Task RootedManifestGameRootIsRejected()
    {
        var packageRoot = TestPaths.CreateTempDirectory();
        var executable = Path.Combine(packageRoot, "Play.exe");
        var rootedGame = Path.Combine(packageRoot, "Game");
        await WriteManifestAsync(packageRoot, rootedGame, "Main", [rootedGame, "Main", "--graphics"]);

        Assert.Throws<InvalidDataException>(
            () => RekallAgePackagedLaunchResolver.Resolve(executable, []));
    }

    private static async Task WriteManifestAsync(
        string packageRoot,
        string gameRoot,
        string sceneName,
        IReadOnlyList<string> arguments,
        string kind = "rekall.age.playable.package")
    {
        var manifest = new { kind, gameRoot, sceneName, arguments };
        await File.WriteAllTextAsync(
            Path.Combine(packageRoot, "rekall.package.json"),
            JsonSerializer.Serialize(manifest));
    }
}
