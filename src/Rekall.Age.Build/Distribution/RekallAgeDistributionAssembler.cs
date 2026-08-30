using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Rekall.Age.Core.Product;

namespace Rekall.Age.Build.Distribution;

public sealed record AssembleDistributionRequest(
    string OutputRoot,
    string CliPublishRoot,
    string StudioPublishRoot,
    string HeadlessPlayerPublishRoot,
    string WindowsPlayerPublishRoot,
    string SdkSourceRoot,
    string ReadmePath,
    string EndUserLicensePath,
    string ProprietaryNoticePath,
    string ThirdPartyNoticesPath,
    string RuntimeIdentifier = "win-x64");

public sealed record RekallAgeDistributionFile(string Path, long Bytes, string Sha256);

public sealed record RekallAgeDistributionManifest(
    string Kind,
    string ProductVersion,
    string Channel,
    string RuntimeIdentifier,
    int ModuleSdkCompatibilityVersion,
    DateTimeOffset BuiltUtc,
    IReadOnlyList<RekallAgeCapabilityStatus> Capabilities,
    IReadOnlyList<RekallAgeDistributionFile> Files);

public sealed record RekallAgeDistributionAssemblyResult(
    string Root,
    string ManifestPath,
    string ArchivePath,
    RekallAgeDistributionManifest Manifest);

public sealed class RekallAgeDistributionAssemblyException : Exception
{
    public RekallAgeDistributionAssemblyException(string code, string message, string target)
        : base(message)
    {
        Code = code;
        Target = target;
    }

    public string Code { get; }

    public string Target { get; }
}

public sealed class RekallAgeDistributionAssembler
{
    private static readonly string[] ForbiddenExtensions = [".env", ".log", ".trx", ".pfx", ".snk"];

    public async ValueTask<RekallAgeDistributionAssemblyResult> AssembleAsync(
        AssembleDistributionRequest request,
        CancellationToken cancellationToken)
    {
        var outputRoot = Path.GetFullPath(request.OutputRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var inputs = InputRoots(request);
        ValidateOutput(outputRoot, inputs);
        ValidateSources(inputs);

        if (Directory.Exists(outputRoot))
        {
            Directory.Delete(outputRoot, recursive: true);
        }

        Directory.CreateDirectory(outputRoot);
        CopyDirectory(request.CliPublishRoot, Path.Combine(outputRoot, "tools", "cli"));
        CopyDirectory(request.StudioPublishRoot, Path.Combine(outputRoot, "tools", "studio"));
        CopyDirectory(request.HeadlessPlayerPublishRoot, Path.Combine(outputRoot, "players", "headless"));
        CopyDirectory(request.WindowsPlayerPublishRoot, Path.Combine(outputRoot, "players", "windows"));
        CopyDirectory(
            request.SdkSourceRoot,
            Path.Combine(
                outputRoot,
                "sdk",
                RekallAgeProductInfo.Current.ModuleSdkCompatibilityVersion.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)));
        CopyFile(request.ReadmePath, Path.Combine(outputRoot, "docs", "README.md"));
        CopyFile(request.EndUserLicensePath, Path.Combine(outputRoot, "END-USER-LICENSE-AGREEMENT.md"));
        CopyFile(request.ProprietaryNoticePath, Path.Combine(outputRoot, "PROPRIETARY-NOTICE.md"));
        CopyFile(request.ThirdPartyNoticesPath, Path.Combine(outputRoot, "THIRD-PARTY-NOTICES.txt"));

        var files = Directory.EnumerateFiles(outputRoot, "*", SearchOption.AllDirectories)
            .OrderBy(path => ToManifestPath(outputRoot, path), StringComparer.Ordinal)
            .Select(path => new RekallAgeDistributionFile(
                ToManifestPath(outputRoot, path),
                new FileInfo(path).Length,
                HashFile(path)))
            .ToArray();
        var manifest = new RekallAgeDistributionManifest(
            "rekall.age.distribution",
            RekallAgeProductInfo.Current.Version,
            RekallAgeProductInfo.Current.Channel,
            request.RuntimeIdentifier,
            RekallAgeProductInfo.Current.ModuleSdkCompatibilityVersion,
            DateTimeOffset.UtcNow,
            RekallAgeProductInfo.Capabilities,
            files);
        var manifestPath = Path.Combine(outputRoot, RekallAgeDistributionLayout.ManifestFileName);
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

        var archivePath = $"{outputRoot}.zip";
        CreateArchive(outputRoot, archivePath);
        return new RekallAgeDistributionAssemblyResult(outputRoot, manifestPath, archivePath, manifest);
    }

