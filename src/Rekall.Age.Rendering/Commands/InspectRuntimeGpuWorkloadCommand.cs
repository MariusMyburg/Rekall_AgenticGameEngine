using Rekall.Age.Core.Commands;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Rendering.Commands;

public sealed record InspectRuntimeGpuWorkloadRequest(
    RekallAgeRuntimeGpuWorkload Workload,
    string Backend = "portable");

public sealed record RekallAgeRuntimeGpuResourceInspection(
    string Id,
    string Kind,
    string Handle,
    ulong EstimatedBytes,
    string DescriptorType);

public sealed record RekallAgeRuntimeGpuCommandInspection(
    int Index,
    string Kind);

public sealed record InspectRuntimeGpuWorkloadResult(
    string WorkloadId,
    string Backend,
    bool Valid,
    IReadOnlyList<RekallAgeRuntimeGpuResourceInspection> Resources,
    IReadOnlyList<RekallAgeRuntimeGpuCommandInspection> Commands,
    IReadOnlyList<RekallAgeGraphicsDiagnostic> Diagnostics);

public sealed class InspectRuntimeGpuWorkloadCommand
    : IRekallAgeCommand<InspectRuntimeGpuWorkloadRequest, InspectRuntimeGpuWorkloadResult>
{
    public string Name => "rekall.render.device.inspect_runtime_workload";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Transactionally compiles one agent-authored named runtime GPU workload against the portable RenderingDevice contract and returns resolved opaque resources, immutable command kinds, and stable diagnostics without submitting GPU work.",
        typeof(InspectRuntimeGpuWorkloadRequest).FullName!,
        typeof(InspectRuntimeGpuWorkloadResult).FullName!);

    public ValueTask<RekallAgeCommandResult<InspectRuntimeGpuWorkloadResult>> ExecuteAsync(
        InspectRuntimeGpuWorkloadRequest request,
        RekallAgeCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Workload);
        var backend = string.IsNullOrWhiteSpace(request.Backend) ? "portable" : request.Backend.Trim();
        using var device = new RekallAgeInMemoryRenderingDevice(
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline(backend));
        using var compiled = new RekallAgeRuntimeGpuWorkloadCompiler().Compile(request.Workload, device);
        var byHandle = device.InspectResources().ToDictionary(item => item.Handle);
        var resources = compiled.Resources
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item =>
            {
                var inspection = byHandle[item.Value];
                return new RekallAgeRuntimeGpuResourceInspection(
                    item.Key,
                    item.Value.Kind.ToString(),
                    item.Value.ToString(),
                    inspection.EstimatedBytes,
                    inspection.Descriptor.GetType().Name);
            })
            .ToArray();
        var commands = compiled.CommandBuffer?.Commands
            .Select((command, index) => new RekallAgeRuntimeGpuCommandInspection(index, CommandName(command)))
            .ToArray() ?? [];
        var result = new InspectRuntimeGpuWorkloadResult(
            request.Workload.Id,
            backend,
            compiled.Valid,
            resources,
            commands,
            compiled.Diagnostics);
        return ValueTask.FromResult(RekallAgeCommandResult<InspectRuntimeGpuWorkloadResult>.Success(
            result,
            result.Valid
                ? $"Runtime GPU workload '{result.WorkloadId}' compiled to {resources.Length} resource(s) and {commands.Length} command(s)."
                : $"Runtime GPU workload '{result.WorkloadId}' has {result.Diagnostics.Count} diagnostic(s)."));
    }

    private static string CommandName(RekallAgeGraphicsCommand command)
    {
        var name = command.GetType().Name;
        const string prefix = "RekallAge";
        const string suffix = "Command";
        if (name.StartsWith(prefix, StringComparison.Ordinal)) name = name[prefix.Length..];
        if (name.EndsWith(suffix, StringComparison.Ordinal)) name = name[..^suffix.Length];
        return name;
    }
}
