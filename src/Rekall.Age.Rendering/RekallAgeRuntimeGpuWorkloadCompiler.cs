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
        foreach (var shader in workload.Shaders)
        {
            if (!Add(shader.Id, device.CreateShaderModule(new(
                Map(shader.Stage),
                Map(shader.Language),
                shader.Source,
                shader.EntryPoint,
                shader.Id)))) return Rollback(workload.Id, device, owned, diagnostics);
        }
        foreach (var pipeline in workload.Pipelines)
        {
            if (pipeline.Kind != RekallAgeRuntimeGpuPipelineKind.Compute)
            {
                diagnostics.Add(new("REKALL_GPU_WORKLOAD_NOT_IMPLEMENTED", "Render pipelines require the render-graph compiler stage.", pipeline.Id));
                return Rollback(workload.Id, device, owned, diagnostics);
            }
            var result = device.CreateComputePipeline(new(
                resources[pipeline.ComputeShader!],
                pipeline.BindingLayouts.Select(id => resources[id]).ToArray(),
                pipeline.Id));
            if (!Add(pipeline.Id, result)) return Rollback(workload.Id, device, owned, diagnostics);
        }

        using var encoder = device.BeginCommandEncoder(workload.Id);
        foreach (var command in workload.Commands)
        {
            var validation = command.Kind switch
            {
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
        var shaders = workload.Shaders ?? [];
        var pipelines = workload.Pipelines ?? [];
        var commands = workload.Commands ?? [];
        if (workload.Buffers is null || workload.Shaders is null || workload.Pipelines is null || workload.Commands is null)
            diagnostics.Add(new("REKALL_GPU_WORKLOAD_SHAPE_INVALID", "Core workload resource and command collections cannot be null.", workload.Id));
        if ((workload.Textures?.Count ?? 0) > 0 || (workload.Samplers?.Count ?? 0) > 0
            || (workload.BindingLayouts?.Count ?? 0) > 0 || (workload.BindingSets?.Count ?? 0) > 0
            || (workload.RenderTargets?.Count ?? 0) > 0)
            diagnostics.Add(new("REKALL_GPU_WORKLOAD_NOT_IMPLEMENTED", "Texture, sampler, binding, and render-target compilation is reserved for the render-graph compiler stage.", workload.Id));
        if (buffers.Any(buffer => !string.IsNullOrWhiteSpace(buffer.InitialDataAsset)))
            diagnostics.Add(new("REKALL_GPU_WORKLOAD_NOT_IMPLEMENTED", "Initial buffer asset upload is reserved for the upload compiler stage.", workload.Id));
        if (pipelines.Any(pipeline => pipeline.Kind != RekallAgeRuntimeGpuPipelineKind.Compute))
            diagnostics.Add(new("REKALL_GPU_WORKLOAD_NOT_IMPLEMENTED", "Render pipelines require the render-graph compiler stage.", workload.Id));
        var supportedCommands = new HashSet<RekallAgeRuntimeGpuCommandKind>
        {
            RekallAgeRuntimeGpuCommandKind.BeginComputePass,
            RekallAgeRuntimeGpuCommandKind.SetComputePipeline,
            RekallAgeRuntimeGpuCommandKind.Dispatch,
            RekallAgeRuntimeGpuCommandKind.EndComputePass
        };
        if (commands.Any(command => !supportedCommands.Contains(command.Kind)))
            diagnostics.Add(new("REKALL_GPU_WORKLOAD_NOT_IMPLEMENTED", "The command stream contains an operation reserved for the transfer/render compiler stage.", workload.Id));
        var resourceIds = buffers.Select(item => item.Id)
            .Concat(shaders.Select(item => item.Id))
            .Concat(workload.BindingLayouts?.Select(item => item.Id) ?? [])
            .Concat(pipelines.Select(item => item.Id))
            .ToArray();
        if (resourceIds.Length > MaximumResources)
            diagnostics.Add(new("REKALL_GPU_WORKLOAD_RESOURCE_LIMIT", $"Workload resources cannot exceed {MaximumResources}.", workload.Id));
        if (commands.Count > MaximumCommands)
            diagnostics.Add(new("REKALL_GPU_WORKLOAD_COMMAND_LIMIT", $"Workload commands cannot exceed {MaximumCommands}.", workload.Id));
        foreach (var id in resourceIds.Where(id => string.IsNullOrWhiteSpace(id) || id.Length > 128))
            diagnostics.Add(new("REKALL_GPU_WORKLOAD_ID_INVALID", "Resource IDs must be nonempty and at most 128 characters.", id));
        foreach (var duplicate in resourceIds.Where(id => !string.IsNullOrWhiteSpace(id)).GroupBy(id => id, StringComparer.Ordinal).Where(group => group.Count() > 1))
            diagnostics.Add(new("REKALL_GPU_WORKLOAD_ID_DUPLICATE", $"Resource ID '{duplicate.Key}' is declared more than once.", duplicate.Key));
        var ids = resourceIds.ToHashSet(StringComparer.Ordinal);
        foreach (var pipeline in pipelines)
        {
            var references = (pipeline.BindingLayouts ?? []).Append(pipeline.ComputeShader).Append(pipeline.VertexShader).Append(pipeline.FragmentShader);
            foreach (var reference in references.Where(reference => !string.IsNullOrWhiteSpace(reference) && !ids.Contains(reference!)))
                diagnostics.Add(new("REKALL_GPU_WORKLOAD_REFERENCE_MISSING", $"Pipeline '{pipeline.Id}' references missing resource '{reference}'.", pipeline.Id));
        }
        foreach (var command in commands.Where(command => !string.IsNullOrWhiteSpace(command.Resource) && !ids.Contains(command.Resource!)))
            diagnostics.Add(new("REKALL_GPU_WORKLOAD_REFERENCE_MISSING", $"Command {command.Kind} references missing resource '{command.Resource}'.", workload.Id));
        return diagnostics;
    }

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
}
