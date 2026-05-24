using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.GameTemplates.Commands;

namespace Rekall.Age.Tests.Build;

public sealed class BuildPlayerCommandTests
{
    [Fact]
    public async Task BuildPlayerPublishesPlayableRuntimeForProject()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("build player"), CancellationToken.None);
        await new CreateGameFromTemplateCommand().ExecuteAsync(
            new CreateGameFromTemplateRequest(root, "Playable Pong", "pong"),
            context);
        var command = new BuildPlayerCommand();

        var result = await command.ExecuteAsync(new BuildPlayerRequest(root, "Main"), context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(File.Exists(result.Value.LaunchPath), result.Value.LaunchPath);
        Assert.Contains(root, result.Value.Arguments);
        Assert.Contains("Main", result.Value.Arguments);
    }

    [Fact]
    public async Task BuildPlayerCanReturnGraphicsLaunchArguments()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("build graphics player"), CancellationToken.None);
        await new CreateGameFromTemplateCommand().ExecuteAsync(
            new CreateGameFromTemplateRequest(root, "Graphical Pong", "pong"),
            context);
        var command = new BuildPlayerCommand();

        var result = await command.ExecuteAsync(new BuildPlayerRequest(root, "Main", Graphics: true), context);

        Assert.True(result.Ok, result.Summary);
        Assert.Contains("--graphics", result.Value.Arguments);
    }
}
