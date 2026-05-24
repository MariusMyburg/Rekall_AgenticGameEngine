namespace Rekall.Age.Rendering;

public interface IRekallAgeVulkanRenderPassSubmission
{
    ValueTask<RekallAgeVulkanRenderPassSubmissionResult> SubmitClearRenderPassAsync(
        uint width,
        uint height,
        string format,
        string? preferredDeviceType,
        CancellationToken cancellationToken);
}
