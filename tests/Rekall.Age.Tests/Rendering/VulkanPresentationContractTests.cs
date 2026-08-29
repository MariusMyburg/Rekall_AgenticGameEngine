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
        using var external = RekallAgeWin32RenderSurface.CreateExternal(new IntPtr(7));

        Assert.Throws<ArgumentOutOfRangeException>(() => external.Describe(width, height));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RekallAgeVulkanPresentationFrame.Presented(ViewportFrame(width, height)));
    }

    [Fact]
    public void OwnedSurfaceCanDescribeMultipleSizesWithoutDuplicatingOwnership()
    {
        var destroyCalls = 0;
        using var surface = RekallAgeWin32RenderSurface.CreateOwned(
            new IntPtr(17),
            _ =>
            {
                destroyCalls++;
                return true;
            });

        var first = surface.Describe(320, 180);
        var second = surface.Describe(640, 360);

        Assert.Equal(new IntPtr(17), first.Hwnd);
        Assert.Equal(320, first.Width);
        Assert.Equal(180, first.Height);
        Assert.Equal(new IntPtr(17), second.Hwnd);
        Assert.Equal(640, second.Width);
        Assert.Equal(360, second.Height);
        Assert.DoesNotContain(typeof(RekallAgeWin32RenderSurfaceDescriptor).GetInterfaces(), type => type == typeof(IDisposable));

        surface.Dispose();

        Assert.Equal(1, destroyCalls);
    }

    [Fact]
    public async Task SessionContractOnlyReceivesNonOwningSurfaceDescriptors()
    {
        var destroyCalls = 0;
        await using var session = new RecordingPresentationSession();
        using var external = RekallAgeWin32RenderSurface.CreateExternal(
            new IntPtr(23),
            _ =>
            {
                destroyCalls++;
                return true;
            });

        var result = await session.PresentAsync(
            external.Describe(320, 180),
            ViewportFrame(320, 180),
            RekallAgeRuntimeViewportAssetSet.Empty,
            CancellationToken.None);

        Assert.True(result.PresentedFrame);
        Assert.Equal(new IntPtr(23), session.LastSurface.Hwnd);
        Assert.Equal(320, session.LastSurface.Width);
        Assert.Equal(180, session.LastSurface.Height);

        await session.DisposeAsync();

        Assert.Equal(0, destroyCalls);
    }

    [Fact]
    public void PresentedFrameUsesInvariantVulkanHardwareTelemetry()
    {
        var frame = RekallAgeVulkanPresentationFrame.Presented(
            ViewportFrame(320, 180),
            selectedDeviceName: "Test GPU");

        Assert.True(frame.PresentedFrame);
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
        Assert.Null(frame.FailureReason);
        Assert.Empty(frame.Errors);
    }

    [Fact]
    public void UnavailableFrameReportsExplicitFailureState()
    {
        var frame = RekallAgeVulkanPresentationFrame.Unavailable(
            ViewportFrame(320, 180),
            "Swapchain creation failed.",
            ["VK_ERROR_INITIALIZATION_FAILED"]);

        Assert.False(frame.PresentedFrame);
        Assert.Equal("vulkan", frame.BackendId);
        Assert.False(frame.HardwareAccelerated);
        Assert.Equal("unavailable", frame.AccelerationStatus);
        Assert.Equal("Swapchain creation failed.", frame.FailureReason);
        Assert.Equal(["VK_ERROR_INITIALIZATION_FAILED"], frame.Errors);
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
        public RekallAgeWin32RenderSurfaceDescriptor LastSurface { get; private set; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public ValueTask<RekallAgeVulkanPresentationFrame> PresentAsync(
            RekallAgeWin32RenderSurfaceDescriptor surface,
            RekallAgeRuntimeViewportFrame frame,
            RekallAgeRuntimeViewportAssetSet assets,
            CancellationToken cancellationToken)
        {
            LastSurface = surface;
            return ValueTask.FromResult(RekallAgeVulkanPresentationFrame.Presented(frame));
        }
    }
}
