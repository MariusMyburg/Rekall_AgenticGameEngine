namespace Rekall.Age.Playback;

public sealed record RekallAgePongRenderFrame(
    int Width,
    int Height,
    IReadOnlyList<RekallAgePongRectangle> Rectangles,
    IReadOnlyList<RekallAgePongCircle> Circles,
    RekallAgePongText ScoreText)
{
    public static RekallAgePongRenderFrame FromGame(RekallAgePongGame game, int width, int height)
    {
        var state = game.State;
        var scaleX = width / (double)state.Width;
        var scaleY = height / (double)state.Height;
        var paddleWidth = Math.Max(8, (int)Math.Round(scaleX * 0.8));
        var paddleHeight = Math.Max(48, (int)Math.Round(scaleY * 5));

        return new RekallAgePongRenderFrame(
            width,
            height,
            [
                new RekallAgePongRectangle(
                    "left-paddle",
                    ToScreenX(2, scaleX) - paddleWidth / 2,
                    ToScreenY(state.LeftPaddleY, scaleY) - paddleHeight / 2,
                    paddleWidth,
                    paddleHeight),
                new RekallAgePongRectangle(
                    "right-paddle",
                    ToScreenX(state.Width - 3, scaleX) - paddleWidth / 2,
                    ToScreenY(state.RightPaddleY, scaleY) - paddleHeight / 2,
                    paddleWidth,
                    paddleHeight)
            ],
            [
                new RekallAgePongCircle(
                    "ball",
                    ToScreenX(state.BallX, scaleX),
                    ToScreenY(state.BallY, scaleY),
                    Math.Max(6, (int)Math.Round(Math.Min(scaleX, scaleY) * 0.65)))
            ],
            new RekallAgePongText(
                "score",
                $"{state.LeftScore} : {state.RightScore}",
                width / 2,
                Math.Max(24, height / 12),
                Math.Max(20, height / 18)));
    }

    private static int ToScreenX(double value, double scale)
    {
        return (int)Math.Round(value * scale);
    }

    private static int ToScreenY(double value, double scale)
    {
        return (int)Math.Round(value * scale);
    }
}

public sealed record RekallAgePongRectangle(
    string Role,
    int X,
    int Y,
    int Width,
    int Height);

public sealed record RekallAgePongCircle(
    string Role,
    int CenterX,
    int CenterY,
    int Radius);

public sealed record RekallAgePongText(
    string Role,
    string Text,
    int CenterX,
    int BaselineY,
    int FontSize);
