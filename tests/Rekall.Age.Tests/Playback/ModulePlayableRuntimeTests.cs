using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.GameTemplates.Commands;
using Rekall.Age.Modules.Commands;
using Rekall.Age.Playback;
using Rekall.Age.Playback.Commands;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Playback;

public sealed class ModulePlayableRuntimeTests
{
    [Fact]
    public async Task PlayScenePrefersCompiledPlayableModuleOverBuiltInFallback()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("module playable"), CancellationToken.None);
        await new CreateGameFromTemplateCommand().ExecuteAsync(
            new CreateGameFromTemplateRequest(root, "Module Pong", "pong"),
            context);
        await new ScaffoldPlayableModuleCommand().ExecuteAsync(
            new ScaffoldPlayableModuleRequest(root, "module.pong", "Module Pong", "ModulePong", "pong"),
            context);
        var buildResult = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);
        Assert.True(buildResult.Ok, buildResult.Summary);

        var playResult = await new PlaySceneCommand().ExecuteAsync(new PlaySceneRequest(root, "Main", 2), context);

        Assert.True(playResult.Ok, playResult.Summary);
        Assert.Equal("pong", playResult.Value.Kind);
        Assert.All(playResult.Value.Frames, frame => Assert.Contains("PONG", frame, StringComparison.Ordinal));
        Assert.All(playResult.Value.Frames, frame => Assert.Contains("Ball", frame, StringComparison.Ordinal));
    }

    [Fact]
    public async Task PlaySceneReturnsStructuredRenderFramesFromPlayableModule()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("module render frame"), CancellationToken.None);
        await new CreatePlayableGameFromTemplateCommand().ExecuteAsync(
            new CreatePlayableGameFromTemplateRequest(root, "Renderable Pong", "pong"),
            context);

        var playResult = await new PlaySceneCommand().ExecuteAsync(
            new PlaySceneRequest(
                root,
                "Main",
                1,
                [
                    new RekallAgePlaybackInput(1, PrimaryAction: true)
                ]),
            context);

        Assert.True(playResult.Ok, playResult.Summary);
        var renderFrame = Assert.Single(playResult.Value.RenderFrames);
        Assert.Equal("pong", renderFrame.Kind);
        Assert.Equal(1, renderFrame.FrameIndex);
        Assert.Contains(renderFrame.DrawCommands, command => command.Kind == "clear" && command.Fill == "#101820");
        Assert.Contains(renderFrame.DrawCommands, command => command.Kind == "rect" && command.Id == "left-paddle");
        Assert.Contains(renderFrame.DrawCommands, command => command.Kind == "circle" && command.Id == "ball");
        Assert.Contains(renderFrame.DrawCommands, command => command.Kind == "text" && command.Text.Contains("Score 10", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RenderAsciiPreservesSingleTrailingNewLine()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("module ascii render"), CancellationToken.None);
        await new CreatePlayableGameFromTemplateCommand().ExecuteAsync(
            new CreatePlayableGameFromTemplateRequest(root, "Ascii Pong", "pong"),
            context);
        var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
        var game = RekallAgePlayableGameFactory.Create(root, scene);

        var frame = game.RenderAscii();

        Assert.EndsWith(Environment.NewLine, frame, StringComparison.Ordinal);
        Assert.False(frame.EndsWith(Environment.NewLine + Environment.NewLine, StringComparison.Ordinal), frame);
    }

    [Fact]
    public async Task PlayScenePassesDeterministicInputSequenceToPlayableModule()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("module playable input"), CancellationToken.None);
        await new CreatePlayableGameFromTemplateCommand().ExecuteAsync(
            new CreatePlayableGameFromTemplateRequest(root, "Input Pong", "pong"),
            context);

        var playResult = await new PlaySceneCommand().ExecuteAsync(
            new PlaySceneRequest(
                root,
                "Main",
                2,
                [
                    new RekallAgePlaybackInput(1, PrimaryAction: true),
                    new RekallAgePlaybackInput(-1)
                ]),
            context);

        Assert.True(playResult.Ok, playResult.Summary);
        Assert.Contains("Score 10", playResult.Value.Frames[0], StringComparison.Ordinal);
        Assert.Contains("Left paddle lane 1", playResult.Value.Frames[0], StringComparison.Ordinal);
        Assert.Contains("Left paddle lane 0", playResult.Value.Frames[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlaytestScenePassesWhenFrameAssertionsMatchDeterministicPlayback()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("module playtest"), CancellationToken.None);
        await new CreatePlayableGameFromTemplateCommand().ExecuteAsync(
            new CreatePlayableGameFromTemplateRequest(root, "Playtest Pong", "pong"),
            context);

        var playtestResult = await new PlaytestSceneCommand().ExecuteAsync(
            new PlaytestSceneRequest(
                root,
                "Main",
                2,
                [
                    new RekallAgePlaybackInput(1, PrimaryAction: true),
                    new RekallAgePlaybackInput(-1)
                ],
                [
                    new RekallAgeFrameAssertion(0, "Score 10"),
                    new RekallAgeFrameAssertion(0, "Left paddle lane 1"),
                    new RekallAgeFrameAssertion(1, "Left paddle lane 0")
                ]),
            context);

        Assert.True(playtestResult.Ok, playtestResult.Summary);
        Assert.True(playtestResult.Value.Passed);
        Assert.Equal("pong", playtestResult.Value.Kind);
        Assert.Equal(2, playtestResult.Value.Frames.Count);
        Assert.All(playtestResult.Value.Assertions, assertion => Assert.True(assertion.Passed));
    }

    [Fact]
    public async Task PlaytestScenePassesWhenDrawCommandAssertionsMatchRenderedFrame()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("module draw playtest"), CancellationToken.None);
        await new CreatePlayableGameFromTemplateCommand().ExecuteAsync(
            new CreatePlayableGameFromTemplateRequest(root, "Draw Playtest Pong", "pong"),
            context);

        var playtestResult = await new PlaytestSceneCommand().ExecuteAsync(
            new PlaytestSceneRequest(
                root,
                "Main",
                1,
                [
                    new RekallAgePlaybackInput(1, PrimaryAction: true)
                ],
                [
                    new RekallAgeFrameAssertion(0, "Score 10")
                ],
                [
                    new RekallAgeDrawCommandAssertion(0, Kind: "clear"),
                    new RekallAgeDrawCommandAssertion(0, Id: "left-paddle", Kind: "rect"),
                    new RekallAgeDrawCommandAssertion(0, Id: "ball", Kind: "circle"),
                    new RekallAgeDrawCommandAssertion(0, Kind: "text", TextContains: "Score 10")
                ]),
            context);

        Assert.True(playtestResult.Ok, playtestResult.Summary);
        Assert.True(playtestResult.Value.Passed);
        Assert.All(playtestResult.Value.DrawAssertions, assertion => Assert.True(assertion.Passed));
        Assert.Contains(playtestResult.Value.RenderFrames[0].DrawCommands, command => command.Id == "ball");
    }

    [Fact]
    public async Task PlaytestSceneFailsWithStructuredAssertionDetailsWhenFrameDoesNotMatch()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("module playtest failure"), CancellationToken.None);
        await new CreatePlayableGameFromTemplateCommand().ExecuteAsync(
            new CreatePlayableGameFromTemplateRequest(root, "Playtest Pong", "pong"),
            context);

        var playtestResult = await new PlaytestSceneCommand().ExecuteAsync(
            new PlaytestSceneRequest(
                root,
                "Main",
                1,
                null,
                [
                    new RekallAgeFrameAssertion(0, "Score 999")
                ]),
            context);

        Assert.False(playtestResult.Ok);
        Assert.False(playtestResult.Value.Passed);
        Assert.Contains(playtestResult.Errors, error => error.Code == "REKALL_PLAYTEST_FAILED");
        var failedAssertion = Assert.Single(playtestResult.Value.Assertions);
        Assert.False(failedAssertion.Passed);
        Assert.Equal(0, failedAssertion.FrameIndex);
        Assert.Equal("Score 999", failedAssertion.Contains);
        Assert.Contains("PONG", failedAssertion.Frame, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlaytestSceneFailsWithStructuredDrawAssertionDetailsWhenCommandDoesNotMatch()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("module draw playtest failure"), CancellationToken.None);
        await new CreatePlayableGameFromTemplateCommand().ExecuteAsync(
            new CreatePlayableGameFromTemplateRequest(root, "Draw Failure Pong", "pong"),
            context);

        var playtestResult = await new PlaytestSceneCommand().ExecuteAsync(
            new PlaytestSceneRequest(
                root,
                "Main",
                1,
                null,
                null,
                [
                    new RekallAgeDrawCommandAssertion(0, Id: "boss-health-bar", Kind: "rect")
                ]),
            context);

        Assert.False(playtestResult.Ok);
        Assert.False(playtestResult.Value.Passed);
        Assert.Contains(playtestResult.Errors, error => error.Code == "REKALL_PLAYTEST_FAILED");
        var failedAssertion = Assert.Single(playtestResult.Value.DrawAssertions);
        Assert.False(failedAssertion.Passed);
        Assert.Equal(0, failedAssertion.FrameIndex);
        Assert.Equal("boss-health-bar", failedAssertion.Id);
        Assert.Equal("rect", failedAssertion.Kind);
        Assert.Empty(failedAssertion.MatchingCommands);
    }
}
