using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class ModelingGraphEvaluationTests
{
    [Fact]
    public async Task DemandEvaluationCachesNodeHashesAndParameterEditInvalidatesReachableChain()
    {
        var evaluator = new RekallAgeModelingGraphEvaluator();
        var firstGraph = Graph(sizeX: 4, revision: 1);

        var first = await evaluator.EvaluateAsync(firstGraph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, EvaluationContext(), CancellationToken.None);
        var cached = await evaluator.EvaluateAsync(firstGraph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, EvaluationContext(), CancellationToken.None);
        var changed = await evaluator.EvaluateAsync(Graph(sizeX: 8, revision: 2), ["mesh"], RekallAgeModelingEvaluationBudget.Default, EvaluationContext(), CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.Equal(2, first.EvaluatedNodeCount);
        Assert.Equal(0, first.CacheHitCount);
        Assert.Equal(0, first.InvalidatedNodeCount);
        Assert.DoesNotContain(first.Nodes, node => node.NodeId == "unused");
        Assert.Equal(-2, first.Outputs["mesh"].Topology.Positions.Min(position => position.X));
        Assert.Equal(2, first.Outputs["mesh"].Topology.Positions.Max(position => position.X));
        Assert.Equal(2, cached.CacheHitCount);
        Assert.Equal(0, cached.InvalidatedNodeCount);
        Assert.Equal(2, changed.InvalidatedNodeCount);
        Assert.Equal(-4, changed.Outputs["mesh"].Topology.Positions.Min(position => position.X));
        Assert.Equal(4, changed.Outputs["mesh"].Topology.Positions.Max(position => position.X));
        Assert.All(changed.Nodes, node => Assert.Matches("^[0-9a-f]{64}$", node.CacheKey));
    }

    [Fact]
    public async Task BudgetFailureReturnsDiagnosticsAndPreservesLastGoodOutput()
    {
        var evaluator = new RekallAgeModelingGraphEvaluator();
        var graph = Graph(sizeX: 4, revision: 1);
        var good = await evaluator.EvaluateAsync(graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, EvaluationContext(), CancellationToken.None);

        var failed = await evaluator.EvaluateAsync(
            graph,
            ["mesh"],
            RekallAgeModelingEvaluationBudget.Default with { MaximumPoints = 4 },
            EvaluationContext() with { TargetProfile = "budget-proof" },
            CancellationToken.None);

        Assert.True(good.Succeeded);
        Assert.False(failed.Succeeded);
        Assert.True(failed.RetainedLastGoodOutputs);
        Assert.Equal(good.Outputs["mesh"].Topology.Positions, failed.Outputs["mesh"].Topology.Positions);
        Assert.Contains(failed.Diagnostics, diagnostic => diagnostic.Code == "REKALL_MODELING_EVALUATION_POINT_BUDGET_EXCEEDED");
    }

    private static RekallAgeModelingGraphAsset Graph(double sizeX, long revision)
    {
        var graph = RekallAgeModelingGraphAsset.Create(
            "box-graph",
            "Box Graph",
            [
                new("box", "rekall.modeling.primitive.box", 1, new JsonObject { ["sizeX"] = sizeX, ["sizeY"] = 2.0, ["sizeZ"] = 3.0 }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject()),
                new("unused", "rekall.modeling.primitive.sphere", 1, new JsonObject())
            ],
            [new("box-output", "box", "geometry", "output", "input")],
            [new("mesh", "output", "geometry")]);
        return graph with { Revision = revision };
    }

    private static RekallAgeModelingEvaluationContext EvaluationContext() =>
        new(Seed: 42, DeterministicTime: 0, EngineVersion: "test-engine", TargetProfile: "desktop");
}
