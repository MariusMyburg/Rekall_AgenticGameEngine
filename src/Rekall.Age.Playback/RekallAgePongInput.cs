namespace Rekall.Age.Playback;

public readonly record struct RekallAgePongInput(int LeftPaddleDirection)
{
    public static RekallAgePongInput None { get; } = new(0);

    public static RekallAgePongInput Up { get; } = new(-1);

    public static RekallAgePongInput Down { get; } = new(1);
}
