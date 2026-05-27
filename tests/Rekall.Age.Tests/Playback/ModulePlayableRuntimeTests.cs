using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules.Commands;
using Rekall.Age.Playback;
using Rekall.Age.Playback.Commands;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Playback;

public sealed class ModulePlayableRuntimeTests
{
    [Fact]
    public async Task PlaySceneRunsCompiledAgentPlayableModule()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("module playable"), CancellationToken.None);
        await CreatePlayableProjectAsync(root, context);

        var playResult = await new PlaySceneCommand().ExecuteAsync(new PlaySceneRequest(root, "Main", 2), context);

        Assert.True(playResult.Ok, playResult.Summary);
        Assert.Equal("agent-authored", playResult.Value.Kind);
        Assert.All(playResult.Value.Frames, frame => Assert.Contains("AGENT AUTHORED PLAYABLE", frame, StringComparison.Ordinal));
    }

    [Fact]
    public async Task PlaySceneReturnsStructuredRenderFramesFromPlayableModule()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("module render frame"), CancellationToken.None);
        await CreatePlayableProjectAsync(root, context);

        var playResult = await new PlaySceneCommand().ExecuteAsync(
            new PlaySceneRequest(root, "Main", 1, [new RekallAgePlaybackInput(1, PrimaryAction: true)]),
            context);

        Assert.True(playResult.Ok, playResult.Summary);
        var renderFrame = Assert.Single(playResult.Value.RenderFrames);
        Assert.Equal("agent-authored", renderFrame.Kind);
        Assert.Equal(1, renderFrame.FrameIndex);
        Assert.Contains(renderFrame.DrawCommands, command => command.Kind == "clear" && command.Fill == "#101820");
        Assert.Contains(renderFrame.DrawCommands, command => command.Kind == "rect" && command.Id == "agent-actor");
        Assert.Contains(renderFrame.DrawCommands, command => command.Kind == "circle" && command.Id == "agent-focus");
        Assert.Contains(renderFrame.DrawCommands, command => command.Kind == "text" && command.Text.Contains("Score 10", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RenderAsciiPreservesSingleTrailingNewLine()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("module ascii render"), CancellationToken.None);
        await CreatePlayableProjectAsync(root, context);
        var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
        var game = RekallAgePlayableGameFactory.Create(root, scene);

        var frame = game.RenderAscii();

        Assert.EndsWith(Environment.NewLine, frame, StringComparison.Ordinal);
        Assert.False(frame.EndsWith(Environment.NewLine + Environment.NewLine, StringComparison.Ordinal), frame);
    }

    [Fact]
    public async Task PlaytestScenePassesWhenFrameAndDrawAssertionsMatchDeterministicPlayback()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("module playtest"), CancellationToken.None);
        await CreatePlayableProjectAsync(root, context);

        var playtestResult = await new PlaytestSceneCommand().ExecuteAsync(
            new PlaytestSceneRequest(
                root,
                "Main",
                2,
                [new RekallAgePlaybackInput(1, PrimaryAction: true), new RekallAgePlaybackInput(-1)],
                [
                    new RekallAgeFrameAssertion(0, "Score 10"),
                    new RekallAgeFrameAssertion(0, "Lane 1"),
                    new RekallAgeFrameAssertion(1, "Lane 0")
                ],
                [
                    new RekallAgeDrawCommandAssertion(0, Kind: "clear"),
                    new RekallAgeDrawCommandAssertion(0, Id: "agent-actor", Kind: "rect"),
                    new RekallAgeDrawCommandAssertion(0, Id: "agent-focus", Kind: "circle"),
                    new RekallAgeDrawCommandAssertion(0, Kind: "text", TextContains: "Score 10")
                ]),
            context);

        Assert.True(playtestResult.Ok, playtestResult.Summary);
        Assert.True(playtestResult.Value.Passed);
        Assert.Equal("agent-authored", playtestResult.Value.Kind);
        Assert.All(playtestResult.Value.Assertions, assertion => Assert.True(assertion.Passed));
        Assert.All(playtestResult.Value.DrawAssertions, assertion => Assert.True(assertion.Passed));
    }

    [Fact]
    public async Task PlaytestSceneFailsWithStructuredAssertionDetailsWhenFrameDoesNotMatch()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("module playtest failure"), CancellationToken.None);
        await CreatePlayableProjectAsync(root, context);

        var playtestResult = await new PlaytestSceneCommand().ExecuteAsync(
            new PlaytestSceneRequest(root, "Main", 1, null, [new RekallAgeFrameAssertion(0, "Score 999")]),
            context);

        Assert.False(playtestResult.Ok);
        Assert.False(playtestResult.Value.Passed);
        Assert.Contains(playtestResult.Errors, error => error.Code == "REKALL_PLAYTEST_FAILED");
        var failedAssertion = Assert.Single(playtestResult.Value.Assertions);
        Assert.False(failedAssertion.Passed);
        Assert.Equal("Score 999", failedAssertion.Contains);
        Assert.Contains("AGENT AUTHORED PLAYABLE", failedAssertion.Frame, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlaytestSceneFailsWithStructuredDrawAssertionDetailsWhenCommandDoesNotMatch()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("module draw playtest failure"), CancellationToken.None);
        await CreatePlayableProjectAsync(root, context);

        var playtestResult = await new PlaytestSceneCommand().ExecuteAsync(
            new PlaytestSceneRequest(
                root,
                "Main",
                1,
                null,
                null,
                [new RekallAgeDrawCommandAssertion(0, Id: "boss-health-bar", Kind: "rect")]),
            context);

        Assert.False(playtestResult.Ok);
        Assert.False(playtestResult.Value.Passed);
        Assert.Contains(playtestResult.Errors, error => error.Code == "REKALL_PLAYTEST_FAILED");
        var failedAssertion = Assert.Single(playtestResult.Value.DrawAssertions);
        Assert.False(failedAssertion.Passed);
        Assert.Equal("boss-health-bar", failedAssertion.Id);
        Assert.Equal("rect", failedAssertion.Kind);
        Assert.Empty(failedAssertion.MatchingCommands);
    }

    private static async Task CreatePlayableProjectAsync(string root, RekallAgeCommandContext context)
    {
        await TestProjectAuthoring.CreateProjectWithSceneAsync(root, context);
        await new ScaffoldPlayableModuleCommand().ExecuteAsync(
            new ScaffoldPlayableModuleRequest(root, "agent.playable", "Agent Playable", "AgentPlayable"),
            context);
        await new WriteModuleSourceCommand().ExecuteAsync(
            new WriteModuleSourceRequest(root, "AgentPlayable", "AgentPlayableModule.cs", CreateAgentPlayableSource()),
            context);
        var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);
        Assert.True(build.Ok, build.Summary);
    }

    private static string CreateAgentPlayableSource()
    {
        return """
using Rekall.Age.Modules;

namespace Game.Modules.AgentPlayable;

[RekallAgeModule("agent.playable", "Agent Playable")]
[RekallAgeRequiresCapability("world")]
public sealed class AgentPlayableModule : RekallAgeModule, IRekallAgePlayableModule
{
    public string Kind => "agent-authored";

    public override void Configure(RekallAgeModuleBuilder builder)
    {
    }

    public RekallAgePlayableModuleState CreateInitialState(RekallAgePlayableModuleContext context)
    {
        var state = new RekallAgePlayableModuleState();
        state.Numbers["frame"] = 0;
        state.Numbers["score"] = 0;
        state.Numbers["lane"] = 0;
        return state;
    }

    public void Tick(RekallAgePlayableModuleState state, RekallAgePlayableModuleInput input)
    {
        state.Numbers["frame"] += 1;
        state.Numbers["lane"] += input.VerticalAxis;
        if (input.PrimaryAction)
        {
            state.Numbers["score"] += 10;
        }
    }

    public RekallAgePlayableModuleFrame Render(RekallAgePlayableModuleState state)
    {
        var score = (int)state.Numbers["score"];
        var lane = (int)state.Numbers["lane"];
        var drawCommands = new[]
        {
            new RekallAgePlayableDrawCommand("clear", "background", 0, 0, 320, 180, "#101820"),
            new RekallAgePlayableDrawCommand("rect", "agent-actor", 128 + (lane * 8), 74, 40, 40, "#f2aa4c"),
            new RekallAgePlayableDrawCommand("circle", "agent-focus", 184, 86, 16, 16, "#f6f7f8"),
            new RekallAgePlayableDrawCommand("text", "agent-hud", 8, 8, 0, 0, "#ffffff", $"Score {score}")
        };
        return new RekallAgePlayableModuleFrame($"AGENT AUTHORED PLAYABLE\nScore {score}\nLane {lane}", drawCommands);
    }
}
""";
    }
}
