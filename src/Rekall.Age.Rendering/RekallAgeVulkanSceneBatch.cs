using System.Numerics;

namespace Rekall.Age.Rendering;

public sealed record RekallAgeVulkanSceneBatch(
    IReadOnlyList<RekallAgeVulkanSceneVertex> Vertices,
    IReadOnlyList<uint> Indices,
    IReadOnlyList<RekallAgeVulkanSceneDraw> Draws,
    RekallAgeVulkanSceneFrameUniform Frame,
    RekallAgeVulkanSceneStereoFrame? Stereo = null);

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

public sealed record RekallAgeVulkanSceneStereoFrame(
    bool Enabled,
    string RenderMode,
    bool PreferSinglePassMultiview,
    IReadOnlyList<RekallAgeVulkanSceneViewUniform> Views);

public sealed record RekallAgeVulkanSceneViewUniform(
    string Name,
    int Index,
    Matrix4x4 ViewProjection,
    Vector4 EyePosition,
    Vector4 Viewport);
