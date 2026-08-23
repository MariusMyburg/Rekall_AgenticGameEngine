using System.IO;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Studio;

public sealed record RekallAgeStudioModelingGraphParameterView(
    string ParameterId,
    string DisplayName,
    RekallAgeModelingValueType ValueType,
    string Value,
    string? Unit,
    string? Description);

public sealed class RekallAgeStudioModelingGraphNodeView
{
    internal RekallAgeStudioModelingGraphNodeView(
        RekallAgeModelingGraphNode node,
        RekallAgeModelingNodeDescriptor descriptor,
        IReadOnlyList<RekallAgeStudioModelingGraphParameterView> parameters,
        int incomingLinkCount,
        int outgoingLinkCount)
    {
        NodeId = node.NodeId;
        TypeId = node.TypeId;
        TypeVersion = node.TypeVersion;
        DisplayName = descriptor.DisplayName;
        Description = descriptor.Description;
        Parameters = parameters;
        IncomingLinkCount = incomingLinkCount;
        OutgoingLinkCount = outgoingLinkCount;
    }

    public string NodeId { get; }
    public string TypeId { get; }
    public int TypeVersion { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public IReadOnlyList<RekallAgeStudioModelingGraphParameterView> Parameters { get; }
    public int IncomingLinkCount { get; }
    public int OutgoingLinkCount { get; }
    public RekallAgeModelingNodeEvaluationReport? LastEvaluation { get; internal set; }
}

public sealed class RekallAgeStudioModelingGraphSession
{
    private readonly RekallAgeModelingGraphAssetStore _store;
    private readonly RekallAgeModelingNodeCatalog _catalog;
    private readonly RekallAgeModelingGraphEvaluator _evaluator;
    private readonly RekallAgeModelingGraphPatchService _patches;
    private readonly RekallAgeTransactionLogStore _transactions;

    public RekallAgeStudioModelingGraphSession(
        RekallAgeModelingGraphAssetStore? store = null,
        RekallAgeModelingNodeCatalog? catalog = null,
        RekallAgeModelingGraphEvaluator? evaluator = null,
        RekallAgeModelingGraphPatchService? patches = null,
        RekallAgeTransactionLogStore? transactions = null)
    {
        _store = store ?? new RekallAgeModelingGraphAssetStore();
        _catalog = catalog ?? RekallAgeModelingNodeCatalog.CreateDefault();
        _evaluator = evaluator ?? new RekallAgeModelingGraphEvaluator();
        _patches = patches ?? new RekallAgeModelingGraphPatchService();
        _transactions = transactions ?? new RekallAgeTransactionLogStore();
    }

    public string? ProjectRoot { get; private set; }
    public string? FileRevision { get; private set; }
    public RekallAgeModelingGraphAsset? Graph { get; private set; }
    public IReadOnlyList<RekallAgeStudioModelingGraphNodeView> Nodes { get; private set; } = [];
    public IReadOnlyList<string> OutputNames { get; private set; } = [];
    public string? SelectedOutputName { get; private set; }
    public RekallAgeMeshAsset? OutputMesh { get; private set; }
    public RekallAgeModelingGraphEvaluationReport? Evaluation { get; private set; }
    public string EvaluationSummary { get; private set; } = "Open and evaluate a procedural graph to inspect execution evidence.";

    public IReadOnlyList<string> ListAssets(string projectRoot) => _store.ListAssetIds(projectRoot);

    public async ValueTask OpenAsync(string projectRoot, string assetId, CancellationToken cancellationToken)
    {
        var loaded = await _store.LoadVersionedAsync(projectRoot, assetId, cancellationToken).ConfigureAwait(false);
        ProjectRoot = Path.GetFullPath(projectRoot);
        FileRevision = loaded.Revision;
        Graph = loaded.Value;
        OutputNames = Graph.Outputs.Select(output => output.Name).Order(StringComparer.Ordinal).ToArray();
        SelectedOutputName = OutputNames.FirstOrDefault();
        OutputMesh = null;
        Evaluation = null;
        EvaluationSummary = $"{Graph.Nodes.Count} node(s), {Graph.Links.Count} link(s), {Graph.Outputs.Count} output(s). Ready to evaluate.";
        Nodes = BuildNodeViews(Graph);
    }

