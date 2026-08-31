using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.Windows;

namespace Rekall.Age.Studio;

internal sealed record RekallAgeStudioMeshVulkanPreviewFrame(
    RekallAgeVulkanPresentationFrame Presentation,
    RekallAgeStudioMeshViewportInteractionSnapshot Interaction);

internal sealed class RekallAgeStudioMeshVulkanPreviewSession : IAsyncDisposable
{
    private readonly IRekallAgeStudioViewportPresenter _presenter;
    private readonly RekallAgeStudioMeshVulkanFrameBuilder _builder = new();
    private RekallAgeStudioViewportRenderStyle _style = RekallAgeStudioViewportRenderStyle.SmoothShaded;
    private bool _disposeStarted;

    public RekallAgeStudioMeshVulkanPreviewSession(IRekallAgeStudioViewportPresenter presenter) =>
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));

    public bool IsDisposalComplete => _presenter.IsDisposalComplete;

    public void SetRenderStyle(RekallAgeStudioViewportRenderStyle style) => _style = style;

    public async ValueTask<RekallAgeStudioMeshVulkanPreviewFrame> PresentAsync(
        string projectRoot,
        RekallAgeMeshAsset mesh,
        RekallAgeGeometryDomain activeDomain,
        IReadOnlyCollection<ulong> selectedIds,
        int width,
        int height,
        bool preview,
        RekallAgeStudioViewportCamera camera,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposeStarted, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var built = _builder.Build(mesh, activeDomain, selectedIds, width, height, preview, camera, _style);
        var presentation = await _presenter.PresentAsync(
            built.Frame,
            RekallAgeRuntimeViewportAssetSet.Empty,
            new RekallAgeStudioPresentationContext(projectRoot, [], 0, 1, 1, RenderStyle: _style),
            cancellationToken);
        return new(presentation, built.Interaction);
    }

    public async ValueTask<RekallAgeStudioMeshVulkanPreviewFrame> PresentEmptyAsync(
        string projectRoot,
        int width,
        int height,
        RekallAgeStudioViewportCamera camera,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposeStarted, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var built = _builder.BuildEmpty(width, height, camera);
        var presentation = await _presenter.PresentAsync(
            built.Frame,
            RekallAgeRuntimeViewportAssetSet.Empty,
            new RekallAgeStudioPresentationContext(projectRoot, [], 0, 1, 1, RenderStyle: _style),
            cancellationToken);
        return new(presentation, built.Interaction);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposeStarted && _presenter.IsDisposalComplete) return;
        _disposeStarted = true;
        await _presenter.DisposeAsync();
    }
}
