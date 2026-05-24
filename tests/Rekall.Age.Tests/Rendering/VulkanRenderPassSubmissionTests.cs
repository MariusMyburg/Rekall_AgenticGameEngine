using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Commands;

namespace Rekall.Age.Tests.Rendering;

public sealed class VulkanRenderPassSubmissionTests
{
    [Fact]
    public async Task RenderPassSubmitClearCommandReturnsSubmittedRenderPassDetails()
    {
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("vulkan render pass submit"), CancellationToken.None);
        var submission = new FakeVulkanRenderPassSubmission(new RekallAgeVulkanRenderPassSubmissionResult(
            Submitted: true,
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
            CommandPoolCreated: true,
            CommandBufferAllocated: true,
            RenderPassBegan: true,
            RenderPassEnded: true,
            FenceSignaled: true,
            ClearColor: RekallAgeVulkanClearColor.Default,
            Errors: []));

        var result = await new SubmitClearVulkanRenderPassCommand(submission).ExecuteAsync(
            new SubmitClearVulkanRenderPassRequest(128, 72, "R8G8B8A8_UNorm", "discrete-gpu"),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.Submitted);
        Assert.True(result.Value.ImageCreated);
        Assert.True(result.Value.ImageViewCreated);
        Assert.True(result.Value.RenderPassCreated);
        Assert.True(result.Value.FramebufferCreated);
        Assert.True(result.Value.CommandPoolCreated);
        Assert.True(result.Value.CommandBufferAllocated);
        Assert.True(result.Value.RenderPassBegan);
        Assert.True(result.Value.RenderPassEnded);
        Assert.True(result.Value.FenceSignaled);
    }

    [Fact]
    public async Task RenderPassSubmitClearCommandReportsFailureWithoutThrowing()
    {
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("vulkan render pass submit failure"), CancellationToken.None);
        var submission = new FakeVulkanRenderPassSubmission(new RekallAgeVulkanRenderPassSubmissionResult(
            Submitted: false,
            LoaderName: null,
            SelectedDevice: null,
            Width: 128,
            Height: 72,
            Format: "R8G8B8A8_UNorm",
            ImageCreated: true,
            ImageViewCreated: true,
            RenderPassCreated: true,
            FramebufferCreated: true,
            CommandPoolCreated: true,
            CommandBufferAllocated: true,
            RenderPassBegan: true,
            RenderPassEnded: false,
            FenceSignaled: false,
            ClearColor: RekallAgeVulkanClearColor.Default,
            Errors: ["vkEndCommandBuffer failed with VkResult -2."]));

        var result = await new SubmitClearVulkanRenderPassCommand(submission).ExecuteAsync(
            new SubmitClearVulkanRenderPassRequest(128, 72, "R8G8B8A8_UNorm", "discrete-gpu"),
            context);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_VULKAN_RENDER_PASS_SUBMIT_FAILED");
    }

    private sealed class FakeVulkanRenderPassSubmission : IRekallAgeVulkanRenderPassSubmission
    {
        private readonly RekallAgeVulkanRenderPassSubmissionResult _result;

        public FakeVulkanRenderPassSubmission(RekallAgeVulkanRenderPassSubmissionResult result)
        {
            _result = result;
        }

        public ValueTask<RekallAgeVulkanRenderPassSubmissionResult> SubmitClearRenderPassAsync(
            uint width,
            uint height,
            string format,
            string? preferredDeviceType,
            RekallAgeVulkanClearColor clearColor,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(_result);
        }
    }
}
