using Rekall.Age.Playback;

namespace Rekall.Age.Tests.Playback;

public sealed class PongRenderPrimitiveTests
{
    [Fact]
    public void RendererBuildsStablePrimitivesFromPongState()
    {
        var game = RekallAgePongGame.CreateDefault();
        game.Tick(RekallAgePongInput.None);

        var frame = RekallAgePongRenderFrame.FromGame(game, 960, 540);

        Assert.Equal(960, frame.Width);
        Assert.Equal(540, frame.Height);
        Assert.Contains(frame.Rectangles, rectangle => rectangle.Role == "left-paddle");
        Assert.Contains(frame.Rectangles, rectangle => rectangle.Role == "right-paddle");
        Assert.Contains(frame.Circles, circle => circle.Role == "ball");
        Assert.Equal("0 : 0", frame.ScoreText.Text);
    }

    [Fact]
    public void RendererScalesBallPositionIntoViewport()
    {
        var game = RekallAgePongGame.CreateDefault();

        var frame = RekallAgePongRenderFrame.FromGame(game, 480, 270);
        var ball = Assert.Single(frame.Circles);

        Assert.InRange(ball.CenterX, 200, 280);
        Assert.InRange(ball.CenterY, 100, 170);
        Assert.True(ball.Radius > 0);
    }
}
