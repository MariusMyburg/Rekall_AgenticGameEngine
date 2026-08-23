using System.Text.Json.Nodes;
using System.IO;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Studio;

public sealed class RekallAgeStudioModelingSession
{
    private readonly RekallAgeMeshAssetStore _store;
    private readonly RekallAgeMeshEditService _edits;
    private readonly RekallAgeMeshOperationExecutor _operations;
    private readonly RekallAgeTransactionLogStore _transactions;
    private readonly List<ulong> _selectionHistory = [];

    public RekallAgeStudioModelingSession(
        RekallAgeMeshAssetStore? store = null,
        RekallAgeMeshEditService? edits = null,
        RekallAgeMeshOperationExecutor? operations = null,
        RekallAgeTransactionLogStore? transactions = null)
    {
        _store = store ?? new RekallAgeMeshAssetStore();
        _operations = operations ?? new RekallAgeMeshOperationExecutor();
        _edits = edits ?? new RekallAgeMeshEditService(_store, _operations);
        _transactions = transactions ?? new RekallAgeTransactionLogStore();
    }

    public string? ProjectRoot { get; private set; }
    public string? AssetId { get; private set; }
    public string? FileRevision { get; private set; }
    public RekallAgeMeshAsset? Mesh { get; private set; }
    public RekallAgeMeshOperationResult? Preview { get; private set; }
    public RekallAgeGeometryDomain Domain { get; private set; } = RekallAgeGeometryDomain.Face;
    public IReadOnlyList<ulong> SelectedElementIds => _selectionHistory;
    public ulong? ActiveElementId => _selectionHistory.Count == 0 ? null : _selectionHistory[^1];
    public IReadOnlyList<RekallAgeMeshOperationDescriptor> AvailableOperations => _operations.Descriptors
        .Where(item => item.Domain == Domain).OrderBy(item => item.OperationId, StringComparer.Ordinal).ToArray();

    public IReadOnlyList<string> ListAssets(string projectRoot) => _store.ListAssetIds(projectRoot);

    public async ValueTask OpenAsync(string projectRoot, string assetId, CancellationToken cancellationToken)
    {
        var loaded = await _store.LoadVersionedAsync(projectRoot, assetId, cancellationToken).ConfigureAwait(false);
        ProjectRoot = Path.GetFullPath(projectRoot); AssetId = assetId; FileRevision = loaded.Revision; Mesh = loaded.Value;
        Preview = null; _selectionHistory.Clear();
    }

    public void SetDomain(RekallAgeGeometryDomain domain)
    {
        if (domain is not (RekallAgeGeometryDomain.Point or RekallAgeGeometryDomain.Edge or RekallAgeGeometryDomain.Face or RekallAgeGeometryDomain.Corner))
            throw new ArgumentOutOfRangeException(nameof(domain));
        Domain = domain; Preview = null; _selectionHistory.Clear();
    }

    public void Select(ulong elementId, bool extend = false, bool toggle = false)
    {
        var available = DomainIds();
        if (!available.Contains(elementId)) throw new ArgumentException($"Element ID '{elementId}' does not exist in the active {Domain} domain.", nameof(elementId));
        Preview = null;
        if (!extend && !toggle) _selectionHistory.Clear();
        var existing = _selectionHistory.IndexOf(elementId);
        if (toggle && existing >= 0) { _selectionHistory.RemoveAt(existing); return; }
        if (existing >= 0) _selectionHistory.RemoveAt(existing);
        _selectionHistory.Add(elementId);
    }

    public void ClearSelection() { Preview = null; _selectionHistory.Clear(); }

    public async ValueTask<RekallAgeMeshOperationResult> PreviewAsync(
        string operationId,
        JsonObject parameters,
        CancellationToken cancellationToken)
    {
        EnsureOpen();
        var operation = BuildRequest(operationId, parameters);
        var transaction = RekallAgeTransaction.Begin($"Preview mesh operation {operationId}");
        var result = await _edits.PreviewAsync(ProjectRoot!, AssetId!, FileRevision!, operation, transaction, cancellationToken).ConfigureAwait(false);
        Preview = result.Operation;
        return Preview;
    }

    public async ValueTask<RekallAgeMeshOperationResult> ApplyAsync(
        string operationId,
        JsonObject parameters,
        string actor,
        CancellationToken cancellationToken)
    {
        EnsureOpen();
        var operation = BuildRequest(operationId, parameters);
        var transaction = RekallAgeTransaction.Begin($"Studio mesh operation {operationId}");
        var result = await _edits.ApplyAsync(ProjectRoot!, AssetId!, FileRevision!, operation, transaction, cancellationToken).ConfigureAwait(false);
        await _transactions.AppendAsync(ProjectRoot!, transaction, actor, cancellationToken).ConfigureAwait(false);
        var loaded = await _store.LoadVersionedAsync(ProjectRoot!, AssetId!, cancellationToken).ConfigureAwait(false);
        FileRevision = loaded.Revision; Mesh = loaded.Value; Preview = null;
        _selectionHistory.Clear();
        return result.Operation;
    }

    public void CancelPreview() => Preview = null;

    private RekallAgeMeshOperationRequest BuildRequest(string operationId, JsonObject parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId); ArgumentNullException.ThrowIfNull(parameters);
        var descriptor = _operations.Descriptors.SingleOrDefault(item => item.OperationId == operationId)
            ?? throw new ArgumentException($"Unknown mesh operation '{operationId}'.", nameof(operationId));
        if (descriptor.Domain != Domain) throw new InvalidOperationException($"Operation '{operationId}' requires {descriptor.Domain} edit mode.");
        var ids = _selectionHistory.Count > 0 ? _selectionHistory.ToArray() : DomainIds().ToArray();
        return new(operationId, Domain, ids, (JsonObject)parameters.DeepClone());
    }

    private IReadOnlyList<ulong> DomainIds()
    {
        EnsureOpen();
        return Domain switch
        {
            RekallAgeGeometryDomain.Point => Mesh!.Topology.PointIds,
            RekallAgeGeometryDomain.Edge => Mesh!.Topology.EdgeIds,
            RekallAgeGeometryDomain.Face => Mesh!.Topology.FaceIds,
            RekallAgeGeometryDomain.Corner => Mesh!.Topology.CornerIds,
            _ => []
        };
    }

    private void EnsureOpen()
    {
        if (Mesh is null || ProjectRoot is null || AssetId is null || FileRevision is null)
            throw new InvalidOperationException("Open a mesh asset before editing geometry.");
    }
}
