using System.Text.Json;
using Rekall.Age.Assets;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Modeling;

namespace Rekall.Age.AssetPipeline.Commands;

public sealed record PublishModelAssetRequest(
    string ProjectRoot,
    string AssetId,
    string DisplayName,
    RekallAgeModelSourceReference Source,
    string ExpectedModelFileRevision);

public sealed record RebuildModelAssetRequest(
    string ProjectRoot,
    string AssetId,
    string ExpectedModelFileRevision);

public sealed record InspectModelAssetRequest(string ProjectRoot, string AssetId);

public sealed record SetModelAssetFreezeRequest(
    string ProjectRoot,
    string AssetId,
    string ExpectedModelFileRevision);

public sealed record ModelAssetFreezeCommandResult(
    RekallAgeModelAssetDocument? Asset,
    string? ModelFileRevision,
    IReadOnlyList<string> NextActions);

public sealed record ModelAssetMutationCommandResult(
    RekallAgePublishModelResult? Publication,
    IReadOnlyList<string> NextActions);

public sealed record ModelAssetInspectionCommandResult(
    RekallAgeModelAssetInspection? Inspection,
    IReadOnlyList<string> NextActions);

public sealed record ListModelAssetsRequest(string ProjectRoot);

public sealed record ModelAssetListItem(
    string AssetId,
    string? ModelDocumentPath,
    RekallAgeModelBuildState BuildState,
    bool CompiledOutputExists,
    string? ActualCompiledContentHash,
    IReadOnlyList<RekallAgeModelBuildDiagnostic> Diagnostics,
    bool DiagnosticsTruncated);

public sealed record ListModelAssetsResult(
    IReadOnlyList<ModelAssetListItem> Assets,
    bool Truncated);

