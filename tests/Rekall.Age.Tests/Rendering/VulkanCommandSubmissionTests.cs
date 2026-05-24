using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Commands;

namespace Rekall.Age.Tests.Rendering;

public sealed class VulkanCommandSubmissionTests
{
    [Fact]
    public async Task SubmitCommandReturnsSubmittedCommandBufferDetails()
    {
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("submit vulkan"), CancellationToken.None);
        var submission = new FakeVulkanCommandSubmission(new RekallAgeVulkanCommandSubmissionResult(
            Submitted: true,
            LoaderName: "fake-vulkan",
            SelectedDevice: new RekallAgeVulkanSelectedDevice(
                "Fake RTX",
                "discrete-gpu",
                "1.4.0",
                new RekallAgeVulkanQueueFamilyInfo(4, ["graphics", "compute"], 8)),
            CommandPoolCreated: true,
            CommandBufferAllocated: true,
            FenceSignaled: true,
            Errors: []));

        var result = await new SubmitEmptyVulkanCommandBufferCommand(submission).ExecuteAsync(
            new SubmitEmptyVulkanCommandBufferRequest("discrete-gpu"),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.Submitted);
        Assert.True(result.Value.CommandPoolCreated);
        Assert.True(result.Value.CommandBufferAllocated);
        Assert.True(result.Value.FenceSignaled);
        Assert.Equal(4u, result.Value.SelectedDevice!.GraphicsQueueFamily.Index);
    }

    [Fact]
    public async Task SubmitCommandReportsUnavailableBackendWithoutThrowing()
    {
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("submit missing vulkan"), CancellationToken.None);
        var submission = new FakeVulkanCommandSubmission(new RekallAgeVulkanCommandSubmissionResult(
            Submitted: false,
            LoaderName: null,
            SelectedDevice: null,
            CommandPoolCreated: false,
            CommandBufferAllocated: false,
            FenceSignaled: false,
            Errors: ["vkQueueSubmit failed with VkResult -4."]));

        var result = await new SubmitEmptyVulkanCommandBufferCommand(submission).ExecuteAsync(
            new SubmitEmptyVulkanCommandBufferRequest("discrete-gpu"),
            context);

        Assert.False(result.Ok);
        Assert.False(result.Value.Submitted);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_VULKAN_COMMAND_SUBMIT_FAILED");
    }

    private sealed class FakeVulkanCommandSubmission : IRekallAgeVulkanCommandSubmission
    {
        private readonly RekallAgeVulkanCommandSubmissionResult _result;

        public FakeVulkanCommandSubmission(RekallAgeVulkanCommandSubmissionResult result)
        {
            _result = result;
        }

        public ValueTask<RekallAgeVulkanCommandSubmissionResult> SubmitEmptyCommandBufferAsync(
            string? preferredDeviceType,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(_result);
        }
    }
}
