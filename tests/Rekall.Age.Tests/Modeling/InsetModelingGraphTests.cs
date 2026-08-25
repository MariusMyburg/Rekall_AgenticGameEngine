using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class InsetModelingGraphTests
{
    [Fact]
    public async Task InsetNodeBuildsRecessedFaceBordersDeterministically()
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "inset-proof", "Inset Proof",
            [
                new("box", "rekall.modeling.primitive.box", 1, new JsonObject()),
                new("inset", "rekall.modeling.inset", 1, new JsonObject
                {
                    ["thickness"] = 0.14, ["depth"] = -0.06,
                    ["individual"] = true, ["boundary"] = true
                }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("box-inset", "box", "geometry", "inset", "geometry"),
                new("inset-output", "inset", "geometry", "output", "input")
            ],
            [new("mesh", "output", "geometry")]);
        var evaluator = new RekallAgeModelingGraphEvaluator();

        var first = await evaluator.EvaluateAsync(graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, new(91, 0, "tests", "desktop"), CancellationToken.None);
        var second = await evaluator.EvaluateAsync(graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, new(91, 0, "tests", "desktop"), CancellationToken.None);

        Assert.True(first.Succeeded, string.Join(Environment.NewLine, first.Diagnostics.Select(item => item.Message)));
        Assert.Equal(32, first.Outputs["mesh"].Topology.PointIds.Count);
        Assert.Equal(30, first.Outputs["mesh"].Topology.FaceIds.Count);
        Assert.Equal(first.Outputs["mesh"].Topology, second.Outputs["mesh"].Topology);
        Assert.Contains(RekallAgeModelingNodeCatalog.CreateDefault().Descriptors, item => item.TypeId == "rekall.modeling.inset");
    }
}
