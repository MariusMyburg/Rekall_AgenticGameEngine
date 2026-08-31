using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.Windows;
using Rekall.Age.Runtime.Abstractions;
using Serilog;

namespace Rekall.Age.Studio;

internal readonly record struct RekallAgeStudioViewportMetrics(
    double DipWidth,
    double DipHeight,
    int PixelWidth,
    int PixelHeight,
    bool IsVisible)
{
    public bool IsPresentable => IsVisible && PixelWidth > 0 && PixelHeight > 0;

    public static RekallAgeStudioViewportMetrics FromDips(
        double dipWidth,
        double dipHeight,
        double dpiScaleX,
        double dpiScaleY,
        bool isVisible)
    {
        var width = double.IsFinite(dipWidth) ? Math.Max(0, dipWidth) : 0;
        var height = double.IsFinite(dipHeight) ? Math.Max(0, dipHeight) : 0;
        var scaleX = double.IsFinite(dpiScaleX) && dpiScaleX > 0 ? dpiScaleX : 1;
        var scaleY = double.IsFinite(dpiScaleY) && dpiScaleY > 0 ? dpiScaleY : 1;
        return new RekallAgeStudioViewportMetrics(
            width,
            height,
            checked((int)Math.Round(width * scaleX, MidpointRounding.AwayFromZero)),
            checked((int)Math.Round(height * scaleY, MidpointRounding.AwayFromZero)),
            isVisible);
    }
}

internal sealed record RekallAgeStudioPresentationContext(
    string ProjectRoot,
    IReadOnlyList<RekallAgeRuntimeGpuWorkload> RuntimeGpuWorkloads,
    int RuntimeEntityCount,
    int SceneRevision,
    int AssetRevision,
    string? DebugBackendText = null,
    RekallAgeStudioViewportRenderStyle RenderStyle = RekallAgeStudioViewportRenderStyle.Textured);

internal interface IRekallAgeStudioViewportPresenter : IAsyncDisposable
{
    RekallAgeStudioViewportMetrics Metrics { get; }

    bool IsDisposalComplete { get; }

    ValueTask<RekallAgeVulkanPresentationFrame> PresentAsync(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportAssetSet assets,
        RekallAgeStudioPresentationContext context,
        CancellationToken cancellationToken);

    ValueTask InvalidateAssetsAsync(CancellationToken cancellationToken);

    ValueTask InvalidateShadersAsync(CancellationToken cancellationToken);
}

internal interface IRekallAgeVulkanViewportSurfaceController : IAsyncDisposable
{
    RekallAgeStudioViewportMetrics Metrics { get; }

    bool IsDisposed { get; }

    bool IsDisposalComplete { get; }

    void AttachSurface(IntPtr hwnd);

    ValueTask<RekallAgeStudioViewportMetrics> ResizeAsync(
        RekallAgeStudioViewportMetrics requested,
        Func<(int Width, int Height)> resizeAndReadClient,
        CancellationToken cancellationToken);

    ValueTask SuspendAsync(
        RekallAgeStudioViewportMetrics metrics,
        CancellationToken cancellationToken);
}

