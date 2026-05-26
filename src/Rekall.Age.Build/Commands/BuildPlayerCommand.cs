using System.Diagnostics;
using Rekall.Age.Core.Commands;

namespace Rekall.Age.Build.Commands;

public sealed record BuildPlayerRequest(
    string ProjectRoot,
    string SceneName,
    string? OutputDirectory = null,
    bool Graphics = false);

public sealed record BuildPlayerResult(
    string OutputDirectory,
    string LaunchPath,
    IReadOnlyList<string> Arguments,
    string Output);

public sealed class BuildPlayerCommand : IRekallAgeCommand<BuildPlayerRequest, BuildPlayerResult>
{
    public string Name => "rekall.build.player";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Publishes the Rekall AGE player for a project and returns launch details.",
        typeof(BuildPlayerRequest).FullName!,
        typeof(BuildPlayerResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<BuildPlayerResult>> ExecuteAsync(
        BuildPlayerRequest request,
        RekallAgeCommandContext context)
    {
        var playerProject = FindPlayerProjectPath(request.Graphics);
        var outputDirectory = Path.GetFullPath(
            request.OutputDirectory
                ?? Path.Combine(request.ProjectRoot, "Builds", "RekallAgePlayer"));
        Directory.CreateDirectory(outputDirectory);

        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(playerProject)!
        };
        startInfo.ArgumentList.Add("publish");
        startInfo.ArgumentList.Add(playerProject);
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("Debug");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(outputDirectory);
        startInfo.ArgumentList.Add("/nr:false");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start dotnet publish.");
        var outputTask = process.StandardOutput.ReadToEndAsync(context.CancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(context.CancellationToken);
        await process.WaitForExitAsync(context.CancellationToken);
        var output = await outputTask + await errorTask;

        var launchPath = FindLaunchPath(outputDirectory);
        var result = new BuildPlayerResult(
            outputDirectory,
            launchPath,
            request.Graphics
                ? [request.ProjectRoot, request.SceneName, "--graphics", "--backend", "vulkan"]
                : [request.ProjectRoot, request.SceneName],
            output);
        if (process.ExitCode != 0 || !File.Exists(launchPath))
        {
            var error = new RekallAgeCommandError(
                "REKALL_PLAYER_BUILD_FAILED",
                output,
                playerProject);
            return RekallAgeCommandResult<BuildPlayerResult>.Failure(
                result,
                "Player publish failed.",
                [error]);
        }

        context.Transaction.RecordChangedResource(outputDirectory);
        return RekallAgeCommandResult<BuildPlayerResult>.Success(
            result,
            $"Built playable player at '{launchPath}'.");
    }

    private static string FindPlayerProjectPath(bool graphics)
    {
        var projectDirectoryName = graphics ? "Rekall.Age.Player.Windows" : "Rekall.Age.Player";
        var projectFileName = $"{projectDirectoryName}.csproj";
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    "src",
                    projectDirectoryName,
                    projectFileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException($"Could not locate src/{projectDirectoryName}/{projectFileName}.");
    }

    private static string FindLaunchPath(string outputDirectory)
    {
        foreach (var baseName in new[] { "Rekall.Age.Player.Windows", "Rekall.Age.Player" })
        {
            var executable = Path.Combine(outputDirectory, OperatingSystem.IsWindows() ? $"{baseName}.exe" : baseName);
            if (File.Exists(executable))
            {
                return executable;
            }

            var assembly = Path.Combine(outputDirectory, $"{baseName}.dll");
            if (File.Exists(assembly))
            {
                return assembly;
            }
        }

        return Path.Combine(outputDirectory, "Rekall.Age.Player.dll");
    }
}
