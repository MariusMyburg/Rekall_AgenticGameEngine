using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed class RekallAgeMeshAdjacency
{
    private readonly IReadOnlyDictionary<ulong, ulong[]> _pointEdges;
    private readonly IReadOnlyDictionary<ulong, ulong[]> _pointFaces;
    private readonly IReadOnlyDictionary<ulong, ulong[]> _edgeFaces;
    private readonly IReadOnlyDictionary<ulong, ulong[]> _edgePoints;
    private readonly IReadOnlyDictionary<ulong, ulong[]> _faceNeighbors;

    private RekallAgeMeshAdjacency(
        IReadOnlyDictionary<ulong, ulong[]> pointEdges,
        IReadOnlyDictionary<ulong, ulong[]> pointFaces,
        IReadOnlyDictionary<ulong, ulong[]> edgeFaces,
        IReadOnlyDictionary<ulong, ulong[]> edgePoints,
        IReadOnlyDictionary<ulong, ulong[]> faceNeighbors)
    {
        _pointEdges = pointEdges;
        _pointFaces = pointFaces;
        _edgeFaces = edgeFaces;
        _edgePoints = edgePoints;
        _faceNeighbors = faceNeighbors;
    }

    public static RekallAgeMeshAdjacency Build(RekallAgeMeshAsset mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var validation = new RekallAgeMeshValidator().Validate(mesh);
        if (!validation.IsValid)
        {
            var codes = validation.Diagnostics
                .Where(item => item.Severity == RekallAgeMeshDiagnosticSeverity.Error)
                .Select(item => item.Code)
                .Distinct(StringComparer.Ordinal);
            throw new InvalidDataException("Cannot build adjacency for an invalid mesh: " + string.Join(", ", codes));
        }

        var topology = mesh.Topology;
        var pointEdges = topology.PointIds.ToDictionary(id => id, _ => new HashSet<ulong>());
        var pointFaces = topology.PointIds.ToDictionary(id => id, _ => new HashSet<ulong>());
        var edgeFaces = topology.EdgeIds.ToDictionary(id => id, _ => new HashSet<ulong>());
        var edgePoints = new Dictionary<ulong, ulong[]>();

        for (var edgeIndex = 0; edgeIndex < topology.EdgeIds.Count; edgeIndex++)
        {
            var edgeId = topology.EdgeIds[edgeIndex];
            var endpoints = topology.EdgePointIndices[edgeIndex];
            var firstPointId = topology.PointIds[endpoints.A];
            var secondPointId = topology.PointIds[endpoints.B];
            pointEdges[firstPointId].Add(edgeId);
            pointEdges[secondPointId].Add(edgeId);
            edgePoints.Add(edgeId, [firstPointId, secondPointId]);
        }

        for (var faceIndex = 0; faceIndex < topology.FaceIds.Count; faceIndex++)
        {
            var faceId = topology.FaceIds[faceIndex];
            for (var cornerIndex = topology.FaceOffsets[faceIndex]; cornerIndex < topology.FaceOffsets[faceIndex + 1]; cornerIndex++)
            {
                var pointId = topology.PointIds[topology.CornerPointIndices[cornerIndex]];
                var edgeId = topology.EdgeIds[topology.CornerEdgeIndices[cornerIndex]];
                pointFaces[pointId].Add(faceId);
                edgeFaces[edgeId].Add(faceId);
            }
        }

        var faceNeighbors = topology.FaceIds.ToDictionary(id => id, _ => new HashSet<ulong>());
        foreach (var faces in edgeFaces.Values)
        {
            foreach (var face in faces)
            {
                faceNeighbors[face].UnionWith(faces.Where(candidate => candidate != face));
            }
        }

        return new RekallAgeMeshAdjacency(
            Freeze(pointEdges),
            Freeze(pointFaces),
            Freeze(edgeFaces),
            edgePoints,
            Freeze(faceNeighbors));
    }

    public IReadOnlyList<ulong> EdgesForPoint(ulong pointId) => Get(_pointEdges, pointId);

    public IReadOnlyList<ulong> FacesForPoint(ulong pointId) => Get(_pointFaces, pointId);

    public IReadOnlyList<ulong> FacesForEdge(ulong edgeId) => Get(_edgeFaces, edgeId);

    public IReadOnlyList<ulong> PointsForEdge(ulong edgeId) => Get(_edgePoints, edgeId);

    public IReadOnlyList<ulong> NeighborFaces(ulong faceId) => Get(_faceNeighbors, faceId);

    private static IReadOnlyDictionary<ulong, ulong[]> Freeze(IReadOnlyDictionary<ulong, HashSet<ulong>> source)
    {
        return source.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Order().ToArray());
    }

    private static IReadOnlyList<ulong> Get(IReadOnlyDictionary<ulong, ulong[]> source, ulong id)
    {
        return source.TryGetValue(id, out var values) ? values : [];
    }
}
