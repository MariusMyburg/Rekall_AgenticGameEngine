using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling.Commands;

public sealed record SearchModelingNodeTypesRequest(string Query = "", int MaximumResults = 32);
public sealed record SearchModelingNodeTypesResult(IReadOnlyList<RekallAgeModelingNodeDescriptor> NodeTypes, bool Truncated);

public sealed class SearchModelingNodeTypesCommand : IRekallAgeCommand<SearchModelingNodeTypesRequest, SearchModelingNodeTypesResult>
{
    private readonly RekallAgeModelingNodeCatalog _catalog = RekallAgeModelingNodeCatalog.CreateDefault();
    public string Name => "rekall.modeling.node_types.search";
    public RekallAgeCommandSchema Schema => new(Name,
        "Searches registered procedural modeling node types by type ID, display name, or description. maximumResults must be 1-128; results are bounded descriptors, never authored content.",
        typeof(SearchModelingNodeTypesRequest).FullName!, typeof(SearchModelingNodeTypesResult).FullName!);

    public ValueTask<RekallAgeCommandResult<SearchModelingNodeTypesResult>> ExecuteAsync(SearchModelingNodeTypesRequest request, RekallAgeCommandContext context)
    {
        if (request.MaximumResults is < 1 or > 128) return ValueTask.FromResult(Fail("REKALL_MODELING_NODE_SEARCH_LIMIT", "maximumResults must be between 1 and 128."));
        var query = request.Query?.Trim() ?? string.Empty;
        var matches = _catalog.Descriptors.Where(item => query.Length == 0
            || item.TypeId.Contains(query, StringComparison.OrdinalIgnoreCase)
            || item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || item.Description.Contains(query, StringComparison.OrdinalIgnoreCase)).OrderBy(item => item.TypeId, StringComparer.Ordinal).ToArray();
        return ValueTask.FromResult(RekallAgeCommandResult<SearchModelingNodeTypesResult>.Success(
            new(matches.Take(request.MaximumResults).ToArray(), matches.Length > request.MaximumResults),
            $"Found {matches.Length} matching modeling node type(s); returned {Math.Min(matches.Length, request.MaximumResults)}."));
    }
    private static RekallAgeCommandResult<SearchModelingNodeTypesResult> Fail(string code, string message) =>
        RekallAgeCommandResult<SearchModelingNodeTypesResult>.Failure(new([], false), message, [new(code, message)]);
}

public sealed record InspectModelingNodeTypeRequest(string TypeId, int TypeVersion = 1);
public sealed record InspectModelingNodeTypeResult(RekallAgeModelingNodeDescriptor? NodeType, IReadOnlyList<string> NextActions);

public sealed class InspectModelingNodeTypeCommand : IRekallAgeCommand<InspectModelingNodeTypeRequest, InspectModelingNodeTypeResult>
{
    private readonly RekallAgeModelingNodeCatalog _catalog = RekallAgeModelingNodeCatalog.CreateDefault();
    public string Name => "rekall.modeling.node_types.inspect";
    public RekallAgeCommandSchema Schema => new(Name,
        "Inspects one exact versioned procedural modeling node contract, including typed ports, parameters, limits, defaults, and determinism metadata.",
        typeof(InspectModelingNodeTypeRequest).FullName!, typeof(InspectModelingNodeTypeResult).FullName!);
    public ValueTask<RekallAgeCommandResult<InspectModelingNodeTypeResult>> ExecuteAsync(InspectModelingNodeTypeRequest request, RekallAgeCommandContext context)
    {
        var descriptor = _catalog.Find(request.TypeId, request.TypeVersion);
        return ValueTask.FromResult(descriptor is null
            ? RekallAgeCommandResult<InspectModelingNodeTypeResult>.Failure(new(null, ["Use rekall.modeling.node_types.search to discover supported types."]),
                $"Modeling node type '{request.TypeId}@{request.TypeVersion}' is not registered.",
                [new("REKALL_MODELING_NODE_TYPE_NOT_FOUND", $"Modeling node type '{request.TypeId}@{request.TypeVersion}' is not registered.", request.TypeId)])
            : RekallAgeCommandResult<InspectModelingNodeTypeResult>.Success(new(descriptor,
                ["Create a graph with rekall.modeling.graph.create or add this node with rekall.modeling.graph.apply_patch."]),
                $"Inspected modeling node type '{descriptor.TypeId}@{descriptor.TypeVersion}'."));
    }
}

