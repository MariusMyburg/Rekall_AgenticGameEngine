using Rekall.Age.Core.Commands;

namespace Rekall.Age.Rendering.Commands;

public sealed record CreateBoundVulkanImageRequest(
    uint Width = 64,
    uint Height = 64,
    string Format = "R8G8B8A8_UNorm",
    string Usage = "color-attachment",
    string? PreferredDeviceType = "discrete-gpu");

public sealed record CreateBoundVulkanImageResult(
    bool Created,
    string? LoaderName,
    RekallAgeVulkanSelectedDevice? SelectedDevice,
    uint Width,
    uint Height,
    string Format,
    string Usage,
    uint? MemoryTypeIndex,
    IReadOnlyList<string> MemoryProperties,
    bool Bound,
    IReadOnlyList<string> Errors);

public sealed class CreateBoundVulkanImageCommand
    : IRekallAgeCommand<CreateBoundVulkanImageRequest, CreateBoundVulkanImageResult>
{
    private readonly IRekallAgeVulkanImageSmoke _smoke;

    public CreateBoundVulkanImageCommand()
        : this(new RekallAgeNativeVulkanImageSmoke())
    {
    }

    public CreateBoundVulkanImageCommand(IRekallAgeVulkanImageSmoke smoke)
    {
        _smoke = smoke;
    }

    public string Name => "rekall.render.vulkan.image.create_bound";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Creates a native Vulkan image, allocates suitable memory, binds it, and cleans it up.",
        typeof(CreateBoundVulkanImageRequest).FullName!,
        typeof(CreateBoundVulkanImageResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CreateBoundVulkanImageResult>> ExecuteAsync(
        CreateBoundVulkanImageRequest request,
        RekallAgeCommandContext context)
    {
        var smoke = await _smoke.CreateBoundImageAsync(
            request.Width,
            request.Height,
            request.Format,
            request.Usage,
            request.PreferredDeviceType,
            context.CancellationToken);
        var result = new CreateBoundVulkanImageResult(
            smoke.Created,
            smoke.LoaderName,
            smoke.SelectedDevice,
            smoke.Width,
            smoke.Height,
            smoke.Format,
            smoke.Usage,
            smoke.MemoryTypeIndex,
            smoke.MemoryProperties,
            smoke.Bound,
            smoke.Errors);

        if (smoke.Created)
        {
            return RekallAgeCommandResult<CreateBoundVulkanImageResult>.Success(
                result,
                $"Created bound Vulkan {smoke.Usage} image on '{smoke.SelectedDevice!.Name}'.");
        }

        return RekallAgeCommandResult<CreateBoundVulkanImageResult>.Failure(
            result,
            "Vulkan image creation failed.",
            [
                new RekallAgeCommandError(
                    "REKALL_VULKAN_IMAGE_CREATE_FAILED",
                    smoke.Errors.Count == 0 ? "Vulkan image creation failed." : string.Join(" ", smoke.Errors),
                    "vulkan")
            ]);
    }
}
