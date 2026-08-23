using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.World;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeRuntimeSnapshotService
{
    private readonly RekallAgeSceneStore _sceneStore;
    private readonly RekallAgeRuntimeWorldBuilder _worldBuilder;
    private readonly RekallAgeRuntimeExecutionLoop? _executionLoop;

    public RekallAgeRuntimeSnapshotService()
        : this(new RekallAgeSceneStore(), new RekallAgeRuntimeWorldBuilder(), null)
    {
    }

    public RekallAgeRuntimeSnapshotService(
        RekallAgeSceneStore sceneStore,
        RekallAgeRuntimeWorldBuilder worldBuilder,
        RekallAgeRuntimeExecutionLoop? executionLoop)
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
        return await InspectSceneAsync(
            projectRoot,
            sceneName,
            frames,
            null,
            cancellationToken);
    }

    public async ValueTask<RekallAgeRuntimeWorld> InspectSceneAsync(
        string projectRoot,
        string sceneName,
        int frames,
        IReadOnlyList<RekallAgeRuntimeInputFrame>? inputs,
        CancellationToken cancellationToken)
    {
        return await InspectSceneTimelineAsync(
            projectRoot,
            sceneName,
            frames,
            inputs,
            null,
            cancellationToken);
    }

    public async ValueTask<RekallAgeRuntimeWorld> InspectSceneTimelineAsync(
        string projectRoot,
        string sceneName,
        int frames,
        IReadOnlyList<RekallAgeRuntimeInputFrame>? inputs,
        Action<RekallAgeRuntimeWorld>? observeFrame,
        CancellationToken cancellationToken)
    {
        var scene = await _sceneStore.LoadAsync(projectRoot, sceneName, cancellationToken);
        var world = _worldBuilder.Build(scene, projectRoot);
        if (frames <= 0)
        {
            return world;
        }

        var ownsExecutionLoop = _executionLoop is null;
        var executionLoop = _executionLoop ?? RekallAgeRuntimeExecutionLoop.CreateDefault(projectRoot);
        try
        {
            for (var frame = 0; frame < frames; frame++)
            {
                var input = inputs is { Count: > 0 } && frame < inputs.Count
                    ? inputs[frame].ToState()
                    : RekallAgeRuntimeInputState.Empty;
                var result = await executionLoop.RunAsync(world, 1, cancellationToken, input);
                world = result.World;
                observeFrame?.Invoke(world);
            }
        }
        finally
        {
            if (ownsExecutionLoop)
            {
                executionLoop.Dispose();
            }
        }

        return world;
    }
}
