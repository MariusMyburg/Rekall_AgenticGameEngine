using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Commands;
using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Rendering;

namespace Rekall.Age.Workflows.Commands;

public sealed record SearchMaterialNodeTypesRequest(string Query = "", int MaximumResults = 32);
public sealed record SearchMaterialNodeTypesResult(IReadOnlyList<RekallAgeMaterialNodeDescriptor> NodeTypes, bool Truncated);
public sealed class SearchMaterialNodeTypesCommand : IRekallAgeCommand<SearchMaterialNodeTypesRequest, SearchMaterialNodeTypesResult>
{
    private readonly RekallAgeMaterialNodeCatalog _catalog = RekallAgeMaterialNodeCatalog.CreateDefault(); public string Name => "rekall.material.node_types.search";
    public RekallAgeCommandSchema Schema => new(Name, "Searches semantic material node contracts by type ID, name, or description; maximumResults must be 1-128.", typeof(SearchMaterialNodeTypesRequest).FullName!, typeof(SearchMaterialNodeTypesResult).FullName!);
    public ValueTask<RekallAgeCommandResult<SearchMaterialNodeTypesResult>> ExecuteAsync(SearchMaterialNodeTypesRequest request, RekallAgeCommandContext context)
    {
        if (request.MaximumResults is < 1 or > 128) return ValueTask.FromResult(RekallAgeCommandResult<SearchMaterialNodeTypesResult>.Failure(new([], false), "maximumResults must be 1-128.", [new("REKALL_MATERIAL_NODE_SEARCH_LIMIT", "maximumResults must be 1-128.")]));
        var query = request.Query?.Trim() ?? ""; var matches = _catalog.Descriptors.Where(item => query.Length == 0 || item.TypeId.Contains(query, StringComparison.OrdinalIgnoreCase) || item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Description.Contains(query, StringComparison.OrdinalIgnoreCase)).OrderBy(item => item.TypeId, StringComparer.Ordinal).ToArray();
        return ValueTask.FromResult(RekallAgeCommandResult<SearchMaterialNodeTypesResult>.Success(new(matches.Take(request.MaximumResults).ToArray(), matches.Length > request.MaximumResults), $"Returned {Math.Min(matches.Length, request.MaximumResults)} of {matches.Length} material node type(s)."));
    }
}
public sealed record InspectMaterialNodeTypeRequest(string TypeId, int TypeVersion = 1);
public sealed record InspectMaterialNodeTypeResult(RekallAgeMaterialNodeDescriptor? NodeType);
public sealed class InspectMaterialNodeTypeCommand : IRekallAgeCommand<InspectMaterialNodeTypeRequest, InspectMaterialNodeTypeResult>
{
    private readonly RekallAgeMaterialNodeCatalog _catalog = RekallAgeMaterialNodeCatalog.CreateDefault(); public string Name => "rekall.material.node_types.inspect";
    public RekallAgeCommandSchema Schema => new(Name, "Inspects one exact semantic material node version with typed ports, parameters, defaults, ranges, and asset-reference facts.", typeof(InspectMaterialNodeTypeRequest).FullName!, typeof(InspectMaterialNodeTypeResult).FullName!);
    public ValueTask<RekallAgeCommandResult<InspectMaterialNodeTypeResult>> ExecuteAsync(InspectMaterialNodeTypeRequest request, RekallAgeCommandContext context)
    { var found = _catalog.Find(request.TypeId, request.TypeVersion); return ValueTask.FromResult(found is null ? RekallAgeCommandResult<InspectMaterialNodeTypeResult>.Failure(new(null), "Material node type was not found.", [new("REKALL_MATERIAL_NODE_TYPE_NOT_FOUND", "Material node type was not found.", request.TypeId)]) : RekallAgeCommandResult<InspectMaterialNodeTypeResult>.Success(new(found), $"Inspected '{found.TypeId}@{found.TypeVersion}'.")); }
}

