using System.Diagnostics;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Diagnostics;
using Rekall.Age.Core.Transactions;

namespace Rekall.Age.Tests.Cli;

public sealed class WindowsPlayerRecoveryTests
{
    [Fact]
    public async Task WindowsPlayerRecoversOnceAndFailsClosedForFatalOrExhaustedRuns()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext(
            "recovery-test",
            RekallAgeTransaction.Begin("author recovery scene"),
            CancellationToken.None);
        await TestProjectAuthoring.CreateProjectWithSceneAsync(root, context, "Recovery Game");
        var executable = FindWindowsPlayer();

        var recoveredRoot = Path.Combine(root, "diagnostics-recovered");
        var recovered = await RunAsync(
            executable,
            recoveredRoot,
            root, "Main", "--frames", "5", "--no-vsync", "--simulate-device-loss-frame", "2");
        var recoveredReports = await new RekallAgeFailureReportStore(recoveredRoot).ReadAsync();

        Assert.Equal(0, recovered.ExitCode);
        Assert.Contains("REKALL_PLAYER_GRAPHICS_RECOVERED", recovered.Output);
        Assert.Contains("Frames: 5/5", recovered.Output);
        var recoveredReport = Assert.Single(recoveredReports.Reports);
        Assert.Equal("recovered", recoveredReport.Outcome);
        Assert.Equal("cold-session-restart", recoveredReport.RecoveryMode);
        Assert.Equal(2, recoveredReport.Attempts);
        Assert.Equal(5, recoveredReport.CompletedFrames);

        var fatalRoot = Path.Combine(root, "diagnostics-fatal");
        var fatal = await RunAsync(
            executable,
            fatalRoot,
            root, "Main", "--frames", "5", "--no-vsync", "--simulate-fatal-frame", "2");
        var fatalReport = Assert.Single((await new RekallAgeFailureReportStore(fatalRoot).ReadAsync()).Reports);

        Assert.Equal(10, fatal.ExitCode);
        Assert.Contains("REKALL_PLAYER_RUNTIME_FATAL", fatal.Output);
        Assert.Equal("fatal", fatalReport.Outcome);
        Assert.Equal(1, fatalReport.Attempts);

        var exhaustedRoot = Path.Combine(root, "diagnostics-exhausted");
        var exhausted = await RunAsync(
            executable,
            exhaustedRoot,
            root, "Main", "--frames", "5", "--no-vsync", "--simulate-device-loss-always-frame", "2");
        var exhaustedReport = Assert.Single((await new RekallAgeFailureReportStore(exhaustedRoot).ReadAsync()).Reports);

        Assert.Equal(11, exhausted.ExitCode);
        Assert.Contains("REKALL_PLAYER_GRAPHICS_RECOVERY_EXHAUSTED", exhausted.Output);
        Assert.Equal("exhausted", exhaustedReport.Outcome);
        Assert.Equal(3, exhaustedReport.Attempts);
        Assert.Equal(3, exhaustedReport.CompletedFrames);
    }

    private static string FindWindowsPlayer()
    {
        var repositoryRoot = FindRepositoryRoot();
        var executable = Path.Combine(
            repositoryRoot,
            "src",
            "Rekall.Age.Player.Windows",
            "bin",
            "Debug",
            "net10.0-windows",
            "Rekall.Age.Player.Windows.exe");
        Assert.True(File.Exists(executable), executable);
        return executable;
    }

    private static Task<(int ExitCode, string Output)> RunAsync(
        string executable,
        string diagnosticsRoot,
        params string[] arguments) =>
        RunProcessAsync(executable, diagnosticsRoot, arguments);

    private static async Task<(int ExitCode, string Output)> RunProcessAsync(
        string executable,
        string? diagnosticsRoot,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = FindRepositoryRoot()
        };
        if (diagnosticsRoot is not null)
        {
            startInfo.Environment[RekallAgeFailureReportStore.DiagnosticsDirectoryVariable] = diagnosticsRoot;
        }
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Process '{executable}' exceeded the 45-second test bound.");
        }
        return (process.ExitCode, await standardOutput + await standardError);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Rekall.AGE.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
