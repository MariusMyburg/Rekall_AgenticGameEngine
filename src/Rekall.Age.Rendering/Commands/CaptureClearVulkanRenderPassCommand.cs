using Rekall.Age.Core.Commands;

namespace Rekall.Age.Rendering.Commands;

public sealed record CaptureClearVulkanRenderPassRequest(
    uint Width = 64,
    uint Height = 64,
    string Format = "R8G8B8A8_UNorm",
    string? PreferredDeviceType = "discrete-gpu",
    string OutputDirectory = "Artifacts/Vulkan",
    RekallAgeVulkanClearColor? ClearColor = null);

public sealed record CaptureClearVulkanRenderPassResult(
    bool Captured,
    string OutputPath,
    string? LoaderName,
    RekallAgeVulkanSelectedDevice? SelectedDevice,
    uint Width,
    uint Height,
    string Format,
    RekallAgeVulkanClearColor ClearColor,
    ulong BytesRead,
    ulong NonZeroBytes,
    RekallAgeVulkanReadbackPixel FirstPixel,
    ulong ByteChecksum,
    IReadOnlyList<string> Errors);

public sealed class CaptureClearVulkanRenderPassCommand
    : IRekallAgeCommand<CaptureClearVulkanRenderPassRequest, CaptureClearVulkanRenderPassResult>
{
    private readonly IRekallAgeVulkanRenderPassCapture _capture;

    public CaptureClearVulkanRenderPassCommand()
        : this(new RekallAgeNativeVulkanRenderPassSubmission())
    {
    }

    public CaptureClearVulkanRenderPassCommand(IRekallAgeVulkanRenderPassCapture capture)
    {
        _capture = capture;
    }

    public string Name => "rekall.render.vulkan.render_pass.capture_clear";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Captures a native Vulkan clear render pass to a PNG artifact using GPU readback bytes.",
        typeof(CaptureClearVulkanRenderPassRequest).FullName!,
        typeof(CaptureClearVulkanRenderPassResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CaptureClearVulkanRenderPassResult>> ExecuteAsync(
        CaptureClearVulkanRenderPassRequest request,
        RekallAgeCommandContext context)
    {
        var capture = await _capture.CaptureClearRenderPassAsync(
            request.Width,
            request.Height,
            request.Format,
            request.PreferredDeviceType,
            request.OutputDirectory,
            RekallAgeVulkanClearColor.Normalize(request.ClearColor),
            context.CancellationToken);
        var result = new CaptureClearVulkanRenderPassResult(
            capture.Captured,
            capture.OutputPath,
            capture.LoaderName,
            capture.SelectedDevice,
            capture.Width,
            capture.Height,
            capture.Format,
            capture.ClearColor,
            capture.BytesRead,
            capture.NonZeroBytes,
            capture.FirstPixel,
            capture.ByteChecksum,
            capture.Errors);

        if (capture.Captured)
        {
            return RekallAgeCommandResult<CaptureClearVulkanRenderPassResult>.Success(
                result,
                $"Captured Vulkan clear render pass to '{capture.OutputPath}'.");
        }

        return RekallAgeCommandResult<CaptureClearVulkanRenderPassResult>.Failure(
            result,
            "Vulkan clear render pass capture failed.",
            [
                new RekallAgeCommandError(
                    "REKALL_VULKAN_RENDER_PASS_CAPTURE_FAILED",
                    capture.Errors.Count == 0
                        ? "Vulkan clear render pass capture failed."
                        : string.Join(" ", capture.Errors),
                    "vulkan")
            ]);
    }
}
