namespace Rekall.Age.Playback;

public sealed record RekallAgeBreakoutState(
    int Width,
    int Height,
    double PaddleX,
    double BallX,
    double BallY,
    double BallVelocityX,
    double BallVelocityY,
    int Columns,
    int Rows,
    int BricksRemaining,
    int Score);