internal sealed class RekallAgeStudioVulkanViewportPresenter :
    IRekallAgeStudioViewportPresenter,
    IRekallAgeVulkanViewportSurfaceController
{
    internal const string UnavailableCode = "REKALL_STUDIO_VULKAN_UNAVAILABLE";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<
        RekallAgeWin32RenderSurfaceDescriptor,
        RekallAgeVulkanPresentationOptions,
        RekallAgeRuntimeViewportFrame,
        RekallAgeRuntimeViewportAssetSet,
        int,
        IRekallAgeVulkanPresentationSession> _sessionFactory;
    private RekallAgeWin32RenderSurface? _surface;
    private IRekallAgeVulkanPresentationSession? _session;
    private RekallAgeStudioViewportMetrics _metrics;
    private string? _sessionProjectRoot;
    private RekallAgeStudioViewportRenderStyle _sessionRenderStyle;
    private bool _assetsInvalidated;
    private bool _shadersInvalidated;
    private bool _sessionCleanupRequired;
    private bool _disposeStarted;
    private bool _disposalComplete;

    internal RekallAgeStudioVulkanViewportPresenter()
        : this((surface, options, frame, assets, assetRevision) =>
            new RekallAgeVeldridVulkanPresentationSession(
                surface,
                options,
                frame,
                assets,
                assetRevision))
    {
    }

    internal RekallAgeStudioVulkanViewportPresenter(
        Func<
            RekallAgeWin32RenderSurfaceDescriptor,
            RekallAgeVulkanPresentationOptions,
            RekallAgeRuntimeViewportFrame,
            RekallAgeRuntimeViewportAssetSet,
            int,
            IRekallAgeVulkanPresentationSession> sessionFactory)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
    }

    public RekallAgeStudioViewportMetrics Metrics => _metrics;

    public bool IsDisposed => _disposalComplete;

    public bool IsDisposalComplete => _disposalComplete;

    public void AttachSurface(IntPtr hwnd)
    {
        ThrowIfDisposalStarted();
        if (_surface is not null)
        {
            throw new InvalidOperationException("The Studio Vulkan presenter already has a child surface.");
        }

        _surface = RekallAgeWin32RenderSurface.CreateExternal(hwnd);
    }

    public async ValueTask<RekallAgeStudioViewportMetrics> ResizeAsync(
        RekallAgeStudioViewportMetrics requested,
        Func<(int Width, int Height)> resizeAndReadClient,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resizeAndReadClient);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (!requested.IsPresentable)
            {
                _metrics = requested;
                return _metrics;
            }

            var verified = resizeAndReadClient();
            _metrics = requested with
            {
                PixelWidth = Math.Max(0, verified.Width),
                PixelHeight = Math.Max(0, verified.Height)
            };
            if (!_metrics.IsPresentable)
            {
                return _metrics;
            }

            _session?.Resize(_metrics.PixelWidth, _metrics.PixelHeight);
            return _metrics;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask SuspendAsync(
        RekallAgeStudioViewportMetrics metrics,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            _metrics = metrics with { IsVisible = false };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<RekallAgeVulkanPresentationFrame> PresentAsync(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportAssetSet assets,
        RekallAgeStudioPresentationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(context);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (_surface is null || !_metrics.IsPresentable)
            {
                return Unavailable(frame, "The Studio Vulkan child surface is not ready.");
            }

            if (frame.Width != _metrics.PixelWidth || frame.Height != _metrics.PixelHeight)
            {
                return Unavailable(
                    frame,
                    $"The Studio Vulkan surface resized from {frame.Width}x{frame.Height} to "
                    + $"{_metrics.PixelWidth}x{_metrics.PixelHeight} before presentation.");
            }

            if (_sessionCleanupRequired)
            {
                try
                {
                    await DisposeSessionAsync();
                }
                catch (Exception cleanupException) when (_session is not null)
                {
                    return Unavailable(
                        frame,
                        "The previous Vulkan session has not completed native cleanup.",
                        cleanupException);
                }
                catch (Exception cleanupException)
                {
                    Log.Warning(cleanupException, "Abandoned Studio Vulkan session completed cleanup with diagnostics.");
                }
            }

            try
            {
                if (_session is null
                    || !string.Equals(_sessionProjectRoot, context.ProjectRoot, StringComparison.OrdinalIgnoreCase)
                    || _sessionRenderStyle != context.RenderStyle)
                {
                    await DisposeSessionAsync();
                    var descriptor = _surface.Describe(_metrics.PixelWidth, _metrics.PixelHeight);
                    _session = _sessionFactory(
                        descriptor,
                        new RekallAgeVulkanPresentationOptions(
                            context.ProjectRoot,
                            SyncToVerticalBlank: true,
                            SceneSupersampleFactor: RekallAgeInteractiveAntialiasing.DefaultSupersampleFactor,
                            DebugHudEnabled: false,
                            RenderStyle: MapRenderStyle(context.RenderStyle)),
                        frame,
                        assets,
                        context.AssetRevision);
                    _sessionProjectRoot = context.ProjectRoot;
                    _sessionRenderStyle = context.RenderStyle;
                    _assetsInvalidated = false;
                    _shadersInvalidated = false;
                }
                else
                {
                    if (_assetsInvalidated)
                    {
                        await _session.InvalidateAssetsAsync(cancellationToken);
                        _assetsInvalidated = false;
                    }

                    if (_shadersInvalidated)
                    {
                        await _session.InvalidateShadersAsync(cancellationToken);
                        _shadersInvalidated = false;
                    }
                }

                return await _session.PresentAsync(
                    new RekallAgeVulkanSceneSubmission(
                        frame,
                        assets,
                        context.RuntimeGpuWorkloads,
                        context.RuntimeEntityCount,
                        context.SceneRevision,
                        context.AssetRevision,
                        context.DebugBackendText),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Studio Vulkan presentation session became unavailable.");
                Exception diagnostic = exception;
                try
                {
                    await DisposeSessionAsync();
                }
                catch (Exception cleanupException)
                {
                    diagnostic = new AggregateException(
                        "Vulkan presentation failed and its abandoned session reported a cleanup failure.",
                        exception,
                        cleanupException);
                }
                return Unavailable(frame, exception.Message, diagnostic);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static RekallAgeVulkanPresentationRenderStyle MapRenderStyle(RekallAgeStudioViewportRenderStyle style) => style switch
    {
        RekallAgeStudioViewportRenderStyle.SmoothShaded => RekallAgeVulkanPresentationRenderStyle.SmoothShaded,
        RekallAgeStudioViewportRenderStyle.FlatShaded => RekallAgeVulkanPresentationRenderStyle.FlatShaded,
        RekallAgeStudioViewportRenderStyle.Wireframe => RekallAgeVulkanPresentationRenderStyle.Wireframe,
        RekallAgeStudioViewportRenderStyle.Clay => RekallAgeVulkanPresentationRenderStyle.Clay,
        _ => RekallAgeVulkanPresentationRenderStyle.Textured
    };

    public async ValueTask InvalidateAssetsAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            _assetsInvalidated = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask InvalidateShadersAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            _shadersInvalidated = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_disposalComplete) return;
            _disposeStarted = true;
            List<Exception>? failures = null;
            try
            {
                await DisposeSessionAsync();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }

            if (_session is null && _surface is not null)
            {
                try
                {
                    _surface.Dispose();
                    _surface = null;
                }
                catch (Exception exception)
                {
                    (failures ??= []).Add(exception);
                }
            }

            _disposalComplete = _session is null && _surface is null;
            if (failures is { Count: > 0 })
            {
                throw new AggregateException(
                    "Studio Vulkan presenter cleanup reported one or more failures.",
                    failures);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static RekallAgeVulkanPresentationFrame Unavailable(
        RekallAgeRuntimeViewportFrame frame,
        string reason,
        Exception? exception = null) =>
        RekallAgeVulkanPresentationFrame.Unavailable(
            frame,
            string.IsNullOrWhiteSpace(reason) ? "Vulkan presentation failed." : reason,
            exception is null
                ? [UnavailableCode]
                : [UnavailableCode, $"{exception.GetType().Name}: {exception.Message}"]);

    private async ValueTask DisposeSessionAsync()
    {
        if (_session is null) return;
        var session = _session;
        Exception? failure = null;
        try
        {
            await session.DisposeAsync();
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        bool completed;
        try
        {
            completed = session.IsDisposalComplete;
        }
        catch (Exception exception)
        {
            completed = false;
            failure = failure is null
                ? exception
                : new AggregateException("Vulkan session cleanup state could not be established.", failure, exception);
        }

        if (completed)
        {
            _session = null;
            _sessionProjectRoot = null;
            _sessionCleanupRequired = false;
        }
        else
        {
            _sessionCleanupRequired = true;
            failure ??= new InvalidOperationException(
                "Vulkan presentation session returned from disposal without proving terminal native cleanup.");
        }

        if (failure is not null)
        {
            throw failure;
        }
    }

    private void ThrowIfDisposed() => ThrowIfDisposalStarted();

    private void ThrowIfDisposalStarted() => ObjectDisposedException.ThrowIf(_disposeStarted, this);
}

internal enum RekallAgeStudioViewportPointerKind
{
    Move,
    Down,
    Up,
    Wheel,
    FocusGained,
    FocusLost,
    CaptureLost
}

internal enum RekallAgeStudioViewportPointerButton
{
    None,
    Left
}

[Flags]
internal enum RekallAgeStudioViewportPointerModifiers
{
    None = 0,
    LeftButton = 1,
    Shift = 2,
    Control = 4
}

internal sealed record RekallAgeStudioViewportPointerFact(
    RekallAgeStudioViewportPointerKind Kind,
    double DisplayX,
    double DisplayY,
    RekallAgeStudioViewportPointerButton Button,
    int WheelDelta,
    RekallAgeStudioViewportPointerModifiers Modifiers);

internal readonly record struct RekallAgeNativeViewportRegion(
    IntPtr Parent,
    int X,
    int Y,
    int Width,
    int Height)
{
    internal RekallAgeNativeViewportRegion Union(RekallAgeNativeViewportRegion other)
    {
        if (Parent == IntPtr.Zero) return other;
        if (other.Parent == IntPtr.Zero || Parent != other.Parent) return this;
        var left = Math.Min(X, other.X);
        var top = Math.Min(Y, other.Y);
        var right = Math.Max(X + Width, other.X + other.Width);
        var bottom = Math.Max(Y + Height, other.Y + other.Height);
        return new(Parent, left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}

internal interface IRekallAgeVulkanViewportNativeWindow
{
    IntPtr CreateChild(IntPtr parent);

    void AttachMessageHandler(
        IntPtr hwnd,
        Func<int, IntPtr, IntPtr, bool> messageHandler)
    {
    }

    void DetachMessageHandler(IntPtr hwnd)
    {
    }

    void DestroyChild(IntPtr hwnd);

    void ResizeChild(IntPtr hwnd, int width, int height);

    void SetVisible(IntPtr hwnd, bool visible)
    {
    }

    RekallAgeNativeViewportRegion CaptureParentSurfaceRegion(IntPtr hwnd) => default;

    void InvalidateParentSurface(RekallAgeNativeViewportRegion region) { }

    (int Width, int Height) GetClientSize(IntPtr hwnd);

    (int X, int Y) ScreenToClient(IntPtr hwnd, int x, int y);

    void Focus(IntPtr hwnd);

    void Capture(IntPtr hwnd);

    void ReleaseCapture();

    (double ScaleX, double ScaleY) GetDpiScale(IntPtr hwnd) => (1, 1);
}

internal sealed class RekallAgeVulkanViewportHostCore
{
    internal const int WmSize = 0x0005;
    internal const int WmSetFocus = 0x0007;
    internal const int WmKillFocus = 0x0008;
    internal const int WmMouseMove = 0x0200;
    internal const int WmLeftButtonDown = 0x0201;
    internal const int WmLeftButtonUp = 0x0202;
    internal const int WmMouseWheel = 0x020A;
    internal const int WmCaptureChanged = 0x0215;
    internal const int WmDpiChanged = 0x02E0;

    private readonly IRekallAgeVulkanViewportNativeWindow _native;
    private readonly IRekallAgeVulkanViewportSurfaceController _surface;
    private readonly object _resizeSync = new();
    private IntPtr _child;
    private RekallAgeStudioViewportMetrics? _pendingMetrics;
    private bool _messageHandlerAttached;
    private bool _presentationVisible = true;
    private bool _nativeChildVisible;
    private bool _destroyed;

    internal RekallAgeVulkanViewportHostCore(
        IRekallAgeVulkanViewportNativeWindow native,
        IRekallAgeVulkanViewportSurfaceController surface)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
    }

    internal event EventHandler<RekallAgeStudioViewportPointerFact>? PointerFact;

    internal event EventHandler<RekallAgeStudioViewportMetrics>? MetricsChanged;

    internal RekallAgeStudioViewportMetrics Metrics => _surface.Metrics;

    internal bool HasPointerCapture { get; private set; }

    internal IntPtr BuildWindow(IntPtr parent, bool attachMessageHandler = true)
    {
        if (_child != IntPtr.Zero)
        {
            throw new InvalidOperationException("The Studio Vulkan child HWND was already created.");
        }

        _child = _native.CreateChild(parent);
        if (_child == IntPtr.Zero)
        {
            throw new InvalidOperationException("The Studio Vulkan child HWND could not be created.");
        }

        _native.SetVisible(_child, false);
        if (attachMessageHandler) AttachWindowMessageHandler();
        _surface.AttachSurface(_child);
        return _child;
    }

    internal void AttachWindowMessageHandler()
    {
        if (_child == IntPtr.Zero || _destroyed || _messageHandlerAttached) return;
        _native.AttachMessageHandler(_child, ProcessWindowMessage);
        _messageHandlerAttached = true;
    }

    internal void QueueResize(
        double dipWidth,
        double dipHeight,
        double dpiScaleX,
        double dpiScaleY,
        bool isVisible)
    {
        var metrics = RekallAgeStudioViewportMetrics.FromDips(
            dipWidth,
            dipHeight,
            dpiScaleX,
            dpiScaleY,
            isVisible);
        lock (_resizeSync)
        {
            _pendingMetrics = metrics;
        }
    }

    internal async ValueTask ApplyPendingResizeAsync(CancellationToken cancellationToken)
    {
        RekallAgeStudioViewportMetrics? pending;
        lock (_resizeSync)
        {
            pending = _pendingMetrics;
            _pendingMetrics = null;
        }

        if (pending is not { } requested || _child == IntPtr.Zero || _destroyed) return;
        var oldRegion = _native.CaptureParentSurfaceRegion(_child);
        var requestedVisible = requested.IsPresentable && _presentationVisible;
        if (_nativeChildVisible != requestedVisible)
        {
            _native.SetVisible(_child, requestedVisible);
            _nativeChildVisible = requestedVisible;
        }
        RekallAgeStudioViewportMetrics coherent;
        if (!requested.IsPresentable)
        {
            await _surface.SuspendAsync(requested, cancellationToken);
            coherent = _surface.Metrics;
        }
        else
        {
            coherent = await _surface.ResizeAsync(
                requested,
                () =>
                {
                    _native.ResizeChild(_child, requested.PixelWidth, requested.PixelHeight);
                    return _native.GetClientSize(_child);
                },
                cancellationToken);
        }

        var newRegion = _native.CaptureParentSurfaceRegion(_child);
        _native.InvalidateParentSurface(oldRegion.Union(newRegion));

        MetricsChanged?.Invoke(this, coherent);
    }

    internal void SetPresentationVisible(bool visible)
    {
        if (_presentationVisible == visible) return;
        _presentationVisible = visible;
        if (_child == IntPtr.Zero || _destroyed) return;
        var requestedVisible = visible && Metrics.IsPresentable;
        if (_nativeChildVisible == requestedVisible) return;
        var oldRegion = _native.CaptureParentSurfaceRegion(_child);
        _native.SetVisible(_child, requestedVisible);
        _nativeChildVisible = requestedVisible;
        var newRegion = _native.CaptureParentSurfaceRegion(_child);
        _native.InvalidateParentSurface(oldRegion.Union(newRegion));
    }

    internal bool ProcessWindowMessage(int message, IntPtr wParam, IntPtr lParam)
    {
        if (_child == IntPtr.Zero || _destroyed) return false;
        switch (message)
        {
            case WmMouseMove:
                Emit(RekallAgeStudioViewportPointerKind.Move, ClientPoint(lParam),
                    RekallAgeStudioViewportPointerButton.None, 0, Modifiers(wParam));
                return true;
            case WmLeftButtonDown:
                _native.Focus(_child);
                Emit(RekallAgeStudioViewportPointerKind.Down, ClientPoint(lParam),
                    RekallAgeStudioViewportPointerButton.Left, 0, Modifiers(wParam));
                return true;
            case WmLeftButtonUp:
                Emit(RekallAgeStudioViewportPointerKind.Up, ClientPoint(lParam),
                    RekallAgeStudioViewportPointerButton.Left, 0, Modifiers(wParam));
                ReleasePointerCapture(releaseNative: true);
                return true;
            case WmMouseWheel:
                var screen = DecodePoint(lParam);
                var client = _native.ScreenToClient(_child, screen.X, screen.Y);
                Emit(RekallAgeStudioViewportPointerKind.Wheel, client,
                    RekallAgeStudioViewportPointerButton.None, SignedHighWord(wParam), Modifiers(wParam));
                return true;
            case WmSetFocus:
                Emit(RekallAgeStudioViewportPointerKind.FocusGained, (0, 0),
                    RekallAgeStudioViewportPointerButton.None, 0, RekallAgeStudioViewportPointerModifiers.None);
                return true;
            case WmKillFocus:
                ReleasePointerCapture(releaseNative: true);
                Emit(RekallAgeStudioViewportPointerKind.FocusLost, (0, 0),
                    RekallAgeStudioViewportPointerButton.None, 0, RekallAgeStudioViewportPointerModifiers.None);
                return true;
            case WmCaptureChanged:
                ReleasePointerCapture(releaseNative: false);
                Emit(RekallAgeStudioViewportPointerKind.CaptureLost, (0, 0),
                    RekallAgeStudioViewportPointerButton.None, 0, RekallAgeStudioViewportPointerModifiers.None);
                return true;
            default:
                return false;
        }
    }

    internal void CapturePointer()
    {
        if (_child == IntPtr.Zero || _destroyed || HasPointerCapture) return;
        _native.Capture(_child);
        HasPointerCapture = true;
    }

    internal ValueTask DisposePresenterAsync() => _surface.DisposeAsync();

    internal void DestroyWindow(IntPtr hwnd)
    {
        if (_destroyed || _child == IntPtr.Zero) return;
        if (hwnd != _child)
        {
            throw new InvalidOperationException("WPF requested destruction of an unexpected Studio Vulkan HWND.");
        }

        if (!_surface.IsDisposalComplete)
        {
            throw new InvalidOperationException(
                "The Studio Vulkan presenter must be drained and disposed before its child HWND is destroyed.");
        }

        var exposedRegion = _native.CaptureParentSurfaceRegion(_child);
        ReleasePointerCapture(releaseNative: true);
        if (_messageHandlerAttached)
        {
            _native.DetachMessageHandler(_child);
            _messageHandlerAttached = false;
        }
        _native.DestroyChild(_child);
        _destroyed = true;
        _child = IntPtr.Zero;
        _nativeChildVisible = false;
        _native.InvalidateParentSurface(exposedRegion);
    }

    private void Emit(
        RekallAgeStudioViewportPointerKind kind,
        (int X, int Y) physicalPoint,
        RekallAgeStudioViewportPointerButton button,
        int wheelDelta,
        RekallAgeStudioViewportPointerModifiers modifiers)
    {
        var metrics = Metrics;
        var scaleX = metrics.DipWidth > 0 && metrics.PixelWidth > 0
            ? metrics.PixelWidth / metrics.DipWidth
            : 1;
        var scaleY = metrics.DipHeight > 0 && metrics.PixelHeight > 0
            ? metrics.PixelHeight / metrics.DipHeight
            : 1;
        PointerFact?.Invoke(this, new RekallAgeStudioViewportPointerFact(
            kind,
            physicalPoint.X / scaleX,
            physicalPoint.Y / scaleY,
            button,
            wheelDelta,
            modifiers));
    }

    private static (int X, int Y) ClientPoint(IntPtr lParam) => DecodePoint(lParam);

    private static (int X, int Y) DecodePoint(IntPtr value)
    {
        var bits = unchecked((uint)value.ToInt64());
        return (unchecked((short)(bits & 0xFFFF)), unchecked((short)((bits >> 16) & 0xFFFF)));
    }

    private static int SignedHighWord(IntPtr value) =>
        unchecked((short)((unchecked((uint)value.ToInt64()) >> 16) & 0xFFFF));

    private static RekallAgeStudioViewportPointerModifiers Modifiers(IntPtr wParam)
    {
        var keys = unchecked((ushort)(unchecked((uint)wParam.ToInt64()) & 0xFFFF));
        var result = RekallAgeStudioViewportPointerModifiers.None;
        if ((keys & 0x0001) != 0) result |= RekallAgeStudioViewportPointerModifiers.LeftButton;
        if ((keys & 0x0004) != 0) result |= RekallAgeStudioViewportPointerModifiers.Shift;
        if ((keys & 0x0008) != 0) result |= RekallAgeStudioViewportPointerModifiers.Control;
        return result;
    }

    private void ReleasePointerCapture(bool releaseNative)
    {
        if (!HasPointerCapture) return;
        HasPointerCapture = false;
        if (releaseNative) _native.ReleaseCapture();
    }
}

internal sealed class RekallAgeVulkanViewportHost : HwndHost, IRekallAgeStudioViewportPresenter
{
    private readonly RekallAgeStudioVulkanViewportPresenter _presenter;
    private readonly RekallAgeVulkanViewportHostCore _core;
    private readonly TaskCompletionSource<RekallAgeStudioViewportMetrics> _surfaceReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _resizeScheduled;

    public RekallAgeVulkanViewportHost()
        : this(new RekallAgeWin32VulkanViewportNativeWindow(), new RekallAgeStudioVulkanViewportPresenter())
    {
    }

    internal RekallAgeVulkanViewportHost(
        IRekallAgeVulkanViewportNativeWindow native,
        RekallAgeStudioVulkanViewportPresenter presenter)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _core = new RekallAgeVulkanViewportHostCore(native, presenter);
        _core.PointerFact += (_, fact) => PointerFact?.Invoke(this, fact);
        _core.MetricsChanged += (_, metrics) =>
        {
            MetricsChanged?.Invoke(this, metrics);
            if (metrics.IsPresentable) _surfaceReady.TrySetResult(metrics);
        };
        IsVisibleChanged += (_, _) => ScheduleResize();
    }

    internal event EventHandler<RekallAgeStudioViewportPointerFact>? PointerFact;

    internal event EventHandler<RekallAgeStudioViewportMetrics>? MetricsChanged;

    public RekallAgeStudioViewportMetrics Metrics => _presenter.Metrics;

    public bool IsDisposalComplete => _presenter.IsDisposalComplete;

    internal Task<RekallAgeStudioViewportMetrics> WaitForSurfaceReadyAsync(CancellationToken cancellationToken) =>
        _surfaceReady.Task.WaitAsync(cancellationToken);

    internal void CapturePointer() => _core.CapturePointer();

    internal void SetPresentationVisible(bool visible) => _core.SetPresentationVisible(visible);

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        // HwndHost installs its own native plumbing after BuildWindowCore returns. Hook the
        // final child procedure on the dispatcher so real mouse messages reach the core.
        var child = _core.BuildWindow(hwndParent.Handle, attachMessageHandler: false);
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(_core.AttachWindowMessageHandler));
        ScheduleResize();
        return new HandleRef(this, child);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        _core.DestroyWindow(hwnd.Handle);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        ScheduleResize();
    }

    protected override IntPtr WndProc(
        IntPtr hwnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (msg is RekallAgeVulkanViewportHostCore.WmSize or RekallAgeVulkanViewportHostCore.WmDpiChanged)
        {
            ScheduleResize();
        }

        return IntPtr.Zero;
    }

    public ValueTask<RekallAgeVulkanPresentationFrame> PresentAsync(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportAssetSet assets,
        RekallAgeStudioPresentationContext context,
        CancellationToken cancellationToken) =>
        _presenter.PresentAsync(frame, assets, context, cancellationToken);

    public ValueTask InvalidateAssetsAsync(CancellationToken cancellationToken) =>
        _presenter.InvalidateAssetsAsync(cancellationToken);

    public ValueTask InvalidateShadersAsync(CancellationToken cancellationToken) =>
        _presenter.InvalidateShadersAsync(cancellationToken);

    public ValueTask DisposeAsync() => _core.DisposePresenterAsync();

    private void ScheduleResize()
    {
        if (_resizeScheduled) return;
        _resizeScheduled = true;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(async () =>
        {
            _resizeScheduled = false;
            if (!IsLoaded && !IsVisible) return;
            var dpi = VisualTreeHelper.GetDpi(this);
            _core.QueueResize(ActualWidth, ActualHeight, dpi.DpiScaleX, dpi.DpiScaleY, IsVisible);
            try
            {
                await _core.ApplyPendingResizeAsync(CancellationToken.None);
            }
            catch (ObjectDisposedException)
            {
            }
        }));
    }
}

internal sealed class RekallAgeWin32VulkanViewportNativeWindow : IRekallAgeVulkanViewportNativeWindow
{
    private const string VulkanChildWindowClassName = "RekallAgeStudioVulkanViewportWindow";
    private const int WmPaint = 0x000F;
    private const int WmEraseBackground = 0x0014;
    private const int GwlpWndProc = -4;
    private const int WsChild = 0x40000000;
    private const int WsClipChildren = 0x02000000;
    private const int WsClipSiblings = 0x04000000;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpHideWindow = 0x0080;
    private static readonly IntPtr ModuleHandle = GetModuleHandleW(null);
    private static readonly WindowProcedure VulkanChildWindowProcedure = ProcessVulkanChildWindowMessage;
    private static readonly Lazy<ushort> VulkanChildWindowClass = new(RegisterVulkanChildWindowClass);
    private WindowProcedure? _windowProcedure;
    private Func<int, IntPtr, IntPtr, bool>? _messageHandler;
    private IntPtr _subclassedHwnd;
    private IntPtr _originalWindowProcedure;

    public IntPtr CreateChild(IntPtr parent)
    {
        _ = VulkanChildWindowClass.Value;
        var child = CreateWindowExW(
            0,
            VulkanChildWindowClassName,
            string.Empty,
            WsChild | WsClipChildren | WsClipSiblings,
            0,
            0,
            1,
            1,
            parent,
            IntPtr.Zero,
            ModuleHandle,
            IntPtr.Zero);
        if (child == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        return child;
    }

    public void AttachMessageHandler(
        IntPtr hwnd,
        Func<int, IntPtr, IntPtr, bool> messageHandler)
    {
        ArgumentNullException.ThrowIfNull(messageHandler);
        if (_subclassedHwnd != IntPtr.Zero)
        {
            throw new InvalidOperationException("The Studio Vulkan child HWND already has a message handler.");
        }

        _messageHandler = messageHandler;
        _windowProcedure = ForwardWindowMessage;
        Marshal.SetLastPInvokeError(0);
        _originalWindowProcedure = SetWindowProcedure(
            hwnd,
            Marshal.GetFunctionPointerForDelegate(_windowProcedure));
        if (_originalWindowProcedure == IntPtr.Zero && Marshal.GetLastPInvokeError() != 0)
        {
            _messageHandler = null;
            _windowProcedure = null;
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        _subclassedHwnd = hwnd;
    }

    public void DetachMessageHandler(IntPtr hwnd)
    {
        if (_subclassedHwnd == IntPtr.Zero) return;
        if (hwnd != _subclassedHwnd)
        {
            throw new InvalidOperationException("Cannot detach the Studio Vulkan handler from an unexpected HWND.");
        }

        if (_originalWindowProcedure != IntPtr.Zero)
        {
            Marshal.SetLastPInvokeError(0);
            var result = SetWindowProcedure(hwnd, _originalWindowProcedure);
            if (result == IntPtr.Zero && Marshal.GetLastPInvokeError() != 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }

        _subclassedHwnd = IntPtr.Zero;
        _originalWindowProcedure = IntPtr.Zero;
        _messageHandler = null;
        _windowProcedure = null;
    }

    public void DestroyChild(IntPtr hwnd)
    {
        if (!DestroyWindow(hwnd)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void ResizeChild(IntPtr hwnd, int width, int height)
    {
        if (!SetWindowPos(hwnd, IntPtr.Zero, 0, 0, width, height, SwpNoActivate | SwpNoZOrder))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public void SetVisible(IntPtr hwnd, bool visible)
    {
        var flags = SwpNoActivate | SwpNoZOrder | SwpNoMove | SwpNoSize
            | (visible ? SwpShowWindow : SwpHideWindow);
        if (!SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, flags))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public RekallAgeNativeViewportRegion CaptureParentSurfaceRegion(IntPtr hwnd)
    {
        var parent = GetParent(hwnd);
        if (parent == IntPtr.Zero || !GetWindowRect(hwnd, out var bounds)) return default;
        var topLeft = new Point(bounds.Left, bounds.Top);
        var bottomRight = new Point(bounds.Right, bounds.Bottom);
        _ = MapWindowPoints(IntPtr.Zero, parent, ref topLeft, 1);
        _ = MapWindowPoints(IntPtr.Zero, parent, ref bottomRight, 1);
        return new(
            parent,
            topLeft.X,
            topLeft.Y,
            Math.Max(0, bottomRight.X - topLeft.X),
            Math.Max(0, bottomRight.Y - topLeft.Y));
    }

    public void InvalidateParentSurface(RekallAgeNativeViewportRegion region)
    {
        if (region.Parent == IntPtr.Zero || region.Width <= 0 || region.Height <= 0) return;
        var bounds = new Rect
        {
            Left = region.X,
            Top = region.Y,
            Right = region.X + region.Width,
            Bottom = region.Y + region.Height
        };
        if (!InvalidateRect(region.Parent, ref bounds, erase: false))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public (int Width, int Height) GetClientSize(IntPtr hwnd)
    {
        if (!GetClientRect(hwnd, out var rect)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return (Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top));
    }

    public (int X, int Y) ScreenToClient(IntPtr hwnd, int x, int y)
    {
        var point = new Point(x, y);
        if (!ScreenToClient(hwnd, ref point)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return (point.X, point.Y);
    }

    public void Focus(IntPtr hwnd) => SetFocus(hwnd);

    public void Capture(IntPtr hwnd) => SetCapture(hwnd);

    public void ReleaseCapture() => ReleaseCaptureNative();

    public (double ScaleX, double ScaleY) GetDpiScale(IntPtr hwnd)
    {
        var dpi = GetDpiForWindow(hwnd);
        var scale = dpi > 0 ? dpi / 96d : 1;
        return (scale, scale);
    }

    private IntPtr ForwardWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam)
    {
        if (_messageHandler?.Invoke(message, wParam, lParam) == true)
        {
            return IntPtr.Zero;
        }

        return _originalWindowProcedure == IntPtr.Zero
            ? DefWindowProcW(hwnd, message, wParam, lParam)
            : CallWindowProcW(_originalWindowProcedure, hwnd, message, wParam, lParam);
    }

    private static ushort RegisterVulkanChildWindowClass()
    {
        if (ModuleHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var description = new WindowClassEx
        {
            Size = checked((uint)Marshal.SizeOf<WindowClassEx>()),
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(VulkanChildWindowProcedure),
            Instance = ModuleHandle,
            BackgroundBrush = IntPtr.Zero,
            ClassName = VulkanChildWindowClassName
        };
        var atom = RegisterClassExW(ref description);
        if (atom == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        return atom;
    }

    private static IntPtr ProcessVulkanChildWindowMessage(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam)
    {
        if (message == WmEraseBackground)
        {
            return new IntPtr(1);
        }

        if (message == WmPaint)
        {
            _ = BeginPaint(hwnd, out var paint);
            _ = EndPaint(hwnd, ref paint);
            return IntPtr.Zero;
        }

        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private static IntPtr SetWindowProcedure(IntPtr hwnd, IntPtr windowProcedure) =>
        IntPtr.Size == 8
            ? SetWindowLongPtrW(hwnd, GwlpWndProc, windowProcedure)
            : new IntPtr(SetWindowLongW(hwnd, GwlpWndProc, windowProcedure.ToInt32()));

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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WindowClassEx windowClass);

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr hwnd, out PaintStruct paint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndPaint(IntPtr hwnd, ref PaintStruct paint);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hwnd, int index, IntPtr newValue);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLongW(IntPtr hwnd, int index, int newValue);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CallWindowProcW(
        IntPtr previousWindowProcedure,
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int MapWindowPoints(IntPtr from, IntPtr to, ref Point point, uint pointCount);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InvalidateRect(IntPtr hwnd, ref Rect rect, bool erase);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr hwnd, ref Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCapture(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "ReleaseCapture")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCaptureNative();

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point(int x, int y)
    {
        internal int X = x;
        internal int Y = y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClassEx
    {
        internal uint Size;
        internal uint Style;
        internal IntPtr WindowProcedure;
        internal int ClassExtraBytes;
        internal int WindowExtraBytes;
        internal IntPtr Instance;
        internal IntPtr Icon;
        internal IntPtr Cursor;
        internal IntPtr BackgroundBrush;
        internal string? MenuName;
        internal string ClassName;
        internal IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintStruct
    {
        internal IntPtr DeviceContext;
        internal int Erase;
        internal Rect PaintRectangle;
        internal int Restore;
        internal int IncrementalUpdate;
        internal uint Reserved0;
        internal uint Reserved1;
        internal uint Reserved2;
        internal uint Reserved3;
        internal uint Reserved4;
        internal uint Reserved5;
        internal uint Reserved6;
        internal uint Reserved7;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WindowProcedure(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam);
}
