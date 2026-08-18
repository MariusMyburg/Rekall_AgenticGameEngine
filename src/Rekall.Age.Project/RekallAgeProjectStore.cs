using System.Text.Json;
using Rekall.Age.Core.Compatibility;
using Rekall.Age.Core.Product;

namespace Rekall.Age.Project;

public sealed class RekallAgeProjectStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public const string ManifestFileName = "rekall.project.json";

    public async ValueTask SaveAsync(
        string projectRoot,
        RekallAgeProjectManifest manifest,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(projectRoot);
        var path = Path.Combine(projectRoot, ManifestFileName);
        await File.WriteAllTextAsync(path, Serialize(manifest), cancellationToken);
    }

    public string Serialize(RekallAgeProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var current = manifest with { SchemaVersion = RekallAgeProductInfo.Current.ProjectSchemaVersion };
        return JsonSerializer.Serialize(current, JsonOptions) + Environment.NewLine;
    }

    public async ValueTask<RekallAgeProjectManifest> LoadAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(projectRoot, ManifestFileName);
        await RekallAgeDocumentSchemaProbe.ReadAsync(
            path,
            "project",
            RekallAgeProductInfo.Current.ProjectSchemaVersion,
            cancellationToken);
        await using var stream = File.OpenRead(path);
        var manifest = await JsonSerializer.DeserializeAsync<RekallAgeProjectManifest>(
            stream,
            JsonOptions,
            cancellationToken);
        return (manifest ?? throw new InvalidOperationException($"Manifest '{path}' could not be read.")) with
        {
            SchemaVersion = RekallAgeProductInfo.Current.ProjectSchemaVersion
        };
    }
}
