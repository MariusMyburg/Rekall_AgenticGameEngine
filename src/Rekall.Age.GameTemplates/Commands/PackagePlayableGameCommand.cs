using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Playback;
using Rekall.Age.Playback.Commands;
using Rekall.Age.Project;
using System.IO.Compression;
using System.Text.Json;

namespace Rekall.Age.GameTemplates.Commands;

public sealed record PackagePlayableGameRequest(
    string ProjectRoot,
    string SceneName = "Main",
    string? OutputDirectory = null,
    int Frames = 2,
    IReadOnlyList<RekallAgePlaybackInput>? Inputs = null,
    IReadOnlyList<RekallAgeFrameAssertion>? Assertions = null,
    bool Graphics = false);

public sealed record PackagePlayableGameResult(
    bool Ready,
    string OutputDirectory,
    string LaunchPath,
    string ManifestPath,
    string ArchivePath,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<RekallAgePlayableGameCheck> Checks,
    string BuildOutput);

public sealed class PackagePlayableGameCommand
    : IRekallAgeCommand<PackagePlayableGameRequest, PackagePlayableGameResult>
{
    private readonly VerifyPlayableGameCommand _verifyPlayableGame = new();
    private readonly BuildPlayerCommand _buildPlayer = new();
    private readonly RekallAgeProjectStore _projectStore = new();
    private readonly RekallAgeGameTemplateCatalog _templateCatalog = RekallAgeGameTemplateCatalog.CreateDefault();

    public string Name => "rekall.workflow.package_playable_game";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Verifies a playable game and publishes the Rekall AGE player launch artifact.",
        typeof(PackagePlayableGameRequest).FullName!,
        typeof(PackagePlayableGameResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<PackagePlayableGameResult>> ExecuteAsync(
        PackagePlayableGameRequest request,
        RekallAgeCommandContext context)
    {
        var verification = await _verifyPlayableGame.ExecuteAsync(
            new VerifyPlayableGameRequest(
                request.ProjectRoot,
                request.SceneName,
                request.Frames,
                request.Inputs,
                request.Assertions),
            context);
        if (!verification.Ok)
        {
            return RekallAgeCommandResult<PackagePlayableGameResult>.Failure(
                new PackagePlayableGameResult(
                    Ready: false,
                    OutputDirectory: request.OutputDirectory ?? string.Empty,
                    LaunchPath: string.Empty,
                    ManifestPath: string.Empty,
                    ArchivePath: string.Empty,
                    Arguments: [],
                    Checks: verification.Value.Checks,
                    BuildOutput: string.Empty),
                verification.Summary,
                verification.Errors);
        }

        var outputDirectory = request.OutputDirectory
            ?? Path.Combine(request.ProjectRoot, "Builds", "RekallAgePlayer");
        var player = await _buildPlayer.ExecuteAsync(
            new BuildPlayerRequest(request.ProjectRoot, request.SceneName, outputDirectory, request.Graphics),
            context);
        var bundledGameRoot = Path.Combine(outputDirectory, "Game");
        var manifestPath = Path.Combine(outputDirectory, "rekall.package.json");
        var archivePath = $"{Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)}.zip";
        var arguments = player.Ok
            ? CreateLaunchArguments(bundledGameRoot, request.SceneName, request.Graphics)
            : player.Value.Arguments;
        var result = new PackagePlayableGameResult(
            Ready: player.Ok,
            OutputDirectory: player.Value.OutputDirectory,
            LaunchPath: player.Value.LaunchPath,
            ManifestPath: manifestPath,
            ArchivePath: archivePath,
            Arguments: arguments,
            Checks: verification.Value.Checks,
            BuildOutput: player.Value.Output);
        if (!player.Ok)
        {
            return RekallAgeCommandResult<PackagePlayableGameResult>.Failure(
                result,
                player.Summary,
                player.Errors);
        }

        CopyProjectToPackage(request.ProjectRoot, bundledGameRoot, outputDirectory);
        var sourceTemplateId = await ReadSourceTemplateIdAsync(request.ProjectRoot, context.CancellationToken);
        var drawCommands = sourceTemplateId is null
            ? []
            : _templateCatalog.GetRequired(sourceTemplateId).DrawCommands;
        await WriteManifestAsync(
            manifestPath,
            request.SceneName,
            bundledGameRoot,
            player.Value.LaunchPath,
            arguments,
            verification.Value.Checks,
            sourceTemplateId,
            drawCommands,
            verification.Value.DrawAssertions,
            context.CancellationToken);
        CreatePackageArchive(outputDirectory, archivePath);
        context.Transaction.RecordChangedResource(bundledGameRoot);
        context.Transaction.RecordChangedResource(manifestPath);
        context.Transaction.RecordChangedResource(archivePath);

        return RekallAgeCommandResult<PackagePlayableGameResult>.Success(
            result,
            $"Packaged playable game '{request.SceneName}' at '{player.Value.OutputDirectory}'.");
    }

    private static IReadOnlyList<string> CreateLaunchArguments(string bundledGameRoot, string sceneName, bool graphics)
    {
        return graphics
            ? [bundledGameRoot, sceneName, "--graphics"]
            : [bundledGameRoot, sceneName];
    }

    private static void CopyProjectToPackage(string projectRoot, string bundledGameRoot, string outputDirectory)
    {
        var sourceRoot = Path.GetFullPath(projectRoot);
        var destinationRoot = Path.GetFullPath(bundledGameRoot);
        var packageRoot = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(destinationRoot))
        {
            Directory.Delete(destinationRoot, recursive: true);
        }

        Directory.CreateDirectory(destinationRoot);
        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var fullDirectory = Path.GetFullPath(directory);
            if (ShouldSkipPath(sourceRoot, fullDirectory, packageRoot))
            {
                continue;
            }

            Directory.CreateDirectory(ToDestinationPath(sourceRoot, destinationRoot, fullDirectory));
        }

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var fullFile = Path.GetFullPath(file);
            if (ShouldSkipPath(sourceRoot, fullFile, packageRoot))
            {
                continue;
            }

            var destination = ToDestinationPath(sourceRoot, destinationRoot, fullFile);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(fullFile, destination, overwrite: true);
        }
    }

    private static string ToDestinationPath(string sourceRoot, string destinationRoot, string path)
    {
        return Path.Combine(destinationRoot, Path.GetRelativePath(sourceRoot, path));
    }

    private static bool ShouldSkipPath(string sourceRoot, string path, string packageRoot)
    {
        if (IsSameOrInside(path, packageRoot))
        {
            return true;
        }

        var relative = Path.GetRelativePath(sourceRoot, path);
        var segments = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment =>
            segment.Equals("Builds", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSameOrInside(string path, string directory)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return normalizedPath.Equals(normalizedDirectory, comparison) ||
            normalizedPath.StartsWith(normalizedDirectory + Path.DirectorySeparatorChar, comparison);
    }

    private static void CreatePackageArchive(string outputDirectory, string archivePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        ZipFile.CreateFromDirectory(outputDirectory, archivePath, CompressionLevel.Optimal, includeBaseDirectory: false);
    }

    private static async Task WriteManifestAsync(
        string manifestPath,
        string sceneName,
        string bundledGameRoot,
        string launchPath,
        IReadOnlyList<string> arguments,
        IReadOnlyList<RekallAgePlayableGameCheck> checks,
        string? sourceTemplateId,
        IReadOnlyList<RekallAgeTemplateDrawCommand> drawCommands,
        IReadOnlyList<RekallAgeDrawCommandAssertionResult> drawAssertions,
        CancellationToken cancellationToken)
    {
        var manifest = new RekallAgePlayablePackageManifest(
            "rekall.age.playable.package",
            sceneName,
            bundledGameRoot,
            launchPath,
            arguments,
            checks,
            sourceTemplateId,
            drawCommands,
            drawAssertions);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(
                manifest,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }),
            cancellationToken);
    }

    private async ValueTask<string?> ReadSourceTemplateIdAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var manifest = await _projectStore.LoadAsync(projectRoot, cancellationToken);
        return manifest.SourceTemplateId;
    }
}

public sealed record RekallAgePlayablePackageManifest(
    string Kind,
    string SceneName,
    string GameRoot,
    string LaunchPath,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<RekallAgePlayableGameCheck> Checks,
    string? SourceTemplateId,
    IReadOnlyList<RekallAgeTemplateDrawCommand> DrawCommands,
    IReadOnlyList<RekallAgeDrawCommandAssertionResult> DrawAssertions);
