using System.Numerics;

namespace Rekall.Age.Rendering;

public sealed record RekallAgeVulkanSceneMaterialKey(
    string? BaseColorTextureId,
    string? NormalTextureId,
    string? MetallicRoughnessTextureId,
    string? OcclusionTextureId,
    string? EmissiveTextureId)
{
    public static RekallAgeVulkanSceneMaterialKey Default { get; } = new(null, null, null, null, null);
}

public sealed record RekallAgeVulkanScenePreparedDraw(
    uint FirstIndex,
    uint IndexCount,
    int VertexOffset,
    Matrix4x4 Model,
    Vector4 MaterialFactors,
    Vector4 EmissiveFactors,
    string? BaseColorTextureId,
    string? MetallicRoughnessTextureId,
    string? NormalTextureId,
    string? OcclusionTextureId,
    string? EmissiveTextureId)
{
    public RekallAgeVulkanSceneMaterialKey MaterialKey => new(
        BaseColorTextureId,
        NormalTextureId,
        MetallicRoughnessTextureId,
        OcclusionTextureId,
        EmissiveTextureId);
}

public sealed record RekallAgeVulkanSceneDrawPlan(
    IReadOnlyList<RekallAgeVulkanScenePreparedDraw> Draws,
    IReadOnlyList<RekallAgeVulkanSceneMaterialKey> MaterialKeys)
{
    public static RekallAgeVulkanSceneDrawPlan Empty { get; } = new(
        Array.Empty<RekallAgeVulkanScenePreparedDraw>(),
        [RekallAgeVulkanSceneMaterialKey.Default]);
}

public static class RekallAgeVulkanSceneDrawPlanBuilder
{
    public static RekallAgeVulkanSceneDrawPlan Build(RekallAgeVulkanSceneBatch batch)
    {
        var draws = batch.Draws
            .Select(draw => new RekallAgeVulkanScenePreparedDraw(
                draw.FirstIndex,
                draw.IndexCount,
                draw.VertexOffset,
                draw.Model,
                draw.MaterialFactors,
                draw.EmissiveFactors,
                draw.TextureId,
                draw.MetallicRoughnessTextureId,
                draw.NormalTextureId,
                draw.OcclusionTextureId,
                draw.EmissiveTextureId))
            .ToArray();
        var materialKeys = draws
            .Select(draw => draw.MaterialKey)
            .Append(RekallAgeVulkanSceneMaterialKey.Default)
            .Distinct()
            .ToArray();
        return new RekallAgeVulkanSceneDrawPlan(draws, materialKeys);
    }
}
