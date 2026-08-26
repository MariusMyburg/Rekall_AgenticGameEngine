using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling.Commands;

public sealed record RekallAgeCurveAssetSummary(
    string AssetId,
    string Name,
    string FileRevision,
    long LogicalRevision,
    int SplineCount,
    int ControlPointCount,
    IReadOnlyList<ulong> SplineIdSample,
    IReadOnlyList<ulong> ControlPointIdSample,
    IReadOnlyList<RekallAgeCurveDiagnostic> Diagnostics,
    bool SamplesTruncated,
    IReadOnlyList<string> NextActions);

public sealed record RekallAgeCurveEvaluationSummary(
    string AssetId,
    long LogicalRevision,
    int ResolutionPerSegment,
    int SplineCount,
    int PointCount,
    IReadOnlyList<RekallAgeGeometryVector3> PositionSample,
    bool SamplesTruncated,
    IReadOnlyList<string> NextActions);

public sealed record CreateCurveAssetRequest(
    string ProjectRoot,
    string AssetId,
    string Name,
    IReadOnlyList<RekallAgeCurveSpline> Splines);
public sealed record CreateCurveAssetResult(RekallAgeCurveAssetSummary? Curve);

public sealed class CreateCurveAssetCommand : IRekallAgeCommand<CreateCurveAssetRequest, CreateCurveAssetResult>
{
    private readonly RekallAgeCurveAssetStore _store = new();
    public string Name => "rekall.modeling.curve.create";
    public RekallAgeCommandSchema Schema => new(Name,
        "Creates a versioned editable poly/Bezier curve resource from explicit agent-authored splines and stable control-point IDs; the engine validates and persists but does not invent curve content.",
        typeof(CreateCurveAssetRequest).FullName!, typeof(CreateCurveAssetResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CreateCurveAssetResult>> ExecuteAsync(CreateCurveAssetRequest request, RekallAgeCommandContext context)
    {
        try
        {
            var path = _store.GetCurvePath(request.ProjectRoot, request.AssetId);
            if (File.Exists(path)) return Failure("REKALL_CURVE_ASSET_EXISTS", $"Curve asset '{request.AssetId}' already exists.", request.AssetId);
            var curve = RekallAgeCurveAsset.Create(request.AssetId, request.Name, request.Splines);
            context.Transaction.CaptureResourcePreimage(path);
            var revision = await _store.SaveIfRevisionAsync(request.ProjectRoot, curve, RekallAgeDocumentRevision.Missing, context.CancellationToken);
            context.Transaction.RecordChangedResource(path);
            return RekallAgeCommandResult<CreateCurveAssetResult>.Success(new(CurveCommandEvidence.Summarize(curve, revision)), $"Created curve asset '{curve.AssetId}'.");
        }
        catch (Exception error) when (CurveCommandEvidence.IsExpected(error))
        {
            return Failure(CurveCommandEvidence.Code(error, "REKALL_CURVE_CREATE_INVALID"), error.Message, request.AssetId);
        }
    }

    private static RekallAgeCommandResult<CreateCurveAssetResult> Failure(string code, string message, string target) =>
        RekallAgeCommandResult<CreateCurveAssetResult>.Failure(new(null), message, [new(code, message, target)]);
}

public sealed record ReplaceCurveAssetRequest(
    string ProjectRoot,
    string AssetId,
    string ExpectedRevision,
    string Name,
    IReadOnlyList<RekallAgeCurveSpline> Splines);
public sealed record ReplaceCurveAssetResult(RekallAgeCurveAssetSummary? Curve);

public sealed class ReplaceCurveAssetCommand : IRekallAgeCommand<ReplaceCurveAssetRequest, ReplaceCurveAssetResult>
{
    private readonly RekallAgeCurveAssetStore _store = new();
    public string Name => "rekall.modeling.curve.replace";
    public RekallAgeCommandSchema Schema => new(Name,
        "Atomically replaces explicit curve control data at an expected file revision while advancing the logical revision and retaining a recovery preimage.",
        typeof(ReplaceCurveAssetRequest).FullName!, typeof(ReplaceCurveAssetResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ReplaceCurveAssetResult>> ExecuteAsync(ReplaceCurveAssetRequest request, RekallAgeCommandContext context)
    {
        try
        {
            var current = await _store.LoadVersionedAsync(request.ProjectRoot, request.AssetId, context.CancellationToken);
            var curve = new RekallAgeCurveAsset(RekallAgeCurveAsset.CurrentSchemaVersion, request.AssetId, request.Name,
                checked(current.Value.Revision + 1), request.Splines);
            var path = _store.GetCurvePath(request.ProjectRoot, request.AssetId);
            context.Transaction.CaptureResourcePreimage(path);
            var revision = await _store.SaveIfRevisionAsync(request.ProjectRoot, curve, request.ExpectedRevision, context.CancellationToken);
            context.Transaction.RecordChangedResource(path);
            return RekallAgeCommandResult<ReplaceCurveAssetResult>.Success(new(CurveCommandEvidence.Summarize(curve, revision)), $"Replaced curve asset '{curve.AssetId}' at logical revision {curve.Revision}.");
        }
        catch (Exception error) when (CurveCommandEvidence.IsExpected(error))
        {
            var code = CurveCommandEvidence.Code(error, "REKALL_CURVE_REPLACE_INVALID");
            return RekallAgeCommandResult<ReplaceCurveAssetResult>.Failure(new(null), error.Message, [new(code, error.Message, request.AssetId)]);
        }
    }
}

public sealed record InspectCurveAssetRequest(string ProjectRoot, string AssetId);
public sealed record InspectCurveAssetResult(RekallAgeCurveAssetSummary? Curve);

public sealed class InspectCurveAssetCommand : IRekallAgeCommand<InspectCurveAssetRequest, InspectCurveAssetResult>
{
    private readonly RekallAgeCurveAssetStore _store = new();
    public string Name => "rekall.modeling.curve.inspect";
    public RekallAgeCommandSchema Schema => new(Name, "Inspects a persisted curve resource with bounded stable-ID samples and validation diagnostics.", typeof(InspectCurveAssetRequest).FullName!, typeof(InspectCurveAssetResult).FullName!);
    public async ValueTask<RekallAgeCommandResult<InspectCurveAssetResult>> ExecuteAsync(InspectCurveAssetRequest request, RekallAgeCommandContext context)
    {
        try
        {
            var curve = await _store.LoadVersionedAsync(request.ProjectRoot, request.AssetId, context.CancellationToken);
            return RekallAgeCommandResult<InspectCurveAssetResult>.Success(new(CurveCommandEvidence.Summarize(curve.Value, curve.Revision)), $"Inspected curve asset '{request.AssetId}'.");
        }
        catch (Exception error) when (CurveCommandEvidence.IsExpected(error))
        {
            return RekallAgeCommandResult<InspectCurveAssetResult>.Failure(new(null), error.Message, [new(CurveCommandEvidence.Code(error, "REKALL_CURVE_INSPECT_FAILED"), error.Message, request.AssetId)]);
        }
    }
}

public sealed record ListCurveAssetsRequest(string ProjectRoot);
public sealed record ListCurveAssetsResult(IReadOnlyList<string> AssetIds, IReadOnlyList<string> NextActions);

public sealed class ListCurveAssetsCommand : IRekallAgeCommand<ListCurveAssetsRequest, ListCurveAssetsResult>
{
    private readonly RekallAgeCurveAssetStore _store = new();
    public string Name => "rekall.modeling.curve.list";
    public RekallAgeCommandSchema Schema => new(Name, "Lists persisted curve resource IDs in deterministic order.", typeof(ListCurveAssetsRequest).FullName!, typeof(ListCurveAssetsResult).FullName!);
    public ValueTask<RekallAgeCommandResult<ListCurveAssetsResult>> ExecuteAsync(ListCurveAssetsRequest request, RekallAgeCommandContext context)
    {
        try
        {
            var ids = _store.ListAssetIds(request.ProjectRoot);
            return ValueTask.FromResult(RekallAgeCommandResult<ListCurveAssetsResult>.Success(
                new(ids, ids.Count == 0 ? ["Create a curve with rekall.modeling.curve.create."] : ["Inspect a curve with rekall.modeling.curve.inspect."]),
                $"Found {ids.Count} curve asset(s)."));
        }
        catch (Exception error) when (CurveCommandEvidence.IsExpected(error))
        {
            return ValueTask.FromResult(RekallAgeCommandResult<ListCurveAssetsResult>.Failure(new([], []), error.Message, [new("REKALL_CURVE_LIST_FAILED", error.Message, request.ProjectRoot)]));
        }
    }
}

public sealed record EvaluateCurveAssetRequest(string ProjectRoot, string AssetId, int ResolutionPerSegment = 8, int MaximumSamples = 32);
public sealed record EvaluateCurveAssetResult(RekallAgeCurveEvaluationSummary? Evaluation);

public sealed class EvaluateCurveAssetCommand : IRekallAgeCommand<EvaluateCurveAssetRequest, EvaluateCurveAssetResult>
{
    private readonly RekallAgeCurveAssetStore _store = new();
    public string Name => "rekall.modeling.curve.evaluate";
    public RekallAgeCommandSchema Schema => new(Name, "Evaluates persisted source control data into a bounded deterministic spline sample without mutating the resource.", typeof(EvaluateCurveAssetRequest).FullName!, typeof(EvaluateCurveAssetResult).FullName!);
    public async ValueTask<RekallAgeCommandResult<EvaluateCurveAssetResult>> ExecuteAsync(EvaluateCurveAssetRequest request, RekallAgeCommandContext context)
    {
        if (request.ResolutionPerSegment is < 1 or > 4096 || request.MaximumSamples is < 1 or > 256)
            return RekallAgeCommandResult<EvaluateCurveAssetResult>.Failure(new(null), "resolutionPerSegment must be 1-4096 and maximumSamples must be 1-256.", [new("REKALL_CURVE_EVALUATION_LIMIT_INVALID", "Evaluation limits are invalid.", request.AssetId)]);
        try
        {
            var curve = await _store.LoadAsync(request.ProjectRoot, request.AssetId, context.CancellationToken);
            var evaluated = new RekallAgeCurveEvaluator().Evaluate(curve, request.ResolutionPerSegment);
            var positions = evaluated.Splines.SelectMany(spline => spline.Points).Select(point => point.Position).ToArray();
            var summary = new RekallAgeCurveEvaluationSummary(curve.AssetId, curve.Revision, request.ResolutionPerSegment,
                evaluated.Splines.Count, positions.Length, positions.Take(request.MaximumSamples).ToArray(), positions.Length > request.MaximumSamples,
                ["Connect the curve to rekall.modeling.curve.profile_sweep or another typed curve operation."]);
            return RekallAgeCommandResult<EvaluateCurveAssetResult>.Success(new(summary), $"Evaluated curve asset '{curve.AssetId}' into {positions.Length} point(s).");
        }
        catch (Exception error) when (CurveCommandEvidence.IsExpected(error))
        {
            return RekallAgeCommandResult<EvaluateCurveAssetResult>.Failure(new(null), error.Message, [new(CurveCommandEvidence.Code(error, "REKALL_CURVE_EVALUATION_FAILED"), error.Message, request.AssetId)]);
        }
    }
}

internal static class CurveCommandEvidence
{
    public static RekallAgeCurveAssetSummary Summarize(RekallAgeCurveAsset curve, string fileRevision)
    {
        var validation = new RekallAgeCurveValidator().Validate(curve);
        var splineIds = curve.Splines.Select(spline => spline.SplineId).ToArray();
        var pointIds = curve.Splines.SelectMany(spline => spline.ControlPoints).Select(point => point.ControlPointId).ToArray();
        return new(curve.AssetId, curve.Name, fileRevision, curve.Revision, curve.Splines.Count, pointIds.Length,
            splineIds.Take(32).ToArray(), pointIds.Take(32).ToArray(), validation.Diagnostics.Take(64).ToArray(),
            splineIds.Length > 32 || pointIds.Length > 32,
            ["Evaluate with rekall.modeling.curve.evaluate.", "Use from a modeling graph with rekall.modeling.curve.source."]);
    }

    public static bool IsExpected(Exception error) => error is InvalidDataException or ArgumentException or IOException or UnauthorizedAccessException or RekallAgeDocumentRevisionException;
    public static string Code(Exception error, string fallback) => error is RekallAgeDocumentRevisionException revision ? revision.Code : fallback;
}
