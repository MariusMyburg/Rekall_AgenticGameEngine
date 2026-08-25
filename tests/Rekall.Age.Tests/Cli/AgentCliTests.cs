using System.Diagnostics;

namespace Rekall.Age.Tests.Cli;

public sealed class AgentCliTests
{
    [Fact]
    public async Task ProviderCommandsExposeBothProvidersAndFailClosedWithoutLeakingSessionCredentials()
    {
        const string sessionKey = "session-key-must-not-appear";
        var cli = FindCliAssemblyPath();

        var providers = await RunAsync(cli, null, "agent", "providers");
        var missingAuth = await RunAsync(
            cli,
            new Dictionary<string, string> { ["OPENAI_API_KEY"] = string.Empty },
            "agent", "models", "openai");
        var unsupportedModel = await RunAsync(
            cli,
            new Dictionary<string, string> { ["OPENAI_API_KEY"] = sessionKey },
            "agent", "run", "openai", "not-gpt-5.6-sol", "inspect the project", "1");

        Assert.Equal(0, providers.ExitCode);
        Assert.Contains("ollama\tOllama\tqwen3.5:35b\tnone", providers.Output, StringComparison.Ordinal);
        Assert.Contains("openai\tOpenAI\tgpt-5.6-sol\tapi-key", providers.Output, StringComparison.Ordinal);
        Assert.Equal(1, missingAuth.ExitCode);
        Assert.Contains("REKALL_OPENAI_API_KEY_MISSING", missingAuth.Output, StringComparison.Ordinal);
        Assert.Equal(1, unsupportedModel.ExitCode);
        Assert.Contains("REKALL_OPENAI_MODEL_UNSUPPORTED", unsupportedModel.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(sessionKey, providers.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(sessionKey, missingAuth.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(sessionKey, unsupportedModel.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExistingOllamaAgentFormsStillReachTheirOriginalArgumentValidation()
    {
        var result = await RunAsync(
            FindCliAssemblyPath(),
            null,
            "agent", "run-project", "ollama", "qwen3.5:35b", ".", "Main", "inspect", "zero");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Invalid maximum turn count 'zero'.", result.Output, StringComparison.Ordinal);
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(
        string cliAssembly,
        IReadOnlyDictionary<string, string>? environment,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = FindRepositoryRoot()
        };
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        if (environment is not null)
        {
            foreach (var item in environment)
            {
                startInfo.Environment[item.Key] = item.Value;
            }
        }

        startInfo.ArgumentList.Add(cliAssembly);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await output + await error);
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
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rekall.AGE.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }
}
