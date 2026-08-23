using System.Text.Json.Nodes;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class ModelingGraphBakeTests
{
    [Fact]
    public async Task BakePublishesThroughStrictMeshStoreAndAdvancesOneRevisionAfterParameterEdit()
    {
        var root = TestPaths.CreateTempDirectory();
        var service = new RekallAgeModelingGraphBakeService();
        var firstTransaction = RekallAgeTransaction.Begin("first bake");
        var first = await service.BakeAsync(
            root, Graph(2, 1), "mesh", "baked-room", RekallAgeDocumentRevision.Missing,
            RekallAgeModelingEvaluationBudget.Default, EvaluationContext(), firstTransaction, CancellationToken.None);
        var firstLoaded = await new RekallAgeMeshAssetStore().LoadVersionedAsync(root, "baked-room", CancellationToken.None);
        var secondTransaction = RekallAgeTransaction.Begin("second bake");
        var second = await service.BakeAsync(
            root, Graph(6, 2), "mesh", "baked-room", firstLoaded.Revision,
            RekallAgeModelingEvaluationBudget.Default, EvaluationContext(), secondTransaction, CancellationToken.None);

        Assert.Equal(1, first.Mesh.Revision);
        Assert.Equal(2, second.Mesh.Revision);
        Assert.Equal(-1, first.Mesh.Topology.Positions.Min(position => position.X));
        Assert.Equal(1, first.Mesh.Topology.Positions.Max(position => position.X));
        Assert.Equal(-3, second.Mesh.Topology.Positions.Min(position => position.X));
        Assert.Equal(3, second.Mesh.Topology.Positions.Max(position => position.X));
        Assert.Single(firstTransaction.ChangedResources);
        Assert.Single(secondTransaction.ResourcePreimages);
        var compiled = new RekallAgeMeshCompiler().Compile(await new RekallAgeMeshAssetStore().LoadAsync(root, "baked-room", CancellationToken.None));
        Assert.Equal(12, compiled.Triangles.Count);
        Assert.Equal(-3, compiled.Bounds.Min.X);
        Assert.Equal(3, compiled.Bounds.Max.X);
    }

    private static RekallAgeModelingGraphAsset Graph(double sizeX, long revision) =>
        RekallAgeModelingGraphAsset.Create(
            "bake-graph",
            "Bake Graph",
            [
                new("box", "rekall.modeling.primitive.box", 1, new JsonObject { ["sizeX"] = sizeX }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [new("box-output", "box", "geometry", "output", "input")],
            [new("mesh", "output", "geometry")]) with { Revision = revision };

    private static RekallAgeModelingEvaluationContext EvaluationContext() =>
        new(42, 0, "test-engine", "desktop");
}
