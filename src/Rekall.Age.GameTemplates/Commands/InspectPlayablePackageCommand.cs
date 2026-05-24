using Rekall.Age.Core.Commands;
using System.IO.Compression;
using System.Text.Json;

namespace Rekall.Age.GameTemplates.Commands;

public sealed record InspectPlayablePackageRequest(string PackagePath);

public sealed record InspectPlayablePackageResult(
    bool Ready,
    string ManifestPath,
    RekallAgePlayablePackageManifest Manifest);

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
        var (manifestPath, manifest) = await ReadManifestAsync(request.PackagePath, context.CancellationToken);
        var ready = manifest.Kind == "rekall.age.playable.package" &&
            manifest.Checks.All(check => check.Passed) &&
            manifest.DrawAssertions.All(assertion => assertion.Passed);
        return RekallAgeCommandResult<InspectPlayablePackageResult>.Success(
            new InspectPlayablePackageResult(ready, manifestPath, manifest),
            $"Inspected playable package '{manifestPath}'.");
    }

    private static async ValueTask<(string ManifestPath, RekallAgePlayablePackageManifest Manifest)> ReadManifestAsync(
        string packagePath,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(packagePath);
        if (Directory.Exists(fullPath))
        {
            return await ReadManifestFileAsync(Path.Combine(fullPath, "rekall.package.json"), cancellationToken);
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
            return ($"{fullPath}!/rekall.package.json", manifest ?? throw new InvalidOperationException($"Package manifest in '{fullPath}' could not be read."));
        }

        return await ReadManifestFileAsync(fullPath, cancellationToken);
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
}
