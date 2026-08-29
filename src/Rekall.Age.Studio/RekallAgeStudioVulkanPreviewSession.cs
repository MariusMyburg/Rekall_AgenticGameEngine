using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.Windows;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;
using System.IO;

namespace Rekall.Age.Studio;

internal sealed record RekallAgeStudioPreviewFrame(
    RekallAgeVulkanPresentationFrame Presentation,
    RekallAgeStudioViewportInteractionSnapshot Interaction)
{
    public int FrameIndex => Presentation.FrameIndex;

    public int RenderableCount => Presentation.RenderableCount;

    public int ObservationCount => Presentation.ObservationCount;

    public string Backend => Presentation.BackendId;

    public bool HardwareAccelerated => Presentation.HardwareAccelerated;
}

internal sealed class RekallAgeStudioVulkanPreviewSession : IRekallAgeStudioPreviewSession
{
    private const int MaximumWidth = 7680;
    private const int MaximumHeight = 4320;
    private const int MaximumStepFrames = 60;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IRekallAgeStudioViewportPresenter _presenter;
    private readonly Func<string, RekallAgeRuntimeViewportFrame, CancellationToken,
        ValueTask<RekallAgeRuntimeViewportAssetSet>> _resolveAssets;
    private readonly Func<string, IRekallAgeStudioViewportDependencyMonitor> _dependencyMonitorFactory;
    private readonly RekallAgeRuntimeRenderFrameBuilder _frameBuilder = new();
    private RekallAgeRuntimeExecutionLoop? _loop;
    private RekallAgeRuntimeWorld? _world;
    private RekallAgeRuntimeViewportAssetSet? _assets;
    private string? _projectRoot;
    private int _width;
    private int _height;
    private int _sceneRevision;
    private int _assetRevision;
    private bool _assetsDirty;
    private IRekallAgeStudioViewportDependencyMonitor? _dependencyMonitor;
    private bool _disposed;

    internal RekallAgeStudioVulkanPreviewSession(IRekallAgeStudioViewportPresenter presenter)
        : this(
            presenter,
            (projectRoot, frame, cancellationToken) =>
                new RekallAgeRuntimeViewportAssetResolver().ResolveAsync(projectRoot, frame, cancellationToken),
            projectRoot => new RekallAgeStudioViewportDependencyMonitor(projectRoot))
    {
    }

    internal RekallAgeStudioVulkanPreviewSession(
        IRekallAgeStudioViewportPresenter presenter,
        Func<string, RekallAgeRuntimeViewportFrame, CancellationToken,
            ValueTask<RekallAgeRuntimeViewportAssetSet>> resolveAssets)
        : this(
            presenter,
            resolveAssets,
            projectRoot => new RekallAgeStudioViewportDependencyMonitor(projectRoot))
    {
    }

