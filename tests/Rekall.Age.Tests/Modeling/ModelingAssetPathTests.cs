using Rekall.Age.Modeling;

namespace Rekall.Age.Tests.Modeling;

public sealed class ModelingAssetPathTests
{
    [Fact]
    public void StoresCanonicalizeRelativeProjectRootsBeforePublishingResourcePaths()
    {
        var relativeRoot = Path.Combine(".test-artifacts", $"modeling-paths-{Guid.NewGuid():N}");
        var projectRoot = Path.GetFullPath(relativeRoot);

        var paths = new[]
        {
            new RekallAgeMeshAssetStore().GetMeshPath(relativeRoot, "mesh"),
            new RekallAgeModelingGraphAssetStore().GetGraphPath(relativeRoot, "model"),
            new RekallAgeMaterialGraphAssetStore().GetGraphPath(relativeRoot, "material"),
            new RekallAgeMaterialInstanceAssetStore().GetInstancePath(relativeRoot, "instance"),
            new RekallAgeModifierStackAssetStore().GetStackPath(relativeRoot, "stack")
        };

        Assert.All(paths, path => Assert.True(Path.IsPathFullyQualified(path), path));
        Assert.All(paths, path => Assert.StartsWith(Path.GetFullPath(projectRoot), path, StringComparison.OrdinalIgnoreCase));
    }
}
