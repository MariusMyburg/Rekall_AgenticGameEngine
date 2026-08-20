namespace Rekall.Age.Rendering;

public sealed record RekallAgeVulkanSceneCaptureResult(
    bool Captured,
    string OutputPath,
    string? LoaderName,
    RekallAgeVulkanSelectedDevice? SelectedDevice,
    uint Width,
    uint Height,
    string Format,
    ulong BytesRead,
    ulong NonZeroBytes,
    RekallAgeVulkanReadbackPixel FirstPixel,
    ulong ByteChecksum,
    int DrawCallCount,
    int MeshCount,
    int SpriteCount,
    int UnsupportedRenderableCount,
    IReadOnlyList<string> UnsupportedRenderableKinds,
    bool ColorTargetCreated,
    bool DepthTargetCreated,
    bool RenderPassCreated,
    bool FramebufferCreated,
    bool VertexBufferCreated,
    bool IndexBufferCreated,
    bool UniformBufferCreated,
    bool DescriptorSetLayoutCreated,
    bool PipelineLayoutCreated,
    bool GraphicsPipelineCreated,
    bool TextureResourcesCreated,
    IReadOnlyList<string> Errors)
{
    public IReadOnlyList<RekallAgeVulkanShaderPipelineUse> ShaderPipelines { get; init; } =
        Array.Empty<RekallAgeVulkanShaderPipelineUse>();
}

public sealed record RekallAgeVulkanShaderPipelineUse(
    string EntityId,
    string EntityName,
    string VertexShader,
    string FragmentShader,
    string Scope,
    string ContentHash,
    bool Valid,
    bool Fallback,
    IReadOnlyList<string> Diagnostics);