public sealed record RekallAgeMaterialGraphSummary(string AssetId, string Name, string FileRevision, long LogicalRevision, int NodeCount, int LinkCount, int ExposedParameterCount, IReadOnlyList<RekallAgeMaterialGraphNode> Nodes, IReadOnlyList<RekallAgeMaterialGraphLink> Links, RekallAgeMaterialGraphOutput Output, bool SamplesTruncated);
public sealed record CreateMaterialGraphRequest(string ProjectRoot, string AssetId, string Name, IReadOnlyList<RekallAgeMaterialGraphNode> Nodes, IReadOnlyList<RekallAgeMaterialGraphLink> Links, RekallAgeMaterialGraphOutput Output, IReadOnlyList<RekallAgeMaterialGraphExposedParameter>? ExposedParameters = null);
public sealed record CreateMaterialGraphResult(RekallAgeMaterialGraphSummary? Graph);
public sealed class CreateMaterialGraphCommand : IRekallAgeCommand<CreateMaterialGraphRequest, CreateMaterialGraphResult>
{
    private readonly RekallAgeMaterialGraphAssetStore _store = new(); public string Name => "rekall.material.graph.create";
    public RekallAgeCommandSchema Schema => new(Name, "Creates a strict semantic material graph atomically from explicit nodes, links, one surface output, and optional typed exposed parameters; the engine does not author material content.", typeof(CreateMaterialGraphRequest).FullName!, typeof(CreateMaterialGraphResult).FullName!);
    public async ValueTask<RekallAgeCommandResult<CreateMaterialGraphResult>> ExecuteAsync(CreateMaterialGraphRequest request, RekallAgeCommandContext context)
    {
        var graph = RekallAgeMaterialGraphAsset.Create(request.AssetId, request.Name, request.Nodes, request.Links, request.Output, request.ExposedParameters); var path = _store.GetGraphPath(request.ProjectRoot, request.AssetId);
        if (File.Exists(path)) return RekallAgeCommandResult<CreateMaterialGraphResult>.Failure(new(null), "Material graph already exists.", [new("REKALL_MATERIAL_GRAPH_EXISTS", "Material graph already exists.", request.AssetId)]);
        context.Transaction.CaptureResourcePreimage(path); var revision = await _store.SaveIfRevisionAsync(request.ProjectRoot, graph, RekallAgeDocumentRevision.Missing, context.CancellationToken); context.Transaction.RecordChangedResource(path);
        return RekallAgeCommandResult<CreateMaterialGraphResult>.Success(new(AdvancedModelingEvidence.MaterialGraph(graph, revision, 32)), $"Created material graph '{graph.AssetId}'.");
    }
}
public sealed record InspectMaterialGraphRequest(string ProjectRoot, string AssetId, int MaximumSamples = 32);
public sealed record InspectMaterialGraphResult(RekallAgeMaterialGraphSummary Graph);
public sealed class InspectMaterialGraphCommand : IRekallAgeCommand<InspectMaterialGraphRequest, InspectMaterialGraphResult>
{
    private readonly RekallAgeMaterialGraphAssetStore _store = new(); public string Name => "rekall.material.graph.inspect";
    public RekallAgeCommandSchema Schema => new(Name, "Inspects a material graph with exact revisions and bounded node/link samples; maximumSamples must be 1-256.", typeof(InspectMaterialGraphRequest).FullName!, typeof(InspectMaterialGraphResult).FullName!);
    public async ValueTask<RekallAgeCommandResult<InspectMaterialGraphResult>> ExecuteAsync(InspectMaterialGraphRequest request, RekallAgeCommandContext context)
    { if (request.MaximumSamples is < 1 or > 256) throw new ArgumentException("maximumSamples must be 1-256."); var loaded = await _store.LoadVersionedAsync(request.ProjectRoot, request.AssetId, context.CancellationToken); return RekallAgeCommandResult<InspectMaterialGraphResult>.Success(new(AdvancedModelingEvidence.MaterialGraph(loaded.Value, loaded.Revision, request.MaximumSamples)), $"Inspected material graph '{request.AssetId}'."); }
}
public sealed record ApplyMaterialGraphPatchRequest(string ProjectRoot, string AssetId, string ExpectedRevision, RekallAgeMaterialGraphPatch Patch);
public sealed record ApplyMaterialGraphPatchResult(RekallAgeMaterialGraphSummary Graph, int AppliedOperationCount);
public sealed class ApplyMaterialGraphPatchCommand : IRekallAgeCommand<ApplyMaterialGraphPatchRequest, ApplyMaterialGraphPatchResult>
{
    private readonly RekallAgeMaterialGraphPatchService _service = new(); public string Name => "rekall.material.graph.apply_patch";
    public RekallAgeCommandSchema Schema => new(Name, "Applies 1-256 typed material graph operations atomically against an exact file revision after validating the complete candidate.", typeof(ApplyMaterialGraphPatchRequest).FullName!, typeof(ApplyMaterialGraphPatchResult).FullName!);
    public async ValueTask<RekallAgeCommandResult<ApplyMaterialGraphPatchResult>> ExecuteAsync(ApplyMaterialGraphPatchRequest request, RekallAgeCommandContext context)
    { var result = await _service.ApplyAsync(request.ProjectRoot, request.AssetId, request.ExpectedRevision, request.Patch, context.Transaction, context.CancellationToken); return RekallAgeCommandResult<ApplyMaterialGraphPatchResult>.Success(new(AdvancedModelingEvidence.MaterialGraph(result.Graph, result.AfterFileRevision, 32), result.AppliedOperationCount), $"Applied {result.AppliedOperationCount} material graph operation(s)."); }
}
public sealed record ValidateMaterialGraphRequest(string ProjectRoot, string AssetId);
public sealed record ValidateMaterialGraphResult(string FileRevision, long LogicalRevision, bool IsValid, IReadOnlyList<string> OrderedNodeIds, IReadOnlyList<string> UnreachableNodeIds, IReadOnlyList<RekallAgeModelingGraphDiagnostic> Diagnostics, bool DiagnosticsTruncated);
public sealed class ValidateMaterialGraphCommand : IRekallAgeCommand<ValidateMaterialGraphRequest, ValidateMaterialGraphResult>
{
    private readonly RekallAgeMaterialGraphAssetStore _store = new(); private readonly RekallAgeMaterialGraphValidator _validator = new(RekallAgeMaterialNodeCatalog.CreateDefault()); public string Name => "rekall.material.graph.validate";
    public RekallAgeCommandSchema Schema => new(Name, "Validates material node versions, parameters, typed links, cardinality, exposed parameters, surface output, and cycles with a deterministic plan.", typeof(ValidateMaterialGraphRequest).FullName!, typeof(ValidateMaterialGraphResult).FullName!);
    public async ValueTask<RekallAgeCommandResult<ValidateMaterialGraphResult>> ExecuteAsync(ValidateMaterialGraphRequest request, RekallAgeCommandContext context)
    { var loaded = await _store.LoadVersionedAsync(request.ProjectRoot, request.AssetId, context.CancellationToken); var report = _validator.Validate(loaded.Value); var diagnostics = report.Diagnostics.Take(128).ToArray(); var value = new ValidateMaterialGraphResult(loaded.Revision, loaded.Value.Revision, report.IsValid, report.ExecutionPlan?.OrderedNodeIds ?? [], report.UnreachableNodeIds, diagnostics, diagnostics.Length < report.Diagnostics.Count); return report.IsValid ? RekallAgeCommandResult<ValidateMaterialGraphResult>.Success(value, "Material graph is valid.") : RekallAgeCommandResult<ValidateMaterialGraphResult>.Failure(value, "Material graph is invalid.", diagnostics.Select(item => new RekallAgeCommandError(item.Code, item.Message, item.NodeId)).ToArray()); }
}
public sealed record CompileMaterialGraphRequest(string ProjectRoot, string AssetId, string? InstanceAssetId = null, int MaximumSourceCharacters = 0);
public sealed record CompileMaterialGraphResult(bool Succeeded, string AssetId, long LogicalRevision, string ContentHash, int TextureCount, IReadOnlyList<RekallAgeMaterialTextureResource> Resources, IReadOnlyList<RekallAgeMaterialShaderSourceMapEntry> GlslSourceMap, IReadOnlyList<RekallAgeMaterialShaderSourceMapEntry> WgslSourceMap, string? GlslSource, string? WgslSource, bool SourcesTruncated, IReadOnlyList<RekallAgeModelingGraphDiagnostic> Diagnostics);
public sealed class CompileMaterialGraphCommand : IRekallAgeCommand<CompileMaterialGraphRequest, CompileMaterialGraphResult>
{
    private readonly RekallAgeMaterialGraphAssetStore _graphs = new(); private readonly RekallAgeMaterialInstanceAssetStore _instances = new(); public string Name => "rekall.material.graph.compile";
    public RekallAgeCommandSchema Schema => new(Name, "Compiles a semantic material graph or exact-revision instance to deterministic Vulkan GLSL and WebGPU WGSL. Sources are omitted by default; maximumSourceCharacters may be 0-65536.", typeof(CompileMaterialGraphRequest).FullName!, typeof(CompileMaterialGraphResult).FullName!);
    public async ValueTask<RekallAgeCommandResult<CompileMaterialGraphResult>> ExecuteAsync(CompileMaterialGraphRequest request, RekallAgeCommandContext context)
    {
        if (request.MaximumSourceCharacters is < 0 or > 65536) throw new ArgumentException("maximumSourceCharacters must be 0-65536."); var loaded = await _graphs.LoadVersionedAsync(request.ProjectRoot, request.AssetId, context.CancellationToken); var graph = loaded.Value;
        if (!string.IsNullOrWhiteSpace(request.InstanceAssetId)) { var instance = await _instances.LoadVersionedAsync(request.ProjectRoot, request.InstanceAssetId, context.CancellationToken); graph = new RekallAgeMaterialInstanceResolver().Resolve(graph, loaded.Revision, instance.Value); }
        var compiled = new RekallAgeMaterialGraphCompiler().Compile(graph); string? glsl = null; string? wgsl = null; var truncated = false;
        if (request.MaximumSourceCharacters > 0) { glsl = compiled.Glsl.Source[..Math.Min(compiled.Glsl.Source.Length, request.MaximumSourceCharacters)]; wgsl = compiled.Wgsl.Source[..Math.Min(compiled.Wgsl.Source.Length, request.MaximumSourceCharacters)]; truncated = glsl.Length < compiled.Glsl.Source.Length || wgsl.Length < compiled.Wgsl.Source.Length; }
        var value = new CompileMaterialGraphResult(compiled.Succeeded, compiled.AssetId, compiled.SourceLogicalRevision, compiled.ContentHash, compiled.Resources.Count, compiled.Resources, compiled.Glsl.SourceMap.Take(256).ToArray(), compiled.Wgsl.SourceMap.Take(256).ToArray(), glsl, wgsl, truncated, compiled.Diagnostics.Take(128).ToArray());
        return compiled.Succeeded ? RekallAgeCommandResult<CompileMaterialGraphResult>.Success(value, $"Compiled material graph '{request.AssetId}' for Vulkan and WebGPU.") : RekallAgeCommandResult<CompileMaterialGraphResult>.Failure(value, "Material graph compilation failed.", value.Diagnostics.Select(item => new RekallAgeCommandError(item.Code, item.Message, item.NodeId)).ToArray());
    }
}

