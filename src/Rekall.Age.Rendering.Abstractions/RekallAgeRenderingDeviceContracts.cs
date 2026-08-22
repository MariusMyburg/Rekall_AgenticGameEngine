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

public sealed record RekallAgeRenderingDeviceCapabilities(
    string Backend,
    ulong MaximumBufferSizeBytes,
    int MaximumTextureDimension1D,
    int MaximumTextureDimension2D,
    int MaximumTextureDimension3D,
    int MaximumTextureArrayLayers,
    int MaximumColorAttachments,
    int MaximumBindingsPerLayout,
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

    RekallAgeGraphicsValidationResult Destroy(RekallAgeGraphicsResourceHandle handle);

    IRekallAgeGraphicsCommandEncoder BeginCommandEncoder(string? label = null);

    RekallAgeGraphicsValidationResult Submit(RekallAgeGraphicsCommandBuffer commandBuffer);

    IReadOnlyList<RekallAgeGraphicsResourceInspection> InspectResources();
}
