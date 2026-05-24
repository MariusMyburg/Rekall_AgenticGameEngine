namespace Rekall.Age.Rendering;

public interface IRekallAgeVulkanRenderPassReadback
{
    ValueTask<RekallAgeVulkanRenderPassReadbackResult> ReadClearRenderPassAsync(
        uint width,
        uint height,
        string format,
        string? preferredDeviceType,
        CancellationToken cancellationToken);
}
