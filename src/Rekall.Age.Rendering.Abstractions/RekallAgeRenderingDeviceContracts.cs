namespace Rekall.Age.Rendering.Abstractions;

public enum RekallAgeGraphicsResourceKind
{
    Buffer,
    Texture,
    TextureView,
    Sampler,
    ShaderModule,
    BindingLayout,
    BindingSet,
    RenderPipeline,
    ComputePipeline,
    RenderTarget,
    CommandBuffer
}

public readonly record struct RekallAgeGraphicsResourceHandle(
    Guid DeviceId,
    RekallAgeGraphicsResourceKind Kind,
    int Slot,
    uint Generation)
{
    public bool IsValid => DeviceId != Guid.Empty && Slot >= 0 && Generation > 0;

    public bool BelongsTo(Guid deviceId) => IsValid && DeviceId == deviceId;

    public override string ToString() =>
        $"{Kind.ToString().ToLowerInvariant()}:{Slot}@{Generation}";
}

[Flags]
public enum RekallAgeBufferUsage
{
    None = 0,
    CopySource = 1 << 0,
    TransferDestination = 1 << 1,
    Vertex = 1 << 2,
    Index = 1 << 3,
    Uniform = 1 << 4,
    Storage = 1 << 5,
    Indirect = 1 << 6,
    Readback = 1 << 7
}

public enum RekallAgeMemoryAccess
{
    DeviceLocal,
    Upload,
    Readback
}

public sealed record RekallAgeBufferDescriptor(
    ulong SizeBytes,
    RekallAgeBufferUsage Usage,
    RekallAgeMemoryAccess MemoryAccess = RekallAgeMemoryAccess.DeviceLocal,
    string? Label = null);

public enum RekallAgeTextureDimension
{
    Texture1D,
    Texture2D,
    Texture3D,
    Cube
}

public enum RekallAgeTextureFormat
{
    R8Unorm,
    Rg8Unorm,
    Rgba8Unorm,
    Rgba8UnormSrgb,
    Bgra8Unorm,
    Bgra8UnormSrgb,
    Rgba16Float,
    R32Float,
    Depth24Stencil8,
    Depth32Float
}

[Flags]
public enum RekallAgeTextureUsage
{
    None = 0,
    CopySource = 1 << 0,
    CopyDestination = 1 << 1,
    Sampled = 1 << 2,
    Storage = 1 << 3,
    ColorAttachment = 1 << 4,
    DepthStencilAttachment = 1 << 5,
    Present = 1 << 6
}

public sealed record RekallAgeTextureDescriptor(
    RekallAgeTextureDimension Dimension,
    int Width,
    int Height,
    int Depth,
    int MipLevels,
    int ArrayLayers,
    int SampleCount,
    RekallAgeTextureFormat Format,
    RekallAgeTextureUsage Usage,
    string? Label = null);

public enum RekallAgeFilter { Nearest, Linear }
public enum RekallAgeMipmapFilter { Nearest, Linear }
public enum RekallAgeAddressMode { ClampToEdge, Repeat, MirrorRepeat }
public enum RekallAgeCompareOperation { Never, Less, LessEqual, Equal, GreaterEqual, Greater, NotEqual, Always }

public sealed record RekallAgeSamplerDescriptor(
    RekallAgeFilter MinFilter = RekallAgeFilter.Linear,
    RekallAgeFilter MagFilter = RekallAgeFilter.Linear,
    RekallAgeMipmapFilter MipmapFilter = RekallAgeMipmapFilter.Linear,
    RekallAgeAddressMode AddressU = RekallAgeAddressMode.Repeat,
    RekallAgeAddressMode AddressV = RekallAgeAddressMode.Repeat,
    RekallAgeAddressMode AddressW = RekallAgeAddressMode.Repeat,
    float MinimumLod = 0,
    float MaximumLod = 32,
    int MaximumAnisotropy = 1,
    RekallAgeCompareOperation? Compare = null,
    string? Label = null);

[Flags]
public enum RekallAgeShaderStage
{
    None = 0,
    Vertex = 1 << 0,
    Fragment = 1 << 1,
    Compute = 1 << 2
}

public enum RekallAgeShaderSourceLanguage { Glsl, SpirV, Wgsl }

