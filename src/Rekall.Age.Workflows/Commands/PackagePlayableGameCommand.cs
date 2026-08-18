using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Playback;
using Rekall.Age.Playback.Commands;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Core.Product;
using Rekall.Age.Modules.Security;

namespace Rekall.Age.Workflows.Commands;

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
    string BuildOutput)
{
    public string? ProofLaunchPath { get; init; }
}

public sealed class PackagePlayableGameCommand
    : IRekallAgeCommand<PackagePlayableGameRequest, PackagePlayableGameResult>
{
    private readonly VerifyPlayableGameCommand _verifyPlayableGame = new();
    private readonly BuildPlayerCommand _buildPlayer = new();
    private readonly RekallAgeProjectModuleTrustInspector _moduleTrust;

    public PackagePlayableGameCommand()
        : this(new RekallAgeProjectModuleTrustInspector())
    {
    }

    internal PackagePlayableGameCommand(RekallAgeProjectModuleTrustInspector moduleTrust)
    {
        _moduleTrust = moduleTrust;
    }

    public string Name => "rekall.workflow.package_playable_game";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Creates and verifies a portable playable package, returning OutputDirectory and ArchivePath for package inspect/audit/relocation plus a separate LaunchPath executable. Never pass LaunchPath as PackagePath.",
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

        var outputDirectory = Path.GetFullPath(
            request.OutputDirectory
                ?? Path.Combine(request.ProjectRoot, "Builds", "RekallAgePlayer"));
        var outputPreparationError = PrepareOutputDirectory(request.ProjectRoot, outputDirectory);
        if (outputPreparationError is not null)
        {
            return RekallAgeCommandResult<PackagePlayableGameResult>.Failure(
                new PackagePlayableGameResult(
                    Ready: false,
                    OutputDirectory: outputDirectory,
                    LaunchPath: string.Empty,
                    ManifestPath: string.Empty,
                    ArchivePath: string.Empty,
                    Arguments: [],
                    Checks: verification.Value.Checks,
                    BuildOutput: string.Empty),
                "Playable package output directory is unsafe.",
                [outputPreparationError]);
        }

        var player = await _buildPlayer.ExecuteAsync(
            new BuildPlayerRequest(request.ProjectRoot, request.SceneName, outputDirectory, request.Graphics),
            context);
        RekallAgeCommandResult<BuildPlayerResult>? proofPlayer = null;
        if (player.Ok && request.Graphics)
        {
            proofPlayer = await _buildPlayer.ExecuteAsync(
                new BuildPlayerRequest(
                    request.ProjectRoot,
                    request.SceneName,
                    Path.Combine(outputDirectory, "ProofPlayer"),
                    Graphics: false),
                context);
        }
        var bundledGameRoot = Path.Combine(outputDirectory, "Game");
        var manifestPath = Path.Combine(outputDirectory, "rekall.package.json");
        var archivePath = $"{Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)}.zip";
        var arguments = player.Ok
            ? CreateLaunchArguments(bundledGameRoot, request.SceneName, request.Graphics)
            : player.Value.Arguments;
        var result = new PackagePlayableGameResult(
            Ready: player.Ok && (proofPlayer?.Ok ?? true),
            OutputDirectory: player.Value.OutputDirectory,
            LaunchPath: player.Value.LaunchPath,
            ManifestPath: manifestPath,
            ArchivePath: archivePath,
            Arguments: arguments,
            Checks: verification.Value.Checks,
            BuildOutput: string.Join(Environment.NewLine, new[] { player.Value.Output, proofPlayer?.Value.Output }
                .Where(output => !string.IsNullOrWhiteSpace(output))));
        if (!player.Ok || proofPlayer is { Ok: false })
        {
            return RekallAgeCommandResult<PackagePlayableGameResult>.Failure(
                result,
                !player.Ok ? player.Summary : proofPlayer!.Summary,
                !player.Ok ? player.Errors : proofPlayer!.Errors);
        }

        var packageTrust = _moduleTrust.Inspect(request.ProjectRoot);
        if (!packageTrust.Ready)
        {
            return RekallAgeCommandResult<PackagePlayableGameResult>.Failure(
                result,
                "Module trust preflight failed before package copy.",
                packageTrust.Issues.Select(issue => new RekallAgeCommandError(
                    issue.Code,
                    issue.Message,
                    issue.Target)).ToArray());
        }

        CopyProjectToPackage(request.ProjectRoot, bundledGameRoot, outputDirectory);
        await SanitizePackagedAssetCatalogAsync(
            request.ProjectRoot,
            bundledGameRoot,
            context.CancellationToken);
        var manifestGameRoot = "Game";
        var manifestLaunchPath = NormalizePath(Path.GetRelativePath(outputDirectory, player.Value.LaunchPath));
        var manifestProofLaunchPath = proofPlayer is null
            ? null
            : NormalizePath(Path.GetRelativePath(outputDirectory, proofPlayer.Value.LaunchPath));
        var manifestArguments = CreateLaunchArguments(manifestGameRoot, request.SceneName, request.Graphics);
        var files = BuildFileInventory(outputDirectory, manifestPath);
        await WriteManifestAsync(
            manifestPath,
            request.SceneName,
            manifestGameRoot,
            manifestLaunchPath,
            manifestProofLaunchPath,
            manifestArguments,
            files,
            verification.Value.Checks,
            verification.Value.DrawAssertions,
            context.CancellationToken);
        CreatePackageArchive(outputDirectory, archivePath);
        context.Transaction.RecordChangedResource(bundledGameRoot);
        context.Transaction.RecordChangedResource(manifestPath);
        context.Transaction.RecordChangedResource(archivePath);

        return RekallAgeCommandResult<PackagePlayableGameResult>.Success(
            result with { ProofLaunchPath = manifestProofLaunchPath },
            $"Packaged playable game '{request.SceneName}' at '{player.Value.OutputDirectory}'.");
    }

    private static IReadOnlyList<string> CreateLaunchArguments(string bundledGameRoot, string sceneName, bool graphics)
    {
        return graphics
            ? [bundledGameRoot, sceneName, "--graphics", "--backend", "vulkan", "--playable"]
            : [bundledGameRoot, sceneName];
    }

    private static RekallAgeCommandError? PrepareOutputDirectory(string projectRoot, string outputDirectory)
    {
        var sourceRoot = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var packageRoot = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var projectInsidePackageRoot = sourceRoot.StartsWith(packageRoot + Path.DirectorySeparatorChar, comparison);
        var driveRoot = Path.GetPathRoot(packageRoot)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (packageRoot.Equals(sourceRoot, comparison) ||
            projectInsidePackageRoot ||
            string.IsNullOrWhiteSpace(driveRoot) ||
            packageRoot.Equals(driveRoot, comparison))
        {
            return new RekallAgeCommandError(
                "REKALL_PLAYABLE_PACKAGE_OUTPUT_UNSAFE",
                "Package output directory must not be the project root, a parent of the project root, or a drive root.",
                outputDirectory);
        }

        if (Directory.Exists(packageRoot))
        {
            Directory.Delete(packageRoot, recursive: true);
        }

        Directory.CreateDirectory(packageRoot);
        return null;
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
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var fullFile = Path.GetFullPath(file);
            if (ShouldSkipFile(sourceRoot, fullFile, packageRoot))
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

    private static bool ShouldSkipFile(string sourceRoot, string path, string packageRoot)
    {
        if (IsSameOrInside(path, packageRoot))
        {
            return true;
        }

        var relative = Path.GetRelativePath(sourceRoot, path);
        var segments = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment =>
            segment.Equals("Builds", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals(".rekall", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("Transactions", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("TestResults", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("Artifacts", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var fileName = Path.GetFileName(path);
        var extension = Path.GetExtension(path);
        if (fileName.Equals(".env", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".env", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("appsettings.", StringComparison.OrdinalIgnoreCase) &&
            fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".user", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".pdb", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".log", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".trx", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".pfx", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".snk", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("packages.lock.json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var modulesIndex = Array.FindIndex(segments, segment =>
            segment.Equals("Modules", StringComparison.OrdinalIgnoreCase));
        return modulesIndex >= 0 && !ContainsSequence(
            segments[(modulesIndex + 1)..],
            ["bin", "rekall", "net10.0"]);
    }

    private static async ValueTask SanitizePackagedAssetCatalogAsync(
        string projectRoot,
        string bundledGameRoot,
        CancellationToken cancellationToken)
    {
        var catalogPath = Path.Combine(bundledGameRoot, "Assets", "assets.age.catalog.json");
        if (!File.Exists(catalogPath))
        {
            return;
        }

        var root = JsonNode.Parse(await File.ReadAllTextAsync(catalogPath, cancellationToken)) as JsonObject;
        if (root?["assets"] is not JsonArray assets)
        {
            return;
        }

        var sourceRoot = Path.GetFullPath(projectRoot);
        foreach (var asset in assets.OfType<JsonObject>())
        {
            asset["sourcePath"] = string.Empty;
            var importedPath = asset["importedPath"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(importedPath))
            {
                continue;
            }

            var fullImportedPath = Path.GetFullPath(importedPath);
            if (!IsSameOrInside(fullImportedPath, sourceRoot))
            {
                throw new InvalidDataException(
                    $"Asset catalog imported path '{importedPath}' is outside the project root.");
            }

            asset["importedPath"] = NormalizePath(Path.GetRelativePath(sourceRoot, fullImportedPath));
        }

        await File.WriteAllTextAsync(
            catalogPath,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine,
            cancellationToken);
    }

    private static bool ContainsSequence(IReadOnlyList<string> values, IReadOnlyList<string> sequence)
    {
        for (var start = 0; start <= values.Count - sequence.Count; start++)
        {
            if (Enumerable.Range(0, sequence.Count).All(offset =>
                    values[start + offset].Equals(sequence[offset], StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
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
        string? proofLaunchPath,
        IReadOnlyList<string> arguments,
        IReadOnlyList<RekallAgePlayablePackageFileIntegrity> files,
        IReadOnlyList<RekallAgePlayableGameCheck> checks,
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
            drawAssertions,
            SchemaVersion: 2,
            ProductVersion: RekallAgeProductInfo.Current.Version,
            Files: files)
        {
            ProofLaunchPath = proofLaunchPath
        };
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

    private static IReadOnlyList<RekallAgePlayablePackageFileIntegrity> BuildFileInventory(
        string outputDirectory,
        string manifestPath)
    {
        return Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories)
            .Where(path => !Path.GetFullPath(path).Equals(Path.GetFullPath(manifestPath), PathComparison))
            .Select(path => new RekallAgePlayablePackageFileIntegrity(
                NormalizePath(Path.GetRelativePath(outputDirectory, path)),
                new FileInfo(path).Length,
                Convert.ToHexString(HashFile(path)).ToLowerInvariant()))
            .OrderBy(file => file.Path, StringComparer.Ordinal)
            .ToArray();
    }

    private static byte[] HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return SHA256.HashData(stream);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

}

public sealed record RekallAgePlayablePackageManifest(
    string Kind,
    string SceneName,
    string GameRoot,
    string LaunchPath,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<RekallAgePlayableGameCheck> Checks,
    IReadOnlyList<RekallAgeDrawCommandAssertionResult> DrawAssertions,
    int SchemaVersion = 1,
    string ProductVersion = "",
    IReadOnlyList<RekallAgePlayablePackageFileIntegrity>? Files = null)
{
    public string? ProofLaunchPath { get; init; }
}

public sealed record RekallAgePlayablePackageFileIntegrity(
    string Path,
    long SizeBytes,
    string Sha256);
