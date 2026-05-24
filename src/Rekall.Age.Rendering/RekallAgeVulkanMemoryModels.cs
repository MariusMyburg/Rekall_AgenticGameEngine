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