public sealed record RekallAgeMaterialInstanceSummary(string AssetId, string Name, string FileRevision, long LogicalRevision, string GraphAssetId, string GraphFileRevision, IReadOnlyDictionary<string, JsonNode?> Overrides);
public sealed record CreateMaterialInstanceRequest(string ProjectRoot, string AssetId, string Name, string GraphAssetId, string GraphFileRevision, IReadOnlyDictionary<string, JsonNode?>? Overrides = null);
public sealed record CreateMaterialInstanceResult(RekallAgeMaterialInstanceSummary? Instance);
public sealed class CreateMaterialInstanceCommand : IRekallAgeCommand<CreateMaterialInstanceRequest, CreateMaterialInstanceResult>
{
    private readonly RekallAgeMaterialInstanceAssetStore _store = new(); public string Name => "rekall.material.instance.create";
    public RekallAgeCommandSchema Schema => new(Name, "Creates a typed material instance bound to an exact graph file revision; only graph-exposed overrides are accepted.", typeof(CreateMaterialInstanceRequest).FullName!, typeof(CreateMaterialInstanceResult).FullName!);
    public async ValueTask<RekallAgeCommandResult<CreateMaterialInstanceResult>> ExecuteAsync(CreateMaterialInstanceRequest request, RekallAgeCommandContext context)
    { var instance = RekallAgeMaterialInstanceAsset.Create(request.AssetId, request.Name, request.GraphAssetId, request.GraphFileRevision, request.Overrides); var path = _store.GetInstancePath(request.ProjectRoot, request.AssetId); if (File.Exists(path)) return RekallAgeCommandResult<CreateMaterialInstanceResult>.Failure(new(null), "Material instance already exists.", [new("REKALL_MATERIAL_INSTANCE_EXISTS", "Material instance already exists.", request.AssetId)]); context.Transaction.CaptureResourcePreimage(path); var revision = await _store.SaveIfRevisionAsync(request.ProjectRoot, instance, RekallAgeDocumentRevision.Missing, context.CancellationToken); context.Transaction.RecordChangedResource(path); return RekallAgeCommandResult<CreateMaterialInstanceResult>.Success(new(AdvancedModelingEvidence.Instance(instance, revision)), $"Created material instance '{request.AssetId}'."); }
}
public sealed record InspectMaterialInstanceRequest(string ProjectRoot, string AssetId);
public sealed record InspectMaterialInstanceResult(RekallAgeMaterialInstanceSummary Instance);
public sealed class InspectMaterialInstanceCommand : IRekallAgeCommand<InspectMaterialInstanceRequest, InspectMaterialInstanceResult>
{
    private readonly RekallAgeMaterialInstanceAssetStore _store = new(); public string Name => "rekall.material.instance.inspect";
    public RekallAgeCommandSchema Schema => new(Name, "Inspects one exact material instance, graph dependency revision, and bounded typed overrides.", typeof(InspectMaterialInstanceRequest).FullName!, typeof(InspectMaterialInstanceResult).FullName!);
    public async ValueTask<RekallAgeCommandResult<InspectMaterialInstanceResult>> ExecuteAsync(InspectMaterialInstanceRequest request, RekallAgeCommandContext context)
    { var loaded = await _store.LoadVersionedAsync(request.ProjectRoot, request.AssetId, context.CancellationToken); return RekallAgeCommandResult<InspectMaterialInstanceResult>.Success(new(AdvancedModelingEvidence.Instance(loaded.Value, loaded.Revision)), $"Inspected material instance '{request.AssetId}'."); }
}

