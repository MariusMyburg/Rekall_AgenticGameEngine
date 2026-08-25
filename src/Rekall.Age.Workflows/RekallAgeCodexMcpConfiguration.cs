using Rekall.Age.Agent.Codex;
using Rekall.Age.Agent.LanguageModels;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Product;

namespace Rekall.Age.Workflows;

public sealed class RekallAgeCodexMcpConfiguration
{
    public const string ServerName = "rekall-age";

    public RekallAgeCodexMcpConfiguration(string cliExecutablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cliExecutablePath);
        CliExecutablePath = Path.GetFullPath(cliExecutablePath);
    }

    public string CliExecutablePath { get; }

    public static RekallAgeCodexMcpConfiguration Resolve(string? startPath = null)
    {
        var configuredDistribution = Environment.GetEnvironmentVariable("REKALL_AGE_DISTRIBUTION_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredDistribution))
        {
            var configured = CandidateFromDistribution(RekallAgeDistributionLayout.Create(configuredDistribution));
            if (File.Exists(configured))
            {
                return new RekallAgeCodexMcpConfiguration(configured);
            }
        }

        var start = Path.GetFullPath(startPath ?? AppContext.BaseDirectory);
        if (RekallAgeDistributionLayout.TryFind(start, out var distribution))
        {
            var packaged = CandidateFromDistribution(distribution);
            if (File.Exists(packaged))
            {
                return new RekallAgeCodexMcpConfiguration(packaged);
            }
        }

        var directory = new DirectoryInfo(File.Exists(start) ? Path.GetDirectoryName(start)! : start);
        var executableName = ExecutableName();
        while (directory is not null)
        {
            foreach (var candidate in new[]
            {
                Path.Combine(directory.FullName, executableName),
                Path.Combine(directory.FullName, "src", "Rekall.Age.Cli", "bin", "Release", "net10.0", executableName),
                Path.Combine(directory.FullName, "src", "Rekall.Age.Cli", "bin", "Debug", "net10.0", executableName)
            })
            {
                if (File.Exists(candidate))
                {
                    return new RekallAgeCodexMcpConfiguration(candidate);
                }
            }

            directory = directory.Parent;
        }

        throw new RekallAgeLanguageModelProviderException(
            RekallAgeCodexErrorCodes.RuntimeMissing,
            "codex",
            "The packaged Rekall AGE CLI executable required for Codex MCP tools is unavailable.");
    }

    public RekallAgeCodexMcpServer CreateValidatedServer(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ValidateExecutable();

        var normalizedProjectRoot = RekallAgeProjectCommandScope.NormalizeProjectRoot(projectRoot);
        return new RekallAgeCodexMcpServer(
            ServerName,
            CliExecutablePath,
            ["mcp", "stdio", "--project-root", normalizedProjectRoot]);
    }

    public void ValidateExecutable()
    {
        if (!File.Exists(CliExecutablePath))
        {
            throw new RekallAgeLanguageModelProviderException(
                RekallAgeCodexErrorCodes.RuntimeMissing,
                "codex",
                "The packaged Rekall AGE CLI executable required for Codex MCP tools is unavailable.");
        }
    }

    private static string CandidateFromDistribution(RekallAgeDistributionPaths distribution) =>
        Path.Combine(distribution.Cli, ExecutableName());

    private static string ExecutableName() =>
        OperatingSystem.IsWindows() ? "Rekall.Age.Cli.exe" : "Rekall.Age.Cli";
}
