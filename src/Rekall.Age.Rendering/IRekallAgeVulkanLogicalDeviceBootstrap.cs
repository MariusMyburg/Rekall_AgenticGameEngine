namespace Rekall.Age.Rendering;

public interface IRekallAgeVulkanLogicalDeviceBootstrap
{
    ValueTask<RekallAgeVulkanLogicalDeviceBootstrapResult> BootstrapAsync(
        string? preferredDeviceType,
        CancellationToken cancellationToken);
}
