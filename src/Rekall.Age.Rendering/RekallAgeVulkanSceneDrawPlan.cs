using System.Numerics;

namespace Rekall.Age.Rendering;

public sealed record RekallAgeVulkanSceneMaterialKey(
    string? BaseColorTextureId,
    string? NormalTextureId,
    string? MetallicRoughnessTextureId,
    string? OcclusionTextureId,
    string? EmissiveTextureId,
    string? CloudShadowTextureId,
    string? SurfaceWaterTextureId,
    bool Atmosphere = false)
{
    public static RekallAgeVulkanSceneMaterialKey Default { get; } = new(null, null, null, null, null, null, null, false);
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
    string? EmissiveTextureId,
    string? CloudShadowTextureId,
    string? SurfaceWaterTextureId,
    Vector4 AtmosphereFactors0,
    Vector4 AtmosphereFactors1,
    Vector4 AtmosphereColor0,
    Vector4 AtmosphereColor1,
    Vector4 AtmosphereColor2,
    Vector4 CloudFactors,
    Vector4 CloudColor,
    Vector4 CloudShadowFactors,
    Vector4 SurfaceWaterFactors,
    bool Transparent = false)
{
    public RekallAgeVulkanSceneMaterialKey MaterialKey => new(
        BaseColorTextureId,
        NormalTextureId,
        MetallicRoughnessTextureId,
        OcclusionTextureId,
        EmissiveTextureId,
        CloudShadowTextureId,
        SurfaceWaterTextureId,
        AtmosphereFactors0.Y > 0.0f);
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
                draw.EmissiveTextureId,
                draw.CloudShadowTextureId,
                draw.SurfaceWaterTextureId,
                draw.AtmosphereFactors0,
                draw.AtmosphereFactors1,
                draw.AtmosphereColor0,
                draw.AtmosphereColor1,
                draw.AtmosphereColor2,
                draw.CloudFactors,
                draw.CloudColor,
                draw.CloudShadowFactors,
                draw.SurfaceWaterFactors,
                draw.Transparent))
            .ToArray();
        var materialKeys = draws
            .Select(draw => draw.MaterialKey)
            .Append(RekallAgeVulkanSceneMaterialKey.Default)
            .Distinct()
            .ToArray();
        return new RekallAgeVulkanSceneDrawPlan(draws, materialKeys);
    }
}
