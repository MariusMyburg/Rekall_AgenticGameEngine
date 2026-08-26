namespace Rekall.Age.Rendering;

public sealed record RekallAgeVulkanScenePipelineDescription(
    string VertexShaderPath,
    string FragmentShaderPath,
    IReadOnlyList<RekallAgeVulkanVertexAttributeDescription> VertexAttributes,
    IReadOnlyList<RekallAgeVulkanDescriptorBindingDescription> DescriptorBindings,
    uint PushConstantBytes,
    bool DepthTestEnabled,
    bool TextureSamplingEnabled,
    bool AlphaBlendingEnabled)
{
    public static RekallAgeVulkanScenePipelineDescription Default { get; } = new(
        Path.Combine("Shaders", "rekall_scene.vert"),
        Path.Combine("Shaders", "rekall_scene.frag"),
        [
            new RekallAgeVulkanVertexAttributeDescription("position", 0, "vec3", 0),
            new RekallAgeVulkanVertexAttributeDescription("normal", 1, "vec3", 12),
            new RekallAgeVulkanVertexAttributeDescription("color", 2, "vec4", 24),
            new RekallAgeVulkanVertexAttributeDescription("uv", 3, "vec2", 40)
        ],
        [
            new RekallAgeVulkanDescriptorBindingDescription("FrameUniform", 0, 0, "uniform-buffer", "vertex+fragment"),
            new RekallAgeVulkanDescriptorBindingDescription("DrawUniform", 1, 0, "dynamic-uniform-buffer", "vertex+fragment"),
            new RekallAgeVulkanDescriptorBindingDescription("BaseColorTexture", 2, 0, "sampled-image", "fragment"),
            new RekallAgeVulkanDescriptorBindingDescription("BaseColorSampler", 2, 1, "sampler", "fragment"),
            new RekallAgeVulkanDescriptorBindingDescription("NormalTexture", 2, 2, "sampled-image", "fragment"),
            new RekallAgeVulkanDescriptorBindingDescription("NormalSampler", 2, 3, "sampler", "fragment"),
            new RekallAgeVulkanDescriptorBindingDescription("MetallicRoughnessTexture", 2, 4, "sampled-image", "fragment"),
            new RekallAgeVulkanDescriptorBindingDescription("MetallicRoughnessSampler", 2, 5, "sampler", "fragment"),
            new RekallAgeVulkanDescriptorBindingDescription("OcclusionTexture", 2, 6, "sampled-image", "fragment"),
            new RekallAgeVulkanDescriptorBindingDescription("OcclusionSampler", 2, 7, "sampler", "fragment"),
            new RekallAgeVulkanDescriptorBindingDescription("EmissiveTexture", 2, 8, "sampled-image", "fragment"),
            new RekallAgeVulkanDescriptorBindingDescription("EmissiveSampler", 2, 9, "sampler", "fragment"),
            new RekallAgeVulkanDescriptorBindingDescription("CloudShadowTexture", 2, 10, "sampled-image", "fragment"),
            new RekallAgeVulkanDescriptorBindingDescription("CloudShadowSampler", 2, 11, "sampler", "fragment"),
            new RekallAgeVulkanDescriptorBindingDescription("SurfaceWaterTexture", 2, 12, "sampled-image", "fragment"),
            new RekallAgeVulkanDescriptorBindingDescription("SurfaceWaterSampler", 2, 13, "sampler", "fragment"),
            new RekallAgeVulkanDescriptorBindingDescription("DirectionalShadowAtlas", 3, 0, "combined-image-sampler", "fragment")
        ],
        PushConstantBytes: 0,
        DepthTestEnabled: true,
        TextureSamplingEnabled: true,
        AlphaBlendingEnabled: true);

    public static RekallAgeVulkanScenePipelineDescription Shadow { get; } = new(
        Path.Combine("Shaders", "rekall_shadow.vert"),
        Path.Combine("Shaders", "rekall_shadow.frag"),
        Default.VertexAttributes,
        [
            new RekallAgeVulkanDescriptorBindingDescription("ShadowCascadeUniform", 0, 0, "uniform-buffer", "vertex"),
            new RekallAgeVulkanDescriptorBindingDescription("DrawUniform", 1, 0, "dynamic-uniform-buffer", "vertex+fragment"),
            new RekallAgeVulkanDescriptorBindingDescription("BaseColorTexture", 2, 0, "sampled-image", "fragment"),
            new RekallAgeVulkanDescriptorBindingDescription("BaseColorSampler", 2, 1, "sampler", "fragment")
        ],
        PushConstantBytes: 0,
        DepthTestEnabled: true,
        TextureSamplingEnabled: true,
        AlphaBlendingEnabled: false);
}

public sealed record RekallAgeVulkanVertexAttributeDescription(
    string Name,
    uint Location,
    string Format,
    uint Offset);

public sealed record RekallAgeVulkanDescriptorBindingDescription(
    string Name,
    uint Set,
    uint Binding,
    string DescriptorType,
    string ShaderStage);
