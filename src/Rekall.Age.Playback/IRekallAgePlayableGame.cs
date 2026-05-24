namespace Rekall.Age.Playback;

public interface IRekallAgePlayableGame
{
    string Kind { get; }

    IReadOnlyList<string> EntityNames { get; }

    void Tick(RekallAgePlaybackInput input);

    string RenderAscii();
}
