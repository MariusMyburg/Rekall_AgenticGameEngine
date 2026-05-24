namespace Rekall.Age.Rendering;

public interface IRekallAgeVulkanRenderPassCapture
{
    ValueTask<RekallAgeVulkanRenderPassCaptureResult> CaptureClearRenderPassAsync(
        uint width,
        uint height,
        string format,
        string? preferredDeviceType,
        string outputDirectory,
        RekallAgeVulkanClearColor clearColor,
        CancellationToken cancellationToken);
}
