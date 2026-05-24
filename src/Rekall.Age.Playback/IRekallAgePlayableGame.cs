namespace Rekall.Age.Playback;

public sealed record RekallAgePlaybackRenderFrame(
    int FrameIndex,
    string Kind,
    string Text,
    IReadOnlyList<RekallAgePlaybackDrawCommand> DrawCommands);

public sealed record RekallAgePlaybackDrawCommand(
    string Kind,
    string Id,
    double X,
    double Y,
    double Width,
    double Height,
    string Fill,
    string Text);

public interface IRekallAgePlayableGame
{
    string Kind { get; }

    IReadOnlyList<string> EntityNames { get; }

    void Tick(RekallAgePlaybackInput input);

    string RenderAscii();

    RekallAgePlaybackRenderFrame RenderFrame(int frameIndex);
}
