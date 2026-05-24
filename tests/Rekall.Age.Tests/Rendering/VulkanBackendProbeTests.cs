using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Commands;

namespace Rekall.Age.Tests.Rendering;

public sealed class VulkanBackendProbeTests
{
    [Fact]
    public void LoaderCandidateNamesIncludeCurrentPlatformLoader()
    {
        var names = RekallAgeVulkanLoaderCandidateNames.ForCurrentPlatform();

        if (OperatingSystem.IsWindows())
        {
            Assert.Contains("vulkan-1", names);
            Assert.Contains("vulkan-1.dll", names);
        }
        else if (OperatingSystem.IsLinux())
        {
            Assert.Contains("libvulkan.so.1", names);
            Assert.Contains("libvulkan.so", names);
        }
        else
        {
            Assert.NotEmpty(names);
        }
    }

    [Fact]
    public async Task ProbeCommandReturnsStructuredVulkanDiagnostics()
    {
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("probe vulkan"), CancellationToken.None);
        var probe = new FakeVulkanBackendProbe(new RekallAgeVulkanProbeResult(
            Available: true,
            LoaderName: "fake-vulkan",
            ApiVersion: "1.3.280",
            InstanceExtensions: ["VK_KHR_surface", "VK_EXT_debug_utils"],
            PhysicalDevices: [new RekallAgeVulkanPhysicalDeviceInfo("Fake GPU", "discrete-gpu", "1.3.280")],
            Errors: []));

        var result = await new ProbeVulkanBackendCommand(probe).ExecuteAsync(new ProbeVulkanBackendRequest(), context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.Available);
        Assert.Equal("fake-vulkan", result.Value.LoaderName);
        Assert.Contains("VK_KHR_surface", result.Value.InstanceExtensions);
        Assert.Single(result.Value.PhysicalDevices);
    }

    [Fact]
    public async Task ProbeCommandReportsUnavailableVulkanWithoutThrowing()
    {
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("probe missing vulkan"), CancellationToken.None);
        var probe = new FakeVulkanBackendProbe(new RekallAgeVulkanProbeResult(
            Available: false,
            LoaderName: null,
            ApiVersion: null,
            InstanceExtensions: [],
            PhysicalDevices: [],
            Errors: ["Vulkan loader was not found."]));

        var result = await new ProbeVulkanBackendCommand(probe).ExecuteAsync(new ProbeVulkanBackendRequest(), context);

        Assert.False(result.Ok);
        Assert.False(result.Value.Available);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_VULKAN_UNAVAILABLE");
    }

    [Theory]
    [InlineData(0, "other")]
    [InlineData(1, "integrated-gpu")]
    [InlineData(2, "discrete-gpu")]
    [InlineData(3, "virtual-gpu")]
    [InlineData(4, "cpu")]
    [InlineData(999, "unknown")]
    public void DeviceTypeNamesFollowVulkanPhysicalDeviceTypes(uint deviceType, string expected)
    {
        Assert.Equal(expected, RekallAgeVulkanDeviceTypeNames.FromVulkanDeviceType(deviceType));
    }

    private sealed class FakeVulkanBackendProbe : IRekallAgeVulkanBackendProbe
    {
        private readonly RekallAgeVulkanProbeResult _result;

        public FakeVulkanBackendProbe(RekallAgeVulkanProbeResult result)
        {
            _result = result;
        }

        public ValueTask<RekallAgeVulkanProbeResult> ProbeAsync(CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(_result);
        }
    }
}
