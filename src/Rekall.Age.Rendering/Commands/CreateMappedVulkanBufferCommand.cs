using Rekall.Age.Core.Commands;

namespace Rekall.Age.Rendering.Commands;

public sealed record CreateMappedVulkanBufferRequest(
    ulong SizeBytes = 256,
    string Usage = "vertex-buffer",
    string? PreferredDeviceType = "discrete-gpu");

public sealed record CreateMappedVulkanBufferResult(
    bool Created,
    string? LoaderName,
    RekallAgeVulkanSelectedDevice? SelectedDevice,
    ulong SizeBytes,
    string Usage,
    uint? MemoryTypeIndex,
    IReadOnlyList<string> MemoryProperties,
    bool Bound,
    bool Mapped,
    int BytesWritten,
    IReadOnlyList<string> Errors);

public sealed class CreateMappedVulkanBufferCommand
    : IRekallAgeCommand<CreateMappedVulkanBufferRequest, CreateMappedVulkanBufferResult>
{
    private readonly IRekallAgeVulkanBufferSmoke _smoke;

    public CreateMappedVulkanBufferCommand()
        : this(new RekallAgeNativeVulkanBufferSmoke())
    {
    }

    public CreateMappedVulkanBufferCommand(IRekallAgeVulkanBufferSmoke smoke)
    {
        _smoke = smoke;
    }

    public string Name => "rekall.render.vulkan.buffer.create_mapped";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Creates a native Vulkan buffer, allocates host-visible memory, maps it, writes bytes, and cleans it up.",
        typeof(CreateMappedVulkanBufferRequest).FullName!,
        typeof(CreateMappedVulkanBufferResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CreateMappedVulkanBufferResult>> ExecuteAsync(
        CreateMappedVulkanBufferRequest request,
        RekallAgeCommandContext context)
    {
        var smoke = await _smoke.CreateMappedBufferAsync(
            request.SizeBytes,
            request.Usage,
            request.PreferredDeviceType,
            context.CancellationToken);
        var result = new CreateMappedVulkanBufferResult(
            smoke.Created,
            smoke.LoaderName,
            smoke.SelectedDevice,
            smoke.SizeBytes,
            smoke.Usage,
            smoke.MemoryTypeIndex,
            smoke.MemoryProperties,
            smoke.Bound,
            smoke.Mapped,
            smoke.BytesWritten,
            smoke.Errors);

        if (smoke.Created)
        {
            return RekallAgeCommandResult<CreateMappedVulkanBufferResult>.Success(
                result,
                $"Created mapped Vulkan {smoke.Usage} buffer on '{smoke.SelectedDevice!.Name}'.");
        }

        return RekallAgeCommandResult<CreateMappedVulkanBufferResult>.Failure(
            result,
            "Vulkan mapped buffer creation failed.",
            [
                new RekallAgeCommandError(
                    "REKALL_VULKAN_BUFFER_CREATE_FAILED",
                    smoke.Errors.Count == 0 ? "Vulkan mapped buffer creation failed." : string.Join(" ", smoke.Errors),
                    "vulkan")
            ]);
    }
}
