using System.Text.Json.Nodes;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class MaterialGraphPatchTests
{
    [Fact]
    public async Task PatchValidatesCandidateAndPublishesOneAtomicRevision()
    {
        var root = TestPaths.CreateTempDirectory(); var store = new RekallAgeMaterialGraphAssetStore(); var graph = Graph();
        var revision = await store.SaveIfRevisionAsync(root, graph, RekallAgeDocumentRevision.Missing, CancellationToken.None);
        var transaction = RekallAgeTransaction.Begin("material patch");

        var result = await new RekallAgeMaterialGraphPatchService().ApplyAsync(root, graph.AssetId, revision,
            new([new(RekallAgeMaterialGraphPatchKind.SetParameter, TargetId: "pbr", ParameterId: "roughness", Value: 0.25)]),
            transaction, CancellationToken.None);

        Assert.Equal(2, result.Graph.Revision);
        Assert.Equal(0.25, result.Graph.Nodes.Single(item => item.NodeId == "pbr").Parameters["roughness"]!.GetValue<double>());
        Assert.Single(transaction.ResourcePreimages);
    }

    [Fact]
    public async Task InvalidCandidateAndStaleRevisionLeaveBytesUntouched()
    {
        var root = TestPaths.CreateTempDirectory(); var store = new RekallAgeMaterialGraphAssetStore(); var graph = Graph();
        var revision = await store.SaveIfRevisionAsync(root, graph, RekallAgeDocumentRevision.Missing, CancellationToken.None);
        var before = await File.ReadAllBytesAsync(store.GetGraphPath(root, graph.AssetId)); var service = new RekallAgeMaterialGraphPatchService();

        await Assert.ThrowsAsync<InvalidDataException>(async () => await service.ApplyAsync(root, graph.AssetId, revision,
            new([new(RekallAgeMaterialGraphPatchKind.RemoveNode, TargetId: "pbr")]), RekallAgeTransaction.Begin("invalid"), CancellationToken.None));
        await Assert.ThrowsAsync<RekallAgeDocumentRevisionException>(async () => await service.ApplyAsync(root, graph.AssetId, new string('0', 64),
            new([new(RekallAgeMaterialGraphPatchKind.SetParameter, TargetId: "pbr", ParameterId: "roughness", Value: 0.5)]), RekallAgeTransaction.Begin("stale"), CancellationToken.None));
        Assert.Equal(before, await File.ReadAllBytesAsync(store.GetGraphPath(root, graph.AssetId)));
    }

    private static RekallAgeMaterialGraphAsset Graph() => RekallAgeMaterialGraphAsset.Create("material", "Material",
        [new("pbr", "rekall.material.surface.pbr", 1, new JsonObject { ["roughness"] = 1.0 }), new("output", "rekall.material.output", 1, new JsonObject())],
        [new("link", "pbr", "surface", "output", "surface")], new("surface", "output", "surface"));
}
