using System.Collections.ObjectModel;
using System.Text;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Veldrid;
using Veldrid.SPIRV;

namespace Rekall.Age.Player.Windows;

/// <summary>
/// Records AGE RenderingDevice command buffers into the Windows Player's active
/// Veldrid command list. Engine-owned resources are imported explicitly and are
/// never exposed as native objects to authored modules.
/// </summary>
internal sealed class RekallAgeVeldridRenderingDevice : IRekallAgeRenderingDevice
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ResourceFactory _factory;
    private readonly CommandList _commands;
    private readonly Dictionary<int, Entry> _resources = [];
    private readonly Dictionary<int, ulong> _uploadedBytes = [];
    private int _nextSlot;
    private bool _disposed;

    public RekallAgeVeldridRenderingDevice(GraphicsDevice graphicsDevice, CommandList commands)
    {
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        _factory = graphicsDevice.ResourceFactory;
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        DeviceId = Guid.NewGuid();
        Capabilities = RekallAgeRenderingDeviceCapabilities.DesktopBaseline($"veldrid-{graphicsDevice.BackendType.ToString().ToLowerInvariant()}")
            with { SupportsStorageBuffers = false, SupportsStorageTextures = false };
    }

    public Guid DeviceId { get; }
    public RekallAgeRenderingDeviceCapabilities Capabilities { get; }

    public RekallAgeGraphicsResourceHandle ImportTexture(Texture texture, string label)
    {
        ArgumentNullException.ThrowIfNull(texture);
        var view = _factory.CreateTextureView(texture);
        return Add(RekallAgeGraphicsResourceKind.Texture, new TextureEntry(texture, view, ownsTexture: false), label,
            new RekallAgeTextureDescriptor(RekallAgeTextureDimension.Texture2D, (int)texture.Width, (int)texture.Height,
                (int)texture.Depth, (int)texture.MipLevels, (int)texture.ArrayLayers, 1,
                Map(texture.Format), RekallAgeTextureUsage.Sampled | RekallAgeTextureUsage.ColorAttachment, label),
            ownsNative: true);
    }

    public RekallAgeGraphicsResourceHandle ImportRenderTarget(Framebuffer framebuffer, string label)
    {
        ArgumentNullException.ThrowIfNull(framebuffer);
        return Add(RekallAgeGraphicsResourceKind.RenderTarget, framebuffer, label,
            new RekallAgeRenderTargetDescriptor([], null, (int)framebuffer.Width, (int)framebuffer.Height, label),
            ownsNative: false);
    }

    public RekallAgeGraphicsResourceCreationResult CreateBuffer(RekallAgeBufferDescriptor descriptor) =>
        Create(RekallAgeGraphicsResourceKind.Buffer, descriptor, () =>
        {
            if (descriptor.SizeBytes > uint.MaxValue) throw new NotSupportedException("Veldrid buffers are limited to 4 GiB per allocation.");
            return _factory.CreateBuffer(new BufferDescription((uint)descriptor.SizeBytes, Map(descriptor.Usage, descriptor.MemoryAccess)));
        }, descriptor.SizeBytes);

    public RekallAgeGraphicsResourceCreationResult CreateTexture(RekallAgeTextureDescriptor descriptor) =>
        Create(RekallAgeGraphicsResourceKind.Texture, descriptor, () =>
        {
            var texture = _factory.CreateTexture(new TextureDescription(
                (uint)descriptor.Width, (uint)descriptor.Height, (uint)descriptor.Depth,
                (uint)descriptor.MipLevels, (uint)descriptor.ArrayLayers, Map(descriptor.Format),
                Map(descriptor.Usage, descriptor.Dimension), Map(descriptor.Dimension), MapSampleCount(descriptor.SampleCount)));
            try { return new TextureEntry(texture, _factory.CreateTextureView(texture), ownsTexture: true); }
            catch { texture.Dispose(); throw; }
        }, EstimateTextureBytes(descriptor));

    public RekallAgeGraphicsResourceCreationResult CreateSampler(RekallAgeSamplerDescriptor descriptor) =>
        Create(RekallAgeGraphicsResourceKind.Sampler, descriptor, () => _factory.CreateSampler(new SamplerDescription(
            Map(descriptor.AddressU), Map(descriptor.AddressV), Map(descriptor.AddressW), Map(descriptor),
            descriptor.Compare.HasValue ? Map(descriptor.Compare.Value) : null,
            (uint)descriptor.MaximumAnisotropy, (uint)descriptor.MinimumLod, (uint)descriptor.MaximumLod,
            0, SamplerBorderColor.TransparentBlack)));

    public RekallAgeGraphicsResourceCreationResult CreateShaderModule(RekallAgeShaderModuleDescriptor descriptor) =>
        Create(RekallAgeGraphicsResourceKind.ShaderModule, descriptor, () =>
        {
            if (descriptor.Language != RekallAgeShaderSourceLanguage.Glsl)
                throw new NotSupportedException("The current Veldrid backend accepts portable GLSL source; SPIR-V byte payloads and WGSL require their dedicated adapters.");
            return new ShaderEntry(
                descriptor,
                descriptor.Stage == RekallAgeShaderStage.Compute
                    ? _factory.CreateFromSpirv(new ShaderDescription(ShaderStages.Compute, Encoding.UTF8.GetBytes(descriptor.Source), descriptor.EntryPoint))
                    : null);
        });

    public RekallAgeGraphicsResourceCreationResult CreateBindingLayout(RekallAgeBindingLayoutDescriptor descriptor) =>
        Create(RekallAgeGraphicsResourceKind.BindingLayout, descriptor, () => _factory.CreateResourceLayout(new ResourceLayoutDescription(
            descriptor.Entries.OrderBy(entry => entry.Binding).Select(entry => new ResourceLayoutElementDescription(
                $"Binding{entry.Binding}", Map(entry.Type), Map(entry.Visibility))).ToArray())));

    public RekallAgeGraphicsResourceCreationResult CreateBindingSet(RekallAgeBindingSetDescriptor descriptor) =>
        Create(RekallAgeGraphicsResourceKind.BindingSet, descriptor, () =>
        {
            var layout = Native<ResourceLayout>(descriptor.Layout, RekallAgeGraphicsResourceKind.BindingLayout);
            var bindings = descriptor.Entries.OrderBy(entry => entry.Binding).Select(ToBindableResource).ToArray();
            return _factory.CreateResourceSet(new ResourceSetDescription(layout, bindings));
        });

    public RekallAgeGraphicsResourceCreationResult CreateGraphicsPipeline(RekallAgeGraphicsPipelineDescriptor descriptor) =>
        Create(RekallAgeGraphicsResourceKind.RenderPipeline, descriptor, () =>
        {
            var vertex = Shader(descriptor.VertexShader, RekallAgeShaderStage.Vertex).Descriptor;
            var fragment = Shader(descriptor.FragmentShader, RekallAgeShaderStage.Fragment).Descriptor;
            var shaders = _factory.CreateFromSpirv(
                new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(vertex.Source), vertex.EntryPoint),
                new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(fragment.Source), fragment.EntryPoint));
            var layouts = descriptor.BindingLayouts.Select(handle => Native<ResourceLayout>(handle, RekallAgeGraphicsResourceKind.BindingLayout)).ToArray();
            var vertexLayouts = descriptor.VertexBuffers.Select(layout => new VertexLayoutDescription(
                (uint)layout.StrideBytes,
                layout.StepMode == RekallAgeVertexStepMode.Instance ? 1U : 0U,
                layout.Attributes.OrderBy(attribute => attribute.Location).Select(attribute => new VertexElementDescription(
                    attribute.Name,
                    VertexElementSemantic.TextureCoordinate,
                    Map(attribute.Format),
                    (uint)attribute.OffsetBytes)).ToArray())).ToArray();
            var output = new OutputDescription(
                descriptor.DepthStencil is null ? null : new OutputAttachmentDescription(Map(descriptor.DepthStencil.Format)),
                descriptor.ColorTargets.Select(target => new OutputAttachmentDescription(Map(target.Format))).ToArray());
            try
            {
                return new PipelineEntry(_factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                    descriptor.ColorTargets.Any(target => target.BlendEnabled)
                        ? BlendStateDescription.SingleAlphaBlend
                        : BlendStateDescription.SingleOverrideBlend,
                    descriptor.DepthStencil is null
                        ? DepthStencilStateDescription.Disabled
                        : new DepthStencilStateDescription(true, descriptor.DepthStencil.DepthWriteEnabled, Map(descriptor.DepthStencil.DepthCompare)),
                    new RasterizerStateDescription(Map(descriptor.CullMode), PolygonFillMode.Solid, Map(descriptor.FrontFace), true, false),
                    Map(descriptor.Topology), new ShaderSetDescription(vertexLayouts, shaders), layouts, output)), shaders);
            }
            catch
            {
                foreach (var shader in shaders) shader.Dispose();
                throw;
            }
        });

    public RekallAgeGraphicsResourceCreationResult CreateComputePipeline(RekallAgeComputePipelineDescriptor descriptor) =>
        Create(RekallAgeGraphicsResourceKind.ComputePipeline, descriptor, () => _factory.CreateComputePipeline(new ComputePipelineDescription(
            Shader(descriptor.ComputeShader, RekallAgeShaderStage.Compute).Native
                ?? throw new InvalidOperationException("Compute shader was not compiled."),
            descriptor.BindingLayouts.Select(handle => Native<ResourceLayout>(handle, RekallAgeGraphicsResourceKind.BindingLayout)).ToArray(),
            1, 1, 1)));

    public RekallAgeGraphicsResourceCreationResult CreateRenderTarget(RekallAgeRenderTargetDescriptor descriptor) =>
        Create(RekallAgeGraphicsResourceKind.RenderTarget, descriptor, () => _factory.CreateFramebuffer(new FramebufferDescription(
            descriptor.DepthStencilAttachment is null ? null : Texture(descriptor.DepthStencilAttachment.Texture).Texture,
            descriptor.ColorAttachments.Select(attachment => Texture(attachment.Texture).Texture).ToArray())));

    public RekallAgeGraphicsValidationResult WriteBuffer(
        RekallAgeGraphicsResourceHandle buffer,
        ulong offset,
        ReadOnlyMemory<byte> data)
    {
        if (!TryEntry(buffer, out var entry, out var diagnostic)) return Invalid(diagnostic!);
        if (entry!.Kind != RekallAgeGraphicsResourceKind.Buffer || entry.Native is not DeviceBuffer native
            || entry.Descriptor is not RekallAgeBufferDescriptor descriptor)
            return Invalid(new RekallAgeGraphicsDiagnostic("REKALL_GPU_RESOURCE_INVALID", $"Resource {buffer} is not a writable buffer."));
        if (!descriptor.Usage.HasFlag(RekallAgeBufferUsage.TransferDestination)
            && descriptor.MemoryAccess != RekallAgeMemoryAccess.Upload)
            return Invalid(new RekallAgeGraphicsDiagnostic("REKALL_GPU_WRITE_USAGE_INVALID", "Buffer writes require TransferDestination usage or upload memory.", buffer.ToString()));
        if (data.IsEmpty || offset > descriptor.SizeBytes || (ulong)data.Length > descriptor.SizeBytes - offset || offset > uint.MaxValue)
            return Invalid(new RekallAgeGraphicsDiagnostic("REKALL_GPU_WRITE_RANGE_INVALID", "Buffer write data must be nonempty and contained by the resource.", buffer.ToString()));
        try
        {
            _graphicsDevice.UpdateBuffer(native, (uint)offset, data.ToArray());
            _uploadedBytes[buffer.Slot] = SaturatingAdd(_uploadedBytes.GetValueOrDefault(buffer.Slot), (ulong)data.Length);
            return Valid();
        }
        catch (Exception exception)
        {
            return Invalid(new RekallAgeGraphicsDiagnostic("REKALL_GPU_NATIVE_WRITE_FAILED", exception.Message, buffer.ToString()));
        }
    }

    public RekallAgeGraphicsValidationResult WriteTexture(
        RekallAgeGraphicsResourceHandle texture,
        ReadOnlyMemory<byte> data,
        int mipLevel = 0,
        int arrayLayer = 0)
    {
        if (!TryEntry(texture, out var entry, out var diagnostic)) return Invalid(diagnostic!);
        if (entry!.Kind != RekallAgeGraphicsResourceKind.Texture || entry.Native is not TextureEntry native
            || entry.Descriptor is not RekallAgeTextureDescriptor descriptor)
            return Invalid(new RekallAgeGraphicsDiagnostic("REKALL_GPU_RESOURCE_INVALID", $"Resource {texture} is not a writable texture."));
        if (!descriptor.Usage.HasFlag(RekallAgeTextureUsage.CopyDestination))
            return Invalid(new RekallAgeGraphicsDiagnostic("REKALL_GPU_WRITE_USAGE_INVALID", "Texture writes require CopyDestination usage.", texture.ToString()));
        if (mipLevel < 0 || mipLevel >= descriptor.MipLevels || arrayLayer < 0 || arrayLayer >= descriptor.ArrayLayers)
            return Invalid(new RekallAgeGraphicsDiagnostic("REKALL_GPU_WRITE_SUBRESOURCE_INVALID", "Texture mip level or array layer is outside the resource.", texture.ToString()));
        if (descriptor.Format is RekallAgeTextureFormat.Depth24Stencil8 or RekallAgeTextureFormat.Depth32Float)
            return Invalid(new RekallAgeGraphicsDiagnostic("REKALL_GPU_WRITE_FORMAT_UNSUPPORTED", "Portable raw texture uploads do not support depth/stencil formats.", texture.ToString()));
        if (descriptor.SampleCount != 1)
            return Invalid(new RekallAgeGraphicsDiagnostic("REKALL_GPU_WRITE_SAMPLE_COUNT_UNSUPPORTED", "Portable raw texture uploads require a single-sampled texture.", texture.ToString()));
        var width = Math.Max(1, descriptor.Width >> mipLevel);
        var height = Math.Max(1, descriptor.Height >> mipLevel);
        var depth = Math.Max(1, descriptor.Depth >> mipLevel);
        var expectedBytes = (ulong)width * (ulong)height * (ulong)depth * BytesPerPixel(descriptor.Format);
        if ((ulong)data.Length != expectedBytes)
            return Invalid(new RekallAgeGraphicsDiagnostic("REKALL_GPU_WRITE_RANGE_INVALID", $"Texture write must contain exactly {expectedBytes} tightly packed bytes for the selected subresource.", texture.ToString()));
        try
        {
            _graphicsDevice.UpdateTexture(native.Texture, data.ToArray(), 0, 0, 0,
                (uint)width, (uint)height, (uint)depth, (uint)mipLevel, (uint)arrayLayer);
            _uploadedBytes[texture.Slot] = SaturatingAdd(_uploadedBytes.GetValueOrDefault(texture.Slot), (ulong)data.Length);
            return Valid();
        }
        catch (Exception exception)
        {
            return Invalid(new RekallAgeGraphicsDiagnostic("REKALL_GPU_NATIVE_WRITE_FAILED", exception.Message, texture.ToString()));
        }
    }

    public RekallAgeGraphicsValidationResult Destroy(RekallAgeGraphicsResourceHandle handle)
    {
        if (!TryEntry(handle, out var entry, out var diagnostic)) return Invalid(diagnostic!);
        _resources.Remove(handle.Slot);
        _uploadedBytes.Remove(handle.Slot);
        if (entry!.OwnsNative && entry.Native is IDisposable disposable) disposable.Dispose();
        return Valid();
    }

    public IRekallAgeGraphicsCommandEncoder BeginCommandEncoder(string? label = null) => new Encoder(this, label);

    public RekallAgeGraphicsValidationResult Submit(RekallAgeGraphicsCommandBuffer commandBuffer)
    {
        ArgumentNullException.ThrowIfNull(commandBuffer);
        if (commandBuffer.DeviceId != DeviceId) return Invalid(new RekallAgeGraphicsDiagnostic("REKALL_GPU_COMMAND_DEVICE_MISMATCH", "Command buffer belongs to another rendering device."));
        try
        {
            var renderPass = false;
            var computePass = false;
            foreach (var command in commandBuffer.Commands)
            {
                switch (command)
                {
                    case RekallAgeCopyBufferCommand copy:
                        _commands.CopyBuffer(Native<DeviceBuffer>(copy.Source, RekallAgeGraphicsResourceKind.Buffer), (uint)copy.SourceOffset,
                            Native<DeviceBuffer>(copy.Destination, RekallAgeGraphicsResourceKind.Buffer), (uint)copy.DestinationOffset, (uint)copy.SizeBytes);
                        break;
                    case RekallAgeBeginRenderPassCommand begin:
                        var framebuffer = Native<Framebuffer>(begin.Descriptor.RenderTarget, RekallAgeGraphicsResourceKind.RenderTarget);
                        _commands.SetFramebuffer(framebuffer);
                        _commands.SetFullViewports();
                        _commands.SetFullScissorRects();
                        for (var i = 0; i < begin.Descriptor.ColorClearValues.Count; i++)
                        {
                            var clear = begin.Descriptor.ColorClearValues[i];
                            _commands.ClearColorTarget((uint)i, new RgbaFloat(clear.Red, clear.Green, clear.Blue, clear.Alpha));
                        }
                        if (begin.Descriptor.DepthClearValue is float depth) _commands.ClearDepthStencil(depth, (byte)(begin.Descriptor.StencilClearValue ?? 0));
                        renderPass = true;
                        break;
                    case RekallAgeSetRenderPipelineCommand set: _commands.SetPipeline(Pipeline(set.Pipeline, RekallAgeGraphicsResourceKind.RenderPipeline)); break;
                    case RekallAgeSetComputePipelineCommand set: _commands.SetPipeline(Pipeline(set.Pipeline, RekallAgeGraphicsResourceKind.ComputePipeline)); break;
                    case RekallAgeSetBindingSetCommand set when renderPass: _commands.SetGraphicsResourceSet((uint)set.Index, Native<ResourceSet>(set.BindingSet, RekallAgeGraphicsResourceKind.BindingSet)); break;
                    case RekallAgeSetBindingSetCommand set when computePass: _commands.SetComputeResourceSet((uint)set.Index, Native<ResourceSet>(set.BindingSet, RekallAgeGraphicsResourceKind.BindingSet)); break;
                    case RekallAgeSetVertexBufferCommand set: _commands.SetVertexBuffer((uint)set.Slot, Native<DeviceBuffer>(set.Buffer, RekallAgeGraphicsResourceKind.Buffer), (uint)set.Offset); break;
                    case RekallAgeSetIndexBufferCommand set: _commands.SetIndexBuffer(Native<DeviceBuffer>(set.Buffer, RekallAgeGraphicsResourceKind.Buffer), set.Format == RekallAgeIndexFormat.UInt16 ? IndexFormat.UInt16 : IndexFormat.UInt32, (uint)set.Offset); break;
                    case RekallAgeDrawCommand draw: _commands.Draw(draw.VertexCount, draw.InstanceCount, draw.FirstVertex, draw.FirstInstance); break;
                    case RekallAgeDrawIndexedCommand draw: _commands.DrawIndexed(draw.IndexCount, draw.InstanceCount, draw.FirstIndex, draw.BaseVertex, draw.FirstInstance); break;
                    case RekallAgeEndRenderPassCommand: renderPass = false; break;
                    case RekallAgeBeginComputePassCommand: computePass = true; break;
                    case RekallAgeDispatchCommand dispatch: _commands.Dispatch(dispatch.GroupCountX, dispatch.GroupCountY, dispatch.GroupCountZ); break;
                    case RekallAgeEndComputePassCommand: computePass = false; break;
                    default: return Invalid(new RekallAgeGraphicsDiagnostic("REKALL_GPU_COMMAND_UNSUPPORTED", $"Veldrid cannot execute {command.GetType().Name}."));
                }
            }
            return Valid();
        }
        catch (Exception exception)
        {
            return Invalid(new RekallAgeGraphicsDiagnostic("REKALL_GPU_NATIVE_SUBMIT_FAILED", exception.Message, commandBuffer.Label));
        }
    }

    public IReadOnlyList<RekallAgeGraphicsResourceInspection> InspectResources() => _resources
        .OrderBy(item => item.Key)
        .Select(item => new RekallAgeGraphicsResourceInspection(
            new(DeviceId, item.Value.Kind, item.Key, 1), item.Value.Label, item.Value.EstimatedBytes, item.Value.Descriptor)
        {
            UploadedBytes = _uploadedBytes.GetValueOrDefault(item.Key)
        })
        .ToArray();

    public void Dispose()
    {
        if (_disposed) return;
        foreach (var entry in _resources.Values.Reverse()) if (entry.OwnsNative && entry.Native is IDisposable disposable) disposable.Dispose();
        _resources.Clear();
        _uploadedBytes.Clear();
        _disposed = true;
    }

    private RekallAgeGraphicsResourceCreationResult Create(RekallAgeGraphicsResourceKind kind, object descriptor, Func<object> factory, ulong estimatedBytes = 0)
    {
        var validation = ValidateDescriptor(descriptor);
        if (!validation.Valid) return new(default, validation.Diagnostics);
        try { return new(Add(kind, factory(), Label(descriptor), descriptor, true, estimatedBytes), []); }
        catch (Exception exception) { return new(default, [new("REKALL_GPU_NATIVE_CREATE_FAILED", exception.Message, Label(descriptor))]); }
    }

    private RekallAgeGraphicsValidationResult ValidateDescriptor(object descriptor) => descriptor switch
    {
        RekallAgeBufferDescriptor value => RekallAgeRenderingDeviceValidator.Validate(value, Capabilities),
        RekallAgeTextureDescriptor value => RekallAgeRenderingDeviceValidator.Validate(value, Capabilities),
        RekallAgeSamplerDescriptor value => RekallAgeRenderingDeviceValidator.Validate(value, Capabilities),
        RekallAgeShaderModuleDescriptor value => RekallAgeRenderingDeviceValidator.Validate(value, Capabilities),
        RekallAgeBindingLayoutDescriptor value => RekallAgeRenderingDeviceValidator.Validate(value, Capabilities),
        RekallAgeBindingSetDescriptor => Valid(),
        RekallAgeGraphicsPipelineDescriptor value => RekallAgeRenderingDeviceValidator.Validate(value, Capabilities),
        RekallAgeComputePipelineDescriptor value => RekallAgeRenderingDeviceValidator.Validate(value, Capabilities),
        RekallAgeRenderTargetDescriptor => Valid(),
        _ => Invalid(new RekallAgeGraphicsDiagnostic("REKALL_GPU_DESCRIPTOR_UNKNOWN", $"Unsupported descriptor {descriptor.GetType().Name}."))
    };

    private RekallAgeGraphicsResourceHandle Add(RekallAgeGraphicsResourceKind kind, object native, string? label, object descriptor, bool ownsNative, ulong estimatedBytes = 0)
    {
        var slot = _nextSlot++;
        _resources.Add(slot, new(kind, native, label, descriptor, ownsNative, estimatedBytes));
        return new(DeviceId, kind, slot, 1);
    }

    private T Native<T>(RekallAgeGraphicsResourceHandle handle, RekallAgeGraphicsResourceKind expected) where T : class
    {
        if (!TryEntry(handle, out var entry, out var diagnostic) || entry!.Kind != expected || entry.Native is not T value)
            throw new InvalidOperationException(diagnostic?.Message ?? $"Resource {handle} is not a native {expected}.");
        return value;
    }

    private TextureEntry Texture(RekallAgeGraphicsResourceHandle handle)
    {
        if (!TryEntry(handle, out var entry, out var diagnostic) || entry!.Kind != RekallAgeGraphicsResourceKind.Texture || entry.Native is not TextureEntry value)
            throw new InvalidOperationException(diagnostic?.Message ?? $"Resource {handle} is not a native texture.");
        return value;
    }

    private ShaderEntry Shader(RekallAgeGraphicsResourceHandle handle, RekallAgeShaderStage expected)
    {
        if (!TryEntry(handle, out var entry, out var diagnostic) || entry!.Kind != RekallAgeGraphicsResourceKind.ShaderModule
            || entry.Native is not ShaderEntry value || value.Descriptor.Stage != expected)
            throw new InvalidOperationException(diagnostic?.Message ?? $"Resource {handle} is not a {expected} shader module.");
        return value;
    }

    private Pipeline Pipeline(RekallAgeGraphicsResourceHandle handle, RekallAgeGraphicsResourceKind expected)
    {
        if (!TryEntry(handle, out var entry, out var diagnostic) || entry!.Kind != expected)
            throw new InvalidOperationException(diagnostic?.Message ?? $"Resource {handle} is not a {expected}.");
        return entry.Native switch
        {
            Pipeline pipeline => pipeline,
            PipelineEntry pipeline => pipeline.Pipeline,
            _ => throw new InvalidOperationException($"Resource {handle} does not contain a native pipeline.")
        };
    }

    private BindableResource ToBindableResource(RekallAgeBindingSetEntry binding)
    {
        if (!TryEntry(binding.Resource, out var entry, out var diagnostic)) throw new InvalidOperationException(diagnostic!.Message);
        return entry!.Native switch
        {
            DeviceBuffer buffer when binding.Offset > 0 || binding.SizeBytes > 0 => new DeviceBufferRange(buffer, (uint)binding.Offset, (uint)(binding.SizeBytes == 0 ? buffer.SizeInBytes - binding.Offset : binding.SizeBytes)),
            DeviceBuffer buffer => buffer,
            TextureEntry texture => texture.View,
            BindableResource bindable => bindable,
            _ => throw new InvalidOperationException($"Resource {binding.Resource} cannot be bound to a shader layout.")
        };
    }

    private bool TryEntry(RekallAgeGraphicsResourceHandle handle, out Entry? entry, out RekallAgeGraphicsDiagnostic? diagnostic)
    {
        entry = null;
        if (!handle.BelongsTo(DeviceId) || !_resources.TryGetValue(handle.Slot, out entry))
        {
            diagnostic = new("REKALL_GPU_RESOURCE_INVALID", $"Resource handle {handle} is invalid for this device.");
            return false;
        }
        diagnostic = null;
        return true;
    }

    private static string? Label(object descriptor) => descriptor.GetType().GetProperty("Label")?.GetValue(descriptor) as string;
    private static RekallAgeGraphicsValidationResult Valid() => new([]);
    private static RekallAgeGraphicsValidationResult Invalid(params RekallAgeGraphicsDiagnostic[] diagnostics) => new(diagnostics);
    private static ulong EstimateTextureBytes(RekallAgeTextureDescriptor descriptor) => RekallAgeTextureLayout.TotalBytes(descriptor);
    private static ulong BytesPerPixel(RekallAgeTextureFormat format) => RekallAgeTextureLayout.BytesPerPixel(format);
    private static ulong SaturatingAdd(ulong left, ulong right) => ulong.MaxValue - left < right ? ulong.MaxValue : left + right;

    private static BufferUsage Map(RekallAgeBufferUsage usage, RekallAgeMemoryAccess access)
    {
        var result = (BufferUsage)0;
        if (usage.HasFlag(RekallAgeBufferUsage.Vertex)) result |= BufferUsage.VertexBuffer;
        if (usage.HasFlag(RekallAgeBufferUsage.Index)) result |= BufferUsage.IndexBuffer;
        if (usage.HasFlag(RekallAgeBufferUsage.Uniform)) result |= BufferUsage.UniformBuffer;
        if (usage.HasFlag(RekallAgeBufferUsage.Indirect)) result |= BufferUsage.IndirectBuffer;
        if (access != RekallAgeMemoryAccess.DeviceLocal) result |= BufferUsage.Dynamic;
        return result == 0 ? BufferUsage.Dynamic : result;
    }
    private static TextureUsage Map(RekallAgeTextureUsage usage, RekallAgeTextureDimension dimension)
    {
        var result = (TextureUsage)0;
        if (usage.HasFlag(RekallAgeTextureUsage.Sampled)) result |= TextureUsage.Sampled;
        if (usage.HasFlag(RekallAgeTextureUsage.ColorAttachment) || usage.HasFlag(RekallAgeTextureUsage.Present)) result |= TextureUsage.RenderTarget;
        if (usage.HasFlag(RekallAgeTextureUsage.DepthStencilAttachment)) result |= TextureUsage.DepthStencil;
        if (usage.HasFlag(RekallAgeTextureUsage.Storage)) result |= TextureUsage.Storage;
        if (dimension == RekallAgeTextureDimension.Cube) result |= TextureUsage.Cubemap;
        return result;
    }
    private static TextureType Map(RekallAgeTextureDimension value) => value switch { RekallAgeTextureDimension.Texture1D => TextureType.Texture1D, RekallAgeTextureDimension.Texture3D => TextureType.Texture3D, _ => TextureType.Texture2D };
    private static PixelFormat Map(RekallAgeTextureFormat value) => value switch
    {
        RekallAgeTextureFormat.R8Unorm => PixelFormat.R8_UNorm,
        RekallAgeTextureFormat.Rg8Unorm => PixelFormat.R8_G8_UNorm,
        RekallAgeTextureFormat.Rgba8Unorm => PixelFormat.R8_G8_B8_A8_UNorm,
        RekallAgeTextureFormat.Rgba8UnormSrgb => PixelFormat.R8_G8_B8_A8_UNorm_SRgb,
        RekallAgeTextureFormat.Bgra8Unorm => PixelFormat.B8_G8_R8_A8_UNorm,
        RekallAgeTextureFormat.Bgra8UnormSrgb => PixelFormat.B8_G8_R8_A8_UNorm_SRgb,
        RekallAgeTextureFormat.Rgba16Float => PixelFormat.R16_G16_B16_A16_Float,
        RekallAgeTextureFormat.R32Float => PixelFormat.R32_Float,
        RekallAgeTextureFormat.Depth24Stencil8 => PixelFormat.D24_UNorm_S8_UInt,
        _ => PixelFormat.D32_Float_S8_UInt
    };
    private static RekallAgeTextureFormat Map(PixelFormat value) => value switch
    {
        PixelFormat.R8_UNorm => RekallAgeTextureFormat.R8Unorm,
        PixelFormat.R8_G8_UNorm => RekallAgeTextureFormat.Rg8Unorm,
        PixelFormat.R8_G8_B8_A8_UNorm => RekallAgeTextureFormat.Rgba8Unorm,
        PixelFormat.R8_G8_B8_A8_UNorm_SRgb => RekallAgeTextureFormat.Rgba8UnormSrgb,
        PixelFormat.B8_G8_R8_A8_UNorm => RekallAgeTextureFormat.Bgra8Unorm,
        PixelFormat.B8_G8_R8_A8_UNorm_SRgb => RekallAgeTextureFormat.Bgra8UnormSrgb,
        PixelFormat.R16_G16_B16_A16_Float => RekallAgeTextureFormat.Rgba16Float,
        PixelFormat.R32_Float => RekallAgeTextureFormat.R32Float,
        PixelFormat.D24_UNorm_S8_UInt => RekallAgeTextureFormat.Depth24Stencil8,
        _ => RekallAgeTextureFormat.Depth32Float
    };
    private static TextureSampleCount MapSampleCount(int value) => value switch { 2 => TextureSampleCount.Count2, 4 => TextureSampleCount.Count4, 8 => TextureSampleCount.Count8, 16 => TextureSampleCount.Count16, 32 => TextureSampleCount.Count32, _ => TextureSampleCount.Count1 };
    private static SamplerAddressMode Map(RekallAgeAddressMode value) => value switch { RekallAgeAddressMode.ClampToEdge => SamplerAddressMode.Clamp, RekallAgeAddressMode.MirrorRepeat => SamplerAddressMode.Mirror, _ => SamplerAddressMode.Wrap };
    private static SamplerFilter Map(RekallAgeSamplerDescriptor value) => value.MaximumAnisotropy > 1 ? SamplerFilter.Anisotropic : (value.MinFilter, value.MagFilter, value.MipmapFilter) switch
    {
        (RekallAgeFilter.Nearest, RekallAgeFilter.Nearest, RekallAgeMipmapFilter.Nearest) => SamplerFilter.MinPoint_MagPoint_MipPoint,
        (RekallAgeFilter.Nearest, RekallAgeFilter.Nearest, _) => SamplerFilter.MinPoint_MagPoint_MipLinear,
        (RekallAgeFilter.Nearest, _, RekallAgeMipmapFilter.Nearest) => SamplerFilter.MinPoint_MagLinear_MipPoint,
        (RekallAgeFilter.Nearest, _, _) => SamplerFilter.MinPoint_MagLinear_MipLinear,
        (RekallAgeFilter.Linear, RekallAgeFilter.Nearest, RekallAgeMipmapFilter.Nearest) => SamplerFilter.MinLinear_MagPoint_MipPoint,
        (RekallAgeFilter.Linear, RekallAgeFilter.Nearest, _) => SamplerFilter.MinLinear_MagPoint_MipLinear,
        (RekallAgeFilter.Linear, _, RekallAgeMipmapFilter.Nearest) => SamplerFilter.MinLinear_MagLinear_MipPoint,
        _ => SamplerFilter.MinLinear_MagLinear_MipLinear
    };
    private static ShaderStages Map(RekallAgeShaderStage value)
    {
        var result = ShaderStages.None;
        if (value.HasFlag(RekallAgeShaderStage.Vertex)) result |= ShaderStages.Vertex;
        if (value.HasFlag(RekallAgeShaderStage.Fragment)) result |= ShaderStages.Fragment;
        if (value.HasFlag(RekallAgeShaderStage.Compute)) result |= ShaderStages.Compute;
        return result;
    }
    private static ResourceKind Map(RekallAgeBindingType value) => value switch
    {
        RekallAgeBindingType.UniformBuffer => ResourceKind.UniformBuffer,
        RekallAgeBindingType.ReadOnlyStorageBuffer => ResourceKind.StructuredBufferReadOnly,
        RekallAgeBindingType.StorageBuffer => ResourceKind.StructuredBufferReadWrite,
        RekallAgeBindingType.Sampler or RekallAgeBindingType.ComparisonSampler => ResourceKind.Sampler,
        RekallAgeBindingType.SampledTexture => ResourceKind.TextureReadOnly,
        _ => ResourceKind.TextureReadWrite
    };
    private static VertexElementFormat Map(RekallAgeVertexFormat value) => value switch
    {
        RekallAgeVertexFormat.Float32 => VertexElementFormat.Float1,
        RekallAgeVertexFormat.Float32x2 => VertexElementFormat.Float2,
        RekallAgeVertexFormat.Float32x3 => VertexElementFormat.Float3,
        RekallAgeVertexFormat.Float32x4 => VertexElementFormat.Float4,
        RekallAgeVertexFormat.Uint32 => VertexElementFormat.UInt1,
        RekallAgeVertexFormat.Uint32x2 => VertexElementFormat.UInt2,
        RekallAgeVertexFormat.Uint32x3 => VertexElementFormat.UInt3,
        RekallAgeVertexFormat.Uint32x4 => VertexElementFormat.UInt4,
        RekallAgeVertexFormat.Sint32 => VertexElementFormat.Int1,
        RekallAgeVertexFormat.Sint32x2 => VertexElementFormat.Int2,
        RekallAgeVertexFormat.Sint32x3 => VertexElementFormat.Int3,
        _ => VertexElementFormat.Int4
    };
    private static PrimitiveTopology Map(RekallAgePrimitiveTopology value) => (PrimitiveTopology)(int)value;
    private static FaceCullMode Map(RekallAgeCullMode value) => value switch { RekallAgeCullMode.None => FaceCullMode.None, RekallAgeCullMode.Front => FaceCullMode.Front, _ => FaceCullMode.Back };
    private static FrontFace Map(RekallAgeFrontFace value) => value == RekallAgeFrontFace.Clockwise ? FrontFace.Clockwise : FrontFace.CounterClockwise;
    private static ComparisonKind Map(RekallAgeCompareOperation value) => value switch
    {
        RekallAgeCompareOperation.Never => ComparisonKind.Never,
        RekallAgeCompareOperation.Less => ComparisonKind.Less,
        RekallAgeCompareOperation.LessEqual => ComparisonKind.LessEqual,
        RekallAgeCompareOperation.Equal => ComparisonKind.Equal,
        RekallAgeCompareOperation.GreaterEqual => ComparisonKind.GreaterEqual,
        RekallAgeCompareOperation.Greater => ComparisonKind.Greater,
        RekallAgeCompareOperation.NotEqual => ComparisonKind.NotEqual,
        _ => ComparisonKind.Always
    };

    private sealed record Entry(RekallAgeGraphicsResourceKind Kind, object Native, string? Label, object Descriptor, bool OwnsNative, ulong EstimatedBytes);
    private sealed class TextureEntry(Texture texture, TextureView view, bool ownsTexture) : IDisposable
    {
        public Texture Texture { get; } = texture;
        public TextureView View { get; } = view;
        public void Dispose() { View.Dispose(); if (ownsTexture) Texture.Dispose(); }
    }
    private sealed record ShaderEntry(RekallAgeShaderModuleDescriptor Descriptor, Shader? Native) : IDisposable
    {
        public void Dispose() => Native?.Dispose();
    }
    private sealed record PipelineEntry(Pipeline Pipeline, IReadOnlyList<Shader> Shaders) : IDisposable
    {
        public void Dispose()
        {
            Pipeline.Dispose();
            foreach (var shader in Shaders) shader.Dispose();
        }
    }

    private sealed class Encoder(RekallAgeVeldridRenderingDevice device, string? label) : IRekallAgeGraphicsCommandEncoder
    {
        private readonly List<RekallAgeGraphicsCommand> _commands = [];
        private bool _renderPass;
        private bool _computePass;
        private bool _finished;

        public RekallAgeGraphicsValidationResult BeginRenderPass(RekallAgeRenderPassDescriptor descriptor) { if (_renderPass || _computePass) return Invalid(new RekallAgeGraphicsDiagnostic("REKALL_GPU_PASS_STATE_INVALID", "A pass is already active.")); _renderPass = true; _commands.Add(new RekallAgeBeginRenderPassCommand(descriptor)); return Valid(); }
        public RekallAgeGraphicsValidationResult SetRenderPipeline(RekallAgeGraphicsResourceHandle pipeline) => AddInPass(_renderPass, new RekallAgeSetRenderPipelineCommand(pipeline));
        public RekallAgeGraphicsValidationResult SetComputePipeline(RekallAgeGraphicsResourceHandle pipeline) => AddInPass(_computePass, new RekallAgeSetComputePipelineCommand(pipeline));
        public RekallAgeGraphicsValidationResult SetBindingSet(int index, RekallAgeGraphicsResourceHandle bindingSet) => AddInPass(_renderPass || _computePass, new RekallAgeSetBindingSetCommand(index, bindingSet));
        public RekallAgeGraphicsValidationResult SetVertexBuffer(int slot, RekallAgeGraphicsResourceHandle buffer, ulong offset = 0, ulong sizeBytes = 0) => AddInPass(_renderPass, new RekallAgeSetVertexBufferCommand(slot, buffer, offset, sizeBytes));
        public RekallAgeGraphicsValidationResult SetIndexBuffer(RekallAgeGraphicsResourceHandle buffer, RekallAgeIndexFormat format, ulong offset = 0, ulong sizeBytes = 0) => AddInPass(_renderPass, new RekallAgeSetIndexBufferCommand(buffer, format, offset, sizeBytes));
        public RekallAgeGraphicsValidationResult Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) => AddInPass(_renderPass, new RekallAgeDrawCommand(vertexCount, instanceCount, firstVertex, firstInstance));
        public RekallAgeGraphicsValidationResult DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0) => AddInPass(_renderPass, new RekallAgeDrawIndexedCommand(indexCount, instanceCount, firstIndex, baseVertex, firstInstance));
        public RekallAgeGraphicsValidationResult EndRenderPass() { if (!_renderPass) return Invalid(new RekallAgeGraphicsDiagnostic("REKALL_GPU_PASS_STATE_INVALID", "No render pass is active.")); _renderPass = false; _commands.Add(new RekallAgeEndRenderPassCommand()); return Valid(); }
        public RekallAgeGraphicsValidationResult BeginComputePass(string? passLabel = null) { if (_renderPass || _computePass) return Invalid(new RekallAgeGraphicsDiagnostic("REKALL_GPU_PASS_STATE_INVALID", "A pass is already active.")); _computePass = true; _commands.Add(new RekallAgeBeginComputePassCommand(passLabel)); return Valid(); }
        public RekallAgeGraphicsValidationResult Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1) => AddInPass(_computePass, new RekallAgeDispatchCommand(groupCountX, groupCountY, groupCountZ));
        public RekallAgeGraphicsValidationResult EndComputePass() { if (!_computePass) return Invalid(new RekallAgeGraphicsDiagnostic("REKALL_GPU_PASS_STATE_INVALID", "No compute pass is active.")); _computePass = false; _commands.Add(new RekallAgeEndComputePassCommand()); return Valid(); }
        public RekallAgeGraphicsValidationResult CopyBuffer(RekallAgeGraphicsResourceHandle source, ulong sourceOffset, RekallAgeGraphicsResourceHandle destination, ulong destinationOffset, ulong sizeBytes) => AddInPass(!_renderPass && !_computePass, new RekallAgeCopyBufferCommand(source, sourceOffset, destination, destinationOffset, sizeBytes));
        public RekallAgeGraphicsCommandBuffer Finish() { if (_finished || _renderPass || _computePass) throw new InvalidOperationException("Command encoder cannot finish while a pass is active or after it has finished."); _finished = true; return new(device.DeviceId, label, new ReadOnlyCollection<RekallAgeGraphicsCommand>(_commands.ToArray())); }
        public void Dispose() { }
        private RekallAgeGraphicsValidationResult AddInPass(bool valid, RekallAgeGraphicsCommand command) { if (!valid) return Invalid(new RekallAgeGraphicsDiagnostic("REKALL_GPU_PASS_STATE_INVALID", "Command is invalid in the current pass state.")); _commands.Add(command); return Valid(); }
    }
}