    internal RekallAgeStudioVulkanPreviewSession(
        IRekallAgeStudioViewportPresenter presenter,
        Func<string, RekallAgeRuntimeViewportFrame, CancellationToken,
            ValueTask<RekallAgeRuntimeViewportAssetSet>> resolveAssets,
        Func<string, IRekallAgeStudioViewportDependencyMonitor> dependencyMonitorFactory)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _resolveAssets = resolveAssets ?? throw new ArgumentNullException(nameof(resolveAssets));
        _dependencyMonitorFactory = dependencyMonitorFactory
            ?? throw new ArgumentNullException(nameof(dependencyMonitorFactory));
    }

    public RekallAgeStudioViewportMetrics Metrics => _presenter.Metrics;

    public async ValueTask<RekallAgeStudioPreviewFrame> ResetAsync(
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

        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            RekallAgeRuntimeExecutionLoop? candidateLoop = null;
            IRekallAgeStudioViewportDependencyMonitor? candidateMonitor = null;
            try
            {
                var normalizedProjectRoot = Path.GetFullPath(projectRoot);
                var replaceMonitor = _dependencyMonitor is null
                    || !string.Equals(_projectRoot, normalizedProjectRoot, StringComparison.OrdinalIgnoreCase);
                if (replaceMonitor) candidateMonitor = _dependencyMonitorFactory(normalizedProjectRoot);
                else await ApplyExternalDependencyChangesAsync(cancellationToken);
                candidateLoop = RekallAgeRuntimeExecutionLoop.CreateDefault(projectRoot);
                var candidateWorld = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
                    projectRoot,
                    sceneName,
                    0,
                    cancellationToken);
                candidateWorld = await new RekallAgeUiLayoutSystem().UpdateAsync(
                    candidateWorld,
                    new RekallAgeRuntimeWorldFrameContext(
                        candidateWorld.FrameIndex,
                        TimeSpan.Zero,
                        candidateWorld.ElapsedTime,
                        cancellationToken));
                candidateWorld = new RekallAgeRuntimeProjectionBuilder().Project(candidateWorld);
                var viewportFrame = _frameBuilder.Build(candidateWorld, width, height, debugOverlay: false);
                var candidateAssets = await _resolveAssets(projectRoot, viewportFrame, cancellationToken);
                var candidateSceneRevision = checked(_sceneRevision + 1);
                var candidateAssetRevision = checked(_assetRevision + 1);
                var previewFrame = await PresentAsync(
                    candidateWorld,
                    viewportFrame,
                    candidateAssets,
                    projectRoot,
                    candidateSceneRevision,
                    candidateAssetRevision,
                    cancellationToken);
                if (!previewFrame.Presentation.PresentedFrame)
                {
                    candidateLoop.Dispose();
                    candidateMonitor?.Dispose();
                    return previewFrame;
                }

                var previousLoop = _loop;
                _loop = candidateLoop;
                candidateLoop = null;
                _world = candidateWorld;
                _assets = candidateAssets;
                _projectRoot = normalizedProjectRoot;
                _width = width;
                _height = height;
                _sceneRevision = candidateSceneRevision;
                _assetRevision = candidateAssetRevision;
                _assetsDirty = false;
                if (replaceMonitor)
                {
                    var previousMonitor = _dependencyMonitor;
                    _dependencyMonitor = candidateMonitor;
                    candidateMonitor = null;
                    previousMonitor?.Dispose();
                }
                previousLoop?.Dispose();
                return previewFrame;
            }
            catch
            {
                candidateLoop?.Dispose();
                candidateMonitor?.Dispose();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<RekallAgeStudioPreviewFrame> StepAsync(
        int frameCount,
        CancellationToken cancellationToken)
    {
        if (frameCount is < 1 or > MaximumStepFrames)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (_loop is null || _world is null || _assets is null || _projectRoot is null)
            {
                throw new InvalidOperationException("Studio preview must be reset before it can advance.");
            }

            var advancedWorld = (await _loop.RunAsync(_world, frameCount, cancellationToken)).World;
            var metrics = _presenter.Metrics;
            var width = metrics.IsPresentable ? metrics.PixelWidth : _width;
            var height = metrics.IsPresentable ? metrics.PixelHeight : _height;
            var viewportFrame = _frameBuilder.Build(advancedWorld, width, height, debugOverlay: false);
            await ApplyExternalDependencyChangesAsync(cancellationToken);
            if (_assetsDirty)
            {
                _assets = await _resolveAssets(_projectRoot, viewportFrame, cancellationToken);
                _assetRevision = checked(_assetRevision + 1);
                _assetsDirty = false;
            }

            _world = advancedWorld;
            _width = width;
            _height = height;
            return await PresentAsync(
                advancedWorld,
                viewportFrame,
                _assets,
                _projectRoot,
                _sceneRevision,
                _assetRevision,
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<RekallAgeStudioPreviewFrame> PresentCurrentAsync(
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        if (width is < 1 or > MaximumWidth) throw new ArgumentOutOfRangeException(nameof(width));
        if (height is < 1 or > MaximumHeight) throw new ArgumentOutOfRangeException(nameof(height));
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (_world is null || _assets is null || _projectRoot is null)
            {
                throw new InvalidOperationException("Studio preview must be reset before it can present.");
            }

            var viewportFrame = _frameBuilder.Build(_world, width, height, debugOverlay: false);
            await ApplyExternalDependencyChangesAsync(cancellationToken);
            if (_assetsDirty)
            {
                _assets = await _resolveAssets(_projectRoot, viewportFrame, cancellationToken);
                _assetRevision = checked(_assetRevision + 1);
                _assetsDirty = false;
            }
            _width = width;
            _height = height;
            return await PresentAsync(
                _world,
                viewportFrame,
                _assets,
                _projectRoot,
                _sceneRevision,
                _assetRevision,
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask InvalidateAssetsAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            await _presenter.InvalidateAssetsAsync(cancellationToken);
            _assetsDirty = true;
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
            await _presenter.InvalidateShadersAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask ClearAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            _loop?.Dispose();
            _loop = null;
            _world = null;
            _assets = null;
            _projectRoot = null;
            _assetsDirty = false;
            _dependencyMonitor?.Dispose();
            _dependencyMonitor = null;
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
            if (_disposed) return;
            _disposed = true;
            _loop?.Dispose();
            _loop = null;
            _world = null;
            _assets = null;
            _dependencyMonitor?.Dispose();
            _dependencyMonitor = null;
            await _presenter.DisposeAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask<RekallAgeStudioPreviewFrame> PresentAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeViewportFrame viewportFrame,
        RekallAgeRuntimeViewportAssetSet assets,
        string projectRoot,
        int sceneRevision,
        int assetRevision,
        CancellationToken cancellationToken)
    {
        var presentation = await _presenter.PresentAsync(
            viewportFrame,
            assets,
            new RekallAgeStudioPresentationContext(
                projectRoot,
                world.Subsystems.Rendering.GpuWorkloads,
                world.Entities.Count,
                sceneRevision,
                assetRevision),
            cancellationToken);
        return new RekallAgeStudioPreviewFrame(
            presentation,
            RekallAgeStudioViewportInteractionBuilder.Build(viewportFrame, world.Entities));
    }

    private async ValueTask ApplyExternalDependencyChangesAsync(CancellationToken cancellationToken)
    {
        if (_dependencyMonitor is null) return;
        var change = await _dependencyMonitor.PollAsync(cancellationToken);
        if ((change & RekallAgeStudioViewportDependencyChange.Assets) != 0)
        {
            await _presenter.InvalidateAssetsAsync(cancellationToken);
            _assetsDirty = true;
        }
        if ((change & RekallAgeStudioViewportDependencyChange.Shaders) != 0)
        {
            await _presenter.InvalidateShadersAsync(cancellationToken);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
