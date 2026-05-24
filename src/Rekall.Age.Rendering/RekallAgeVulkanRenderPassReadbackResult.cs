namespace Rekall.Age.Rendering;

public readonly record struct RekallAgeVulkanReadbackPixel(byte R, byte G, byte B, byte A);

public sealed record RekallAgeVulkanRenderPassReadbackResult(
    bool Readback,
    string? LoaderName,
    RekallAgeVulkanSelectedDevice? SelectedDevice,
    uint Width,
    uint Height,
    string Format,
    RekallAgeVulkanClearColor ClearColor,
    bool Submitted,
    bool BufferCreated,
    bool BufferBound,
    bool BufferMapped,
    ulong BytesRead,
    ulong NonZeroBytes,
    RekallAgeVulkanReadbackPixel FirstPixel,
    ulong ByteChecksum,
    IReadOnlyList<string> Errors);
