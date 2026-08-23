using System.Text.Json.Nodes;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class MaterialGraphContractTests
{
    [Fact]
    public void DefaultCatalogExposesSemanticInitialInventory()
    {
        var catalog = RekallAgeMaterialNodeCatalog.CreateDefault();

        Assert.Equal(11, catalog.Descriptors.Count);
        Assert.Contains(catalog.Descriptors, item => item.TypeId == "rekall.material.coordinate.uv");
        Assert.Contains(catalog.Descriptors, item => item.TypeId == "rekall.material.texture.sample");
        Assert.Contains(catalog.Descriptors, item => item.TypeId == "rekall.material.surface.pbr");
        var output = Assert.Single(catalog.Descriptors, item => item.TypeId == "rekall.material.output");
        Assert.Contains(output.Ports, port => port.PortId == "surface" && port.Required && port.ValueType == RekallAgeMaterialValueType.Surface);
    }

    [Fact]
    public void ValidatorBuildsDeterministicPlanAndRejectsTypedMismatchAndCycle()
    {
        var validator = new RekallAgeMaterialGraphValidator(RekallAgeMaterialNodeCatalog.CreateDefault());
        var valid = Graph();

        var report = validator.Validate(valid);

        Assert.True(report.IsValid);
        Assert.Equal(["color", "pbr", "output"], report.ExecutionPlan!.OrderedNodeIds);

        var mismatch = valid with
        {
            Links = [new("bad", "color", "color", "pbr", "metallic"), valid.Links[1]]
        };
        Assert.Contains(validator.Validate(mismatch).Diagnostics, item => item.Code == "REKALL_MATERIAL_GRAPH_LINK_TYPE_MISMATCH");

        var cycle = valid with
        {
            Nodes = [
                .. valid.Nodes,
                new("mapping-a", "rekall.material.mapping", 1, new JsonObject()),
                new("mapping-b", "rekall.material.mapping", 1, new JsonObject())
            ],
            Links = [
                .. valid.Links,
                new("mapping-a-b", "mapping-a", "vector", "mapping-b", "vector"),
                new("mapping-b-a", "mapping-b", "vector", "mapping-a", "vector")
            ]
        };
        var cycleReport = validator.Validate(cycle);
        Assert.False(cycleReport.IsValid);
        Assert.Contains(cycleReport.Diagnostics, item => item.Code == "REKALL_MATERIAL_GRAPH_CYCLE");
    }

    [Fact]
    public async Task StoreRoundTripsCanonicallyAndRejectsStaleRevision()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeMaterialGraphAssetStore();
        var graph = Graph();

        var firstRevision = await store.SaveIfRevisionAsync(root, graph, RekallAgeDocumentRevision.Missing, CancellationToken.None);
        var loaded = await store.LoadVersionedAsync(root, graph.AssetId, CancellationToken.None);

        Assert.Equal(firstRevision, loaded.Revision);
        Assert.Equal(store.Serialize(graph), store.Serialize(loaded.Value));
        await Assert.ThrowsAsync<RekallAgeDocumentRevisionException>(async () =>
            await store.SaveIfRevisionAsync(root, graph with { Revision = 2 }, new string('0', 64), CancellationToken.None));
        Assert.Equal(firstRevision, (await store.LoadVersionedAsync(root, graph.AssetId, CancellationToken.None)).Revision);
    }

    private static RekallAgeMaterialGraphAsset Graph() => RekallAgeMaterialGraphAsset.Create(
        "stone",
        "Stone",
        [
            new("color", "rekall.material.constant.color", 1, new JsonObject { ["value"] = "#808080" }),
            new("pbr", "rekall.material.surface.pbr", 1, new JsonObject { ["metallic"] = 0.1, ["roughness"] = 0.8 }),
            new("output", "rekall.material.output", 1, new JsonObject())
        ],
        [
            new("color-pbr", "color", "color", "pbr", "baseColor"),
            new("pbr-output", "pbr", "surface", "output", "surface")
        ],
        new("surface", "output", "surface"));
}
