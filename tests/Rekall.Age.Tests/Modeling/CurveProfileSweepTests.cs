using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class CurveProfileSweepTests
{
    [Fact]
    public async Task ProfileSweepSamplesStableFramesCapsUvsAndMaterial()
    {
        var parameters = new JsonObject
        {
            ["pathPoints"] = new JsonArray(new JsonArray(0, 0, 0), new JsonArray(0, 2, 0), new JsonArray(2, 3, 0)),
            ["profile"] = "circle", ["profileSegments"] = 8, ["radius"] = 0.25,
            ["capStart"] = true, ["capEnd"] = true,
            ["materialAssetId"] = "material.weathered-metal", ["slotName"] = "Sweep"
        };
        var graph = RekallAgeModelingGraphAsset.Create("sweep-proof", "Sweep Proof",
            [new("sweep", "rekall.modeling.curve.profile_sweep", 1, parameters), new("output", "rekall.modeling.output.mesh", 1, new())],
            [new("out", "sweep", "geometry", "output", "input")], [new("mesh", "output", "geometry")]);

        var evaluator = new RekallAgeModelingGraphEvaluator();
        var first = await evaluator.EvaluateAsync(graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, new(7, 0, "tests", "desktop"), CancellationToken.None);
        var second = await evaluator.EvaluateAsync(graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, new(7, 0, "tests", "desktop"), CancellationToken.None);

        Assert.True(first.Succeeded, string.Join(Environment.NewLine, first.Diagnostics.Select(item => item.Message)));
        var mesh = first.Outputs["mesh"];
        Assert.Equal(24, mesh.Topology.PointIds.Count);
        Assert.Equal(18, mesh.Topology.FaceIds.Count);
        Assert.Equal(mesh.Topology.Positions, second.Outputs["mesh"].Topology.Positions);
        Assert.Contains(mesh.MaterialSlots, slot => slot.MaterialAssetId == "material.weathered-metal" && slot.Name == "Sweep");
        var uv = Assert.Single(mesh.Attributes, item => item.Semantic == "texcoord-0");
        Assert.Equal(RekallAgeGeometryDomain.Corner, uv.Domain);
        Assert.Equal(mesh.Topology.CornerIds.Count, uv.Values.Count);
        var validation = new RekallAgeMeshValidator().Validate(mesh);
        Assert.True(validation.IsValid);
        Assert.Equal(0, validation.Summary.BoundaryEdgeCount);
    }
}
