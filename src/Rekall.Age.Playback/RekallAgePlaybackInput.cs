namespace Rekall.Age.Playback;

public readonly record struct RekallAgePlaybackInput(int VerticalAxis, bool PrimaryAction = false)
{
    public static RekallAgePlaybackInput None { get; } = new(0);

    public static RekallAgePlaybackInput Up { get; } = new(-1);

    public static RekallAgePlaybackInput Down { get; } = new(1);
}
