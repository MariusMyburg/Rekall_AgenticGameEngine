namespace Rekall.Age.Rendering;

public sealed record RekallAgeVulkanRenderTargetSmokeResult(
    bool Created,
    string? LoaderName,
    RekallAgeVulkanSelectedDevice? SelectedDevice,
    uint Width,
    uint Height,
    string Format,
    bool ImageCreated,
    bool ImageViewCreated,
    bool RenderPassCreated,
    bool FramebufferCreated,
    IReadOnlyList<string> Errors);
