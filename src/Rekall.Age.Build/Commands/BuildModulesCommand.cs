using System.Diagnostics;
using System.Text.Json;
using Rekall.Age.Core.Commands;
using Rekall.Age.Modules.Security;

namespace Rekall.Age.Build.Commands;

public sealed record BuildModulesRequest(string ProjectRoot);

public sealed record BuildModulesResult(IReadOnlyList<BuildModuleResult> Modules);

public sealed record BuildModuleResult(
    string ModuleName,
    string ProjectPath,
    string AssemblyPath,
    bool Succeeded,
    string Output,
    int ExitCode,
    string SdkVersion);

public sealed class BuildModulesCommand
    : IRekallAgeCommand<BuildModulesRequest, BuildModulesResult>
{
    private readonly RekallAgeModuleBuildPolicy _buildPolicy;

    public BuildModulesCommand()
        : this(new RekallAgeModuleBuildPolicy())
    {
    }

    internal BuildModulesCommand(RekallAgeModuleBuildPolicy buildPolicy)
    {
        _buildPolicy = buildPolicy;
    }

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
        var policy = _buildPolicy.Inspect(request.ProjectRoot);
        var results = new List<BuildModuleResult>();
        if (!policy.Ready)
        {
            return RekallAgeCommandResult<BuildModulesResult>.Failure(
                new BuildModulesResult(results),
                "Module build policy rejected one or more projects before compilation.",
                policy.Issues
                    .Select(issue => new RekallAgeCommandError(
                        "REKALL_MODULE_BUILD_POLICY_REJECTED",
                        issue.Message,
                        issue.Target))
                    .ToArray());
        }

        foreach (var candidate in policy.Candidates)
        {
            ResetVerifiedOutputDirectory(candidate);
            var result = await BuildProjectAsync(candidate, context.CancellationToken);
            results.Add(result);
            if (result.Succeeded)
            {
                context.Transaction.RecordChangedResource(result.AssemblyPath);
            }
        }

        var value = new BuildModulesResult(results);
        if (results.Count == 0)
        {
            var scaffold = new RekallAgeSuggestedCommand(
                "rekall.module.scaffold_playable",
                new Dictionary<string, object?>
                {
                    ["projectRoot"] = request.ProjectRoot,
                    ["moduleId"] = "game.playable",
                    ["displayName"] = "Agent Authored Playable",
                    ["moduleName"] = "Playable"
                });
            return RekallAgeCommandResult<BuildModulesResult>.Failure(
                value,
                "No module projects were found.",
                [new RekallAgeCommandError(
                    "REKALL_MODULE_PROJECTS_MISSING",
                    "No module projects were found. Execute the suggested playable scaffold, author behavior if needed, then rebuild modules.",
                    request.ProjectRoot,
                    [scaffold])]);
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

    private static void ResetVerifiedOutputDirectory(RekallAgeModuleBuildCandidate candidate)
    {
        var output = Path.GetFullPath(candidate.OutputDirectory);
        var module = Path.GetFullPath(candidate.ModuleDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!output.StartsWith(module + Path.DirectorySeparatorChar, comparison))
        {
            throw new InvalidOperationException($"Verified module output '{output}' is outside '{module}'.");
        }

        if (Directory.Exists(output))
        {
            Directory.Delete(output, recursive: true);
        }
    }

    private static async Task<BuildModuleResult> BuildProjectAsync(
        RekallAgeModuleBuildCandidate candidate,
        CancellationToken cancellationToken)
    {
        var projectPath = candidate.ProjectPath;
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
        startInfo.ArgumentList.Add("/nr:false");
        startInfo.ArgumentList.Add("-p:ImportDirectoryBuildProps=false");
        startInfo.ArgumentList.Add("-p:ImportDirectoryBuildTargets=false");
        var portableSdkProject = IsPortableSdkProject(projectPath);
        if (portableSdkProject)
        {
            startInfo.ArgumentList.Add("-p:BaseIntermediateOutputPath=obj/rekall/");
            startInfo.ArgumentList.Add("-p:OutputPath=bin/rekall/net10.0/");
        }

        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start dotnet build.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask + await errorTask;

        var moduleName = candidate.ModuleName;
        var assemblyPath = portableSdkProject
            ? Path.Combine(candidate.OutputDirectory, $"{moduleName}.dll")
            : Path.Combine(Path.GetDirectoryName(projectPath)!, "bin", "Debug", "net10.0", $"{moduleName}.dll");
        var sdkVersion = ReadSdkVersion(projectPath);

        return new BuildModuleResult(
            moduleName,
            projectPath,
            assemblyPath,
            process.ExitCode == 0 && File.Exists(assemblyPath),
            output,
            process.ExitCode,
            sdkVersion);
    }

    private static bool IsPortableSdkProject(string projectPath)
    {
        return File.ReadAllText(projectPath).Contains(
            "Rekall.Age.Sdk.props",
            StringComparison.Ordinal);
    }

    private static string ReadSdkVersion(string projectPath)
    {
        var moduleDirectory = new DirectoryInfo(Path.GetDirectoryName(projectPath)!);
        var projectRoot = moduleDirectory.Parent?.Parent;
        if (projectRoot is null)
        {
            return "unknown";
        }

        var sdkRoot = Path.Combine(projectRoot.FullName, ".rekall", "sdk");
        if (!Directory.Exists(sdkRoot))
        {
            return "unknown";
        }

        foreach (var manifestPath in Directory.EnumerateFiles(sdkRoot, "rekall.sdk.json", SearchOption.AllDirectories))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
                if (document.RootElement.TryGetProperty("compatibilityVersion", out var version))
                {
                    return version.GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            catch (JsonException)
            {
                return "invalid";
            }
        }

        return "unknown";
    }
}
