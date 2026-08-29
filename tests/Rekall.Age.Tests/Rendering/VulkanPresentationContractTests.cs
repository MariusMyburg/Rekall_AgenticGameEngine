using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.Windows;

namespace Rekall.Age.Tests.Rendering;

public sealed class VulkanPresentationContractTests
{
    [Theory]
    [InlineData(0, 180)]
    [InlineData(320, 0)]
    [InlineData(-1, 180)]
    [InlineData(320, -1)]
    public void ContractsRejectNonPositiveDimensions(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RekallAgeWin32RenderSurface.CreateExternal(new IntPtr(7), width, height));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RekallAgeVulkanPresentationFrame.FromViewportFrame(ViewportFrame(width, height)));
    }

    [Fact]
    public async Task SessionDisposalNeverDestroysCallerOwnedSurfaceHandle()
    {
        var destroyCalls = 0;
        var session = new RecordingPresentationSession();
        var surface = new RekallAgeWin32RenderSurface(
            new IntPtr(17),
            320,
            180,
            ownsHandle: false,
            destroyHandle: _ =>
            {
                destroyCalls++;
                return true;
            });

        await session.PresentAsync(surface, ViewportFrame(320, 180), RekallAgeRuntimeViewportAssetSet.Empty, CancellationToken.None);
        await session.DisposeAsync();

        Assert.Equal(0, destroyCalls);
    }

    [Fact]
    public void PresentationFrameDefaultsToVulkanHardwareTelemetry()
    {
        var frame = RekallAgeVulkanPresentationFrame.FromViewportFrame(
            ViewportFrame(320, 180),
            selectedDeviceName: "Test GPU");

        Assert.Equal("Main", frame.SceneName);
        Assert.Equal(12, frame.FrameIndex);
        Assert.Equal(320, frame.Width);
        Assert.Equal(180, frame.Height);
        Assert.Equal("vulkan", frame.BackendId);
        Assert.True(frame.HardwareAccelerated);
        Assert.Equal("hardware", frame.AccelerationStatus);
        Assert.Equal("Test GPU", frame.SelectedDeviceName);
        Assert.Equal(1, frame.RenderableCount);
        Assert.Equal(1, frame.ObservationCount);
    }

    private static RekallAgeRuntimeViewportFrame ViewportFrame(int width, int height) =>
        new(
            "Main",
            12,
            2.5,
            width,
            height,
            ActiveCamera: null,
            Cameras: Array.Empty<RekallAgeRuntimeViewportCamera>(),
            Renderables:
            [
                new RekallAgeRuntimeViewportRenderable(
                    "entity-1",
                    "Rover",
                    "mesh",
                    "asset_rover",
                    1,
                    2,
                    3,
                    100)
            ],
            UiLayerCount: 0,
            DebugOverlay: new RekallAgeRuntimeViewportOverlay(false, 0),
            Observations:
            [
                new RekallAgeRuntimeViewportObservation(
                    "runtime.test",
                    "info",
                    "rendering",
                    "Rover",
                    "Telemetry smoke check.")
            ]);

    private sealed class RecordingPresentationSession : IRekallAgeVulkanPresentationSession
    {
        private RekallAgeWin32RenderSurface? _surface;

        public ValueTask DisposeAsync()
        {
            _surface?.Dispose();
            _surface = null;
            return ValueTask.CompletedTask;
        }

        public ValueTask<RekallAgeVulkanPresentationFrame> PresentAsync(
            RekallAgeWin32RenderSurface surface,
            RekallAgeRuntimeViewportFrame frame,
            RekallAgeRuntimeViewportAssetSet assets,
            CancellationToken cancellationToken)
        {
            _surface = surface;
            return ValueTask.FromResult(RekallAgeVulkanPresentationFrame.FromViewportFrame(frame));
        }
    }
}
