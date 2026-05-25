using Rekall.Age.Rendering;

namespace Rekall.Age.Tests.Rendering;

public sealed class PngReaderTests
{
    [Fact]
    public async Task PngReaderDecodesRgbaWrittenByPngWriter()
    {
        var root = TestPaths.CreateTempDirectory();
        var path = Path.Combine(root, "sprite.png");
        byte[] rgba =
        [
            255, 0, 0, 255,
            0, 255, 0, 255,
            0, 0, 255, 255,
            255, 255, 0, 128
        ];
        await RekallAgePngWriter.WriteRgbaAsync(path, 2, 2, rgba, CancellationToken.None);

        var image = await RekallAgePngReader.ReadRgbaAsync(path, CancellationToken.None);

        Assert.Equal(2, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(rgba, image.Rgba);
        Assert.Equal((byte)255, image.GetPixel(0, 0).R);
        Assert.Equal((byte)128, image.GetPixel(1, 1).A);
    }
}