public sealed record RekallAgeModelingGraphSummary(string AssetId, string Name, string FileRevision, long LogicalRevision,
    int NodeCount, int LinkCount, int OutputCount, int ExposedParameterCount,
    IReadOnlyList<RekallAgeModelingGraphNode> Nodes, IReadOnlyList<RekallAgeModelingGraphLink> Links,
    IReadOnlyList<RekallAgeModelingGraphOutput> Outputs, bool SamplesTruncated, IReadOnlyList<string> NextActions);

public sealed record RekallAgeModelingOutputSummary(string Name, int PointCount, int EdgeCount, int FaceCount, int CornerCount, RekallAgeMeshBounds Bounds);
public sealed record RekallAgeModelingEvaluationSummary(bool Succeeded, string AssetId, long SourceLogicalRevision,
    IReadOnlyList<RekallAgeModelingOutputSummary> Outputs, bool RetainedLastGoodOutputs, int EvaluatedNodeCount,
    int CacheHitCount, int InvalidatedNodeCount, IReadOnlyList<RekallAgeModelingNodeEvaluationReport> Nodes,
    bool NodesTruncated, double DurationMilliseconds, IReadOnlyList<RekallAgeModelingGraphDiagnostic> Diagnostics,
    bool DiagnosticsTruncated, IReadOnlyList<string> NextActions);

public sealed class RekallAgeModelingGraphCommandRuntime
{
    private readonly object _gate = new();
    private readonly Dictionary<string, RekallAgeModelingEvaluationSummary> _latest = new(StringComparer.OrdinalIgnoreCase);
    public RekallAgeModelingGraphEvaluator Evaluator { get; } = new();
    public void Remember(string projectRoot, string assetId, RekallAgeModelingEvaluationSummary summary)
    { lock (_gate) _latest[Key(projectRoot, assetId)] = summary; }
    public RekallAgeModelingEvaluationSummary? Find(string projectRoot, string assetId)
    { lock (_gate) return _latest.GetValueOrDefault(Key(projectRoot, assetId)); }
    private static string Key(string root, string id) => Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + "|" + id;
}

public sealed record CreateModelingGraphRequest(string ProjectRoot, string AssetId, string Name,
    IReadOnlyList<RekallAgeModelingGraphNode> Nodes, IReadOnlyList<RekallAgeModelingGraphLink> Links,
    IReadOnlyList<RekallAgeModelingGraphOutput> Outputs, IReadOnlyList<RekallAgeModelingGraphExposedParameter>? ExposedParameters = null);
public sealed record CreateModelingGraphResult(RekallAgeModelingGraphSummary? Graph);

public sealed class CreateModelingGraphCommand : IRekallAgeCommand<CreateModelingGraphRequest, CreateModelingGraphResult>
{
    private readonly RekallAgeModelingGraphAssetStore _store = new();
    public string Name => "rekall.modeling.graph.create";
    public RekallAgeCommandSchema Schema => new(Name,
        "Creates one strictly validated, versioned procedural modeling graph atomically. Supply explicit generic nodes, links, named outputs, and optional exposed parameters; the engine does not author graph content.",
        typeof(CreateModelingGraphRequest).FullName!, typeof(CreateModelingGraphResult).FullName!);
    public async ValueTask<RekallAgeCommandResult<CreateModelingGraphResult>> ExecuteAsync(CreateModelingGraphRequest request, RekallAgeCommandContext context)
    {
        var path = _store.GetGraphPath(request.ProjectRoot, request.AssetId);
        if (File.Exists(path)) return Failure("REKALL_MODELING_GRAPH_EXISTS", $"Modeling graph '{request.AssetId}' already exists.", request.AssetId);
        try
        {
            var graph = RekallAgeModelingGraphAsset.Create(request.AssetId, request.Name, request.Nodes, request.Links, request.Outputs, request.ExposedParameters);
            context.Transaction.CaptureResourcePreimage(path);
            var revision = await _store.SaveIfRevisionAsync(request.ProjectRoot, graph, RekallAgeDocumentRevision.Missing, context.CancellationToken);
            context.Transaction.RecordChangedResource(path);
            return RekallAgeCommandResult<CreateModelingGraphResult>.Success(new(ModelingGraphCommandEvidence.Graph(graph, revision, 32)), $"Created modeling graph '{graph.AssetId}' with {graph.Nodes.Count} nodes.");
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or RekallAgeDocumentRevisionException)
        { return Failure(error is RekallAgeDocumentRevisionException re ? re.Code : "REKALL_MODELING_GRAPH_CREATE_INVALID", error.Message, request.AssetId); }
    }
    private static RekallAgeCommandResult<CreateModelingGraphResult> Failure(string code, string message, string target) =>
        RekallAgeCommandResult<CreateModelingGraphResult>.Failure(new(null), message, [new(code, message, target)]);
}

