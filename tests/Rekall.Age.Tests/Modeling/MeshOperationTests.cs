using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class MeshOperationTests
{
    [Fact]
    public void TransformPointsIsPureRevisionedAndReturnsStableIdDiff()
    {
        var mesh = CreateQuad();
        var executor = new RekallAgeMeshOperationExecutor();

        var result = executor.Execute(
            mesh,
            new RekallAgeMeshOperationRequest(
                "transform",
                RekallAgeGeometryDomain.Point,
                [1, 2],
                new JsonObject { ["x"] = 2, ["y"] = -1, ["z"] = 3 }));

        Assert.Equal(1, result.BeforeRevision);
        Assert.Equal(2, result.AfterRevision);
        Assert.Equal(new RekallAgeGeometryVector3(0, 0, 0), mesh.Topology.Positions[0]);
        Assert.Equal(new RekallAgeGeometryVector3(2, -1, 3), result.Mesh.Topology.Positions[0]);
        Assert.Equal(new RekallAgeGeometryVector3(3, -1, 3), result.Mesh.Topology.Positions[1]);
        Assert.Equal(new RekallAgeGeometryVector3(1, 1, 0), result.Mesh.Topology.Positions[2]);
        Assert.Equal([1UL, 2UL], result.Changes.ModifiedPointIds);
        Assert.Equal(RekallAgeMeshChangeKind.Positions, result.Changes.Kind);
        Assert.True(result.Validation.IsValid);
        Assert.Contains(result.Provenance, item => item.InputElementId == 1 && item.OutputElementIds.SequenceEqual([1UL]));
    }

    [Fact]
    public void TransformRejectsMissingIdsAndNonFiniteParametersAtomically()
    {
        var mesh = CreateQuad();
        var executor = new RekallAgeMeshOperationExecutor();

        var missing = Assert.Throws<RekallAgeMeshOperationException>(() => executor.Execute(
            mesh,
            new RekallAgeMeshOperationRequest(
                "transform",
                RekallAgeGeometryDomain.Point,
                [999],
                new JsonObject { ["x"] = 1 })));
        var nonFinite = Assert.Throws<RekallAgeMeshOperationException>(() => executor.Execute(
            mesh,
            new RekallAgeMeshOperationRequest(
                "transform",
                RekallAgeGeometryDomain.Point,
                [1],
                new JsonObject { ["x"] = double.NaN })));

        Assert.Equal("REKALL_MESH_OPERATION_SELECTION_INVALID", missing.Code);
        Assert.Equal("REKALL_MESH_OPERATION_PARAMETER_INVALID", nonFinite.Code);
        Assert.Equal(1, mesh.Revision);
        Assert.Equal(new RekallAgeGeometryVector3(0, 0, 0), mesh.Topology.Positions[0]);
    }

    [Fact]
    public void ReverseFacePreservesCornerIdentityAttributesAndValidTopology()
    {
        var mesh = CreateQuad();
        var executor = new RekallAgeMeshOperationExecutor();

        var result = executor.Execute(
            mesh,
            new RekallAgeMeshOperationRequest(
                "reverse_faces",
                RekallAgeGeometryDomain.Face,
                [21],
                new JsonObject()));

        Assert.Equal([0, 3, 2, 1], result.Mesh.Topology.CornerPointIndices);
        Assert.Equal([31UL, 34UL, 33UL, 32UL], result.Mesh.Topology.CornerIds);
        Assert.Equal([3, 2, 1, 0], result.Mesh.Topology.CornerEdgeIndices);
        Assert.Equal([21UL], result.Changes.ModifiedFaceIds);
        Assert.True(result.Validation.IsValid, string.Join(",", result.Validation.Diagnostics.Select(item => item.Code)));
    }

    private static RekallAgeMeshAsset CreateQuad()
    {
        return RekallAgeMeshAsset.Create(
            "operations",
            "Operations",
            new RekallAgeMeshTopology(
                PointIds: [1, 2, 3, 4],
                Positions: [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0)],
                EdgeIds: [11, 12, 13, 14],
                EdgePointIndices: [new(0, 1), new(1, 2), new(2, 3), new(3, 0)],
                FaceIds: [21],
                FaceOffsets: [0, 4],
                CornerIds: [31, 32, 33, 34],
                CornerPointIndices: [0, 1, 2, 3],
                CornerEdgeIndices: [0, 1, 2, 3]));
    }
}