public sealed record RekallAgeShaderModuleDescriptor(
    RekallAgeShaderStage Stage,
    RekallAgeShaderSourceLanguage Language,
    string Source,
    string EntryPoint = "main",
    string? Label = null);

public enum RekallAgeBindingType
{
    UniformBuffer,
    ReadOnlyStorageBuffer,
    StorageBuffer,
    Sampler,
    ComparisonSampler,
    SampledTexture,
    ReadOnlyStorageTexture,
    StorageTexture
}

public sealed record RekallAgeBindingLayoutEntry(
    int Binding,
    RekallAgeBindingType Type,
    RekallAgeShaderStage Visibility,
    ulong MinimumBindingSize = 0);

public sealed record RekallAgeBindingLayoutDescriptor(
    IReadOnlyList<RekallAgeBindingLayoutEntry> Entries,
    string? Label = null);

public sealed record RekallAgeBindingSetEntry(
    int Binding,
    RekallAgeGraphicsResourceHandle Resource,
    ulong Offset = 0,
    ulong SizeBytes = 0);

public sealed record RekallAgeBindingSetDescriptor(
    RekallAgeGraphicsResourceHandle Layout,
    IReadOnlyList<RekallAgeBindingSetEntry> Entries,
    string? Label = null);

public enum RekallAgePrimitiveTopology { TriangleList, TriangleStrip, LineList, LineStrip, PointList }
public enum RekallAgeCullMode { None, Front, Back }
public enum RekallAgeFrontFace { Clockwise, CounterClockwise }

public sealed record RekallAgeColorTargetDescriptor(
    RekallAgeTextureFormat Format,
    bool BlendEnabled = false,
    ulong WriteMask = 0xFUL);

public sealed record RekallAgeDepthStencilDescriptor(
    RekallAgeTextureFormat Format,
    bool DepthWriteEnabled = true,
    RekallAgeCompareOperation DepthCompare = RekallAgeCompareOperation.LessEqual);

public sealed record RekallAgeGraphicsPipelineDescriptor(
    RekallAgeGraphicsResourceHandle VertexShader,
    RekallAgeGraphicsResourceHandle FragmentShader,
    IReadOnlyList<RekallAgeGraphicsResourceHandle> BindingLayouts,
    IReadOnlyList<RekallAgeColorTargetDescriptor> ColorTargets,
    RekallAgeDepthStencilDescriptor? DepthStencil = null,
    RekallAgePrimitiveTopology Topology = RekallAgePrimitiveTopology.TriangleList,
    RekallAgeCullMode CullMode = RekallAgeCullMode.Back,
    RekallAgeFrontFace FrontFace = RekallAgeFrontFace.CounterClockwise,
    string? Label = null);

public sealed record RekallAgeComputePipelineDescriptor(
    RekallAgeGraphicsResourceHandle ComputeShader,
    IReadOnlyList<RekallAgeGraphicsResourceHandle> BindingLayouts,
    string? Label = null);

public sealed record RekallAgeRenderTargetAttachment(
    RekallAgeGraphicsResourceHandle Texture,
    int MipLevel = 0,
    int ArrayLayer = 0);

public sealed record RekallAgeRenderTargetDescriptor(
    IReadOnlyList<RekallAgeRenderTargetAttachment> ColorAttachments,
    RekallAgeRenderTargetAttachment? DepthStencilAttachment,
    int Width,
    int Height,
    string? Label = null);

public sealed record RekallAgeRenderingDeviceCapabilities(
    string Backend,
    ulong MaximumBufferSizeBytes,
    int MaximumTextureDimension1D,
    int MaximumTextureDimension2D,
    int MaximumTextureDimension3D,
    int MaximumTextureArrayLayers,
    int MaximumColorAttachments,
    int MaximumBindingsPerLayout,
    int MaximumSamplerAnisotropy,
    int MaximumShaderSourceBytes,
    bool SupportsCompute,
    bool SupportsStorageBuffers,
    bool SupportsStorageTextures,
    bool SupportsIndirectDrawing,
    bool SupportsTimestampQueries)
{
    public static RekallAgeRenderingDeviceCapabilities DesktopBaseline(string backend) => new(
        backend,
        MaximumBufferSizeBytes: 1UL << 30,
        MaximumTextureDimension1D: 16384,
        MaximumTextureDimension2D: 16384,
        MaximumTextureDimension3D: 2048,
        MaximumTextureArrayLayers: 2048,
        MaximumColorAttachments: 8,
        MaximumBindingsPerLayout: 32,
        MaximumSamplerAnisotropy: 16,
        MaximumShaderSourceBytes: 1024 * 1024,
        SupportsCompute: true,
        SupportsStorageBuffers: true,
        SupportsStorageTextures: true,
        SupportsIndirectDrawing: true,
        SupportsTimestampQueries: true);
}

