using Rekall.Age.Core.Commands;

namespace Rekall.Age.Rendering.Commands;

public sealed record BootstrapVulkanLogicalDeviceRequest(string? PreferredDeviceType = "discrete-gpu");

public sealed record BootstrapVulkanLogicalDeviceResult(
    bool Available,
    string? LoaderName,
    RekallAgeVulkanSelectedDevice? SelectedDevice,
    IReadOnlyList<string> Errors);

public sealed class BootstrapVulkanLogicalDeviceCommand
    : IRekallAgeCommand<BootstrapVulkanLogicalDeviceRequest, BootstrapVulkanLogicalDeviceResult>
{
    private readonly IRekallAgeVulkanLogicalDeviceBootstrap _bootstrap;

    public BootstrapVulkanLogicalDeviceCommand()
        : this(new RekallAgeNativeVulkanLogicalDeviceBootstrap())
    {
    }

    public BootstrapVulkanLogicalDeviceCommand(IRekallAgeVulkanLogicalDeviceBootstrap bootstrap)
    {
        _bootstrap = bootstrap;
    }

    public string Name => "rekall.render.vulkan.device.bootstrap";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Creates and destroys a native Vulkan logical device to validate backend initialization.",
        typeof(BootstrapVulkanLogicalDeviceRequest).FullName!,
        typeof(BootstrapVulkanLogicalDeviceResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<BootstrapVulkanLogicalDeviceResult>> ExecuteAsync(
        BootstrapVulkanLogicalDeviceRequest request,
        RekallAgeCommandContext context)
    {
        var bootstrap = await _bootstrap.BootstrapAsync(request.PreferredDeviceType, context.CancellationToken);
        var result = new BootstrapVulkanLogicalDeviceResult(
            bootstrap.Available,
            bootstrap.LoaderName,
            bootstrap.SelectedDevice,
            bootstrap.Errors);

        if (bootstrap.Available)
        {
            return RekallAgeCommandResult<BootstrapVulkanLogicalDeviceResult>.Success(
                result,
                $"Bootstrapped Vulkan logical device '{bootstrap.SelectedDevice!.Name}'.");
        }

        return RekallAgeCommandResult<BootstrapVulkanLogicalDeviceResult>.Failure(
            result,
            "Vulkan logical device bootstrap failed.",
            [
                new RekallAgeCommandError(
                    "REKALL_VULKAN_LOGICAL_DEVICE_UNAVAILABLE",
                    bootstrap.Errors.Count == 0
                        ? "Vulkan logical device bootstrap failed."
                        : string.Join(" ", bootstrap.Errors),
                    "vulkan")
            ]);
    }
}
