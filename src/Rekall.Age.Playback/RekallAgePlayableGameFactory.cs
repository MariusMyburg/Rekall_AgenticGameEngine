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
            new RekallAgeRuntimeWorldBuilder().Build(scene),
            RekallAgeRuntimeExecutionLoop.CreateDefault(projectRoot));
    }
}

internal sealed class RekallAgeRuntimeObservedPlayableGame(
    IRekallAgePlayableGame inner,
    Rekall.Age.Runtime.Abstractions.RekallAgeRuntimeWorld runtimeWorld,
    RekallAgeRuntimeExecutionLoop runtimeLoop) : IRekallAgePlayableGame
{
    private Rekall.Age.Runtime.Abstractions.RekallAgeRuntimeWorld _runtimeWorld = runtimeWorld;

    public string Kind => inner.Kind;

    public IReadOnlyList<string> EntityNames => inner.EntityNames;

    public void Tick(RekallAgePlaybackInput input)
    {
        inner.Tick(input);
        _runtimeWorld = runtimeLoop.RunAsync(_runtimeWorld, 1, CancellationToken.None)
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
}
