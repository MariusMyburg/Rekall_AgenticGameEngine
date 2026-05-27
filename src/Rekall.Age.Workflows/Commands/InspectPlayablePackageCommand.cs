using Rekall.Age.Core.Commands;
using System.IO.Compression;
using System.Text.Json;

namespace Rekall.Age.Workflows.Commands;

public sealed record InspectPlayablePackageRequest(string PackagePath);

public sealed record RekallAgePlayablePackageFile(
    string Path,
    long SizeBytes,
    bool IsKeyArtifact);

public sealed record InspectPlayablePackageResult(
    bool Ready,
    string ManifestPath,
    RekallAgePlayablePackageManifest Manifest,
    int FileCount,
    IReadOnlyList<RekallAgePlayablePackageFile> Files,
    IReadOnlyList<string> KeyArtifacts);

public sealed class InspectPlayablePackageCommand
    : IRekallAgeCommand<InspectPlayablePackageRequest, InspectPlayablePackageResult>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string Name => "rekall.workflow.inspect_playable_package";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Inspects a packaged playable game manifest from a package directory, manifest file, or zip archive.",
        typeof(InspectPlayablePackageRequest).FullName!,
        typeof(InspectPlayablePackageResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<InspectPlayablePackageResult>> ExecuteAsync(
        InspectPlayablePackageRequest request,
        RekallAgeCommandContext context)
    {
        var (manifestPath, manifest, files) = await ReadManifestAsync(request.PackagePath, context.CancellationToken);
        var keyArtifacts = files
            .Where(file => file.IsKeyArtifact)
            .Select(file => file.Path)
            .ToArray();
        var ready = manifest.Kind == "rekall.age.playable.package" &&
            manifest.Checks.All(check => check.Passed) &&
            manifest.DrawAssertions.All(assertion => assertion.Passed);
        return RekallAgeCommandResult<InspectPlayablePackageResult>.Success(
            new InspectPlayablePackageResult(ready, manifestPath, manifest, files.Count, files, keyArtifacts),
            $"Inspected playable package '{manifestPath}'.");
    }

    private static async ValueTask<(
        string ManifestPath,
        RekallAgePlayablePackageManifest Manifest,
        IReadOnlyList<RekallAgePlayablePackageFile> Files)> ReadManifestAsync(
        string packagePath,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(packagePath);
        if (Directory.Exists(fullPath))
        {
            var (manifestPath, manifest) = await ReadManifestFileAsync(Path.Combine(fullPath, "rekall.package.json"), cancellationToken);
            return (manifestPath, manifest, EnumerateDirectoryFiles(fullPath));
        }

        if (File.Exists(fullPath) && Path.GetExtension(fullPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var archive = ZipFile.OpenRead(fullPath);
            var entry = archive.GetEntry("rekall.package.json")
                ?? throw new InvalidOperationException($"Package archive '{fullPath}' does not contain rekall.package.json.");
            await using var stream = entry.Open();
            var manifest = await JsonSerializer.DeserializeAsync<RekallAgePlayablePackageManifest>(
                stream,
                JsonOptions,
                cancellationToken);
            return (
                $"{fullPath}!/rekall.package.json",
                manifest ?? throw new InvalidOperationException($"Package manifest in '{fullPath}' could not be read."),
                EnumerateArchiveFiles(archive));
        }

        var fileManifest = await ReadManifestFileAsync(fullPath, cancellationToken);
        return (fileManifest.ManifestPath, fileManifest.Manifest, EnumerateDirectoryFiles(Path.GetDirectoryName(fullPath)!));
    }

    private static async ValueTask<(string ManifestPath, RekallAgePlayablePackageManifest Manifest)> ReadManifestFileAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<RekallAgePlayablePackageManifest>(
            stream,
            JsonOptions,
            cancellationToken);
        return (manifestPath, manifest ?? throw new InvalidOperationException($"Package manifest '{manifestPath}' could not be read."));
    }

    private static IReadOnlyList<RekallAgePlayablePackageFile> EnumerateDirectoryFiles(string packageRoot)
    {
        return Directory.EnumerateFiles(packageRoot, "*", SearchOption.AllDirectories)
            .Select(file =>
            {
                var relativePath = NormalizePath(Path.GetRelativePath(packageRoot, file));
                return new RekallAgePlayablePackageFile(
                    relativePath,
                    new FileInfo(file).Length,
                    IsKeyArtifact(relativePath));
            })
            .OrderBy(file => file.Path, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<RekallAgePlayablePackageFile> EnumerateArchiveFiles(ZipArchive archive)
    {
        return archive.Entries
            .Where(entry => !entry.FullName.EndsWith("/", StringComparison.Ordinal))
            .Select(entry =>
            {
                var path = NormalizePath(entry.FullName);
                return new RekallAgePlayablePackageFile(path, entry.Length, IsKeyArtifact(path));
            })
            .OrderBy(file => file.Path, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsKeyArtifact(string path)
    {
        return path.Equals("rekall.package.json", StringComparison.Ordinal) ||
            path.Equals("Game/rekall.project.json", StringComparison.Ordinal) ||
            path.StartsWith("Game/Scenes/", StringComparison.Ordinal) && path.EndsWith(".age.scene.json", StringComparison.Ordinal) ||
            path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }
}
