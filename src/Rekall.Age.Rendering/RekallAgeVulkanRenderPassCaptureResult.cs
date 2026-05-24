namespace Rekall.Age.Rendering;

public sealed record RekallAgeVulkanRenderPassCaptureResult(
    bool Captured,
    string OutputPath,
    string? LoaderName,
    RekallAgeVulkanSelectedDevice? SelectedDevice,
    uint Width,
    uint Height,
    string Format,
    RekallAgeVulkanClearColor ClearColor,
    ulong BytesRead,
    ulong NonZeroBytes,
    RekallAgeVulkanReadbackPixel FirstPixel,
    ulong ByteChecksum,
    IReadOnlyList<string> Errors);
