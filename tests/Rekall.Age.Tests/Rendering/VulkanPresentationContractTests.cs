using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.Windows;
using Rekall.Age.Runtime.Abstractions;

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
    public void SceneSubmissionCopiesRuntimeWorkloadsAndCarriesRevisionFacts()
    {
        var workloads = new List<RekallAgeRuntimeGpuWorkload>
        {
            new("particles")
        };

        var submission = new RekallAgeVulkanSceneSubmission(
            ViewportFrame(320, 180),
            RekallAgeRuntimeViewportAssetSet.Empty,
            workloads,
            RuntimeEntityCount: 37,
            SceneRevision: 7,
            AssetRevision: 11,
            DebugBackendText: "OpenXR");
        workloads.Clear();

        Assert.Single(submission.RuntimeGpuWorkloads);
        Assert.Equal("particles", submission.RuntimeGpuWorkloads[0].Id);
        Assert.Equal(37, submission.RuntimeEntityCount);
        Assert.Equal(7, submission.SceneRevision);
        Assert.Equal(11, submission.AssetRevision);
        Assert.Equal("OpenXR", submission.DebugBackendText);
    }

    [Fact]
    public void LegacyPixelSubmissionCopiesPixelsWithoutWeakeningSceneSubmissionFacts()
    {
        var pixels = new byte[2 * 2 * 4];
        pixels[0] = 17;
        var scene = new RekallAgeVulkanSceneSubmission(
            ViewportFrame(320, 180),
            RekallAgeRuntimeViewportAssetSet.Empty,
            Array.Empty<RekallAgeRuntimeGpuWorkload>(),
            RuntimeEntityCount: 1,
            SceneRevision: 3,
            AssetRevision: 4);

        var submission = new RekallAgeVulkanPixelSubmission(2, 2, pixels, scene);
        pixels[0] = 99;

        Assert.Equal(17, submission.Rgba.Span[0]);
        Assert.Equal(3, submission.Scene.SceneRevision);
    }

    [Fact]
    public void SceneSubmissionRejectsNegativeRuntimeEntityTelemetry()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RekallAgeVulkanSceneSubmission(
            ViewportFrame(320, 180),
            RekallAgeRuntimeViewportAssetSet.Empty,
            Array.Empty<RekallAgeRuntimeGpuWorkload>(),
            RuntimeEntityCount: -1,
            SceneRevision: 1,
            AssetRevision: 1));
    }

    [Fact]
    public void NativeDeviceInfoContainsOnlyImmutableInteropFacts()
    {
        var info = new RekallAgeVulkanNativeDeviceInfo(
            "Test GPU",
            "Vulkan",
            1,
            2,
            3,
            4,
            5,
            "Driver",
            "1.2.3");

        Assert.Equal("Test GPU", info.DeviceName);
        Assert.Equal((ulong)1, info.Instance);
        Assert.Equal((uint)5, info.GraphicsQueueFamilyIndex);
        Assert.DoesNotContain(
            typeof(RekallAgeVulkanNativeDeviceInfo).GetProperties(),
            property => property.PropertyType.FullName?.Contains("Veldrid.GraphicsDevice", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task SurfaceBoundSessionContractPresentsSubmissionsAndCapturesRgba()
    {
        await using var session = new RecordingPresentationSession();
        var submission = new RekallAgeVulkanSceneSubmission(
            ViewportFrame(320, 180),
            RekallAgeRuntimeViewportAssetSet.Empty,
            Array.Empty<RekallAgeRuntimeGpuWorkload>(),
            RuntimeEntityCount: 1,
            SceneRevision: 1,
            AssetRevision: 1);

        var presented = await session.PresentAsync(submission, CancellationToken.None);
        var pixels = await session.CapturePresentedRgbaAsync(CancellationToken.None);

        Assert.True(presented.PresentedFrame);
        Assert.Equal(320, pixels.Width);
        Assert.Equal(180, pixels.Height);
        Assert.Equal(320 * 180 * 4, pixels.Rgba.Length);
        Assert.Equal("Test GPU", session.DeviceInfo.DeviceName);
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

    [Fact]
    public void UnavailableFrameCopiesErrorsToPreventLaterMutation()
    {
        var sourceErrors = new List<string> { "VK_ERROR_DEVICE_LOST" };

        var frame = RekallAgeVulkanPresentationFrame.Unavailable(
            ViewportFrame(320, 180),
            "Presentation failed.",
            sourceErrors);

        sourceErrors[0] = "CHANGED";
        sourceErrors.Add("VK_ERROR_OUT_OF_DATE_KHR");

        Assert.Equal(["VK_ERROR_DEVICE_LOST"], frame.Errors);
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

        public bool IsDisposalComplete { get; private set; }

        public ValueTask DisposeAsync()
        {
            IsDisposalComplete = true;
            return ValueTask.CompletedTask;
        }

        public RekallAgeVulkanNativeDeviceInfo DeviceInfo { get; } = new(
            "Test GPU",
            "Vulkan",
            1,
            2,
            3,
            4,
            5,
            "Driver",
            "1.2.3");

        public ValueTask<RekallAgeVulkanPresentationFrame> PresentAsync(
            RekallAgeVulkanSceneSubmission submission,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(RekallAgeVulkanPresentationFrame.Presented(submission.Frame));

        public ValueTask<RekallAgeVulkanPresentedPixels> CapturePresentedRgbaAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new RekallAgeVulkanPresentedPixels(320, 180, new byte[320 * 180 * 4]));

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
