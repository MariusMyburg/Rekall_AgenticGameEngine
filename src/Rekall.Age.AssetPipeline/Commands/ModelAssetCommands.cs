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

public sealed record ModelAssetMutationCommandResult(
    RekallAgePublishModelResult? Publication,
    IReadOnlyList<string> NextActions);

public sealed record ModelAssetInspectionCommandResult(
    RekallAgeModelAssetInspection? Inspection,
    IReadOnlyList<string> NextActions);

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
