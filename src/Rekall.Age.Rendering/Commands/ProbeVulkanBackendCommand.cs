using Rekall.Age.Core.Commands;

namespace Rekall.Age.Rendering.Commands;

public sealed record ProbeVulkanBackendRequest;

public sealed record ProbeVulkanBackendResult(
    bool Available,
    string? LoaderName,
    string? ApiVersion,
    IReadOnlyList<string> InstanceExtensions,
    IReadOnlyList<RekallAgeVulkanPhysicalDeviceInfo> PhysicalDevices,
    IReadOnlyList<string> Errors);

public sealed class ProbeVulkanBackendCommand
    : IRekallAgeCommand<ProbeVulkanBackendRequest, ProbeVulkanBackendResult>
{
    private readonly IRekallAgeVulkanBackendProbe _probe;

    public ProbeVulkanBackendCommand()
        : this(new RekallAgeNativeVulkanBackendProbe())
    {
    }

    public ProbeVulkanBackendCommand(IRekallAgeVulkanBackendProbe probe)
    {
        _probe = probe;
    }

    public string Name => "rekall.render.vulkan.probe";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Probes the native Vulkan loader and reports low-level backend diagnostics for agents.",
        typeof(ProbeVulkanBackendRequest).FullName!,
        typeof(ProbeVulkanBackendResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ProbeVulkanBackendResult>> ExecuteAsync(
        ProbeVulkanBackendRequest request,
        RekallAgeCommandContext context)
    {
        var probe = await _probe.ProbeAsync(context.CancellationToken);
        var result = new ProbeVulkanBackendResult(
            probe.Available,
            probe.LoaderName,
            probe.ApiVersion,
            probe.InstanceExtensions,
            probe.PhysicalDevices,
            probe.Errors);

        if (probe.Available)
        {
            return RekallAgeCommandResult<ProbeVulkanBackendResult>.Success(
                result,
                $"Vulkan loader '{probe.LoaderName}' is available.");
        }

        return RekallAgeCommandResult<ProbeVulkanBackendResult>.Failure(
            result,
            "Vulkan backend is unavailable.",
            [
                new RekallAgeCommandError(
                    "REKALL_VULKAN_UNAVAILABLE",
                    probe.Errors.Count == 0 ? "Vulkan backend is unavailable." : string.Join(" ", probe.Errors),
                    "vulkan")
            ]);
    }
}
