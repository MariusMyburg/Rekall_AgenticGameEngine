namespace Rekall.Age.Rendering;

public sealed record RekallAgeVulkanBufferSmokeResult(
    bool Created,
    string? LoaderName,
    RekallAgeVulkanSelectedDevice? SelectedDevice,
    ulong SizeBytes,
    string Usage,
    uint? MemoryTypeIndex,
    IReadOnlyList<string> MemoryProperties,
    bool Bound,
    bool Mapped,
    int BytesWritten,
    IReadOnlyList<string> Errors);
