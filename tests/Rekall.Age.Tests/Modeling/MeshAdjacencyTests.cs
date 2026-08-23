using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class MeshAdjacencyTests
{
    [Fact]
    public void BuildsStableIdAdjacencyForFacesPointsEdgesAndLooseEdges()
    {
        var mesh = CreateTwoTrianglesAndLooseEdge();

        var adjacency = RekallAgeMeshAdjacency.Build(mesh);

        Assert.Equal([11UL, 13UL, 15UL, 16UL], adjacency.EdgesForPoint(1));
        Assert.Equal([21UL, 22UL], adjacency.FacesForPoint(1));
        Assert.Equal([21UL, 22UL], adjacency.FacesForEdge(11));
        Assert.Equal([22UL], adjacency.NeighborFaces(21));
        Assert.Equal([21UL], adjacency.NeighborFaces(22));
        Assert.Empty(adjacency.FacesForEdge(16));
        Assert.Equal([1UL, 5UL], adjacency.PointsForEdge(16));
    }

    [Fact]
    public void RejectsInvalidMeshBeforeBuildingAdjacency()
    {
        var mesh = CreateTwoTrianglesAndLooseEdge() with
        {
            Topology = CreateTwoTrianglesAndLooseEdge().Topology with
            {
                CornerEdgeIndices = [99, 1, 2, 0, 3, 4]
            }
        };

        var error = Assert.Throws<InvalidDataException>(() => RekallAgeMeshAdjacency.Build(mesh));

        Assert.Contains("REKALL_MESH_CORNER_EDGE_REFERENCE_INVALID", error.Message, StringComparison.Ordinal);
    }

    private static RekallAgeMeshAsset CreateTwoTrianglesAndLooseEdge()
    {
        return RekallAgeMeshAsset.Create(
            "adjacency",
            "Adjacency",
            new RekallAgeMeshTopology(
                PointIds: [1, 2, 3, 4, 5],
                Positions: [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(2, 0, 0)],
                EdgeIds: [11, 12, 13, 14, 15, 16],
                EdgePointIndices: [new(0, 1), new(1, 2), new(2, 0), new(1, 3), new(3, 0), new(0, 4)],
                FaceIds: [21, 22],
                FaceOffsets: [0, 3, 6],
                CornerIds: [31, 32, 33, 34, 35, 36],
                CornerPointIndices: [0, 1, 2, 1, 0, 3],
                CornerEdgeIndices: [0, 1, 2, 0, 4, 3]));
    }
}
