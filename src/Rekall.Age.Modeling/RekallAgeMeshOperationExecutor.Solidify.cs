using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed partial class RekallAgeMeshOperationExecutor
{
    private sealed record SolidFace(int[] Points, int SourceFace, int[] CornerSources);

    private static RekallAgeMeshOperationResult Solidify(RekallAgeMeshAsset source, RekallAgeMeshOperationRequest request)
    {
        RequireDomain(request, RekallAgeGeometryDomain.Face);
        if (!request.ElementIds.Order().SequenceEqual(source.Topology.FaceIds.Order()))
            throw Failure("REKALL_MESH_SOLIDIFY_PARTIAL_UNSUPPORTED", "Solidify currently requires the complete surface face selection.");
        var topology = source.Topology;
        var thickness = ReadFiniteDouble(request.Parameters, "thickness");
        var offset = ReadFiniteDouble(request.Parameters, "offset", 0);
        var rim = ReadBoolean(request.Parameters, "rim", true);
        _ = ReadBoolean(request.Parameters, "evenThickness", true);
        if (Math.Abs(thickness) <= 1e-9 || offset is < -1 or > 1)
            throw Failure("REKALL_MESH_OPERATION_PARAMETER_INVALID", "Solidify thickness must be non-zero and offset must be between -1 and 1.");

        var faceNormals = Enumerable.Range(0, topology.FaceIds.Count)
            .Select(face => SolidNormal(FaceCornerSourceIndices(face, topology).Select(corner => topology.Positions[topology.CornerPointIndices[corner]]).ToArray())).ToArray();
        var pointNormals = Enumerable.Range(0, topology.PointIds.Count).Select(point =>
        {
            var incident = Enumerable.Range(0, topology.FaceIds.Count).Where(face => FaceCornerSourceIndices(face, topology).Any(corner => topology.CornerPointIndices[corner] == point)).ToArray();
            var sum = incident.Aggregate(new RekallAgeGeometryVector3(0, 0, 0), (value, face) => SolidAdd(value, faceNormals[face]));
            return SolidNormalize(sum);
        }).ToArray();
        var outside = thickness * (offset + 1) * 0.5;
        var inside = thickness * (offset - 1) * 0.5;
        var positions = topology.Positions.Select((point, index) => SolidAdd(point, SolidScale(pointNormals[index], outside)))
            .Concat(topology.Positions.Select((point, index) => SolidAdd(point, SolidScale(pointNormals[index], inside)))).ToArray();
        var sourcePointByOutput = Enumerable.Range(0, topology.PointIds.Count).Concat(Enumerable.Range(0, topology.PointIds.Count)).ToArray();
        var faces = new List<SolidFace>();
        for (var face = 0; face < topology.FaceIds.Count; face++)
        {
            var corners = FaceCornerSourceIndices(face, topology).ToArray();
            faces.Add(new(corners.Select(corner => topology.CornerPointIndices[corner]).ToArray(), face, corners));
            faces.Add(new(corners.Reverse().Select(corner => topology.CornerPointIndices[corner] + topology.PointIds.Count).ToArray(), face, corners.Reverse().ToArray()));
        }
        var edgeUses = topology.CornerEdgeIndices.GroupBy(index => index).ToDictionary(group => group.Key, group => group.Count());
        if (rim)
        {
            for (var edgeIndex = 0; edgeIndex < topology.EdgeIds.Count; edgeIndex++)
            {
                if (edgeUses.GetValueOrDefault(edgeIndex) != 1) continue;
                var edge = topology.EdgePointIndices[edgeIndex];
                var corner = Enumerable.Range(0, topology.CornerIds.Count).First(index => topology.CornerEdgeIndices[index] == edgeIndex);
                var face = FaceIndexForCorner(topology.FaceOffsets, corner);
                var firstCorner = FindCorner(edge.A, face);
                var secondCorner = FindCorner(edge.B, face);
                faces.Add(new([edge.A, edge.B, edge.B + topology.PointIds.Count, edge.A + topology.PointIds.Count], face, [firstCorner, secondCorner, secondCorner, firstCorner]));
            }
        }

        var edges = new List<RekallAgeMeshEdgePointIndices>();
        var edgeMap = new Dictionary<(int, int), int>();
        var faceOffsets = new List<int> { 0 };
        var cornerPoints = new List<int>();
        var cornerEdges = new List<int>();
        var cornerSources = new List<int>();
        foreach (var face in faces)
        {
            for (var local = 0; local < face.Points.Length; local++)
            {
                var first = face.Points[local]; var second = face.Points[(local + 1) % face.Points.Length];
                var key = first < second ? (first, second) : (second, first);
                if (!edgeMap.TryGetValue(key, out var edge)) { edge = edges.Count; edgeMap[key] = edge; edges.Add(new(first, second)); }
                cornerPoints.Add(first); cornerEdges.Add(edge); cornerSources.Add(face.CornerSources[local]);
            }
            faceOffsets.Add(cornerPoints.Count);
        }
        var pointIds = AllocateIds(topology.PointIds, positions.Length);
        var edgeIds = AllocateIds(topology.EdgeIds, edges.Count);
        var faceIds = AllocateIds(topology.FaceIds, faces.Count);
        var cornerIds = AllocateIds(topology.CornerIds, cornerPoints.Count);
        var attributes = source.Attributes.Select(attribute => attribute.Domain switch
        {
            RekallAgeGeometryDomain.Point => attribute with { Values = sourcePointByOutput.Select(index => attribute.Values[index]).ToArray() },
            RekallAgeGeometryDomain.Edge => attribute with { Values = Enumerable.Repeat(DefaultValue(attribute), edges.Count).ToArray() },
            RekallAgeGeometryDomain.Face => attribute with { Values = faces.Select(face => attribute.Values[face.SourceFace]).ToArray() },
            RekallAgeGeometryDomain.Corner => attribute with { Values = cornerSources.Select(index => attribute.Values[index]).ToArray() },
            _ => attribute
        }).ToArray();
        var mesh = source with { Revision = checked(source.Revision + 1), Topology = new(pointIds, positions, edgeIds, edges, faceIds, faceOffsets, cornerIds, cornerPoints, cornerEdges), Attributes = attributes, SelectionSets = [] };
        return Result(source, mesh, ChangeSet(RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Positions | (attributes.Length > 0 ? RekallAgeMeshChangeKind.Attributes : RekallAgeMeshChangeKind.None),
            createdPoints: pointIds, createdEdges: edgeIds, createdFaces: faceIds, createdCorners: cornerIds,
            deletedPoints: topology.PointIds, deletedEdges: topology.EdgeIds, deletedFaces: topology.FaceIds, deletedCorners: topology.CornerIds,
            changedAttributes: source.Attributes.Select(item => item.Name).ToArray(), affectedBounds: Bounds(topology.Positions.Concat(positions))),
            topology.FaceIds.Select((id, index) => new RekallAgeMeshElementProvenance(RekallAgeGeometryDomain.Face, id, faces.Select((face, output) => (face, output)).Where(item => item.face.SourceFace == index).Select(item => faceIds[item.output]).ToArray())).ToArray());

        int FindCorner(int point, int face) => FaceCornerSourceIndices(face, topology).First(corner => topology.CornerPointIndices[corner] == point);
    }

    private static RekallAgeGeometryVector3 SolidNormal(IReadOnlyList<RekallAgeGeometryVector3> points)
    {
        var normal = new RekallAgeGeometryVector3(0, 0, 0);
        for (var i = 0; i < points.Count; i++)
        { var a = points[i]; var b = points[(i + 1) % points.Count]; normal = new(normal.X + (a.Y - b.Y) * (a.Z + b.Z), normal.Y + (a.Z - b.Z) * (a.X + b.X), normal.Z + (a.X - b.X) * (a.Y + b.Y)); }
        return SolidNormalize(normal);
    }
    private static RekallAgeGeometryVector3 SolidNormalize(RekallAgeGeometryVector3 value) { var length = Math.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z); if (length <= 1e-12) throw Failure("REKALL_MESH_SOLIDIFY_NORMAL_INVALID", "Solidify requires finite non-degenerate surface normals."); return SolidScale(value, 1 / length); }
    private static RekallAgeGeometryVector3 SolidAdd(RekallAgeGeometryVector3 a, RekallAgeGeometryVector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    private static RekallAgeGeometryVector3 SolidScale(RekallAgeGeometryVector3 value, double scale) => new(value.X * scale, value.Y * scale, value.Z * scale);
}
