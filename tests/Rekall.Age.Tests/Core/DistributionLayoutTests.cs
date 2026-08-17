using Rekall.Age.Core.Product;

namespace Rekall.Age.Tests.Core;

public sealed class DistributionLayoutTests
{
    [Fact]
    public void LocatorFindsDistributionFromNestedToolDirectory()
    {
        var root = TestPaths.CreateTempDirectory();
        var cli = Path.Combine(root, "tools", "cli");
        Directory.CreateDirectory(cli);
        File.WriteAllText(Path.Combine(root, "rekall.distribution.json"), "{}");

        Assert.True(RekallAgeDistributionLayout.TryFind(cli, out var paths));
        Assert.Equal(root, paths.Root);
        Assert.Equal(Path.Combine(root, "players", "windows"), paths.WindowsPlayerPayload);
        Assert.Equal(Path.Combine(root, "sdk", "1"), paths.ModuleSdk);
    }

    [Fact]
    public void LocatorDoesNotSearchSiblingRepositoryPaths()
    {
        var parent = TestPaths.CreateTempDirectory();
        var distribution = Path.Combine(parent, "distribution");
        var unrelated = Path.Combine(parent, "unrelated", "tools", "cli");
        Directory.CreateDirectory(distribution);
        Directory.CreateDirectory(unrelated);
        File.WriteAllText(Path.Combine(distribution, "rekall.distribution.json"), "{}");

        Assert.False(RekallAgeDistributionLayout.TryFind(unrelated, out _));
    }
}
