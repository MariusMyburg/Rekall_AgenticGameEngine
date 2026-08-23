using System.Text.Json.Nodes;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class ModelingGraphPersistenceTests
{
    [Fact]
    public async Task StorePersistsStrictCanonicalGraphAndRejectsStaleRevisionWithoutOverwrite()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeModelingGraphAssetStore();
        var graph = Graph();
        await store.SaveAsync(root, graph, CancellationToken.None);
        var first = await store.LoadVersionedAsync(root, graph.AssetId, CancellationToken.None);
        var changed = graph with { Name = "Changed", Revision = 2 };

        await store.SaveIfRevisionAsync(root, changed, first.Revision, CancellationToken.None);
        var conflict = await Assert.ThrowsAsync<RekallAgeDocumentRevisionException>(() =>
            store.SaveIfRevisionAsync(root, graph with { Name = "Stale", Revision = 2 }, first.Revision, CancellationToken.None).AsTask());

        Assert.Equal("REKALL_DOCUMENT_REVISION_CONFLICT", conflict.Code);
        Assert.Equal("Changed", (await store.LoadAsync(root, graph.AssetId, CancellationToken.None)).Name);
        Assert.Equal([graph.AssetId], store.ListAssetIds(root));
        Assert.EndsWith(Path.Combine("Modeling", "Graphs", "room.age.modeling-graph.json"), store.GetGraphPath(root, graph.AssetId));
    }

    [Fact]
    public async Task PatchServicePublishesValidatedBatchOnceAndCapturesUndoPreimage()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeModelingGraphAssetStore();
        await store.SaveAsync(root, Graph(), CancellationToken.None);
        var before = await store.LoadVersionedAsync(root, "room", CancellationToken.None);
        var transaction = RekallAgeTransaction.Begin("patch graph");

        var result = await new RekallAgeModelingGraphPatchService().ApplyAsync(
            root,
            "room",
            before.Revision,
            new([
                new(RekallAgeModelingGraphPatchKind.SetParameter, TargetId: "box", ParameterId: "sizeX", Value: JsonValue.Create(8.0)),
                new(RekallAgeModelingGraphPatchKind.AddNode, Node: new("move", "rekall.modeling.transform", 1, new JsonObject())),
                new(RekallAgeModelingGraphPatchKind.RemoveLink, TargetId: "box-output"),
                new(RekallAgeModelingGraphPatchKind.AddLink, Link: new("box-move", "box", "geometry", "move", "geometry")),
                new(RekallAgeModelingGraphPatchKind.AddLink, Link: new("move-output", "move", "geometry", "output", "input"))
            ]),
            transaction,
            CancellationToken.None);

        Assert.Equal(2, result.Graph.Revision);
        Assert.Equal(["box", "move", "output"], result.Validation.ExecutionPlan!.OrderedNodeIds);
        Assert.Equal(8.0, result.Graph.Nodes.Single(node => node.NodeId == "box").Parameters["sizeX"]!.GetValue<double>());
        Assert.Single(transaction.ResourcePreimages);
        Assert.Single(transaction.ChangedResources);
    }

    [Fact]
    public async Task InvalidPatchPublishesNothing()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeModelingGraphAssetStore();
        await store.SaveAsync(root, Graph(), CancellationToken.None);
        var before = await store.LoadVersionedAsync(root, "room", CancellationToken.None);
        var beforeBytes = await File.ReadAllBytesAsync(store.GetGraphPath(root, "room"));

        var error = await Assert.ThrowsAsync<RekallAgeModelingGraphPatchException>(() =>
            new RekallAgeModelingGraphPatchService().ApplyAsync(
                root,
                "room",
                before.Revision,
                new([new(RekallAgeModelingGraphPatchKind.AddLink, Link: new("cycle", "output", "geometry", "output", "input"))]),
                RekallAgeTransaction.Begin("invalid patch"),
                CancellationToken.None).AsTask());

        Assert.Equal("REKALL_MODELING_GRAPH_PATCH_INVALID", error.Code);
        Assert.Equal(beforeBytes, await File.ReadAllBytesAsync(store.GetGraphPath(root, "room")));
    }

    private static RekallAgeModelingGraphAsset Graph() => RekallAgeModelingGraphAsset.Create(
        "room",
        "Room",
        [
            new("box", "rekall.modeling.primitive.box", 1, new JsonObject { ["sizeX"] = 4.0 }),
            new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
        ],
        [new("box-output", "box", "geometry", "output", "input")],
        [new("mesh", "output", "geometry")]);
}
