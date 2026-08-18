using System.Text.Json;
using Rekall.Age.Core.Compatibility;
using Rekall.Age.Core.Product;
using Rekall.Age.Core.Persistence;

namespace Rekall.Age.World;

public sealed class RekallAgeSceneStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = RekallAgeDocumentSchemaProbe.MaximumDocumentDepth
    };

    public string GetScenePath(string projectRoot, string sceneName)
    {
        return Path.Combine(projectRoot, "Scenes", $"{sceneName}.age.scene.json");
    }

    public async ValueTask SaveAsync(
        string projectRoot,
        RekallAgeSceneDocument scene,
        CancellationToken cancellationToken)
    {
        var scenesDirectory = Path.Combine(projectRoot, "Scenes");
        Directory.CreateDirectory(scenesDirectory);
        var path = GetScenePath(projectRoot, scene.Name);
        await RekallAgeAtomicFile.WriteAllTextAsync(
            path,
            Serialize(scene),
            RekallAgeDocumentSchemaProbe.MaximumDocumentBytes,
            cancellationToken);
    }

    public async ValueTask<string> SaveIfRevisionAsync(
        string projectRoot,
        RekallAgeSceneDocument scene,
        string expectedRevision,
        CancellationToken cancellationToken)
    {
        var scenesDirectory = Path.Combine(projectRoot, "Scenes");
        Directory.CreateDirectory(scenesDirectory);
        return await RekallAgeAtomicFile.WriteAllTextIfRevisionAsync(
            GetScenePath(projectRoot, scene.Name),
            Serialize(scene),
            RekallAgeDocumentSchemaProbe.MaximumDocumentBytes,
            expectedRevision,
            cancellationToken);
    }

    public string Serialize(RekallAgeSceneDocument scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var current = scene with { SchemaVersion = RekallAgeProductInfo.Current.ProjectSchemaVersion };
        return JsonSerializer.Serialize(current, JsonOptions) + Environment.NewLine;
    }

    public async ValueTask<RekallAgeSceneDocument> LoadAsync(
        string projectRoot,
        string sceneName,
        CancellationToken cancellationToken) =>
        (await LoadVersionedAsync(projectRoot, sceneName, cancellationToken).ConfigureAwait(false)).Value;

    public async ValueTask<RekallAgeVersionedDocument<RekallAgeSceneDocument>> LoadVersionedAsync(
        string projectRoot,
        string sceneName,
        CancellationToken cancellationToken)
    {
        var path = GetScenePath(projectRoot, sceneName);
        var snapshot = await RekallAgeDocumentSchemaProbe.ReadSnapshotAsync(
            path,
            "scene",
            RekallAgeProductInfo.Current.ProjectSchemaVersion,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var scene = snapshot.Deserialize<RekallAgeSceneDocument>(JsonOptions);
        return new RekallAgeVersionedDocument<RekallAgeSceneDocument>(scene with
        {
            SchemaVersion = RekallAgeProductInfo.Current.ProjectSchemaVersion
        }, snapshot.File.Revision);
    }

    public IReadOnlyList<string> ListSceneNames(string projectRoot)
    {
        var scenesDirectory = Path.Combine(projectRoot, "Scenes");
        if (!Directory.Exists(scenesDirectory))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(scenesDirectory, "*.age.scene.json")
            .Select(path => Path.GetFileName(path).Replace(".age.scene.json", string.Empty, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }
}