    public async ValueTask<RekallAgeModelingGraphEvaluationReport> EvaluateAsync(
        string outputName,
        CancellationToken cancellationToken)
    {
        EnsureOpen();
        ArgumentException.ThrowIfNullOrWhiteSpace(outputName);
        var context = new RekallAgeModelingEvaluationContext(
            Seed: 0,
            DeterministicTime: 0,
            EngineVersion: typeof(RekallAgeStudioModelingGraphSession).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            TargetProfile: "studio-preview");
        var report = await _evaluator.EvaluateAsync(
            Graph!, [outputName], RekallAgeModelingEvaluationBudget.Default, context, cancellationToken).ConfigureAwait(false);
        Evaluation = report;
        SelectedOutputName = outputName;
        OutputMesh = report.Outputs.GetValueOrDefault(outputName);
        var reports = report.Nodes.ToDictionary(item => item.NodeId, StringComparer.Ordinal);
        foreach (var node in Nodes)
            node.LastEvaluation = reports.GetValueOrDefault(node.NodeId);
        EvaluationSummary = report.Succeeded
            ? $"Evaluated {report.EvaluatedNodeCount} node(s) in {report.DurationMilliseconds:F2} ms; {report.CacheHitCount} cache hit(s), {report.InvalidatedNodeCount} invalidated."
            : $"Evaluation failed with {report.Diagnostics.Count(item => item.Severity == RekallAgeModelingDiagnosticSeverity.Error)} error(s); last-good output retained: {report.RetainedLastGoodOutputs}.";
        return report;
    }

    public async ValueTask<RekallAgeModelingGraphPatchExecution> ApplyPatchAsync(
        RekallAgeModelingGraphPatch patch,
        string actor,
        CancellationToken cancellationToken)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(patch);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var transaction = RekallAgeTransaction.Begin($"Studio patch modeling graph {Graph!.AssetId}");
        var result = await _patches.ApplyAsync(
            ProjectRoot!, Graph.AssetId, FileRevision!, patch, transaction, cancellationToken).ConfigureAwait(false);
        await _transactions.AppendAsync(ProjectRoot!, transaction, actor, cancellationToken).ConfigureAwait(false);
        Graph = result.Graph;
        FileRevision = result.AfterFileRevision;
        OutputNames = Graph.Outputs.Select(output => output.Name).Order(StringComparer.Ordinal).ToArray();
        if (SelectedOutputName is null || !OutputNames.Contains(SelectedOutputName, StringComparer.Ordinal))
            SelectedOutputName = OutputNames.FirstOrDefault();
        Nodes = BuildNodeViews(Graph);
        Evaluation = null;
        OutputMesh = null;
        EvaluationSummary = $"Applied {result.AppliedOperationCount} graph operation(s); logical revision {Graph.Revision}. Ready to evaluate.";
        return result;
    }

    private IReadOnlyList<RekallAgeStudioModelingGraphNodeView> BuildNodeViews(RekallAgeModelingGraphAsset graph) =>
        graph.Nodes.Select(node =>
        {
            var descriptor = _catalog.Find(node.TypeId, node.TypeVersion)
                ?? throw new InvalidDataException($"Unknown modelling node type '{node.TypeId}' version {node.TypeVersion}.");
            var parameters = descriptor.Parameters.Select(parameter => new RekallAgeStudioModelingGraphParameterView(
                parameter.ParameterId,
                parameter.DisplayName,
                parameter.ValueType,
                (node.Parameters[parameter.ParameterId] ?? parameter.DefaultValue)?.ToJsonString() ?? "null",
                parameter.Unit,
                parameter.Description)).ToArray();
            return new RekallAgeStudioModelingGraphNodeView(
                node,
                descriptor,
                parameters,
                graph.Links.Count(link => link.ToNodeId == node.NodeId),
                graph.Links.Count(link => link.FromNodeId == node.NodeId));
        }).ToArray();

    private void EnsureOpen()
    {
        if (Graph is null || ProjectRoot is null || FileRevision is null)
            throw new InvalidOperationException("Open a procedural graph before evaluating it.");
    }
}
