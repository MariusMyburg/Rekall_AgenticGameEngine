using System.Collections.ObjectModel;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Rendering;

public sealed class RekallAgeCompiledGpuWorkload : IDisposable
{
    private readonly IRekallAgeRenderingDevice? _device;
    private readonly IReadOnlyList<RekallAgeGraphicsResourceHandle> _ownedResources;
    private bool _disposed;

    internal RekallAgeCompiledGpuWorkload(
        string workloadId,
        IRekallAgeRenderingDevice? device,
        IReadOnlyDictionary<string, RekallAgeGraphicsResourceHandle> resources,
        IReadOnlyList<RekallAgeGraphicsResourceHandle> ownedResources,
        RekallAgeGraphicsCommandBuffer? commandBuffer,
        IReadOnlyList<RekallAgeGraphicsDiagnostic> diagnostics)
    {
        WorkloadId = workloadId;
        _device = device;
        Resources = resources;
        _ownedResources = ownedResources;
        CommandBuffer = commandBuffer;
        Diagnostics = diagnostics;
    }

    public string WorkloadId { get; }
    public IReadOnlyDictionary<string, RekallAgeGraphicsResourceHandle> Resources { get; }
    public RekallAgeGraphicsCommandBuffer? CommandBuffer { get; }
    public IReadOnlyList<RekallAgeGraphicsDiagnostic> Diagnostics { get; }
    public bool Valid => CommandBuffer is not null && Diagnostics.Count == 0;

    public void Dispose()
    {
        if (_disposed || _device is null) return;
        foreach (var handle in _ownedResources.Reverse()) _device.Destroy(handle);
        _disposed = true;
    }
}

public sealed class RekallAgeRuntimeGpuWorkloadCompiler
{
    public const int MaximumResources = 256;
    public const int MaximumCommands = 4_096;
    public const ulong MaximumEstimatedBytes = 512UL * 1024 * 1024;
    public const int MaximumAggregateShaderBytes = 4 * 1024 * 1024;

