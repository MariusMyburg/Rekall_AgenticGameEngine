using System.Collections.ObjectModel;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

public sealed class RekallAgeInMemoryRenderingDevice : IRekallAgeRenderingDevice
{
    private readonly object _gate = new();
    private readonly List<ResourceSlot> _slots = [];
    private readonly Dictionary<int, ulong> _uploadedBytes = [];
    private bool _disposed;

    public RekallAgeInMemoryRenderingDevice(RekallAgeRenderingDeviceCapabilities capabilities)
    {
        Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        DeviceId = Guid.NewGuid();
    }

    public Guid DeviceId { get; }

    public RekallAgeRenderingDeviceCapabilities Capabilities { get; }

    public int SubmissionCount { get; private set; }

    public RekallAgeGraphicsResourceCreationResult CreateBuffer(RekallAgeBufferDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(descriptor);
        var validation = RekallAgeRenderingDeviceValidator.Validate(descriptor, Capabilities);
        return validation.Valid
            ? Create(RekallAgeGraphicsResourceKind.Buffer, descriptor, descriptor.Label, descriptor.SizeBytes)
            : new(default, validation.Diagnostics);
    }

    public RekallAgeGraphicsResourceCreationResult CreateTexture(RekallAgeTextureDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(descriptor);
        var validation = RekallAgeRenderingDeviceValidator.Validate(descriptor, Capabilities);
        return validation.Valid
            ? Create(RekallAgeGraphicsResourceKind.Texture, descriptor, descriptor.Label, EstimateTextureBytes(descriptor))
            : new(default, validation.Diagnostics);
    }

    public RekallAgeGraphicsResourceCreationResult CreateSampler(RekallAgeSamplerDescriptor descriptor) =>
        CreateValidated(
            descriptor,
            RekallAgeGraphicsResourceKind.Sampler,
            descriptor.Label,
            RekallAgeRenderingDeviceValidator.Validate(descriptor, Capabilities));

    public RekallAgeGraphicsResourceCreationResult CreateShaderModule(RekallAgeShaderModuleDescriptor descriptor) =>
        CreateValidated(
            descriptor,
            RekallAgeGraphicsResourceKind.ShaderModule,
            descriptor.Label,
            RekallAgeRenderingDeviceValidator.Validate(descriptor, Capabilities),
            descriptor.Source is null ? 0 : (ulong)System.Text.Encoding.UTF8.GetByteCount(descriptor.Source));

    public RekallAgeGraphicsResourceCreationResult CreateBindingLayout(RekallAgeBindingLayoutDescriptor descriptor) =>
        CreateValidated(
            descriptor,
            RekallAgeGraphicsResourceKind.BindingLayout,
            descriptor.Label,
            RekallAgeRenderingDeviceValidator.Validate(descriptor, Capabilities));

    public RekallAgeGraphicsResourceCreationResult CreateBindingSet(RekallAgeBindingSetDescriptor descriptor)
    {
        var diagnostics = new List<RekallAgeGraphicsDiagnostic>();
        lock (_gate)
        {
            diagnostics.AddRange(ValidateHandleLocked(descriptor.Layout, RekallAgeGraphicsResourceKind.BindingLayout));
            if (diagnostics.Count == 0)
            {
                var layout = (RekallAgeBindingLayoutDescriptor)_slots[descriptor.Layout.Slot].Resource!.Descriptor;
                foreach (var expected in layout.Entries)
                {
                    var matches = descriptor.Entries.Where(entry => entry.Binding == expected.Binding).ToArray();
                    if (matches.Length != 1)
                    {
                        diagnostics.Add(new("REKALL_GPU_BINDING_SET_INCOMPLETE", $"Binding {expected.Binding} must have exactly one resource.", descriptor.Label));
                        continue;
                    }
                    var requiredKind = expected.Type is RekallAgeBindingType.UniformBuffer or RekallAgeBindingType.ReadOnlyStorageBuffer or RekallAgeBindingType.StorageBuffer
                        ? RekallAgeGraphicsResourceKind.Buffer
                        : expected.Type is RekallAgeBindingType.Sampler or RekallAgeBindingType.ComparisonSampler
                            ? RekallAgeGraphicsResourceKind.Sampler
                            : RekallAgeGraphicsResourceKind.Texture;
                    var resourceDiagnostics = ValidateHandleLocked(matches[0].Resource, requiredKind);
                    diagnostics.AddRange(resourceDiagnostics);
                    if (requiredKind == RekallAgeGraphicsResourceKind.Buffer && resourceDiagnostics.Count == 0)
                    {
                        var buffer = (RekallAgeBufferDescriptor)_slots[matches[0].Resource.Slot].Resource!.Descriptor;
                        var available = matches[0].Offset <= buffer.SizeBytes ? buffer.SizeBytes - matches[0].Offset : 0;
                        var size = matches[0].SizeBytes == 0 ? available : matches[0].SizeBytes;
                        if (matches[0].Offset > buffer.SizeBytes || size > available || size < expected.MinimumBindingSize)
                        {
                            diagnostics.Add(new("REKALL_GPU_BINDING_RANGE_INVALID", $"Binding {expected.Binding} buffer range is invalid.", descriptor.Label));
                        }
                    }
                }
                if (descriptor.Entries.Select(entry => entry.Binding).Distinct().Count() != descriptor.Entries.Count)
                {
                    diagnostics.Add(new("REKALL_GPU_BINDING_DUPLICATE", "Binding set contains duplicate indices.", descriptor.Label));
                }
            }
        }
        return diagnostics.Count == 0
            ? Create(RekallAgeGraphicsResourceKind.BindingSet, descriptor, descriptor.Label, 0)
            : new(default, diagnostics);
    }

