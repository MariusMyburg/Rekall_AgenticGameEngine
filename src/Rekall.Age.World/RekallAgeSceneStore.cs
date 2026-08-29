using Rekall.Age.Core.Compatibility;
using Rekall.Age.Core.Product;
using Rekall.Age.Core.Persistence;

namespace Rekall.Age.World;

public sealed class RekallAgeSceneStore
{
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
        => RekallAgeSceneCodec.Serialize(scene);

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
        var scene = RekallAgeSceneCodec.DeserializeValidated(snapshot.File.Bytes, sceneName, snapshot.File.Path);
        return new RekallAgeVersionedDocument<RekallAgeSceneDocument>(scene, snapshot.File.Revision);
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

    public static bool IsValidSceneName(string? sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName)) return false;
        return !Path.IsPathRooted(sceneName)
            && sceneName is not "." and not ".."
            && sceneName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            && !sceneName.Contains(Path.DirectorySeparatorChar)
            && !sceneName.Contains(Path.AltDirectorySeparatorChar);
    }

    private static void ValidateSceneName(string sceneName)
    {
        if (!IsValidSceneName(sceneName))
        {
            throw new ArgumentException("Scene name must be a single safe file-name segment.", nameof(sceneName));
        }
    }

    private static void ValidateSnapshot(RekallAgeDocumentSnapshot snapshot, string expectedSceneName)
    {
        RekallAgeSceneCodec.DeserializeValidated(snapshot.File.Bytes, expectedSceneName, snapshot.File.Path);
    }
}