    public RekallAgeCompiledGpuWorkload Compile(
        RekallAgeRuntimeGpuWorkload workload,
        IRekallAgeRenderingDevice device)
    {
        ArgumentNullException.ThrowIfNull(workload);
        ArgumentNullException.ThrowIfNull(device);
        var diagnostics = Preflight(workload);
        if (diagnostics.Count > 0) return Invalid(workload.Id, diagnostics);

        var resources = new Dictionary<string, RekallAgeGraphicsResourceHandle>(StringComparer.Ordinal);
        var owned = new List<RekallAgeGraphicsResourceHandle>();
        foreach (var buffer in workload.Buffers)
        {
            if (!Add(buffer.Id, device.CreateBuffer(new(
                buffer.SizeBytes,
                Map(buffer.Usage),
                MapMemoryAccess(buffer.MemoryAccess),
                buffer.Id)))) return Rollback(workload.Id, device, owned, diagnostics);
        }
        foreach (var texture in workload.Textures)
        {
            if (!Add(texture.Id, device.CreateTexture(new(
                Map(texture.Dimension), texture.Width, texture.Height, texture.Depth,
                texture.MipLevels, texture.ArrayLayers, texture.SampleCount,
                MapFormat(texture.Format), Map(texture.Usage), texture.Id))))
                return Rollback(workload.Id, device, owned, diagnostics);
        }
        foreach (var sampler in workload.Samplers)
        {
            if (!Add(sampler.Id, device.CreateSampler(new(
                MapFilter(sampler.MinFilter), MapFilter(sampler.MagFilter), MapMipmapFilter(sampler.MipmapFilter),
                MapAddress(sampler.AddressU), MapAddress(sampler.AddressV), MapAddress(sampler.AddressW),
                MaximumAnisotropy: sampler.MaximumAnisotropy, Label: sampler.Id))))
                return Rollback(workload.Id, device, owned, diagnostics);
        }
        foreach (var shader in workload.Shaders)
        {
            if (!Add(shader.Id, device.CreateShaderModule(new(
                Map(shader.Stage),
                Map(shader.Language),
                shader.Source,
                shader.EntryPoint,
                shader.Id)))) return Rollback(workload.Id, device, owned, diagnostics);
        }
        foreach (var layout in workload.BindingLayouts)
        {
            if (!Add(layout.Id, device.CreateBindingLayout(new(
                layout.Entries.Select(entry => new RekallAgeBindingLayoutEntry(
                    entry.Binding, Map(entry.Type), Map(entry.Visibility), entry.MinimumBindingSize)).ToArray(),
                layout.Id)))) return Rollback(workload.Id, device, owned, diagnostics);
        }
        foreach (var set in workload.BindingSets)
        {
            if (!Add(set.Id, device.CreateBindingSet(new(
                resources[set.Layout],
                set.Bindings.Select(binding => new RekallAgeBindingSetEntry(
                    binding.Binding, resources[binding.Resource], binding.Offset, binding.SizeBytes)).ToArray(),
                set.Id)))) return Rollback(workload.Id, device, owned, diagnostics);
        }
        foreach (var pipeline in workload.Pipelines)
        {
            var layouts = pipeline.BindingLayouts.Select(id => resources[id]).ToArray();
            var result = pipeline.Kind == RekallAgeRuntimeGpuPipelineKind.Compute
                ? device.CreateComputePipeline(new(resources[pipeline.ComputeShader!], layouts, pipeline.Id))
                : device.CreateGraphicsPipeline(new(
                    resources[pipeline.VertexShader!], resources[pipeline.FragmentShader!], layouts,
                    pipeline.ColorFormats.Select(format => new RekallAgeColorTargetDescriptor(MapFormat(format))).ToArray(),
                    string.IsNullOrWhiteSpace(pipeline.DepthStencilFormat)
                        ? null
                        : new RekallAgeDepthStencilDescriptor(MapFormat(pipeline.DepthStencilFormat)),
                    MapTopology(pipeline.PrimitiveTopology), MapCull(pipeline.CullMode), Label: pipeline.Id));
            if (!Add(pipeline.Id, result)) return Rollback(workload.Id, device, owned, diagnostics);
        }
        foreach (var target in workload.RenderTargets)
        {
            if (!Add(target.Id, device.CreateRenderTarget(new(
                target.ColorAttachments.Select(id => new RekallAgeRenderTargetAttachment(resources[id])).ToArray(),
                string.IsNullOrWhiteSpace(target.DepthStencilAttachment)
                    ? null
                    : new RekallAgeRenderTargetAttachment(resources[target.DepthStencilAttachment]),
                target.Width, target.Height, target.Id))))
                return Rollback(workload.Id, device, owned, diagnostics);
        }

        using var encoder = device.BeginCommandEncoder(workload.Id);
        foreach (var command in workload.Commands)
        {
            var validation = command.Kind switch
            {
                RekallAgeRuntimeGpuCommandKind.CopyBuffer => encoder.CopyBuffer(
                    resources[command.Source!], command.SourceOffset,
                    resources[command.Destination!], command.DestinationOffset, command.SizeBytes),
                RekallAgeRuntimeGpuCommandKind.BeginRenderPass => encoder.BeginRenderPass(new(
                    resources[command.Resource!],
                    command.ClearColors.Select(color => new RekallAgeColorClearValue(color.Red, color.Green, color.Blue, color.Alpha)).ToArray(),
                    command.ClearDepth,
                    Label: command.Label)),
                RekallAgeRuntimeGpuCommandKind.SetRenderPipeline => encoder.SetRenderPipeline(resources[command.Resource!]),
                RekallAgeRuntimeGpuCommandKind.SetBindingSet => encoder.SetBindingSet(command.BindingSetIndex, resources[command.Resource!]),
                RekallAgeRuntimeGpuCommandKind.SetVertexBuffer => encoder.SetVertexBuffer(
                    command.Slot, resources[command.Resource!], command.SourceOffset, command.SizeBytes),
                RekallAgeRuntimeGpuCommandKind.SetIndexBuffer => encoder.SetIndexBuffer(
                    resources[command.Resource!], Map(command.IndexFormat), command.SourceOffset, command.SizeBytes),
                RekallAgeRuntimeGpuCommandKind.Draw => encoder.Draw(
                    command.VertexCount, command.InstanceCount, command.FirstVertex, command.FirstInstance),
                RekallAgeRuntimeGpuCommandKind.DrawIndexed => encoder.DrawIndexed(
                    command.IndexCount, command.InstanceCount, command.FirstIndex, command.BaseVertex, command.FirstInstance),
                RekallAgeRuntimeGpuCommandKind.EndRenderPass => encoder.EndRenderPass(),
                RekallAgeRuntimeGpuCommandKind.BeginComputePass => encoder.BeginComputePass(command.Label),
                RekallAgeRuntimeGpuCommandKind.SetComputePipeline => encoder.SetComputePipeline(resources[command.Resource!]),
                RekallAgeRuntimeGpuCommandKind.Dispatch => encoder.Dispatch(command.GroupCountX, command.GroupCountY, command.GroupCountZ),
                RekallAgeRuntimeGpuCommandKind.EndComputePass => encoder.EndComputePass(),
                _ => new RekallAgeGraphicsValidationResult([
                    new("REKALL_GPU_WORKLOAD_NOT_IMPLEMENTED", $"Command {command.Kind} requires the next compiler stage.")])
            };
            if (!validation.Valid)
            {
                diagnostics.AddRange(validation.Diagnostics);
                return Rollback(workload.Id, device, owned, diagnostics);
            }
        }
        RekallAgeGraphicsCommandBuffer commandBuffer;
        try
        {
            commandBuffer = encoder.Finish();
        }
        catch (InvalidOperationException exception)
        {
            diagnostics.Add(new("REKALL_GPU_PASS_STATE_INVALID", exception.Message, workload.Id));
            return Rollback(workload.Id, device, owned, diagnostics);
        }
        return new(
            workload.Id,
            device,
            new ReadOnlyDictionary<string, RekallAgeGraphicsResourceHandle>(resources),
            owned.ToArray(),
            commandBuffer,
            []);

        bool Add(string id, RekallAgeGraphicsResourceCreationResult result)
        {
            if (!result.Created)
            {
                diagnostics.AddRange(result.Diagnostics);
                return false;
            }
            resources.Add(id, result.Handle);
            owned.Add(result.Handle);
            return true;
        }
    }

