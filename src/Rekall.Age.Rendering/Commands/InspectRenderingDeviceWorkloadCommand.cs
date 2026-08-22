using Rekall.Age.Core.Commands;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering.Commands;

public sealed record InspectRenderingDeviceWorkloadRequest(
    string Backend,
    IReadOnlyList<RekallAgeBufferDescriptor> Buffers,
    IReadOnlyList<RekallAgeTextureDescriptor> Textures,
    IReadOnlyList<RekallAgeSamplerDescriptor> Samplers,
    IReadOnlyList<RekallAgeShaderModuleDescriptor> Shaders,
    IReadOnlyList<RekallAgeBindingLayoutDescriptor> BindingLayouts,
    uint MaximumDispatchX = 0,
    uint MaximumDispatchY = 0,
    uint MaximumDispatchZ = 0);

public sealed record InspectRenderingDeviceWorkloadResult(
    RekallAgeRenderingDeviceCapabilities Capabilities,
    int ResourceDescriptorCount,
    ulong TotalBufferBytes,
    ulong EstimatedTextureBytes,
    IReadOnlyList<string> CommandSurface,
    IReadOnlyList<RekallAgeGraphicsDiagnostic> Diagnostics)
{
    public bool Valid => Diagnostics.Count == 0;
}

public sealed class InspectRenderingDeviceWorkloadCommand
    : IRekallAgeCommand<InspectRenderingDeviceWorkloadRequest, InspectRenderingDeviceWorkloadResult>
{
    private static readonly string[] Commands =
    [
        "copy-buffer", "begin-render-pass", "set-render-pipeline", "set-binding-set",
        "set-vertex-buffer", "set-index-buffer", "draw", "draw-indexed", "end-render-pass",
        "begin-compute-pass", "set-compute-pipeline", "dispatch", "end-compute-pass"
    ];

    public string Name => "rekall.render.device.inspect_workload";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Validates generic GPU resource and compute requirements against AGE's portable RenderingDevice contract and returns inspectable limits, diagnostics, and command capabilities.",
        typeof(InspectRenderingDeviceWorkloadRequest).FullName!,
        typeof(InspectRenderingDeviceWorkloadResult).FullName!);

    public ValueTask<RekallAgeCommandResult<InspectRenderingDeviceWorkloadResult>> ExecuteAsync(
        InspectRenderingDeviceWorkloadRequest request,
        RekallAgeCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var capabilities = RekallAgeRenderingDeviceCapabilities.DesktopBaseline(
            string.IsNullOrWhiteSpace(request.Backend) ? "portable" : request.Backend.Trim());
        var diagnostics = new List<RekallAgeGraphicsDiagnostic>();
        var buffers = request.Buffers ?? [];
        var textures = request.Textures ?? [];
        var samplers = request.Samplers ?? [];
        var shaders = request.Shaders ?? [];
        var layouts = request.BindingLayouts ?? [];

        foreach (var descriptor in buffers) diagnostics.AddRange(RekallAgeRenderingDeviceValidator.Validate(descriptor, capabilities).Diagnostics);
        foreach (var descriptor in textures) diagnostics.AddRange(RekallAgeRenderingDeviceValidator.Validate(descriptor, capabilities).Diagnostics);
        foreach (var descriptor in samplers) diagnostics.AddRange(RekallAgeRenderingDeviceValidator.Validate(descriptor, capabilities).Diagnostics);
        foreach (var descriptor in shaders) diagnostics.AddRange(RekallAgeRenderingDeviceValidator.Validate(descriptor, capabilities).Diagnostics);
        foreach (var descriptor in layouts) diagnostics.AddRange(RekallAgeRenderingDeviceValidator.Validate(descriptor, capabilities).Diagnostics);

        var dispatchRequested = request.MaximumDispatchX != 0 || request.MaximumDispatchY != 0 || request.MaximumDispatchZ != 0;
        if (dispatchRequested && (request.MaximumDispatchX == 0 || request.MaximumDispatchY == 0 || request.MaximumDispatchZ == 0
            || request.MaximumDispatchX > capabilities.MaximumComputeWorkgroupsPerDimension
            || request.MaximumDispatchY > capabilities.MaximumComputeWorkgroupsPerDimension
            || request.MaximumDispatchZ > capabilities.MaximumComputeWorkgroupsPerDimension))
        {
            diagnostics.Add(new(
                "REKALL_GPU_DISPATCH_RANGE_INVALID",
                $"Maximum dispatch counts must be from 1 through {capabilities.MaximumComputeWorkgroupsPerDimension} per dimension."));
        }

        var bufferBytes = buffers.Aggregate(0UL, (total, descriptor) => SaturatingAdd(total, descriptor.SizeBytes));
        var textureBytes = textures.Aggregate(0UL, (total, descriptor) => SaturatingAdd(total, EstimateTextureBytes(descriptor)));
        var result = new InspectRenderingDeviceWorkloadResult(
            capabilities,
            buffers.Count + textures.Count + samplers.Count + shaders.Count + layouts.Count,
            bufferBytes,
            textureBytes,
            Commands,
            diagnostics);
        return ValueTask.FromResult(RekallAgeCommandResult<InspectRenderingDeviceWorkloadResult>.Success(
            result,
            result.Valid
                ? $"Rendering workload is valid for the {capabilities.Backend} device contract."
                : $"Rendering workload has {diagnostics.Count} contract diagnostic(s)."));
    }

    private static ulong EstimateTextureBytes(RekallAgeTextureDescriptor descriptor)
    {
        if (descriptor.Width < 1 || descriptor.Height < 1 || descriptor.Depth < 1 || descriptor.ArrayLayers < 1)
        {
            return 0;
        }
        var bytesPerPixel = descriptor.Format == RekallAgeTextureFormat.Rgba16Float ? 8UL : 4UL;
        try
        {
            return checked((ulong)descriptor.Width * (ulong)descriptor.Height * (ulong)descriptor.Depth
                * (ulong)descriptor.ArrayLayers * bytesPerPixel);
        }
        catch (OverflowException)
        {
            return ulong.MaxValue;
        }
    }

    private static ulong SaturatingAdd(ulong left, ulong right) => ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
}
