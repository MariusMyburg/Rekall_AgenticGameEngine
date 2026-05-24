using Rekall.Age.Core.Commands;

namespace Rekall.Age.Rendering.Commands;

public sealed record SubmitEmptyVulkanCommandBufferRequest(string? PreferredDeviceType = "discrete-gpu");

public sealed record SubmitEmptyVulkanCommandBufferResult(
    bool Submitted,
    string? LoaderName,
    RekallAgeVulkanSelectedDevice? SelectedDevice,
    bool CommandPoolCreated,
    bool CommandBufferAllocated,
    bool FenceSignaled,
    IReadOnlyList<string> Errors);

public sealed class SubmitEmptyVulkanCommandBufferCommand
    : IRekallAgeCommand<SubmitEmptyVulkanCommandBufferRequest, SubmitEmptyVulkanCommandBufferResult>
{
    private readonly IRekallAgeVulkanCommandSubmission _submission;

    public SubmitEmptyVulkanCommandBufferCommand()
        : this(new RekallAgeNativeVulkanCommandSubmission())
    {
    }

    public SubmitEmptyVulkanCommandBufferCommand(IRekallAgeVulkanCommandSubmission submission)
    {
        _submission = submission;
    }

    public string Name => "rekall.render.vulkan.command_buffer.submit_empty";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Creates, records, submits, and waits for an empty native Vulkan command buffer.",
        typeof(SubmitEmptyVulkanCommandBufferRequest).FullName!,
        typeof(SubmitEmptyVulkanCommandBufferResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<SubmitEmptyVulkanCommandBufferResult>> ExecuteAsync(
        SubmitEmptyVulkanCommandBufferRequest request,
        RekallAgeCommandContext context)
    {
        var submission = await _submission.SubmitEmptyCommandBufferAsync(
            request.PreferredDeviceType,
            context.CancellationToken);
        var result = new SubmitEmptyVulkanCommandBufferResult(
            submission.Submitted,
            submission.LoaderName,
            submission.SelectedDevice,
            submission.CommandPoolCreated,
            submission.CommandBufferAllocated,
            submission.FenceSignaled,
            submission.Errors);

        if (submission.Submitted)
        {
            return RekallAgeCommandResult<SubmitEmptyVulkanCommandBufferResult>.Success(
                result,
                $"Submitted empty Vulkan command buffer on '{submission.SelectedDevice!.Name}'.");
        }

        return RekallAgeCommandResult<SubmitEmptyVulkanCommandBufferResult>.Failure(
            result,
            "Vulkan command buffer submission failed.",
            [
                new RekallAgeCommandError(
                    "REKALL_VULKAN_COMMAND_SUBMIT_FAILED",
                    submission.Errors.Count == 0
                        ? "Vulkan command buffer submission failed."
                        : string.Join(" ", submission.Errors),
                    "vulkan")
            ]);
    }
}
