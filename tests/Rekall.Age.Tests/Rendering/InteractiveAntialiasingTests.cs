using Rekall.Age.Rendering;

namespace Rekall.Age.Tests.Rendering;

public sealed class InteractiveAntialiasingTests
{
    [Fact]
    public void InteractiveRenderingDefaultsToTwoByTwoSupersamplingAndHonorsExplicitOverrides()
    {
        Assert.Equal(2, RekallAgeInteractiveAntialiasing.ResolveSupersampleFactor(null));
        Assert.Equal(1, RekallAgeInteractiveAntialiasing.ResolveSupersampleFactor(1));
        Assert.Equal(4, RekallAgeInteractiveAntialiasing.ResolveSupersampleFactor(99));
    }

    [Fact]
    public void SupersampleResolveAveragesEverySourceSampleIntoTheOutputPixel()
    {
        byte[] source =
        [
            255, 0, 0, 255,    0, 255, 0, 255,
            0, 0, 255, 255,    255, 255, 255, 255
        ];

        var resolved = RekallAgeInteractiveAntialiasing.ResolveRgba(source, 2, 2, 2);

        Assert.Equal([128, 128, 128, 255], resolved);
    }
}
