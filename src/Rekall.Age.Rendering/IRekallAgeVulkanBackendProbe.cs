namespace Rekall.Age.Rendering;

public interface IRekallAgeVulkanBackendProbe
{
    ValueTask<RekallAgeVulkanProbeResult> ProbeAsync(CancellationToken cancellationToken);
}
