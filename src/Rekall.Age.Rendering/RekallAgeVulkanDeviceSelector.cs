namespace Rekall.Age.Rendering;

public static class RekallAgeVulkanDeviceSelector
{
    public static RekallAgeVulkanDeviceSelection? Select(
        IEnumerable<RekallAgeVulkanCandidateDevice> devices,
        string? preferredDeviceType)
    {
        var candidates = devices
            .Select(device => new
            {
                Device = device,
                QueueFamily = device.QueueFamilies.FirstOrDefault(HasGraphics)
            })
            .Where(candidate => candidate.QueueFamily is not null)
            .ToArray();

        var preferred = Normalize(preferredDeviceType);
        var selected = candidates
            .OrderByDescending(candidate => candidate.Device.DeviceType.Equals(preferred, StringComparison.Ordinal))
            .ThenByDescending(candidate => candidate.Device.DeviceType.Equals("discrete-gpu", StringComparison.Ordinal))
            .ThenBy(candidate => candidate.Device.Name, StringComparer.Ordinal)
            .FirstOrDefault();

        return selected is null
            ? null
            : new RekallAgeVulkanDeviceSelection(selected.Device, selected.QueueFamily!);
    }

    private static bool HasGraphics(RekallAgeVulkanQueueFamilyInfo queueFamily)
    {
        return queueFamily.Capabilities.Contains("graphics", StringComparer.Ordinal);
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "discrete-gpu" : value.Trim().ToLowerInvariant();
    }
}
