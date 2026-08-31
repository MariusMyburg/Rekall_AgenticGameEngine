using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.Windows;
using Rekall.Age.Studio;
using System.Runtime.InteropServices;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioVulkanViewportHostTests
{
    [Fact]
    public void WorldViewportStyleAdapterProducesClayAndFlatInspectionFramesWithoutMutatingAuthoredData()
    {
        var renderable = new RekallAgeRuntimeViewportRenderable(
            "mesh", "Mesh", "model", "hero", 0, 0, 0, 0,
            MaterialColor: "#224466",
            TextureAssetId: "base-color",
            NormalTextureAssetId: "normal-map",
            MetallicFactor: 0.8,
            RoughnessFactor: 0.2,
            EmissiveTextureAssetId: "emissive");
        var source = ViewportFrame() with { Renderables = [renderable] };

        var clay = Assert.Single(RekallAgeStudioViewportStyleAdapter.Apply(
            source, RekallAgeStudioViewportRenderStyle.Clay).Renderables);
        var flat = Assert.Single(RekallAgeStudioViewportStyleAdapter.Apply(
            source, RekallAgeStudioViewportRenderStyle.FlatShaded).Renderables);

        Assert.Equal("#B6AA97", clay.MaterialColor);
        Assert.Null(clay.TextureAssetId);
        Assert.Null(clay.NormalTextureAssetId);
        Assert.Null(clay.EmissiveTextureAssetId);
        Assert.Null(flat.NormalTextureAssetId);
        Assert.Equal("base-color", flat.TextureAssetId);
        Assert.Equal("base-color", renderable.TextureAssetId);
        Assert.Equal("normal-map", renderable.NormalTextureAssetId);
    }

    [Fact]
    public void NativeVulkanChildPaintLeavesThePresentedSurfaceUntouched()
    {
        if (!OperatingSystem.IsWindows()) return;

        var parent = NativePaintProbe.CreateOffscreenParent();
        var native = new RekallAgeWin32VulkanViewportNativeWindow();
        var child = IntPtr.Zero;
        try
        {
            child = native.CreateChild(parent);
            native.ResizeChild(child, 4, 4);

            NativePaintProbe.PrintClient(child, 0x00332211, out var before, out var after);

            Assert.Equal(before, after);
        }
        finally
        {
            if (child != IntPtr.Zero) native.DestroyChild(child);
            NativePaintProbe.Destroy(parent);
        }
    }

    [Fact]
    public void NativeVulkanChildAcknowledgesBackgroundEraseWithoutChangingTheSurface()
    {
        if (!OperatingSystem.IsWindows()) return;

        var parent = NativePaintProbe.CreateOffscreenParent();
        var native = new RekallAgeWin32VulkanViewportNativeWindow();
        var child = IntPtr.Zero;
        try
        {
            child = native.CreateChild(parent);
            native.ResizeChild(child, 4, 4);

            var result = NativePaintProbe.EraseBackground(child, 0x00332211, out var before, out var after);

            Assert.Equal(new IntPtr(1), result);
            Assert.Equal(before, after);
        }
        finally
        {
            if (child != IntPtr.Zero) native.DestroyChild(child);
            NativePaintProbe.Destroy(parent);
        }
    }

    [Fact]
    public void MainWindowAvailabilityStateDoesNotReportVulkanFailureBeforeAProjectIsOpen()
    {
        var recovery = new RekallAgeStudioViewportRecoveryState(TimeSpan.FromSeconds(1));
        var now = new DateTimeOffset(2026, 8, 30, 8, 0, 0, TimeSpan.Zero);

        var visual = recovery.Synchronize(hasProject: false, viewportAvailable: false, now);

        Assert.False(visual.PresentationSurfaceVisible);
        Assert.False(visual.PlaceholderVisible);
        Assert.False(recovery.TryBeginAutomaticRetry(now));
    }

    [Fact]
    public void MainWindowAvailabilityStateShowsPlaceholderOnTheFirstUnavailableFrame()
    {
        var recovery = new RekallAgeStudioViewportRecoveryState(TimeSpan.FromSeconds(1));
        var now = new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);

        var visual = recovery.Synchronize(hasProject: true, viewportAvailable: false, now);

        Assert.False(visual.PresentationSurfaceVisible);
        Assert.True(visual.PlaceholderVisible);
        Assert.True(recovery.TryBeginAutomaticRetry(now));
    }

    [Fact]
    public void MainWindowAvailabilityStateAutomaticallyRetriesDeviceLossAndRestoresTheSurface()
    {
        var recovery = new RekallAgeStudioViewportRecoveryState(TimeSpan.FromSeconds(1));
        var now = new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);
        Assert.True(recovery.Synchronize(hasProject: true, viewportAvailable: true, now)
            .PresentationSurfaceVisible);

        var unavailable = recovery.Synchronize(hasProject: true, viewportAvailable: false, now);

        Assert.False(unavailable.PresentationSurfaceVisible);
        Assert.True(unavailable.PlaceholderVisible);
        Assert.True(recovery.TryBeginAutomaticRetry(now));
        Assert.False(recovery.TryBeginAutomaticRetry(now + TimeSpan.FromMilliseconds(500)));
        Assert.True(recovery.TryBeginAutomaticRetry(now + TimeSpan.FromSeconds(1)));

        var recovered = recovery.Synchronize(
            hasProject: true,
            viewportAvailable: true,
            now + TimeSpan.FromSeconds(1));
        Assert.True(recovered.PresentationSurfaceVisible);
        Assert.False(recovered.PlaceholderVisible);
        Assert.False(recovery.TryBeginAutomaticRetry(now + TimeSpan.FromSeconds(2)));
    }

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
        Assert.Single(native.InvalidatedRegions);
        Assert.Equal(new RekallAgeNativeViewportRegion(new IntPtr(17), 0, 0, 802, 452), native.InvalidatedRegions[0]);
    }

    [Fact]
    public async Task HiddenViewportHidesTheNativeChildAndSuspendsPresentation()
    {
        var native = new RecordingNativeWindow();
        var surface = new RecordingSurfaceController(native.Order);
        var core = new RekallAgeVulkanViewportHostCore(native, surface);
        core.BuildWindow(new IntPtr(17));

        core.QueueResize(400, 225, 2, 2, isVisible: false);
        await core.ApplyPendingResizeAsync(CancellationToken.None);

        Assert.Equal([false], native.VisibilityChanges);
        Assert.False(surface.Metrics.IsPresentable);
        Assert.Single(native.InvalidatedRegions);
    }

    [Fact]
    public async Task PresentationVisibilityChangeRefreshesTheWpfParentToClearExposedNativePixels()
    {
        var native = new RecordingNativeWindow();
        var surface = new RecordingSurfaceController(native.Order);
        var core = new RekallAgeVulkanViewportHostCore(native, surface);
        core.BuildWindow(new IntPtr(17));
        core.QueueResize(400, 225, 1, 1, isVisible: true);
        await core.ApplyPendingResizeAsync(CancellationToken.None);

        core.SetPresentationVisible(false);

        Assert.Equal([false], native.VisibilityChanges);
        Assert.Equal(2, native.InvalidatedRegions.Count);

        core.SetPresentationVisible(false);

        Assert.Equal([false], native.VisibilityChanges);
        Assert.Equal(2, native.InvalidatedRegions.Count);
    }

    [Fact]
    public async Task UnavailablePlaceholderHidesOnlyTheNativePresentationAndPreservesRetryMetrics()
    {
        var native = new RecordingNativeWindow();
        var surface = new RecordingSurfaceController(native.Order);
        var core = new RekallAgeVulkanViewportHostCore(native, surface);
        core.BuildWindow(new IntPtr(17));
        core.QueueResize(400, 225, 2, 2, isVisible: true);
        await core.ApplyPendingResizeAsync(CancellationToken.None);

        core.SetPresentationVisible(false);

        Assert.Equal([false], native.VisibilityChanges);
        Assert.True(surface.Metrics.IsPresentable);
        Assert.Equal(0, surface.SuspendCount);
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
    public void ChildWindowMessagesAreForwardedIntoPointerFacts()
    {
        var native = new RecordingNativeWindow();
        var surface = new RecordingSurfaceController(native.Order);
        var core = new RekallAgeVulkanViewportHostCore(native, surface);
        var facts = new List<RekallAgeStudioViewportPointerFact>();
        core.PointerFact += (_, fact) => facts.Add(fact);

        core.BuildWindow(new IntPtr(17));
        native.DeliverMessage(
            RekallAgeVulkanViewportHostCore.WmLeftButtonDown,
            IntPtr.Zero,
            MakeLParam(10, 20));

        Assert.Equal(1, native.AttachMessageHandlerCount);
        Assert.Contains(facts, fact => fact.Kind == RekallAgeStudioViewportPointerKind.Down);
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

        Assert.Equal(["presenter", "hwnd", "invalidate"], native.Order);
        Assert.Equal(1, surface.DisposeCount);
        Assert.Equal(1, native.DestroyCount);
        Assert.Equal(1, native.CreateCount);
        Assert.Single(native.InvalidatedRegions);
    }

    [Fact]
    public async Task IncompleteSessionDisposalRetainsCleanupObligationAndBlocksHwndUntilRetryCompletes()
    {
        var session = new IncompleteThenTerminalPresentationSession();
        var presenter = new RekallAgeStudioVulkanViewportPresenter((_, _, _, _, _) => session);
        var native = new RecordingNativeWindow();
        var core = new RekallAgeVulkanViewportHostCore(native, presenter);
        var child = core.BuildWindow(new IntPtr(17));
        core.QueueResize(320, 180, 1, 1, isVisible: true);
        await core.ApplyPendingResizeAsync(CancellationToken.None);
        await presenter.PresentAsync(
            ViewportFrame(),
            RekallAgeRuntimeViewportAssetSet.Empty,
            PresentationContext(),
            CancellationToken.None);

        var firstFailure = await Assert.ThrowsAsync<AggregateException>(
            () => core.DisposePresenterAsync().AsTask());

        Assert.Contains(firstFailure.InnerExceptions, error => error.Message == "session cleanup interrupted");
        Assert.Equal(1, session.DisposeCount);
        Assert.False(session.IsDisposalComplete);
        Assert.False(presenter.IsDisposalComplete);
        Assert.Throws<InvalidOperationException>(() => core.DestroyWindow(child));

        var terminalFailure = await Assert.ThrowsAsync<AggregateException>(
            () => core.DisposePresenterAsync().AsTask());

        Assert.Contains("terminal cleanup issue", terminalFailure.ToString(), StringComparison.Ordinal);
        Assert.Equal(2, session.DisposeCount);
        Assert.True(session.IsDisposalComplete);
        Assert.True(presenter.IsDisposalComplete);
        core.DestroyWindow(child);
        Assert.Equal(1, native.DestroyCount);
    }

    [Fact]
    public async Task AggregateAfterProvenTerminalSessionCleanupAllowsHwndDestruction()
    {
        var session = new TerminalThrowingPresentationSession();
        var presenter = new RekallAgeStudioVulkanViewportPresenter((_, _, _, _, _) => session);
        var native = new RecordingNativeWindow();
        var core = new RekallAgeVulkanViewportHostCore(native, presenter);
        var child = core.BuildWindow(new IntPtr(17));
        core.QueueResize(320, 180, 1, 1, isVisible: true);
        await core.ApplyPendingResizeAsync(CancellationToken.None);
        await presenter.PresentAsync(
            ViewportFrame(),
            RekallAgeRuntimeViewportAssetSet.Empty,
            PresentationContext(),
            CancellationToken.None);

        await Assert.ThrowsAsync<AggregateException>(() => core.DisposePresenterAsync().AsTask());

        Assert.True(session.IsDisposalComplete);
        Assert.True(presenter.IsDisposalComplete);
        core.DestroyWindow(child);
        Assert.Equal(1, native.DestroyCount);
    }

    [Fact]
    public async Task PresenterSerializesDisposalBehindAnInFlightPresentation()
    {
        var session = new BlockingPresentationSession();
        var presenter = new RekallAgeStudioVulkanViewportPresenter((_, _, _, _, _) => session);
        var native = new RecordingNativeWindow();
        var core = new RekallAgeVulkanViewportHostCore(native, presenter);
        var child = core.BuildWindow(new IntPtr(17));
        core.QueueResize(320, 180, 1, 1, isVisible: true);
        await core.ApplyPendingResizeAsync(CancellationToken.None);

        var present = presenter.PresentAsync(
            ViewportFrame(),
            RekallAgeRuntimeViewportAssetSet.Empty,
            PresentationContext(),
            CancellationToken.None).AsTask();
        await session.PresentationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var dispose = core.DisposePresenterAsync().AsTask();

        Assert.False(dispose.IsCompleted);
        Assert.False(presenter.IsDisposalComplete);
        Assert.Throws<InvalidOperationException>(() => core.DestroyWindow(child));

        session.ReleasePresentation.TrySetResult();
        await present;
        await dispose;
        Assert.True(presenter.IsDisposalComplete);
        core.DestroyWindow(child);
    }

    [Fact]
    public async Task ShaderInvalidationReachesTheSharedPresentationSessionOnTheNextFrame()
    {
        var session = new RecordingInvalidationPresentationSession();
        var presenter = new RekallAgeStudioVulkanViewportPresenter((_, _, _, _, _) => session);
        presenter.AttachSurface(new IntPtr(23));
        await presenter.ResizeAsync(
            new RekallAgeStudioViewportMetrics(320, 180, 320, 180, true),
            () => (320, 180),
            CancellationToken.None);
        await presenter.PresentAsync(
            ViewportFrame(),
            RekallAgeRuntimeViewportAssetSet.Empty,
            PresentationContext(),
            CancellationToken.None);

        await presenter.InvalidateShadersAsync(CancellationToken.None);
        await presenter.PresentAsync(
            ViewportFrame(),
            RekallAgeRuntimeViewportAssetSet.Empty,
            PresentationContext(),
            CancellationToken.None);

        Assert.Equal(1, session.ShaderInvalidationCount);
        await presenter.DisposeAsync();
    }

    [Fact]
    public async Task MainWindowRetryRecreatesTheProductionPresenterSessionAfterDeviceLoss()
    {
        var sessionCount = 0;
        var presenter = new RekallAgeStudioVulkanViewportPresenter((_, _, _, _, _) =>
        {
            sessionCount++;
            return sessionCount == 1
                ? new DeviceLossPresentationSession()
                : new RecordingInvalidationPresentationSession();
        });
        presenter.AttachSurface(new IntPtr(23));
        await presenter.ResizeAsync(
            new RekallAgeStudioViewportMetrics(320, 180, 320, 180, true),
            () => (320, 180),
            CancellationToken.None);
        var recovery = new RekallAgeStudioViewportRecoveryState(TimeSpan.FromSeconds(1));
        var now = new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);

        var unavailable = await presenter.PresentAsync(
            ViewportFrame(),
            RekallAgeRuntimeViewportAssetSet.Empty,
            PresentationContext(),
            CancellationToken.None);
        recovery.Synchronize(hasProject: true, unavailable.PresentedFrame, now);
        Assert.True(recovery.TryBeginAutomaticRetry(now));
        var recovered = await presenter.PresentAsync(
            ViewportFrame(),
            RekallAgeRuntimeViewportAssetSet.Empty,
            PresentationContext(),
            CancellationToken.None);

        Assert.False(unavailable.PresentedFrame);
        Assert.Contains("device lost", unavailable.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.True(recovered.PresentedFrame);
        Assert.Equal(2, sessionCount);
        await presenter.DisposeAsync();
    }

    [Fact]
    public async Task MainWindowSimulationTickUsesRecoveryCadenceBeforeResumingAdvance()
    {
        var sessionCount = 0;
        var presenter = new RekallAgeStudioVulkanViewportPresenter((_, _, _, _, _) =>
        {
            sessionCount++;
            return sessionCount < 3
                ? new DeviceLossPresentationSession()
                : new RecordingInvalidationPresentationSession();
        });
        presenter.AttachSurface(new IntPtr(23));
        await presenter.ResizeAsync(
            new RekallAgeStudioViewportMetrics(320, 180, 320, 180, true),
            () => (320, 180),
            CancellationToken.None);
        var recovery = new RekallAgeStudioViewportRecoveryState(TimeSpan.FromSeconds(1));
        var now = new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);

        var initial = await presenter.PresentAsync(
            ViewportFrame(),
            RekallAgeRuntimeViewportAssetSet.Empty,
            PresentationContext(),
            CancellationToken.None);
        recovery.Synchronize(hasProject: true, initial.PresentedFrame, now);

        Assert.Equal(
            RekallAgeStudioViewportTickAction.RecoverPresentation,
            recovery.SelectTickAction(true, viewportAvailable: false, isSimulating: true, now));
        var retry = await presenter.PresentAsync(
            ViewportFrame(),
            RekallAgeRuntimeViewportAssetSet.Empty,
            PresentationContext(),
            CancellationToken.None);
        recovery.Synchronize(hasProject: true, retry.PresentedFrame, now);

        Assert.Equal(
            RekallAgeStudioViewportTickAction.None,
            recovery.SelectTickAction(
                true,
                viewportAvailable: false,
                isSimulating: true,
                now + TimeSpan.FromMilliseconds(16)));
        Assert.Equal(
            RekallAgeStudioViewportTickAction.None,
            recovery.SelectTickAction(
                true,
                viewportAvailable: false,
                isSimulating: true,
                now + TimeSpan.FromMilliseconds(999)));
        Assert.Equal(2, sessionCount);

        Assert.Equal(
            RekallAgeStudioViewportTickAction.RecoverPresentation,
            recovery.SelectTickAction(
                true,
                viewportAvailable: false,
                isSimulating: true,
                now + TimeSpan.FromSeconds(1)));
        var recovered = await presenter.PresentAsync(
            ViewportFrame(),
            RekallAgeRuntimeViewportAssetSet.Empty,
            PresentationContext(),
            CancellationToken.None);
        recovery.Synchronize(
            hasProject: true,
            recovered.PresentedFrame,
            now + TimeSpan.FromSeconds(1));

        Assert.True(recovered.PresentedFrame);
        Assert.Equal(3, sessionCount);
        Assert.Equal(
            RekallAgeStudioViewportTickAction.AdvanceSimulation,
            recovery.SelectTickAction(
                true,
                viewportAvailable: true,
                isSimulating: true,
                now + TimeSpan.FromSeconds(1) + TimeSpan.FromMilliseconds(16)));
        await presenter.DisposeAsync();
    }

    [Fact]
    public async Task ProductionShutdownChainRetriesIncompleteRendererBeforeAllowingHwndDestruction()
    {
        var session = new IncompleteThenTerminalPresentationSession();
        var presenter = new RekallAgeStudioVulkanViewportPresenter((_, _, _, _, _) => session);
        var native = new RecordingNativeWindow();
        var core = new RekallAgeVulkanViewportHostCore(native, presenter);
        var child = core.BuildWindow(new IntPtr(17));
        core.QueueResize(320, 180, 1, 1, isVisible: true);
        await core.ApplyPendingResizeAsync(CancellationToken.None);
        await presenter.PresentAsync(
            ViewportFrame(),
            RekallAgeRuntimeViewportAssetSet.Empty,
            PresentationContext(),
            CancellationToken.None);
        var preview = new RekallAgeStudioVulkanPreviewSession(presenter);
        var viewModel = new RekallAgeStudioViewModel(preview);
        var observedIncompleteBoundary = false;
        RekallAgeStudioShutdownCoordinator coordinator = null!;
        coordinator = new RekallAgeStudioShutdownCoordinator(
            maximumAttempts: 2,
            retryDelay: TimeSpan.Zero,
            (_, _) =>
            {
                observedIncompleteBoundary = true;
                Assert.False(coordinator.IsDisposalComplete);
                Assert.False(viewModel.IsDisposalComplete);
                Assert.False(preview.IsDisposalComplete);
                Assert.Throws<InvalidOperationException>(() => core.DestroyWindow(child));
                return ValueTask.CompletedTask;
            });

        var result = await coordinator.TryShutdownAsync(viewModel, CancellationToken.None);

        Assert.True(observedIncompleteBoundary);
        Assert.True(result.TerminalCleanupComplete);
        Assert.Equal(2, result.Attempts);
        Assert.True(coordinator.IsDisposalComplete);
        Assert.True(viewModel.IsDisposalComplete);
        Assert.True(preview.IsDisposalComplete);
        Assert.Equal(2, session.DisposeCount);
        core.DestroyWindow(child);
        Assert.Equal(1, native.DestroyCount);
    }

    [Fact]
    public async Task ProductionShutdownChainRefusesHwndDestructionAfterBoundedPersistentFailure()
    {
        var session = new PersistentIncompletePresentationSession();
        var presenter = new RekallAgeStudioVulkanViewportPresenter((_, _, _, _, _) => session);
        var native = new RecordingNativeWindow();
        var core = new RekallAgeVulkanViewportHostCore(native, presenter);
        var child = core.BuildWindow(new IntPtr(17));
        core.QueueResize(320, 180, 1, 1, isVisible: true);
        await core.ApplyPendingResizeAsync(CancellationToken.None);
        await presenter.PresentAsync(
            ViewportFrame(),
            RekallAgeRuntimeViewportAssetSet.Empty,
            PresentationContext(),
            CancellationToken.None);
        var preview = new RekallAgeStudioVulkanPreviewSession(presenter);
        var viewModel = new RekallAgeStudioViewModel(preview);
        var coordinator = new RekallAgeStudioShutdownCoordinator(
            maximumAttempts: 2,
            retryDelay: TimeSpan.Zero,
            (_, _) => ValueTask.CompletedTask);

        var result = await coordinator.TryShutdownAsync(viewModel, CancellationToken.None);

        Assert.False(result.TerminalCleanupComplete);
        Assert.Equal(2, result.Attempts);
        Assert.Contains("REKALL_STUDIO_VULKAN_SHUTDOWN_INCOMPLETE", result.Failure?.ToString(), StringComparison.Ordinal);
        Assert.False(coordinator.IsDisposalComplete);
        Assert.False(viewModel.IsDisposalComplete);
        Assert.False(preview.IsDisposalComplete);
        Assert.Contains(
            "REKALL_STUDIO_VULKAN_SHUTDOWN_INCOMPLETE",
            viewModel.ViewportUnavailableReason,
            StringComparison.Ordinal);
        Assert.Equal(2, session.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => core.DestroyWindow(child));
        Assert.Equal(0, native.DestroyCount);

        session.AllowCompletion = true;
        await viewModel.DisposeAsync();
        Assert.True(viewModel.IsDisposalComplete);
        core.DestroyWindow(child);
    }

    private static IntPtr MakeLParam(short x, short y) =>
        new(unchecked((int)((ushort)x | ((uint)(ushort)y << 16))));

    private static IntPtr MakeWParam(ushort low, short high) =>
        new(unchecked((int)(low | ((uint)(ushort)high << 16))));

    private static RekallAgeRuntimeViewportFrame ViewportFrame() => new(
        "Main",
        0,
        0,
        320,
        180,
        null,
        [],
        [],
        0,
        new RekallAgeRuntimeViewportOverlay(false, 0),
        []);

    private static RekallAgeStudioPresentationContext PresentationContext() => new(
        "C:\\Project",
        [],
        0,
        1,
        1);

    private sealed class IncompleteThenTerminalPresentationSession : IRekallAgeVulkanPresentationSession
    {
        public int DisposeCount { get; private set; }

        public bool IsDisposalComplete { get; private set; }

        public ValueTask<RekallAgeVulkanPresentationFrame> PresentAsync(
            RekallAgeVulkanSceneSubmission submission,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(RekallAgeVulkanPresentationFrame.Presented(submission.Frame, "test-gpu"));

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            if (DisposeCount == 1)
            {
                return ValueTask.FromException(new InvalidOperationException("session cleanup interrupted"));
            }

            IsDisposalComplete = true;
            return ValueTask.FromException(
                new AggregateException("terminal cleanup issue", new InvalidOperationException("native cleanup issue")));
        }
    }

    private sealed class TerminalThrowingPresentationSession : IRekallAgeVulkanPresentationSession
    {
        public bool IsDisposalComplete { get; private set; }

        public ValueTask<RekallAgeVulkanPresentationFrame> PresentAsync(
            RekallAgeVulkanSceneSubmission submission,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(RekallAgeVulkanPresentationFrame.Presented(submission.Frame, "test-gpu"));

        public ValueTask DisposeAsync()
        {
            IsDisposalComplete = true;
            return ValueTask.FromException(
                new AggregateException("terminal cleanup issue", new InvalidOperationException("native cleanup issue")));
        }
    }

    private sealed class PersistentIncompletePresentationSession : IRekallAgeVulkanPresentationSession
    {
        public int DisposeCount { get; private set; }

        public bool AllowCompletion { get; set; }

        public bool IsDisposalComplete { get; private set; }

        public ValueTask<RekallAgeVulkanPresentationFrame> PresentAsync(
            RekallAgeVulkanSceneSubmission submission,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(RekallAgeVulkanPresentationFrame.Presented(submission.Frame, "test-gpu"));

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            if (AllowCompletion)
            {
                IsDisposalComplete = true;
                return ValueTask.CompletedTask;
            }

            return ValueTask.FromException(
                new InvalidOperationException("session cleanup remains incomplete"));
        }
    }

    private sealed class BlockingPresentationSession : IRekallAgeVulkanPresentationSession
    {
        public TaskCompletionSource PresentationEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleasePresentation { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<RekallAgeVulkanPresentationFrame> PresentAsync(
            RekallAgeVulkanSceneSubmission submission,
            CancellationToken cancellationToken)
        {
            PresentationEntered.TrySetResult();
            await ReleasePresentation.Task.WaitAsync(cancellationToken);
            return RekallAgeVulkanPresentationFrame.Presented(submission.Frame, "test-gpu");
        }

        public bool IsDisposalComplete { get; private set; }

        public ValueTask DisposeAsync()
        {
            IsDisposalComplete = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingInvalidationPresentationSession : IRekallAgeVulkanPresentationSession
    {
        public int ShaderInvalidationCount { get; private set; }

        public bool IsDisposalComplete { get; private set; }

        public ValueTask<RekallAgeVulkanPresentationFrame> PresentAsync(
            RekallAgeVulkanSceneSubmission submission,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(RekallAgeVulkanPresentationFrame.Presented(submission.Frame, "test-gpu"));

        public ValueTask InvalidateShadersAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ShaderInvalidationCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            IsDisposalComplete = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DeviceLossPresentationSession : IRekallAgeVulkanPresentationSession
    {
        public bool IsDisposalComplete { get; private set; }

        public ValueTask<RekallAgeVulkanPresentationFrame> PresentAsync(
            RekallAgeVulkanSceneSubmission submission,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<RekallAgeVulkanPresentationFrame>(
                new InvalidOperationException("VK_ERROR_DEVICE_LOST: simulated device lost"));

        public ValueTask DisposeAsync()
        {
            IsDisposalComplete = true;
            return ValueTask.FromException(
                new AggregateException("device-loss session cleanup failed", new InvalidOperationException("native issue")));
        }
    }

    private sealed class RecordingSurfaceController(List<string> order) : IRekallAgeVulkanViewportSurfaceController
    {
        public RekallAgeStudioViewportMetrics Metrics { get; private set; }

        public bool IsDisposed { get; private set; }

        public bool IsDisposalComplete => IsDisposed;

        public int DisposeCount { get; private set; }

        public int SuspendCount { get; private set; }

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
            SuspendCount++;
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

        public int AttachMessageHandlerCount { get; private set; }

        private Func<int, IntPtr, IntPtr, bool>? MessageHandler { get; set; }

        public int VerifiedClientWidth { get; init; }

        public int VerifiedClientHeight { get; init; }

        public int ScreenOffsetX { get; init; }

        public int ScreenOffsetY { get; init; }

        public List<(int Width, int Height)> Resizes { get; } = [];

        public List<bool> VisibilityChanges { get; } = [];

        public List<RekallAgeNativeViewportRegion> InvalidatedRegions { get; } = [];

        public List<string> Order { get; } = [];

        public IntPtr CreateChild(IntPtr parent)
        {
            Assert.NotEqual(IntPtr.Zero, parent);
            CreateCount++;
            return _child;
        }

        public void AttachMessageHandler(
            IntPtr hwnd,
            Func<int, IntPtr, IntPtr, bool> messageHandler)
        {
            Assert.Equal(_child, hwnd);
            AttachMessageHandlerCount++;
            MessageHandler = messageHandler;
        }

        public void DetachMessageHandler(IntPtr hwnd)
        {
            Assert.Equal(_child, hwnd);
            MessageHandler = null;
        }

        public void DeliverMessage(int message, IntPtr wParam, IntPtr lParam)
        {
            Assert.NotNull(MessageHandler);
            MessageHandler(message, wParam, lParam);
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

        public void SetVisible(IntPtr hwnd, bool visible)
        {
            Assert.Equal(_child, hwnd);
            VisibilityChanges.Add(visible);
        }

        public RekallAgeNativeViewportRegion CaptureParentSurfaceRegion(IntPtr hwnd)
        {
            Assert.Equal(_child, hwnd);
            var size = Resizes.Count == 0 ? (1, 1) : Resizes[^1];
            return new RekallAgeNativeViewportRegion(new IntPtr(17), 0, 0, size.Item1, size.Item2);
        }

        public void InvalidateParentSurface(RekallAgeNativeViewportRegion region)
        {
            InvalidatedRegions.Add(region);
            Order.Add("invalidate");
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

    private static class NativePaintProbe
    {
        private const int WsPopup = unchecked((int)0x80000000);
        private const int WsVisible = 0x10000000;

        internal static IntPtr CreateOffscreenParent()
        {
            var hwnd = CreateWindowExW(
                0,
                "STATIC",
                string.Empty,
                WsPopup | WsVisible,
                -32000,
                -32000,
                8,
                8,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);
            if (hwnd == IntPtr.Zero) throw new InvalidOperationException("Could not create paint-probe parent.");
            return hwnd;
        }

        internal static void PrintClient(
            IntPtr hwnd,
            uint color,
            out uint before,
            out uint after) =>
            _ = SendToMemorySurface(hwnd, 0x0318, new IntPtr(0x0000000C), color, out before, out after);

        internal static IntPtr EraseBackground(
            IntPtr hwnd,
            uint color,
            out uint before,
            out uint after) =>
            SendToMemorySurface(hwnd, 0x0014, IntPtr.Zero, color, out before, out after);

        private static IntPtr SendToMemorySurface(
            IntPtr hwnd,
            int message,
            IntPtr lParam,
            uint color,
            out uint before,
            out uint after)
        {
            var screen = GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) throw new InvalidOperationException("Could not acquire paint-probe screen DC.");
            var dc = CreateCompatibleDC(screen);
            var bitmap = CreateCompatibleBitmap(screen, 4, 4);
            _ = ReleaseDC(IntPtr.Zero, screen);
            if (dc == IntPtr.Zero || bitmap == IntPtr.Zero)
            {
                if (dc != IntPtr.Zero) _ = DeleteDC(dc);
                if (bitmap != IntPtr.Zero) _ = DeleteObject(bitmap);
                throw new InvalidOperationException("Could not create paint-probe memory surface.");
            }

            var previous = SelectObject(dc, bitmap);
            try
            {
                if (SetPixel(dc, 1, 1, color) == uint.MaxValue)
                {
                    throw new InvalidOperationException("Could not seed paint-probe pixel.");
                }
                before = GetPixel(dc, 1, 1);
                var result = SendMessageW(hwnd, message, dc, lParam);
                after = GetPixel(dc, 1, 1);
                return result;
            }
            finally
            {
                _ = SelectObject(dc, previous);
                _ = DeleteObject(bitmap);
                _ = DeleteDC(dc);
            }
        }

        internal static void Destroy(IntPtr hwnd)
        {
            if (hwnd != IntPtr.Zero) _ = DestroyWindow(hwnd);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowExW(
            int extendedStyle,
            string className,
            string windowName,
            int style,
            int x,
            int y,
            int width,
            int height,
            IntPtr parent,
            IntPtr menu,
            IntPtr instance,
            IntPtr parameter);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern uint SetPixel(IntPtr dc, int x, int y, uint color);

        [DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr dc, int x, int y);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateCompatibleDC(IntPtr dc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr dc, IntPtr objectHandle);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr objectHandle);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteDC(IntPtr dc);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageW(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    }
}
