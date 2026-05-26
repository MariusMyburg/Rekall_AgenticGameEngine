using Rekall.Age.Core.Rendering;

namespace Rekall.Age.Tests.Core;

public sealed class RenderLayerMaskTests
{
    [Theory]
    [InlineData(null, "world", true)]
    [InlineData("", "world", true)]
    [InlineData("*", "world", true)]
    [InlineData("all", "world", true)]
    [InlineData("world, helpers", "helpers", true)]
    [InlineData("world;helpers|ui", "ui", true)]
    [InlineData("world helpers", "editor", false)]
    [InlineData("*, !helpers", "world", true)]
    [InlineData("*, !helpers", "helpers", false)]
    [InlineData("all -debug", "debug", false)]
    [InlineData("world, !world", "world", false)]
    public void CameraMaskMatchesLayerUsingSharedSeparators(string? mask, string layer, bool expected)
    {
        Assert.Equal(expected, RekallAgeRenderLayerMask.IncludesLayer(layer, mask));
    }

    [Fact]
    public void IncludedLayersIgnoresWildcardsAndNormalizesNames()
    {
        Assert.Equal(
            ["helpers", "world"],
            RekallAgeRenderLayerMask.EnumerateIncludedLayers(" World, helpers, all, *, !debug "));
    }

    [Fact]
    public void ExcludedLayersNormalizesNegatedNames()
    {
        Assert.Equal(
            ["debug", "helpers"],
            RekallAgeRenderLayerMask.EnumerateExcludedLayers("*, !helpers, -Debug"));
    }

    [Theory]
    [InlineData(null, "default")]
    [InlineData("", "default")]
    [InlineData(" World ", "world")]
    public void NormalizeLayerDefaultsAndLowercases(string? layer, string expected)
    {
        Assert.Equal(expected, RekallAgeRenderLayerMask.NormalizeLayer(layer));
    }
}
