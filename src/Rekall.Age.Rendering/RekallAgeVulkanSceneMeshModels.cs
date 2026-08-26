using System.Numerics;
using Rekall.Age.Rendering.Abstractions;

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
    RekallAgeVulkanSceneTexture? SurfaceWaterTexture = null,
    float MetallicFactor = 0,
    float RoughnessFactor = 1,
    float NormalScale = 1,
    float OcclusionStrength = 1,
    Vector4 EmissiveFactor = default,
    RekallAgeVulkanSceneAtmosphereMaterial? Atmosphere = null,
    RekallAgeVulkanSceneCloudLayerMaterial? CloudLayer = null,
    RekallAgeVulkanSceneCloudShadowMaterial? CloudShadow = null,
    RekallAgeVulkanSceneSurfaceWaterMaterial? SurfaceWater = null,
    int? VirtualGeometrySourceTriangleCount = null,
    int VirtualGeometryLodLevel = 0)
{
    public bool CastShadows { get; init; } = true;

    public bool ReceiveShadows { get; init; } = true;

    public uint ShadowLayerMask { get; init; } = uint.MaxValue;

    public string AlphaMode { get; init; } = "opaque";

    public float AlphaCutoff { get; init; } = 0.5f;

    public int? SkinIndex { get; init; }

    public IReadOnlyList<RekallAgeVulkanSceneSkinBinding> SkinBindings { get; init; } =
        Array.Empty<RekallAgeVulkanSceneSkinBinding>();

    public IReadOnlyList<RekallAgeVulkanSceneMorphTarget> MorphTargets { get; init; } =
        Array.Empty<RekallAgeVulkanSceneMorphTarget>();

    public IReadOnlyList<float> DefaultMorphWeights { get; init; } = Array.Empty<float>();

    public string MorphWeightSource { get; init; } = "none";

    public RekallAgeRuntimeViewportShaderPipeline? ShaderPipeline { get; init; }

    public string? MaterialAssetId { get; init; }

    public bool VirtualGeometryBudgetSatisfied { get; init; } = true;
}

public sealed record RekallAgeVulkanSceneMorphTarget(
    string Name,
    IReadOnlyList<Vector3> PositionDeltas,
    IReadOnlyList<Vector3> NormalDeltas);

public readonly record struct RekallAgeVulkanSceneSkinBinding(
    int Joint0,
    int Joint1,
    int Joint2,
    int Joint3,
    float Weight0,
    float Weight1,
    float Weight2,
    float Weight3);

public sealed record RekallAgeVulkanSceneAtmosphereMaterial(
    float PlanetRadius,
    float AtmosphereRadius,
    Vector4 RayleighColor,
    Vector4 MieColor,
    Vector4 OzoneAbsorptionColor,
    Vector4 AtmosphereFactors,
    Vector4 ScatteringFactors,
    Vector4 OzoneFactors,
    float AerialPerspectiveStrength,
    int ViewSampleCount,
    int LightSampleCount);

public sealed record RekallAgeVulkanSceneCloudLayerMaterial(
    Vector4 Factors,
    Vector4 Color);

public sealed record RekallAgeVulkanSceneCloudShadowMaterial(
    string TextureAssetId,
    Vector4 Factors);

public sealed record RekallAgeVulkanSceneSurfaceWaterMaterial(
    string TextureAssetId,
    Vector4 Factors);

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
