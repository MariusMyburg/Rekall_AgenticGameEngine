using Rekall.Age.Studio;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioVulkanViewportHostTests
{
    [Fact]
    public void DipMetricsRoundToPhysicalPixelsAndZeroSizeSuspendsPresentation()
    {
        var visible = RekallAgeStudioViewportMetrics.FromDips(320.4, 180.4, 1.25, 1.5, true);
        var zero = RekallAgeStudioViewportMetrics.FromDips(0, 180, 1.25, 1.5, true);

        Assert.Equal(401, visible.PixelWidth);
        Assert.Equal(271, visible.PixelHeight);
        Assert.True(visible.IsPresentable);
        Assert.Equal(0, zero.PixelWidth);
        Assert.False(zero.IsPresentable);
    }

    [Fact]
    public async Task ResizeMessagesCoalesceAndUseVerifiedClientExtent()
    {
        var native = new RecordingNativeWindow { VerifiedClientWidth = 801, VerifiedClientHeight = 451 };
        var surface = new RecordingSurfaceController(native.Order);
        var core = new RekallAgeVulkanViewportHostCore(native, surface);
        core.BuildWindow(new IntPtr(17));

        core.QueueResize(400, 225, 2, 2, true);
        core.QueueResize(401, 226, 2, 2, true);
        await core.ApplyPendingResizeAsync(CancellationToken.None);

        Assert.Single(native.Resizes);
        Assert.Equal((802, 452), native.Resizes[0]);
        Assert.Equal(801, surface.Metrics.PixelWidth);
        Assert.Equal(451, surface.Metrics.PixelHeight);
        Assert.Equal(401, surface.Metrics.DipWidth);
        Assert.Equal(226, surface.Metrics.DipHeight);
    }

    [Fact]
    public async Task PointerFactsDecodeSignedClientAndWheelScreenCoordinatesInDips()
    {
        var native = new RecordingNativeWindow { ScreenOffsetX = 100, ScreenOffsetY = 40 };
        var surface = new RecordingSurfaceController(native.Order);
        var core = new RekallAgeVulkanViewportHostCore(native, surface);
        core.BuildWindow(new IntPtr(17));
        core.QueueResize(320, 180, 2, 2, true);
        await core.ApplyPendingResizeAsync(CancellationToken.None);
        var facts = new List<RekallAgeStudioViewportPointerFact>();
        core.PointerFact += (_, fact) => facts.Add(fact);

        core.ProcessWindowMessage(
            RekallAgeVulkanViewportHostCore.WmMouseMove,
            IntPtr.Zero,
            MakeLParam(-12, 34));
        core.ProcessWindowMessage(
            RekallAgeVulkanViewportHostCore.WmMouseWheel,
            MakeWParam(0, -120),
            MakeLParam(140, 100));

        Assert.Equal(-6, facts[0].DisplayX);
        Assert.Equal(17, facts[0].DisplayY);
        Assert.Equal(RekallAgeStudioViewportPointerKind.Move, facts[0].Kind);
        Assert.Equal(20, facts[1].DisplayX);
        Assert.Equal(30, facts[1].DisplayY);
        Assert.Equal(-120, facts[1].WheelDelta);
    }

    [Fact]
    public void FocusAndCaptureLossCancelAcceptedTransformCapture()
    {
        var native = new RecordingNativeWindow();
        var surface = new RecordingSurfaceController(native.Order);
        var core = new RekallAgeVulkanViewportHostCore(native, surface);
        core.BuildWindow(new IntPtr(17));
        var facts = new List<RekallAgeStudioViewportPointerFact>();
        core.PointerFact += (_, fact) => facts.Add(fact);

        core.ProcessWindowMessage(
            RekallAgeVulkanViewportHostCore.WmLeftButtonDown,
            IntPtr.Zero,
            MakeLParam(10, 20));
        core.CapturePointer();
        core.ProcessWindowMessage(RekallAgeVulkanViewportHostCore.WmKillFocus, IntPtr.Zero, IntPtr.Zero);

        Assert.Equal(1, native.FocusCount);
        Assert.Equal(1, native.CaptureCount);
        Assert.Equal(1, native.ReleaseCaptureCount);
        Assert.Contains(facts, fact => fact.Kind == RekallAgeStudioViewportPointerKind.FocusLost);
        Assert.False(core.HasPointerCapture);
    }

    [Fact]
    public async Task ChildWindowIsDestroyedExactlyOnceAfterPresenterDisposal()
    {
        var native = new RecordingNativeWindow();
        var surface = new RecordingSurfaceController(native.Order);
        var core = new RekallAgeVulkanViewportHostCore(native, surface);
        var child = core.BuildWindow(new IntPtr(17));

        await core.DisposePresenterAsync();
        core.DestroyWindow(child);
        core.DestroyWindow(child);

        Assert.Equal(["presenter", "hwnd"], native.Order);
        Assert.Equal(1, surface.DisposeCount);
        Assert.Equal(1, native.DestroyCount);
        Assert.Equal(1, native.CreateCount);
    }

    private static IntPtr MakeLParam(short x, short y) =>
        new(unchecked((int)((ushort)x | ((uint)(ushort)y << 16))));

    private static IntPtr MakeWParam(ushort low, short high) =>
        new(unchecked((int)(low | ((uint)(ushort)high << 16))));

    private sealed class RecordingSurfaceController(List<string> order) : IRekallAgeVulkanViewportSurfaceController
    {
        public RekallAgeStudioViewportMetrics Metrics { get; private set; }

        public bool IsDisposed { get; private set; }

        public int DisposeCount { get; private set; }

        public void AttachSurface(IntPtr hwnd) => Assert.NotEqual(IntPtr.Zero, hwnd);

        public ValueTask<RekallAgeStudioViewportMetrics> ResizeAsync(
            RekallAgeStudioViewportMetrics requested,
            Func<(int Width, int Height)> resizeAndReadClient,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var verified = resizeAndReadClient();
            Metrics = requested with { PixelWidth = verified.Width, PixelHeight = verified.Height };
            return ValueTask.FromResult(Metrics);
        }

        public ValueTask SuspendAsync(RekallAgeStudioViewportMetrics metrics, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Metrics = metrics;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            IsDisposed = true;
            order.Add("presenter");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingNativeWindow : IRekallAgeVulkanViewportNativeWindow
    {
        private readonly IntPtr _child = new(23);

        public int CreateCount { get; private set; }

        public int DestroyCount { get; private set; }

        public int FocusCount { get; private set; }

        public int CaptureCount { get; private set; }

        public int ReleaseCaptureCount { get; private set; }

        public int VerifiedClientWidth { get; init; }

        public int VerifiedClientHeight { get; init; }

        public int ScreenOffsetX { get; init; }

        public int ScreenOffsetY { get; init; }

        public List<(int Width, int Height)> Resizes { get; } = [];

        public List<string> Order { get; } = [];

        public IntPtr CreateChild(IntPtr parent)
        {
            Assert.NotEqual(IntPtr.Zero, parent);
            CreateCount++;
            return _child;
        }

        public void DestroyChild(IntPtr hwnd)
        {
            Assert.Equal(_child, hwnd);
            DestroyCount++;
            Order.Add("hwnd");
        }

        public void ResizeChild(IntPtr hwnd, int width, int height)
        {
            Assert.Equal(_child, hwnd);
            Resizes.Add((width, height));
        }

        public (int Width, int Height) GetClientSize(IntPtr hwnd)
        {
            Assert.Equal(_child, hwnd);
            var requested = Resizes[^1];
            return (
                VerifiedClientWidth > 0 ? VerifiedClientWidth : requested.Width,
                VerifiedClientHeight > 0 ? VerifiedClientHeight : requested.Height);
        }

        public (int X, int Y) ScreenToClient(IntPtr hwnd, int x, int y) =>
            (x - ScreenOffsetX, y - ScreenOffsetY);

        public void Focus(IntPtr hwnd) => FocusCount++;

        public void Capture(IntPtr hwnd) => CaptureCount++;

        public void ReleaseCapture() => ReleaseCaptureCount++;
    }
}