    private static List<RekallAgeGraphicsDiagnostic> Preflight(RekallAgeRuntimeGpuWorkload workload)
    {
        var diagnostics = new List<RekallAgeGraphicsDiagnostic>();
        var buffers = workload.Buffers ?? [];
        var textures = workload.Textures ?? [];
        var samplers = workload.Samplers ?? [];
        var shaders = workload.Shaders ?? [];
        var layouts = workload.BindingLayouts ?? [];
        var sets = workload.BindingSets ?? [];
        var pipelines = workload.Pipelines ?? [];
        var targets = workload.RenderTargets ?? [];
        var commands = workload.Commands ?? [];
        if (workload.Buffers is null || workload.Textures is null || workload.Samplers is null
            || workload.Shaders is null || workload.BindingLayouts is null || workload.BindingSets is null
            || workload.Pipelines is null || workload.RenderTargets is null || workload.Commands is null)
            diagnostics.Add(new("REKALL_GPU_WORKLOAD_SHAPE_INVALID", "Workload resource and command collections cannot be null.", workload.Id));
        if (string.IsNullOrWhiteSpace(workload.Id) || workload.Id.Length > 128)
            diagnostics.Add(new("REKALL_GPU_WORKLOAD_ID_INVALID", "Workload ID must be nonempty and at most 128 characters.", workload.Id));
        foreach (var layout in layouts.Where(layout => layout.Entries is null))
            diagnostics.Add(new("REKALL_GPU_WORKLOAD_SHAPE_INVALID", "Binding-layout entries cannot be null.", layout.Id));
        foreach (var set in sets.Where(set => set.Bindings is null))
            diagnostics.Add(new("REKALL_GPU_WORKLOAD_SHAPE_INVALID", "Binding-set bindings cannot be null.", set.Id));
        foreach (var pipeline in pipelines.Where(pipeline => pipeline.BindingLayouts is null || pipeline.ColorFormats is null))
            diagnostics.Add(new("REKALL_GPU_WORKLOAD_SHAPE_INVALID", "Pipeline layout and color-format collections cannot be null.", pipeline.Id));
        foreach (var target in targets.Where(target => target.ColorAttachments is null))
            diagnostics.Add(new("REKALL_GPU_WORKLOAD_SHAPE_INVALID", "Render-target color attachments cannot be null.", target.Id));
        foreach (var command in commands.Where(command => command.ClearColors is null))
            diagnostics.Add(new("REKALL_GPU_WORKLOAD_SHAPE_INVALID", "Command clear-color collections cannot be null.", workload.Id));
        if (buffers.Any(buffer => !string.IsNullOrWhiteSpace(buffer.InitialDataAsset)))
            diagnostics.Add(new("REKALL_GPU_WORKLOAD_NOT_IMPLEMENTED", "Initial buffer asset upload is reserved for the upload compiler stage.", workload.Id));
        if (textures.Any(texture => !string.IsNullOrWhiteSpace(texture.InitialDataAsset)))
            diagnostics.Add(new("REKALL_GPU_WORKLOAD_NOT_IMPLEMENTED", "Initial texture asset upload is reserved for the upload compiler stage.", workload.Id));
        var resourceIds = buffers.Select(item => item.Id)
            .Concat(textures.Select(item => item.Id))
            .Concat(samplers.Select(item => item.Id))
            .Concat(shaders.Select(item => item.Id))
            .Concat(layouts.Select(item => item.Id))
            .Concat(sets.Select(item => item.Id))
            .Concat(pipelines.Select(item => item.Id))
            .Concat(targets.Select(item => item.Id))
            .ToArray();
        if (resourceIds.Length > MaximumResources)
            diagnostics.Add(new("REKALL_GPU_WORKLOAD_RESOURCE_LIMIT", $"Workload resources cannot exceed {MaximumResources}.", workload.Id));
        if (commands.Count > MaximumCommands)
            diagnostics.Add(new("REKALL_GPU_WORKLOAD_COMMAND_LIMIT", $"Workload commands cannot exceed {MaximumCommands}.", workload.Id));
        var estimatedBytes = buffers.Aggregate(0UL, (total, buffer) => SaturatingAdd(total, buffer.SizeBytes));
        estimatedBytes = textures.Aggregate(estimatedBytes, (total, texture) => SaturatingAdd(total, EstimateTextureBytes(texture)));
        if (estimatedBytes > MaximumEstimatedBytes)
            diagnostics.Add(new("REKALL_GPU_WORKLOAD_MEMORY_LIMIT", $"Estimated workload allocation {estimatedBytes} exceeds {MaximumEstimatedBytes} bytes.", workload.Id));
        var shaderBytes = shaders.Aggregate(0UL, (total, shader) => SaturatingAdd(
            total, (ulong)System.Text.Encoding.UTF8.GetByteCount(shader.Source ?? string.Empty)));
        if (shaderBytes > MaximumAggregateShaderBytes)
            diagnostics.Add(new("REKALL_GPU_WORKLOAD_SHADER_LIMIT", $"Aggregate shader source exceeds {MaximumAggregateShaderBytes} bytes.", workload.Id));
        foreach (var id in resourceIds.Where(id => string.IsNullOrWhiteSpace(id) || id.Length > 128))
            diagnostics.Add(new("REKALL_GPU_WORKLOAD_ID_INVALID", "Resource IDs must be nonempty and at most 128 characters.", id));
        foreach (var duplicate in resourceIds.Where(id => !string.IsNullOrWhiteSpace(id)).GroupBy(id => id, StringComparer.Ordinal).Where(group => group.Count() > 1))
            diagnostics.Add(new("REKALL_GPU_WORKLOAD_ID_DUPLICATE", $"Resource ID '{duplicate.Key}' is declared more than once.", duplicate.Key));
        var ids = resourceIds.ToHashSet(StringComparer.Ordinal);
        foreach (var texture in textures.Where(texture => !TryMapFormat(texture.Format, out _)))
            diagnostics.Add(new("REKALL_GPU_WORKLOAD_FORMAT_UNSUPPORTED", $"Texture format '{texture.Format}' is unsupported.", texture.Id));
        foreach (var set in sets)
        {
            AddMissing(set.Id, set.Layout);
            foreach (var binding in set.Bindings ?? []) AddMissing(set.Id, binding.Resource);
        }
        foreach (var pipeline in pipelines)
        {
            var references = (pipeline.BindingLayouts ?? []).Append(pipeline.ComputeShader).Append(pipeline.VertexShader).Append(pipeline.FragmentShader);
            foreach (var reference in references) AddMissing(pipeline.Id, reference);
            if (pipeline.Kind == RekallAgeRuntimeGpuPipelineKind.Compute && string.IsNullOrWhiteSpace(pipeline.ComputeShader)
                || pipeline.Kind == RekallAgeRuntimeGpuPipelineKind.Render
                    && (string.IsNullOrWhiteSpace(pipeline.VertexShader) || string.IsNullOrWhiteSpace(pipeline.FragmentShader)))
                diagnostics.Add(new("REKALL_GPU_WORKLOAD_PIPELINE_INVALID", $"Pipeline '{pipeline.Id}' is missing required shader references.", pipeline.Id));
            foreach (var format in (pipeline.ColorFormats ?? []).Append(pipeline.DepthStencilFormat).Where(format => !string.IsNullOrWhiteSpace(format) && !TryMapFormat(format!, out _)))
                diagnostics.Add(new("REKALL_GPU_WORKLOAD_FORMAT_UNSUPPORTED", $"Pipeline format '{format}' is unsupported.", pipeline.Id));
        }
        foreach (var target in targets)
        {
            foreach (var reference in (target.ColorAttachments ?? []).Append(target.DepthStencilAttachment)) AddMissing(target.Id, reference);
        }
        foreach (var command in commands)
        {
            AddMissing(workload.Id, command.Resource);
            AddMissing(workload.Id, command.Source);
            AddMissing(workload.Id, command.Destination);
            var requiredResource = command.Kind is RekallAgeRuntimeGpuCommandKind.BeginRenderPass
                or RekallAgeRuntimeGpuCommandKind.SetRenderPipeline
                or RekallAgeRuntimeGpuCommandKind.SetComputePipeline
                or RekallAgeRuntimeGpuCommandKind.SetBindingSet
                or RekallAgeRuntimeGpuCommandKind.SetVertexBuffer
                or RekallAgeRuntimeGpuCommandKind.SetIndexBuffer;
            if (requiredResource && string.IsNullOrWhiteSpace(command.Resource)
                || command.Kind == RekallAgeRuntimeGpuCommandKind.CopyBuffer
                    && (string.IsNullOrWhiteSpace(command.Source) || string.IsNullOrWhiteSpace(command.Destination)))
                diagnostics.Add(new("REKALL_GPU_WORKLOAD_COMMAND_OPERAND_REQUIRED", $"Command {command.Kind} is missing a required resource operand.", workload.Id));
        }
        return diagnostics;

        void AddMissing(string owner, string? reference)
        {
            if (!string.IsNullOrWhiteSpace(reference) && !ids.Contains(reference))
                diagnostics.Add(new("REKALL_GPU_WORKLOAD_REFERENCE_MISSING", $"'{owner}' references missing resource '{reference}'.", owner));
        }
    }

