using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class MeshLoopCutTests
{
    [Fact]
    public void EdgeSplitKernelFindsDeterministicQuadRingFromEitherSeedDirection()
    {
        var mesh = TwoQuadStrip();

        var forward = RekallAgeMeshEdgeSplitKernel.ResolveQuadRing(mesh, 11);
        var reverse = RekallAgeMeshEdgeSplitKernel.ResolveQuadRing(mesh, 15);

        Assert.Equal([11UL, 13UL, 15UL], forward);
        Assert.Equal(forward, reverse);
    }

    [Fact]
    public void LoopCutSplitsQuadStripAndInterpolatesPointAttributesWithStableProvenance()
    {
        var source = TwoQuadStrip();
        var request = new RekallAgeMeshOperationRequest(
            "loop_cut_edges",
            RekallAgeGeometryDomain.Edge,
            [11],
            new JsonObject { ["factor"] = 0.25 });

        var first = new RekallAgeMeshOperationExecutor().Execute(source, request);
        var second = new RekallAgeMeshOperationExecutor().Execute(source, request);

        Assert.Equal(9, first.Mesh.Topology.PointIds.Count);
        Assert.Equal(12, first.Mesh.Topology.EdgeIds.Count);
        Assert.Equal(4, first.Mesh.Topology.FaceIds.Count);
        Assert.Equal(16, first.Mesh.Topology.CornerIds.Count);
        Assert.Equal(3, first.Changes.CreatedPointIds.Count);
        Assert.Equal(4, first.Changes.CreatedFaceIds.Count);
        var pointIds = first.Mesh.Topology.PointIds.ToList();
        Assert.Equal([new RekallAgeGeometryVector3(0.25, 0, 0), new(0.25, 1, 0), new(0.25, 2, 0)],
            first.Changes.CreatedPointIds.Select(id => first.Mesh.Topology.Positions[pointIds.IndexOf(id)]));
        var weights = Assert.Single(first.Mesh.Attributes, item => item.Domain == RekallAgeGeometryDomain.Point).Values;
        Assert.Equal([0.25, 2.25, 4.25], weights.Skip(6).Select(value => value.GetDouble()));
        Assert.All(source.Topology.FaceIds, face => Assert.Equal(2,
            Assert.Single(first.Provenance, item => item.Domain == RekallAgeGeometryDomain.Face && item.InputElementId == face).OutputElementIds.Count));
        Assert.Equal(JsonSerializer.Serialize(first.Mesh, RekallAgeModelingJson.Options),
            JsonSerializer.Serialize(second.Mesh, RekallAgeModelingJson.Options));
        Assert.True(first.Validation.IsValid, string.Join(", ", first.Validation.Diagnostics.Select(item => item.Code)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void LoopCutRejectsEndpointFactors(double factor)
    {
        var source = TwoQuadStrip();
        var error = Assert.Throws<RekallAgeMeshOperationException>(() => new RekallAgeMeshOperationExecutor().Execute(
            source,
            new("loop_cut_edges", RekallAgeGeometryDomain.Edge, [11], new JsonObject { ["factor"] = factor })));

        Assert.Equal("REKALL_MESH_LOOP_CUT_FACTOR_INVALID", error.Code);
    }

    private static RekallAgeMeshAsset TwoQuadStrip() => RekallAgeMeshAsset.Create(
        "quad-strip",
        "Quad Strip",
        new(
            PointIds: [1, 2, 3, 4, 5, 6],
            Positions: [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(1, 1, 0), new(0, 2, 0), new(1, 2, 0)],
            EdgeIds: [11, 12, 13, 14, 15, 16, 17],
            EdgePointIndices: [new(0, 1), new(1, 3), new(3, 2), new(2, 0), new(5, 4), new(4, 2), new(3, 5)],
            FaceIds: [21, 22], FaceOffsets: [0, 4, 8],
            CornerIds: [31, 32, 33, 34, 35, 36, 37, 38],
            CornerPointIndices: [0, 1, 3, 2, 2, 3, 5, 4],
            CornerEdgeIndices: [0, 1, 2, 3, 2, 6, 4, 5]),
        attributes:
        [
            new("weight", RekallAgeGeometryDomain.Point, RekallAgeGeometryValueType.Float,
                Enumerable.Range(0, 6).Select(i => JsonSerializer.SerializeToElement((double)i)).ToArray())
        ]);
}