public sealed record InspectModelingGraphRequest(string ProjectRoot, string AssetId, int MaximumSamples = 32);
public sealed record InspectModelingGraphResult(RekallAgeModelingGraphSummary? Graph);
public sealed class InspectModelingGraphCommand : IRekallAgeCommand<InspectModelingGraphRequest, InspectModelingGraphResult>
{
    private readonly RekallAgeModelingGraphAssetStore _store = new();
    public string Name => "rekall.modeling.graph.inspect";
    public RekallAgeCommandSchema Schema => new(Name,
        "Inspects a procedural graph with exact revisions, bounded node/link/output samples, counts, and next actions; maximumSamples must be 1-256.",
        typeof(InspectModelingGraphRequest).FullName!, typeof(InspectModelingGraphResult).FullName!);
    public async ValueTask<RekallAgeCommandResult<InspectModelingGraphResult>> ExecuteAsync(InspectModelingGraphRequest request, RekallAgeCommandContext context)
    {
        if (request.MaximumSamples is < 1 or > 256) return RekallAgeCommandResult<InspectModelingGraphResult>.Failure(new(null), "maximumSamples must be between 1 and 256.", [new("REKALL_MODELING_GRAPH_INSPECT_LIMIT", "maximumSamples must be between 1 and 256.", request.AssetId)]);
        var loaded = await _store.LoadVersionedAsync(request.ProjectRoot, request.AssetId, context.CancellationToken);
        return RekallAgeCommandResult<InspectModelingGraphResult>.Success(new(ModelingGraphCommandEvidence.Graph(loaded.Value, loaded.Revision, request.MaximumSamples)), $"Inspected modeling graph '{request.AssetId}' revision {loaded.Value.Revision}.");
    }
}

public sealed record ApplyModelingGraphPatchRequest(string ProjectRoot, string AssetId, string ExpectedRevision, RekallAgeModelingGraphPatch Patch);
public sealed record ApplyModelingGraphPatchResult(RekallAgeModelingGraphSummary? Graph, int AppliedOperationCount, IReadOnlyList<RekallAgeModelingGraphDiagnostic> Diagnostics);
public sealed class ApplyModelingGraphPatchCommand : IRekallAgeCommand<ApplyModelingGraphPatchRequest, ApplyModelingGraphPatchResult>
{
    private readonly RekallAgeModelingGraphPatchService _service = new();
    public string Name => "rekall.modeling.graph.apply_patch";
    public RekallAgeCommandSchema Schema => new(Name,
        "Atomically applies 1-256 typed graph operations against an exact file revision, validates the complete candidate, and records a recoverable transaction preimage.",
        typeof(ApplyModelingGraphPatchRequest).FullName!, typeof(ApplyModelingGraphPatchResult).FullName!);
    public async ValueTask<RekallAgeCommandResult<ApplyModelingGraphPatchResult>> ExecuteAsync(ApplyModelingGraphPatchRequest request, RekallAgeCommandContext context)
    {
        try
        {
            var result = await _service.ApplyAsync(request.ProjectRoot, request.AssetId, request.ExpectedRevision, request.Patch, context.Transaction, context.CancellationToken);
            return RekallAgeCommandResult<ApplyModelingGraphPatchResult>.Success(new(ModelingGraphCommandEvidence.Graph(result.Graph, result.AfterFileRevision, 32), result.AppliedOperationCount, result.Validation.Diagnostics.Take(128).ToArray()), $"Applied {result.AppliedOperationCount} operation(s) to modeling graph '{request.AssetId}'.");
        }
        catch (RekallAgeModelingGraphPatchException error)
        { return RekallAgeCommandResult<ApplyModelingGraphPatchResult>.Failure(new(null, 0, error.Diagnostics.Take(128).ToArray()), error.Message, [new(error.Code, error.Message, request.AssetId)]); }
        catch (RekallAgeDocumentRevisionException error)
        { return RekallAgeCommandResult<ApplyModelingGraphPatchResult>.Failure(new(null, 0, []), error.Message, [new(error.Code, error.Message, request.AssetId)]); }
    }
}