public sealed record SearchModifierTypesRequest(string Query = "", int MaximumResults = 32);
public sealed record SearchModifierTypesResult(IReadOnlyList<RekallAgeModifierDescriptor> ModifierTypes, bool Truncated);
public sealed class SearchModifierTypesCommand : IRekallAgeCommand<SearchModifierTypesRequest, SearchModifierTypesResult>
{
    private readonly RekallAgeModifierCatalog _catalog = RekallAgeModifierCatalog.CreateDefault(); public string Name => "rekall.modifier.types.search";
    public RekallAgeCommandSchema Schema => new(Name, "Searches generic ordered modifier descriptors and their parameter, change-mask, determinism, attribute, and loss policies; maximumResults must be 1-128.", typeof(SearchModifierTypesRequest).FullName!, typeof(SearchModifierTypesResult).FullName!);
    public ValueTask<RekallAgeCommandResult<SearchModifierTypesResult>> ExecuteAsync(SearchModifierTypesRequest request, RekallAgeCommandContext context)
    { if (request.MaximumResults is < 1 or > 128) throw new ArgumentException("maximumResults must be 1-128."); var q = request.Query?.Trim() ?? ""; var matches = _catalog.Descriptors.Where(item => q.Length == 0 || item.TypeId.Contains(q, StringComparison.OrdinalIgnoreCase) || item.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) || item.Description.Contains(q, StringComparison.OrdinalIgnoreCase)).ToArray(); return ValueTask.FromResult(RekallAgeCommandResult<SearchModifierTypesResult>.Success(new(matches.Take(request.MaximumResults).ToArray(), matches.Length > request.MaximumResults), $"Returned {Math.Min(matches.Length, request.MaximumResults)} modifier type(s).")); }
}
public sealed record RekallAgeModifierStackSummary(string AssetId, string Name, string FileRevision, long LogicalRevision, string SourceMeshAssetId, string SourceMeshFileRevision, IReadOnlyList<RekallAgeModifierInstance> Modifiers, bool ModifiersTruncated);
public sealed record CreateModifierStackRequest(string ProjectRoot, string AssetId, string Name, string SourceMeshAssetId, string SourceMeshFileRevision, IReadOnlyList<RekallAgeModifierInstance> Modifiers);
public sealed record CreateModifierStackResult(RekallAgeModifierStackSummary? Stack);
public sealed class CreateModifierStackCommand : IRekallAgeCommand<CreateModifierStackRequest, CreateModifierStackResult>
{
    private readonly RekallAgeModifierStackAssetStore _store = new(); public string Name => "rekall.modifier.stack.create";
    public RekallAgeCommandSchema Schema => new(Name, "Creates an ordered generic modifier stack bound to an exact source mesh revision and atomically publishes it after strict validation.", typeof(CreateModifierStackRequest).FullName!, typeof(CreateModifierStackResult).FullName!);
    public async ValueTask<RekallAgeCommandResult<CreateModifierStackResult>> ExecuteAsync(CreateModifierStackRequest request, RekallAgeCommandContext context)
    { var stack = RekallAgeModifierStackAsset.Create(request.AssetId, request.Name, request.SourceMeshAssetId, request.SourceMeshFileRevision, request.Modifiers); var path = _store.GetStackPath(request.ProjectRoot, request.AssetId); if (File.Exists(path)) return RekallAgeCommandResult<CreateModifierStackResult>.Failure(new(null), "Modifier stack already exists.", [new("REKALL_MODIFIER_STACK_EXISTS", "Modifier stack already exists.", request.AssetId)]); context.Transaction.CaptureResourcePreimage(path); var revision = await _store.SaveIfRevisionAsync(request.ProjectRoot, stack, RekallAgeDocumentRevision.Missing, context.CancellationToken); context.Transaction.RecordChangedResource(path); return RekallAgeCommandResult<CreateModifierStackResult>.Success(new(AdvancedModelingEvidence.Stack(stack, revision, 64)), $"Created modifier stack '{request.AssetId}'."); }
}
public sealed record InspectModifierStackRequest(string ProjectRoot, string AssetId, int MaximumModifiers = 64);
public sealed record InspectModifierStackResult(RekallAgeModifierStackSummary Stack);
public sealed class InspectModifierStackCommand : IRekallAgeCommand<InspectModifierStackRequest, InspectModifierStackResult>
{
    private readonly RekallAgeModifierStackAssetStore _store = new(); public string Name => "rekall.modifier.stack.inspect";
    public RekallAgeCommandSchema Schema => new(Name, "Inspects an ordered modifier stack, exact source/file revisions, and bounded modifier samples; maximumModifiers must be 1-256.", typeof(InspectModifierStackRequest).FullName!, typeof(InspectModifierStackResult).FullName!);
    public async ValueTask<RekallAgeCommandResult<InspectModifierStackResult>> ExecuteAsync(InspectModifierStackRequest request, RekallAgeCommandContext context)
    { if (request.MaximumModifiers is < 1 or > 256) throw new ArgumentException("maximumModifiers must be 1-256."); var loaded = await _store.LoadVersionedAsync(request.ProjectRoot, request.AssetId, context.CancellationToken); return RekallAgeCommandResult<InspectModifierStackResult>.Success(new(AdvancedModelingEvidence.Stack(loaded.Value, loaded.Revision, request.MaximumModifiers)), $"Inspected modifier stack '{request.AssetId}'."); }
}
public sealed record ApplyModifierStackPatchRequest(string ProjectRoot, string AssetId, string ExpectedRevision, RekallAgeModifierStackPatch Patch);
public sealed record ApplyModifierStackPatchResult(RekallAgeModifierStackSummary Stack, int AppliedOperationCount);
public sealed class ApplyModifierStackPatchCommand : IRekallAgeCommand<ApplyModifierStackPatchRequest, ApplyModifierStackPatchResult>
{
    private readonly RekallAgeModifierStackPatchService _service = new(); public string Name => "rekall.modifier.stack.apply_patch";
    public RekallAgeCommandSchema Schema => new(Name, "Atomically adds, removes, reorders, configures, enables, or retargets modifiers using 1-256 operations against an exact stack revision.", typeof(ApplyModifierStackPatchRequest).FullName!, typeof(ApplyModifierStackPatchResult).FullName!);
    public async ValueTask<RekallAgeCommandResult<ApplyModifierStackPatchResult>> ExecuteAsync(ApplyModifierStackPatchRequest request, RekallAgeCommandContext context)
    { var result = await _service.ApplyAsync(request.ProjectRoot, request.AssetId, request.ExpectedRevision, request.Patch, context.Transaction, context.CancellationToken); return RekallAgeCommandResult<ApplyModifierStackPatchResult>.Success(new(AdvancedModelingEvidence.Stack(result.Stack, result.AfterFileRevision, 64), result.AppliedOperationCount), $"Applied {result.AppliedOperationCount} modifier stack operation(s)."); }
}
public sealed record RekallAgeModifierEvaluationSummary(bool Succeeded, string StackAssetId, long StackLogicalRevision, RekallAgeModelingOutputSummary? Mesh, int EvaluatedModifierCount, int CacheHitCount, int InvalidatedModifierCount, IReadOnlyList<RekallAgeModifierEvaluationItem> Modifiers, IReadOnlyList<RekallAgeModelingGraphDiagnostic> Diagnostics);
public sealed record EvaluateModifierStackRequest(string ProjectRoot, string AssetId, RekallAgeModelingEvaluationBudget? Budget = null);
public sealed record EvaluateModifierStackResult(RekallAgeModifierEvaluationSummary Evaluation);
public sealed class EvaluateModifierStackCommand : IRekallAgeCommand<EvaluateModifierStackRequest, EvaluateModifierStackResult>
{
    private readonly RekallAgeModifierStackEvaluationService _service; public EvaluateModifierStackCommand(RekallAgeModifierStackEvaluator evaluator) => _service = new(evaluator); public string Name => "rekall.modifier.stack.evaluate";
    public RekallAgeCommandSchema Schema => new(Name, "Previews a modifier stack without persistence and returns bounded topology/bounds, cache/invalidation, timings, and diagnostics without mesh buffers.", typeof(EvaluateModifierStackRequest).FullName!, typeof(EvaluateModifierStackResult).FullName!);
    public async ValueTask<RekallAgeCommandResult<EvaluateModifierStackResult>> ExecuteAsync(EvaluateModifierStackRequest request, RekallAgeCommandContext context)
    { var report = await _service.EvaluateAsync(request.ProjectRoot, request.AssetId, request.Budget ?? RekallAgeModelingEvaluationBudget.Default, context.CancellationToken); var value = new EvaluateModifierStackResult(AdvancedModelingEvidence.ModifierEvaluation(report)); return report.Succeeded ? RekallAgeCommandResult<EvaluateModifierStackResult>.Success(value, $"Evaluated modifier stack '{request.AssetId}'.") : RekallAgeCommandResult<EvaluateModifierStackResult>.Failure(value, "Modifier evaluation failed.", report.Diagnostics.Select(item => new RekallAgeCommandError(item.Code, item.Message, item.NodeId)).ToArray()); }
}
public sealed record BakeModifierStackRequest(string ProjectRoot, string AssetId, string TargetMeshAssetId, string ExpectedTargetRevision, RekallAgeModelingEvaluationBudget? Budget = null);
public sealed record BakeModifierStackResult(string StackAssetId, long StackLogicalRevision, string TargetMeshAssetId, string BeforeFileRevision, string AfterFileRevision, RekallAgeModelingOutputSummary Mesh, RekallAgeModifierEvaluationSummary Evaluation);
public sealed class BakeModifierStackCommand : IRekallAgeCommand<BakeModifierStackRequest, BakeModifierStackResult>
{
    private readonly RekallAgeModifierStackBakeService _service; public BakeModifierStackCommand(RekallAgeModifierStackEvaluator evaluator) => _service = new(evaluator); public string Name => "rekall.modifier.stack.bake";
    public RekallAgeCommandSchema Schema => new(Name, "Evaluates a modifier stack and atomically bakes it through the strict editable mesh store against an exact target revision with transaction evidence.", typeof(BakeModifierStackRequest).FullName!, typeof(BakeModifierStackResult).FullName!);
    public async ValueTask<RekallAgeCommandResult<BakeModifierStackResult>> ExecuteAsync(BakeModifierStackRequest request, RekallAgeCommandContext context)
    { var result = await _service.BakeAsync(request.ProjectRoot, request.AssetId, request.TargetMeshAssetId, request.ExpectedTargetRevision, request.Budget ?? RekallAgeModelingEvaluationBudget.Default, context.Transaction, context.CancellationToken); return RekallAgeCommandResult<BakeModifierStackResult>.Success(new(result.StackAssetId, result.StackLogicalRevision, request.TargetMeshAssetId, result.BeforeFileRevision, result.AfterFileRevision, AdvancedModelingEvidence.Mesh("mesh", result.Mesh), AdvancedModelingEvidence.ModifierEvaluation(result.Evaluation)), $"Baked modifier stack '{request.AssetId}' to mesh '{request.TargetMeshAssetId}'."); }
}

