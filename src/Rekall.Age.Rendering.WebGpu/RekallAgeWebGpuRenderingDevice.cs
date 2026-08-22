using System.Text.Json;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering.WebGpu;

public sealed class RekallAgeWebGpuRenderingDevice : IRekallAgeRenderingDevice
{
    private readonly IRekallAgeWebGpuBridge _bridge;
    private readonly RekallAgeInMemoryRenderingDevice _conformance;
    private IReadOnlyList<RekallAgeGraphicsDiagnostic>? _faultDiagnostics;
    private bool _disposed;

    public RekallAgeWebGpuRenderingDevice(
        IRekallAgeWebGpuBridge bridge,
        RekallAgeRenderingDeviceCapabilities capabilities)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _conformance = new RekallAgeInMemoryRenderingDevice(capabilities ?? throw new ArgumentNullException(nameof(capabilities)));
    }

    public Guid DeviceId => _conformance.DeviceId;

    public RekallAgeRenderingDeviceCapabilities Capabilities => _conformance.Capabilities;

    public RekallAgeGraphicsResourceCreationResult CreateBuffer(RekallAgeBufferDescriptor descriptor) =>
        Create(descriptor, _conformance.CreateBuffer, "buffer");

    public RekallAgeGraphicsResourceCreationResult CreateTexture(RekallAgeTextureDescriptor descriptor) =>
        Create(descriptor, _conformance.CreateTexture, "texture");

    public RekallAgeGraphicsResourceCreationResult CreateSampler(RekallAgeSamplerDescriptor descriptor) =>
        Create(descriptor, _conformance.CreateSampler, "sampler");

    public RekallAgeGraphicsResourceCreationResult CreateShaderModule(RekallAgeShaderModuleDescriptor descriptor)
    {
        if (!IsAvailable(out var diagnostics)) return CreationFailure(diagnostics);
        if (descriptor.Language != RekallAgeShaderSourceLanguage.Wgsl)
        {
            return CreationFailure([new(
                "REKALL_WEBGPU_SHADER_LANGUAGE_REQUIRED",
                "The WebGPU rendering device accepts WGSL shader modules only.",
                descriptor.Label)]);
        }

        return Create(descriptor, _conformance.CreateShaderModule, "shaderModule");
    }

    public RekallAgeGraphicsResourceCreationResult CreateBindingLayout(RekallAgeBindingLayoutDescriptor descriptor) =>
        Create(descriptor, _conformance.CreateBindingLayout, "bindingLayout");

    public RekallAgeGraphicsResourceCreationResult CreateBindingSet(RekallAgeBindingSetDescriptor descriptor) =>
        Create(descriptor, _conformance.CreateBindingSet, "bindingSet");

    public RekallAgeGraphicsResourceCreationResult CreateGraphicsPipeline(RekallAgeGraphicsPipelineDescriptor descriptor) =>
        Create(descriptor, _conformance.CreateGraphicsPipeline, "renderPipeline");

    public RekallAgeGraphicsResourceCreationResult CreateComputePipeline(RekallAgeComputePipelineDescriptor descriptor) =>
        Create(descriptor, _conformance.CreateComputePipeline, "computePipeline");

    public RekallAgeGraphicsResourceCreationResult CreateRenderTarget(RekallAgeRenderTargetDescriptor descriptor) =>
        Create(descriptor, _conformance.CreateRenderTarget, "renderTarget");

    public RekallAgeGraphicsValidationResult WriteBuffer(RekallAgeGraphicsResourceHandle buffer, ulong offset, ReadOnlyMemory<byte> data)
    {
        if (!IsAvailable(out var diagnostics)) return new(diagnostics);
        var validation = _conformance.WriteBuffer(buffer, offset, data);
        return validation.Valid
            ? Execute(new RekallAgeWebGpuWriteBufferPacket(RekallAgeWebGpuProtocol.Version, buffer, offset, Convert.ToBase64String(data.Span)))
            : validation;
    }

    public RekallAgeGraphicsValidationResult WriteTexture(RekallAgeGraphicsResourceHandle texture, ReadOnlyMemory<byte> data, int mipLevel = 0, int arrayLayer = 0)
    {
        if (!IsAvailable(out var diagnostics)) return new(diagnostics);
        var validation = _conformance.WriteTexture(texture, data, mipLevel, arrayLayer);
        return validation.Valid
            ? Execute(new RekallAgeWebGpuWriteTexturePacket(RekallAgeWebGpuProtocol.Version, texture, mipLevel, arrayLayer, Convert.ToBase64String(data.Span)))
            : validation;
    }

    public RekallAgeGraphicsValidationResult Destroy(RekallAgeGraphicsResourceHandle handle)
    {
        if (_disposed) return new([new("REKALL_WEBGPU_DEVICE_DISPOSED", "The WebGPU rendering device has been disposed.")]);
        var validation = _conformance.Destroy(handle);
        return validation.Valid
            ? Execute(new RekallAgeWebGpuDestroyPacket(RekallAgeWebGpuProtocol.Version, handle))
            : validation;
    }

    public IRekallAgeGraphicsCommandEncoder BeginCommandEncoder(string? label = null) =>
        IsAvailable(out _)
            ? new RekallAgeWebGpuCommandEncoder(this, _conformance.BeginCommandEncoder(label), label)
            : new RekallAgeWebGpuCommandEncoder(this, null, label);

    public RekallAgeGraphicsValidationResult Submit(RekallAgeGraphicsCommandBuffer commandBuffer)
    {
        if (!IsAvailable(out var diagnostics)) return new(diagnostics);
        var validation = _conformance.Submit(commandBuffer);
        return validation.Valid
            ? Execute(new RekallAgeWebGpuSubmitPacket(
                RekallAgeWebGpuProtocol.Version,
                commandBuffer.Label,
                commandBuffer.Commands.Select(ToPacket).ToArray()))
            : validation;
    }

    public RekallAgeGraphicsResourceCreationResult ImportCanvasOutput(
        int width,
        int height,
        RekallAgeTextureFormat format,
        string? label = "engine.output")
    {
        if (!IsAvailable(out var diagnostics)) return CreationFailure(diagnostics);
        var texture = _conformance.CreateTexture(new(
            RekallAgeTextureDimension.Texture2D, width, height, 1, 1, 1, 1, format,
            RekallAgeTextureUsage.ColorAttachment | RekallAgeTextureUsage.Present, label));
        if (!texture.Created) return texture;
        var target = _conformance.CreateRenderTarget(new([new(texture.Handle)], null, width, height, label));
        if (!target.Created)
        {
            _conformance.Destroy(texture.Handle);
            return target;
        }

        var execution = Execute(new RekallAgeWebGpuImportCanvasOutputPacket(
            RekallAgeWebGpuProtocol.Version, texture.Handle, target.Handle, width, height, format, label));
        if (execution.Valid) return target;
        _conformance.Destroy(target.Handle);
        _conformance.Destroy(texture.Handle);
        return CreationFailure(execution.Diagnostics);
    }

    public async ValueTask<RekallAgeGraphicsValidationResult> FlushAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAvailable(out var diagnostics)) return new(diagnostics);
        try
        {
            var result = await _bridge.FlushAsync(cancellationToken).ConfigureAwait(false);
            if (result.Succeeded) return new([]);
            Fault(result.Diagnostics);
            return new(_faultDiagnostics!);
        }
        catch (Exception exception)
        {
            Fault([new("REKALL_WEBGPU_BRIDGE_FLUSH_FAILED", "The WebGPU bridge failed while flushing device work.", exception.GetType().Name)]);
            return new(_faultDiagnostics!);
        }
    }

    public IReadOnlyList<RekallAgeGraphicsResourceInspection> InspectResources() =>
        _disposed ? [] : _conformance.InspectResources();

    public void Dispose()
    {
        if (_disposed) return;
        foreach (var resource in _conformance.InspectResources())
        {
            TryExecute(new RekallAgeWebGpuDestroyPacket(RekallAgeWebGpuProtocol.Version, resource.Handle));
            _conformance.Destroy(resource.Handle);
        }
        _conformance.Dispose();
        _disposed = true;
    }

    internal RekallAgeGraphicsValidationResult EncoderAvailable() =>
        IsAvailable(out var diagnostics) ? new([]) : new(diagnostics);

    private RekallAgeGraphicsResourceCreationResult Create<T>(T descriptor, Func<T, RekallAgeGraphicsResourceCreationResult> create, string resourceType)
        where T : class
    {
        if (!IsAvailable(out var diagnostics)) return CreationFailure(diagnostics);
        var created = create(descriptor);
        if (!created.Created) return created;
        var execution = Execute(new RekallAgeWebGpuCreatePacket(
            RekallAgeWebGpuProtocol.Version,
            resourceType,
            created.Handle,
            RekallAgeWebGpuProtocol.ToJsonElement(descriptor)));
        if (execution.Valid) return created;
        _conformance.Destroy(created.Handle);
        return CreationFailure(execution.Diagnostics);
    }

    private RekallAgeGraphicsValidationResult Execute<T>(T packet) where T : IRekallAgeWebGpuPacket
    {
        try
        {
            var result = _bridge.Execute(RekallAgeWebGpuProtocol.Serialize(packet));
            return result.Succeeded
                ? new([])
                : new(result.Diagnostics.Count == 0
                    ? [new("REKALL_WEBGPU_BRIDGE_REJECTED", "The WebGPU bridge rejected an AGE rendering operation.")]
                    : result.Diagnostics);
        }
        catch (RekallAgeWebGpuProtocolException exception)
        {
            return new([exception.Diagnostic]);
        }
        catch (Exception exception)
        {
            return new([new("REKALL_WEBGPU_BRIDGE_EXECUTION_FAILED", "The WebGPU bridge could not execute an AGE rendering operation.", exception.GetType().Name)]);
        }
    }

    private void TryExecute<T>(T packet) where T : IRekallAgeWebGpuPacket
    {
        _ = Execute(packet);
    }

    private bool IsAvailable(out IReadOnlyList<RekallAgeGraphicsDiagnostic> diagnostics)
    {
        if (_disposed)
        {
            diagnostics = [new("REKALL_WEBGPU_DEVICE_DISPOSED", "The WebGPU rendering device has been disposed.")];
            return false;
        }
        if (_faultDiagnostics is not null)
        {
            diagnostics = [new("REKALL_WEBGPU_DEVICE_FAULTED", "The WebGPU rendering device is faulted and cannot accept new work.")];
            return false;
        }
        diagnostics = [];
        return true;
    }

    private void Fault(IReadOnlyList<RekallAgeGraphicsDiagnostic> diagnostics) =>
        _faultDiagnostics = diagnostics.Count == 0
            ? [new("REKALL_WEBGPU_DEVICE_FAULTED", "The WebGPU rendering device faulted without a backend diagnostic.")]
            : diagnostics.Take(64).ToArray();

    private static RekallAgeGraphicsResourceCreationResult CreationFailure(IReadOnlyList<RekallAgeGraphicsDiagnostic> diagnostics) => new(default, diagnostics);

    private static RekallAgeWebGpuCommandPacket ToPacket(RekallAgeGraphicsCommand command) => new(
        command switch
        {
            RekallAgeCopyBufferCommand => "copyBuffer",
            RekallAgeBeginRenderPassCommand => "beginRenderPass",
            RekallAgeSetRenderPipelineCommand => "setRenderPipeline",
            RekallAgeSetComputePipelineCommand => "setComputePipeline",
            RekallAgeSetBindingSetCommand => "setBindingSet",
            RekallAgeSetVertexBufferCommand => "setVertexBuffer",
            RekallAgeSetIndexBufferCommand => "setIndexBuffer",
            RekallAgeDrawCommand => "draw",
            RekallAgeDrawIndexedCommand => "drawIndexed",
            RekallAgeDrawIndirectCommand => "drawIndirect",
            RekallAgeDrawIndexedIndirectCommand => "drawIndexedIndirect",
            RekallAgeEndRenderPassCommand => "endRenderPass",
            RekallAgeBeginComputePassCommand => "beginComputePass",
            RekallAgeDispatchCommand => "dispatch",
            RekallAgeDispatchIndirectCommand => "dispatchIndirect",
            RekallAgeEndComputePassCommand => "endComputePass",
            _ => throw new RekallAgeWebGpuProtocolException(new("REKALL_WEBGPU_PROTOCOL_COMMAND_KIND_INVALID", "WebGPU command packets must use a known command kind."))
        },
        RekallAgeWebGpuProtocol.ToJsonElement(command));
}
