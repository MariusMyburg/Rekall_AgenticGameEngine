using System.Diagnostics;
using Rekall.Age.Core.Commands;
using Rekall.Age.Workflows.Web;

namespace Rekall.Age.Workflows.Commands;

public sealed record PublishWebGameRequest(
    string ProjectRoot,
    string SceneName = "Main",
    string? OutputDirectory = null);

public sealed record PublishWebGameResult(
    bool Ready,
    string OutputDirectory,
    string ManifestPath,
    string StagingDirectory,
    RekallAgeWebGameManifest? Manifest,
    string RestoreOutput,
    string PublishOutput);

/// <summary>
/// Wraps the existing generic building blocks -- <see cref="RekallAgeWebModuleRegistryGenerator"/> (static module
/// registry) and <see cref="RekallAgeWebGameExporter"/> (declarative content closure) -- into one reusable command
/// that produces a real, trimmed, browser-servable publish directory. It runs the exact sequence proven working by
/// <c>WebGameExporterTests.TrimmedWebAssemblyPublishIncludesTheStaticallyRegisteredAuthoredModule</c>: discover the
/// authored module(s), generate the static registry + MSBuild inputs, stage the declarative content closure, then
/// restore and publish the generic <c>Rekall.Age.Player.Web</c> project (never a project-specific web player --
/// per AGENTS.md the engine stays generic, only the authored project's own content and module registry vary).
/// </summary>
public sealed class PublishWebGameCommand : IRekallAgeCommand<PublishWebGameRequest, PublishWebGameResult>
{
    public string Name => "rekall.game.publish_web";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Publishes an authored project as a real, trimmed, browser-servable WebAssembly build: discovers the authored static module(s), stages the declarative content closure, and runs a real dotnet publish of the generic Rekall.Age.Player.Web project against them. Returns OutputDirectory (serve this statically) and ManifestPath for rekall.game.audit_web.",
        typeof(PublishWebGameRequest).FullName!,
        typeof(PublishWebGameResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<PublishWebGameResult>> ExecuteAsync(
        PublishWebGameRequest request,
        RekallAgeCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var projectRoot = Path.GetFullPath(request.ProjectRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // Defaults to a sibling of the project root, never nested inside it: RekallAgeWebGameExporter.StageAsync
        // rejects a staging output that overlaps the authored project root, since the staged content closure is a
        // read-only export of it, not part of it.
        var outputDirectory = Path.GetFullPath(request.OutputDirectory ?? Path.Combine(
            Path.GetDirectoryName(projectRoot) ?? Directory.GetCurrentDirectory(),
            $"{Path.GetFileName(projectRoot)}.web-publish"));
        var workingRoot = Path.Combine(outputDirectory, ".rekall-web-publish");
        var generatedDirectory = Path.Combine(workingRoot, "generated");
        var stagingDirectory = Path.Combine(workingRoot, "staged");
        var publishDirectory = Path.Combine(outputDirectory, "publish");

        RekallAgeWebModuleRegistryPlan discovery;
        RekallAgeWebModuleRegistryGeneration generation;
        RekallAgeWebGameStageResult staged;
        try
        {
            var generator = new RekallAgeWebModuleRegistryGenerator();
            discovery = generator.Discover(projectRoot);
            if (Directory.Exists(workingRoot))
            {
                Directory.Delete(workingRoot, recursive: true);
            }
            generation = generator.Generate(projectRoot, generatedDirectory);
            staged = await new RekallAgeWebGameExporter().StageAsync(
                new RekallAgeWebGameStageRequest(projectRoot, request.SceneName, stagingDirectory, ModulePlan: discovery),
                context.CancellationToken);
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or ArgumentException or IOException)
        {
            return Failure(
                outputDirectory,
                stagingDirectory,
                new RekallAgeCommandError("REKALL_WEB_GAME_PUBLISH_STAGING_FAILED", error.Message, projectRoot),
                string.Empty,
                string.Empty);
        }

        var webProject = FindWebPlayerProjectPath();
        var restoreOutput = string.Empty;
        foreach (var moduleProject in generation.ModuleProjectPaths)
        {
            var moduleRestore = await RunDotNetAsync(context.CancellationToken, "restore", moduleProject);
            restoreOutput += Environment.NewLine + moduleRestore.Output;
            if (moduleRestore.ExitCode != 0)
            {
                return Failure(
                    outputDirectory,
                    stagingDirectory,
                    new RekallAgeCommandError("REKALL_WEB_GAME_PUBLISH_MODULE_RESTORE_FAILED", moduleRestore.Output, moduleProject),
                    restoreOutput,
                    string.Empty);
            }
        }

        if (Directory.Exists(publishDirectory))
        {
            Directory.Delete(publishDirectory, recursive: true);
        }

        // One combined publish (implicit restore, no separate --no-restore step) with --artifacts-path redirecting
        // Rekall.Age.Player.Web's own obj/bin to a location private to this request -- the same isolation
        // BuildPlayerCommand already uses for the shared, repo-fixed, generic player project (per AGENTS.md there
        // is no per-project web player). Without it, two concurrent publishes -- of two different authored
        // projects, or this command racing an unrelated build of the same project, as the conformance test suite
        // does -- collide writing the same obj/Release/net10.0/Rekall.Age.Player.Web.dll and fail with a
        // file-locked MSB/CSC error instead of a graceful command failure.
        var artifactsPath = Path.Combine(workingRoot, ".rekall-publish-artifacts");
        // Also redirects the NuGet lock file: restore, run without --locked-mode (a freshly generated module
        // project is, by definition, not yet in any committed lock file), otherwise silently rewrites the engine's
        // own checked-in src/Rekall.Age.Player.Web/packages.lock.json with this request's transient project
        // reference -- observed directly during this task's own verification, not a hypothetical.
        var lockFilePath = Path.Combine(workingRoot, "packages.lock.json");
        var publish = await RunDotNetAsync(
            context.CancellationToken,
            "publish", webProject,
            "--nologo",
            "-c", "Release",
            "-p:PublishTrimmed=true",
            "-p:ILLinkTreatWarningsAsErrors=false",
            "-p:SuppressTrimAnalysisWarnings=true",
            "--artifacts-path", artifactsPath,
            $"-p:NuGetLockFilePath={lockFilePath}",
            $"-p:RekallAgeWebPublishInputsPath={generation.MsBuildInputsPath}",
            $"-p:RekallAgeWebGameContentPath={staged.OutputDirectory}",
            "-o", publishDirectory,
            "/nr:false");

        var publishedManifestPath = Directory.Exists(publishDirectory)
            ? Directory.EnumerateFiles(publishDirectory, "game.manifest.json", SearchOption.AllDirectories).FirstOrDefault()
            : null;
        if (Directory.Exists(artifactsPath))
        {
            Directory.Delete(artifactsPath, recursive: true);
        }
        if (publish.ExitCode != 0 || publishedManifestPath is null)
        {
            return Failure(
                outputDirectory,
                stagingDirectory,
                new RekallAgeCommandError("REKALL_WEB_GAME_PUBLISH_FAILED", publish.Output, webProject),
                restoreOutput,
                publish.Output);
        }

        context.Transaction.RecordChangedResource(outputDirectory);
        var result = new PublishWebGameResult(
            true,
            publishDirectory,
            publishedManifestPath,
            staged.OutputDirectory,
            staged.Manifest,
            restoreOutput,
            publish.Output);
        return RekallAgeCommandResult<PublishWebGameResult>.Success(
            result,
            $"Published web game to '{publishDirectory}'.");
    }

    private static RekallAgeCommandResult<PublishWebGameResult> Failure(
        string outputDirectory,
        string stagingDirectory,
        RekallAgeCommandError error,
        string restoreOutput,
        string publishOutput)
    {
        var result = new PublishWebGameResult(
            false,
            outputDirectory,
            string.Empty,
            stagingDirectory,
            null,
            restoreOutput,
            publishOutput);
        return RekallAgeCommandResult<PublishWebGameResult>.Failure(
            result,
            "Web game publish failed.",
            [error]);
    }

    private static async ValueTask<(int ExitCode, string Output)> RunDotNetAsync(
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        // rekall.game.audit_web republishes through this same command against the same
        // workingRoot/lockFilePath a preceding rekall.game.publish_web call already used (see
        // AuditWebGameCommand). On Windows, the just-exited dotnet.exe process's handle on
        // packages.lock.json is not always released the instant WaitForExitAsync returns --
        // observed directly both interactively (had to `dotnet build-server shutdown` to unstick
        // it) and as a transient full-suite failure under parallel test load. Retry a handful of
        // times with a short backoff specifically for that one transient NuGet.targets "file...
        // is being used by another process" error; any other failure (a real compile/publish
        // error) surfaces immediately on the first attempt.
        const int maxAttempts = 4;
        for (var attempt = 1; ; attempt++)
        {
            var result = await RunDotNetOnceAsync(cancellationToken, arguments);
            var isTransientLockFailure = result.ExitCode != 0
                && result.Output.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
                && result.Output.Contains("packages.lock.json", StringComparison.OrdinalIgnoreCase);
            if (!isTransientLockFailure || attempt >= maxAttempts)
            {
                return result;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
        }
    }

    private static async ValueTask<(int ExitCode, string Output)> RunDotNetOnceAsync(
        CancellationToken cancellationToken,
        string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start dotnet.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, await outputTask + await errorTask);
    }

    internal static string FindWebPlayerProjectPath()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "src", "Rekall.Age.Player.Web", "Rekall.Age.Player.Web.csproj");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException("Could not locate src/Rekall.Age.Player.Web/Rekall.Age.Player.Web.csproj.");
    }
}
