using Rekall.Age.Core.Commands;

namespace Rekall.Age.Assets.Commands;

public sealed record ImportAssetRequest(
    string ProjectRoot,
    string SourcePath,
    string Kind,
    string? DisplayName = null);

public sealed record ImportAssetResult(RekallAgeAssetDocument Asset);

public sealed class ImportAssetCommand : IRekallAgeCommand<ImportAssetRequest, ImportAssetResult>
{
    private readonly RekallAgeAssetCatalogStore _store = new();

    public string Name => "rekall.asset.import";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Imports an asset file into the project asset catalog with a stable asset id.",
        typeof(ImportAssetRequest).FullName!,
        typeof(ImportAssetResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ImportAssetResult>> ExecuteAsync(
        ImportAssetRequest request,
        RekallAgeCommandContext context)
    {
        var asset = await RekallAgeAssetImporter.ImportAsync(
            request.ProjectRoot,
            request.SourcePath,
            request.Kind,
            request.DisplayName,
            context.CancellationToken);
        var catalog = await _store.LoadAsync(request.ProjectRoot, context.CancellationToken);
        await _store.SaveAsync(
            request.ProjectRoot,
            catalog.AddOrReplace(asset),
            context.CancellationToken);
        context.Transaction.RecordChangedResource(asset.ImportedPath);
        context.Transaction.RecordChangedResource(_store.GetCatalogPath(request.ProjectRoot));

        return RekallAgeCommandResult<ImportAssetResult>.Success(
            new ImportAssetResult(asset),
            $"Imported asset '{asset.Id}'.");
    }
}
