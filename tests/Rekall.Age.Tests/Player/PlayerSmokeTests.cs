using System.Diagnostics;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.GameTemplates.Commands;

namespace Rekall.Age.Tests.Player;

public sealed class PlayerSmokeTests
{
    [Fact]
    public async Task PlayerFramesModeAcceptsDeterministicInputSequence()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("player-test", RekallAgeTransaction.Begin("player input"), CancellationToken.None);
        var create = await new CreatePlayableGameFromTemplateCommand().ExecuteAsync(
            new CreatePlayableGameFromTemplateRequest(root, "Player Pong", "pong"),
            context);
        Assert.True(create.Ok, create.Summary);
        var project = Path.Combine(FindRepositoryRoot(), "src", "Rekall.Age.Player", "Rekall.Age.Player.csproj");

        var run = await RunAsync(
            project,
            root,
            "Main",
            "--frames",
            "2",
            "--inputs",
            """[{"verticalAxis":1,"primaryAction":true},{"verticalAxis":-1,"primaryAction":false}]""");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("FRAME 1", run.Output);
        Assert.Contains("Score 10", run.Output);
        Assert.Contains("Left paddle lane 0", run.Output);
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string project, params string[] args)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(project);
        startInfo.ArgumentList.Add("--");
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        output += await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, output);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rekall.AGE.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find Rekall.AGE.sln from the test output directory.");
    }
}
