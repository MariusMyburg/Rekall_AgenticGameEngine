using Rekall.Age.Core.Commands;
using System.IO.Compression;
using System.Security.Cryptography;
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
    internal const int MaximumArchiveEntries = 100_000;
    internal const long MaximumEntrySizeBytes = 8L * 1024 * 1024 * 1024;
    internal const long MaximumPackageSizeBytes = 32L * 1024 * 1024 * 1024;

    public string Name => "rekall.workflow.inspect_playable_package";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Inspects a packaged playable game manifest from PackagePlayableGameResult.OutputDirectory, ManifestPath, or ArchivePath. Never pass its LaunchPath executable.",
        typeof(InspectPlayablePackageRequest).FullName!,
        typeof(InspectPlayablePackageResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<InspectPlayablePackageResult>> ExecuteAsync(
        InspectPlayablePackageRequest request,
        RekallAgeCommandContext context)
    {
        var fullPath = Path.GetFullPath(request.PackagePath);
        if (File.Exists(fullPath)
            && !Path.GetExtension(fullPath).Equals(".zip", StringComparison.OrdinalIgnoreCase)
            && !Path.GetFileName(fullPath).Equals("rekall.package.json", StringComparison.OrdinalIgnoreCase))
        {
            return InvalidPackagePath(fullPath);
        }

        (string manifestPath, RekallAgePlayablePackageManifest manifest, IReadOnlyList<RekallAgePlayablePackageFile> files) package;
        try
        {
            package = await ReadManifestAsync(fullPath, context.CancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return InvalidPackagePath(fullPath, exception.Message);
        }

        var (manifestPath, manifest, files) = package;
        var keyArtifacts = files
            .Where(file => file.IsKeyArtifact)
            .Select(file => file.Path)
            .ToArray();
        var ready = manifest.Kind == "rekall.age.playable.package" &&
            manifest.Checks.All(check => check.Passed) &&
            manifest.DrawAssertions.All(assertion => assertion.Passed);
        var integrityErrors = await VerifyIntegrityAsync(request.PackagePath, manifest, context.CancellationToken);
        var result = new InspectPlayablePackageResult(
            ready && integrityErrors.Count == 0,
            manifestPath,
            manifest,
            files.Count,
            files,
            keyArtifacts);
        if (integrityErrors.Count > 0)
        {
            return RekallAgeCommandResult<InspectPlayablePackageResult>.Failure(
                result,
                "Playable package integrity verification failed.",
                integrityErrors);
        }

        return RekallAgeCommandResult<InspectPlayablePackageResult>.Success(
            result,
            $"Inspected playable package '{manifestPath}'.");
    }

    private static RekallAgeCommandResult<InspectPlayablePackageResult> InvalidPackagePath(
        string packagePath,
        string? detail = null)
    {
        var message = "PackagePath must be a package OutputDirectory, rekall.package.json ManifestPath, or .zip ArchivePath returned by rekall.workflow.package_playable_game; do not pass its LaunchPath executable."
            + (string.IsNullOrWhiteSpace(detail) ? string.Empty : $" {detail}");
        var manifest = new RekallAgePlayablePackageManifest("", "", "", "", [], [], []);
        var value = new InspectPlayablePackageResult(false, string.Empty, manifest, 0, [], []);
        return RekallAgeCommandResult<InspectPlayablePackageResult>.Failure(
            value,
            message,
            [new RekallAgeCommandError("REKALL_PACKAGE_PATH_KIND_INVALID", message, packagePath)]);
    }

    private static async ValueTask<IReadOnlyList<RekallAgeCommandError>> VerifyIntegrityAsync(
        string packagePath,
        RekallAgePlayablePackageManifest manifest,
        CancellationToken cancellationToken)
    {
        if (manifest.SchemaVersion < 2)
        {
            return [];
        }

        var errors = new List<RekallAgeCommandError>();
        if (manifest.Files is null)
        {
            return
            [
                new RekallAgeCommandError(
                    "REKALL_PACKAGE_INVENTORY_MISSING",
                    "Schema v2 package manifest has no file integrity inventory.",
                    "rekall.package.json")
            ];
        }

        ValidateManifestPath(manifest.GameRoot, "gameRoot", errors);
        ValidateManifestPath(manifest.LaunchPath, "launchPath", errors);
        if (manifest.Arguments.Count == 0 ||
            !manifest.Arguments[0].Equals(manifest.GameRoot, StringComparison.Ordinal))
        {
            errors.Add(new RekallAgeCommandError(
                "REKALL_PACKAGE_ARGUMENTS_INVALID",
                "The first package launch argument must be the relative game root.",
                "arguments[0]"));
        }

        var expected = new Dictionary<string, RekallAgePlayablePackageFileIntegrity>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            if (!TryValidateRelativePath(file.Path, out var normalized))
            {
                errors.Add(new RekallAgeCommandError(
                    "REKALL_PACKAGE_PATH_UNSAFE",
                    "Package inventory path must be normalized, relative, and traversal-free.",
                    file.Path));
                continue;
            }

            if (!expected.TryAdd(normalized, file))
            {
                errors.Add(new RekallAgeCommandError(
                    "REKALL_PACKAGE_PATH_COLLISION",
                    "Package inventory contains a duplicate or case-colliding path.",
                    file.Path));
            }
        }

        var actual = await ReadActualFilesAsync(packagePath, cancellationToken, errors);
        foreach (var (path, expectedFile) in expected)
        {
            if (!actual.TryGetValue(path, out var actualFile))
            {
                errors.Add(new RekallAgeCommandError(
                    "REKALL_PACKAGE_FILE_MISSING",
                    "A file declared by the package manifest is missing.",
                    path));
                continue;
            }

            if (expectedFile.SizeBytes != actualFile.SizeBytes ||
                !expectedFile.Sha256.Equals(actualFile.Sha256, StringComparison.Ordinal))
            {
                errors.Add(new RekallAgeCommandError(
                    "REKALL_PACKAGE_HASH_MISMATCH",
                    "A packaged file does not match its declared size or SHA-256 digest.",
                    path));
            }
        }

        foreach (var path in actual.Keys.Where(path => !expected.ContainsKey(path)))
        {
            errors.Add(new RekallAgeCommandError(
                "REKALL_PACKAGE_UNEXPECTED_FILE",
                "The package contains a file that is not declared by its integrity inventory.",
                path));
        }

        return errors;
    }

    private static async ValueTask<Dictionary<string, ActualPackageFile>> ReadActualFilesAsync(
        string packagePath,
        CancellationToken cancellationToken,
        List<RekallAgeCommandError> errors)
    {
        var fullPath = Path.GetFullPath(packagePath);
        var actual = new Dictionary<string, ActualPackageFile>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(fullPath) && Path.GetExtension(fullPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var archive = ZipFile.OpenRead(fullPath);
            var totalLength = 0L;
            var archiveLimitExceeded = archive.Entries.Count > MaximumArchiveEntries;
            foreach (var entry in archive.Entries)
            {
                if (entry.Length > MaximumEntrySizeBytes ||
                    totalLength > MaximumPackageSizeBytes - entry.Length)
                {
                    archiveLimitExceeded = true;
                    break;
                }

                totalLength += entry.Length;
            }

            if (archiveLimitExceeded)
            {
                errors.Add(new RekallAgeCommandError(
                    "REKALL_PACKAGE_ARCHIVE_LIMIT_EXCEEDED",
                    "Package archive exceeds the supported entry-count or uncompressed-size limit.",
                    fullPath));
                return actual;
            }

            foreach (var entry in archive.Entries.Where(entry => !entry.FullName.EndsWith("/", StringComparison.Ordinal)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Length > MaximumEntrySizeBytes)
                {
                    errors.Add(new RekallAgeCommandError(
                        "REKALL_PACKAGE_ARCHIVE_LIMIT_EXCEEDED",
                        "A package archive entry exceeds the supported uncompressed-size limit.",
                        entry.FullName));
                    continue;
                }

                if (!TryValidateRelativePath(entry.FullName, out var normalized))
                {
                    errors.Add(new RekallAgeCommandError(
                        "REKALL_PACKAGE_PATH_UNSAFE",
                        "Package archive path must be normalized, relative, and traversal-free.",
                        entry.FullName));
                    continue;
                }

                if (normalized.Equals("rekall.package.json", StringComparison.Ordinal))
                {
                    continue;
                }

                await using var stream = entry.Open();
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
                if (!actual.TryAdd(normalized, new ActualPackageFile(entry.Length, hash)))
                {
                    errors.Add(new RekallAgeCommandError(
                        "REKALL_PACKAGE_PATH_COLLISION",
                        "Package archive contains a duplicate or case-colliding path.",
                        entry.FullName));
                }
            }

            return actual;
        }

        var root = Directory.Exists(fullPath)
            ? fullPath
            : Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException($"Package path '{fullPath}' has no parent directory.");
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = NormalizePath(Path.GetRelativePath(root, file));
            if (relative.Equals("rekall.package.json", StringComparison.Ordinal))
            {
                continue;
            }

            await using var stream = File.OpenRead(file);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            if (!actual.TryAdd(relative, new ActualPackageFile(stream.Length, hash)))
            {
                errors.Add(new RekallAgeCommandError(
                    "REKALL_PACKAGE_PATH_COLLISION",
                    "Package directory contains a duplicate or case-colliding path.",
                    relative));
            }
        }

        return actual;
    }

    private static void ValidateManifestPath(
        string path,
        string target,
        List<RekallAgeCommandError> errors)
    {
        if (!TryValidateRelativePath(path, out _))
        {
            errors.Add(new RekallAgeCommandError(
                "REKALL_PACKAGE_PATH_UNSAFE",
                "Package manifest path must be normalized, relative, and traversal-free.",
                target));
        }
    }

    internal static bool TryValidateRelativePath(string path, out string normalized)
    {
        normalized = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(path) ||
            path.Contains('\\') ||
            Path.IsPathRooted(path) ||
            !normalized.Equals(path, StringComparison.Ordinal))
        {
            return false;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 && segments.All(segment =>
            segment is not "." and not ".." &&
            !segment.Contains(':'));
    }

    private sealed record ActualPackageFile(long SizeBytes, string Sha256);

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
