using System.Numerics;

namespace Rekall.Age.Rendering;

public sealed record RekallAgeVulkanSceneMesh(
    string EntityId,
    string EntityName,
    string Primitive,
    IReadOnlyList<RekallAgeVulkanSceneVertex> Vertices,
    IReadOnlyList<uint> Indices,
    RekallAgeVulkanSceneTexture? BaseColorTexture = null,
    RekallAgeVulkanSceneTexture? MetallicRoughnessTexture = null,
    RekallAgeVulkanSceneTexture? NormalTexture = null,
    RekallAgeVulkanSceneTexture? OcclusionTexture = null,
    RekallAgeVulkanSceneTexture? EmissiveTexture = null,
    float MetallicFactor = 0,
    float RoughnessFactor = 1,
    float NormalScale = 1,
    float OcclusionStrength = 1,
    Vector4 EmissiveFactor = default);

public sealed record RekallAgeVulkanSceneTexture(
    string Id,
    int Width,
    int Height,
    byte[] Rgba,
    RekallAgeVulkanSceneSampler Sampler,
    RekallAgeRuntimeTextureAsset? RuntimeTexture = null);

public sealed record RekallAgeVulkanSceneSampler(
    RekallAgeVulkanSceneFilter MinFilter,
    RekallAgeVulkanSceneFilter MagFilter,
    RekallAgeVulkanSceneWrapMode WrapS,
    RekallAgeVulkanSceneWrapMode WrapT);

public enum RekallAgeVulkanSceneFilter
{
    Nearest,
    Linear
}

public enum RekallAgeVulkanSceneWrapMode
{
    Repeat,
    ClampToEdge,
    MirroredRepeat
}

public readonly record struct RekallAgeVulkanSceneVertex(
    float X,
    float Y,
    float Z,
    float NormalX,
    float NormalY,
    float NormalZ,
    float R,
    float G,
    float B,
    float A,
    float U,
    float V);
