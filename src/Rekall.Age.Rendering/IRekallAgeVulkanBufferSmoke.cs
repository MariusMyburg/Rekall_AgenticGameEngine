namespace Rekall.Age.Rendering;

public interface IRekallAgeVulkanBufferSmoke
{
    ValueTask<RekallAgeVulkanBufferSmokeResult> CreateMappedBufferAsync(
        ulong sizeBytes,
        string usage,
        string? preferredDeviceType,
        CancellationToken cancellationToken);
}
