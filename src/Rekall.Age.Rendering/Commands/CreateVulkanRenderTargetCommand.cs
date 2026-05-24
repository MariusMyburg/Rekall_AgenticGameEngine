using Rekall.Age.Core.Commands;

namespace Rekall.Age.Rendering.Commands;

public sealed record CreateVulkanRenderTargetRequest(
    uint Width = 128,
    uint Height = 72,
    string Format = "R8G8B8A8_UNorm",
    string? PreferredDeviceType = "discrete-gpu");

public sealed record CreateVulkanRenderTargetResult(
    bool Created,
    string? LoaderName,
    RekallAgeVulkanSelectedDevice? SelectedDevice,
    uint Width,
    uint Height,
    string Format,
    bool ImageCreated,
    bool ImageViewCreated,
    bool RenderPassCreated,
    bool FramebufferCreated,
    IReadOnlyList<string> Errors);

public sealed class CreateVulkanRenderTargetCommand
    : IRekallAgeCommand<CreateVulkanRenderTargetRequest, CreateVulkanRenderTargetResult>
{
    private readonly IRekallAgeVulkanRenderTargetSmoke _smoke;

    public CreateVulkanRenderTargetCommand()
        : this(new RekallAgeNativeVulkanRenderTargetSmoke())
    {
    }

    public CreateVulkanRenderTargetCommand(IRekallAgeVulkanRenderTargetSmoke smoke)
    {
        _smoke = smoke;
    }

    public string Name => "rekall.render.vulkan.render_target.create";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Creates a native Vulkan offscreen color render target, image view, render pass, and framebuffer.",
        typeof(CreateVulkanRenderTargetRequest).FullName!,
        typeof(CreateVulkanRenderTargetResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CreateVulkanRenderTargetResult>> ExecuteAsync(
        CreateVulkanRenderTargetRequest request,
        RekallAgeCommandContext context)
    {
        var smoke = await _smoke.CreateRenderTargetAsync(
            request.Width,
            request.Height,
            request.Format,
            request.PreferredDeviceType,
            context.CancellationToken);
        var result = new CreateVulkanRenderTargetResult(
            smoke.Created,
            smoke.LoaderName,
            smoke.SelectedDevice,
            smoke.Width,
            smoke.Height,
            smoke.Format,
            smoke.ImageCreated,
            smoke.ImageViewCreated,
            smoke.RenderPassCreated,
            smoke.FramebufferCreated,
            smoke.Errors);

        if (smoke.Created)
        {
            return RekallAgeCommandResult<CreateVulkanRenderTargetResult>.Success(
                result,
                $"Created Vulkan render target on '{smoke.SelectedDevice!.Name}'.");
        }

        return RekallAgeCommandResult<CreateVulkanRenderTargetResult>.Failure(
            result,
            "Vulkan render target creation failed.",
            [
                new RekallAgeCommandError(
                    "REKALL_VULKAN_RENDER_TARGET_CREATE_FAILED",
                    smoke.Errors.Count == 0 ? "Vulkan render target creation failed." : string.Join(" ", smoke.Errors),
                    "vulkan")
            ]);
    }
}
