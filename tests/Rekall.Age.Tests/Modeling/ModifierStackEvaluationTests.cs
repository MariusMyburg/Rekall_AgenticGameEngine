using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class ModifierStackEvaluationTests
{
    [Fact]
    public async Task OrderedStackEvaluatesImmutablyAndReusesCachedSteps()
    {
        var source = await Box();
        var stack = RekallAgeModifierStackAsset.Create(
            "stack", "Stack", "source", new string('a', 64),
            [
                new("move", "rekall.modifier.transform", 1, true, new JsonObject { ["x"] = 3.0 }),
                new("triangulate", "rekall.modifier.triangulate", 1, true, new JsonObject())
            ]);
        var evaluator = new RekallAgeModifierStackEvaluator();

        var first = await evaluator.EvaluateAsync(stack, source, RekallAgeModelingEvaluationBudget.Default, CancellationToken.None);
        var second = await evaluator.EvaluateAsync(stack, source, RekallAgeModelingEvaluationBudget.Default, CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.Equal(-0.5, source.Topology.Positions.Min(item => item.X));
        Assert.Equal(2.5, first.Mesh!.Topology.Positions.Min(item => item.X));
        Assert.Equal(12, first.Mesh.Topology.FaceIds.Count);
        Assert.Equal(0, first.CacheHitCount);
        Assert.Equal(2, second.CacheHitCount);
        Assert.All(second.Modifiers, item => Assert.True(item.CacheHit));
    }

    [Fact]
    public async Task ChangedConfigurationInvalidatesEditedModifierAndDownstreamOnly()
    {
        var source = await Box();
        var evaluator = new RekallAgeModifierStackEvaluator();
        var firstStack = RekallAgeModifierStackAsset.Create("stack", "Stack", "source", new string('a', 64),
        [
            new("move-a", "rekall.modifier.transform", 1, true, new JsonObject { ["x"] = 1.0 }),
            new("move-b", "rekall.modifier.transform", 1, true, new JsonObject { ["y"] = 2.0 })
        ]);
        await evaluator.EvaluateAsync(firstStack, source, RekallAgeModelingEvaluationBudget.Default, CancellationToken.None);
        var edited = firstStack with
        {
            Revision = 2,
            Modifiers = [firstStack.Modifiers[0], firstStack.Modifiers[1] with { Parameters = new JsonObject { ["y"] = 5.0 } }]
        };

        var result = await evaluator.EvaluateAsync(edited, source, RekallAgeModelingEvaluationBudget.Default, CancellationToken.None);

        Assert.Equal(1, result.CacheHitCount);
        Assert.Equal(1, result.InvalidatedModifierCount);
        Assert.False(result.Modifiers[1].CacheHit);
        Assert.Equal(5.5, result.Mesh!.Topology.Positions.Max(item => item.Y));
    }

    [Fact]
    public void CatalogDeclaresAttributePropagationAndLossPolicy()
    {
        var catalog = RekallAgeModifierCatalog.CreateDefault();

        Assert.Equal(3, catalog.Descriptors.Count);
        Assert.All(catalog.Descriptors, descriptor => Assert.NotNull(descriptor.AttributePolicy));
        Assert.Contains(catalog.Descriptors, item => item.TypeId == "rekall.modifier.triangulate" && item.AttributePolicy.PreservesUnknownAttributes);
    }

    private static async ValueTask<RekallAgeMeshAsset> Box()
    {
        var graph = RekallAgeModelingGraphAsset.Create("box", "Box",
        [
            new("box", "rekall.modeling.primitive.box", 1, new JsonObject()),
            new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
        ], [new("box-output", "box", "geometry", "output", "input")], [new("mesh", "output", "geometry")]);
        var report = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, new(0, 0, "test", "desktop"), CancellationToken.None);
        return report.Outputs["mesh"];
    }
}
