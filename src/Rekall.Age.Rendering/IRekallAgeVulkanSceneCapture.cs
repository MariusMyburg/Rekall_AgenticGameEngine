using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

public interface IRekallAgeVulkanSceneCapture
{
    ValueTask<RekallAgeVulkanSceneCaptureResult> CaptureSceneAsync(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportAssetSet assets,
        string outputDirectory,
        string? preferredDeviceType,
        CancellationToken cancellationToken);
}
