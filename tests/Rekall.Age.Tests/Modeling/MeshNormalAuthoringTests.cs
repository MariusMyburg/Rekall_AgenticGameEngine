using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class MeshNormalAuthoringTests
{
    [Fact]
    public async Task WeightedNormalsShadeSegmentedBevelWithFiniteUnitCornerVectors()
    {
        var graph = RekallAgeModelingGraphAsset.Create("weighted-normal-proof", "Weighted Normal Proof",
            [
                new("box", "rekall.modeling.primitive.box", 1, new JsonObject()),
                new("bevel", "rekall.modeling.bevel", 1, new JsonObject { ["width"] = 0.08, ["segments"] = 3 }),
                new("normals", "rekall.modeling.weighted_normals", 1, new JsonObject { ["attribute"] = "normal.weighted", ["faceAreaWeight"] = 1.0 }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [new("box-bevel", "box", "geometry", "bevel", "geometry"), new("bevel-normal", "bevel", "geometry", "normals", "geometry"), new("normal-output", "normals", "geometry", "output", "input")],
            [new("mesh", "output", "geometry")]);

        var report = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, new(1, 0, "tests", "desktop"), CancellationToken.None);

        Assert.True(report.Succeeded, string.Join(Environment.NewLine, report.Diagnostics.Select(item => item.Message)));
        var mesh = report.Outputs["mesh"];
        var normals = Assert.Single(mesh.Attributes, item => item.Name == "normal.weighted");
        Assert.Equal(RekallAgeGeometryDomain.Corner, normals.Domain);
        Assert.Equal(mesh.Topology.CornerIds.Count, normals.Values.Count);
        Assert.All(normals.Values, value => Assert.InRange(Math.Sqrt(value[0].GetDouble() * value[0].GetDouble() + value[1].GetDouble() * value[1].GetDouble() + value[2].GetDouble() * value[2].GetDouble()), 0.999999, 1.000001));
    }
}
