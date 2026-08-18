using System.Text.Json;
using Rekall.Age.Core.Persistence;

namespace Rekall.Age.LevelDesign;

public sealed class RekallAgePrefabStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = RekallAgePersistedJson.MaximumDocumentDepth
    };

    public string GetPath(string projectRoot, string prefabId)
    {
        return Path.Combine(projectRoot, "Prefabs", $"{prefabId}.age.prefab.json");
    }

    public async ValueTask SaveAsync(
        string projectRoot,
        RekallAgePrefabDocument prefab,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.Combine(projectRoot, "Prefabs"));
        var json = JsonSerializer.Serialize(prefab, JsonOptions);
        await RekallAgePersistedJson.WriteAllTextAsync(
            GetPath(projectRoot, prefab.Id),
            json + Environment.NewLine,
            cancellationToken);
    }

    public async ValueTask<RekallAgePrefabDocument> LoadAsync(
        string projectRoot,
        string prefabId,
        CancellationToken cancellationToken)
    {
        return await RekallAgePersistedJson.ReadAsync<RekallAgePrefabDocument>(
            GetPath(projectRoot, prefabId),
            JsonOptions,
            cancellationToken);
    }
}
