using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling.Commands;

public sealed record RekallAgeMeshAttributeSummary(
    string Name,
    RekallAgeGeometryDomain Domain,
    RekallAgeGeometryValueType ValueType,
    string? Semantic,
    int ValueCount);

public sealed record RekallAgeMeshAssetSummary(
    string AssetId,
    string Name,
    string FileRevision,
    long LogicalRevision,
    RekallAgeMeshValidationSummary Topology,
    IReadOnlyList<RekallAgeMeshAttributeSummary> Attributes,
    IReadOnlyList<string> MaterialSlots,
    IReadOnlyList<string> SelectionSets,
    IReadOnlyList<ulong> PointIdSample,
    IReadOnlyList<ulong> EdgeIdSample,
    IReadOnlyList<ulong> FaceIdSample,
    IReadOnlyList<ulong> CornerIdSample,
    bool SamplesTruncated,
    IReadOnlyList<RekallAgeMeshDiagnostic> Diagnostics,
    bool DiagnosticsTruncated,
    IReadOnlyList<string> NextActions);

public sealed record RekallAgeMeshChangeEvidence(
    RekallAgeMeshChangeKind Kind,
    int CreatedPointCount,
    int CreatedEdgeCount,
    int CreatedFaceCount,
    int CreatedCornerCount,
    int DeletedPointCount,
    int DeletedEdgeCount,
    int DeletedFaceCount,
    int DeletedCornerCount,
    int ModifiedPointCount,
    int ModifiedEdgeCount,
    int ModifiedFaceCount,
    int ModifiedCornerCount,
    IReadOnlyList<ulong> CreatedIdSample,
    IReadOnlyList<ulong> DeletedIdSample,
    IReadOnlyList<ulong> ModifiedIdSample,
    IReadOnlyList<string> ChangedAttributes,
    RekallAgeMeshBounds AffectedBounds,
    bool IdSamplesTruncated);

public sealed record RekallAgeMeshProvenanceEvidence(
    RekallAgeGeometryDomain Domain,
    ulong InputElementId,
    IReadOnlyList<ulong> OutputElementIds,
    bool OutputsTruncated);

public sealed record RekallAgeMeshOperationEvidence(
    string OperationId,
    bool Persisted,
    string BeforeFileRevision,
    string AfterFileRevision,
    long BeforeLogicalRevision,
    long AfterLogicalRevision,
    RekallAgeMeshChangeEvidence Changes,
    IReadOnlyList<RekallAgeMeshProvenanceEvidence> Provenance,
    bool ProvenanceTruncated,
    RekallAgeMeshValidationSummary Validation,
    IReadOnlyList<RekallAgeMeshDiagnostic> Diagnostics,
    bool DiagnosticsTruncated,
    IReadOnlyList<string> NextActions);

public sealed record CreateMeshAssetRequest(
    string ProjectRoot,
    string AssetId,
    string Name,
    RekallAgeMeshTopology Topology,
    IReadOnlyList<RekallAgeGeometryAttribute>? Attributes = null,
    IReadOnlyList<RekallAgeMaterialSlot>? MaterialSlots = null,
    IReadOnlyList<RekallAgeMeshSelection>? SelectionSets = null);

public sealed record CreateMeshAssetResult(RekallAgeMeshAssetSummary? Mesh);

public sealed class CreateMeshAssetCommand : IRekallAgeCommand<CreateMeshAssetRequest, CreateMeshAssetResult>
{
    private readonly RekallAgeMeshAssetStore _store = new();

    public string Name => "rekall.mesh.create_asset";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Creates a persistent editable polygon mesh asset with stable point/edge/face/corner IDs, typed domain attributes, material slots, selections, strict validation, and atomic missing-revision publication.",
        typeof(CreateMeshAssetRequest).FullName!,
        typeof(CreateMeshAssetResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CreateMeshAssetResult>> ExecuteAsync(
        CreateMeshAssetRequest request,
        RekallAgeCommandContext context)
    {
        var mesh = RekallAgeMeshAsset.Create(
            request.AssetId,
            request.Name,
            request.Topology,
            request.Attributes,
            request.MaterialSlots,
            request.SelectionSets);
        var path = _store.GetMeshPath(request.ProjectRoot, request.AssetId);
        if (File.Exists(path))
        {
            return Failure("REKALL_MESH_ASSET_EXISTS", $"Mesh asset '{request.AssetId}' already exists.", request.AssetId);
        }
        try
        {
            context.Transaction.CaptureResourcePreimage(path);
            var revision = await _store.SaveIfRevisionAsync(
                request.ProjectRoot,
                mesh,
                RekallAgeDocumentRevision.Missing,
                context.CancellationToken);
            context.Transaction.RecordChangedResource(path);
            return RekallAgeCommandResult<CreateMeshAssetResult>.Success(
                new(MeshCommandEvidence.Summarize(mesh, revision, 32)),
                $"Created editable mesh asset '{request.AssetId}' with {mesh.Topology.PointIds.Count} points and {mesh.Topology.FaceIds.Count} faces.");
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or RekallAgeDocumentRevisionException)
        {
            var code = error is RekallAgeDocumentRevisionException revisionError
                ? revisionError.Code
                : "REKALL_MESH_CREATE_INVALID";
            return Failure(code, error.Message, request.AssetId);
        }
    }

    private static RekallAgeCommandResult<CreateMeshAssetResult> Failure(string code, string message, string target) =>
        RekallAgeCommandResult<CreateMeshAssetResult>.Failure(new(null), message, [new(code, message, target)]);
}

public sealed record InspectMeshAssetRequest(string ProjectRoot, string AssetId, int MaximumSamples = 32);

public sealed record InspectMeshAssetResult(RekallAgeMeshAssetSummary? Mesh);

public sealed class InspectMeshAssetCommand : IRekallAgeCommand<InspectMeshAssetRequest, InspectMeshAssetResult>
{
    private readonly RekallAgeMeshAssetStore _store = new();

