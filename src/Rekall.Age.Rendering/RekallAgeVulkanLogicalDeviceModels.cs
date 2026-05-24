namespace Rekall.Age.Rendering;

public sealed record RekallAgeVulkanQueueFamilyInfo(
    uint Index,
    IReadOnlyList<string> Capabilities,
    uint QueueCount);

public sealed record RekallAgeVulkanCandidateDevice(
    string Name,
    string DeviceType,
    string ApiVersion,
    IReadOnlyList<RekallAgeVulkanQueueFamilyInfo> QueueFamilies);

public sealed record RekallAgeVulkanSelectedDevice(
    string Name,
    string DeviceType,
    string ApiVersion,
    RekallAgeVulkanQueueFamilyInfo GraphicsQueueFamily);

public sealed record RekallAgeVulkanDeviceSelection(
    RekallAgeVulkanCandidateDevice Device,
    RekallAgeVulkanQueueFamilyInfo QueueFamily);

public sealed record RekallAgeVulkanLogicalDeviceBootstrapResult(
    bool Available,
    string? LoaderName,
    RekallAgeVulkanSelectedDevice? SelectedDevice,
    IReadOnlyList<string> Errors);
