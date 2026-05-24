using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.GameTemplates.Commands;
using Rekall.Age.Playback;
using Rekall.Age.Playback.Commands;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Playback;

public sealed class PongPlaybackTests
{
    [Fact]
    public async Task FactoryCreatesPlayablePongFromTemplateScene()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("pong"), CancellationToken.None);
        await new CreateGameFromTemplateCommand().ExecuteAsync(
            new CreateGameFromTemplateRequest(root, "Playable Pong", "pong"),
            context);
        var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);

        var game = RekallAgePlayableGameFactory.Create(scene);

        Assert.Equal("pong", game.Kind);
        Assert.Contains("LeftPaddle", game.EntityNames);
        Assert.Contains("Ball", game.EntityNames);
    }

    [Fact]
    public void PongSimulationMovesBallAndBouncesFromPlayfield()
    {
        var game = RekallAgePongGame.CreateDefault();
        var before = game.State.BallX;

        for (var i = 0; i < 20; i++)
        {
            game.Tick(RekallAgePongInput.None);
        }

        Assert.NotEqual(before, game.State.BallX);
        Assert.InRange(game.State.BallY, 1, game.State.Height - 2);
    }

    [Fact]
    public void PongRendererDrawsPaddlesBallAndScore()
    {
        var game = RekallAgePongGame.CreateDefault();

        var frame = game.RenderAscii();

        Assert.Contains("|", frame, StringComparison.Ordinal);
        Assert.Contains("O", frame, StringComparison.Ordinal);
        Assert.Contains("0 : 0", frame, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlaySceneCommandReturnsDeterministicPreviewFrames()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("play"), CancellationToken.None);
        await new CreateGameFromTemplateCommand().ExecuteAsync(
            new CreateGameFromTemplateRequest(root, "Playable Pong", "pong"),
            context);
        var command = new PlaySceneCommand();

        var result = await command.ExecuteAsync(new PlaySceneRequest(root, "Main", 4), context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal("pong", result.Value.Kind);
        Assert.Equal(4, result.Value.Frames.Count);
        Assert.All(result.Value.Frames, frame => Assert.Contains("0 : 0", frame, StringComparison.Ordinal));
    }
}
