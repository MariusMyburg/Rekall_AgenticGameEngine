namespace Rekall.Age.Rendering;

public sealed record RekallAgeVulkanScenePipelineDescription(
    string VertexShaderPath,
    string FragmentShaderPath,
    IReadOnlyList<RekallAgeVulkanVertexAttributeDescription> VertexAttributes,
    IReadOnlyList<RekallAgeVulkanDescriptorBindingDescription> DescriptorBindings,
    uint PushConstantBytes,
    bool DepthTestEnabled,
    bool TextureSamplingEnabled)
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
            new RekallAgeVulkanDescriptorBindingDescription("FrameUniform", 0, "uniform-buffer", "vertex+fragment"),
            new RekallAgeVulkanDescriptorBindingDescription("BaseColorTexture", 1, "combined-image-sampler", "fragment"),
            new RekallAgeVulkanDescriptorBindingDescription("NormalTexture", 2, "combined-image-sampler", "fragment"),
            new RekallAgeVulkanDescriptorBindingDescription("MetallicRoughnessTexture", 3, "combined-image-sampler", "fragment"),
            new RekallAgeVulkanDescriptorBindingDescription("OcclusionTexture", 4, "combined-image-sampler", "fragment"),
            new RekallAgeVulkanDescriptorBindingDescription("EmissiveTexture", 5, "combined-image-sampler", "fragment")
        ],
        PushConstantBytes: 96,
        DepthTestEnabled: true,
        TextureSamplingEnabled: true);
}

public sealed record RekallAgeVulkanVertexAttributeDescription(
    string Name,
    uint Location,
    string Format,
    uint Offset);

public sealed record RekallAgeVulkanDescriptorBindingDescription(
    string Name,
    uint Binding,
    string DescriptorType,
    string ShaderStage);
