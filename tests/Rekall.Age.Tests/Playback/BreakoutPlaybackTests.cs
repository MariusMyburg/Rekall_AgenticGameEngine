using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.GameTemplates.Commands;
using Rekall.Age.Playback;
using Rekall.Age.Playback.Commands;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Playback;

public sealed class BreakoutPlaybackTests
{
    [Fact]
    public async Task FactoryCreatesPlayableBreakoutFromTemplateScene()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("breakout"), CancellationToken.None);
        await new CreateGameFromTemplateCommand().ExecuteAsync(
            new CreateGameFromTemplateRequest(root, "Playable Breakout", "breakout"),
            context);
        var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);

        var game = RekallAgePlayableGameFactory.Create(scene);

        Assert.Equal("breakout", game.Kind);
        Assert.Contains("BrickField", game.EntityNames);
    }

    [Fact]
    public void BreakoutSimulationMovesBallAndTracksBricks()
    {
        var game = RekallAgeBreakoutGame.CreateDefault();
        var before = game.State.BallX;

        for (var i = 0; i < 12; i++)
        {
            game.Tick(RekallAgePongInput.None);
        }

        Assert.NotEqual(before, game.State.BallX);
        Assert.True(game.State.BricksRemaining > 0);
    }

    [Fact]
    public void BreakoutRendererDrawsPaddleBallBricksAndScore()
    {
        var game = RekallAgeBreakoutGame.CreateDefault();

        var frame = game.RenderAscii();

        Assert.Contains("#", frame, StringComparison.Ordinal);
        Assert.Contains("_", frame, StringComparison.Ordinal);
        Assert.Contains("O", frame, StringComparison.Ordinal);
        Assert.Contains("Score: 0", frame, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlaySceneCommandCanPlayBreakoutFrames()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("play breakout"), CancellationToken.None);
        await new CreateGameFromTemplateCommand().ExecuteAsync(
            new CreateGameFromTemplateRequest(root, "Playable Breakout", "breakout"),
            context);

        var result = await new PlaySceneCommand().ExecuteAsync(new PlaySceneRequest(root, "Main", 2), context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal("breakout", result.Value.Kind);
        Assert.All(result.Value.Frames, frame => Assert.Contains("Score: 0", frame, StringComparison.Ordinal));
    }
}
