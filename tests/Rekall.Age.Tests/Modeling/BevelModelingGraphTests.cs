using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class BevelModelingGraphTests
{
    [Fact]
    public async Task BevelNodeCreatesStableInsetFacesEdgeStripsAndVertexCaps()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "bevel-proof", "Bevel Proof",
            [
                new("box", "rekall.modeling.primitive.box", 1, new JsonObject
                {
                    ["sizeX"] = 2.0, ["sizeY"] = 2.0, ["sizeZ"] = 2.0
                }),
                new("bevel", "rekall.modeling.bevel", 1, new JsonObject { ["width"] = 0.2 }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("box-bevel", "box", "geometry", "bevel", "geometry"),
                new("bevel-output", "bevel", "geometry", "output", "input")
            ],
            [new("mesh", "output", "geometry")]);
        var evaluator = new RekallAgeModelingGraphEvaluator();
        var first = await evaluator.EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default,
            new(417, 0, "tests", "desktop"), CancellationToken.None);
        var second = await evaluator.EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default,
            new(417, 0, "tests", "desktop"), CancellationToken.None);

        Assert.True(first.Succeeded, string.Join(Environment.NewLine, first.Diagnostics.Select(item => item.Message)));
        Assert.Equal(24, first.Outputs["mesh"].Topology.Positions.Count);
        Assert.Equal(26, first.Outputs["mesh"].Topology.FaceIds.Count);
        Assert.Equal(first.Outputs["mesh"].Topology, second.Outputs["mesh"].Topology);
        Assert.Contains(RekallAgeModelingNodeCatalog.CreateDefault().Descriptors,
            item => item.TypeId == "rekall.modeling.bevel");
    }

    [Fact]
    public async Task SegmentedBevelRoundsTransitionsAndPreservesUvAndMaterialData()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "segmented-bevel-proof", "Segmented Bevel Proof",
            [
                new("box", "rekall.modeling.primitive.box", 1, new JsonObject
                {
                    ["sizeX"] = 2.0, ["sizeY"] = 2.0, ["sizeZ"] = 2.0
                }),
                new("uv", "rekall.modeling.project_uv", 1, new JsonObject
                {
                    ["attribute"] = "uv.main", ["axis"] = "xz"
                }),
                new("material", "rekall.modeling.material.assign", 1, new JsonObject
                {
                    ["materialAssetId"] = "material.weathered-stone", ["slotName"] = "stone"
                }),
                new("bevel", "rekall.modeling.bevel", 1, new JsonObject
                {
                    ["width"] = 0.2, ["segments"] = 3, ["profile"] = 0.5,
                    ["clampOverlap"] = true, ["hardenNormals"] = true
                }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("box-uv", "box", "geometry", "uv", "geometry"),
                new("uv-material", "uv", "geometry", "material", "geometry"),
                new("material-bevel", "material", "geometry", "bevel", "geometry"),
                new("bevel-output", "bevel", "geometry", "output", "input")
            ],
            [new("mesh", "output", "geometry")]);
        var evaluator = new RekallAgeModelingGraphEvaluator();

        var first = await evaluator.EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default,
            new(417, 0, "tests", "desktop"), CancellationToken.None);
        var second = await evaluator.EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default,
            new(417, 0, "tests", "desktop"), CancellationToken.None);

        Assert.True(first.Succeeded, string.Join(Environment.NewLine, first.Diagnostics.Select(item => item.Message)));
        var mesh = first.Outputs["mesh"];
        Assert.Equal(80, mesh.Topology.Positions.Count);
        Assert.Equal(114, mesh.Topology.FaceIds.Count);
        Assert.Equal(mesh.Topology, second.Outputs["mesh"].Topology);
        Assert.Equal("material.weathered-stone", Assert.Single(mesh.MaterialSlots).MaterialAssetId);
        var uv = Assert.Single(mesh.Attributes, item => item.Name == "uv.main");
        Assert.Equal(mesh.Topology.CornerIds.Count, uv.Values.Count);
        Assert.All(uv.Values, value => Assert.Equal(2, value.GetArrayLength()));
    }
}
