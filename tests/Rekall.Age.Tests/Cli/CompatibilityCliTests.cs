using System.Diagnostics;

namespace Rekall.Age.Tests.Cli;

public sealed class CompatibilityCliTests
{
    [Fact]
    public async Task CliInspectsLegacyProjectWithoutMutation()
    {
        var root = TestPaths.CreateTempDirectory();
        var path = Path.Combine(root, "rekall.project.json");
        const string legacy = """{"name":"Legacy CLI","capabilities":[]}""";
        await File.WriteAllTextAsync(path, legacy);

        var result = await RunAsync(FindCliAssemblyPath(), "compatibility", "inspect", root);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("REKALL_DOCUMENT_SCHEMA_LEGACY", result.Output);
        Assert.Contains("migratable: True", result.Output);
        Assert.Equal(legacy, await File.ReadAllTextAsync(path));
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
