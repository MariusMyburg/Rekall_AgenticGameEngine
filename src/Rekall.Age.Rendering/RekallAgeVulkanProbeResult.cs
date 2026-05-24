namespace Rekall.Age.Rendering;

public sealed record RekallAgeVulkanProbeResult(
    bool Available,
    string? LoaderName,
    string? ApiVersion,
    IReadOnlyList<string> InstanceExtensions,
    IReadOnlyList<RekallAgeVulkanPhysicalDeviceInfo> PhysicalDevices,
    IReadOnlyList<string> Errors);

public sealed record RekallAgeVulkanPhysicalDeviceInfo(
    string Name,
    string DeviceType,
    string ApiVersion);
