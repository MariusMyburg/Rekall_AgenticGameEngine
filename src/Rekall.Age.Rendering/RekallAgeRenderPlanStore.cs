using System.Text.Json;
using Rekall.Age.Core.Persistence;

namespace Rekall.Age.Rendering;

public sealed class RekallAgeRenderPlanStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = RekallAgePersistedJson.MaximumDocumentDepth
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
        return await RekallAgePersistedJson.ReadAsync<RekallAgeRenderPlanDocument>(
            path,
            JsonOptions,
            cancellationToken);
    }

    public async ValueTask SaveAsync(
        string projectRoot,
        RekallAgeRenderPlanDocument plan,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(projectRoot, "Render");
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(plan, JsonOptions);
        await RekallAgePersistedJson.WriteAllTextAsync(
            GetPlanPath(projectRoot),
            json + Environment.NewLine,
            cancellationToken);
    }
}
