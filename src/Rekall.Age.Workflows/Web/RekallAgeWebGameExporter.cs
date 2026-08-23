using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Assets;
using Rekall.Age.Core.Compatibility;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Core.Product;
using Rekall.Age.Project;
using Rekall.Age.World;

namespace Rekall.Age.Workflows.Web;

public sealed record RekallAgeWebGameStageRequest(
    string ProjectRoot,
    string EntrySceneName,
    string OutputDirectory,
    int ReferenceWidth = 1280,
    int ReferenceHeight = 720,
    RekallAgeWebModuleRegistryPlan? ModulePlan = null);

public sealed record RekallAgeWebGameStageResult(
    string OutputDirectory,
    string ManifestPath,
    RekallAgeWebGameManifest Manifest);

public sealed class RekallAgeWebGameExporter
{
    public const int MaximumContentEntries = 4096;
    public const long MaximumAggregateContentBytes = 512L * 1024L * 1024L;
    private const string ProjectPath = "rekall.project.json";
    private const string AssetCatalogPath = "Assets/assets.age.catalog.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = RekallAgeDocumentSchemaProbe.MaximumDocumentDepth
    };

    public async ValueTask<RekallAgeWebGameStageResult> StageAsync(
        RekallAgeWebGameStageRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.EntrySceneName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputDirectory);
        var projectRoot = Path.GetFullPath(request.ProjectRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var output = Path.GetFullPath(request.OutputDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        ValidateOutput(projectRoot, output);

        IRekallAgeGameContent content = new RekallAgeFileGameContent(projectRoot);
        var projectEntry = await content.ReadAsync(
            ProjectPath,
            RekallAgeDocumentSchemaProbe.MaximumDocumentBytes,
            cancellationToken);
        var project = ReadProject(projectEntry.Bytes, ProjectPath);
        var scenePath = RekallAgeGameContentPath.Normalize($"Scenes/{request.EntrySceneName}.age.scene.json");
        var sceneEntry = await content.ReadAsync(
            scenePath,
            RekallAgeDocumentSchemaProbe.MaximumDocumentBytes,
            cancellationToken);
        var scene = RekallAgeSceneCodec.Deserialize(sceneEntry.Bytes, request.EntrySceneName, scenePath);

        var staged = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [ProjectPath] = projectEntry.Bytes.ToArray(),
            [scenePath] = sceneEntry.Bytes.ToArray()
        };
        var catalogEntry = await TryReadAsync(content, AssetCatalogPath, cancellationToken);
        if (catalogEntry is not null)
        {
            var catalog = ReadCatalog(catalogEntry.Bytes, AssetCatalogPath);
            var referencedIds = CollectStringValues(scene)
                .ToHashSet(StringComparer.Ordinal);
            var referencedAssets = catalog.Assets
                .Where(asset => referencedIds.Contains(asset.Id))
                .OrderBy(asset => asset.Id, StringComparer.Ordinal)
                .ToArray();
            if (referencedAssets.Length + 3 > MaximumContentEntries)
            {
                throw new InvalidDataException(
                    $"Web game declarative closure exceeds the {MaximumContentEntries}-content-entry limit.");
            }
            var packagedAssets = new List<RekallAgeAssetDocument>();
            foreach (var asset in referencedAssets)
            {
                var logicalPath = ResolveAssetPath(projectRoot, asset.ImportedPath);
                var assetEntry = await content.ReadAsync(
                    logicalPath,
                    RekallAgeDocumentSchemaProbe.MaximumDocumentBytes,
                    cancellationToken);
                staged.TryAdd(logicalPath, assetEntry.Bytes.ToArray());
                packagedAssets.Add(asset with
                {
                    SourcePath = string.Empty,
                    ImportedPath = logicalPath
                });
            }
            staged.Add(AssetCatalogPath, SerializeCatalog(packagedAssets));
        }

        var aggregateBytes = staged.Values.Aggregate(0L, (total, bytes) => checked(total + bytes.LongLength));
        if (aggregateBytes > MaximumAggregateContentBytes)
        {
            throw new InvalidDataException(
                $"Web game declarative closure exceeds the {MaximumAggregateContentBytes}-byte aggregate limit.");
        }

        var inventory = staged
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new RekallAgeWebContentEntry(
                entry.Key,
                MediaType(entry.Key),
                entry.Value.LongLength,
                RekallAgeDocumentRevision.Compute(entry.Value)))
            .ToArray();
        var modulePlan = request.ModulePlan
            ?? new RekallAgeWebModuleRegistryGenerator().Discover(projectRoot);
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!Path.GetFullPath(modulePlan.ProjectRoot).Equals(projectRoot, pathComparison))
        {
            throw new InvalidDataException("Web module publication plan belongs to a different authored project.");
        }
        var manifest = RekallAgeWebGameManifestCodec.Create(
            new RekallAgeWebProjectIdentity(
                project.Name,
                RekallAgeDocumentRevision.Compute(projectEntry.Bytes.Span)),
            scenePath,
            new RekallAgeWebViewportPolicy(request.ReferenceWidth, request.ReferenceHeight, "fit"),
            modulePlan.Modules.Select(module => module.Identity).ToArray(),
            scene.Capabilities
                .Where(capability => capability.StartsWith("rendering", StringComparison.Ordinal)
                    || capability.Equals("ui", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(capability => capability, StringComparer.Ordinal)
                .ToArray(),
            inventory);

        Directory.CreateDirectory(output);
        foreach (var entry in staged.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var destination = ResolveDestination(output, entry.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await File.WriteAllBytesAsync(destination, entry.Value, cancellationToken);
        }
        var manifestPath = Path.Combine(output, "game.manifest.json");
        await File.WriteAllBytesAsync(
            manifestPath,
            RekallAgeWebGameManifestCodec.EncodeForFile(manifest),
            cancellationToken);
        return new RekallAgeWebGameStageResult(output, manifestPath, manifest);
    }

    private static RekallAgeProjectManifest ReadProject(ReadOnlyMemory<byte> bytes, string source)
    {
        RekallAgeDocumentSchemaProbe.Inspect(
            bytes,
            source,
            "project",
            RekallAgeProductInfo.Current.ProjectSchemaVersion);
        var project = JsonSerializer.Deserialize<RekallAgeProjectManifest>(bytes.Span, JsonOptions)
            ?? throw new InvalidDataException($"Project document '{source}' has an invalid required shape.");
        if (string.IsNullOrWhiteSpace(project.Name) || project.Capabilities is null)
        {
            throw new InvalidDataException($"Project document '{source}' has an invalid required shape.");
        }
        return project;
    }

    private static RekallAgeAssetCatalogDocument ReadCatalog(ReadOnlyMemory<byte> bytes, string source)
    {
        JsonDocument.Parse(bytes, new JsonDocumentOptions
        {
            MaxDepth = RekallAgeDocumentSchemaProbe.MaximumDocumentDepth
        }).Dispose();
        var catalog = JsonSerializer.Deserialize<RekallAgeAssetCatalogDocument>(bytes.Span, JsonOptions)
            ?? throw new InvalidDataException($"Asset catalog '{source}' has an invalid required shape.");
        if (catalog.Assets is null || catalog.Assets.Any(asset => asset is null
                || string.IsNullOrWhiteSpace(asset.Id)
                || string.IsNullOrWhiteSpace(asset.ImportedPath)))
        {
            throw new InvalidDataException($"Asset catalog '{source}' has an invalid required shape.");
        }
        return catalog;
    }

    private static byte[] SerializeCatalog(IReadOnlyList<RekallAgeAssetDocument> assets)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(
            new RekallAgeAssetCatalogDocument(
                assets.OrderBy(asset => asset.Id, StringComparer.Ordinal).ToArray()),
            new JsonSerializerOptions(JsonOptions) { WriteIndented = true });
        var result = new byte[json.Length + 1];
        json.CopyTo(result, 0);
        result[^1] = (byte)'\n';
        return result;
    }

    private static IEnumerable<string> CollectStringValues(RekallAgeSceneDocument scene)
    {
        foreach (var component in scene.Entities.SelectMany(entity => entity.Components))
        {
            foreach (var value in CollectStringValues(component.Properties))
            {
                yield return value;
            }
        }
    }

    private static IEnumerable<string> CollectStringValues(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            yield return text;
        }
        else if (node is JsonObject item)
        {
            foreach (var child in item)
            foreach (var nestedText in CollectStringValues(child.Value))
                yield return nestedText;
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            foreach (var nestedText in CollectStringValues(child))
                yield return nestedText;
        }
    }

    private static async ValueTask<RekallAgeGameContentEntry?> TryReadAsync(
        IRekallAgeGameContent content,
        string logicalPath,
        CancellationToken cancellationToken)
    {
        try
        {
            return await content.ReadAsync(
                logicalPath,
                RekallAgeDocumentSchemaProbe.MaximumDocumentBytes,
                cancellationToken);
        }
        catch (RekallAgeGameContentException error) when (
            error.Code == "REKALL_GAME_CONTENT_NOT_FOUND")
        {
            return null;
        }
    }

    private static string ResolveAssetPath(string projectRoot, string importedPath)
    {
        try
        {
            if (Path.IsPathRooted(importedPath))
            {
                var confined = RekallAgeConfinedPath.Resolve(projectRoot, importedPath, "Imported asset");
                return RekallAgeGameContentPath.Normalize(Path.GetRelativePath(projectRoot, confined));
            }
            return RekallAgeGameContentPath.Normalize(importedPath);
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException)
        {
            throw new InvalidDataException(
                $"Imported asset path '{importedPath}' is outside the project root or is not a safe logical path.",
                error);
        }
    }

    private static void ValidateOutput(string projectRoot, string output)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (Directory.Exists(output) || File.Exists(output))
            throw new InvalidOperationException("Web staging output must not already exist.");
        if (output.Equals(projectRoot, comparison)
            || output.StartsWith(projectRoot + Path.DirectorySeparatorChar, comparison)
            || projectRoot.StartsWith(output + Path.DirectorySeparatorChar, comparison))
            throw new InvalidOperationException("Web staging output must not overlap the project root.");
    }

    private static string ResolveDestination(string output, string logicalPath) =>
        RekallAgeConfinedPath.Resolve(
            output,
            Path.Combine(output, logicalPath.Replace('/', Path.DirectorySeparatorChar)),
            "Web staging content");

    private static string MediaType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".json" => "application/json",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".wav" => "audio/wav",
        ".glb" => "model/gltf-binary",
        ".ttf" => "font/ttf",
        ".otf" => "font/otf",
        _ => "application/octet-stream"
    };
}
