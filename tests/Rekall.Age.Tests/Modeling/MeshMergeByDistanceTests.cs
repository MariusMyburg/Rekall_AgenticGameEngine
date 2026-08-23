using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class MeshMergeByDistanceTests
{
    [Fact]
    public async Task ProceduralAndModifierEvaluationExposeTheSameWeldPrimitive()
    {
        var graph = RekallAgeModelingGraphAsset.Create("weld", "Weld",
            [
                new("left", "rekall.modeling.primitive.grid", 1, new JsonObject()),
                new("right", "rekall.modeling.primitive.grid", 1, new JsonObject()),
                new("move-right", "rekall.modeling.transform", 1, new JsonObject { ["translation"] = new JsonArray(1.0, 0.0, 0.0) }),
                new("join", "rekall.modeling.join", 1, new JsonObject()),
                new("weld", "rekall.modeling.merge_by_distance", 1, new JsonObject { ["distance"] = 0.001 }),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [
                new("right-move", "right", "geometry", "move-right", "geometry"),
                new("left-join", "left", "geometry", "join", "geometry"),
                new("right-join", "move-right", "geometry", "join", "geometry"),
                new("join-weld", "join", "geometry", "weld", "geometry"),
                new("weld-output", "weld", "geometry", "output", "input")
            ], [new("mesh", "output", "geometry")]);

        var graphReport = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(graph, ["mesh"],
            RekallAgeModelingEvaluationBudget.Default, new(0, 0, "test", "desktop"), CancellationToken.None);
        Assert.True(graphReport.Succeeded, string.Join(",", graphReport.Diagnostics.Select(item => item.Code)));
        Assert.Equal(6, graphReport.Outputs["mesh"].Topology.PointIds.Count);

        var stack = RekallAgeModifierStackAsset.Create("weld-stack", "Weld Stack", "source", new string('a', 64),
            [new("weld", "rekall.modifier.merge_by_distance", 1, true, new JsonObject { ["distance"] = 0.001 })]);
        var modifierReport = await new RekallAgeModifierStackEvaluator().EvaluateAsync(stack, TwoAdjacentDisconnectedQuads(),
            RekallAgeModelingEvaluationBudget.Default, CancellationToken.None);
        Assert.True(modifierReport.Succeeded, string.Join(",", modifierReport.Diagnostics.Select(item => item.Code)));
        Assert.Equal(6, modifierReport.Mesh!.Topology.PointIds.Count);
    }

    [Fact]
    public void MergeByDistanceWeldsCoincidentSeamAndDeduplicatesSharedEdge()
    {
        var source = TwoAdjacentDisconnectedQuads();

        var result = new RekallAgeMeshOperationExecutor().Execute(source,
            new("merge_by_distance", RekallAgeGeometryDomain.Point, source.Topology.PointIds,
                new JsonObject { ["distance"] = 0.001 }));

        Assert.Equal(6, result.Mesh.Topology.PointIds.Count);
        Assert.Equal(7, result.Mesh.Topology.EdgeIds.Count);
        Assert.Equal(2, result.Mesh.Topology.FaceIds.Count);
        Assert.Equal(8, result.Mesh.Topology.CornerIds.Count);
        Assert.Equal(2, result.Changes.DeletedPointIds.Count);
        Assert.Single(result.Changes.DeletedEdgeIds);
        Assert.Contains(result.Provenance, item => item.Domain == RekallAgeGeometryDomain.Point && item.InputElementId == 5 && item.OutputElementIds.SequenceEqual([2UL]));
        Assert.True(result.Validation.IsValid, string.Join(",", result.Validation.Diagnostics.Select(item => item.Code)));
        Assert.Equal(0, result.Validation.Summary.NonManifoldEdgeCount);
    }

    private static RekallAgeMeshAsset TwoAdjacentDisconnectedQuads() => RekallAgeMeshAsset.Create(
        "seam", "Seam",
        new(
            PointIds: [1, 2, 3, 4, 5, 6, 7, 8],
            Positions:
            [
                new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0),
                new(1, 0, 0), new(2, 0, 0), new(2, 1, 0), new(1, 1, 0)
            ],
            EdgeIds: [11, 12, 13, 14, 15, 16, 17, 18],
            EdgePointIndices: [new(0, 1), new(1, 2), new(2, 3), new(3, 0), new(4, 5), new(5, 6), new(6, 7), new(7, 4)],
            FaceIds: [21, 22], FaceOffsets: [0, 4, 8],
            CornerIds: [31, 32, 33, 34, 35, 36, 37, 38],
            CornerPointIndices: [0, 1, 2, 3, 4, 5, 6, 7],
            CornerEdgeIndices: [0, 1, 2, 3, 4, 5, 6, 7]));
}
