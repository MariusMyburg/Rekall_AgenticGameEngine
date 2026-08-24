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
                var inputFrame = inputs is { Count: > 0 } && frame < inputs.Count
                    ? inputs[frame]
                    : null;
                var input = inputFrame is not null
                    ? inputFrame.ToState()
                    : RekallAgeRuntimeInputState.Empty;
                var deltaSeconds = inputFrame?.DeltaSeconds ?? (1.0 / 60.0);
                TimeSpan? deltaTime = double.IsFinite(deltaSeconds) && deltaSeconds > 0
                    ? TimeSpan.FromSeconds(deltaSeconds)
                    : null;
                var result = await executionLoop.RunAsync(world, 1, cancellationToken, input, deltaTime);
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

        // Each inputs[i] applies to exactly simulation frame i (documented on
        // rekall.runtime.inspect_scene's own schema); frames beyond the supplied array
        // length receive RekallAgeRuntimeInputState.Empty, not the last-held state. A
        // client that supplies fewer input entries than requested frames -- e.g. one
        // "held key" entry meant to persist across a long run -- gets a plausible-looking
        // but silently wrong result with no error. Surface it as a structured observation
        // so that failure mode is visible in the result instead of requiring the client to
        // notice on its own.
        if (inputs is { Count: > 0 } suppliedInputs && suppliedInputs.Count < frames)
        {
            world = world with
            {
                Observations = world.Observations
                    .Append(new RekallAgeRuntimeObservation(
                        frames,
                        "REKALL_RUNTIME_INPUT_FRAMES_EXHAUSTED",
                        "warning",
                        "input",
                        string.Empty,
                        string.Empty,
                        "runtime.input",
                        $"Only {suppliedInputs.Count} of {frames} requested frame(s) had a supplied input entry. "
                            + $"Frame(s) {suppliedInputs.Count} through {frames - 1} received no input at all "
                            + "(not the prior frame's held state) -- each inputs[i] applies to exactly frame i. "
                            + "To hold a key/action across N frames, repeat that same input entry N times in the "
                            + "inputs array.",
                        []))
                    .ToArray()
            };
        }

        return world;
    }
}
