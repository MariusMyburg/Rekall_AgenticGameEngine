using Rekall.Age.Runtime;
using Rekall.Age.World;

namespace Rekall.Age.Rendering;

public sealed class RekallAgeSoftwarePreview
{
    private const int Width = 160;
    private const int Height = 90;
    private readonly RekallAgeSceneStore _sceneStore;
    private readonly RekallAgeRuntimeRenderFrameBuilder _frameBuilder = new();
    private readonly RekallAgeRuntimeSoftwareRenderer _renderer = new();

    public RekallAgeSoftwarePreview(RekallAgeSceneStore sceneStore)
    {
        _sceneStore = sceneStore;
    }

    public async ValueTask<RekallAgeScreenshotResult> CaptureAsync(
        string projectRoot,
        string sceneName,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var world = await new RekallAgeRuntimeSnapshotService(
                _sceneStore,
                new RekallAgeRuntimeWorldBuilder(),
                RekallAgeRuntimeExecutionLoop.CreateDefault())
            .InspectSceneAsync(projectRoot, sceneName, 0, cancellationToken);
        var frame = _frameBuilder.Build(world, Width, Height, debugOverlay: true);
        var assets = await new RekallAgeRuntimeViewportAssetResolver().ResolveAsync(
            projectRoot,
            frame,
            cancellationToken);
        var capture = await _renderer.CaptureAsync(
            frame,
            outputDirectory,
            $"{world.SceneName}_preview.png",
            assets,
            cancellationToken);
        var visibleRenderers = capture.RenderableCount > 0
            ? capture.RenderableCount
            : world.Entities.Count(entity => entity.Components.Count > 0);
        return new RekallAgeScreenshotResult(
            capture.ScreenshotPath,
            capture.NonBlank,
            capture.Width,
            capture.Height,
            visibleRenderers,
            capture.ActiveCamera);
    }
}
