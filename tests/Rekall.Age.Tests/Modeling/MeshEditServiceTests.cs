using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class MeshEditServiceTests
{
    [Fact]
    public async Task MeshEditPersistsCompactReversibleDeltaSmallerThanFullPreimage()
    {
        var (root, store, initial) = await CreateStoredTriangle();
        var padded = initial.Value with
        {
            Revision = 2,
            SelectionSets = Enumerable.Range(0, 128)
                .Select(index => new RekallAgeMeshSelection(
                    $"selection-{index:D3}",
                    RekallAgeGeometryDomain.Point,
                    [1, 2, 3]))
                .ToArray()
        };
        await store.SaveIfRevisionAsync(root, padded, initial.Revision, CancellationToken.None);
        var loaded = await store.LoadVersionedAsync(root, "triangle", CancellationToken.None);
        var edit = RekallAgeTransaction.Begin("compact mesh edit");
        await new RekallAgeMeshEditService(store).ApplyAsync(
            root,
            "triangle",
            loaded.Revision,
            Transform(0.25, 0),
            edit,
            CancellationToken.None);
        await new RekallAgeTransactionLogStore().AppendAsync(root, edit, "agent", CancellationToken.None);

        var log = JsonNode.Parse(await File.ReadAllTextAsync(
            new RekallAgeTransactionLogStore().GetPath(root),
            CancellationToken.None))!.AsObject();
        var transaction = log["transactions"]!.AsArray().Single()!.AsObject();
        var preimageBytes = transaction["resourcePreimages"]!.AsArray().Single()!["sizeBytes"]!.GetValue<long>();
        var delta = transaction["resourceDeltas"]!.AsArray().Single()!.AsObject();

        Assert.Equal("reversible-json-splice-v1", delta["format"]!.GetValue<string>());
        Assert.True(delta["encodedSizeBytes"]!.GetValue<long>() < preimageBytes);
        Assert.NotEmpty(delta["operations"]!.AsArray());
        Assert.NotNull(delta["beforeSha256"]);
        Assert.NotNull(delta["afterSha256"]);
    }

    [Fact]
    public async Task MeshUndoRestoresExactAssetFromDeltaWhenSnapshotFallbackIsUnavailable()
    {
        var (root, store, loaded) = await CreateStoredTriangle();
        var beforeBytes = await File.ReadAllBytesAsync(store.GetMeshPath(root, "triangle"), CancellationToken.None);
        var edit = RekallAgeTransaction.Begin("delta-backed mesh edit");
        await new RekallAgeMeshEditService(store).ApplyAsync(
            root,
            "triangle",
            loaded.Revision,
            Transform(2, 0),
            edit,
            CancellationToken.None);
        var history = new RekallAgeTransactionLogStore();
        await history.AppendAsync(root, edit, "agent", CancellationToken.None);
        var document = await history.LoadAsync(root, CancellationToken.None);
        var preimage = Assert.Single(Assert.Single(document.Transactions).ResourcePreimages);
        File.Delete(Path.Combine(root, preimage.SnapshotPath!));
        var undo = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("delta undo"), CancellationToken.None);

        var result = await new RestoreTransactionPreimageCommand(history).ExecuteAsync(
            new(root, edit.Id, Path.GetRelativePath(root, store.GetMeshPath(root, "triangle"))),
            undo);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(beforeBytes, await File.ReadAllBytesAsync(store.GetMeshPath(root, "triangle"), CancellationToken.None));
    }

    [Fact]
    public async Task GroupedMeshEditSupportsTransactionUndoAndRedoWithoutPartialTopology()
    {
        var (root, store, loaded) = await CreateStoredTriangle();
        var service = new RekallAgeMeshEditService(store);
        var edit = RekallAgeTransaction.Begin("grouped mesh edit");
        await service.ApplyBatchAsync(
            root,
            "triangle",
            loaded.Revision,
            [Transform(2, 0), Reverse()],
            edit,
            CancellationToken.None);
        var edited = await store.LoadAsync(root, "triangle", CancellationToken.None);
        var history = new RekallAgeTransactionLogStore();
        await history.AppendAsync(root, edit, "agent", CancellationToken.None);
        var relativePath = Path.GetRelativePath(root, store.GetMeshPath(root, "triangle"));
        var undo = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("undo grouped mesh edit"), CancellationToken.None);

        var undone = await new RestoreTransactionPreimageCommand(history).ExecuteAsync(
            new(root, edit.Id, relativePath),
            undo);

        Assert.True(undone.Ok, undone.Summary);
        var restored = await store.LoadAsync(root, "triangle", CancellationToken.None);
        Assert.Equal(new RekallAgeGeometryVector3(0, 0, 0), restored.Topology.Positions[0]);
        Assert.Equal([31UL, 32UL, 33UL], restored.Topology.CornerIds);
        await history.AppendAsync(root, undo.Transaction, "agent", CancellationToken.None);
        var redo = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("redo grouped mesh edit"), CancellationToken.None);

        var redone = await new RestoreTransactionPreimageCommand(history).ExecuteAsync(
            new(root, undo.Transaction.Id, relativePath),
            redo);

        Assert.True(redone.Ok, redone.Summary);
        var reapplied = await store.LoadAsync(root, "triangle", CancellationToken.None);
        Assert.Equal(edited.Topology.Positions, reapplied.Topology.Positions);
        Assert.Equal(edited.Topology.CornerIds, reapplied.Topology.CornerIds);
        Assert.Equal(edited.Topology.CornerPointIndices, reapplied.Topology.CornerPointIndices);
        Assert.Single(redo.Transaction.ResourcePreimages);
    }

    [Fact]
    public async Task PreviewReturnsEvidenceWithoutWritingOrTouchingTransaction()
    {
        var (root, store, loaded) = await CreateStoredTriangle();
        var transaction = RekallAgeTransaction.Begin("preview mesh");
        var service = new RekallAgeMeshEditService(store);

        var result = await service.PreviewAsync(
            root,
            "triangle",
            loaded.Revision,
            Transform(1, 2),
            transaction,
            CancellationToken.None);

        Assert.False(result.Persisted);
        Assert.Equal(loaded.Revision, result.BeforeFileRevision);
        Assert.Equal(loaded.Revision, result.AfterFileRevision);
        Assert.Equal(2, result.Operation.AfterRevision);
        Assert.Equal(new RekallAgeGeometryVector3(1, 2, 0), result.Operation.Mesh.Topology.Positions[0]);
        Assert.Equal(new RekallAgeGeometryVector3(0, 0, 0), (await store.LoadAsync(root, "triangle", CancellationToken.None)).Topology.Positions[0]);
        Assert.Empty(transaction.ChangedResources);
        Assert.Empty(transaction.ResourcePreimages);
    }

    [Fact]
    public async Task ApplyWritesOnceWithPreimageAndRejectsStaleRevisionWithoutMutation()
    {
        var (root, store, loaded) = await CreateStoredTriangle();
        var transaction = RekallAgeTransaction.Begin("apply mesh");
        var service = new RekallAgeMeshEditService(store);

        var applied = await service.ApplyAsync(
            root,
            "triangle",
            loaded.Revision,
            Transform(3, 0),
            transaction,
            CancellationToken.None);
        var persisted = await store.LoadVersionedAsync(root, "triangle", CancellationToken.None);

        Assert.True(applied.Persisted);
        Assert.NotEqual(applied.BeforeFileRevision, applied.AfterFileRevision);
        Assert.Equal(2, persisted.Value.Revision);
        Assert.Equal(new RekallAgeGeometryVector3(3, 0, 0), persisted.Value.Topology.Positions[0]);
        Assert.Equal([store.GetMeshPath(root, "triangle")], transaction.ChangedResources);
        Assert.Single(transaction.ResourcePreimages);
        var staleTransaction = RekallAgeTransaction.Begin("stale mesh");
        var stale = await Assert.ThrowsAsync<RekallAgeDocumentRevisionException>(() => service.ApplyAsync(
            root,
            "triangle",
            loaded.Revision,
            Transform(100, 0),
            staleTransaction,
            CancellationToken.None).AsTask());
        Assert.Equal("REKALL_DOCUMENT_REVISION_CONFLICT", stale.Code);
        Assert.Empty(staleTransaction.ChangedResources);
        Assert.Empty(staleTransaction.ResourcePreimages);
        Assert.Equal(new RekallAgeGeometryVector3(3, 0, 0), (await store.LoadAsync(root, "triangle", CancellationToken.None)).Topology.Positions[0]);
    }

    [Fact]
    public async Task BatchPublishesOneRevisionAndFailedLaterStepRollsBackEntireCandidate()
    {
        var (root, store, loaded) = await CreateStoredTriangle();
        var service = new RekallAgeMeshEditService(store);
        var transaction = RekallAgeTransaction.Begin("batch mesh");

        var batch = await service.ApplyBatchAsync(
            root,
            "triangle",
            loaded.Revision,
            [Transform(2, 0), Reverse()],
            transaction,
            CancellationToken.None);
        var persisted = await store.LoadVersionedAsync(root, "triangle", CancellationToken.None);

        Assert.True(batch.Persisted);
        Assert.Equal(2, batch.Steps.Count);
        Assert.Equal(2, batch.AfterLogicalRevision);
        Assert.Equal(2, persisted.Value.Revision);
        Assert.Equal(new RekallAgeGeometryVector3(2, 0, 0), persisted.Value.Topology.Positions[0]);
        Assert.Single(transaction.ResourcePreimages);
        Assert.Single(transaction.ChangedResources);

        var failedTransaction = RekallAgeTransaction.Begin("failed batch");
        var beforeFailure = await store.LoadVersionedAsync(root, "triangle", CancellationToken.None);
        var error = await Assert.ThrowsAsync<RekallAgeMeshOperationException>(() => service.ApplyBatchAsync(
            root,
            "triangle",
            beforeFailure.Revision,
            [Transform(5, 0), new("transform", RekallAgeGeometryDomain.Point, [999], new JsonObject { ["x"] = 1 })],
            failedTransaction,
            CancellationToken.None).AsTask());

        Assert.Equal("REKALL_MESH_OPERATION_SELECTION_INVALID", error.Code);
        Assert.Empty(failedTransaction.ChangedResources);
        Assert.Empty(failedTransaction.ResourcePreimages);
        var afterFailure = await store.LoadVersionedAsync(root, "triangle", CancellationToken.None);
        Assert.Equal(beforeFailure.Revision, afterFailure.Revision);
        Assert.Equal(beforeFailure.Value.Topology.Positions, afterFailure.Value.Topology.Positions);
    }

    private static RekallAgeMeshOperationRequest Transform(double x, double y) =>
        new("transform", RekallAgeGeometryDomain.Point, [1], new JsonObject { ["x"] = x, ["y"] = y });

    private static RekallAgeMeshOperationRequest Reverse() =>
        new("reverse_faces", RekallAgeGeometryDomain.Face, [21], new JsonObject());

    private static async Task<(string Root, RekallAgeMeshAssetStore Store, RekallAgeVersionedDocument<RekallAgeMeshAsset> Loaded)> CreateStoredTriangle()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeMeshAssetStore();
        await store.SaveAsync(
            root,
            RekallAgeMeshAsset.Create(
                "triangle",
                "Triangle",
                new RekallAgeMeshTopology(
                    PointIds: [1, 2, 3],
                    Positions: [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
                    EdgeIds: [11, 12, 13],
                    EdgePointIndices: [new(0, 1), new(1, 2), new(2, 0)],
                    FaceIds: [21],
                    FaceOffsets: [0, 3],
                    CornerIds: [31, 32, 33],
                    CornerPointIndices: [0, 1, 2],
                    CornerEdgeIndices: [0, 1, 2])),
            CancellationToken.None);
        return (root, store, await store.LoadVersionedAsync(root, "triangle", CancellationToken.None));
    }
}
