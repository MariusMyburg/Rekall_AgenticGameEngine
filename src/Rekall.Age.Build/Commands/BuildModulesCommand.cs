using System.Diagnostics;
using Rekall.Age.Core.Commands;

namespace Rekall.Age.Build.Commands;

public sealed record BuildModulesRequest(string ProjectRoot);

public sealed record BuildModulesResult(IReadOnlyList<BuildModuleResult> Modules);

public sealed record BuildModuleResult(
    string ModuleName,
    string ProjectPath,
    string AssemblyPath,
    bool Succeeded,
    string Output);

public sealed class BuildModulesCommand
    : IRekallAgeCommand<BuildModulesRequest, BuildModulesResult>
{
    public string Name => "rekall.build.modules";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Builds C# module projects under a Rekall AGE project.",
        typeof(BuildModulesRequest).FullName!,
        typeof(BuildModulesResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<BuildModulesResult>> ExecuteAsync(
        BuildModulesRequest request,
        RekallAgeCommandContext context)
    {
        var moduleProjects = FindModuleProjects(request.ProjectRoot);
        var results = new List<BuildModuleResult>();

        foreach (var projectPath in moduleProjects)
        {
            var result = await BuildProjectAsync(projectPath, context.CancellationToken);
            results.Add(result);
            if (result.Succeeded)
            {
                context.Transaction.RecordChangedResource(result.AssemblyPath);
            }
        }

        var value = new BuildModulesResult(results);
        if (results.Count == 0)
        {
            return RekallAgeCommandResult<BuildModulesResult>.Failure(
                value,
                "No module projects were found.",
                [new RekallAgeCommandError("REKALL_MODULE_PROJECTS_MISSING", "No module projects were found.", request.ProjectRoot)]);
        }

        if (results.Any(result => !result.Succeeded))
        {
            return RekallAgeCommandResult<BuildModulesResult>.Failure(
                value,
                "One or more module projects failed to build.",
                results
                    .Where(result => !result.Succeeded)
                    .Select(result => new RekallAgeCommandError("REKALL_MODULE_BUILD_FAILED", result.Output, result.ProjectPath))
                    .ToArray());
        }

        return RekallAgeCommandResult<BuildModulesResult>.Success(
            value,
            $"Built {results.Count} module project(s).");
    }

    private static IReadOnlyList<string> FindModuleProjects(string projectRoot)
    {
        var modulesRoot = Path.Combine(projectRoot, "Modules");
        if (!Directory.Exists(modulesRoot))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(modulesRoot, "*.csproj", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task<BuildModuleResult> BuildProjectAsync(string projectPath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(projectPath)!
        };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add("-v:minimal");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start dotnet build.");
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        output += await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var moduleName = Path.GetFileNameWithoutExtension(projectPath);
        var assemblyPath = Path.Combine(
            Path.GetDirectoryName(projectPath)!,
            "bin",
            "Debug",
            "net10.0",
            $"{moduleName}.dll");

        return new BuildModuleResult(
            moduleName,
            projectPath,
            assemblyPath,
            process.ExitCode == 0 && File.Exists(assemblyPath),
            output);
    }
}