    private static ulong EstimateTextureBytes(RekallAgeRuntimeGpuTexture texture)
    {
        if (texture.Width < 1 || texture.Height < 1 || texture.Depth < 1 || texture.ArrayLayers < 1) return 0;
        var bytesPerPixel = TryMapFormat(texture.Format, out var format) && format == RekallAgeTextureFormat.Rgba16Float ? 8UL : 4UL;
        try
        {
            return checked((ulong)texture.Width * (ulong)texture.Height * (ulong)texture.Depth
                * (ulong)texture.ArrayLayers * bytesPerPixel);
        }
        catch (OverflowException)
        {
            return ulong.MaxValue;
        }
    }

    private static ulong SaturatingAdd(ulong left, ulong right) => ulong.MaxValue - left < right ? ulong.MaxValue : left + right;

    private static RekallAgeCompiledGpuWorkload Rollback(
        string id,
        IRekallAgeRenderingDevice device,
        List<RekallAgeGraphicsResourceHandle> owned,
        List<RekallAgeGraphicsDiagnostic> diagnostics)
    {
        foreach (var handle in owned.AsEnumerable().Reverse()) device.Destroy(handle);
        return Invalid(id, diagnostics);
    }

    private static RekallAgeCompiledGpuWorkload Invalid(string id, IReadOnlyList<RekallAgeGraphicsDiagnostic> diagnostics) =>
        new(id, null, new ReadOnlyDictionary<string, RekallAgeGraphicsResourceHandle>(new Dictionary<string, RekallAgeGraphicsResourceHandle>()), [], null, diagnostics.ToArray());

