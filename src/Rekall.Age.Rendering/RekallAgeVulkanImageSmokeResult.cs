namespace Rekall.Age.Rendering;

public sealed record RekallAgeVulkanImageSmokeResult(
    bool Created,
    string? LoaderName,
    RekallAgeVulkanSelectedDevice? SelectedDevice,
    uint Width,
    uint Height,
    string Format,
    string Usage,
    uint? MemoryTypeIndex,
    IReadOnlyList<string> MemoryProperties,
    bool Bound,
    IReadOnlyList<string> Errors);
