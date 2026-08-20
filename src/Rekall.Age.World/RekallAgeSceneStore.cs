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
        ValidateSceneName(sceneName);
        return Path.Combine(projectRoot, "Scenes", $"{sceneName}.age.scene.json");
    }

    public string GetRecoveryPath(string projectRoot, string sceneName) =>
        RekallAgeDocumentRecoveryStore.GetPreviousPath(projectRoot, GetScenePath(projectRoot, sceneName));

    public string GetQuarantineDirectory(string projectRoot, string sceneName) =>
        RekallAgeDocumentRecoveryStore.GetQuarantineDirectory(projectRoot, GetScenePath(projectRoot, sceneName));

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
            GetRecoveryPath(projectRoot, scene.Name),
            cancellationToken);
    }

    public ValueTask<RekallAgeDocumentRecoveryInspection> InspectRecoveryAsync(
        string projectRoot,
        string sceneName,
        CancellationToken cancellationToken) =>
        RekallAgeDocumentRecoveryStore.InspectAsync(
            projectRoot,
            GetScenePath(projectRoot, sceneName),
            "scene",
            RekallAgeProductInfo.Current.ProjectSchemaVersion,
            snapshot => ValidateSnapshot(snapshot, sceneName),
            cancellationToken);

    public async ValueTask<RekallAgeVersionedDocument<RekallAgeSceneDocument>> RestorePreviousAsync(
        string projectRoot,
        string sceneName,
        string expectedRevision,
        CancellationToken cancellationToken)
    {
        await RekallAgeDocumentRecoveryStore.RestorePreviousAsync(
            projectRoot,
            GetScenePath(projectRoot, sceneName),
            "scene",
            RekallAgeProductInfo.Current.ProjectSchemaVersion,
            expectedRevision,
            snapshot => ValidateSnapshot(snapshot, sceneName),
            cancellationToken).ConfigureAwait(false);
        return await LoadVersionedAsync(projectRoot, sceneName, cancellationToken).ConfigureAwait(false);
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

    private static void ValidateSceneName(string sceneName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneName);
        if (Path.IsPathRooted(sceneName) ||
            sceneName is "." or ".." ||
            sceneName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            sceneName.Contains(Path.DirectorySeparatorChar) ||
            sceneName.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException("Scene name must be a single safe file-name segment.", nameof(sceneName));
        }
    }

    private static void ValidateSnapshot(RekallAgeDocumentSnapshot snapshot, string expectedSceneName)
    {
        var scene = snapshot.Deserialize<RekallAgeSceneDocument>(JsonOptions);
        if (string.IsNullOrWhiteSpace(scene.Id) ||
            !string.Equals(scene.Name, expectedSceneName, StringComparison.Ordinal) ||
            scene.Capabilities is null ||
            scene.Entities is null)
        {
            throw new InvalidDataException($"Scene document '{snapshot.File.Path}' has an invalid required shape.");
        }
    }
}
