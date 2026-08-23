using Rekall.Age.World;
using Rekall.Age.Runtime;

namespace Rekall.Age.Playback;

public static class RekallAgePlayableGameFactory
{
    public static IRekallAgePlayableGame Create(string projectRoot, RekallAgeSceneDocument scene)
    {
        return RekallAgeModulePlayableGame.Create(projectRoot, scene);
    }

    public static IRekallAgePlayableGame CreateWithRuntime(string projectRoot, RekallAgeSceneDocument scene)
    {
        return new RekallAgeRuntimeObservedPlayableGame(
            Create(projectRoot, scene),
            new RekallAgeRuntimeWorldBuilder().Build(scene, projectRoot),
            RekallAgeRuntimeExecutionLoop.CreateDefault(projectRoot));
    }
}

internal sealed class RekallAgeRuntimeObservedPlayableGame(
    IRekallAgePlayableGame inner,
    Rekall.Age.Runtime.Abstractions.RekallAgeRuntimeWorld runtimeWorld,
    RekallAgeRuntimeExecutionLoop runtimeLoop) : IRekallAgePlayableGame
{
    private const double FixedDeltaSeconds = 1.0 / 60.0;
    private const double MaximumAccumulatedSeconds = 0.25;
    private Rekall.Age.Runtime.Abstractions.RekallAgeRuntimeWorld _runtimeWorld = runtimeWorld;
    private double _runtimeAccumulatorSeconds;

    public string Kind => inner.Kind;

    public IReadOnlyList<string> EntityNames => inner.EntityNames;

    public void Tick(RekallAgePlaybackInput input)
    {
        inner.Tick(input);
        _runtimeAccumulatorSeconds = Math.Min(
            MaximumAccumulatedSeconds,
            _runtimeAccumulatorSeconds + Math.Max(0, input.DeltaSeconds));
        var fixedSteps = (int)Math.Floor((_runtimeAccumulatorSeconds + 1e-9) / FixedDeltaSeconds);
        if (fixedSteps <= 0)
        {
            return;
        }

        _runtimeAccumulatorSeconds -= fixedSteps * FixedDeltaSeconds;
        _runtimeWorld = runtimeLoop.RunAsync(_runtimeWorld, fixedSteps, CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult()
            .World;
    }

    public string RenderAscii() => inner.RenderAscii();

    public RekallAgePlaybackRenderFrame RenderFrame(int frameIndex)
    {
        var frame = inner.RenderFrame(frameIndex);
        return frame with
        {
            RuntimeState = new RekallAgePlaybackRuntimeState(
                _runtimeWorld.FrameIndex,
                _runtimeWorld.Entities.Count,
                _runtimeWorld.Subsystems.Audio.Voices.Count,
                _runtimeWorld.Subsystems.Ui.Elements.Count,
                _runtimeWorld.Subsystems.Animation.Players.Count,
                _runtimeWorld.Entities.Select(entity => new RekallAgePlaybackRuntimeEntityState(
                    entity.Id,
                    entity.Name,
                    entity.Transform.Position3D.X,
                    entity.Transform.Position3D.Y,
                    entity.Transform.Position3D.Z,
                    entity.Components.Select(component => component.Type).OrderBy(type => type, StringComparer.Ordinal).ToArray()))
                    .OrderBy(entity => entity.Name, StringComparer.Ordinal)
                    .ThenBy(entity => entity.Id, StringComparer.Ordinal)
                    .ToArray(),
                _runtimeWorld.Observations.Select(observation => new RekallAgePlaybackRuntimeObservation(
                    observation.Code,
                    observation.Severity,
                    observation.Subsystem,
                    observation.TargetId,
                    observation.Message)).ToArray())
        };
    }

    public void Dispose()
    {
        runtimeLoop.Dispose();
        inner.Dispose();
    }
}
