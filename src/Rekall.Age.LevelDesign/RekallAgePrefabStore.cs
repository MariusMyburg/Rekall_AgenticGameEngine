using System.Text.Json;

namespace Rekall.Age.LevelDesign;

public sealed class RekallAgePrefabStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
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
        await File.WriteAllTextAsync(GetPath(projectRoot, prefab.Id), json + Environment.NewLine, cancellationToken);
    }

    public async ValueTask<RekallAgePrefabDocument> LoadAsync(
        string projectRoot,
        string prefabId,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(GetPath(projectRoot, prefabId));
        return await JsonSerializer.DeserializeAsync<RekallAgePrefabDocument>(
            stream,
            JsonOptions,
            cancellationToken) ?? throw new InvalidOperationException($"Prefab '{prefabId}' could not be read.");
    }
}
