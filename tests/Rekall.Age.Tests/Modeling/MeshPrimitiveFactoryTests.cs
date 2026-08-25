using Rekall.Age.Modeling;

namespace Rekall.Age.Tests.Modeling;

public sealed class MeshPrimitiveFactoryTests
{
    [Theory]
    [InlineData("box")]
    [InlineData("plane")]
    [InlineData("grid")]
    [InlineData("disc")]
    [InlineData("sphere")]
    [InlineData("ico-sphere")]
    [InlineData("capsule")]
    [InlineData("cylinder")]
    [InlineData("cone")]
    [InlineData("torus")]
    public async Task FactoryCreatesEveryAdvertisedEditablePrimitiveWithCanonicalIdentity(string primitive)
    {
        var mesh = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
            primitive,
            $"mesh-{primitive}",
            $"New {primitive}",
            CancellationToken.None);

        Assert.Equal($"mesh-{primitive}", mesh.AssetId);
        Assert.Equal($"New {primitive}", mesh.Name);
        Assert.NotEmpty(mesh.Topology.PointIds);
        Assert.NotEmpty(mesh.Topology.FaceIds);
        Assert.True(new RekallAgeMeshValidator().Validate(mesh).IsValid);
    }
}
