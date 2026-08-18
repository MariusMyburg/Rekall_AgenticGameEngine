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
        var current = manifest with { SchemaVersion = RekallAgeProductInfo.Current.ProjectSchemaVersion };
        var json = JsonSerializer.Serialize(current, JsonOptions);
        await File.WriteAllTextAsync(path, json + Environment.NewLine, cancellationToken);
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
