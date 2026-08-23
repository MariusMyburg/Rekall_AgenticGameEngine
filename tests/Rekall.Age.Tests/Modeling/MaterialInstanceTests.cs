using System.Text.Json.Nodes;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class MaterialInstanceTests
{
    [Fact]
    public async Task InstancePersistsTypedOverridesAgainstExactGraphRevisionAndResolvesImmutably()
    {
        var root = TestPaths.CreateTempDirectory();
        var graphStore = new RekallAgeMaterialGraphAssetStore();
        var graph = Graph();
        var graphRevision = await graphStore.SaveIfRevisionAsync(root, graph, RekallAgeDocumentRevision.Missing, CancellationToken.None);
        var instance = RekallAgeMaterialInstanceAsset.Create(
            "polished-stone", "Polished Stone", graph.AssetId, graphRevision,
            new Dictionary<string, JsonNode?> { ["Roughness"] = 0.25 });
        var instanceStore = new RekallAgeMaterialInstanceAssetStore();

        var instanceRevision = await instanceStore.SaveIfRevisionAsync(root, instance, RekallAgeDocumentRevision.Missing, CancellationToken.None);
        var loaded = await instanceStore.LoadVersionedAsync(root, instance.AssetId, CancellationToken.None);
        var resolved = new RekallAgeMaterialInstanceResolver().Resolve(graph, graphRevision, loaded.Value);

        Assert.Equal(instanceRevision, loaded.Revision);
        Assert.Equal(1.0, graph.Nodes.Single(item => item.NodeId == "pbr").Parameters["roughness"]!.GetValue<double>());
        Assert.Equal(0.25, resolved.Nodes.Single(item => item.NodeId == "pbr").Parameters["roughness"]!.GetValue<double>());
    }

    [Fact]
    public async Task InstanceRejectsUnknownOverrideAndStaleGraphRevisionWithoutPublishing()
    {
        var root = TestPaths.CreateTempDirectory();
        var graphStore = new RekallAgeMaterialGraphAssetStore();
        var graph = Graph();
        var graphRevision = await graphStore.SaveIfRevisionAsync(root, graph, RekallAgeDocumentRevision.Missing, CancellationToken.None);
        var store = new RekallAgeMaterialInstanceAssetStore();

        await Assert.ThrowsAsync<InvalidDataException>(async () => await store.SaveIfRevisionAsync(root,
            RekallAgeMaterialInstanceAsset.Create("bad", "Bad", graph.AssetId, graphRevision,
                new Dictionary<string, JsonNode?> { ["Unknown"] = 1.0 }),
            RekallAgeDocumentRevision.Missing, CancellationToken.None));
        await Assert.ThrowsAsync<RekallAgeDocumentRevisionException>(async () => await store.SaveIfRevisionAsync(root,
            RekallAgeMaterialInstanceAsset.Create("stale", "Stale", graph.AssetId, new string('0', 64),
                new Dictionary<string, JsonNode?> { ["Roughness"] = 0.5 }),
            RekallAgeDocumentRevision.Missing, CancellationToken.None));
        Assert.Empty(store.ListAssetIds(root));
    }

    private static RekallAgeMaterialGraphAsset Graph() => RekallAgeMaterialGraphAsset.Create(
        "base-stone", "Base Stone",
        [
            new("pbr", "rekall.material.surface.pbr", 1, new JsonObject { ["roughness"] = 1.0 }),
            new("output", "rekall.material.output", 1, new JsonObject())
        ],
        [new("pbr-output", "pbr", "surface", "output", "surface")],
        new("surface", "output", "surface"),
        [new("Roughness", "pbr", "roughness", RekallAgeMaterialValueType.Float, 1.0)]);
}