public sealed record ValidateModelingGraphRequest(string ProjectRoot, string AssetId);
public sealed record ValidateModelingGraphResult(string FileRevision, long LogicalRevision, bool IsValid,
    IReadOnlyList<string> OrderedNodeIds, IReadOnlyList<string> UnreachableNodeIds,
    IReadOnlyList<RekallAgeModelingGraphDiagnostic> Diagnostics, bool DiagnosticsTruncated);
public sealed class ValidateModelingGraphCommand : IRekallAgeCommand<ValidateModelingGraphRequest, ValidateModelingGraphResult>
{
    private readonly RekallAgeModelingGraphAssetStore _store = new();
    private readonly RekallAgeModelingGraphValidator _validator = new(RekallAgeModelingNodeCatalog.CreateDefault());
    public string Name => "rekall.modeling.graph.validate";
    public RekallAgeCommandSchema Schema => new(Name,
        "Strictly validates graph schema, IDs, node versions, parameters, typed port compatibility, cardinality, outputs, domains, and cycles, returning a deterministic execution plan.",
        typeof(ValidateModelingGraphRequest).FullName!, typeof(ValidateModelingGraphResult).FullName!);
    public async ValueTask<RekallAgeCommandResult<ValidateModelingGraphResult>> ExecuteAsync(ValidateModelingGraphRequest request, RekallAgeCommandContext context)
    {
        var loaded = await _store.LoadVersionedAsync(request.ProjectRoot, request.AssetId, context.CancellationToken);
        var report = _validator.Validate(loaded.Value);
        var diagnostics = report.Diagnostics.Take(128).ToArray();
        var value = new ValidateModelingGraphResult(loaded.Revision, loaded.Value.Revision, report.IsValid,
            report.ExecutionPlan?.OrderedNodeIds ?? [], report.UnreachableNodeIds, diagnostics, report.Diagnostics.Count > diagnostics.Length);
        return report.IsValid
            ? RekallAgeCommandResult<ValidateModelingGraphResult>.Success(value, $"Modeling graph '{request.AssetId}' is valid.")
            : RekallAgeCommandResult<ValidateModelingGraphResult>.Failure(value, $"Modeling graph '{request.AssetId}' is invalid.", report.Diagnostics.Where(item => item.Severity == RekallAgeModelingDiagnosticSeverity.Error).Take(32).Select(item => new RekallAgeCommandError(item.Code, item.Message, item.NodeId ?? item.LinkId)).ToArray());
    }
}

public sealed record EvaluateModelingGraphRequest(string ProjectRoot, string AssetId, IReadOnlyList<string> OutputNames,
    RekallAgeModelingEvaluationBudget? Budget = null, RekallAgeModelingEvaluationContext? EvaluationContext = null);
