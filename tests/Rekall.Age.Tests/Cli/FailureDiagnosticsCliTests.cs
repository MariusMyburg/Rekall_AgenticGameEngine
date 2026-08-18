using System.Diagnostics;
using Rekall.Age.Core.Diagnostics;

namespace Rekall.Age.Tests.Cli;

public sealed class FailureDiagnosticsCliTests
{
    [Fact]
    public async Task CliPrintsCompactFailureFactsAndNoStackExcerpt()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeFailureReportStore(root).WriteAsync(
            RekallAgeFailureReport.Create(
                "player.windows", "recovered", "graphics.device-lost", "RECOVERED",
                "cold-session-restart", 2, 10, 10, "System.Exception", "device lost",
                "SECRET_STACK_EXCERPT", "vulkan", "F:/Game", "Main"),
            CancellationToken.None);

        var result = await RunAsync(FindCliAssemblyPath(), "diagnostics", "failures", root);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("RECOVERED", result.Output);
        Assert.Contains("recovered", result.Output);
        Assert.Contains("player.windows", result.Output);
        Assert.DoesNotContain("SECRET_STACK_EXCERPT", result.Output);
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string cliAssembly, params string[] args)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = FindRepositoryRoot()
        };
        startInfo.ArgumentList.Add(cliAssembly);
        foreach (var argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, output + error);
    }

    private static string FindCliAssemblyPath() =>
        Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Rekall.Age.Cli",
            "bin",
            "Debug",
            "net10.0",
            "Rekall.Age.Cli.dll");

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
