using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.Windows;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.Modules.Security;
using Serilog;
using System.IO;

namespace Rekall.Age.Studio;

internal sealed record RekallAgeStudioPreviewFrame(
    RekallAgeVulkanPresentationFrame Presentation,
    RekallAgeStudioViewportInteractionSnapshot Interaction,
    RekallAgeStudioProjectModuleDiagnostic? ProjectModuleDiagnostic = null,
    RekallAgeStudioViewportPlacementContext? PlacementContext = null)
{
    public int FrameIndex => Presentation.FrameIndex;

    public int RenderableCount => Presentation.RenderableCount;

    public int ObservationCount => Presentation.ObservationCount;

    public string Backend => Presentation.BackendId;

    public bool HardwareAccelerated => Presentation.HardwareAccelerated;
}

internal sealed record RekallAgeStudioProjectModuleDiagnostic(string Code, string Message);

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
    private RekallAgeStudioViewportRenderStyle _renderStyle = RekallAgeStudioViewportRenderStyle.Textured;
    private RekallAgeStudioProjectModuleDiagnostic? _projectModuleDiagnostic;
    private IRekallAgeStudioViewportDependencyMonitor? _dependencyMonitor;
    private bool _disposeStarted;
    private bool _disposalComplete;

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

    public void SetRenderStyle(RekallAgeStudioViewportRenderStyle style) => _renderStyle = style;

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

    public bool IsDisposalComplete => _disposalComplete;

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
                RekallAgeStudioProjectModuleDiagnostic? candidateModuleDiagnostic = null;
                try
                {
                    candidateLoop = RekallAgeRuntimeExecutionLoop.CreateDefault(projectRoot);
                }
                catch (RekallAgeModuleTrustException exception)
                {
                    // Editing and rendering a scene must not be blocked by an absent or
                    // stale module build receipt. Never execute unverified assemblies;
                    // retain the project-aware built-in systems and omit project modules.
                    Log.Warning(
                        exception,
                        "Studio preview omitted unverified project modules. ProjectRoot={ProjectRoot} Code={Code}",
                        normalizedProjectRoot,
                        exception.Code);
                    candidateLoop = RekallAgeRuntimeExecutionLoop.CreateDefaultWithoutProjectModules(
                        normalizedProjectRoot);
                    candidateModuleDiagnostic = new RekallAgeStudioProjectModuleDiagnostic(
                        exception.Code,
                        exception.Message);
                }
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
                    candidateModuleDiagnostic,
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
                _projectModuleDiagnostic = candidateModuleDiagnostic;
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
                _projectModuleDiagnostic,
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
                _projectModuleDiagnostic,
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<RekallAgeStudioPreviewFrame?> RefreshExternalDependenciesAsync(
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
                throw new InvalidOperationException("Studio preview must be reset before it can refresh dependencies.");
            }

            var change = await ApplyExternalDependencyChangesAsync(cancellationToken);
            if (change == RekallAgeStudioViewportDependencyChange.None) return null;

            var viewportFrame = _frameBuilder.Build(_world, width, height, debugOverlay: false);
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
                _projectModuleDiagnostic,
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
            _projectModuleDiagnostic = null;
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
            if (_disposalComplete) return;
            if (!_disposeStarted)
            {
                _disposeStarted = true;
                _loop?.Dispose();
                _loop = null;
                _world = null;
                _assets = null;
                _dependencyMonitor?.Dispose();
                _dependencyMonitor = null;
            }

            Exception? failure = null;
            try
            {
                await _presenter.DisposeAsync();
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            if (_presenter.IsDisposalComplete)
            {
                _disposalComplete = true;
            }
            else
            {
                failure ??= new InvalidOperationException(
                    "The Studio Vulkan presenter returned from disposal without proving terminal cleanup.");
            }

            if (failure is not null) throw failure;
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
        RekallAgeStudioProjectModuleDiagnostic? projectModuleDiagnostic,
        CancellationToken cancellationToken)
    {
        var styledFrame = RekallAgeStudioViewportStyleAdapter.Apply(viewportFrame, _renderStyle);
        var presentation = await _presenter.PresentAsync(
            styledFrame,
            assets,
            new RekallAgeStudioPresentationContext(
                projectRoot,
                world.Subsystems.Rendering.GpuWorkloads,
                world.Entities.Count,
                sceneRevision,
                assetRevision,
                RenderStyle: _renderStyle),
            cancellationToken);
        return new RekallAgeStudioPreviewFrame(
            presentation,
            RekallAgeStudioViewportInteractionBuilder.Build(viewportFrame, world.Entities),
            projectModuleDiagnostic,
            RekallAgeStudioViewportPlacementContext.From(viewportFrame.ActiveCamera));
    }

    private async ValueTask<RekallAgeStudioViewportDependencyChange> ApplyExternalDependencyChangesAsync(
        CancellationToken cancellationToken)
    {
        if (_dependencyMonitor is null) return RekallAgeStudioViewportDependencyChange.None;
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
        return change;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposeStarted, this);
}
