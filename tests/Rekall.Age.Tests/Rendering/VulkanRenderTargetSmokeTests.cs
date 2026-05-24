using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Commands;

namespace Rekall.Age.Tests.Rendering;

public sealed class VulkanRenderTargetSmokeTests
{
    [Fact]
    public async Task RenderTargetSmokeCommandReturnsFramebufferDetails()
    {
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("vulkan render target"), CancellationToken.None);
        var smoke = new FakeVulkanRenderTargetSmoke(new RekallAgeVulkanRenderTargetSmokeResult(
            Created: true,
            LoaderName: "fake-vulkan",
            SelectedDevice: new RekallAgeVulkanSelectedDevice(
                "Fake RTX",
                "discrete-gpu",
                "1.4.0",
                new RekallAgeVulkanQueueFamilyInfo(0, ["graphics"], 8)),
            Width: 128,
            Height: 72,
            Format: "R8G8B8A8_UNorm",
            ImageCreated: true,
            ImageViewCreated: true,
            RenderPassCreated: true,
            FramebufferCreated: true,
            Errors: []));

        var result = await new CreateVulkanRenderTargetCommand(smoke).ExecuteAsync(
            new CreateVulkanRenderTargetRequest(128, 72, "R8G8B8A8_UNorm", "discrete-gpu"),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.Created);
        Assert.True(result.Value.ImageCreated);
        Assert.True(result.Value.ImageViewCreated);
        Assert.True(result.Value.RenderPassCreated);
        Assert.True(result.Value.FramebufferCreated);
    }

    [Fact]
    public async Task RenderTargetSmokeCommandReportsFailureWithoutThrowing()
    {
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("vulkan render target failure"), CancellationToken.None);
        var smoke = new FakeVulkanRenderTargetSmoke(new RekallAgeVulkanRenderTargetSmokeResult(
            Created: false,
            LoaderName: null,
            SelectedDevice: null,
            Width: 128,
            Height: 72,
            Format: "R8G8B8A8_UNorm",
            ImageCreated: true,
            ImageViewCreated: false,
            RenderPassCreated: false,
            FramebufferCreated: false,
            Errors: ["vkCreateImageView failed with VkResult -2."]));

        var result = await new CreateVulkanRenderTargetCommand(smoke).ExecuteAsync(
            new CreateVulkanRenderTargetRequest(128, 72, "R8G8B8A8_UNorm", "discrete-gpu"),
            context);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_VULKAN_RENDER_TARGET_CREATE_FAILED");
    }

    private sealed class FakeVulkanRenderTargetSmoke : IRekallAgeVulkanRenderTargetSmoke
    {
        private readonly RekallAgeVulkanRenderTargetSmokeResult _result;

        public FakeVulkanRenderTargetSmoke(RekallAgeVulkanRenderTargetSmokeResult result)
        {
            _result = result;
        }

        public ValueTask<RekallAgeVulkanRenderTargetSmokeResult> CreateRenderTargetAsync(
            uint width,
            uint height,
            string format,
            string? preferredDeviceType,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(_result);
        }
    }
}
