using Rekall.Age.Rendering;

namespace Rekall.Age.Tests.Rendering;

public sealed class RgbaMipChainBuilderTests
{
    [Fact]
    public void BuildCreatesCompleteBoxFilteredMipChain()
    {
        byte[] rgba =
        [
            0, 0, 0, 255, 40, 0, 0, 255, 80, 0, 0, 255, 120, 0, 0, 255,
            0, 40, 0, 255, 40, 40, 0, 255, 80, 40, 0, 255, 120, 40, 0, 255,
            0, 80, 0, 255, 40, 80, 0, 255, 80, 80, 0, 255, 120, 80, 0, 255,
            0, 120, 0, 255, 40, 120, 0, 255, 80, 120, 0, 255, 120, 120, 0, 255
        ];

        var levels = RekallAgeRgbaMipChainBuilder.Build(4, 4, rgba);

        Assert.Equal(3, levels.Count);
        Assert.Equal((0, 4, 4), (levels[0].Level, levels[0].Width, levels[0].Height));
        Assert.Equal((1, 2, 2), (levels[1].Level, levels[1].Width, levels[1].Height));
        Assert.Equal((2, 1, 1), (levels[2].Level, levels[2].Width, levels[2].Height));
        Assert.Equal(new byte[] { 60, 60, 0, 255 }, levels[2].Bytes);
    }
}
