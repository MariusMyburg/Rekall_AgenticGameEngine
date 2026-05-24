namespace Rekall.Age.Rendering;

public sealed record RekallAgeVulkanRenderPassSubmissionResult(
    bool Submitted,
    string? LoaderName,
    RekallAgeVulkanSelectedDevice? SelectedDevice,
    uint Width,
    uint Height,
    string Format,
    bool ImageCreated,
    bool ImageViewCreated,
    bool RenderPassCreated,
    bool FramebufferCreated,
    bool CommandPoolCreated,
    bool CommandBufferAllocated,
    bool RenderPassBegan,
    bool RenderPassEnded,
    bool FenceSignaled,
    IReadOnlyList<string> Errors);
