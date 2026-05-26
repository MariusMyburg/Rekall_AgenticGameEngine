using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Tests.Rendering;

public sealed class ViewportCameraRectTests
{
    [Fact]
    public void FromFrameConvertsNormalizedActiveCameraViewportToPixels()
    {
        var camera = new RekallAgeRuntimeViewportCamera(
            "camera",
            "Camera",
            "Camera3D",
            true,
            RenderOrder: 10,
            ViewportX: 0.25,
            ViewportY: 0.1,
            ViewportWidth: 0.5,
            ViewportHeight: 0.75);
        var frame = new RekallAgeRuntimeViewportFrame(
            "Main",
            0,
            0,
            800,
            600,
            camera,
            [camera],
            [],
            0,
            new RekallAgeRuntimeViewportOverlay(false, 0),
            []);

        var rect = RekallAgeRuntimeViewportCameraRect.FromFrame(frame);

        Assert.Equal(200, rect.X);
        Assert.Equal(60, rect.Y);
        Assert.Equal(400, rect.Width);
        Assert.Equal(450, rect.Height);
    }

    [Fact]
    public void FromFrameClampsCameraViewportToFramebufferBounds()
    {
        var camera = new RekallAgeRuntimeViewportCamera(
            "camera",
            "Camera",
            "Camera3D",
            true,
            ViewportX: 0.9,
            ViewportY: 0.8,
            ViewportWidth: 0.5,
            ViewportHeight: 0.5);
        var frame = new RekallAgeRuntimeViewportFrame(
            "Main",
            0,
            0,
            100,
            50,
            camera,
            [camera],
            [],
            0,
            new RekallAgeRuntimeViewportOverlay(false, 0),
            []);

        var rect = RekallAgeRuntimeViewportCameraRect.FromFrame(frame);

        Assert.Equal(90, rect.X);
        Assert.Equal(40, rect.Y);
        Assert.Equal(10, rect.Width);
        Assert.Equal(10, rect.Height);
    }
}
