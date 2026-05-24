using Rekall.Age.Core.Commands;

namespace Rekall.Age.Rendering.Commands;

public sealed record ListRenderBackendsRequest;

public sealed record ListRenderBackendsResult(IReadOnlyList<RekallAgeRenderBackendDescriptor> Backends);

public sealed class ListRenderBackendsCommand : IRekallAgeCommand<ListRenderBackendsRequest, ListRenderBackendsResult>
{
    public string Name => "rekall.render.backends";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Lists Rekall AGE rendering backends and low-level capabilities exposed to agents.",
        typeof(ListRenderBackendsRequest).FullName!,
        typeof(ListRenderBackendsResult).FullName!);

    public ValueTask<RekallAgeCommandResult<ListRenderBackendsResult>> ExecuteAsync(
        ListRenderBackendsRequest request,
        RekallAgeCommandContext context)
    {
        var catalog = RekallAgeRenderBackendCatalog.CreateDefault();
        return ValueTask.FromResult(RekallAgeCommandResult<ListRenderBackendsResult>.Success(
            new ListRenderBackendsResult(catalog.Backends),
            $"Loaded {catalog.Backends.Count} render backend descriptor(s)."));
    }
}
