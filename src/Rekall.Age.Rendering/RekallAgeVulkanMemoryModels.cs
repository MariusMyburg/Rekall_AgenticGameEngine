namespace Rekall.Age.Rendering;

public sealed record RekallAgeVulkanMemoryTypeInfo(
    uint Index,
    IReadOnlyList<string> Properties);

public static class RekallAgeVulkanMemoryTypeSelector
{
    public static uint? Select(
        IEnumerable<RekallAgeVulkanMemoryTypeInfo> memoryTypes,
        uint memoryTypeBits,
        IReadOnlyList<string> requiredProperties)
    {
        foreach (var memoryType in memoryTypes.OrderBy(item => item.Index))
        {
            var bit = 1u << (int)memoryType.Index;
            if ((memoryTypeBits & bit) == 0)
            {
                continue;
            }

            if (requiredProperties.All(property => memoryType.Properties.Contains(property, StringComparer.Ordinal)))
            {
                return memoryType.Index;
            }
        }

        return null;
    }
}

public static class RekallAgeVulkanMemoryPropertyNames
{
    private const uint VkMemoryPropertyDeviceLocalBit = 0x00000001;
    private const uint VkMemoryPropertyHostVisibleBit = 0x00000002;
    private const uint VkMemoryPropertyHostCoherentBit = 0x00000004;
    private const uint VkMemoryPropertyHostCachedBit = 0x00000008;

    public static IReadOnlyList<string> FromVulkanFlags(uint flags)
    {
        var properties = new List<string>();
        if ((flags & VkMemoryPropertyDeviceLocalBit) != 0)
        {
            properties.Add("device-local");
        }

        if ((flags & VkMemoryPropertyHostVisibleBit) != 0)
        {
            properties.Add("host-visible");
        }

        if ((flags & VkMemoryPropertyHostCoherentBit) != 0)
        {
            properties.Add("host-coherent");
        }

        if ((flags & VkMemoryPropertyHostCachedBit) != 0)
        {
            properties.Add("host-cached");
        }

        return properties;
    }
}
