using System.Diagnostics;

namespace Rekall.Age.Tests.Cli;

/// <summary>
/// Proves the CLI actually reaches <c>rekall.game.publish_web</c>/<c>rekall.game.audit_web</c> and reports their
/// result faithfully. This deliberately exercises only the failure path (an authored project that does not exist):
/// the full success path -- a real static-module build plus a real trimmed browser-wasm publish -- is already
/// proven end-to-end, at the command level, by
/// <see cref="Rekall.Age.Tests.Workflows.WebGamePublishingTests.PublishesAndAuditsARealWebGameEndToEnd"/>; running
/// that same multi-minute pipeline a second time here, through a CLI subprocess, would only prove argument
/// plumbing, at several times the cost.
/// </summary>
public sealed class WebGameCliTests
{
    [Fact]
    public async Task PublishWebReportsAFailureForAMissingProjectRoot()
    {
        var missingRoot = Path.Combine(TestPaths.CreateTempDirectory(), "does-not-exist");

        var result = await RunAsync(FindCliAssemblyPath(), "game", "publish-web", missingRoot);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("REKALL_WEB_GAME_PUBLISH", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuditWebReportsAFailureForAMissingProjectRoot()
    {
        var missingRoot = Path.Combine(TestPaths.CreateTempDirectory(), "does-not-exist");

        var result = await RunAsync(FindCliAssemblyPath(), "game", "audit-web", missingRoot);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Ready: False", result.Output, StringComparison.Ordinal);
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string cliAssembly, params string[] args)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.ArgumentList.Add(cliAssembly);
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask + await errorTask;
        return (process.ExitCode, output);
    }

    private static string FindCliAssemblyPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Rekall.Age.Cli", "bin", "Release", "net10.0", "Rekall.Age.Cli.dll");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            candidate = Path.Combine(directory.FullName, "src", "Rekall.Age.Cli", "bin", "Debug", "net10.0", "Rekall.Age.Cli.dll");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate a built Rekall.Age.Cli.dll from the test output directory.");
    }
}