public sealed class PublishModelAssetCommand
    : IRekallAgeCommand<PublishModelAssetRequest, ModelAssetMutationCommandResult>
{
    private readonly RekallAgeModelPublishingService _service;

    public PublishModelAssetCommand()
        : this(new RekallAgeModelPublishingService())
    {
    }

    public PublishModelAssetCommand(RekallAgeModelPublishingService service) =>
        _service = service ?? throw new ArgumentNullException(nameof(service));

    public string Name => "rekall.asset.model.publish";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Creates or revision-checks a live-linked Model Asset while retaining the editable source. Requires expectedModelFileRevision ('missing' for first publish); source has exact shape { kind: Mesh, assetId, outputName? }. Compilation and staged-output validation complete before published files change, and failure retains the last successful output.",
        typeof(PublishModelAssetRequest).FullName!,
        typeof(ModelAssetMutationCommandResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ModelAssetMutationCommandResult>> ExecuteAsync(
        PublishModelAssetRequest request,
        RekallAgeCommandContext context)
    {
        if (request is null)
        {
            return ModelAssetCommandErrors.MutationFailure(
                new ArgumentNullException(nameof(request)),
                "request",
                "request");
        }

        try
        {
            var publication = await _service.PublishAsync(
                request.ProjectRoot,
                new(request.AssetId, request.DisplayName, request.Source, request.ExpectedModelFileRevision),
                context.Transaction,
                context.CancellationToken).ConfigureAwait(false);
            return RekallAgeCommandResult<ModelAssetMutationCommandResult>.Success(
                new(publication, ["rekall.asset.model.inspect", "rekall.asset.model.rebuild"]),
                $"Published live-linked Model Asset '{publication.Asset.AssetId}' at logical revision {publication.Asset.Revision}.");
        }
        catch (Exception error) when (ModelAssetCommandErrors.IsKnown(error))
        {
            return ModelAssetCommandErrors.MutationFailure(
                error,
                request.AssetId,
                request.Source?.AssetId ?? request.AssetId);
        }
    }
}

public sealed class RebuildModelAssetCommand
    : IRekallAgeCommand<RebuildModelAssetRequest, ModelAssetMutationCommandResult>
{
    private readonly RekallAgeModelPublishingService _service;

    public RebuildModelAssetCommand()
        : this(new RekallAgeModelPublishingService())
    {
    }

    public RebuildModelAssetCommand(RekallAgeModelPublishingService service) =>
        _service = service ?? throw new ArgumentNullException(nameof(service));

    public string Name => "rekall.asset.model.rebuild";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Rebuilds a live-linked editable-mesh Model Asset using the required expected Model Asset file revision. Source shape remains { kind: Mesh, assetId, outputName? }; compilation and staged validation precede replacement, frozen assets reject rebuild, and every failure retains the last successful compiled output and Model Asset revision.",
        typeof(RebuildModelAssetRequest).FullName!,
        typeof(ModelAssetMutationCommandResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ModelAssetMutationCommandResult>> ExecuteAsync(
        RebuildModelAssetRequest request,
        RekallAgeCommandContext context)
    {
        if (request is null)
        {
            return ModelAssetCommandErrors.MutationFailure(
                new ArgumentNullException(nameof(request)),
                "request",
                "request");
        }

        try
        {
            var publication = await _service.RebuildAsync(
                request.ProjectRoot,
                request.AssetId,
                request.ExpectedModelFileRevision,
                context.Transaction,
                context.CancellationToken).ConfigureAwait(false);
            return RekallAgeCommandResult<ModelAssetMutationCommandResult>.Success(
                new(publication, ["rekall.asset.model.inspect"]),
                $"Rebuilt live-linked Model Asset '{publication.Asset.AssetId}' at logical revision {publication.Asset.Revision}.");
        }
        catch (Exception error) when (ModelAssetCommandErrors.IsKnown(error))
        {
            return ModelAssetCommandErrors.MutationFailure(error, request.AssetId, request.AssetId);
        }
    }
}

public sealed class InspectModelAssetCommand
    : IRekallAgeCommand<InspectModelAssetRequest, ModelAssetInspectionCommandResult>
{
    private readonly RekallAgeModelPublishingService _service;

    public InspectModelAssetCommand()
        : this(new RekallAgeModelPublishingService())
    {
    }

    public InspectModelAssetCommand(RekallAgeModelPublishingService service) =>
        _service = service ?? throw new ArgumentNullException(nameof(service));

    public string Name => "rekall.asset.model.inspect";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Inspects a live-linked Model Asset without mutating source, output, Model Asset, catalog, or transaction. Compares exact source file/logical revisions and compiled hash to report Current, Stale, Frozen, Failed, or missing-source diagnostics and preserves the last successful output.",
        typeof(InspectModelAssetRequest).FullName!,
        typeof(ModelAssetInspectionCommandResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ModelAssetInspectionCommandResult>> ExecuteAsync(
        InspectModelAssetRequest request,
        RekallAgeCommandContext context)
    {
        if (request is null)
        {
            var requestError = ModelAssetCommandErrors.Map(
                new ArgumentNullException(nameof(request)),
                "request",
                "request");
            return RekallAgeCommandResult<ModelAssetInspectionCommandResult>.Failure(
                new(null, ["rekall.asset.model.publish", "rekall.mesh.inspect"]),
                requestError.Message,
                [requestError]);
        }

        try
        {
            var inspection = await _service.InspectAsync(
                request.ProjectRoot,
                request.AssetId,
                context.CancellationToken).ConfigureAwait(false);
            return RekallAgeCommandResult<ModelAssetInspectionCommandResult>.Success(
                new(
                    inspection,
                    inspection.BuildState is RekallAgeModelBuildState.Stale or RekallAgeModelBuildState.Failed
                        ? ["rekall.asset.model.rebuild", "rekall.mesh.inspect"]
                        : ["rekall.mesh.inspect"]),
                $"Model Asset '{request.AssetId}' build state is {inspection.BuildState}.");
        }
        catch (Exception error) when (ModelAssetCommandErrors.IsKnown(error))
        {
            var mapped = ModelAssetCommandErrors.Map(error, request.AssetId, request.AssetId);
            return RekallAgeCommandResult<ModelAssetInspectionCommandResult>.Failure(
                new(null, ["rekall.asset.model.publish", "rekall.mesh.inspect"]),
                mapped.Message,
                [mapped]);
        }
    }
}

public sealed class ListModelAssetsCommand
    : IRekallAgeCommand<ListModelAssetsRequest, ListModelAssetsResult>
{
    public const int MaximumAssets = 8;
    private const int MaximumDiagnosticsPerAsset = 4;
    private readonly RekallAgeModelAssetStore _modelStore;
    private readonly RekallAgeModelPublishingService _service;

    public ListModelAssetsCommand()
        : this(new RekallAgeModelAssetStore(), new RekallAgeModelPublishingService())
    {
    }

    public ListModelAssetsCommand(
        RekallAgeModelAssetStore modelStore,
        RekallAgeModelPublishingService service)
    {
        _modelStore = modelStore ?? throw new ArgumentNullException(nameof(modelStore));
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public string Name => "rekall.asset.model.list";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Lists up to 8 live-linked Model Assets in deterministic asset-ID order. Each compact item reports non-mutating dependency health, retained-output evidence, and bounded diagnostics without embedding the Model Asset document.",
        typeof(ListModelAssetsRequest).FullName!,
        typeof(ListModelAssetsResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ListModelAssetsResult>> ExecuteAsync(
        ListModelAssetsRequest request,
        RekallAgeCommandContext context)
    {
        if (request is null)
        {
            return Failure(new ArgumentNullException(nameof(request)), "request");
        }

        try
        {
            var assetIds = _modelStore.ListAssetIds(request.ProjectRoot);
            var selectedAssetIds = assetIds.Take(MaximumAssets + 1).ToArray();
            var assets = new List<ModelAssetListItem>(Math.Min(selectedAssetIds.Length, MaximumAssets));
            foreach (var assetId in selectedAssetIds.Take(MaximumAssets))
            {
                assets.Add(await InspectItemAsync(request.ProjectRoot, assetId, context.CancellationToken).ConfigureAwait(false));
            }

            return RekallAgeCommandResult<ListModelAssetsResult>.Success(
                new(assets, selectedAssetIds.Length > MaximumAssets),
                $"Loaded {assets.Count} of {assetIds.Count} Model Asset(s).");
        }
        catch (Exception error) when (ModelAssetCommandErrors.IsKnown(error))
        {
            return Failure(error, request.ProjectRoot);
        }
    }

    private async ValueTask<ModelAssetListItem> InspectItemAsync(
        string projectRoot,
        string assetId,
        CancellationToken cancellationToken)
    {
        string? documentPath = null;
        try
        {
            documentPath = ToProjectRelativePath(projectRoot, _modelStore.GetModelPath(projectRoot, assetId));
            var inspection = await _service.InspectAsync(projectRoot, assetId, cancellationToken).ConfigureAwait(false);
            var diagnostics = inspection.Diagnostics.Take(MaximumDiagnosticsPerAsset).ToArray();
            return new(
                assetId,
                documentPath,
                inspection.BuildState,
                inspection.CompiledOutputExists,
                inspection.ActualCompiledContentHash,
                diagnostics,
                diagnostics.Length < inspection.Diagnostics.Count);
        }
        catch (Exception error) when (ModelAssetCommandErrors.IsKnown(error))
        {
            var mapped = ModelAssetCommandErrors.Map(error, assetId, assetId);
            return new(
                assetId,
                documentPath,
                RekallAgeModelBuildState.Failed,
                false,
                null,
                [new(mapped.Code, "Error", mapped.Message, mapped.Target)],
                false);
        }
    }

    private static RekallAgeCommandResult<ListModelAssetsResult> Failure(Exception error, string target)
    {
        var mapped = ModelAssetCommandErrors.Map(error, target, target);
        return RekallAgeCommandResult<ListModelAssetsResult>.Failure(
            new([], false),
            mapped.Message,
            [mapped]);
    }

    private static string ToProjectRelativePath(string projectRoot, string path) =>
        Path.GetRelativePath(Path.GetFullPath(projectRoot), Path.GetFullPath(path)).Replace('\\', '/');
}

public sealed class FreezeModelAssetCommand
    : IRekallAgeCommand<SetModelAssetFreezeRequest, ModelAssetFreezeCommandResult>
{
    private readonly RekallAgeModelPublishingService _service;
    public FreezeModelAssetCommand(RekallAgeModelPublishingService service) =>
        _service = service ?? throw new ArgumentNullException(nameof(service));
    public string Name => "rekall.asset.model.freeze";
    public RekallAgeCommandSchema Schema => new(
        Name,
        "Revision-checks and freezes a Model Asset at its last validated immutable compiled output. Frozen placement remains independent of the editable source while compiled structure, content hash, and provenance are still validated.",
        typeof(SetModelAssetFreezeRequest).FullName!,
        typeof(ModelAssetFreezeCommandResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ModelAssetFreezeCommandResult>> ExecuteAsync(
        SetModelAssetFreezeRequest request,
        RekallAgeCommandContext context)
    {
        if (request is null)
        {
            var mapped = ModelAssetCommandErrors.Map(new ArgumentNullException(nameof(request)), "request", "request");
            return RekallAgeCommandResult<ModelAssetFreezeCommandResult>.Failure(
                new(null, null, ["rekall.asset.model.inspect"]), mapped.Message, [mapped]);
        }

        try
        {
            var result = await _service.FreezeAsync(
                request.ProjectRoot, request.AssetId, request.ExpectedModelFileRevision,
                context.Transaction, context.CancellationToken).ConfigureAwait(false);
            return RekallAgeCommandResult<ModelAssetFreezeCommandResult>.Success(
                new(result.Value, result.Revision, ["rekall.asset.model.inspect", "rekall.asset.model.unfreeze"]),
                $"Froze Model Asset '{request.AssetId}' at revision {result.Value.Revision}.");
        }
        catch (Exception error) when (ModelAssetCommandErrors.IsKnown(error))
        {
            var mapped = ModelAssetCommandErrors.Map(error, request.AssetId, request.AssetId);
            return RekallAgeCommandResult<ModelAssetFreezeCommandResult>.Failure(
                new(null, null, ["rekall.asset.model.inspect"]), mapped.Message, [mapped]);
        }
    }
}

public sealed class UnfreezeModelAssetCommand
    : IRekallAgeCommand<SetModelAssetFreezeRequest, ModelAssetFreezeCommandResult>
{
    private readonly RekallAgeModelPublishingService _service;
    public UnfreezeModelAssetCommand(RekallAgeModelPublishingService service) =>
        _service = service ?? throw new ArgumentNullException(nameof(service));
    public string Name => "rekall.asset.model.unfreeze";
    public RekallAgeCommandSchema Schema => new(
        Name,
        "Revision-checks and returns a frozen Model Asset to live linking. A valid editable source is required; exact source/compiler revisions determine Current or Stale health without rebuilding.",
        typeof(SetModelAssetFreezeRequest).FullName!,
        typeof(ModelAssetFreezeCommandResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ModelAssetFreezeCommandResult>> ExecuteAsync(
        SetModelAssetFreezeRequest request,
        RekallAgeCommandContext context)
    {
        if (request is null)
        {
            var mapped = ModelAssetCommandErrors.Map(new ArgumentNullException(nameof(request)), "request", "request");
            return RekallAgeCommandResult<ModelAssetFreezeCommandResult>.Failure(
                new(null, null, ["rekall.asset.model.inspect"]), mapped.Message, [mapped]);
        }

        try
        {
            var result = await _service.UnfreezeAsync(
                request.ProjectRoot, request.AssetId, request.ExpectedModelFileRevision,
                context.Transaction, context.CancellationToken).ConfigureAwait(false);
            return RekallAgeCommandResult<ModelAssetFreezeCommandResult>.Success(
                new(result.Value, result.Revision, ["rekall.asset.model.inspect", "rekall.asset.model.rebuild"]),
                $"Unfroze Model Asset '{request.AssetId}' with {result.Value.BuildState} health.");
        }
        catch (Exception error) when (ModelAssetCommandErrors.IsKnown(error))
        {
            var mapped = ModelAssetCommandErrors.Map(error, request.AssetId, request.AssetId);
            return RekallAgeCommandResult<ModelAssetFreezeCommandResult>.Failure(
                new(null, null, ["rekall.asset.model.inspect"]), mapped.Message, [mapped]);
        }
    }
}

internal static class ModelAssetCommandErrors
{
    public static bool IsKnown(Exception error) =>
        error is RekallAgeModelPublishingException
            or RekallAgeDocumentRevisionException
            or RekallAgeMeshCompileException
            or JsonException
            or InvalidDataException
            or FileNotFoundException
            or DirectoryNotFoundException
            or UnauthorizedAccessException
            or IOException
            or ArgumentException;

    public static RekallAgeCommandResult<ModelAssetMutationCommandResult> MutationFailure(
        Exception error,
        string assetId,
        string sourceAssetId)
    {
        var mapped = Map(error, assetId, sourceAssetId);
        return RekallAgeCommandResult<ModelAssetMutationCommandResult>.Failure(
            new(null, ["rekall.asset.model.inspect", "rekall.mesh.inspect"]),
            mapped.Message,
            [mapped]);
    }

    public static RekallAgeCommandError Map(Exception error, string assetId, string sourceAssetId)
    {
        var code = error switch
        {
            RekallAgeModelPublishingException publishingError => publishingError.Code,
            RekallAgeDocumentRevisionException => "REKALL_MODEL_REVISION_CONFLICT",
            RekallAgeMeshCompileException => "REKALL_MODEL_COMPILE_FAILED",
            FileNotFoundException or DirectoryNotFoundException => "REKALL_MODEL_SOURCE_MISSING",
            JsonException => "REKALL_MODEL_JSON_INVALID",
            InvalidDataException dataError => ExtractModelCode(dataError.Message) ?? "REKALL_MODEL_SOURCE_INVALID",
            UnauthorizedAccessException or IOException => "REKALL_MODEL_IO_FAILED",
            ArgumentException => "REKALL_MODEL_REQUEST_INVALID",
            _ => "REKALL_MODEL_FAILED"
        };
        var target = error switch
        {
            RekallAgeModelPublishingException publishingError when publishingError.Target is not null => publishingError.Target,
            FileNotFoundException or DirectoryNotFoundException or RekallAgeMeshCompileException => sourceAssetId,
            _ => assetId
        };
        return new(code, error.Message, target);
    }

    private static string? ExtractModelCode(string message)
    {
        var separator = message.IndexOf(':');
        var candidate = separator < 0 ? message : message[..separator];
        return candidate.StartsWith("REKALL_MODEL_", StringComparison.Ordinal)
            && candidate.All(character => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character) || character == '_')
                ? candidate
                : null;
    }
}
