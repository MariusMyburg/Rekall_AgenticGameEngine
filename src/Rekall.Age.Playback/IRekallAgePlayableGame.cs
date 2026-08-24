using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Playback;

public sealed record RekallAgePlaybackRenderFrame(
    int FrameIndex,
    string Kind,
    string Text,
    IReadOnlyList<RekallAgePlaybackDrawCommand> DrawCommands)
{
    public RekallAgePlaybackRuntimeState? RuntimeState { get; init; }
}

public sealed record RekallAgePlaybackRuntimeState(
    int FrameIndex,
    int EntityCount,
    int AudioVoiceCount,
    int UiElementCount,
    int AnimationPlayerCount,
    IReadOnlyList<RekallAgePlaybackRuntimeEntityState> Entities,
    IReadOnlyList<RekallAgePlaybackRuntimeObservation> Observations);

public sealed record RekallAgePlaybackRuntimeEntityState(
    string Id,
    string Name,
    double X,
    double Y,
    double Z,
    IReadOnlyList<string> ComponentTypes);

public sealed record RekallAgePlaybackRuntimeObservation(
    string Code,
    string Severity,
    string Subsystem,
    string TargetId,
    string Message);

public sealed record RekallAgePlaybackDrawCommand(
    string Kind,
    string Id,
    double X,
    double Y,
    double Width,
    double Height,
    string Fill,
    string Text);

public interface IRekallAgePlayableGame : IDisposable
{
    string Kind { get; }

    IReadOnlyList<string> EntityNames { get; }

    void Tick(RekallAgeRuntimeInputFrame input);

    string RenderAscii();

    RekallAgePlaybackRenderFrame RenderFrame(int frameIndex);
}
