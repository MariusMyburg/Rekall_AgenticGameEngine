using System.Numerics;

namespace Rekall.Age.Rendering;

public sealed record RekallAgeVulkanSceneBatch(
    IReadOnlyList<RekallAgeVulkanSceneVertex> Vertices,
    IReadOnlyList<uint> Indices,
    IReadOnlyList<RekallAgeVulkanSceneDraw> Draws,
    RekallAgeVulkanSceneFrameUniform Frame);

public sealed record RekallAgeVulkanSceneDraw(
    uint FirstIndex,
    uint IndexCount,
    int VertexOffset,
    uint VertexCount,
    Matrix4x4 Model,
    string? TextureId = null,
    string? MetallicRoughnessTextureId = null,
    string? NormalTextureId = null,
    string? OcclusionTextureId = null,
    string? EmissiveTextureId = null,
    Vector4 MaterialFactors = default,
    Vector4 EmissiveFactors = default);

public sealed record RekallAgeVulkanSceneFrameUniform(
    Matrix4x4 ViewProjection,
    Vector3 LightDirection,
    Vector4 LightColor,
    Vector4 LightPosition);
