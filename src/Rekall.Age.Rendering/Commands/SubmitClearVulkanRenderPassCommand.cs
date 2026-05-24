using Rekall.Age.Core.Commands;

namespace Rekall.Age.Rendering.Commands;

public sealed record SubmitClearVulkanRenderPassRequest(
    uint Width = 128,
    uint Height = 72,
    string Format = "R8G8B8A8_UNorm",
    string? PreferredDeviceType = "discrete-gpu",
    RekallAgeVulkanClearColor? ClearColor = null);

public sealed record SubmitClearVulkanRenderPassResult(
    bool Submitted,
    string? LoaderName,
    RekallAgeVulkanSelectedDevice? SelectedDevice,
    uint Width,
    uint Height,
    string Format,
    bool ImageCreated,
    bool ImageViewCreated,
    bool RenderPassCreated,
    bool FramebufferCreated,
    bool CommandPoolCreated,
    bool CommandBufferAllocated,
    bool RenderPassBegan,
    bool RenderPassEnded,
    bool FenceSignaled,
    RekallAgeVulkanClearColor ClearColor,
    IReadOnlyList<string> Errors);

public sealed class SubmitClearVulkanRenderPassCommand
    : IRekallAgeCommand<SubmitClearVulkanRenderPassRequest, SubmitClearVulkanRenderPassResult>
{
    private readonly IRekallAgeVulkanRenderPassSubmission _submission;

    public SubmitClearVulkanRenderPassCommand()
        : this(new RekallAgeNativeVulkanRenderPassSubmission())
    {
    }

    public SubmitClearVulkanRenderPassCommand(IRekallAgeVulkanRenderPassSubmission submission)
    {
        _submission = submission;
    }

    public string Name => "rekall.render.vulkan.render_pass.submit_clear";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Creates a native Vulkan offscreen render target, records a clear render pass, submits it, and waits for GPU completion.",
        typeof(SubmitClearVulkanRenderPassRequest).FullName!,
        typeof(SubmitClearVulkanRenderPassResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<SubmitClearVulkanRenderPassResult>> ExecuteAsync(
        SubmitClearVulkanRenderPassRequest request,
        RekallAgeCommandContext context)
    {
        var submission = await _submission.SubmitClearRenderPassAsync(
            request.Width,
            request.Height,
            request.Format,
            request.PreferredDeviceType,
            RekallAgeVulkanClearColor.Normalize(request.ClearColor),
            context.CancellationToken);
        var result = new SubmitClearVulkanRenderPassResult(
            submission.Submitted,
            submission.LoaderName,
            submission.SelectedDevice,
            submission.Width,
            submission.Height,
            submission.Format,
            submission.ImageCreated,
            submission.ImageViewCreated,
            submission.RenderPassCreated,
            submission.FramebufferCreated,
            submission.CommandPoolCreated,
            submission.CommandBufferAllocated,
            submission.RenderPassBegan,
            submission.RenderPassEnded,
            submission.FenceSignaled,
            submission.ClearColor,
            submission.Errors);

        if (submission.Submitted)
        {
            return RekallAgeCommandResult<SubmitClearVulkanRenderPassResult>.Success(
                result,
                $"Submitted Vulkan clear render pass on '{submission.SelectedDevice!.Name}'.");
        }

        return RekallAgeCommandResult<SubmitClearVulkanRenderPassResult>.Failure(
            result,
            "Vulkan clear render pass submission failed.",
            [
                new RekallAgeCommandError(
                    "REKALL_VULKAN_RENDER_PASS_SUBMIT_FAILED",
                    submission.Errors.Count == 0
                        ? "Vulkan clear render pass submission failed."
                        : string.Join(" ", submission.Errors),
                    "vulkan")
            ]);
    }
}
