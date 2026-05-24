using System.Diagnostics;

namespace Rekall.Age.Tests.Cli;

public sealed class CliSmokeTests
{
    [Fact]
    public async Task CliCreatesTemplateProjectAndPrintsSummary()
    {
        var root = TestPaths.CreateTempDirectory();
        var project = Path.Combine(FindRepositoryRoot(), "src", "Rekall.Age.Cli", "Rekall.Age.Cli.csproj");

        var create = await RunAsync(project, "game", "create", root, "Crystal Mines", "puzzle");
        Assert.Equal(0, create.ExitCode);
        Assert.Contains("Created puzzle game", create.Output);

        var summary = await RunAsync(project, "context", "summary", root);
        Assert.Equal(0, summary.ExitCode);
        Assert.Contains("Crystal Mines: ok", summary.Output);

        var run = await RunAsync(project, "run", "scene", root, "Main", "0.1");
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Simulated Main", run.Output);
        Assert.Contains("GridBoard", run.Output);

        var sceneSummary = await RunAsync(project, "context", "scene", root, "Main");
        Assert.Equal(0, sceneSummary.ExitCode);
        Assert.Contains("Scene Main", sceneSummary.Output);
        Assert.Contains("PuzzleGrid", sceneSummary.Output);

        var schemas = await RunAsync(project, "module", "schemas");
        Assert.Equal(0, schemas.ExitCode);
        Assert.Contains("Loaded", schemas.Output);
        Assert.Contains("Transform", schemas.Output);

        var scaffold = await RunAsync(project, "module", "scaffold", root, "crystal.mining", "Crystal Mining", "CrystalMining", "MiningController");
        Assert.Equal(0, scaffold.ExitCode);
        Assert.Contains("CrystalMiningModule.cs", scaffold.Output);
        Assert.True(File.Exists(Path.Combine(root, "Modules", "CrystalMining", "CrystalMiningModule.cs")));

        var capture = await RunAsync(project, "capture", "screenshot", root, "Main");
        Assert.Equal(0, capture.ExitCode);
        Assert.Contains("Main_preview.png", capture.Output);
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
