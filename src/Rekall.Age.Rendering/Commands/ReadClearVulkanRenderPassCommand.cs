using Rekall.Age.Core.Commands;

namespace Rekall.Age.Rendering.Commands;

public sealed record ReadClearVulkanRenderPassRequest(
    uint Width = 64,
    uint Height = 64,
    string Format = "R8G8B8A8_UNorm",
    string? PreferredDeviceType = "discrete-gpu",
    RekallAgeVulkanClearColor? ClearColor = null);

public sealed record ReadClearVulkanRenderPassResult(
    bool Readback,
    string? LoaderName,
    RekallAgeVulkanSelectedDevice? SelectedDevice,
    uint Width,
    uint Height,
    string Format,
    RekallAgeVulkanClearColor ClearColor,
    bool Submitted,
    bool BufferCreated,
    bool BufferBound,
    bool BufferMapped,
    ulong BytesRead,
    ulong NonZeroBytes,
    RekallAgeVulkanReadbackPixel FirstPixel,
    ulong ByteChecksum,
    IReadOnlyList<string> Errors);

public sealed class ReadClearVulkanRenderPassCommand
    : IRekallAgeCommand<ReadClearVulkanRenderPassRequest, ReadClearVulkanRenderPassResult>
{
    private readonly IRekallAgeVulkanRenderPassReadback _readback;

    public ReadClearVulkanRenderPassCommand()
        : this(new RekallAgeNativeVulkanRenderPassSubmission())
    {
    }

    public ReadClearVulkanRenderPassCommand(IRekallAgeVulkanRenderPassReadback readback)
    {
        _readback = readback;
    }

    public string Name => "rekall.render.vulkan.render_pass.read_clear";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Creates a native Vulkan clear render pass, copies the image to a host-visible buffer, and returns readback diagnostics.",
        typeof(ReadClearVulkanRenderPassRequest).FullName!,
        typeof(ReadClearVulkanRenderPassResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ReadClearVulkanRenderPassResult>> ExecuteAsync(
        ReadClearVulkanRenderPassRequest request,
        RekallAgeCommandContext context)
    {
        var readback = await _readback.ReadClearRenderPassAsync(
            request.Width,
            request.Height,
            request.Format,
            request.PreferredDeviceType,
            RekallAgeVulkanClearColor.Normalize(request.ClearColor),
            context.CancellationToken);
        var result = new ReadClearVulkanRenderPassResult(
            readback.Readback,
            readback.LoaderName,
            readback.SelectedDevice,
            readback.Width,
            readback.Height,
            readback.Format,
            readback.ClearColor,
            readback.Submitted,
            readback.BufferCreated,
            readback.BufferBound,
            readback.BufferMapped,
            readback.BytesRead,
            readback.NonZeroBytes,
            readback.FirstPixel,
            readback.ByteChecksum,
            readback.Errors);

        if (readback.Readback)
        {
            return RekallAgeCommandResult<ReadClearVulkanRenderPassResult>.Success(
                result,
                $"Read back Vulkan clear render pass on '{readback.SelectedDevice!.Name}'.");
        }

        return RekallAgeCommandResult<ReadClearVulkanRenderPassResult>.Failure(
            result,
            "Vulkan clear render pass readback failed.",
            [
                new RekallAgeCommandError(
                    "REKALL_VULKAN_RENDER_PASS_READBACK_FAILED",
                    readback.Errors.Count == 0
                        ? "Vulkan clear render pass readback failed."
                        : string.Join(" ", readback.Errors),
                    "vulkan")
            ]);
    }
}
