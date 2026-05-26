namespace Rekall.Age.Rendering;

public sealed record RekallAgeOpenXrVulkanDeviceInteropInfo(
    string Backend,
    ulong Instance,
    ulong PhysicalDevice,
    ulong Device,
    ulong GraphicsQueue,
    uint GraphicsQueueFamilyIndex,
    bool ExternalTextureWrappingSupported,
    string? DriverName,
    string? DriverInfo,
    int RecommendedEyeWidth = 1920,
    int RecommendedEyeHeight = 1080);

public sealed record RekallAgeOpenXrVulkanInteropInspection(
    string Status,
    bool ReadyForXrGraphicsBinding,
    bool ReadyForXrSwapchainWrapping,
    bool ReadyForCompositorSession,
    int RecommendedEyeWidth,
    int RecommendedEyeHeight,
    int SwapchainArrayLayers,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> Blockers);

public static class RekallAgeOpenXrVulkanInteropInspector
{
    public static RekallAgeOpenXrVulkanInteropInspection Inspect(
        RekallAgeOpenXrSessionBootstrapResult? openXr,
        RekallAgeOpenXrVulkanDeviceInteropInfo? vulkan)
    {
        var capabilities = new List<string>();
        var blockers = new List<string>();

        if (openXr is null)
        {
            blockers.Add("OpenXR was not requested for this player session.");
        }
        else
        {
            if (openXr.HeadsetSessionReady)
            {
                capabilities.Add("openxr-headset-ready");
            }
            else
            {
                blockers.AddRange(openXr.Errors.Count > 0 ? openXr.Errors : openXr.NextRenderSteps);
            }

            if (openXr.VulkanGraphicsRequirementsReady)
            {
                capabilities.Add("openxr-vulkan-graphics-requirements");
            }
            else
            {
                blockers.Add("OpenXR Vulkan graphics requirements are not available.");
            }

            if (openXr.PrimaryStereoViewConfigurationReady && openXr.PrimaryStereoViews.Count >= 2)
            {
                capabilities.Add("openxr-primary-stereo-views");
            }
            else
            {
                blockers.Add("OpenXR primary-stereo view configuration is not available.");
            }
        }

        if (vulkan is null)
        {
            blockers.Add("No Vulkan graphics device interop information was supplied.");
        }
        else
        {
            if (vulkan.Backend.Equals("Vulkan", StringComparison.OrdinalIgnoreCase))
            {
                capabilities.Add("vulkan-backend");
            }
            else
            {
                blockers.Add($"Graphics backend '{vulkan.Backend}' cannot be bound to XR_KHR_vulkan_enable2.");
            }

            if (vulkan.Instance != 0
                && vulkan.PhysicalDevice != 0
                && vulkan.Device != 0
                && vulkan.GraphicsQueue != 0)
            {
                capabilities.Add("native-vulkan-handles");
            }
            else
            {
                blockers.Add("The renderer did not expose all native Vulkan instance/device/queue handles.");
            }

            if (vulkan.ExternalTextureWrappingSupported)
            {
                capabilities.Add("external-vkimage-wrapping");
            }
            else
            {
                blockers.Add("The renderer cannot wrap OpenXR VkImage swapchain images as render targets.");
            }
        }

        var firstEye = openXr?.PrimaryStereoViews.FirstOrDefault();
        var readyForGraphicsBinding = openXr is { HeadsetSessionReady: true, VulkanGraphicsRequirementsReady: true }
            && vulkan is not null
            && vulkan.Backend.Equals("Vulkan", StringComparison.OrdinalIgnoreCase)
            && vulkan.Instance != 0
            && vulkan.PhysicalDevice != 0
            && vulkan.Device != 0
            && vulkan.GraphicsQueue != 0;
        var readyForSwapchainWrapping = readyForGraphicsBinding
            && vulkan is { ExternalTextureWrappingSupported: true }
            && openXr is { PrimaryStereoViewConfigurationReady: true }
            && openXr.PrimaryStereoViews.Count >= 2;
        var readyForCompositorSession = readyForGraphicsBinding && readyForSwapchainWrapping;

        return new RekallAgeOpenXrVulkanInteropInspection(
            readyForCompositorSession ? "ready" : "blocked",
            readyForGraphicsBinding,
            readyForSwapchainWrapping,
            readyForCompositorSession,
            firstEye is null ? 0 : checked((int)firstEye.RecommendedImageRectWidth),
            firstEye is null ? 0 : checked((int)firstEye.RecommendedImageRectHeight),
            openXr?.PrimaryStereoViews.Count ?? 0,
            capabilities.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            blockers.Distinct(StringComparer.Ordinal).ToArray());
    }
}
