using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class MeshSmoothSubdivisionTests
{
    [Fact]
    public async Task ProceduralAndModifierSmoothSubdivisionHandleAClosedBox()
    {
        var graph = RekallAgeModelingGraphAsset.Create("smooth-box", "Smooth Box",
            [
                new("box", "rekall.modeling.primitive.box", 1, new JsonObject()),
                new("smooth", "rekall.modeling.subdivide_smooth", 1, new JsonObject()),
                new("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            ],
            [new("box-smooth", "box", "geometry", "smooth", "geometry"), new("smooth-output", "smooth", "geometry", "output", "input")],
            [new("mesh", "output", "geometry")]);
        var graphReport = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(graph, ["mesh"],
            RekallAgeModelingEvaluationBudget.Default, new(0, 0, "test", "desktop"), CancellationToken.None);

        Assert.True(graphReport.Succeeded, string.Join(",", graphReport.Diagnostics.Select(item => item.Code)));
        var graphMesh = graphReport.Outputs["mesh"];
        Assert.Equal(26, graphMesh.Topology.PointIds.Count);
        Assert.Equal(48, graphMesh.Topology.EdgeIds.Count);
        Assert.Equal(24, graphMesh.Topology.FaceIds.Count);

        var sourceGraph = RekallAgeModelingGraphAsset.Create("box", "Box",
            [new("box", "rekall.modeling.primitive.box", 1, new JsonObject()), new("output", "rekall.modeling.output.mesh", 1, new JsonObject())],
            [new("box-output", "box", "geometry", "output", "input")], [new("mesh", "output", "geometry")]);
        var source = (await new RekallAgeModelingGraphEvaluator().EvaluateAsync(sourceGraph, ["mesh"],
            RekallAgeModelingEvaluationBudget.Default, new(0, 0, "test", "desktop"), CancellationToken.None)).Outputs["mesh"];
        var partial = Assert.Throws<RekallAgeMeshOperationException>(() => new RekallAgeMeshOperationExecutor().Execute(source,
            new("subdivide_smooth", RekallAgeGeometryDomain.Face, [source.Topology.FaceIds[0]], new JsonObject())));
        Assert.Equal("REKALL_MESH_OPERATION_SMOOTH_REQUIRES_COMPLETE_SURFACE", partial.Code);
        var stack = RekallAgeModifierStackAsset.Create("smooth", "Smooth", "source", new string('a', 64),
            [new("smooth", "rekall.modifier.subdivide_smooth", 1, true, new JsonObject())]);
        var modifierReport = await new RekallAgeModifierStackEvaluator().EvaluateAsync(stack, source,
            RekallAgeModelingEvaluationBudget.Default, CancellationToken.None);

        Assert.True(modifierReport.Succeeded, string.Join(",", modifierReport.Diagnostics.Select(item => item.Code)));
        Assert.Equal(24, modifierReport.Mesh!.Topology.FaceIds.Count);
    }

    [Fact]
    public void SmoothSubdivisionRejectsNonManifoldEdgesWithARepairableCode()
    {
        var error = Assert.Throws<RekallAgeMeshOperationException>(() => new RekallAgeMeshOperationExecutor().Execute(NonManifoldFan(),
            new("subdivide_smooth", RekallAgeGeometryDomain.Face, [21, 22, 23], new JsonObject())));

        Assert.Equal("REKALL_MESH_OPERATION_SMOOTH_NON_MANIFOLD", error.Code);
        Assert.Contains("11", error.Message);
    }

    [Fact]
    public void SmoothSubdivisionCreatesCatmullClarkQuadsAndPropagatesCornerUv()
    {
        var source = Quad();

        var result = new RekallAgeMeshOperationExecutor().Execute(source,
            new("subdivide_smooth", RekallAgeGeometryDomain.Face, source.Topology.FaceIds, new JsonObject()));

        Assert.Equal(9, result.Mesh.Topology.PointIds.Count);
        Assert.Equal(12, result.Mesh.Topology.EdgeIds.Count);
        Assert.Equal(4, result.Mesh.Topology.FaceIds.Count);
        Assert.Equal(16, result.Mesh.Topology.CornerIds.Count);
        Assert.Equal(new(0.125, 0.125, 0), result.Mesh.Topology.Positions[0]);
        Assert.Equal(5, result.Changes.CreatedPointIds.Count);
        Assert.Equal(8, result.Changes.CreatedEdgeIds.Count);
        Assert.Equal(3, result.Changes.CreatedFaceIds.Count);
        Assert.Equal(12, result.Changes.CreatedCornerIds.Count);
        Assert.Equal(4, Assert.Single(result.Provenance, item => item.Domain == RekallAgeGeometryDomain.Face).OutputElementIds.Count);
        var uv = Assert.Single(result.Mesh.Attributes);
        Assert.Equal(16, uv.Values.Count);
        Assert.Contains(uv.Values, item => item[0].GetDouble() == 0.5 && item[1].GetDouble() == 0.5);
        Assert.True(result.Validation.IsValid, string.Join(",", result.Validation.Diagnostics.Select(item => item.Code)));
    }

    private static RekallAgeMeshAsset Quad() => RekallAgeMeshAsset.Create("quad", "Quad",
        new(
            PointIds: [1, 2, 3, 4],
            Positions: [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0)],
            EdgeIds: [11, 12, 13, 14],
            EdgePointIndices: [new(0, 1), new(1, 2), new(2, 3), new(3, 0)],
            FaceIds: [21], FaceOffsets: [0, 4],
            CornerIds: [31, 32, 33, 34], CornerPointIndices: [0, 1, 2, 3], CornerEdgeIndices: [0, 1, 2, 3]),
        attributes:
        [
            new("uv.main", RekallAgeGeometryDomain.Corner, RekallAgeGeometryValueType.Float2,
            [
                JsonSerializer.SerializeToElement(new[] { 0d, 0d }), JsonSerializer.SerializeToElement(new[] { 1d, 0d }),
                JsonSerializer.SerializeToElement(new[] { 1d, 1d }), JsonSerializer.SerializeToElement(new[] { 0d, 1d })
            ], "texcoord")
        ]);

    private static RekallAgeMeshAsset NonManifoldFan() => RekallAgeMeshAsset.Create("fan", "Fan",
        new(
            PointIds: [1, 2, 3, 4, 5],
            Positions: [new(0, 0, 0), new(1, 0, 0), new(0.5, 1, 0), new(0.5, -1, 0), new(0.5, 0, 1)],
            EdgeIds: [11, 12, 13, 14, 15, 16, 17],
            EdgePointIndices: [new(0, 1), new(1, 2), new(2, 0), new(0, 3), new(3, 1), new(1, 4), new(4, 0)],
            FaceIds: [21, 22, 23], FaceOffsets: [0, 3, 6, 9],
            CornerIds: [31, 32, 33, 34, 35, 36, 37, 38, 39],
            CornerPointIndices: [0, 1, 2, 1, 0, 3, 0, 1, 4],
            CornerEdgeIndices: [0, 1, 2, 0, 3, 4, 0, 5, 6]));
}
