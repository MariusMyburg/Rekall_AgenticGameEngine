using System.Text.Json.Nodes;
using System.Text.Json;
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

    [Fact]
    public void TriangulateNgonDerivesFacesAndPreservesSourceProvenanceAndCornerData()
    {
        var mesh = CreateQuad() with
        {
            Attributes =
            [
                new RekallAgeGeometryAttribute(
                    "uv.main",
                    RekallAgeGeometryDomain.Corner,
                    RekallAgeGeometryValueType.Float2,
                    [
                        JsonSerializer.SerializeToElement(new[] { 0.0, 0.0 }),
                        JsonSerializer.SerializeToElement(new[] { 1.0, 0.0 }),
                        JsonSerializer.SerializeToElement(new[] { 1.0, 1.0 }),
                        JsonSerializer.SerializeToElement(new[] { 0.0, 1.0 })
                    ],
                    Semantic: "texcoord")
            ]
        };
        var executor = new RekallAgeMeshOperationExecutor();

        var result = executor.Execute(
            mesh,
            new RekallAgeMeshOperationRequest(
                "triangulate_faces",
                RekallAgeGeometryDomain.Face,
                [21],
                new JsonObject()));

        Assert.Equal(4, mesh.Topology.CornerIds.Count);
        Assert.Equal(5, result.Mesh.Topology.EdgeIds.Count);
        Assert.Equal(2, result.Mesh.Topology.FaceIds.Count);
        Assert.Equal([0, 3, 6], result.Mesh.Topology.FaceOffsets);
        Assert.Equal(6, result.Mesh.Topology.CornerIds.Count);
        Assert.Single(result.Changes.CreatedEdgeIds);
        Assert.Single(result.Changes.CreatedFaceIds);
        Assert.Equal(2, result.Changes.CreatedCornerIds.Count);
        Assert.True(result.Changes.Kind.HasFlag(RekallAgeMeshChangeKind.Topology));
        var provenance = Assert.Single(result.Provenance, item =>
            item.Domain == RekallAgeGeometryDomain.Face && item.InputElementId == 21);
        Assert.Equal(2, provenance.OutputElementIds.Count);
        Assert.Contains(21UL, provenance.OutputElementIds);
        var uv = Assert.Single(result.Mesh.Attributes);
        Assert.Equal(6, uv.Values.Count);
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
