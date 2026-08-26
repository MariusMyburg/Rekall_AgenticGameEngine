using System.IO;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Studio;

public sealed record RekallAgeStudioMaterialGraphParameterView(
    string ParameterId,
    string DisplayName,
    RekallAgeMaterialValueType ValueType,
    string Value,
    string? Description);

public sealed class RekallAgeStudioMaterialGraphNodeView
{
    internal RekallAgeStudioMaterialGraphNodeView(
        RekallAgeMaterialGraphNode node,
        RekallAgeMaterialNodeDescriptor descriptor,
        IReadOnlyList<RekallAgeStudioMaterialGraphParameterView> parameters,
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
    public IReadOnlyList<RekallAgeStudioMaterialGraphParameterView> Parameters { get; }
    public int IncomingLinkCount { get; }
    public int OutgoingLinkCount { get; }
}

/// <summary>
/// Same role as <see cref="RekallAgeStudioModelingGraphSession"/> for material graph assets:
/// open, browse node contracts with typed parameter editors, and apply revision-safe patches.
/// There is no material-to-pixel evaluator in the engine yet, so unlike the modeling graph
/// session this exposes no evaluated preview - tracked as a follow-up in PROGRESS.md.
/// </summary>
public sealed class RekallAgeStudioMaterialGraphSession
{
    private readonly RekallAgeMaterialGraphAssetStore _store;
    private readonly RekallAgeMaterialNodeCatalog _catalog;
    private readonly RekallAgeMaterialGraphPatchService _patches;
    private readonly RekallAgeTransactionLogStore _transactions;

    public RekallAgeStudioMaterialGraphSession(
        RekallAgeMaterialGraphAssetStore? store = null,
        RekallAgeMaterialNodeCatalog? catalog = null,
        RekallAgeMaterialGraphPatchService? patches = null,
        RekallAgeTransactionLogStore? transactions = null)
    {
        _store = store ?? new RekallAgeMaterialGraphAssetStore();
        _catalog = catalog ?? RekallAgeMaterialNodeCatalog.CreateDefault();
        _patches = patches ?? new RekallAgeMaterialGraphPatchService();
        _transactions = transactions ?? new RekallAgeTransactionLogStore();
    }

    public string? ProjectRoot { get; private set; }
    public string? FileRevision { get; private set; }
    public RekallAgeMaterialGraphAsset? Graph { get; private set; }
    public IReadOnlyList<RekallAgeStudioMaterialGraphNodeView> Nodes { get; private set; } = [];
    public string EvaluationSummary { get; private set; } = "Open a material graph to inspect its node contracts.";

    public IReadOnlyList<string> ListAssets(string projectRoot) => _store.ListAssetIds(projectRoot);

    public async ValueTask OpenAsync(string projectRoot, string assetId, CancellationToken cancellationToken)
    {
        var loaded = await _store.LoadVersionedAsync(projectRoot, assetId, cancellationToken).ConfigureAwait(false);
        ProjectRoot = Path.GetFullPath(projectRoot);
        FileRevision = loaded.Revision;
        Graph = loaded.Value;
        Nodes = BuildNodeViews(Graph);
        EvaluationSummary = $"{Graph.Nodes.Count} node(s), {Graph.Links.Count} link(s). Output: {Graph.Output.Name}.";
    }

    public async ValueTask<RekallAgeMaterialGraphPatchResult> ApplyPatchAsync(
        RekallAgeMaterialGraphPatch patch,
        string actor,
        CancellationToken cancellationToken)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(patch);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var transaction = RekallAgeTransaction.Begin($"Studio patch material graph {Graph!.AssetId}");
        var result = await _patches.ApplyAsync(
            ProjectRoot!, Graph.AssetId, FileRevision!, patch, transaction, cancellationToken).ConfigureAwait(false);
        await _transactions.AppendAsync(ProjectRoot!, transaction, actor, cancellationToken).ConfigureAwait(false);
        Graph = result.Graph;
        FileRevision = result.AfterFileRevision;
        Nodes = BuildNodeViews(Graph);
        EvaluationSummary = $"Applied {result.AppliedOperationCount} material graph operation(s); logical revision {Graph.Revision}.";
        return result;
    }

    public IReadOnlyList<RekallAgeStudioMaterialGraphParameterModel> CreateParameterEditors(string nodeId)
    {
        EnsureOpen();
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        var node = Graph!.Nodes.SingleOrDefault(item => item.NodeId.Equals(nodeId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Node '{nodeId}' does not exist in graph '{Graph.AssetId}'.", nameof(nodeId));
        var descriptor = _catalog.Find(node.TypeId, node.TypeVersion)
            ?? throw new InvalidDataException($"Unknown material node type '{node.TypeId}' version {node.TypeVersion}.");
        return descriptor.Parameters.Select(parameter => new RekallAgeStudioMaterialGraphParameterModel(
            parameter,
            node.Parameters[parameter.ParameterId] ?? parameter.DefaultValue)).ToArray();
    }

    public async ValueTask<RekallAgeMaterialGraphPatchResult> ApplyParameterEditsAsync(
        string nodeId,
        IReadOnlyList<RekallAgeStudioMaterialGraphParameterModel> editors,
        string actor,
        CancellationToken cancellationToken)
    {
        EnsureOpen();
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentNullException.ThrowIfNull(editors);
        var operations = new List<RekallAgeMaterialGraphPatchOperation>();
        foreach (var editor in editors)
        {
            if (!editor.TryGetValue(out var value))
                throw new ArgumentException($"Parameter '{editor.ParameterId}' is invalid.", nameof(editors));
            if (editor.IsModified)
                operations.Add(new(
                    RekallAgeMaterialGraphPatchKind.SetParameter,
                    TargetId: nodeId,
                    ParameterId: editor.ParameterId,
                    Value: value));
        }
        if (operations.Count == 0)
            throw new InvalidOperationException("Change at least one node parameter before applying material graph edits.");
        var result = await ApplyPatchAsync(new(operations), actor, cancellationToken).ConfigureAwait(false);
        foreach (var editor in editors) editor.AcceptChanges();
        return result;
    }

    private IReadOnlyList<RekallAgeStudioMaterialGraphNodeView> BuildNodeViews(RekallAgeMaterialGraphAsset graph) =>
        graph.Nodes.Select(node =>
        {
            var descriptor = _catalog.Find(node.TypeId, node.TypeVersion)
                ?? throw new InvalidDataException($"Unknown material node type '{node.TypeId}' version {node.TypeVersion}.");
            var parameters = descriptor.Parameters.Select(parameter => new RekallAgeStudioMaterialGraphParameterView(
                parameter.ParameterId,
                parameter.DisplayName,
                parameter.ValueType,
                (node.Parameters[parameter.ParameterId] ?? parameter.DefaultValue)?.ToJsonString() ?? "null",
                parameter.Description)).ToArray();
            return new RekallAgeStudioMaterialGraphNodeView(
                node,
                descriptor,
                parameters,
                graph.Links.Count(link => link.ToNodeId == node.NodeId),
                graph.Links.Count(link => link.FromNodeId == node.NodeId));
        }).ToArray();

    private void EnsureOpen()
    {
        if (Graph is null || ProjectRoot is null || FileRevision is null)
            throw new InvalidOperationException("Open a material graph before editing it.");
    }
}
