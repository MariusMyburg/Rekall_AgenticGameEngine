namespace Rekall.Age.Rendering;

public interface IRekallAgeVulkanImageSmoke
{
    ValueTask<RekallAgeVulkanImageSmokeResult> CreateBoundImageAsync(
        uint width,
        uint height,
        string format,
        string usage,
        string? preferredDeviceType,
        CancellationToken cancellationToken);
}
