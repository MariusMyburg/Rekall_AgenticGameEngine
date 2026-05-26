using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.GameTemplates.Commands;
using Rekall.Age.Modules.Commands;
using Rekall.Age.Playback;
using Rekall.Age.Playback.Commands;

namespace Rekall.Age.Tests.Modules;

public sealed class WriteModuleSourceCommandTests
{
    [Fact]
    public async Task WriteModuleSourceLetsAgentReplacePlayableModuleCode()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("write module source"), CancellationToken.None);
        await new CreateGameFromTemplateCommand().ExecuteAsync(
            new CreateGameFromTemplateRequest(root, "Agent Module Game", "pong"),
            context);
        await new ScaffoldPlayableModuleCommand().ExecuteAsync(
            new ScaffoldPlayableModuleRequest(root, "agent.playable", "Agent Playable", "AgentPlayable", "pong"),
            context);

        var write = await new WriteModuleSourceCommand().ExecuteAsync(
            new WriteModuleSourceRequest(root, "AgentPlayable", "AgentPlayableModule.cs", CreateAgentPlayableSource()),
            context);
        var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);
        var play = await new PlaySceneCommand().ExecuteAsync(
            new PlaySceneRequest(root, "Main", 1, [new RekallAgePlaybackInput(1, PrimaryAction: true)]),
            context);

        Assert.True(write.Ok, write.Summary);
        Assert.True(File.Exists(write.Value.SourcePath));
        Assert.True(build.Ok, build.Summary);
        Assert.True(play.Ok, play.Summary);
        Assert.Contains("AGENT CUSTOM PONG", play.Value.Frames[0], StringComparison.Ordinal);
        Assert.Contains("Score 50", play.Value.Frames[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgentPlayableModuleCanUsePlaybackDeltaSeconds()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("write module delta source"), CancellationToken.None);
        await new CreateGameFromTemplateCommand().ExecuteAsync(
            new CreateGameFromTemplateRequest(root, "Agent Delta Module Game", "pong"),
            context);
        await new ScaffoldPlayableModuleCommand().ExecuteAsync(
            new ScaffoldPlayableModuleRequest(root, "agent.delta", "Agent Delta", "AgentDelta", "pong"),
            context);

        var write = await new WriteModuleSourceCommand().ExecuteAsync(
            new WriteModuleSourceRequest(root, "AgentDelta", "AgentDeltaModule.cs", CreateAgentDeltaSource()),
            context);
        var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);
        var play = await new PlaySceneCommand().ExecuteAsync(
            new PlaySceneRequest(root, "Main", 1, [new RekallAgePlaybackInput(0, DeltaSeconds: 0.25)]),
            context);

        Assert.True(write.Ok, write.Summary);
        Assert.True(build.Ok, build.Summary);
        Assert.True(play.Ok, play.Summary);
        Assert.Contains("Seconds 0.25", play.Value.Frames[0], StringComparison.Ordinal);
        Assert.Contains("Distance 3", play.Value.Frames[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteModuleSourceRejectsPathsOutsideProjectModules()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("write module source rejected"), CancellationToken.None);

        var write = await new WriteModuleSourceCommand().ExecuteAsync(
            new WriteModuleSourceRequest(root, "AgentPlayable", "..\\Outside.cs", "public sealed class Outside {}"),
            context);

        Assert.False(write.Ok);
        Assert.Contains(write.Errors, error => error.Code == "REKALL_MODULE_SOURCE_PATH_OUTSIDE_PROJECT");
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
    public string Kind => "pong";

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
            state.Numbers["score"] += 50;
        }
    }

    public RekallAgePlayableModuleFrame Render(RekallAgePlayableModuleState state)
    {
        return new RekallAgePlayableModuleFrame($"AGENT CUSTOM PONG\nFrame {(int)state.Numbers["frame"]}\nScore {(int)state.Numbers["score"]}\nLane {(int)state.Numbers["lane"]}");
    }
}
""";
    }

    private static string CreateAgentDeltaSource()
    {
        return """
using Rekall.Age.Modules;

namespace Game.Modules.AgentDelta;

[RekallAgeModule("agent.delta", "Agent Delta")]
[RekallAgeRequiresCapability("world")]
public sealed class AgentDeltaModule : RekallAgeModule, IRekallAgePlayableModule
{
    public string Kind => "delta";

    public override void Configure(RekallAgeModuleBuilder builder)
    {
    }

    public RekallAgePlayableModuleState CreateInitialState(RekallAgePlayableModuleContext context)
    {
        var state = new RekallAgePlayableModuleState();
        state.Numbers["seconds"] = 0;
        state.Numbers["distance"] = 0;
        return state;
    }

    public void Tick(RekallAgePlayableModuleState state, RekallAgePlayableModuleInput input)
    {
        state.Numbers["seconds"] = input.DeltaSeconds;
        state.Numbers["distance"] += input.DeltaSeconds * 12;
    }

    public RekallAgePlayableModuleFrame Render(RekallAgePlayableModuleState state)
    {
        return new RekallAgePlayableModuleFrame($"AGENT DELTA\nSeconds {state.Numbers["seconds"]:0.00}\nDistance {state.Numbers["distance"]:0}");
    }
}
""";
    }
}
