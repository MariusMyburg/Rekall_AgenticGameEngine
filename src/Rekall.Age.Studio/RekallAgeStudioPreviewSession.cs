using Rekall.Age.Rendering;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Rekall.Age.Studio;

internal sealed record RekallAgeStudioPreviewFrame(
    BitmapSource Image,
    int FrameIndex,
    int RenderableCount,
    int ObservationCount,
    string Backend,
    RekallAgeStudioViewportInteractionSnapshot Interaction);

internal interface IRekallAgeStudioPreviewSession : IAsyncDisposable
{
    ValueTask<RekallAgeStudioPreviewFrame> ResetAsync(
        string projectRoot,
        string sceneName,
        int width,
        int height,
        CancellationToken cancellationToken);

    ValueTask<RekallAgeStudioPreviewFrame> StepAsync(
        int frameCount,
        CancellationToken cancellationToken);

    ValueTask ClearAsync(CancellationToken cancellationToken);
}

internal sealed class RekallAgeStudioPreviewSession : IRekallAgeStudioPreviewSession
{
    private const int MaximumWidth = 1920;
    private const int MaximumHeight = 1080;
    private const int MaximumStepFrames = 60;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<RekallAgeRuntimeWorld, string, int, int, CancellationToken, ValueTask<RekallAgeStudioPreviewFrame>> _render;
    private RekallAgeRuntimeExecutionLoop? _loop;
    private RekallAgeRuntimeWorld? _world;
    private string? _projectRoot;
    private int _width;
    private int _height;
    private bool _disposed;

    public RekallAgeStudioPreviewSession()
    {
        _render = RenderFrameAsync;
    }

    internal RekallAgeStudioPreviewSession(
        Func<RekallAgeRuntimeWorld, string, int, int, CancellationToken, ValueTask<RekallAgeStudioPreviewFrame>> render)
    {
        _render = render ?? throw new ArgumentNullException(nameof(render));
    }

    public ValueTask<RekallAgeStudioPreviewFrame> ResetAsync(
        string projectRoot,
        string sceneName,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneName);
        if (width is < 1 or > MaximumWidth) throw new ArgumentOutOfRangeException(nameof(width));
        if (height is < 1 or > MaximumHeight) throw new ArgumentOutOfRangeException(nameof(height));

        return new ValueTask<RekallAgeStudioPreviewFrame>(Task.Run(
            () => ResetCoreAsync(projectRoot, sceneName, width, height, cancellationToken),
            cancellationToken));
    }

    private async Task<RekallAgeStudioPreviewFrame> ResetCoreAsync(
        string projectRoot,
        string sceneName,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            RekallAgeRuntimeExecutionLoop? candidateLoop = null;
            RekallAgeRuntimeWorld candidateWorld;
            RekallAgeStudioPreviewFrame candidateFrame;
            try
            {
                candidateLoop = RekallAgeRuntimeExecutionLoop.CreateDefault(projectRoot);
                candidateWorld = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
                    projectRoot,
                    sceneName,
                    0,
                    cancellationToken).ConfigureAwait(false);
                candidateWorld = await new RekallAgeUiLayoutSystem().UpdateAsync(
                    candidateWorld,
                    new RekallAgeRuntimeWorldFrameContext(
                        candidateWorld.FrameIndex,
                        TimeSpan.Zero,
                        candidateWorld.ElapsedTime,
                        cancellationToken)).ConfigureAwait(false);
                candidateWorld = new RekallAgeRuntimeProjectionBuilder().Project(candidateWorld);
                candidateFrame = await _render(
                    candidateWorld,
                    projectRoot,
                    width,
                    height,
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                candidateLoop?.Dispose();
                throw;
            }
            var previousLoop = _loop;
            _loop = candidateLoop;
            _world = candidateWorld;
            _projectRoot = projectRoot;
            _width = width;
            _height = height;
            previousLoop?.Dispose();
            return candidateFrame;
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask<RekallAgeStudioPreviewFrame> StepAsync(
        int frameCount,
        CancellationToken cancellationToken)
    {
        if (frameCount is < 1 or > MaximumStepFrames)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount));
        }

        return new ValueTask<RekallAgeStudioPreviewFrame>(Task.Run(
            () => StepCoreAsync(frameCount, cancellationToken),
            cancellationToken));
    }

    private async Task<RekallAgeStudioPreviewFrame> StepCoreAsync(
        int frameCount,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_loop is null || _world is null)
            {
                throw new InvalidOperationException("Studio preview must be reset before it can advance.");
            }
            _world = (await _loop.RunAsync(_world, frameCount, cancellationToken).ConfigureAwait(false)).World;
            return await _render(
                _world,
                _projectRoot ?? throw new InvalidOperationException("Studio preview project is unavailable."),
                _width,
                _height,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask ClearAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            _loop?.Dispose();
            _loop = null;
            _world = null;
            _projectRoot = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            _loop?.Dispose();
            _loop = null;
            _world = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async ValueTask<RekallAgeStudioPreviewFrame> RenderFrameAsync(
        RekallAgeRuntimeWorld world,
        string projectRoot,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, width, height, debugOverlay: false);
        var assets = await new RekallAgeRuntimeViewportAssetResolver().ResolveAsync(
            projectRoot,
            frame,
            cancellationToken).ConfigureAwait(false);
        var rendered = new RekallAgeRuntimeSoftwareRenderer().RenderRgba(
            frame,
            assets,
            RekallAgeInteractiveAntialiasing.DefaultSupersampleFactor);
        var bgra = new byte[rendered.Rgba.Length];
        for (var index = 0; index < rendered.Rgba.Length; index += 4)
        {
            bgra[index] = rendered.Rgba[index + 2];
            bgra[index + 1] = rendered.Rgba[index + 1];
            bgra[index + 2] = rendered.Rgba[index];
            bgra[index + 3] = rendered.Rgba[index + 3];
        }
        var image = BitmapSource.Create(
            rendered.Width,
            rendered.Height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            bgra,
            checked(rendered.Width * 4));
        image.Freeze();
        return new RekallAgeStudioPreviewFrame(
            image,
            rendered.FrameIndex,
            rendered.RenderableCount,
            frame.Observations.Count,
            "software-live",
            RekallAgeStudioViewportInteractionBuilder.Build(frame, world.Entities));
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
