using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Commands;

namespace Rekall.Age.Tests.Rendering;

public sealed class VulkanImageSmokeTests
{
    [Fact]
    public async Task ImageSmokeCommandReturnsBoundImageDetails()
    {
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("vulkan image"), CancellationToken.None);
        var smoke = new FakeVulkanImageSmoke(new RekallAgeVulkanImageSmokeResult(
            Created: true,
            LoaderName: "fake-vulkan",
            SelectedDevice: new RekallAgeVulkanSelectedDevice(
                "Fake RTX",
                "discrete-gpu",
                "1.4.0",
                new RekallAgeVulkanQueueFamilyInfo(0, ["graphics"], 8)),
            Width: 64,
            Height: 64,
            Format: "R8G8B8A8_UNorm",
            Usage: "color-attachment",
            MemoryTypeIndex: 2,
            MemoryProperties: ["device-local"],
            Bound: true,
            Errors: []));

        var result = await new CreateBoundVulkanImageCommand(smoke).ExecuteAsync(
            new CreateBoundVulkanImageRequest(64, 64, "R8G8B8A8_UNorm", "color-attachment", "discrete-gpu"),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.Created);
        Assert.True(result.Value.Bound);
        Assert.Equal(64u, result.Value.Width);
        Assert.Equal("color-attachment", result.Value.Usage);
        Assert.Equal(2u, result.Value.MemoryTypeIndex);
    }

    [Fact]
    public async Task ImageSmokeCommandReportsFailureWithoutThrowing()
    {
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("vulkan image failure"), CancellationToken.None);
        var smoke = new FakeVulkanImageSmoke(new RekallAgeVulkanImageSmokeResult(
            Created: false,
            LoaderName: null,
            SelectedDevice: null,
            Width: 64,
            Height: 64,
            Format: "R8G8B8A8_UNorm",
            Usage: "color-attachment",
            MemoryTypeIndex: null,
            MemoryProperties: [],
            Bound: false,
            Errors: ["vkCreateImage failed with VkResult -2."]));

        var result = await new CreateBoundVulkanImageCommand(smoke).ExecuteAsync(
            new CreateBoundVulkanImageRequest(64, 64, "R8G8B8A8_UNorm", "color-attachment", "discrete-gpu"),
            context);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_VULKAN_IMAGE_CREATE_FAILED");
    }

    private sealed class FakeVulkanImageSmoke : IRekallAgeVulkanImageSmoke
    {
        private readonly RekallAgeVulkanImageSmokeResult _result;

        public FakeVulkanImageSmoke(RekallAgeVulkanImageSmokeResult result)
        {
            _result = result;
        }

        public ValueTask<RekallAgeVulkanImageSmokeResult> CreateBoundImageAsync(
            uint width,
            uint height,
            string format,
            string usage,
            string? preferredDeviceType,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(_result);
        }
    }
}
