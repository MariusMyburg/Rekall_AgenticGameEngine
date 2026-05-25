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

    [Fact]
    public void PngWriterDoesNotRequirePumpingSynchronizationContext()
    {
        var root = TestPaths.CreateTempDirectory();
        var path = Path.Combine(root, "sync-context.png");
        byte[] rgba =
        [
            24, 32, 48, 255,
            180, 220, 255, 255,
            40, 80, 120, 255,
            255, 210, 80, 255
        ];
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());

            var task = RekallAgePngWriter.WriteRgbaAsync(path, 2, 2, rgba, CancellationToken.None).AsTask();

#pragma warning disable xUnit1031
            Assert.True(task.Wait(TimeSpan.FromSeconds(2)), "PNG writing should not depend on a UI message pump.");
#pragma warning restore xUnit1031
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        Assert.True(new FileInfo(path).Length > 0);
    }

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
        }
    }
}