internal static class AdvancedModelingEvidence
{
    public static RekallAgeMaterialGraphSummary MaterialGraph(RekallAgeMaterialGraphAsset graph, string revision, int limit) => new(graph.AssetId, graph.Name, revision, graph.Revision, graph.Nodes.Count, graph.Links.Count, graph.ExposedParameters.Count, graph.Nodes.Take(limit).ToArray(), graph.Links.Take(limit).ToArray(), graph.Output, graph.Nodes.Count > limit || graph.Links.Count > limit);
    public static RekallAgeMaterialInstanceSummary Instance(RekallAgeMaterialInstanceAsset instance, string revision) => new(instance.AssetId, instance.Name, revision, instance.Revision, instance.GraphAssetId, instance.GraphFileRevision, instance.Overrides.Take(512).ToDictionary(item => item.Key, item => item.Value?.DeepClone(), StringComparer.Ordinal));
    public static RekallAgeModifierStackSummary Stack(RekallAgeModifierStackAsset stack, string revision, int limit) => new(stack.AssetId, stack.Name, revision, stack.Revision, stack.SourceMeshAssetId, stack.SourceMeshFileRevision, stack.Modifiers.Take(limit).ToArray(), stack.Modifiers.Count > limit);
    public static RekallAgeModelingOutputSummary Mesh(string name, RekallAgeMeshAsset mesh) { var summary = new RekallAgeMeshValidator().Validate(mesh).Summary; return new(name, summary.PointCount, summary.EdgeCount, summary.FaceCount, summary.CornerCount, summary.Bounds); }
    public static RekallAgeModifierEvaluationSummary ModifierEvaluation(RekallAgeModifierStackEvaluationReport report) => new(report.Succeeded, report.StackAssetId, report.StackLogicalRevision, report.Mesh is null ? null : Mesh("mesh", report.Mesh), report.EvaluatedModifierCount, report.CacheHitCount, report.InvalidatedModifierCount, report.Modifiers.Take(256).ToArray(), report.Diagnostics.Take(128).ToArray());
}
