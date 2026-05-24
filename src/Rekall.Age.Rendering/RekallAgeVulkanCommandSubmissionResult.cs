namespace Rekall.Age.Rendering;

public sealed record RekallAgeVulkanCommandSubmissionResult(
    bool Submitted,
    string? LoaderName,
    RekallAgeVulkanSelectedDevice? SelectedDevice,
    bool CommandPoolCreated,
    bool CommandBufferAllocated,
    bool FenceSignaled,
    IReadOnlyList<string> Errors);
