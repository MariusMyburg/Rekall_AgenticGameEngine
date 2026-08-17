using System.Diagnostics;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Product;

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
    private readonly string? _distributionRoot;

    public BuildPlayerCommand(string? distributionRoot = null)
    {
        _distributionRoot = distributionRoot;
    }

    public string Name => "rekall.build.player";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Publishes a raw Rekall AGE player and returns launch details; this does not create a playable package. For package creation, integrity inventory, and an archive, use rekall.workflow.package_playable_game.",
        typeof(BuildPlayerRequest).FullName!,
        typeof(BuildPlayerResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<BuildPlayerResult>> ExecuteAsync(
        BuildPlayerRequest request,
        RekallAgeCommandContext context)
    {
        var outputDirectory = Path.GetFullPath(
            request.OutputDirectory
                ?? Path.Combine(request.ProjectRoot, "Builds", "RekallAgePlayer"));
        var distribution = FindDistribution();
        if (distribution is not null)
        {
            return BuildFromDistribution(request, outputDirectory, distribution, context);
        }

        var playerProject = FindPlayerProjectPath(request.Graphics);
        Directory.CreateDirectory(outputDirectory);
        var artifactsPath = Path.Combine(outputDirectory, ".rekall-publish");

        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(playerProject)!
        };
        startInfo.ArgumentList.Add("publish");
        startInfo.ArgumentList.Add(playerProject);
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add("-p:RestoreLockedMode=true");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("Debug");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(outputDirectory);
        startInfo.ArgumentList.Add("--artifacts-path");
        startInfo.ArgumentList.Add(artifactsPath);
        startInfo.ArgumentList.Add("/nr:false");

        string output;
        int exitCode;
        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start dotnet publish.");
            var outputTask = process.StandardOutput.ReadToEndAsync(context.CancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(context.CancellationToken);
            await process.WaitForExitAsync(context.CancellationToken);
            output = await outputTask + await errorTask;
            exitCode = process.ExitCode;
        }
        finally
        {
            if (Directory.Exists(artifactsPath))
            {
                Directory.Delete(artifactsPath, recursive: true);
            }
        }

        var launchPath = FindLaunchPath(outputDirectory);
        var result = new BuildPlayerResult(
            outputDirectory,
            launchPath,
            request.Graphics
                ? [request.ProjectRoot, request.SceneName, "--graphics", "--backend", "vulkan"]
                : [request.ProjectRoot, request.SceneName],
            output);
        if (exitCode != 0 || !File.Exists(launchPath))
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

    private RekallAgeDistributionPaths? FindDistribution()
    {
        var configuredRoot = _distributionRoot ?? Environment.GetEnvironmentVariable("REKALL_AGE_DISTRIBUTION_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            var paths = RekallAgeDistributionLayout.Create(configuredRoot);
            return File.Exists(paths.Manifest) ? paths : null;
        }

        return RekallAgeDistributionLayout.TryFind(AppContext.BaseDirectory, out var discovered)
            ? discovered
            : null;
    }

    private static RekallAgeCommandResult<BuildPlayerResult> BuildFromDistribution(
        BuildPlayerRequest request,
        string outputDirectory,
        RekallAgeDistributionPaths distribution,
        RekallAgeCommandContext context)
    {
        var payload = request.Graphics
            ? distribution.WindowsPlayerPayload
            : distribution.HeadlessPlayerPayload;
        if (!Directory.Exists(payload))
        {
            var missing = new BuildPlayerResult(outputDirectory, string.Empty, [], string.Empty);
            return RekallAgeCommandResult<BuildPlayerResult>.Failure(
                missing,
                "Installed player payload is missing.",
                [new RekallAgeCommandError(
                    "REKALL_DISTRIBUTION_PLAYER_MISSING",
                    "The installed Rekall AGE distribution does not contain the requested player payload.",
                    request.Graphics ? "players/windows" : "players/headless")]);
        }

        if (!IsSafeOutput(outputDirectory, payload))
        {
            var unsafeResult = new BuildPlayerResult(outputDirectory, string.Empty, [], string.Empty);
            return RekallAgeCommandResult<BuildPlayerResult>.Failure(
                unsafeResult,
                "Player output directory is unsafe.",
                [new RekallAgeCommandError(
                    "REKALL_PLAYER_OUTPUT_UNSAFE",
                    "Player output cannot be a drive root or contain the installed player payload.",
                    outputDirectory)]);
        }

        if (Directory.Exists(outputDirectory))
        {
            Directory.Delete(outputDirectory, recursive: true);
        }

        CopyDirectory(payload, outputDirectory);
        var launchPath = FindLaunchPath(outputDirectory);
        var result = new BuildPlayerResult(
            outputDirectory,
            launchPath,
            request.Graphics
                ? [request.ProjectRoot, request.SceneName, "--graphics", "--backend", "vulkan"]
                : [request.ProjectRoot, request.SceneName],
            $"Copied installed distribution player payload from '{(request.Graphics ? "players/windows" : "players/headless")}'.");
        if (!File.Exists(launchPath))
        {
            return RekallAgeCommandResult<BuildPlayerResult>.Failure(
                result,
                "Installed player payload has no launch executable.",
                [new RekallAgeCommandError(
                    "REKALL_DISTRIBUTION_PLAYER_LAUNCH_MISSING",
                    "The installed player payload does not contain a recognized launch executable.",
                    request.Graphics ? "players/windows" : "players/headless")]);
        }

        context.Transaction.RecordChangedResource(outputDirectory);
        return RekallAgeCommandResult<BuildPlayerResult>.Success(
            result,
            $"Built playable player at '{launchPath}' from the installed distribution.");
    }

    private static bool IsSafeOutput(string outputDirectory, string payload)
    {
        var output = Path.GetFullPath(outputDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var source = Path.GetFullPath(payload)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var driveRoot = Path.GetPathRoot(output)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !string.IsNullOrWhiteSpace(driveRoot) &&
            !output.Equals(driveRoot, PathComparison) &&
            !source.Equals(output, PathComparison) &&
            !source.StartsWith(output + Path.DirectorySeparatorChar, PathComparison);
    }

    private static void CopyDirectory(string sourceRoot, string destinationRoot)
    {
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(destinationRoot, Path.GetRelativePath(sourceRoot, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
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

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
