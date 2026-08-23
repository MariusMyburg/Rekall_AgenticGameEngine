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

    [Fact]
    public void ExtrudeFaceRegionCreatesTopAndBoundarySidesWithPointProvenance()
    {
        var mesh = CreateQuad();
        var executor = new RekallAgeMeshOperationExecutor();

        var result = executor.Execute(
            mesh,
            new RekallAgeMeshOperationRequest(
                "extrude_faces",
                RekallAgeGeometryDomain.Face,
                [21],
                new JsonObject { ["x"] = 0, ["y"] = 0, ["z"] = 2 }));

        Assert.Equal(4, mesh.Topology.PointIds.Count);
        Assert.Equal(8, result.Mesh.Topology.PointIds.Count);
        Assert.Equal(12, result.Mesh.Topology.EdgeIds.Count);
        Assert.Equal(5, result.Mesh.Topology.FaceIds.Count);
        Assert.Equal(20, result.Mesh.Topology.CornerIds.Count);
        Assert.Equal(4, result.Changes.CreatedPointIds.Count);
        Assert.Equal(8, result.Changes.CreatedEdgeIds.Count);
        Assert.Equal(4, result.Changes.CreatedFaceIds.Count);
        Assert.Equal(16, result.Changes.CreatedCornerIds.Count);
        Assert.Contains(result.Mesh.Topology.Positions, item => item == new RekallAgeGeometryVector3(0, 0, 2));
        var faceProvenance = Assert.Single(result.Provenance, item =>
            item.Domain == RekallAgeGeometryDomain.Face && item.InputElementId == 21);
        Assert.Equal(5, faceProvenance.OutputElementIds.Count);
        var pointProvenance = Assert.Single(result.Provenance, item =>
            item.Domain == RekallAgeGeometryDomain.Point && item.InputElementId == 1);
        Assert.Equal(2, pointProvenance.OutputElementIds.Count);
        Assert.Contains(1UL, pointProvenance.OutputElementIds);
        Assert.True(result.Validation.IsValid, string.Join(",", result.Validation.Diagnostics.Select(item => item.Code)));
    }

    [Fact]
    public void DeleteFacesRemovesCornerDataAndSelectionReferencesAtomically()
    {
        var mesh = CreateQuad() with
        {
            SelectionSets = [new("selected", RekallAgeGeometryDomain.Face, [21], ActiveElementId: 21, OrderedHistory: [21])]
        };
        var executor = new RekallAgeMeshOperationExecutor();

        var result = executor.Execute(
            mesh,
            new RekallAgeMeshOperationRequest(
                "delete",
                RekallAgeGeometryDomain.Face,
                [21],
                new JsonObject()));

        Assert.Single(mesh.Topology.FaceIds);
        Assert.Empty(result.Mesh.Topology.FaceIds);
        Assert.Equal([0], result.Mesh.Topology.FaceOffsets);
        Assert.Empty(result.Mesh.Topology.CornerIds);
        Assert.Equal([21UL], result.Changes.DeletedFaceIds);
        Assert.Equal([31UL, 32UL, 33UL, 34UL], result.Changes.DeletedCornerIds);
        var selection = Assert.Single(result.Mesh.SelectionSets);
        Assert.Empty(selection.ElementIds);
        Assert.Null(selection.ActiveElementId);
        Assert.Empty(selection.OrderedHistory!);
        Assert.True(result.Validation.IsValid);
        Assert.Contains(result.Provenance, item => item.InputElementId == 21 && item.OutputElementIds.Count == 0);
    }

    [Fact]
    public void OperationDescriptorsAreUniqueSelfDescribingAndMatchExecutorInventory()
    {
        var executor = new RekallAgeMeshOperationExecutor();

        Assert.Equal(executor.Descriptors.Count, executor.Descriptors.Select(item => item.OperationId).Distinct().Count());
        Assert.Contains(executor.Descriptors, item =>
            item.OperationId == "extrude_faces"
            && item.Domain == RekallAgeGeometryDomain.Face
            && item.Parameters.Select(parameter => parameter.Name).SequenceEqual(["x", "y", "z"]));
        Assert.Contains(executor.Descriptors, item => item.OperationId == "delete");
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
