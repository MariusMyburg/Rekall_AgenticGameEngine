using System.Diagnostics;
using Rekall.Age.Core.Commands;

namespace Rekall.Age.Build.Commands;

public sealed record BuildPlayerRequest(
    string ProjectRoot,
    string SceneName,
    string? OutputDirectory = null);

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
        var playerProject = FindPlayerProjectPath();
        var outputDirectory = request.OutputDirectory
            ?? Path.Combine(request.ProjectRoot, "Builds", "RekallAgePlayer");
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

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start dotnet publish.");
        var output = await process.StandardOutput.ReadToEndAsync(context.CancellationToken);
        output += await process.StandardError.ReadToEndAsync(context.CancellationToken);
        await process.WaitForExitAsync(context.CancellationToken);

        var launchPath = FindLaunchPath(outputDirectory);
        var result = new BuildPlayerResult(
            outputDirectory,
            launchPath,
            [request.ProjectRoot, request.SceneName],
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

    private static string FindPlayerProjectPath()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    "src",
                    "Rekall.Age.Player",
                    "Rekall.Age.Player.csproj");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException("Could not locate src/Rekall.Age.Player/Rekall.Age.Player.csproj.");
    }

    private static string FindLaunchPath(string outputDirectory)
    {
        var executable = Path.Combine(outputDirectory, OperatingSystem.IsWindows() ? "Rekall.Age.Player.exe" : "Rekall.Age.Player");
        if (File.Exists(executable))
        {
            return executable;
        }

        return Path.Combine(outputDirectory, "Rekall.Age.Player.dll");
    }
}