public sealed record EvaluateModelingGraphResult(RekallAgeModelingEvaluationSummary Evaluation);
public sealed class EvaluateModelingGraphCommand : IRekallAgeCommand<EvaluateModelingGraphRequest, EvaluateModelingGraphResult>
{
    private readonly RekallAgeModelingGraphAssetStore _store = new(); private readonly RekallAgeModelingGraphCommandRuntime _runtime;
    public EvaluateModelingGraphCommand(RekallAgeModelingGraphCommandRuntime runtime) => _runtime = runtime;
    public string Name => "rekall.modeling.graph.evaluate";
    public RekallAgeCommandSchema Schema => new(Name,
        "Demand-evaluates named graph outputs deterministically under explicit bounded budgets. Returns cache/invalidation facts, bounded node reports, diagnostics, and output topology/bounds without raw buffers.",
        typeof(EvaluateModelingGraphRequest).FullName!, typeof(EvaluateModelingGraphResult).FullName!);
    public async ValueTask<RekallAgeCommandResult<EvaluateModelingGraphResult>> ExecuteAsync(EvaluateModelingGraphRequest request, RekallAgeCommandContext context)
    {
        if (request.OutputNames is null || request.OutputNames.Count is < 1 or > 256) return RekallAgeCommandResult<EvaluateModelingGraphResult>.Failure(new(ModelingGraphCommandEvidence.EmptyEvaluation(request.AssetId)), "outputNames must contain 1-256 named outputs.", [new("REKALL_MODELING_GRAPH_OUTPUT_BOUNDS", "outputNames must contain 1-256 named outputs.", request.AssetId)]);
        var graph = await _store.LoadAsync(request.ProjectRoot, request.AssetId, context.CancellationToken);
        var report = await _runtime.Evaluator.EvaluateAsync(graph, request.OutputNames, request.Budget ?? RekallAgeModelingEvaluationBudget.Default, request.EvaluationContext ?? ModelingGraphCommandEvidence.DefaultContext, context.CancellationToken);
        var summary = ModelingGraphCommandEvidence.Evaluation(report); _runtime.Remember(request.ProjectRoot, request.AssetId, summary);
        return report.Succeeded
            ? RekallAgeCommandResult<EvaluateModelingGraphResult>.Success(new(summary), $"Evaluated {summary.Outputs.Count} output(s) from modeling graph '{request.AssetId}'; {summary.CacheHitCount} cache hit(s), {summary.InvalidatedNodeCount} invalidation(s).")
            : RekallAgeCommandResult<EvaluateModelingGraphResult>.Failure(new(summary), $"Modeling graph '{request.AssetId}' evaluation failed.", report.Diagnostics.Where(item => item.Severity == RekallAgeModelingDiagnosticSeverity.Error).Take(32).Select(item => new RekallAgeCommandError(item.Code, item.Message, item.NodeId)).ToArray());
    }
}

public sealed record BakeModelingGraphRequest(string ProjectRoot, string AssetId, string OutputName, string TargetMeshAssetId,
    string ExpectedTargetRevision, RekallAgeModelingEvaluationBudget? Budget = null, RekallAgeModelingEvaluationContext? EvaluationContext = null);
public sealed record BakeModelingGraphResult(string GraphAssetId, long GraphLogicalRevision, string OutputName, string TargetMeshAssetId,
    string BeforeFileRevision, string AfterFileRevision, RekallAgeModelingOutputSummary Mesh, RekallAgeModelingEvaluationSummary Evaluation);
public sealed class BakeModelingGraphCommand : IRekallAgeCommand<BakeModelingGraphRequest, BakeModelingGraphResult>
{
    private readonly RekallAgeModelingGraphAssetStore _store = new(); private readonly RekallAgeModelingGraphBakeService _service; private readonly RekallAgeModelingGraphCommandRuntime _runtime;
    public BakeModelingGraphCommand(RekallAgeModelingGraphCommandRuntime runtime) { _runtime = runtime; _service = new(runtime.Evaluator); }
    public string Name => "rekall.modeling.graph.bake";
    public RekallAgeCommandSchema Schema => new(Name,
        "Evaluates one named graph output and atomically bakes it through the strict editable mesh store against an exact target revision, preserving provenance, transaction preimage, cache, and bounded evidence.",
        typeof(BakeModelingGraphRequest).FullName!, typeof(BakeModelingGraphResult).FullName!);
    public async ValueTask<RekallAgeCommandResult<BakeModelingGraphResult>> ExecuteAsync(BakeModelingGraphRequest request, RekallAgeCommandContext context)
    {
        try
        {
            var graph = await _store.LoadAsync(request.ProjectRoot, request.AssetId, context.CancellationToken);
            var result = await _service.BakeAsync(request.ProjectRoot, graph, request.OutputName, request.TargetMeshAssetId, request.ExpectedTargetRevision, request.Budget ?? RekallAgeModelingEvaluationBudget.Default, request.EvaluationContext ?? ModelingGraphCommandEvidence.DefaultContext, context.Transaction, context.CancellationToken);
            var evaluation = ModelingGraphCommandEvidence.Evaluation(result.Evaluation); _runtime.Remember(request.ProjectRoot, request.AssetId, evaluation);
            var mesh = ModelingGraphCommandEvidence.Output(result.OutputName, result.Mesh);
            return RekallAgeCommandResult<BakeModelingGraphResult>.Success(new(result.GraphAssetId, result.GraphLogicalRevision, result.OutputName, request.TargetMeshAssetId, result.BeforeFileRevision, result.AfterFileRevision, mesh, evaluation), $"Baked graph output '{result.OutputName}' to mesh '{request.TargetMeshAssetId}'.");
        }
        catch (RekallAgeModelingGraphBakeException error)
        {
            return RekallAgeCommandResult<BakeModelingGraphResult>.Failure(default!, error.Message, [new(error.Code, error.Message, request.AssetId)]);
        }
    }
}

