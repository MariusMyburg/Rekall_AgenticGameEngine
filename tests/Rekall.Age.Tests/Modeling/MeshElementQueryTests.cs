using System.Text.Json;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class MeshElementQueryTests
{
    [Fact]
    public void CombinesStableIdsNamedSelectionAttributesAndBoundedResults()
    {
        var mesh = CreateMesh();
        var query = new RekallAgeMeshElementQuery();
        var selector = new RekallAgeMeshElementSelector(
            RekallAgeGeometryDomain.Face,
            ExplicitElementIds: [21, 22],
            SelectionSetName: "all-faces",
            AttributePredicate: new(
                "material.index",
                JsonSerializer.SerializeToElement(1)));

        var result = query.Resolve(mesh, selector, maximumResults: 1);

        Assert.Equal([22UL], result.ElementIds);
        Assert.Equal(1, result.MatchedCount);
        Assert.Equal(2, result.TotalDomainCount);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void ResolvesConnectivityAndSpatialPredicatesByStableDomain()
    {
        var mesh = CreateMesh();
        var query = new RekallAgeMeshElementQuery();

        var neighbors = query.Resolve(
            mesh,
            new RekallAgeMeshElementSelector(
                RekallAgeGeometryDomain.Face,
                ConnectivitySeedIds: [21]),
            maximumResults: 10);
        var points = query.Resolve(
            mesh,
            new RekallAgeMeshElementSelector(
                RekallAgeGeometryDomain.Point,
                WithinBounds: new(new(0.9, -0.1, -0.1), new(1.1, 1.1, 0.1))),
            maximumResults: 10);

        Assert.Equal([22UL], neighbors.ElementIds);
        Assert.Equal([2UL, 3UL], points.ElementIds);
    }

    [Fact]
    public void RejectsUnknownIdsWrongSelectionDomainsAndInvalidLimits()
    {
        var mesh = CreateMesh();
        var query = new RekallAgeMeshElementQuery();

        Assert.Equal("REKALL_MESH_QUERY_ELEMENT_INVALID", Assert.Throws<RekallAgeMeshQueryException>(() =>
            query.Resolve(mesh, new(RekallAgeGeometryDomain.Face, ExplicitElementIds: [999]), 10)).Code);
        Assert.Equal("REKALL_MESH_QUERY_SELECTION_DOMAIN_INVALID", Assert.Throws<RekallAgeMeshQueryException>(() =>
            query.Resolve(mesh, new(RekallAgeGeometryDomain.Point, SelectionSetName: "all-faces"), 10)).Code);
        Assert.Equal("REKALL_MESH_QUERY_LIMIT_INVALID", Assert.Throws<RekallAgeMeshQueryException>(() =>
            query.Resolve(mesh, new(RekallAgeGeometryDomain.Point), 0)).Code);
    }

    private static RekallAgeMeshAsset CreateMesh()
    {
        return RekallAgeMeshAsset.Create(
            "query",
            "Query",
            new RekallAgeMeshTopology(
                PointIds: [1, 2, 3, 4],
                Positions: [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0)],
                EdgeIds: [11, 12, 13, 14, 15],
                EdgePointIndices: [new(0, 1), new(1, 2), new(2, 0), new(2, 3), new(3, 0)],
                FaceIds: [21, 22],
                FaceOffsets: [0, 3, 6],
                CornerIds: [31, 32, 33, 34, 35, 36],
                CornerPointIndices: [0, 1, 2, 0, 2, 3],
                CornerEdgeIndices: [0, 1, 2, 2, 3, 4]),
            attributes:
            [
                new RekallAgeGeometryAttribute(
                    "material.index",
                    RekallAgeGeometryDomain.Face,
                    RekallAgeGeometryValueType.Int32,
                    [JsonSerializer.SerializeToElement(0), JsonSerializer.SerializeToElement(1)],
                    Semantic: "material-index")
            ],
            materialSlots: [new("first", null), new("second", null)],
            selectionSets:
            [
                new("all-faces", RekallAgeGeometryDomain.Face, [21, 22], ActiveElementId: 21, OrderedHistory: [21, 22])
            ]);
    }
}
