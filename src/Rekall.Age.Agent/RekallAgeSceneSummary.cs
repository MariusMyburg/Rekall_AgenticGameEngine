namespace Rekall.Age.Agent;

public sealed record RekallAgeSceneSummary(
    string Scene,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<RekallAgeEntitySummary> Entities,
    IReadOnlyList<string> ComponentTypes,
    IReadOnlyList<RekallAgeSceneCameraSummary> Cameras,
    IReadOnlyList<RekallAgeSceneRenderLayerSummary> RenderLayers)
{
    public int EntityCount => Entities.Count;
}

public sealed record RekallAgeEntitySummary(
    string Id,
    string Name,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Components);

public sealed record RekallAgeSceneCameraSummary(
    string EntityId,
    string EntityName,
    string Kind,
    bool Active,
    string CullingMask,
    double RenderOrder = 0,
    double ViewportX = 0,
    double ViewportY = 0,
    double ViewportWidth = 1,
    double ViewportHeight = 1);

public sealed record RekallAgeSceneRenderLayerSummary(
    string Layer,
    int RenderableCount,
    IReadOnlyList<string> EntityNames);
