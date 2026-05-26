using Rekall.Age.Rendering;

namespace Rekall.Age.Tests.Rendering;

public sealed class OpenXrVulkanInteropTests
{
    [Fact]
    public void InspectorReportsReadyWhenOpenXrAndVulkanHandlesAreUsable()
    {
        var openXr = ReadyOpenXr();
        var vulkan = new RekallAgeOpenXrVulkanDeviceInteropInfo(
            "Vulkan",
            Instance: 1,
            PhysicalDevice: 2,
            Device: 3,
            GraphicsQueue: 4,
            GraphicsQueueFamilyIndex: 0,
            ExternalTextureWrappingSupported: true,
            DriverName: "driver",
            DriverInfo: "info");

        var result = RekallAgeOpenXrVulkanInteropInspector.Inspect(openXr, vulkan);

        Assert.Equal("ready", result.Status);
        Assert.True(result.ReadyForXrGraphicsBinding);
        Assert.True(result.ReadyForXrSwapchainWrapping);
        Assert.True(result.ReadyForCompositorSession);
        Assert.Equal(1832, result.RecommendedEyeWidth);
        Assert.Equal(1920, result.RecommendedEyeHeight);
        Assert.Equal(2, result.SwapchainArrayLayers);
        Assert.Contains("external-vkimage-wrapping", result.Capabilities);
    }

    [Fact]
    public void InspectorBlocksWhenTextureWrappingIsUnavailable()
    {
        var openXr = ReadyOpenXr();
        var vulkan = new RekallAgeOpenXrVulkanDeviceInteropInfo(
            "Vulkan",
            Instance: 1,
            PhysicalDevice: 2,
            Device: 3,
            GraphicsQueue: 4,
            GraphicsQueueFamilyIndex: 0,
            ExternalTextureWrappingSupported: false,
            DriverName: null,
            DriverInfo: null);

        var result = RekallAgeOpenXrVulkanInteropInspector.Inspect(openXr, vulkan);

        Assert.Equal("blocked", result.Status);
        Assert.True(result.ReadyForXrGraphicsBinding);
        Assert.False(result.ReadyForXrSwapchainWrapping);
        Assert.False(result.ReadyForCompositorSession);
        Assert.Contains(result.Blockers, blocker => blocker.Contains("wrap", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CompositorBootstrapPrefersSrgbSwapchainFormats()
    {
        var preferred = RekallAgeNativeOpenXrCompositorSessionBootstrap.SelectPreferredColorFormat([50, 43]);

        Assert.Equal(43, preferred);
    }

    [Fact]
    public void CompositorBootstrapSelectsDepthFormatWhenRuntimeAdvertisesOne()
    {
        var preferred = RekallAgeNativeOpenXrCompositorSessionBootstrap.SelectPreferredDepthFormat([50, 126]);

        Assert.Equal(126, preferred);
    }

    [Fact]
    public void CompositorBootstrapOnlyBeginsSessionAfterReadyState()
    {
        Assert.False(RekallAgeNativeOpenXrCompositorSessionBootstrap.CanBeginOpenXrSession(1));
        Assert.True(RekallAgeNativeOpenXrCompositorSessionBootstrap.CanBeginOpenXrSession(2));
        Assert.False(RekallAgeNativeOpenXrCompositorSessionBootstrap.CanBeginOpenXrSession(3));
    }

    [Fact]
    public void CompositorBootstrapDescribesObservedSessionState()
    {
        Assert.Equal("READY", RekallAgeNativeOpenXrCompositorSessionBootstrap.DescribeOpenXrSessionState(2));
        Assert.Equal("FOCUSED", RekallAgeNativeOpenXrCompositorSessionBootstrap.DescribeOpenXrSessionState(5));
        Assert.Equal("NONE", RekallAgeNativeOpenXrCompositorSessionBootstrap.DescribeOpenXrSessionState(null));
    }

    private static RekallAgeOpenXrSessionBootstrapResult ReadyOpenXr()
    {
        return new RekallAgeOpenXrSessionBootstrapResult(
            LoaderAvailable: true,
            RuntimeAvailable: true,
            InstanceCreated: true,
            HmdSystemAvailable: true,
            SystemId: 42,
            VulkanEnable2Available: true,
            PrimaryStereoReady: true,
            VulkanGraphicsRequirementsReady: true,
            VulkanGraphicsRequirements: new RekallAgeOpenXrVulkanGraphicsRequirements("1.1.0", "1.3.0"),
            PrimaryStereoViewConfigurationReady: true,
            PrimaryStereoViews:
            [
                new RekallAgeOpenXrViewConfigurationView(0, 1832, 1832, 1920, 1920, 1, 4),
                new RekallAgeOpenXrViewConfigurationView(1, 1832, 1832, 1920, 1920, 1, 4)
            ],
            HeadsetSessionReady: true,
            RequiredExtensions: ["XR_KHR_vulkan_enable2"],
            EnabledExtensions: ["XR_KHR_vulkan_enable2"],
            MissingExtensions: [],
            NextRenderSteps: [],
            Errors: []);
    }
}
