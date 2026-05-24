using Rekall.Age.Core.Commands;

namespace Rekall.Age.Assets.Commands;

public sealed record ListAssetsRequest(string ProjectRoot, string? Kind = null);

public sealed record ListAssetsResult(IReadOnlyList<RekallAgeAssetDocument> Assets);

public sealed class ListAssetsCommand : IRekallAgeCommand<ListAssetsRequest, ListAssetsResult>
{
    private readonly RekallAgeAssetCatalogStore _store = new();

    public string Name => "rekall.asset.list";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Lists imported project assets from the Rekall AGE asset catalog.",
        typeof(ListAssetsRequest).FullName!,
        typeof(ListAssetsResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ListAssetsResult>> ExecuteAsync(
        ListAssetsRequest request,
        RekallAgeCommandContext context)
    {
        var catalog = await _store.LoadAsync(request.ProjectRoot, context.CancellationToken);
        var assets = catalog.Assets
            .Where(asset => request.Kind is null || asset.Kind.Equals(request.Kind, StringComparison.Ordinal))
            .OrderBy(asset => asset.Kind, StringComparer.Ordinal)
            .ThenBy(asset => asset.Name, StringComparer.Ordinal)
            .ToArray();

        return RekallAgeCommandResult<ListAssetsResult>.Success(
            new ListAssetsResult(assets),
            $"Loaded {assets.Length} asset(s).");
    }
}