    private static RekallAgeBufferUsage Map(RekallAgeRuntimeGpuBufferUsage usage) => (RekallAgeBufferUsage)(int)usage;
    private static RekallAgeTextureUsage Map(RekallAgeRuntimeGpuTextureUsage usage) => (RekallAgeTextureUsage)(int)usage;
    private static RekallAgeTextureDimension Map(RekallAgeRuntimeGpuTextureDimension dimension) => dimension switch
    {
        RekallAgeRuntimeGpuTextureDimension.Texture1D => RekallAgeTextureDimension.Texture1D,
        RekallAgeRuntimeGpuTextureDimension.Texture3D => RekallAgeTextureDimension.Texture3D,
        RekallAgeRuntimeGpuTextureDimension.Cube => RekallAgeTextureDimension.Cube,
        _ => RekallAgeTextureDimension.Texture2D
    };
    private static RekallAgeMemoryAccess MapMemoryAccess(string value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "upload" => RekallAgeMemoryAccess.Upload,
        "readback" => RekallAgeMemoryAccess.Readback,
        _ => RekallAgeMemoryAccess.DeviceLocal
    };
    private static RekallAgeShaderStage Map(RekallAgeRuntimeGpuShaderStage stage) => stage switch
    {
        RekallAgeRuntimeGpuShaderStage.Vertex => RekallAgeShaderStage.Vertex,
        RekallAgeRuntimeGpuShaderStage.Fragment => RekallAgeShaderStage.Fragment,
        _ => RekallAgeShaderStage.Compute
    };
    private static RekallAgeShaderSourceLanguage Map(RekallAgeRuntimeGpuShaderLanguage language) => language switch
    {
        RekallAgeRuntimeGpuShaderLanguage.SpirV => RekallAgeShaderSourceLanguage.SpirV,
        RekallAgeRuntimeGpuShaderLanguage.Wgsl => RekallAgeShaderSourceLanguage.Wgsl,
        _ => RekallAgeShaderSourceLanguage.Glsl
    };
    private static RekallAgeBindingType Map(RekallAgeRuntimeGpuBindingType type) => type switch
    {
        RekallAgeRuntimeGpuBindingType.ReadOnlyStorageBuffer => RekallAgeBindingType.ReadOnlyStorageBuffer,
        RekallAgeRuntimeGpuBindingType.StorageBuffer => RekallAgeBindingType.StorageBuffer,
        RekallAgeRuntimeGpuBindingType.Sampler => RekallAgeBindingType.Sampler,
        RekallAgeRuntimeGpuBindingType.ComparisonSampler => RekallAgeBindingType.ComparisonSampler,
        RekallAgeRuntimeGpuBindingType.SampledTexture => RekallAgeBindingType.SampledTexture,
        RekallAgeRuntimeGpuBindingType.ReadOnlyStorageTexture => RekallAgeBindingType.ReadOnlyStorageTexture,
        RekallAgeRuntimeGpuBindingType.StorageTexture => RekallAgeBindingType.StorageTexture,
        _ => RekallAgeBindingType.UniformBuffer
    };
    private static RekallAgeShaderStage Map(IReadOnlyList<RekallAgeRuntimeGpuShaderStage> stages) =>
        stages.Aggregate(RekallAgeShaderStage.None, (result, stage) => result | Map(stage));
    private static RekallAgeIndexFormat Map(RekallAgeRuntimeGpuIndexFormat format) =>
        format == RekallAgeRuntimeGpuIndexFormat.UInt16 ? RekallAgeIndexFormat.UInt16 : RekallAgeIndexFormat.UInt32;
    private static RekallAgeFilter MapFilter(string value) => Normalize(value) == "nearest" ? RekallAgeFilter.Nearest : RekallAgeFilter.Linear;
    private static RekallAgeMipmapFilter MapMipmapFilter(string value) => Normalize(value) == "nearest" ? RekallAgeMipmapFilter.Nearest : RekallAgeMipmapFilter.Linear;
    private static RekallAgeAddressMode MapAddress(string value) => Normalize(value) switch
    {
        "clamp-to-edge" or "clamp" => RekallAgeAddressMode.ClampToEdge,
        "mirror-repeat" or "mirrored-repeat" or "mirror" => RekallAgeAddressMode.MirrorRepeat,
        _ => RekallAgeAddressMode.Repeat
    };
    private static RekallAgePrimitiveTopology MapTopology(string value) => Normalize(value) switch
    {
        "triangle-strip" => RekallAgePrimitiveTopology.TriangleStrip,
        "line-list" => RekallAgePrimitiveTopology.LineList,
        "line-strip" => RekallAgePrimitiveTopology.LineStrip,
        "point-list" => RekallAgePrimitiveTopology.PointList,
        _ => RekallAgePrimitiveTopology.TriangleList
    };
    private static RekallAgeCullMode MapCull(string value) => Normalize(value) switch
    {
        "none" => RekallAgeCullMode.None,
        "front" => RekallAgeCullMode.Front,
        _ => RekallAgeCullMode.Back
    };
    private static RekallAgeTextureFormat MapFormat(string value) =>
        TryMapFormat(value, out var format) ? format : throw new InvalidOperationException($"Unsupported GPU format '{value}'.");
    private static bool TryMapFormat(string value, out RekallAgeTextureFormat format)
    {
        var mapped = Normalize(value) switch
        {
            "r8-unorm" => (RekallAgeTextureFormat?)RekallAgeTextureFormat.R8Unorm,
            "rg8-unorm" => RekallAgeTextureFormat.Rg8Unorm,
            "rgba8-unorm" => RekallAgeTextureFormat.Rgba8Unorm,
            "rgba8-unorm-srgb" => RekallAgeTextureFormat.Rgba8UnormSrgb,
            "bgra8-unorm" => RekallAgeTextureFormat.Bgra8Unorm,
            "bgra8-unorm-srgb" => RekallAgeTextureFormat.Bgra8UnormSrgb,
            "rgba16-float" => RekallAgeTextureFormat.Rgba16Float,
            "r32-float" => RekallAgeTextureFormat.R32Float,
            "depth24-stencil8" => RekallAgeTextureFormat.Depth24Stencil8,
            "depth32-float" => RekallAgeTextureFormat.Depth32Float,
            _ => null
        };
        format = mapped.GetValueOrDefault();
        return mapped.HasValue;
    }
    private static string Normalize(string value) => (value ?? string.Empty).Trim().ToLowerInvariant().Replace('_', '-');
}
