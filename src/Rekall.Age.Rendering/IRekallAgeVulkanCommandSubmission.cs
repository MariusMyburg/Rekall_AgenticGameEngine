namespace Rekall.Age.Rendering;

public interface IRekallAgeVulkanCommandSubmission
{
    ValueTask<RekallAgeVulkanCommandSubmissionResult> SubmitEmptyCommandBufferAsync(
        string? preferredDeviceType,
        CancellationToken cancellationToken);
}
