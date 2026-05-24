namespace Rekall.Age.Playback;

public sealed record RekallAgePongState(
    int Width,
    int Height,
    double LeftPaddleY,
    double RightPaddleY,
    double BallX,
    double BallY,
    double BallVelocityX,
    double BallVelocityY,
    int LeftScore,
    int RightScore);
