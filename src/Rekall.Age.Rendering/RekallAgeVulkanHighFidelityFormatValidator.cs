using Silk.NET.Vulkan;

namespace Rekall.Age.Rendering;

public static class RekallAgeVulkanHighFidelityFormatValidator
{
    public static string? ValidateOptimalTilingFeatures(
        Format format,
        FormatFeatureFlags available,
        FormatFeatureFlags required,
        string resource)
    {
        var missing = required & ~available;
        return missing == 0
            ? null
            : $"REKALL_RENDER_FORMAT_UNSUPPORTED: '{resource}' cannot use {format}; missing optimal-tiling features '{missing}'.";
    }
}
