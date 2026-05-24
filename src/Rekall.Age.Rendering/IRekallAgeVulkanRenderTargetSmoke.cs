namespace Rekall.Age.Rendering;

public interface IRekallAgeVulkanRenderTargetSmoke
{
    ValueTask<RekallAgeVulkanRenderTargetSmokeResult> CreateRenderTargetAsync(
        uint width,
        uint height,
        string format,
        string? preferredDeviceType,
        CancellationToken cancellationToken);
}
