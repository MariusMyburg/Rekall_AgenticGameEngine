using System.Text.Json;

namespace Rekall.Age.Rendering;

public sealed class RekallAgeRenderPlanStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string GetPlanPath(string projectRoot)
    {
        return Path.Combine(projectRoot, "Render", "render.age.plan.json");
    }

    public async ValueTask<RekallAgeRenderPlanDocument> LoadAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var path = GetPlanPath(projectRoot);
        await using var stream = File.OpenRead(path);
        var plan = await JsonSerializer.DeserializeAsync<RekallAgeRenderPlanDocument>(
            stream,
            JsonOptions,
            cancellationToken);
        return plan ?? throw new InvalidOperationException($"Render plan '{path}' could not be read.");
    }

    public async ValueTask SaveAsync(
        string projectRoot,
        RekallAgeRenderPlanDocument plan,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(projectRoot, "Render");
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(plan, JsonOptions);
        await File.WriteAllTextAsync(GetPlanPath(projectRoot), json + Environment.NewLine, cancellationToken);
    }
}
