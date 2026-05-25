using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.World;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeRuntimeSnapshotService
{
    private readonly RekallAgeSceneStore _sceneStore;
    private readonly RekallAgeRuntimeWorldBuilder _worldBuilder;
    private readonly RekallAgeRuntimeExecutionLoop _executionLoop;

    public RekallAgeRuntimeSnapshotService()
        : this(new RekallAgeSceneStore(), new RekallAgeRuntimeWorldBuilder(), RekallAgeRuntimeExecutionLoop.CreateDefault())
    {
    }

    public RekallAgeRuntimeSnapshotService(
        RekallAgeSceneStore sceneStore,
        RekallAgeRuntimeWorldBuilder worldBuilder,
        RekallAgeRuntimeExecutionLoop executionLoop)
    {
        _sceneStore = sceneStore;
        _worldBuilder = worldBuilder;
        _executionLoop = executionLoop;
    }

    public async ValueTask<RekallAgeRuntimeWorld> InspectSceneAsync(
        string projectRoot,
        string sceneName,
        int frames,
        CancellationToken cancellationToken)
    {
        var scene = await _sceneStore.LoadAsync(projectRoot, sceneName, cancellationToken);
        var world = _worldBuilder.Build(scene);
        if (frames <= 0)
        {
            return world;
        }

        var result = await _executionLoop.RunAsync(world, frames, cancellationToken);
        return result.World;
    }
}
