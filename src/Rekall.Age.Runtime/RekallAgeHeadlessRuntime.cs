using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.Validation;
using Rekall.Age.World;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeHeadlessRuntime
{
    private readonly RekallAgeSceneStore _sceneStore;
    private readonly RekallAgeProjectValidator? _validator;
    private readonly RekallAgeGameplayInterpreter _interpreter = new();

    public RekallAgeHeadlessRuntime(RekallAgeSceneStore sceneStore, RekallAgeProjectValidator? validator = null)
    {
        _sceneStore = sceneStore;
        _validator = validator;
    }

    public async ValueTask<RekallAgeRuntimeResult> RunAsync(
        string projectRoot,
        string sceneName,
        TimeSpan duration,
        CancellationToken cancellationToken,
        IReadOnlyList<RekallAgeRuntimeInputFrame>? inputs = null)
    {
        var scene = await _sceneStore.LoadAsync(projectRoot, sceneName, cancellationToken);

        if (_validator is not null)
        {
            var report = await _validator.ValidateSceneAsync(projectRoot, sceneName, cancellationToken);
            if (report.Status == "blocked")
            {
                return new RekallAgeRuntimeResult(false, 0, TimeSpan.Zero, report.BlockingMessages, Array.Empty<RekallAgeRuntimeObservation>());
            }
        }

        var frameTime = TimeSpan.FromSeconds(1.0 / 60.0);
        var frames = Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds / frameTime.TotalSeconds));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);
        var loop = RekallAgeRuntimeExecutionLoop.CreateDefault(projectRoot);

        for (var frame = 0; frame < frames; frame++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = inputs is { Count: > 0 } && frame < inputs.Count
                ? inputs[frame].ToState()
                : RekallAgeRuntimeInputState.Empty;
            var result = await loop.RunAsync(world, 1, cancellationToken, input);
            world = result.World;
        }

        var observations = world.Observations
            .Concat(_interpreter.Observe(scene, frames))
            .ToArray();
        return new RekallAgeRuntimeResult(true, frames, duration, Array.Empty<string>(), observations)
        {
            SystemsRun = world.SystemsRun,
            InputActions = world.Subsystems.Input.Actions
        };
    }
}
