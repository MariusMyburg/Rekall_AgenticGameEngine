using Rekall.Age.Runtime;
using Rekall.Age.World;

namespace Rekall.Age.Rendering;

public sealed class RekallAgeOpenXrSoftwareSceneFrameRenderer
{
    public async ValueTask<RekallAgeRuntimeViewportRgbaFrame> RenderAsync(
        RekallAgeOpenXrHeadsetSoftwareSceneSubmitPlan plan,
        CancellationToken cancellationToken)
    {
        var sceneStore = new RekallAgeSceneStore();
        var worldBuilder = new RekallAgeRuntimeWorldBuilder();
        var executionLoop = RekallAgeRuntimeExecutionLoop.CreateDefault(plan.ProjectRoot);
        var world = await new RekallAgeRuntimeSnapshotService(sceneStore, worldBuilder, executionLoop)
            .InspectSceneAsync(
                plan.ProjectRoot,
                plan.SceneName,
                plan.SimulationStartFrame,
                cancellationToken)
            .ConfigureAwait(false);
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(
            world,
            plan.RenderWidth,
            plan.RenderHeight,
            plan.DebugOverlay).ForHeadsetOutput();
        var assets = await new RekallAgeRuntimeViewportAssetResolver()
            .ResolveAsync(plan.ProjectRoot, frame, cancellationToken)
            .ConfigureAwait(false);
        return new RekallAgeRuntimeSoftwareRenderer().RenderRgba(frame, assets);
    }
}