    public RekallAgeGraphicsResourceCreationResult CreateGraphicsPipeline(RekallAgeGraphicsPipelineDescriptor descriptor)
    {
        var diagnostics = RekallAgeRenderingDeviceValidator.Validate(descriptor, Capabilities).Diagnostics.ToList();
        lock (_gate)
        {
            diagnostics.AddRange(ValidateShaderStageLocked(descriptor.VertexShader, RekallAgeShaderStage.Vertex));
            diagnostics.AddRange(ValidateShaderStageLocked(descriptor.FragmentShader, RekallAgeShaderStage.Fragment));
            foreach (var layout in descriptor.BindingLayouts)
            {
                diagnostics.AddRange(ValidateHandleLocked(layout, RekallAgeGraphicsResourceKind.BindingLayout));
            }
        }
        return diagnostics.Count == 0
            ? Create(RekallAgeGraphicsResourceKind.RenderPipeline, descriptor, descriptor.Label, 0)
            : new(default, diagnostics);
    }

    public RekallAgeGraphicsResourceCreationResult CreateComputePipeline(RekallAgeComputePipelineDescriptor descriptor)
    {
        var diagnostics = RekallAgeRenderingDeviceValidator.Validate(descriptor, Capabilities).Diagnostics.ToList();
        lock (_gate)
        {
            diagnostics.AddRange(ValidateShaderStageLocked(descriptor.ComputeShader, RekallAgeShaderStage.Compute));
            foreach (var layout in descriptor.BindingLayouts)
            {
                diagnostics.AddRange(ValidateHandleLocked(layout, RekallAgeGraphicsResourceKind.BindingLayout));
            }
        }
        return diagnostics.Count == 0
            ? Create(RekallAgeGraphicsResourceKind.ComputePipeline, descriptor, descriptor.Label, 0)
            : new(default, diagnostics);
    }

    public RekallAgeGraphicsResourceCreationResult CreateRenderTarget(RekallAgeRenderTargetDescriptor descriptor)
    {
        var diagnostics = new List<RekallAgeGraphicsDiagnostic>();
        if (descriptor.Width < 1 || descriptor.Height < 1
            || descriptor.ColorAttachments.Count == 0
            || descriptor.ColorAttachments.Count > Capabilities.MaximumColorAttachments)
        {
            diagnostics.Add(new("REKALL_GPU_RENDER_TARGET_SHAPE_INVALID", "Render target dimensions and attachment count must be bounded and nonzero.", descriptor.Label));
        }
        lock (_gate)
        {
            foreach (var attachment in descriptor.ColorAttachments)
            {
                diagnostics.AddRange(ValidateAttachmentLocked(attachment, descriptor.Width, descriptor.Height, depth: false, descriptor.Label));
            }
            if (descriptor.DepthStencilAttachment is not null)
            {
                diagnostics.AddRange(ValidateAttachmentLocked(descriptor.DepthStencilAttachment, descriptor.Width, descriptor.Height, depth: true, descriptor.Label));
            }
        }
        return diagnostics.Count == 0
            ? Create(RekallAgeGraphicsResourceKind.RenderTarget, descriptor, descriptor.Label, 0)
            : new(default, diagnostics);
    }

