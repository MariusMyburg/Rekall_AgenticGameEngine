using Rekall.Age.Rendering.Windows;

namespace Rekall.Age.Tests.Rendering;

public sealed class VeldridSceneCoverageBlendTests
{
    [Fact]
    public void SceneCoverageUsesSourceOverAlphaForBuiltInAndProjectShaders()
    {
        var blend = RekallAgeVeldridBlendStates.DescribeSceneCoverage();

        Assert.Equal("SourceAlpha", blend.SourceColor);
        Assert.Equal("InverseSourceAlpha", blend.DestinationColor);
        Assert.Equal("One", blend.SourceAlpha);
        Assert.Equal("InverseSourceAlpha", blend.DestinationAlpha);
    }
}
