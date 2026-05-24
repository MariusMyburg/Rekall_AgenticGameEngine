namespace Rekall.Age.Rendering;

public static class RekallAgeVulkanLoaderCandidateNames
{
    public static IReadOnlyList<string> ForCurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return ["vulkan-1", "vulkan-1.dll"];
        }

        if (OperatingSystem.IsLinux())
        {
            return ["libvulkan.so.1", "libvulkan.so"];
        }

        return ["vulkan"];
    }
}
