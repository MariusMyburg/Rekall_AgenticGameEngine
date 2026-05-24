using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Commands;

namespace Rekall.Age.Tests.Rendering;

public sealed class VulkanLogicalDeviceBootstrapTests
{
    [Fact]
    public void DeviceSelectorPrefersDiscreteGpuWithGraphicsQueue()
    {
        var devices = new[]
        {
            new RekallAgeVulkanCandidateDevice(
                "Integrated",
                "integrated-gpu",
                "1.3.0",
                [new RekallAgeVulkanQueueFamilyInfo(0, ["graphics"], 8)]),
            new RekallAgeVulkanCandidateDevice(
                "Discrete",
                "discrete-gpu",
                "1.4.0",
                [new RekallAgeVulkanQueueFamilyInfo(2, ["graphics", "compute"], 16)])
        };

        var selection = RekallAgeVulkanDeviceSelector.Select(devices, "discrete-gpu");

        Assert.NotNull(selection);
        Assert.Equal("Discrete", selection.Device.Name);
        Assert.Equal(2u, selection.QueueFamily.Index);
    }

    [Fact]
    public void DeviceSelectorFallsBackToAnyGraphicsDeviceWhenPreferenceIsUnavailable()
    {
        var devices = new[]
        {
            new RekallAgeVulkanCandidateDevice(
                "Graphics Device",
                "integrated-gpu",
                "1.3.0",
                [new RekallAgeVulkanQueueFamilyInfo(1, ["graphics"], 4)]),
            new RekallAgeVulkanCandidateDevice(
                "Compute Only",
                "discrete-gpu",
                "1.3.0",
                [new RekallAgeVulkanQueueFamilyInfo(0, ["compute"], 4)])
        };

        var selection = RekallAgeVulkanDeviceSelector.Select(devices, "discrete-gpu");

        Assert.NotNull(selection);
        Assert.Equal("Graphics Device", selection.Device.Name);
        Assert.Equal(1u, selection.QueueFamily.Index);
    }

    [Fact]
    public async Task BootstrapCommandReturnsSelectedLogicalDeviceDetails()
    {
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("bootstrap vulkan"), CancellationToken.None);
        var bootstrap = new FakeVulkanLogicalDeviceBootstrap(new RekallAgeVulkanLogicalDeviceBootstrapResult(
            Available: true,
            LoaderName: "fake-vulkan",
            SelectedDevice: new RekallAgeVulkanSelectedDevice(
                "Fake RTX",
                "discrete-gpu",
                "1.4.0",
                new RekallAgeVulkanQueueFamilyInfo(3, ["graphics"], 8)),
            Errors: []));

        var result = await new BootstrapVulkanLogicalDeviceCommand(bootstrap).ExecuteAsync(
            new BootstrapVulkanLogicalDeviceRequest("discrete-gpu"),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.Available);
        Assert.Equal("Fake RTX", result.Value.SelectedDevice!.Name);
        Assert.Equal(3u, result.Value.SelectedDevice.GraphicsQueueFamily.Index);
    }

    [Fact]
    public async Task BootstrapCommandReportsUnavailableBackendWithoutThrowing()
    {
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("bootstrap missing vulkan"), CancellationToken.None);
        var bootstrap = new FakeVulkanLogicalDeviceBootstrap(new RekallAgeVulkanLogicalDeviceBootstrapResult(
            Available: false,
            LoaderName: null,
            SelectedDevice: null,
            Errors: ["No Vulkan device with a graphics queue was found."]));

        var result = await new BootstrapVulkanLogicalDeviceCommand(bootstrap).ExecuteAsync(
            new BootstrapVulkanLogicalDeviceRequest("discrete-gpu"),
            context);

        Assert.False(result.Ok);
        Assert.Null(result.Value.SelectedDevice);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_VULKAN_LOGICAL_DEVICE_UNAVAILABLE");
    }

    private sealed class FakeVulkanLogicalDeviceBootstrap : IRekallAgeVulkanLogicalDeviceBootstrap
    {
        private readonly RekallAgeVulkanLogicalDeviceBootstrapResult _result;

        public FakeVulkanLogicalDeviceBootstrap(RekallAgeVulkanLogicalDeviceBootstrapResult result)
        {
            _result = result;
        }

        public ValueTask<RekallAgeVulkanLogicalDeviceBootstrapResult> BootstrapAsync(
            string? preferredDeviceType,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(_result);
        }
    }
}
