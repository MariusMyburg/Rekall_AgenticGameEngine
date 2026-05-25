using Rekall.Age.Assets;
using Rekall.Age.Core.Commands;

namespace Rekall.Age.AssetPipeline.Commands;

public sealed record ImportAssetWithReportRequest(
    string ProjectRoot,
    string SourcePath,
    string Kind,
    string? DisplayName = null);

public sealed record ImportAssetWithReportResult(
    RekallAgeAssetImportReport Report,
    RekallAgeAssetPipelineDocument Pipeline);

public sealed class ImportAssetWithReportCommand
    : IRekallAgeCommand<ImportAssetWithReportRequest, ImportAssetWithReportResult>
{
    private readonly RekallAgeAssetCatalogStore _assetStore = new();
    private readonly RekallAgeAssetPipelineStore _pipelineStore = new();

    public string Name => "rekall.asset.import_report";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Imports an asset and writes editor-facing source/imported/cooked pipeline records.",
        typeof(ImportAssetWithReportRequest).FullName!,
        typeof(ImportAssetWithReportResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ImportAssetWithReportResult>> ExecuteAsync(
        ImportAssetWithReportRequest request,
        RekallAgeCommandContext context)
    {
        var asset = await RekallAgeAssetImporter.ImportAsync(
            request.ProjectRoot,
            request.SourcePath,
            request.Kind,
            request.DisplayName,
            context.CancellationToken);
        var catalog = await _assetStore.LoadAsync(request.ProjectRoot, context.CancellationToken);
        await _assetStore.SaveAsync(request.ProjectRoot, catalog.AddOrReplace(asset), context.CancellationToken);

        var pipeline = await _pipelineStore.LoadAsync(request.ProjectRoot, context.CancellationToken);
        var updatedPipeline = pipeline.AddImport(asset, request.SourcePath, request.Kind);
        await _pipelineStore.SaveAsync(request.ProjectRoot, updatedPipeline, context.CancellationToken);

        context.Transaction.RecordChangedResource(asset.ImportedPath);
        context.Transaction.RecordChangedResource(_assetStore.GetCatalogPath(request.ProjectRoot));
        context.Transaction.RecordChangedResource(_pipelineStore.GetPath(request.ProjectRoot));

        var report = new RekallAgeAssetImportReport(
            true,
            asset.Id,
            asset.Kind,
            asset.SourcePath,
            asset.ImportedPath,
            Array.Empty<string>());
        return RekallAgeCommandResult<ImportAssetWithReportResult>.Success(
            new ImportAssetWithReportResult(report, updatedPipeline),
            $"Imported asset '{asset.Id}' with pipeline report.");
    }
}
