using Rekall.Age.Core.Commands;
using Rekall.Age.Playback;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Rekall.Age.GameTemplates.Commands;

public sealed record RunPlayablePackageRequest(
    string PackagePath,
    int Frames = 2,
    IReadOnlyList<RekallAgePlaybackInput>? Inputs = null);

public sealed record RunPlayablePackageResult(
    bool Ready,
    string ManifestPath,
    string LaunchPath,
    string GameRoot,
    int ExitCode,
    IReadOnlyList<string> Frames,
    string Output);

public sealed class RunPlayablePackageCommand
    : IRekallAgeCommand<RunPlayablePackageRequest, RunPlayablePackageResult>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly InspectPlayablePackageCommand _inspectPackage = new();

    public string Name => "rekall.workflow.run_playable_package";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Runs a packaged playable game through the published Rekall AGE player and returns deterministic frames.",
        typeof(RunPlayablePackageRequest).FullName!,
        typeof(RunPlayablePackageResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<RunPlayablePackageResult>> ExecuteAsync(
        RunPlayablePackageRequest request,
        RekallAgeCommandContext context)
    {
        var package = PreparePackage(request.PackagePath);
        try
        {
            var inspect = await _inspectPackage.ExecuteAsync(
                new InspectPlayablePackageRequest(package.InspectPath),
                context);
            var manifest = inspect.Value.Manifest;
            var launchPath = ResolvePackagedLaunchPath(package.PackageRoot, manifest.LaunchPath);
            var gameRoot = ResolvePackagedGameRoot(package.PackageRoot);
            var frameCount = Math.Clamp(request.Frames, 1, 600);
            var run = await RunPlayerAsync(
                launchPath,
                gameRoot,
                manifest.SceneName,
                frameCount,
                request.Inputs,
                context.CancellationToken);
            var ready = inspect.Value.Ready && run.ExitCode == 0;
            var result = new RunPlayablePackageResult(
                ready,
                inspect.Value.ManifestPath,
                launchPath,
                gameRoot,
                run.ExitCode,
                ParseFrames(run.Output),
                run.Output);
            if (!ready)
            {
                return RekallAgeCommandResult<RunPlayablePackageResult>.Failure(
                    result,
                    $"Packaged playable run failed with exit code {run.ExitCode}.",
                    [
                        new RekallAgeCommandError(
                            "REKALL_PLAYABLE_PACKAGE_RUN_FAILED",
                            run.Output,
                            launchPath)
                    ]);
            }

            return RekallAgeCommandResult<RunPlayablePackageResult>.Success(
                result,
                $"Ran packaged playable game '{manifest.SceneName}' for {frameCount} frame(s).");
        }
        finally
        {
            package.Dispose();
        }
    }

    private static PreparedPackage PreparePackage(string packagePath)
    {
        var fullPath = Path.GetFullPath(packagePath);
        if (Directory.Exists(fullPath))
        {
            return new PreparedPackage(fullPath, fullPath, null);
        }

        if (File.Exists(fullPath) && Path.GetExtension(fullPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var extractionRoot = Path.Combine(Path.GetTempPath(), "RekallAgePackageRuns", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(extractionRoot);
            ZipFile.ExtractToDirectory(fullPath, extractionRoot);
            return new PreparedPackage(extractionRoot, extractionRoot, extractionRoot);
        }

        if (File.Exists(fullPath))
        {
            var packageRoot = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException($"Package manifest '{fullPath}' does not have a parent directory.");
            return new PreparedPackage(packageRoot, fullPath, null);
        }

        throw new FileNotFoundException($"Playable package path '{fullPath}' does not exist.", fullPath);
    }

    private static string ResolvePackagedLaunchPath(string packageRoot, string manifestLaunchPath)
    {
        var launchFileName = Path.GetFileName(manifestLaunchPath);
        var launchPath = Path.Combine(packageRoot, launchFileName);
        if (!File.Exists(launchPath))
        {
            throw new InvalidOperationException($"Package player '{launchPath}' does not exist.");
        }

        return launchPath;
    }

    private static string ResolvePackagedGameRoot(string packageRoot)
    {
        var gameRoot = Path.Combine(packageRoot, "Game");
        if (!Directory.Exists(gameRoot))
        {
            throw new InvalidOperationException($"Package game root '{gameRoot}' does not exist.");
        }

        return gameRoot;
    }

    private static async Task<(int ExitCode, string Output)> RunPlayerAsync(
        string launchPath,
        string gameRoot,
        string sceneName,
        int frames,
        IReadOnlyList<RekallAgePlaybackInput>? inputs,
        CancellationToken cancellationToken)
    {
        var startInfo = Path.GetExtension(launchPath).Equals(".dll", StringComparison.OrdinalIgnoreCase)
            ? new ProcessStartInfo("dotnet")
            : new ProcessStartInfo(launchPath);
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.WorkingDirectory = Path.GetDirectoryName(launchPath)!;
        if (Path.GetExtension(launchPath).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add(launchPath);
        }

        startInfo.ArgumentList.Add(gameRoot);
        startInfo.ArgumentList.Add(sceneName);
        startInfo.ArgumentList.Add("--frames");
        startInfo.ArgumentList.Add(frames.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (inputs is { Count: > 0 })
        {
            startInfo.ArgumentList.Add("--inputs");
            startInfo.ArgumentList.Add(JsonSerializer.Serialize(inputs, JsonOptions));
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start packaged Rekall AGE player.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask + await errorTask;
        return (process.ExitCode, output);
    }

    private static IReadOnlyList<string> ParseFrames(string output)
    {
        var frames = new List<string>();
        using var reader = new StringReader(output);
        StringBuilder? current = null;
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("FRAME ", StringComparison.Ordinal))
            {
                if (current is not null)
                {
                    frames.Add(current.ToString());
                }

                current = new StringBuilder();
            }

            current?.AppendLine(line);
        }

        if (current is not null)
        {
            frames.Add(current.ToString());
        }

        return frames;
    }

    private sealed record PreparedPackage(
        string PackageRoot,
        string InspectPath,
        string? TemporaryRoot)
        : IDisposable
    {
        public void Dispose()
        {
            if (TemporaryRoot is null || !Directory.Exists(TemporaryRoot))
            {
                return;
            }

            try
            {
                Directory.Delete(TemporaryRoot, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
