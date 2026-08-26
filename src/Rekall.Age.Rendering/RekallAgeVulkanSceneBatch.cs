using System.Numerics;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

public sealed record RekallAgeVulkanSceneBatch(
    IReadOnlyList<RekallAgeVulkanSceneVertex> Vertices,
    IReadOnlyList<uint> Indices,
    IReadOnlyList<RekallAgeVulkanSceneDraw> Draws,
    RekallAgeVulkanSceneFrameUniform Frame,
    RekallAgeVulkanSceneStereoFrame? Stereo = null)
{
    public RekallAgeVulkanEffectiveCamera EffectiveCamera { get; init; } = RekallAgeVulkanEffectiveCamera.Default;
}

public sealed record RekallAgeVulkanEffectiveCamera(
    string? EntityId,
    Vector3 Position,
    Vector3 RotationDegrees,
    Vector3 Forward,
    Vector3 Right,
    Vector3 Up,
    float NearClip,
    float FarClip,
    float Aspect,
    float TangentOrHalfHeight,
    bool Orthographic,
    bool AutoFramed,
    Matrix4x4 View,
    Matrix4x4 Projection,
    Matrix4x4 ViewProjection,
    Matrix4x4 SoftwareViewProjection)
{
    public static RekallAgeVulkanEffectiveCamera Default { get; } = new(
        null,
        Vector3.Zero,
        Vector3.Zero,
        Vector3.UnitZ,
        Vector3.UnitX,
        Vector3.UnitY,
        0.05f,
        100,
        1,
        MathF.Tan(MathF.PI / 360f * 65),
        false,
        false,
        Matrix4x4.Identity,
        Matrix4x4.Identity,
        Matrix4x4.Identity,
        Matrix4x4.Identity);

    public Vector3 ViewOrigin(Vector2 uv)
    {
        if (!Orthographic)
        {
            return Position;
        }

        var ndc = uv * 2 - Vector2.One;
        return Position
            + Right * (ndc.X * TangentOrHalfHeight * Aspect)
            - Up * (ndc.Y * TangentOrHalfHeight);
    }

    public Vector3 ViewRay(Vector2 uv)
    {
        if (Orthographic)
        {
            return Forward;
        }

        var ndc = uv * 2 - Vector2.One;
        return Vector3.Normalize(
            Forward
            + Right * (ndc.X * TangentOrHalfHeight * Aspect)
            - Up * (ndc.Y * TangentOrHalfHeight));
    }
}

public sealed record RekallAgeVulkanDirectionalLightInjection(
    bool Available,
    string? EntityId,
    Vector3 Direction,
    Vector4 Color)
{
    public static RekallAgeVulkanDirectionalLightInjection Disabled { get; } = new(
        false,
        null,
        Vector3.Zero,
        Vector4.Zero);
}

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
    string? CloudShadowTextureId = null,
    string? SurfaceWaterTextureId = null,
    Vector4 MaterialFactors = default,
    Vector4 EmissiveFactors = default,
    Vector4 AtmosphereFactors0 = default,
    Vector4 AtmosphereFactors1 = default,
    Vector4 AtmosphereColor0 = default,
    Vector4 AtmosphereColor1 = default,
    Vector4 AtmosphereColor2 = default,
    Vector4 CloudFactors = default,
    Vector4 CloudColor = default,
    Vector4 CloudShadowFactors = default,
    Vector4 SurfaceWaterFactors = default,
    bool Transparent = false,
    RekallAgeRuntimeViewportShaderPipeline? ShaderPipeline = null,
    string EntityId = "",
    bool CastShadows = true,
    bool ReceiveShadows = true,
    uint ShadowLayerMask = uint.MaxValue,
    string AlphaMode = "opaque",
    float AlphaCutoff = 0.5f);

public sealed record RekallAgeVulkanSceneFrameUniform(
    Matrix4x4 ViewProjection,
    Vector3 LightDirection,
    Vector4 LightColor,
    Vector4 LightPosition,
    Vector4 CameraPosition = default,
    Matrix4x4 SoftwareViewProjection = default,
    Vector3 AdditionalLightDirection = default,
    Vector4 AdditionalLightColor = default,
    Vector4 AdditionalLightPosition = default,
    Vector4 AdditionalLightParameters = default,
    Vector4 EnvironmentParameters = default)
{
    public IReadOnlyList<RekallAgeVulkanPointLight> PointLights { get; init; } = [];

    public int PointLightBudget { get; init; } = 4;

    public IReadOnlyList<string> DroppedPointLightEntityIds { get; init; } = [];
}

public sealed record RekallAgeVulkanPointLight(
    string EntityId,
    Vector4 Color,
    Vector4 Position,
    Vector4 Parameters);

public sealed record RekallAgePointLightSelectionReport(
    int Budget,
    IReadOnlyList<string> SelectedEntityIds,
    IReadOnlyList<string> DroppedEntityIds);

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
