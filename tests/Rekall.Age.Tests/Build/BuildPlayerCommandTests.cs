using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;

namespace Rekall.Age.Tests.Build;

public sealed class BuildPlayerCommandTests
{
    [Fact]
    public async Task BuildPlayerCopiesInstalledPayloadWithoutRepositorySource()
    {
        var distributionRoot = TestPaths.CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(distributionRoot, "rekall.distribution.json"), "{}");
        var payload = Path.Combine(distributionRoot, "players", "headless");
        Directory.CreateDirectory(payload);
        var payloadExecutable = Path.Combine(payload, "Rekall.Age.Player.exe");
        await File.WriteAllTextAsync(payloadExecutable, "installed-player");
        await File.WriteAllTextAsync(Path.Combine(payload, "runtime.dll"), "runtime");
        var projectRoot = TestPaths.CreateTempDirectory();
        var outputRoot = Path.Combine(TestPaths.CreateTempDirectory(), "player-output");
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("installed player"),
            CancellationToken.None);

        var result = await new BuildPlayerCommand(distributionRoot).ExecuteAsync(
            new BuildPlayerRequest(projectRoot, "Main", outputRoot),
            context);

        Assert.True(result.Ok, FailureDetails(result));
        Assert.Equal("installed-player", await File.ReadAllTextAsync(result.Value.LaunchPath));
        Assert.True(File.Exists(Path.Combine(outputRoot, "runtime.dll")));
        Assert.DoesNotContain("dotnet publish", result.Value.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("installed distribution", result.Value.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([projectRoot, "Main"], result.Value.Arguments);
    }

    [Fact]
    public async Task BuildPlayerPublishesPlayableRuntimeForProject()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("build player"), CancellationToken.None);
        await TestProjectAuthoring.CreateProjectWithSceneAsync(root, context, "Playable Build");
        var command = new BuildPlayerCommand();

        var result = await command.ExecuteAsync(new BuildPlayerRequest(root, "Main"), context);

        Assert.True(result.Ok, FailureDetails(result));
        Assert.True(File.Exists(result.Value.LaunchPath), result.Value.LaunchPath);
        Assert.Contains(root, result.Value.Arguments);
        Assert.Contains("Main", result.Value.Arguments);
    }

    [Fact]
    public async Task BuildPlayerCanReturnGraphicsLaunchArguments()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("build graphics player"), CancellationToken.None);
        await TestProjectAuthoring.CreateProjectWithSceneAsync(root, context, "Graphical Build");
        var command = new BuildPlayerCommand();

        var result = await command.ExecuteAsync(new BuildPlayerRequest(root, "Main", Graphics: true), context);

        Assert.True(result.Ok, FailureDetails(result));
        Assert.Contains("--graphics", result.Value.Arguments);
        Assert.Contains("--backend", result.Value.Arguments);
        Assert.Contains("vulkan", result.Value.Arguments);
        Assert.DoesNotContain("--playable", result.Value.Arguments);
    }

    private static string FailureDetails(RekallAgeCommandResult<BuildPlayerResult> result)
    {
        return string.Join(
            Environment.NewLine,
            new[] { result.Summary, result.Value.Output }
                .Concat(result.Errors.Select(error => $"{error.Code}: {error.Message} ({error.Target})")));
    }
}