    public RekallAgeGraphicsValidationResult WriteBuffer(
        RekallAgeGraphicsResourceHandle buffer,
        ulong offset,
        ReadOnlyMemory<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            var diagnostics = ValidateHandleLocked(buffer, RekallAgeGraphicsResourceKind.Buffer);
            if (diagnostics.Count > 0)
                return new(diagnostics);

            var descriptor = (RekallAgeBufferDescriptor)_slots[buffer.Slot].Resource!.Descriptor;
            if (!descriptor.Usage.HasFlag(RekallAgeBufferUsage.TransferDestination)
                && descriptor.MemoryAccess != RekallAgeMemoryAccess.Upload)
            {
                diagnostics.Add(new(
                    "REKALL_GPU_WRITE_USAGE_INVALID",
                    "Buffer writes require TransferDestination usage or upload memory.",
                    buffer.ToString()));
            }
            if (data.IsEmpty || offset > descriptor.SizeBytes || (ulong)data.Length > descriptor.SizeBytes - offset)
            {
                diagnostics.Add(new(
                    "REKALL_GPU_WRITE_RANGE_INVALID",
                    "Buffer write data must be nonempty and contained by the resource.",
                    buffer.ToString()));
            }
            if (diagnostics.Count == 0)
                _uploadedBytes[buffer.Slot] = SaturatingAdd(_uploadedBytes.GetValueOrDefault(buffer.Slot), (ulong)data.Length);
            return new(diagnostics);
        }
    }

    public RekallAgeGraphicsValidationResult WriteTexture(
        RekallAgeGraphicsResourceHandle texture,
        ReadOnlyMemory<byte> data,
        int mipLevel = 0,
        int arrayLayer = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            var diagnostics = ValidateHandleLocked(texture, RekallAgeGraphicsResourceKind.Texture);
            if (diagnostics.Count > 0)
                return new(diagnostics);

            var descriptor = (RekallAgeTextureDescriptor)_slots[texture.Slot].Resource!.Descriptor;
            if (!descriptor.Usage.HasFlag(RekallAgeTextureUsage.CopyDestination))
            {
                diagnostics.Add(new(
                    "REKALL_GPU_WRITE_USAGE_INVALID",
                    "Texture writes require CopyDestination usage.",
                    texture.ToString()));
            }
            if (descriptor.Format is RekallAgeTextureFormat.Depth24Stencil8 or RekallAgeTextureFormat.Depth32Float)
                diagnostics.Add(new("REKALL_GPU_WRITE_FORMAT_UNSUPPORTED", "Portable raw texture uploads do not support depth/stencil formats.", texture.ToString()));
            if (descriptor.SampleCount != 1)
                diagnostics.Add(new("REKALL_GPU_WRITE_SAMPLE_COUNT_UNSUPPORTED", "Portable raw texture uploads require a single-sampled texture.", texture.ToString()));
            var expectedBytes = TextureSubresourceBytes(descriptor, mipLevel, arrayLayer, diagnostics, texture.ToString());
            if (expectedBytes > 0 && (ulong)data.Length != expectedBytes)
            {
                diagnostics.Add(new(
                    "REKALL_GPU_WRITE_RANGE_INVALID",
                    $"Texture write must contain exactly {expectedBytes} tightly packed bytes for the selected subresource.",
                    texture.ToString()));
            }
            if (diagnostics.Count == 0)
                _uploadedBytes[texture.Slot] = SaturatingAdd(_uploadedBytes.GetValueOrDefault(texture.Slot), (ulong)data.Length);
            return new(diagnostics);
        }
    }

    public RekallAgeGraphicsValidationResult Destroy(RekallAgeGraphicsResourceHandle handle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            var diagnostics = ValidateHandleLocked(handle, expectedKind: null);
            if (diagnostics.Count > 0)
            {
                return new(diagnostics);
            }

            _slots[handle.Slot] = _slots[handle.Slot] with { Resource = null };
            _uploadedBytes.Remove(handle.Slot);
            return new([]);
        }
    }

    public IRekallAgeGraphicsCommandEncoder BeginCommandEncoder(string? label = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new CommandEncoder(this, label);
    }

    public RekallAgeGraphicsValidationResult Submit(RekallAgeGraphicsCommandBuffer commandBuffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(commandBuffer);
        var diagnostics = new List<RekallAgeGraphicsDiagnostic>();
        if (!commandBuffer.Finished)
        {
            diagnostics.Add(new("REKALL_GPU_COMMAND_BUFFER_UNFINISHED", "Only finished immutable command buffers can be submitted.", commandBuffer.Label));
        }
        if (commandBuffer.DeviceId != DeviceId)
        {
            diagnostics.Add(new("REKALL_GPU_HANDLE_FOREIGN", "Command buffer belongs to another rendering device.", commandBuffer.Label));
        }

        lock (_gate)
        {
            diagnostics.AddRange(ValidateCommandSequenceLocked(commandBuffer.Commands));
            if (diagnostics.Count == 0)
            {
                SubmissionCount++;
            }
        }
        return new(diagnostics);
    }

    public IReadOnlyList<RekallAgeGraphicsResourceInspection> InspectResources()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            return _slots
                .Where(slot => slot.Resource is not null)
                .Select(slot => slot.Resource!)
                .OrderBy(resource => resource.Handle.Slot)
                .Select(resource => new RekallAgeGraphicsResourceInspection(
                    resource.Handle,
                    resource.Label,
                    resource.EstimatedBytes,
                    resource.Descriptor)
                {
                    UploadedBytes = _uploadedBytes.GetValueOrDefault(resource.Handle.Slot)
                })
                .ToArray();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _slots.Clear();
            _uploadedBytes.Clear();
            _disposed = true;
        }
    }

    private RekallAgeGraphicsResourceCreationResult Create(
        RekallAgeGraphicsResourceKind kind,
        object descriptor,
        string? label,
        ulong estimatedBytes)
    {
        lock (_gate)
        {
            var slotIndex = _slots.FindIndex(slot => slot.Resource is null);
            if (slotIndex < 0)
            {
                slotIndex = _slots.Count;
                _slots.Add(new ResourceSlot(0, null));
            }
            var generation = checked(_slots[slotIndex].Generation + 1);
            var handle = new RekallAgeGraphicsResourceHandle(DeviceId, kind, slotIndex, generation);
            _slots[slotIndex] = new(generation, new Resource(handle, descriptor, label, estimatedBytes));
            return new(handle, []);
        }
    }

    private RekallAgeGraphicsResourceCreationResult CreateValidated(
        object descriptor,
        RekallAgeGraphicsResourceKind kind,
        string? label,
        RekallAgeGraphicsValidationResult validation,
        ulong estimatedBytes = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(descriptor);
        return validation.Valid
            ? Create(kind, descriptor, label, estimatedBytes)
            : new(default, validation.Diagnostics);
    }

    private IReadOnlyList<RekallAgeGraphicsDiagnostic> ValidateCopy(
        RekallAgeGraphicsResourceHandle source,
        ulong sourceOffset,
        RekallAgeGraphicsResourceHandle destination,
        ulong destinationOffset,
        ulong sizeBytes)
    {
        lock (_gate)
        {
            return ValidateCopyLocked(source, sourceOffset, destination, destinationOffset, sizeBytes);
        }
    }

    private IReadOnlyList<RekallAgeGraphicsDiagnostic> ValidateHandle(
        RekallAgeGraphicsResourceHandle handle,
        RekallAgeGraphicsResourceKind expectedKind)
    {
        lock (_gate)
        {
            return ValidateHandleLocked(handle, expectedKind);
        }
    }

    private IReadOnlyList<RekallAgeGraphicsDiagnostic> ValidateBufferBinding(
        RekallAgeGraphicsResourceHandle buffer,
        RekallAgeBufferUsage requiredUsage,
        ulong offset,
        ulong sizeBytes)
    {
        lock (_gate)
        {
            return ValidateBufferBindingLocked(buffer, requiredUsage, offset, sizeBytes);
        }
    }

    private IReadOnlyList<RekallAgeGraphicsDiagnostic> ValidateBufferBindingLocked(
        RekallAgeGraphicsResourceHandle buffer,
        RekallAgeBufferUsage requiredUsage,
        ulong offset,
        ulong sizeBytes)
    {
        var diagnostics = ValidateHandleLocked(buffer, RekallAgeGraphicsResourceKind.Buffer);
        if (diagnostics.Count > 0)
        {
            return diagnostics;
        }
        var descriptor = (RekallAgeBufferDescriptor)_slots[buffer.Slot].Resource!.Descriptor;
        var available = offset <= descriptor.SizeBytes ? descriptor.SizeBytes - offset : 0;
        var selectedSize = sizeBytes == 0 ? available : sizeBytes;
        if (!descriptor.Usage.HasFlag(requiredUsage))
        {
            diagnostics.Add(new("REKALL_GPU_BUFFER_USAGE_INVALID", $"Buffer requires {requiredUsage} usage.", buffer.ToString()));
        }
        if (selectedSize == 0 || offset > descriptor.SizeBytes || selectedSize > available)
        {
            diagnostics.Add(new("REKALL_GPU_BUFFER_RANGE_INVALID", "Buffer binding range is empty or outside the resource.", buffer.ToString()));
        }
        return diagnostics;
    }

    private IReadOnlyList<RekallAgeGraphicsDiagnostic> ValidateRenderPass(RekallAgeRenderPassDescriptor descriptor)
    {
        lock (_gate)
        {
            return ValidateRenderPassLocked(descriptor);
        }
    }

    private List<RekallAgeGraphicsDiagnostic> ValidateRenderPassLocked(RekallAgeRenderPassDescriptor descriptor)
    {
        if (descriptor.ColorClearValues is null)
        {
            return [new("REKALL_GPU_CLEAR_VALUE_COUNT_INVALID", "Color clear values cannot be null.", descriptor.Label)];
        }
        var diagnostics = ValidateHandleLocked(descriptor.RenderTarget, RekallAgeGraphicsResourceKind.RenderTarget);
        if (diagnostics.Count > 0)
        {
            return diagnostics;
        }
        var target = (RekallAgeRenderTargetDescriptor)_slots[descriptor.RenderTarget.Slot].Resource!.Descriptor;
        if (descriptor.ColorClearValues.Count != 0 && descriptor.ColorClearValues.Count != target.ColorAttachments.Count)
        {
            diagnostics.Add(new("REKALL_GPU_CLEAR_VALUE_COUNT_INVALID", "Color clear values must be empty or match the render target color attachment count.", descriptor.Label));
        }
        if (descriptor.DepthClearValue is < 0 or > 1 || (descriptor.DepthClearValue.HasValue && target.DepthStencilAttachment is null))
        {
            diagnostics.Add(new("REKALL_GPU_DEPTH_CLEAR_INVALID", "Depth clear requires a depth attachment and a value from zero through one.", descriptor.Label));
        }
        return diagnostics;
    }

    private IReadOnlyList<RekallAgeGraphicsDiagnostic> ValidateCommandSequenceLocked(IReadOnlyList<RekallAgeGraphicsCommand> commands)
    {
        var diagnostics = new List<RekallAgeGraphicsDiagnostic>();
        CommandPass pass = CommandPass.None;
        var renderPipelineSet = false;
        var computePipelineSet = false;
        var indexBufferSet = false;
        foreach (var command in commands)
        {
            switch (command)
            {
                case RekallAgeCopyBufferCommand copy:
                    if (pass != CommandPass.None) diagnostics.Add(PassStateDiagnostic("Buffer copies must be outside a pass."));
                    diagnostics.AddRange(ValidateCopyLocked(copy.Source, copy.SourceOffset, copy.Destination, copy.DestinationOffset, copy.SizeBytes));
                    break;
                case RekallAgeBeginRenderPassCommand begin:
                    if (pass != CommandPass.None) diagnostics.Add(PassStateDiagnostic("Passes cannot be nested."));
                    else { pass = CommandPass.Render; renderPipelineSet = false; indexBufferSet = false; }
                    diagnostics.AddRange(ValidateRenderPassLocked(begin.Descriptor));
                    break;
                case RekallAgeSetRenderPipelineCommand set:
                    if (pass != CommandPass.Render) diagnostics.Add(PassStateDiagnostic("A render pipeline can only be bound in a render pass."));
                    diagnostics.AddRange(ValidateHandleLocked(set.Pipeline, RekallAgeGraphicsResourceKind.RenderPipeline));
                    renderPipelineSet = true;
                    break;
                case RekallAgeSetComputePipelineCommand set:
                    if (pass != CommandPass.Compute) diagnostics.Add(PassStateDiagnostic("A compute pipeline can only be bound in a compute pass."));
                    diagnostics.AddRange(ValidateHandleLocked(set.Pipeline, RekallAgeGraphicsResourceKind.ComputePipeline));
                    computePipelineSet = true;
                    break;
                case RekallAgeSetBindingSetCommand set:
                    if (pass == CommandPass.None || set.Index < 0 || set.Index >= Capabilities.MaximumBindingsPerLayout)
                        diagnostics.Add(PassStateDiagnostic("Binding sets require an active pass and a bounded set index."));
                    diagnostics.AddRange(ValidateHandleLocked(set.BindingSet, RekallAgeGraphicsResourceKind.BindingSet));
                    break;
                case RekallAgeSetVertexBufferCommand set:
                    if (pass != CommandPass.Render || set.Slot < 0 || set.Slot >= Capabilities.MaximumVertexBuffers) diagnostics.Add(PassStateDiagnostic("Vertex buffers can only be bound to available slots in a render pass."));
                    diagnostics.AddRange(ValidateBufferBindingLocked(set.Buffer, RekallAgeBufferUsage.Vertex, set.Offset, set.SizeBytes));
                    break;
                case RekallAgeSetIndexBufferCommand set:
                    if (pass != CommandPass.Render) diagnostics.Add(PassStateDiagnostic("Index buffers can only be bound in a render pass."));
                    diagnostics.AddRange(ValidateBufferBindingLocked(set.Buffer, RekallAgeBufferUsage.Index, set.Offset, set.SizeBytes));
                    indexBufferSet = true;
                    break;
                case RekallAgeDrawCommand draw:
                    if (pass != CommandPass.Render || !renderPipelineSet) diagnostics.Add(PassStateDiagnostic("Draw requires an active render pass and render pipeline."));
                    if (draw.VertexCount == 0 || draw.InstanceCount == 0) diagnostics.Add(DrawRangeDiagnostic());
                    break;
                case RekallAgeDrawIndexedCommand draw:
                    if (pass != CommandPass.Render || !renderPipelineSet || !indexBufferSet) diagnostics.Add(PassStateDiagnostic("Indexed draw requires a render pass, pipeline, and index buffer."));
                    if (draw.IndexCount == 0 || draw.InstanceCount == 0) diagnostics.Add(DrawRangeDiagnostic());
                    break;
                case RekallAgeEndRenderPassCommand:
                    if (pass != CommandPass.Render) diagnostics.Add(PassStateDiagnostic("No render pass is active.")); else pass = CommandPass.None;
                    break;
                case RekallAgeBeginComputePassCommand:
                    if (pass != CommandPass.None) diagnostics.Add(PassStateDiagnostic("Passes cannot be nested."));
                    else { pass = CommandPass.Compute; computePipelineSet = false; }
                    if (!Capabilities.SupportsCompute) diagnostics.Add(new("REKALL_GPU_FEATURE_REQUIRED", "Compute commands require compute support."));
                    break;
                case RekallAgeDispatchCommand dispatch:
                    if (pass != CommandPass.Compute || !computePipelineSet) diagnostics.Add(PassStateDiagnostic("Dispatch requires an active compute pass and compute pipeline."));
                    if (!IsDispatchInRange(dispatch.GroupCountX, dispatch.GroupCountY, dispatch.GroupCountZ)) diagnostics.Add(DispatchRangeDiagnostic(Capabilities.MaximumComputeWorkgroupsPerDimension));
                    break;
                case RekallAgeEndComputePassCommand:
                    if (pass != CommandPass.Compute) diagnostics.Add(PassStateDiagnostic("No compute pass is active.")); else pass = CommandPass.None;
                    break;
                default:
                    diagnostics.Add(new("REKALL_GPU_COMMAND_UNKNOWN", $"Unsupported command type {command.GetType().Name}."));
                    break;
            }
        }
        if (pass != CommandPass.None) diagnostics.Add(PassStateDiagnostic("Command buffer ends with an active pass."));
        return diagnostics;
    }

    private static RekallAgeGraphicsDiagnostic PassStateDiagnostic(string message) => new("REKALL_GPU_PASS_STATE_INVALID", message);
    private static RekallAgeGraphicsDiagnostic DrawRangeDiagnostic() => new("REKALL_GPU_DRAW_RANGE_INVALID", "Draw vertex/index and instance counts must be nonzero.");
    private bool IsDispatchInRange(uint x, uint y, uint z) =>
        x > 0 && y > 0 && z > 0
        && x <= Capabilities.MaximumComputeWorkgroupsPerDimension
        && y <= Capabilities.MaximumComputeWorkgroupsPerDimension
        && z <= Capabilities.MaximumComputeWorkgroupsPerDimension;

    private static RekallAgeGraphicsDiagnostic DispatchRangeDiagnostic(uint maximum) => new(
        "REKALL_GPU_DISPATCH_RANGE_INVALID",
        $"Dispatch workgroup counts must be from 1 through {maximum} per dimension.");

    private IReadOnlyList<RekallAgeGraphicsDiagnostic> ValidateCopyLocked(
        RekallAgeGraphicsResourceHandle source,
        ulong sourceOffset,
        RekallAgeGraphicsResourceHandle destination,
        ulong destinationOffset,
        ulong sizeBytes)
    {
        var diagnostics = new List<RekallAgeGraphicsDiagnostic>();
        diagnostics.AddRange(ValidateHandleLocked(source, RekallAgeGraphicsResourceKind.Buffer));
        diagnostics.AddRange(ValidateHandleLocked(destination, RekallAgeGraphicsResourceKind.Buffer));
        if (diagnostics.Count > 0)
        {
            return diagnostics;
        }

        var sourceDescriptor = (RekallAgeBufferDescriptor)_slots[source.Slot].Resource!.Descriptor;
        var destinationDescriptor = (RekallAgeBufferDescriptor)_slots[destination.Slot].Resource!.Descriptor;
        if (!sourceDescriptor.Usage.HasFlag(RekallAgeBufferUsage.CopySource)
            || !destinationDescriptor.Usage.HasFlag(RekallAgeBufferUsage.TransferDestination))
        {
            diagnostics.Add(new("REKALL_GPU_COPY_USAGE_INVALID", "Buffer copy requires CopySource and TransferDestination usage.", null));
        }
        if (sizeBytes == 0
            || sourceOffset > sourceDescriptor.SizeBytes
            || sizeBytes > sourceDescriptor.SizeBytes - sourceOffset
            || destinationOffset > destinationDescriptor.SizeBytes
            || sizeBytes > destinationDescriptor.SizeBytes - destinationOffset)
        {
            diagnostics.Add(new("REKALL_GPU_COPY_RANGE_INVALID", "Buffer copy range is empty or outside a resource.", null));
        }
        return diagnostics;
    }

    private List<RekallAgeGraphicsDiagnostic> ValidateHandleLocked(
        RekallAgeGraphicsResourceHandle handle,
        RekallAgeGraphicsResourceKind? expectedKind)
    {
        if (!handle.IsValid || handle.DeviceId != DeviceId)
        {
            return [new("REKALL_GPU_HANDLE_FOREIGN", "Resource handle is invalid or belongs to another rendering device.", handle.ToString())];
        }
        if (expectedKind.HasValue && handle.Kind != expectedKind.Value)
        {
            return [new("REKALL_GPU_HANDLE_KIND_INVALID", $"Expected {expectedKind.Value} but received {handle.Kind}.", handle.ToString())];
        }
        if (handle.Slot >= _slots.Count
            || _slots[handle.Slot].Generation != handle.Generation
            || _slots[handle.Slot].Resource is null)
        {
            return [new("REKALL_GPU_HANDLE_STALE", "Resource handle is stale or has been destroyed.", handle.ToString())];
        }
        return [];
    }

    private IReadOnlyList<RekallAgeGraphicsDiagnostic> ValidateShaderStageLocked(
        RekallAgeGraphicsResourceHandle handle,
        RekallAgeShaderStage expectedStage)
    {
        var diagnostics = ValidateHandleLocked(handle, RekallAgeGraphicsResourceKind.ShaderModule);
        if (diagnostics.Count > 0)
        {
            return diagnostics;
        }
        var shader = (RekallAgeShaderModuleDescriptor)_slots[handle.Slot].Resource!.Descriptor;
        return shader.Stage == expectedStage
            ? []
            : [new("REKALL_GPU_SHADER_STAGE_MISMATCH", $"Expected {expectedStage} shader but received {shader.Stage}.", handle.ToString())];
    }

    private IReadOnlyList<RekallAgeGraphicsDiagnostic> ValidateAttachmentLocked(
        RekallAgeRenderTargetAttachment attachment,
        int width,
        int height,
        bool depth,
        string? label)
    {
        var diagnostics = ValidateHandleLocked(attachment.Texture, RekallAgeGraphicsResourceKind.Texture);
        if (diagnostics.Count > 0)
        {
            return diagnostics;
        }
        var texture = (RekallAgeTextureDescriptor)_slots[attachment.Texture.Slot].Resource!.Descriptor;
        var requiredUsage = depth ? RekallAgeTextureUsage.DepthStencilAttachment : RekallAgeTextureUsage.ColorAttachment;
        if (!texture.Usage.HasFlag(requiredUsage) || texture.Width < width || texture.Height < height
            || attachment.MipLevel < 0 || attachment.MipLevel >= texture.MipLevels
            || attachment.ArrayLayer < 0 || attachment.ArrayLayer >= texture.ArrayLayers)
        {
            return [new("REKALL_GPU_RENDER_TARGET_ATTACHMENT_INVALID", "Render-target attachment usage, extent, mip, or layer is incompatible.", label)];
        }
        return [];
    }

    private static ulong EstimateTextureBytes(RekallAgeTextureDescriptor descriptor) => RekallAgeTextureLayout.TotalBytes(descriptor);

    private static ulong TextureSubresourceBytes(
        RekallAgeTextureDescriptor descriptor,
        int mipLevel,
        int arrayLayer,
        List<RekallAgeGraphicsDiagnostic> diagnostics,
        string target)
    {
        if (mipLevel < 0 || mipLevel >= descriptor.MipLevels
            || arrayLayer < 0 || arrayLayer >= descriptor.ArrayLayers)
        {
            diagnostics.Add(new(
                "REKALL_GPU_WRITE_SUBRESOURCE_INVALID",
                "Texture mip level or array layer is outside the resource.",
                target));
            return 0;
        }

        return RekallAgeTextureLayout.SubresourceBytes(descriptor, mipLevel);
    }

    private static ulong SaturatingAdd(ulong left, ulong right) => ulong.MaxValue - left < right ? ulong.MaxValue : left + right;

    private sealed class CommandEncoder : IRekallAgeGraphicsCommandEncoder
    {
        private readonly RekallAgeInMemoryRenderingDevice _device;
        private readonly string? _label;
        private readonly List<RekallAgeGraphicsCommand> _commands = [];
        private bool _finished;
        private bool _disposed;
        private CommandPass _pass;
        private bool _renderPipelineSet;
        private bool _computePipelineSet;
        private bool _indexBufferSet;

        public CommandEncoder(RekallAgeInMemoryRenderingDevice device, string? label)
        {
            _device = device;
            _label = label;
        }

        public RekallAgeGraphicsValidationResult BeginRenderPass(RekallAgeRenderPassDescriptor descriptor)
        {
            EnsureWritable();
            ArgumentNullException.ThrowIfNull(descriptor);
            if (_pass != CommandPass.None) return InvalidPass("Passes cannot be nested.");
            var diagnostics = _device.ValidateRenderPass(descriptor);
            if (diagnostics.Count > 0) return new(diagnostics);
            _commands.Add(new RekallAgeBeginRenderPassCommand(descriptor));
            _pass = CommandPass.Render;
            _renderPipelineSet = false;
            _indexBufferSet = false;
            return Valid();
        }

        public RekallAgeGraphicsValidationResult SetRenderPipeline(RekallAgeGraphicsResourceHandle pipeline)
        {
            EnsureWritable();
            if (_pass != CommandPass.Render) return InvalidPass("A render pipeline can only be bound in a render pass.");
            var diagnostics = _device.ValidateHandle(pipeline, RekallAgeGraphicsResourceKind.RenderPipeline);
            if (diagnostics.Count > 0) return new(diagnostics);
            _commands.Add(new RekallAgeSetRenderPipelineCommand(pipeline));
            _renderPipelineSet = true;
            return Valid();
        }

        public RekallAgeGraphicsValidationResult SetComputePipeline(RekallAgeGraphicsResourceHandle pipeline)
        {
            EnsureWritable();
            if (_pass != CommandPass.Compute) return InvalidPass("A compute pipeline can only be bound in a compute pass.");
            var diagnostics = _device.ValidateHandle(pipeline, RekallAgeGraphicsResourceKind.ComputePipeline);
            if (diagnostics.Count > 0) return new(diagnostics);
            _commands.Add(new RekallAgeSetComputePipelineCommand(pipeline));
            _computePipelineSet = true;
            return Valid();
        }

        public RekallAgeGraphicsValidationResult SetBindingSet(int index, RekallAgeGraphicsResourceHandle bindingSet)
        {
            EnsureWritable();
            if (_pass == CommandPass.None || index < 0 || index >= _device.Capabilities.MaximumBindingsPerLayout)
                return InvalidPass("Binding sets require an active pass and a bounded set index.");
            var diagnostics = _device.ValidateHandle(bindingSet, RekallAgeGraphicsResourceKind.BindingSet);
            if (diagnostics.Count > 0) return new(diagnostics);
            _commands.Add(new RekallAgeSetBindingSetCommand(index, bindingSet));
            return Valid();
        }

        public RekallAgeGraphicsValidationResult SetVertexBuffer(int slot, RekallAgeGraphicsResourceHandle buffer, ulong offset = 0, ulong sizeBytes = 0)
        {
            EnsureWritable();
            if (_pass != CommandPass.Render || slot < 0 || slot >= _device.Capabilities.MaximumVertexBuffers)
                return InvalidPass("Vertex buffers can only be bound to available slots in a render pass.");
            var diagnostics = _device.ValidateBufferBinding(buffer, RekallAgeBufferUsage.Vertex, offset, sizeBytes);
            if (diagnostics.Count > 0) return new(diagnostics);
            _commands.Add(new RekallAgeSetVertexBufferCommand(slot, buffer, offset, sizeBytes));
            return Valid();
        }

        public RekallAgeGraphicsValidationResult SetIndexBuffer(RekallAgeGraphicsResourceHandle buffer, RekallAgeIndexFormat format, ulong offset = 0, ulong sizeBytes = 0)
        {
            EnsureWritable();
            if (_pass != CommandPass.Render) return InvalidPass("Index buffers can only be bound in a render pass.");
            var diagnostics = _device.ValidateBufferBinding(buffer, RekallAgeBufferUsage.Index, offset, sizeBytes);
            if (diagnostics.Count > 0) return new(diagnostics);
            _commands.Add(new RekallAgeSetIndexBufferCommand(buffer, format, offset, sizeBytes));
            _indexBufferSet = true;
            return Valid();
        }

        public RekallAgeGraphicsValidationResult Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
        {
            EnsureWritable();
            if (_pass != CommandPass.Render || !_renderPipelineSet) return InvalidPass("Draw requires an active render pass and render pipeline.");
            if (vertexCount == 0 || instanceCount == 0) return new([DrawRangeDiagnostic()]);
            _commands.Add(new RekallAgeDrawCommand(vertexCount, instanceCount, firstVertex, firstInstance));
            return Valid();
        }

        public RekallAgeGraphicsValidationResult DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0)
        {
            EnsureWritable();
            if (_pass != CommandPass.Render || !_renderPipelineSet || !_indexBufferSet)
                return InvalidPass("Indexed draw requires a render pass, pipeline, and index buffer.");
            if (indexCount == 0 || instanceCount == 0) return new([DrawRangeDiagnostic()]);
            _commands.Add(new RekallAgeDrawIndexedCommand(indexCount, instanceCount, firstIndex, baseVertex, firstInstance));
            return Valid();
        }

        public RekallAgeGraphicsValidationResult EndRenderPass()
        {
            EnsureWritable();
            if (_pass != CommandPass.Render) return InvalidPass("No render pass is active.");
            _commands.Add(new RekallAgeEndRenderPassCommand());
            _pass = CommandPass.None;
            return Valid();
        }

        public RekallAgeGraphicsValidationResult BeginComputePass(string? label = null)
        {
            EnsureWritable();
            if (_pass != CommandPass.None) return InvalidPass("Passes cannot be nested.");
            if (!_device.Capabilities.SupportsCompute)
                return new([new("REKALL_GPU_FEATURE_REQUIRED", "Compute commands require compute support.", label)]);
            _commands.Add(new RekallAgeBeginComputePassCommand(label));
            _pass = CommandPass.Compute;
            _computePipelineSet = false;
            return Valid();
        }

        public RekallAgeGraphicsValidationResult Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1)
        {
            EnsureWritable();
            if (_pass != CommandPass.Compute || !_computePipelineSet)
                return InvalidPass("Dispatch requires an active compute pass and compute pipeline.");
            if (!_device.IsDispatchInRange(groupCountX, groupCountY, groupCountZ))
                return new([DispatchRangeDiagnostic(_device.Capabilities.MaximumComputeWorkgroupsPerDimension)]);
            _commands.Add(new RekallAgeDispatchCommand(groupCountX, groupCountY, groupCountZ));
            return Valid();
        }

        public RekallAgeGraphicsValidationResult EndComputePass()
        {
            EnsureWritable();
            if (_pass != CommandPass.Compute) return InvalidPass("No compute pass is active.");
            _commands.Add(new RekallAgeEndComputePassCommand());
            _pass = CommandPass.None;
            return Valid();
        }

        public RekallAgeGraphicsValidationResult CopyBuffer(
            RekallAgeGraphicsResourceHandle source,
            ulong sourceOffset,
            RekallAgeGraphicsResourceHandle destination,
            ulong destinationOffset,
            ulong sizeBytes)
        {
            EnsureWritable();
            if (_pass != CommandPass.None) return InvalidPass("Buffer copies must be outside a pass.");
            var diagnostics = _device.ValidateCopy(source, sourceOffset, destination, destinationOffset, sizeBytes);
            if (diagnostics.Count == 0)
            {
                _commands.Add(new RekallAgeCopyBufferCommand(source, sourceOffset, destination, destinationOffset, sizeBytes));
            }
            return new(diagnostics);
        }

        public RekallAgeGraphicsCommandBuffer Finish()
        {
            EnsureWritable();
            if (_pass != CommandPass.None)
            {
                throw new InvalidOperationException("Command encoder cannot finish while a render or compute pass is active.");
            }
            _finished = true;
            return new(
                _device.DeviceId,
                _label,
                new ReadOnlyCollection<RekallAgeGraphicsCommand>(_commands.ToArray()));
        }

        public void Dispose() => _disposed = true;

        private void EnsureWritable()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_finished)
            {
                throw new InvalidOperationException("Command encoder has already been finished.");
            }
        }

        private static RekallAgeGraphicsValidationResult Valid() => new([]);
        private static RekallAgeGraphicsValidationResult InvalidPass(string message) => new([PassStateDiagnostic(message)]);
    }

    private enum CommandPass { None, Render, Compute }

    private sealed record Resource(
        RekallAgeGraphicsResourceHandle Handle,
        object Descriptor,
        string? Label,
        ulong EstimatedBytes);

    private sealed record ResourceSlot(uint Generation, Resource? Resource);
}