public sealed record RekallAgeGraphicsDiagnostic(
    string Code,
    string Message,
    string? Target = null);

public sealed record RekallAgeGraphicsValidationResult(
    IReadOnlyList<RekallAgeGraphicsDiagnostic> Diagnostics)
{
    public bool Valid => Diagnostics.Count == 0;
}

public sealed record RekallAgeGraphicsResourceCreationResult(
    RekallAgeGraphicsResourceHandle Handle,
    IReadOnlyList<RekallAgeGraphicsDiagnostic> Diagnostics)
{
    public bool Created => Handle.IsValid && Diagnostics.Count == 0;
}

public sealed record RekallAgeGraphicsResourceInspection(
    RekallAgeGraphicsResourceHandle Handle,
    string? Label,
    ulong EstimatedBytes,
    object Descriptor);

public abstract record RekallAgeGraphicsCommand;

public sealed record RekallAgeCopyBufferCommand(
    RekallAgeGraphicsResourceHandle Source,
    ulong SourceOffset,
    RekallAgeGraphicsResourceHandle Destination,
    ulong DestinationOffset,
    ulong SizeBytes) : RekallAgeGraphicsCommand;

public sealed record RekallAgeGraphicsCommandBuffer(
    Guid DeviceId,
    string? Label,
    IReadOnlyList<RekallAgeGraphicsCommand> Commands,
    bool Finished = true);

public interface IRekallAgeGraphicsCommandEncoder : IDisposable
{
    RekallAgeGraphicsValidationResult CopyBuffer(
        RekallAgeGraphicsResourceHandle source,
        ulong sourceOffset,
        RekallAgeGraphicsResourceHandle destination,
        ulong destinationOffset,
        ulong sizeBytes);

    RekallAgeGraphicsCommandBuffer Finish();
}

public interface IRekallAgeRenderingDevice : IDisposable
{
    Guid DeviceId { get; }

    RekallAgeRenderingDeviceCapabilities Capabilities { get; }

    RekallAgeGraphicsResourceCreationResult CreateBuffer(RekallAgeBufferDescriptor descriptor);

    RekallAgeGraphicsResourceCreationResult CreateTexture(RekallAgeTextureDescriptor descriptor);

    RekallAgeGraphicsResourceCreationResult CreateSampler(RekallAgeSamplerDescriptor descriptor);

    RekallAgeGraphicsResourceCreationResult CreateShaderModule(RekallAgeShaderModuleDescriptor descriptor);

    RekallAgeGraphicsResourceCreationResult CreateBindingLayout(RekallAgeBindingLayoutDescriptor descriptor);

    RekallAgeGraphicsResourceCreationResult CreateBindingSet(RekallAgeBindingSetDescriptor descriptor);

    RekallAgeGraphicsResourceCreationResult CreateGraphicsPipeline(RekallAgeGraphicsPipelineDescriptor descriptor);

    RekallAgeGraphicsResourceCreationResult CreateComputePipeline(RekallAgeComputePipelineDescriptor descriptor);

    RekallAgeGraphicsResourceCreationResult CreateRenderTarget(RekallAgeRenderTargetDescriptor descriptor);

    RekallAgeGraphicsValidationResult Destroy(RekallAgeGraphicsResourceHandle handle);

    IRekallAgeGraphicsCommandEncoder BeginCommandEncoder(string? label = null);

    RekallAgeGraphicsValidationResult Submit(RekallAgeGraphicsCommandBuffer commandBuffer);

    IReadOnlyList<RekallAgeGraphicsResourceInspection> InspectResources();
}
