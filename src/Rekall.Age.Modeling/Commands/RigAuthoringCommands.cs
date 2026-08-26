using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling.Commands;

public sealed record RekallAgeRigAssetSummary(
    string AssetId,
    string Name,
    string FileRevision,
    long LogicalRevision,
    int JointCount,
    IReadOnlyList<string> JointIdSample,
    bool SampleTruncated,
    IReadOnlyList<RekallAgeRigDiagnostic> Diagnostics,
    IReadOnlyList<string> NextActions);

public sealed record CreateRigAssetRequest(string ProjectRoot, string AssetId, string Name, IReadOnlyList<RekallAgeRigJoint> Joints);
public sealed record CreateRigAssetResult(RekallAgeRigAssetSummary? Rig);

public sealed class CreateRigAssetCommand : IRekallAgeCommand<CreateRigAssetRequest, CreateRigAssetResult>
{
    private readonly RekallAgeRigAssetStore _store = new();
    public string Name => "rekall.modeling.rig.create";
    public RekallAgeCommandSchema Schema => new(Name,
        "Creates a versioned native rig resource from explicit stable named joints, parent indices, and bind-local matrices; the engine validates and persists but does not invent a skeleton.",
        typeof(CreateRigAssetRequest).FullName!, typeof(CreateRigAssetResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CreateRigAssetResult>> ExecuteAsync(CreateRigAssetRequest request, RekallAgeCommandContext context)
    {
        try
        {
            var path = _store.GetRigPath(request.ProjectRoot, request.AssetId);
            if (File.Exists(path)) return Failure("REKALL_RIG_ASSET_EXISTS", $"Rig asset '{request.AssetId}' already exists.", request.AssetId);
            var rig = RekallAgeRigAsset.Create(request.AssetId, request.Name, request.Joints);
            context.Transaction.CaptureResourcePreimage(path);
            var revision = await _store.SaveIfRevisionAsync(request.ProjectRoot, rig, RekallAgeDocumentRevision.Missing, context.CancellationToken).ConfigureAwait(false);
            context.Transaction.RecordChangedResource(path);
            return RekallAgeCommandResult<CreateRigAssetResult>.Success(new(RigCommandEvidence.Summarize(rig, revision)), $"Created rig asset '{rig.AssetId}'.");
        }
        catch (Exception error) when (RigCommandEvidence.IsExpected(error))
        {
            return Failure(RigCommandEvidence.Code(error, "REKALL_RIG_CREATE_INVALID"), error.Message, request.AssetId);
        }
    }

    private static RekallAgeCommandResult<CreateRigAssetResult> Failure(string code, string message, string target) =>
        RekallAgeCommandResult<CreateRigAssetResult>.Failure(new(null), message, [new(code, message, target)]);
}

public sealed record ReplaceRigAssetRequest(string ProjectRoot, string AssetId, string ExpectedRevision, string Name, IReadOnlyList<RekallAgeRigJoint> Joints);
public sealed record ReplaceRigAssetResult(RekallAgeRigAssetSummary? Rig);

public sealed class ReplaceRigAssetCommand : IRekallAgeCommand<ReplaceRigAssetRequest, ReplaceRigAssetResult>
{
    private readonly RekallAgeRigAssetStore _store = new();
    public string Name => "rekall.modeling.rig.replace";
    public RekallAgeCommandSchema Schema => new(Name,
        "Atomically replaces an explicit native rig at an expected file revision while advancing its logical revision.",
        typeof(ReplaceRigAssetRequest).FullName!, typeof(ReplaceRigAssetResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ReplaceRigAssetResult>> ExecuteAsync(ReplaceRigAssetRequest request, RekallAgeCommandContext context)
    {
        try
        {
            var current = await _store.LoadVersionedAsync(request.ProjectRoot, request.AssetId, context.CancellationToken).ConfigureAwait(false);
            var rig = new RekallAgeRigAsset(RekallAgeRigAsset.CurrentSchemaVersion, request.AssetId, request.Name, checked(current.Value.Revision + 1), request.Joints);
            var path = _store.GetRigPath(request.ProjectRoot, request.AssetId);
            context.Transaction.CaptureResourcePreimage(path);
            var revision = await _store.SaveIfRevisionAsync(request.ProjectRoot, rig, request.ExpectedRevision, context.CancellationToken).ConfigureAwait(false);
            context.Transaction.RecordChangedResource(path);
            return RekallAgeCommandResult<ReplaceRigAssetResult>.Success(new(RigCommandEvidence.Summarize(rig, revision)), $"Replaced rig asset '{rig.AssetId}'.");
        }
        catch (Exception error) when (RigCommandEvidence.IsExpected(error))
        {
            var code = RigCommandEvidence.Code(error, "REKALL_RIG_REPLACE_INVALID");
            return RekallAgeCommandResult<ReplaceRigAssetResult>.Failure(new(null), error.Message, [new(code, error.Message, request.AssetId)]);
        }
    }
}

public sealed record InspectRigAssetRequest(string ProjectRoot, string AssetId, int MaximumSamples = 32);
public sealed record InspectRigAssetResult(RekallAgeRigAssetSummary? Rig);

public sealed class InspectRigAssetCommand : IRekallAgeCommand<InspectRigAssetRequest, InspectRigAssetResult>
{
    private readonly RekallAgeRigAssetStore _store = new();
    public string Name => "rekall.modeling.rig.inspect";
    public RekallAgeCommandSchema Schema => new(Name,
        "Inspects a native rig resource with bounded stable joint samples and validation diagnostics.",
        typeof(InspectRigAssetRequest).FullName!, typeof(InspectRigAssetResult).FullName!);
    public async ValueTask<RekallAgeCommandResult<InspectRigAssetResult>> ExecuteAsync(InspectRigAssetRequest request, RekallAgeCommandContext context)
    {
        try
        {
            if (request.MaximumSamples is < 1 or > 256)
                return RekallAgeCommandResult<InspectRigAssetResult>.Failure(new(null), "maximumSamples must be 1-256.", [new("REKALL_RIG_SAMPLE_LIMIT_INVALID", "maximumSamples must be 1-256.", request.AssetId)]);
            var rig = await _store.LoadVersionedAsync(request.ProjectRoot, request.AssetId, context.CancellationToken).ConfigureAwait(false);
            return RekallAgeCommandResult<InspectRigAssetResult>.Success(new(RigCommandEvidence.Summarize(rig.Value, rig.Revision, request.MaximumSamples)), $"Inspected rig asset '{request.AssetId}'.");
        }
        catch (Exception error) when (RigCommandEvidence.IsExpected(error))
        {
            return RekallAgeCommandResult<InspectRigAssetResult>.Failure(new(null), error.Message, [new(RigCommandEvidence.Code(error, "REKALL_RIG_INSPECT_FAILED"), error.Message, request.AssetId)]);
        }
    }
}

public sealed record ListRigAssetsRequest(string ProjectRoot);
public sealed record ListRigAssetsResult(IReadOnlyList<string> AssetIds, IReadOnlyList<string> NextActions);

public sealed class ListRigAssetsCommand : IRekallAgeCommand<ListRigAssetsRequest, ListRigAssetsResult>
{
    private readonly RekallAgeRigAssetStore _store = new();
    public string Name => "rekall.modeling.rig.list";
    public RekallAgeCommandSchema Schema => new(Name, "Lists native rig resource IDs in deterministic order.", typeof(ListRigAssetsRequest).FullName!, typeof(ListRigAssetsResult).FullName!);
    public ValueTask<RekallAgeCommandResult<ListRigAssetsResult>> ExecuteAsync(ListRigAssetsRequest request, RekallAgeCommandContext context)
    {
        try
        {
            var ids = _store.ListAssetIds(request.ProjectRoot);
            return ValueTask.FromResult(RekallAgeCommandResult<ListRigAssetsResult>.Success(
                new(ids, ids.Count == 0 ? ["Create a rig with rekall.modeling.rig.create."] : ["Inspect a rig with rekall.modeling.rig.inspect."]),
                $"Found {ids.Count} rig asset(s)."));
        }
        catch (Exception error) when (RigCommandEvidence.IsExpected(error))
        {
            return ValueTask.FromResult(RekallAgeCommandResult<ListRigAssetsResult>.Failure(new([], []), error.Message, [new("REKALL_RIG_LIST_FAILED", error.Message, request.ProjectRoot)]));
        }
    }
}

internal static class RigCommandEvidence
{
    public static RekallAgeRigAssetSummary Summarize(RekallAgeRigAsset rig, string fileRevision, int maximumSamples = 32)
    {
        var report = new RekallAgeRigValidator().Validate(rig);
        return new(rig.AssetId, rig.Name, fileRevision, rig.Revision, rig.Joints.Count,
            rig.Joints.Take(maximumSamples).Select(joint => joint.JointId).ToArray(), rig.Joints.Count > maximumSamples,
            report.Diagnostics.Take(64).ToArray(),
            ["Attach with Rekall.RigPose and author named jointDeltas.", "Bind procedural points with rekall.modeling.skin.linear_weights."]);
    }

    public static bool IsExpected(Exception error) => error is InvalidDataException or ArgumentException or IOException or UnauthorizedAccessException or RekallAgeDocumentRevisionException;
    public static string Code(Exception error, string fallback) => error is RekallAgeDocumentRevisionException revision ? revision.Code : fallback;
}