public sealed record InspectModelingEvaluationRequest(string ProjectRoot, string AssetId);
public sealed record InspectModelingEvaluationResult(RekallAgeModelingEvaluationSummary? Evaluation);
public sealed class InspectModelingEvaluationCommand : IRekallAgeCommand<InspectModelingEvaluationRequest, InspectModelingEvaluationResult>
{
    private readonly RekallAgeModelingGraphCommandRuntime _runtime; public InspectModelingEvaluationCommand(RekallAgeModelingGraphCommandRuntime runtime) => _runtime = runtime;
    public string Name => "rekall.modeling.inspect_evaluation";
    public RekallAgeCommandSchema Schema => new(Name,
        "Returns the latest bounded evaluation evidence for one graph in this engine session, including output bounds, cache/invalidation facts, node timings, and diagnostics; no geometry buffers are returned.",
        typeof(InspectModelingEvaluationRequest).FullName!, typeof(InspectModelingEvaluationResult).FullName!);
    public ValueTask<RekallAgeCommandResult<InspectModelingEvaluationResult>> ExecuteAsync(InspectModelingEvaluationRequest request, RekallAgeCommandContext context)
    {
        var found = _runtime.Find(request.ProjectRoot, request.AssetId);
        return ValueTask.FromResult(found is null
            ? RekallAgeCommandResult<InspectModelingEvaluationResult>.Failure(new(null), $"No evaluation has run for modeling graph '{request.AssetId}' in this engine session.", [new("REKALL_MODELING_EVALUATION_NOT_FOUND", "Evaluate or bake the graph first.", request.AssetId)])
            : RekallAgeCommandResult<InspectModelingEvaluationResult>.Success(new(found), $"Inspected latest evaluation for modeling graph '{request.AssetId}'."));
    }
}

internal static class ModelingGraphCommandEvidence
{
    public static RekallAgeModelingEvaluationContext DefaultContext { get; } = new(0, 0, "rekall-age", "desktop");
    public static RekallAgeModelingGraphSummary Graph(RekallAgeModelingGraphAsset graph, string revision, int limit) => new(
        graph.AssetId, graph.Name, revision, graph.Revision, graph.Nodes.Count, graph.Links.Count, graph.Outputs.Count, graph.ExposedParameters.Count,
        graph.Nodes.Take(limit).ToArray(), graph.Links.Take(limit).ToArray(), graph.Outputs.Take(limit).ToArray(),
        graph.Nodes.Count > limit || graph.Links.Count > limit || graph.Outputs.Count > limit,
        ["Use rekall.modeling.graph.validate before evaluation.", "Use rekall.modeling.graph.apply_patch with fileRevision for atomic edits.", "Use rekall.modeling.graph.evaluate to inspect generated topology and bounds."]);
    public static RekallAgeModelingOutputSummary Output(string name, RekallAgeMeshAsset mesh)
    { var summary = new RekallAgeMeshValidator().Validate(mesh).Summary; return new(name, summary.PointCount, summary.EdgeCount, summary.FaceCount, summary.CornerCount, summary.Bounds); }
    public static RekallAgeModelingEvaluationSummary Evaluation(RekallAgeModelingGraphEvaluationReport report)
    {
        var diagnostics = report.Diagnostics.Take(128).ToArray();
        return new(report.Succeeded, report.AssetId, report.SourceLogicalRevision,
            report.Outputs.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => Output(item.Key, item.Value)).ToArray(),
            report.RetainedLastGoodOutputs, report.EvaluatedNodeCount, report.CacheHitCount, report.InvalidatedNodeCount,
            report.Nodes.Take(256).ToArray(), report.NodesTruncated || report.Nodes.Count > 256, report.DurationMilliseconds,
            diagnostics, report.Diagnostics.Count > diagnostics.Length,
            report.Succeeded ? ["Inspect compiled output after baking with rekall.mesh.inspect_compiled.", "Patch parameters or topology, then reevaluate to inspect invalidation."] : ["Repair the reported graph node or link, validate, and reevaluate."]);
    }
    public static RekallAgeModelingEvaluationSummary EmptyEvaluation(string assetId) => new(false, assetId, 0, [], false, 0, 0, 0, [], false, 0, [], false, ["Evaluate the graph first."]);
}