    public string Name => "rekall.mesh.inspect";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Inspects a persistent editable mesh with bounded stable-ID samples, topology/manifold counts, bounds, attribute/material/selection summaries, revision facts, and structured diagnostics; it never dumps unbounded buffers.",
        typeof(InspectMeshAssetRequest).FullName!,
        typeof(InspectMeshAssetResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<InspectMeshAssetResult>> ExecuteAsync(
        InspectMeshAssetRequest request,
        RekallAgeCommandContext context)
    {
        if (request.MaximumSamples < 1 || request.MaximumSamples > 256)
        {
            const string message = "Mesh inspection maximumSamples must be between 1 and 256.";
            return RekallAgeCommandResult<InspectMeshAssetResult>.Failure(new(null), message, [new("REKALL_MESH_INSPECTION_LIMIT_INVALID", message, request.AssetId)]);
        }
        var loaded = await _store.LoadVersionedAsync(request.ProjectRoot, request.AssetId, context.CancellationToken);
        var summary = MeshCommandEvidence.Summarize(loaded.Value, loaded.Revision, request.MaximumSamples);
        return RekallAgeCommandResult<InspectMeshAssetResult>.Success(
            new(summary),
            $"Mesh '{request.AssetId}' revision {summary.LogicalRevision} has {summary.Topology.PointCount} points and {summary.Topology.FaceCount} faces.");
    }
}

public sealed record RekallAgeCompiledTriangleEvidence(
    int TriangleIndex,
    ulong SourceFaceId,
    IReadOnlyList<ulong> SourceCornerIds,
    IReadOnlyList<ulong> SourcePointIds,
    int SurfaceIndex);

public sealed record RekallAgeCompiledSurfaceEvidence(
    int SurfaceIndex,
    int MaterialSlotIndex,
    string? MaterialAssetId,
    int FirstIndex,
    int IndexCount,
    IReadOnlyList<ulong> SourceFaceIds);

public sealed record InspectCompiledMeshRequest(
    string ProjectRoot,
    string AssetId,
    int MaximumTriangles = 32);

public sealed record InspectCompiledMeshResult(
    string AssetId,
    string FileRevision,
    long LogicalRevision,
    int VertexCount,
    bool HasVertexColors,
    int IndexCount,
    int TriangleCount,
    int SurfaceCount,
    RekallAgeMeshBounds Bounds,
    IReadOnlyList<RekallAgeCompiledTriangleEvidence> Triangles,
    bool TrianglesTruncated,
    IReadOnlyList<RekallAgeCompiledSurfaceEvidence> Surfaces,
    IReadOnlyList<string> NextActions);

public sealed class InspectCompiledMeshCommand
    : IRekallAgeCommand<InspectCompiledMeshRequest, InspectCompiledMeshResult>
{
    private readonly RekallAgeMeshAssetStore _store = new();
    private readonly RekallAgeMeshCompiler _compiler = new();

    public string Name => "rekall.mesh.inspect_compiled";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Compiles a strict editable mesh and returns bounded immutable runtime counts, material surfaces, bounds, and triangle-to-source face/corner/point provenance for picking and repair loops without dumping vertex/index buffers.",
        typeof(InspectCompiledMeshRequest).FullName!,
        typeof(InspectCompiledMeshResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<InspectCompiledMeshResult>> ExecuteAsync(
        InspectCompiledMeshRequest request,
        RekallAgeCommandContext context)
    {
        if (request.MaximumTriangles < 1 || request.MaximumTriangles > 256)
        {
            const string message = "Compiled mesh inspection maximumTriangles must be between 1 and 256.";
            return RekallAgeCommandResult<InspectCompiledMeshResult>.Failure(
                default!,
                message,
                [new("REKALL_MESH_COMPILED_INSPECTION_LIMIT_INVALID", message, request.AssetId)]);
        }
        try
        {
            var loaded = await _store.LoadVersionedAsync(request.ProjectRoot, request.AssetId, context.CancellationToken);
            var compiled = _compiler.Compile(loaded.Value);
            var result = new InspectCompiledMeshResult(
                request.AssetId,
                loaded.Revision,
                compiled.SourceLogicalRevision,
                compiled.Vertices.Count,
                compiled.HasVertexColors,
                compiled.Indices.Count,
                compiled.Triangles.Count,
                compiled.Surfaces.Count,
                compiled.Bounds,
                compiled.Triangles.Take(request.MaximumTriangles)
                    .Select(triangle => new RekallAgeCompiledTriangleEvidence(
                        triangle.TriangleIndex,
                        triangle.SourceFaceId,
                        triangle.SourceCornerIds,
                        triangle.SourcePointIds,
                        triangle.SurfaceIndex))
                    .ToArray(),
                compiled.Triangles.Count > request.MaximumTriangles,
                compiled.Surfaces.Select(surface => new RekallAgeCompiledSurfaceEvidence(
                    surface.SurfaceIndex,
                    surface.MaterialSlotIndex,
                    surface.MaterialAssetId,
                    surface.FirstIndex,
                    surface.IndexCount,
                    surface.SourceFaceIds)).ToArray(),
                ["rekall.mesh.inspect", "rekall.mesh.validate", "rekall.render.capture_runtime_viewport"]);
            return RekallAgeCommandResult<InspectCompiledMeshResult>.Success(
                result,
                $"Compiled mesh '{request.AssetId}' to {result.TriangleCount} triangle(s) across {result.SurfaceCount} surface(s).");
        }
        catch (RekallAgeMeshCompileException error)
        {
            return RekallAgeCommandResult<InspectCompiledMeshResult>.Failure(
                default!,
                error.Message,
                [new(error.Code, error.Message, request.AssetId)]);
        }
    }
}

public sealed record PickCompiledMeshRequest(
    string ProjectRoot,
    string AssetId,
    RekallAgeGeometryVector3 Origin,
    RekallAgeGeometryVector3 Direction,
    double MaximumDistance = 1_000,
    int MaximumHits = 16);

public sealed record RekallAgeCompiledMeshPickHit(
    int TriangleIndex,
    double Distance,
    RekallAgeGeometryVector3 Position,
    ulong SourceFaceId,
    IReadOnlyList<ulong> SourceCornerIds,
    IReadOnlyList<ulong> SourcePointIds,
    int SurfaceIndex);

public sealed record PickCompiledMeshResult(
    string AssetId,
    string FileRevision,
    long LogicalRevision,
    int TotalHitCount,
    IReadOnlyList<RekallAgeCompiledMeshPickHit> Hits,
    bool HitsTruncated,
    IReadOnlyList<string> NextActions);

public sealed class PickCompiledMeshCommand
    : IRekallAgeCommand<PickCompiledMeshRequest, PickCompiledMeshResult>
{
    private const double IntersectionEpsilon = 1e-9;
    private readonly RekallAgeMeshAssetStore _store = new();
    private readonly RekallAgeMeshCompiler _compiler = new();

    public string Name => "rekall.mesh.pick_compiled";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Ray-picks an immutable compiled editable mesh in asset-local coordinates and returns bounded nearest hits with source face/corner/point and material-surface provenance.",
        typeof(PickCompiledMeshRequest).FullName!,
        typeof(PickCompiledMeshResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<PickCompiledMeshResult>> ExecuteAsync(
        PickCompiledMeshRequest request,
        RekallAgeCommandContext context)
    {
        var lengthSquared = Dot(request.Direction, request.Direction);
        if (!Finite(request.Origin) || !Finite(request.Direction)
            || !double.IsFinite(request.MaximumDistance) || request.MaximumDistance <= 0
            || !double.IsFinite(lengthSquared) || lengthSquared <= IntersectionEpsilon
            || request.MaximumHits < 1 || request.MaximumHits > 256)
        {
            const string message = "Compiled mesh pick requires finite origin/direction, a nonzero direction, positive finite maximumDistance, and maximumHits between 1 and 256.";
            return RekallAgeCommandResult<PickCompiledMeshResult>.Failure(
                default!,
                message,
                [new("REKALL_MESH_COMPILED_PICK_INPUT_INVALID", message, request.AssetId)]);
        }

        try
        {
            var loaded = await _store.LoadVersionedAsync(request.ProjectRoot, request.AssetId, context.CancellationToken);
            var compiled = _compiler.Compile(loaded.Value);
            var inverseLength = 1d / Math.Sqrt(lengthSquared);
            var direction = Scale(request.Direction, inverseLength);
            var hits = new List<RekallAgeCompiledMeshPickHit>();
            foreach (var triangle in compiled.Triangles)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var firstIndex = checked(triangle.TriangleIndex * 3);
                var a = compiled.Vertices[checked((int)compiled.Indices[firstIndex])].Position;
                var b = compiled.Vertices[checked((int)compiled.Indices[firstIndex + 1])].Position;
                var c = compiled.Vertices[checked((int)compiled.Indices[firstIndex + 2])].Position;
                if (!TryIntersect(request.Origin, direction, a, b, c, request.MaximumDistance, out var distance))
                {
                    continue;
                }
                hits.Add(new(
                    triangle.TriangleIndex,
                    distance,
                    Add(request.Origin, Scale(direction, distance)),
                    triangle.SourceFaceId,
                    triangle.SourceCornerIds,
                    triangle.SourcePointIds,
                    triangle.SurfaceIndex));
            }

            var ordered = hits.OrderBy(hit => hit.Distance).ThenBy(hit => hit.TriangleIndex).ToArray();
            var result = new PickCompiledMeshResult(
                request.AssetId,
                loaded.Revision,
                compiled.SourceLogicalRevision,
                ordered.Length,
                ordered.Take(request.MaximumHits).ToArray(),
                ordered.Length > request.MaximumHits,
                ["rekall.mesh.inspect_compiled", "rekall.mesh.inspect", "rekall.render.capture_runtime_viewport"]);
            return RekallAgeCommandResult<PickCompiledMeshResult>.Success(
                result,
                ordered.Length == 0
                    ? $"Ray did not hit compiled mesh '{request.AssetId}'."
                    : $"Ray hit compiled mesh '{request.AssetId}' {ordered.Length} time(s); nearest distance {ordered[0].Distance:0.######}.");
        }
        catch (RekallAgeMeshCompileException error)
        {
            return RekallAgeCommandResult<PickCompiledMeshResult>.Failure(
                default!,
                error.Message,
                [new(error.Code, error.Message, request.AssetId)]);
        }
    }

    private static bool TryIntersect(
        RekallAgeGeometryVector3 origin,
        RekallAgeGeometryVector3 direction,
        RekallAgeGeometryVector3 a,
        RekallAgeGeometryVector3 b,
        RekallAgeGeometryVector3 c,
        double maximumDistance,
        out double distance)
    {
        var edge1 = Subtract(b, a);
        var edge2 = Subtract(c, a);
        var p = Cross(direction, edge2);
        var determinant = Dot(edge1, p);
        if (Math.Abs(determinant) <= IntersectionEpsilon)
        {
            distance = 0;
            return false;
        }
        var inverseDeterminant = 1d / determinant;
        var fromA = Subtract(origin, a);
        var u = Dot(fromA, p) * inverseDeterminant;
        if (u < 0 || u > 1)
        {
            distance = 0;
            return false;
        }
        var q = Cross(fromA, edge1);
        var v = Dot(direction, q) * inverseDeterminant;
        if (v < 0 || u + v > 1)
        {
            distance = 0;
            return false;
        }
        distance = Dot(edge2, q) * inverseDeterminant;
        return distance >= 0 && distance <= maximumDistance;
    }

    private static bool Finite(RekallAgeGeometryVector3 value) =>
        double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);

    private static RekallAgeGeometryVector3 Add(RekallAgeGeometryVector3 left, RekallAgeGeometryVector3 right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    private static RekallAgeGeometryVector3 Subtract(RekallAgeGeometryVector3 left, RekallAgeGeometryVector3 right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    private static RekallAgeGeometryVector3 Scale(RekallAgeGeometryVector3 value, double scalar) =>
        new(value.X * scalar, value.Y * scalar, value.Z * scalar);

    private static double Dot(RekallAgeGeometryVector3 left, RekallAgeGeometryVector3 right) =>
        left.X * right.X + left.Y * right.Y + left.Z * right.Z;

    private static RekallAgeGeometryVector3 Cross(RekallAgeGeometryVector3 left, RekallAgeGeometryVector3 right) =>
        new(
            left.Y * right.Z - left.Z * right.Y,
            left.Z * right.X - left.X * right.Z,
            left.X * right.Y - left.Y * right.X);
}

public sealed record ValidateMeshAssetRequest(string ProjectRoot, string AssetId);

public sealed record ValidateMeshAssetResult(
    string AssetId,
    string FileRevision,
    long LogicalRevision,
    bool IsValid,
    RekallAgeMeshValidationSummary Summary,
    IReadOnlyList<RekallAgeMeshDiagnostic> Diagnostics,
    bool DiagnosticsTruncated,
    IReadOnlyList<string> NextActions);

public sealed class ValidateMeshAssetCommand : IRekallAgeCommand<ValidateMeshAssetRequest, ValidateMeshAssetResult>
{
    private readonly RekallAgeMeshAssetStore _store = new();
    private readonly RekallAgeMeshValidator _validator = new();

    public string Name => "rekall.mesh.validate";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Runs strict production topology, attribute, material, selection, finite-data, boundary, loose-edge, and non-manifold validation for a persistent editable mesh and returns bounded element-linked diagnostics.",
        typeof(ValidateMeshAssetRequest).FullName!,
        typeof(ValidateMeshAssetResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ValidateMeshAssetResult>> ExecuteAsync(
        ValidateMeshAssetRequest request,
        RekallAgeCommandContext context)
    {
        var loaded = await _store.LoadVersionedAsync(request.ProjectRoot, request.AssetId, context.CancellationToken);
        var report = _validator.Validate(loaded.Value);
        var diagnostics = report.Diagnostics.Take(MeshCommandEvidence.MaximumEvidenceItems).ToArray();
        var result = new ValidateMeshAssetResult(
            request.AssetId,
            loaded.Revision,
            loaded.Value.Revision,
            report.IsValid,
            report.Summary,
            diagnostics,
            diagnostics.Length < report.Diagnostics.Count,
            report.IsValid
                ? ["rekall.mesh.query_elements", "rekall.mesh.operation.preview", "rekall.mesh.assert"]
                : ["rekall.mesh.inspect", "rekall.mesh.operation.preview"]);
        return report.IsValid
            ? RekallAgeCommandResult<ValidateMeshAssetResult>.Success(result, $"Mesh '{request.AssetId}' passes strict validation.")
            : RekallAgeCommandResult<ValidateMeshAssetResult>.Failure(
                result,
                $"Mesh '{request.AssetId}' failed strict validation.",
                diagnostics.Where(item => item.Severity == RekallAgeMeshDiagnosticSeverity.Error)
                    .Select(item => new RekallAgeCommandError(item.Code, item.Message, request.AssetId))
                    .ToArray());
    }
}

public sealed record QueryMeshElementsRequest(
    string ProjectRoot,
    string AssetId,
    RekallAgeMeshElementSelector Selector,
    int MaximumResults = 64);

public sealed record QueryMeshElementsResult(
    string AssetId,
    string FileRevision,
    long LogicalRevision,
    RekallAgeMeshElementQueryResult Query,
    IReadOnlyList<string> NextActions);

public sealed class QueryMeshElementsCommand : IRekallAgeCommand<QueryMeshElementsRequest, QueryMeshElementsResult>
{
    private readonly RekallAgeMeshAssetStore _store = new();
    private readonly RekallAgeMeshElementQuery _query = new();

    public string Name => "rekall.mesh.query_elements";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Queries bounded stable mesh element IDs by domain using intersected explicit IDs, named selections, connectivity, finite spatial bounds, and typed attribute equality.",
        typeof(QueryMeshElementsRequest).FullName!,
        typeof(QueryMeshElementsResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<QueryMeshElementsResult>> ExecuteAsync(
        QueryMeshElementsRequest request,
        RekallAgeCommandContext context)
    {
        try
        {
            var loaded = await _store.LoadVersionedAsync(request.ProjectRoot, request.AssetId, context.CancellationToken);
            var query = _query.Resolve(loaded.Value, request.Selector, request.MaximumResults);
            return RekallAgeCommandResult<QueryMeshElementsResult>.Success(
                new(request.AssetId, loaded.Revision, loaded.Value.Revision, query,
                    ["rekall.mesh.operation.preview", "rekall.mesh.inspect"]),
                $"Mesh query matched {query.MatchedCount} {query.Domain} element(s) and returned {query.ElementIds.Count}.");
        }
        catch (RekallAgeMeshQueryException error)
        {
            return RekallAgeCommandResult<QueryMeshElementsResult>.Failure(
                new(request.AssetId, string.Empty, 0, new(request.Selector.Domain, [], 0, 0, false),
                    ["rekall.mesh.inspect", "rekall.mesh.query_elements"]),
                error.Message,
                [new(error.Code, error.Message, request.AssetId)]);
        }
    }
}

public sealed record PreviewMeshOperationRequest(
    string ProjectRoot,
    string AssetId,
    string ExpectedRevision,
    RekallAgeMeshOperationRequest Operation);

public sealed record PreviewMeshOperationResult(RekallAgeMeshOperationEvidence? Evidence);

public sealed class PreviewMeshOperationCommand : IRekallAgeCommand<PreviewMeshOperationRequest, PreviewMeshOperationResult>
{
    private readonly RekallAgeMeshEditService _service = new();
    public string Name => "rekall.mesh.operation.preview";
    public RekallAgeCommandSchema Schema => new(
        Name,
        "Previews one typed semantic mesh operation against an exact file revision and returns bounded changes, provenance, affected bounds, and validation without writing the asset or transaction.",
        typeof(PreviewMeshOperationRequest).FullName!,
        typeof(PreviewMeshOperationResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<PreviewMeshOperationResult>> ExecuteAsync(
        PreviewMeshOperationRequest request,
        RekallAgeCommandContext context) =>
        await MeshOperationCommandRunner.RunSingleAsync(
            request.ProjectRoot,
            request.AssetId,
            request.ExpectedRevision,
            request.Operation,
            persist: false,
            context,
            _service);
}

public sealed record ApplyMeshOperationRequest(
    string ProjectRoot,
    string AssetId,
    string ExpectedRevision,
    RekallAgeMeshOperationRequest Operation);

public sealed record ApplyMeshOperationResult(RekallAgeMeshOperationEvidence? Evidence);

public sealed class ApplyMeshOperationCommand : IRekallAgeCommand<ApplyMeshOperationRequest, ApplyMeshOperationResult>
{
    private readonly RekallAgeMeshEditService _service = new();
    public string Name => "rekall.mesh.operation.apply";
    public RekallAgeCommandSchema Schema => new(
        Name,
        "Atomically applies one typed semantic mesh operation to an exact file revision, captures a transaction preimage, and returns bounded changes, provenance, affected bounds, new revisions, and strict validation.",
        typeof(ApplyMeshOperationRequest).FullName!,
        typeof(ApplyMeshOperationResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ApplyMeshOperationResult>> ExecuteAsync(
        ApplyMeshOperationRequest request,
        RekallAgeCommandContext context)
    {
        var result = await MeshOperationCommandRunner.RunSingleAsync(
            request.ProjectRoot,
            request.AssetId,
            request.ExpectedRevision,
            request.Operation,
            persist: true,
            context,
            _service);
        return result.Ok
            ? RekallAgeCommandResult<ApplyMeshOperationResult>.Success(new(result.Value.Evidence), result.Summary)
            : RekallAgeCommandResult<ApplyMeshOperationResult>.Failure(new(result.Value.Evidence), result.Summary, result.Errors);
    }
}

public sealed record BatchMeshOperationsRequest(
    string ProjectRoot,
    string AssetId,
    string ExpectedRevision,
    IReadOnlyList<RekallAgeMeshOperationRequest> Operations,
    bool Apply = false);

public sealed record BatchMeshOperationsResult(
    string AssetId,
    bool Persisted,
    string BeforeFileRevision,
    string AfterFileRevision,
    long BeforeLogicalRevision,
    long AfterLogicalRevision,
    IReadOnlyList<RekallAgeMeshOperationEvidence> Steps,
    bool StepsTruncated,
    RekallAgeMeshValidationSummary? Validation,
    IReadOnlyList<string> NextActions);

public sealed class BatchMeshOperationsCommand : IRekallAgeCommand<BatchMeshOperationsRequest, BatchMeshOperationsResult>
{
    private readonly RekallAgeMeshEditService _service = new();
    public string Name => "rekall.mesh.operation.batch";
    public RekallAgeCommandSchema Schema => new(
        Name,
        "Previews by default, or atomically applies, 1-128 ordered semantic mesh operations against one exact file revision; a failed step publishes nothing and successful apply writes one logical revision.",
        typeof(BatchMeshOperationsRequest).FullName!,
        typeof(BatchMeshOperationsResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<BatchMeshOperationsResult>> ExecuteAsync(
        BatchMeshOperationsRequest request,
        RekallAgeCommandContext context)
    {
        try
        {
            var execution = request.Apply
                ? await _service.ApplyBatchAsync(request.ProjectRoot, request.AssetId, request.ExpectedRevision, request.Operations, context.Transaction, context.CancellationToken)
                : await _service.PreviewBatchAsync(request.ProjectRoot, request.AssetId, request.ExpectedRevision, request.Operations, context.Transaction, context.CancellationToken);
            var stepEvidence = execution.Steps
                .Take(MeshCommandEvidence.MaximumEvidenceItems)
                .Select((step, index) => MeshCommandEvidence.Operation(
                    request.Operations[index].OperationId,
                    new(execution.AssetId, false, execution.BeforeFileRevision, execution.BeforeFileRevision, step)))
                .ToArray();
            var result = new BatchMeshOperationsResult(
                execution.AssetId,
                execution.Persisted,
                execution.BeforeFileRevision,
                execution.AfterFileRevision,
                execution.BeforeLogicalRevision,
                execution.AfterLogicalRevision,
                stepEvidence,
                stepEvidence.Length < execution.Steps.Count,
                execution.Validation.Summary,
                execution.Persisted
                    ? ["rekall.mesh.validate", "rekall.mesh.assert", "rekall.mesh.inspect"]
                    : ["rekall.mesh.operation.batch", "rekall.mesh.validate"]);
            return RekallAgeCommandResult<BatchMeshOperationsResult>.Success(
                result,
                $"{(execution.Persisted ? "Applied" : "Previewed")} {execution.Steps.Count} mesh operation(s) atomically; logical revision {execution.BeforeLogicalRevision} -> {execution.AfterLogicalRevision}.");
        }
        catch (Exception error) when (error is RekallAgeMeshOperationException or RekallAgeDocumentRevisionException or ArgumentException)
        {
            var code = error switch
            {
                RekallAgeMeshOperationException operationError => operationError.Code,
                RekallAgeDocumentRevisionException revisionError => revisionError.Code,
                _ => "REKALL_MESH_BATCH_INVALID"
            };
            return RekallAgeCommandResult<BatchMeshOperationsResult>.Failure(
                new(request.AssetId, false, request.ExpectedRevision, request.ExpectedRevision, 0, 0, [], false, null,
                    ["rekall.mesh.inspect", "rekall.mesh.operation.preview"]),
                error.Message,
                [new(code, error.Message, request.AssetId)]);
        }
    }
}

public sealed record AssertMeshAssetRequest(
    string ProjectRoot,
    string AssetId,
    long? ExpectedLogicalRevision = null,
    int MinimumPointCount = 0,
    int MinimumEdgeCount = 0,
    int MinimumFaceCount = 0,
    int MinimumCornerCount = 0,
    bool RequireStrictValidation = true);

public sealed record AssertMeshAssetResult(
    string AssetId,
    string FileRevision,
    long LogicalRevision,
    bool Passed,
    IReadOnlyList<string> FailedAssertions,
    RekallAgeMeshValidationSummary Summary,
    IReadOnlyList<string> NextActions);

public sealed class AssertMeshAssetCommand : IRekallAgeCommand<AssertMeshAssetRequest, AssertMeshAssetResult>
{
    private readonly RekallAgeMeshAssetStore _store = new();
    private readonly RekallAgeMeshValidator _validator = new();
    public string Name => "rekall.mesh.assert";
    public RekallAgeCommandSchema Schema => new(
        Name,
        "Executes strict deterministic mesh assertions over logical revision, minimum topology counts, and validation; failed assertions are reported without weakening requested thresholds.",
        typeof(AssertMeshAssetRequest).FullName!,
        typeof(AssertMeshAssetResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<AssertMeshAssetResult>> ExecuteAsync(
        AssertMeshAssetRequest request,
        RekallAgeCommandContext context)
    {
        if (request.MinimumPointCount < 0 || request.MinimumEdgeCount < 0 || request.MinimumFaceCount < 0 || request.MinimumCornerCount < 0)
        {
            const string message = "Mesh assertion minimum counts must be non-negative.";
            return RekallAgeCommandResult<AssertMeshAssetResult>.Failure(
                new(request.AssetId, string.Empty, 0, false, [message], new(0, 0, 0, 0, 0, 0, 0, new(new(0, 0, 0), new(0, 0, 0))),
                    ["rekall.mesh.inspect"]),
                message,
                [new("REKALL_MESH_ASSERTION_INVALID", message, request.AssetId)]);
        }
        var loaded = await _store.LoadVersionedAsync(request.ProjectRoot, request.AssetId, context.CancellationToken);
        var validation = _validator.Validate(loaded.Value);
        var failed = new List<string>();
        if (request.ExpectedLogicalRevision.HasValue && request.ExpectedLogicalRevision.Value != loaded.Value.Revision)
        {
            failed.Add($"Expected logical revision {request.ExpectedLogicalRevision.Value}, actual {loaded.Value.Revision}.");
        }
        CheckMinimum("points", validation.Summary.PointCount, request.MinimumPointCount, failed);
        CheckMinimum("edges", validation.Summary.EdgeCount, request.MinimumEdgeCount, failed);
        CheckMinimum("faces", validation.Summary.FaceCount, request.MinimumFaceCount, failed);
        CheckMinimum("corners", validation.Summary.CornerCount, request.MinimumCornerCount, failed);
        if (request.RequireStrictValidation && !validation.IsValid)
        {
            failed.Add("Strict mesh validation failed.");
        }
        var result = new AssertMeshAssetResult(
            request.AssetId,
            loaded.Revision,
            loaded.Value.Revision,
            failed.Count == 0,
            failed,
            validation.Summary,
            failed.Count == 0
                ? ["rekall.mesh.inspect"]
                : ["rekall.mesh.inspect", "rekall.mesh.query_elements", "rekall.mesh.operation.preview"]);
        return result.Passed
            ? RekallAgeCommandResult<AssertMeshAssetResult>.Success(result, $"Mesh '{request.AssetId}' passed all requested assertions.")
            : RekallAgeCommandResult<AssertMeshAssetResult>.Failure(
                result,
                $"Mesh '{request.AssetId}' failed {failed.Count} assertion(s).",
                [new("REKALL_MESH_ASSERTION_FAILED", string.Join(" ", failed), request.AssetId)]);
    }

    private static void CheckMinimum(string label, int actual, int minimum, ICollection<string> failed)
    {
        if (actual < minimum)
        {
            failed.Add($"Expected at least {minimum} {label}, actual {actual}.");
        }
    }
}

public sealed record FractureMeshRequest(
    string ProjectRoot,
    string SourceAssetId,
    string ChunkAssetIdPrefix,
    int ChunkCount,
    long Seed = 0);

public sealed record FractureMeshResult(IReadOnlyList<RekallAgeMeshAssetSummary> Chunks);

public sealed class FractureMeshCommand : IRekallAgeCommand<FractureMeshRequest, FractureMeshResult>
{
    private readonly RekallAgeMeshAssetStore _store = new();

    public string Name => "rekall.mesh.fracture";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Splits a closed manifold mesh asset into N Voronoi-style chunk mesh assets around random seed points (built on the same CSG kernel as Boolean operations), persisting each chunk as a new editable mesh asset.",
        typeof(FractureMeshRequest).FullName!,
        typeof(FractureMeshResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<FractureMeshResult>> ExecuteAsync(
        FractureMeshRequest request,
        RekallAgeCommandContext context)
    {
        if (string.IsNullOrWhiteSpace(request.ChunkAssetIdPrefix))
        {
            return Failure("REKALL_MESH_FRACTURE_PREFIX_REQUIRED", "A chunk asset id prefix is required.", request.SourceAssetId);
        }

        RekallAgeMeshAsset source;
        try
        {
            source = await _store.LoadAsync(request.ProjectRoot, request.SourceAssetId, context.CancellationToken);
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            return Failure("REKALL_MESH_FRACTURE_SOURCE_NOT_FOUND", error.Message, request.SourceAssetId);
        }

        IReadOnlyList<RekallAgeMeshAsset> chunks;
        try
        {
            chunks = RekallAgeMeshFracture.Fracture(source, request.ChunkCount, request.Seed);
        }
        catch (Exception error) when (error is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            return Failure("REKALL_MESH_FRACTURE_FAILED", error.Message, request.SourceAssetId);
        }

        var summaries = new List<RekallAgeMeshAssetSummary>(chunks.Count);
        for (var index = 0; index < chunks.Count; index++)
        {
            var assetId = $"{request.ChunkAssetIdPrefix}-{index}";
            var path = _store.GetMeshPath(request.ProjectRoot, assetId);
            if (File.Exists(path))
            {
                return Failure("REKALL_MESH_ASSET_EXISTS", $"Chunk mesh asset '{assetId}' already exists.", assetId);
            }
            var chunk = RekallAgeMeshAsset.Create(
                assetId,
                $"{request.ChunkAssetIdPrefix} Chunk {index}",
                chunks[index].Topology,
                chunks[index].Attributes,
                chunks[index].MaterialSlots,
                chunks[index].SelectionSets);
            context.Transaction.CaptureResourcePreimage(path);
            var revision = await _store.SaveIfRevisionAsync(request.ProjectRoot, chunk, RekallAgeDocumentRevision.Missing, context.CancellationToken);
            context.Transaction.RecordChangedResource(path);
            summaries.Add(MeshCommandEvidence.Summarize(chunk, revision, 8));
        }
        return RekallAgeCommandResult<FractureMeshResult>.Success(
            new(summaries),
            $"Fractured '{request.SourceAssetId}' into {chunks.Count} chunk mesh asset(s).");
    }

    private static RekallAgeCommandResult<FractureMeshResult> Failure(string code, string message, string target) =>
        RekallAgeCommandResult<FractureMeshResult>.Failure(new([]), message, [new(code, message, target)]);
}

internal static class MeshOperationCommandRunner
{
    public static async ValueTask<RekallAgeCommandResult<PreviewMeshOperationResult>> RunSingleAsync(
        string projectRoot,
        string assetId,
        string expectedRevision,
        RekallAgeMeshOperationRequest operation,
        bool persist,
        RekallAgeCommandContext context,
        RekallAgeMeshEditService service)
    {
        try
        {
            var execution = persist
                ? await service.ApplyAsync(projectRoot, assetId, expectedRevision, operation, context.Transaction, context.CancellationToken)
                : await service.PreviewAsync(projectRoot, assetId, expectedRevision, operation, context.Transaction, context.CancellationToken);
            var evidence = MeshCommandEvidence.Operation(operation.OperationId, execution);
            return RekallAgeCommandResult<PreviewMeshOperationResult>.Success(
                new(evidence),
                $"{(persist ? "Applied" : "Previewed")} mesh operation '{operation.OperationId}' at logical revision {evidence.BeforeLogicalRevision} -> {evidence.AfterLogicalRevision}.");
        }
        catch (Exception error) when (error is RekallAgeMeshOperationException or RekallAgeDocumentRevisionException or ArgumentException)
        {
            var code = error switch
            {
                RekallAgeMeshOperationException operationError => operationError.Code,
                RekallAgeDocumentRevisionException revisionError => revisionError.Code,
                _ => "REKALL_MESH_OPERATION_INVALID"
            };
            return RekallAgeCommandResult<PreviewMeshOperationResult>.Failure(
                new(null),
                error.Message,
                [new(code, error.Message, assetId)]);
        }
    }
}

internal static class MeshCommandEvidence
{
    public const int MaximumEvidenceItems = 64;

    public static RekallAgeMeshAssetSummary Summarize(RekallAgeMeshAsset mesh, string fileRevision, int maximumSamples)
    {
        var validation = new RekallAgeMeshValidator().Validate(mesh);
        var diagnostics = validation.Diagnostics.Take(MaximumEvidenceItems).ToArray();
        var samplesTruncated = new[]
        {
            mesh.Topology.PointIds.Count,
            mesh.Topology.EdgeIds.Count,
            mesh.Topology.FaceIds.Count,
            mesh.Topology.CornerIds.Count
        }.Any(count => count > maximumSamples);
        return new RekallAgeMeshAssetSummary(
            mesh.AssetId,
            mesh.Name,
            fileRevision,
            mesh.Revision,
            validation.Summary,
            mesh.Attributes.Select(item => new RekallAgeMeshAttributeSummary(
                item.Name, item.Domain, item.ValueType, item.Semantic, item.Values.Count)).ToArray(),
            mesh.MaterialSlots.Select(item => item.Name).ToArray(),
            mesh.SelectionSets.Select(item => item.Name).ToArray(),
            mesh.Topology.PointIds.Take(maximumSamples).ToArray(),
            mesh.Topology.EdgeIds.Take(maximumSamples).ToArray(),
            mesh.Topology.FaceIds.Take(maximumSamples).ToArray(),
            mesh.Topology.CornerIds.Take(maximumSamples).ToArray(),
            samplesTruncated,
            diagnostics,
            diagnostics.Length < validation.Diagnostics.Count,
            ["rekall.mesh.query_elements", "rekall.mesh.operation.preview", "rekall.mesh.validate", "rekall.mesh.assert"]);
    }

    public static RekallAgeMeshOperationEvidence Operation(string operationId, RekallAgeMeshEditExecution execution)
    {
        var operation = execution.Operation;
        var provenance = operation.Provenance.Take(MaximumEvidenceItems)
            .Select(item => new RekallAgeMeshProvenanceEvidence(
                item.Domain,
                item.InputElementId,
                item.OutputElementIds.Take(MaximumEvidenceItems).ToArray(),
                item.OutputElementIds.Count > MaximumEvidenceItems))
            .ToArray();
        var diagnostics = operation.Validation.Diagnostics.Take(MaximumEvidenceItems).ToArray();
        return new RekallAgeMeshOperationEvidence(
            operationId,
            execution.Persisted,
            execution.BeforeFileRevision,
            execution.AfterFileRevision,
            operation.BeforeRevision,
            operation.AfterRevision,
            Change(operation.Changes),
            provenance,
            provenance.Length < operation.Provenance.Count,
            operation.Validation.Summary,
            diagnostics,
            diagnostics.Length < operation.Validation.Diagnostics.Count,
            execution.Persisted
                ? ["rekall.mesh.validate", "rekall.mesh.assert", "rekall.mesh.inspect"]
                : ["rekall.mesh.operation.apply", "rekall.mesh.validate"]);
    }

    private static RekallAgeMeshChangeEvidence Change(RekallAgeMeshChangeSet changes)
    {
        var created = changes.CreatedPointIds.Concat(changes.CreatedEdgeIds).Concat(changes.CreatedFaceIds).Concat(changes.CreatedCornerIds).ToArray();
        var deleted = changes.DeletedPointIds.Concat(changes.DeletedEdgeIds).Concat(changes.DeletedFaceIds).Concat(changes.DeletedCornerIds).ToArray();
        var modified = changes.ModifiedPointIds.Concat(changes.ModifiedEdgeIds).Concat(changes.ModifiedFaceIds).Concat(changes.ModifiedCornerIds).ToArray();
        return new RekallAgeMeshChangeEvidence(
            changes.Kind,
            changes.CreatedPointIds.Count,
            changes.CreatedEdgeIds.Count,
            changes.CreatedFaceIds.Count,
            changes.CreatedCornerIds.Count,
            changes.DeletedPointIds.Count,
            changes.DeletedEdgeIds.Count,
            changes.DeletedFaceIds.Count,
            changes.DeletedCornerIds.Count,
            changes.ModifiedPointIds.Count,
            changes.ModifiedEdgeIds.Count,
            changes.ModifiedFaceIds.Count,
            changes.ModifiedCornerIds.Count,
            created.Take(MaximumEvidenceItems).ToArray(),
            deleted.Take(MaximumEvidenceItems).ToArray(),
            modified.Take(MaximumEvidenceItems).ToArray(),
            changes.ChangedAttributes.Take(MaximumEvidenceItems).ToArray(),
            changes.AffectedBounds,
            created.Length > MaximumEvidenceItems || deleted.Length > MaximumEvidenceItems || modified.Length > MaximumEvidenceItems);
    }
}
