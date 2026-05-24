using Rekall.Age.Core.Commands;

namespace Rekall.Age.Rendering.Commands;

public sealed record RecordRenderCommandBufferRequest(
    string ProjectRoot,
    string Id,
    string Queue,
    IReadOnlyList<RekallAgeRenderCommand> Commands);

public sealed record RecordRenderCommandBufferResult(RekallAgeRenderPlanDocument Plan);

public sealed class RecordRenderCommandBufferCommand
    : IRekallAgeCommand<RecordRenderCommandBufferRequest, RecordRenderCommandBufferResult>
{
    private readonly RekallAgeRenderPlanStore _store = new();

    public string Name => "rekall.render.command_buffer.record";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Records a low-level render command buffer in backend-neutral Vulkan-style operations.",
        typeof(RecordRenderCommandBufferRequest).FullName!,
        typeof(RecordRenderCommandBufferResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<RecordRenderCommandBufferResult>> ExecuteAsync(
        RecordRenderCommandBufferRequest request,
        RekallAgeCommandContext context)
    {
        var plan = await _store.LoadAsync(request.ProjectRoot, context.CancellationToken);
        var updated = plan.RecordCommandBuffer(RekallAgeRenderCommandBuffer.Create(
            request.Id,
            request.Queue,
            request.Commands));
        await _store.SaveAsync(request.ProjectRoot, updated, context.CancellationToken);
        context.Transaction.RecordChangedResource(_store.GetPlanPath(request.ProjectRoot));
        return RekallAgeCommandResult<RecordRenderCommandBufferResult>.Success(
            new RecordRenderCommandBufferResult(updated),
            $"Recorded command buffer '{request.Id}'.");
    }
}
