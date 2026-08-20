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

    ValueTask<RekallAgeVulkanSceneCaptureResult> CaptureProjectSceneAsync(
        string projectRoot,
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportAssetSet assets,
        string outputDirectory,
        string? preferredDeviceType,
        CancellationToken cancellationToken) =>
        CaptureSceneAsync(frame, assets, outputDirectory, preferredDeviceType, cancellationToken);
}
