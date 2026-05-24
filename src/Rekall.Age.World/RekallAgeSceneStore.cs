using System.Text.Json;

namespace Rekall.Age.World;

public sealed class RekallAgeSceneStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
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
        var json = JsonSerializer.Serialize(scene, JsonOptions);
        await File.WriteAllTextAsync(path, json + Environment.NewLine, cancellationToken);
    }

    public async ValueTask<RekallAgeSceneDocument> LoadAsync(
        string projectRoot,
        string sceneName,
        CancellationToken cancellationToken)
    {
        var path = GetScenePath(projectRoot, sceneName);
        await using var stream = File.OpenRead(path);
        var scene = await JsonSerializer.DeserializeAsync<RekallAgeSceneDocument>(
            stream,
            JsonOptions,
            cancellationToken);
        return scene ?? throw new InvalidOperationException($"Scene '{path}' could not be read.");
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
