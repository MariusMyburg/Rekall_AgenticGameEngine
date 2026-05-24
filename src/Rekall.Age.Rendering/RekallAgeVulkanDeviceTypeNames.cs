namespace Rekall.Age.Rendering;

public static class RekallAgeVulkanDeviceTypeNames
{
    public static string FromVulkanDeviceType(uint deviceType)
    {
        return deviceType switch
        {
            0 => "other",
            1 => "integrated-gpu",
            2 => "discrete-gpu",
            3 => "virtual-gpu",
            4 => "cpu",
            _ => "unknown"
        };
    }
}
