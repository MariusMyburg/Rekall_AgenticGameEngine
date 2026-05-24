using System.Diagnostics;

namespace Rekall.Age.Tests.Cli;

public sealed class CliSmokeTests
{
    [Fact]
    public async Task CliCreatesTemplateProjectAndPrintsSummary()
    {
        var root = TestPaths.CreateTempDirectory();
        var project = Path.Combine(FindRepositoryRoot(), "src", "Rekall.Age.Cli", "Rekall.Age.Cli.csproj");

        var template = await RunAsync(project, "templates", "inspect", "puzzle");
        Assert.Equal(0, template.ExitCode);
        Assert.Contains("puzzle: Puzzle Game", template.Output);
        Assert.Contains("rekall.workflow.create_playable_package_from_template", template.Output);

        var engine = await RunAsync(project, "context", "engine");
        Assert.Equal(0, engine.ExitCode);
        Assert.Contains("Rekall AGE", engine.Output);
        Assert.Contains("Agent-first: True", engine.Output);
        Assert.Contains("rekall.workflow.create_playable_package_from_template", engine.Output);

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

        var build = await RunAsync(project, "build", "modules", root);
        Assert.Equal(0, build.ExitCode);
        Assert.Contains("Built 1 module project", build.Output);
        Assert.Contains("CrystalMining.dll", build.Output);

        var capture = await RunAsync(project, "capture", "screenshot", root, "Main");
        Assert.Equal(0, capture.ExitCode);
        Assert.Contains("Main_preview.png", capture.Output);
    }

    [Fact]
    public async Task CliPlaytestsPlayableTemplateWithJsonAssertions()
    {
        var root = TestPaths.CreateTempDirectory();
        var project = Path.Combine(FindRepositoryRoot(), "src", "Rekall.Age.Cli", "Rekall.Age.Cli.csproj");

        var create = await RunAsync(project, "game", "create-playable", root, "CLI Pong", "pong");
        Assert.Equal(0, create.ExitCode);

        var playtest = await RunAsync(
            project,
            "playtest",
            "scene",
            root,
            "Main",
            "2",
            """[{"verticalAxis":1,"primaryAction":true},{"verticalAxis":-1,"primaryAction":false}]""",
            """[{"frameIndex":0,"contains":"Score 10"},{"frameIndex":1,"contains":"Left paddle lane 0"}]""",
            """[{"frameIndex":0,"id":"ball","kind":"circle"},{"frameIndex":0,"kind":"text","textContains":"Score 10"}]""");

        Assert.Equal(0, playtest.ExitCode);
        Assert.Contains("Passed: True", playtest.Output);
        Assert.Contains("Assertion frame 0 contains \"Score 10\": True", playtest.Output);
        Assert.Contains("Draw assertion frame 0 id=ball kind=circle text=<any>: True", playtest.Output);

        var verify = await RunAsync(
            project,
            "game",
            "verify-playable",
            root,
            "Main",
            "1",
            """[{"verticalAxis":1,"primaryAction":true}]""",
            """[{"frameIndex":0,"contains":"Score 10"}]""",
            """[{"frameIndex":0,"id":"ball","kind":"circle"}]""");
        Assert.Equal(0, verify.ExitCode);
        Assert.Contains("Ready: True", verify.Output);
        Assert.Contains("Draw assertion frame 0 id=ball kind=circle text=<any>: True", verify.Output);

        var captureDirectory = Path.Combine(root, "PlayCaptures");
        var capture = await RunAsync(project, "play", "capture-frame", root, "Main", captureDirectory, "1");
        Assert.Equal(0, capture.ExitCode);
        Assert.Contains("Main_play_frame_001.png", capture.Output);
        Assert.True(File.Exists(Path.Combine(captureDirectory, "Main_play_frame_001.png")));

        var packageDirectory = Path.Combine(root, "Packaged");
        var package = await RunAsync(project, "game", "package-playable", root, "Main", packageDirectory);
        Assert.Equal(0, package.ExitCode);

        var inspect = await RunAsync(project, "game", "inspect-package", $"{packageDirectory}.zip");
        Assert.Equal(0, inspect.ExitCode);
        Assert.Contains("Ready: True", inspect.Output);
        Assert.Contains("Template: pong", inspect.Output);
        Assert.Contains("Draw commands:", inspect.Output);

        var runPackage = await RunAsync(project, "game", "run-package", $"{packageDirectory}.zip", "1");
        Assert.Equal(0, runPackage.ExitCode);
        Assert.Contains("Ready: True", runPackage.Output);
        Assert.Contains("FRAME 1", runPackage.Output);
        Assert.Contains("PONG", runPackage.Output);

        var packageFrameDirectory = Path.Combine(root, "PackageFrames");
        var capturePackageFrame = await RunAsync(project, "game", "capture-package-frame", $"{packageDirectory}.zip", packageFrameDirectory, "1");
        Assert.Equal(0, capturePackageFrame.ExitCode);
        Assert.Contains("Captured: True", capturePackageFrame.Output);
        Assert.Contains("Non-blank: True", capturePackageFrame.Output);
        Assert.True(File.Exists(Path.Combine(packageFrameDirectory, "package_play_frame_001.png")));

        var oneShotRoot = TestPaths.CreateTempDirectory();
        var oneShotOutput = Path.Combine(oneShotRoot, "Packaged");
        var oneShotFrames = Path.Combine(oneShotRoot, "Frames");
        var oneShot = await RunAsync(
            project,
            "game",
            "create-package-playable",
            oneShotRoot,
            "CLI One Shot Pong",
            "pong",
            oneShotOutput,
            oneShotFrames);
        Assert.Equal(0, oneShot.ExitCode);
        Assert.Contains("Ready: True", oneShot.Output);
        Assert.Contains("Archive:", oneShot.Output);
        Assert.Contains("Capture:", oneShot.Output);
        Assert.True(File.Exists($"{oneShotOutput}.zip"));
        Assert.True(File.Exists(Path.Combine(oneShotFrames, "package_play_frame_001.png")));
    }

    [Fact]
    public async Task CliReportsInvalidPlaytestJsonWithoutUnhandledException()
    {
        var root = TestPaths.CreateTempDirectory();
        var project = Path.Combine(FindRepositoryRoot(), "src", "Rekall.Age.Cli", "Rekall.Age.Cli.csproj");

        var create = await RunAsync(project, "game", "create-playable", root, "CLI Pong", "pong");
        Assert.Equal(0, create.ExitCode);

        var playtest = await RunAsync(project, "playtest", "scene", root, "Main", "1", "[{verticalAxis:1}]", "[]");

        Assert.Equal(1, playtest.ExitCode);
        Assert.Contains("JSON is invalid", playtest.Output);
        Assert.DoesNotContain("Unhandled exception", playtest.Output);
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