    private static IReadOnlyList<string> InputRoots(AssembleDistributionRequest request)
    {
        return
        [
            request.CliPublishRoot,
            request.StudioPublishRoot,
            request.HeadlessPlayerPublishRoot,
            request.WindowsPlayerPublishRoot,
            request.SdkSourceRoot,
            request.ReadmePath,
            request.EndUserLicensePath,
            request.ProprietaryNoticePath,
            request.ThirdPartyNoticesPath
        ];
    }

    private static void ValidateOutput(string outputRoot, IReadOnlyList<string> inputs)
    {
        var driveRoot = Path.GetPathRoot(outputRoot)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(driveRoot) || outputRoot.Equals(driveRoot, PathComparison))
        {
            throw new RekallAgeDistributionAssemblyException(
                "REKALL_DISTRIBUTION_OUTPUT_UNSAFE",
                "Distribution output cannot be a drive root.",
                outputRoot);
        }

        foreach (var input in inputs.Select(Path.GetFullPath))
        {
            if (input.Equals(outputRoot, PathComparison) ||
                input.StartsWith(outputRoot + Path.DirectorySeparatorChar, PathComparison))
            {
                throw new RekallAgeDistributionAssemblyException(
                    "REKALL_DISTRIBUTION_OUTPUT_UNSAFE",
                    "Distribution output cannot equal or contain an input.",
                    outputRoot);
            }
        }
    }

    private static void ValidateSources(IEnumerable<string> inputs)
    {
        foreach (var input in inputs)
        {
            var fullInput = Path.GetFullPath(input);
            var files = Directory.Exists(fullInput)
                ? Directory.EnumerateFiles(fullInput, "*", SearchOption.AllDirectories)
                : File.Exists(fullInput)
                    ? [fullInput]
                    : throw new RekallAgeDistributionAssemblyException(
                        "REKALL_DISTRIBUTION_INPUT_MISSING",
                        "A required distribution input does not exist.",
                        fullInput);
            foreach (var file in files)
            {
                if (IsForbidden(file))
                {
                    throw new RekallAgeDistributionAssemblyException(
                        "REKALL_DISTRIBUTION_FORBIDDEN_FILE",
                        "A forbidden file was found in distribution input.",
                        file);
                }
            }
        }
    }

    private static bool IsForbidden(string path)
    {
        var fileName = Path.GetFileName(path);
        var segments = Path.GetFullPath(path).Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        return ForbiddenExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) ||
            fileName.Equals(".env", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("appsettings.", StringComparison.OrdinalIgnoreCase) &&
                fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
            segments.Contains("TestResults", StringComparer.OrdinalIgnoreCase);
    }

    private static void CopyDirectory(string sourceRoot, string destinationRoot)
    {
        var fullSource = Path.GetFullPath(sourceRoot);
        foreach (var file in Directory.EnumerateFiles(fullSource, "*", SearchOption.AllDirectories)
                     .OrderBy(path => Path.GetRelativePath(fullSource, path), StringComparer.Ordinal))
        {
            CopyFile(file, Path.Combine(destinationRoot, Path.GetRelativePath(fullSource, file)));
        }
    }

    private static void CopyFile(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
    }

    private static string ToManifestPath(string root, string path)
    {
        return Path.GetRelativePath(root, path).Replace('\\', '/');
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void CreateArchive(string root, string archivePath)
    {
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .OrderBy(path => ToManifestPath(root, path), StringComparer.Ordinal))
        {
            var entry = archive.CreateEntry(ToManifestPath(root, file), CompressionLevel.Optimal);
            entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var input = File.OpenRead(file);
            using var output = entry.Open();
            input.CopyTo(output);
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
