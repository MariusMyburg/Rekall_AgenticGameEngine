using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed partial class RekallAgeMeshOperationExecutor
{
    private sealed record InsetFace(int[] Points, int SourceFace, int[] CornerSources);

    private static RekallAgeMeshOperationResult InsetFaces(RekallAgeMeshAsset source, RekallAgeMeshOperationRequest request)
    {
        RequireDomain(request, RekallAgeGeometryDomain.Face);
        var topology = source.Topology;
        var selectedFaces = ResolveIndices(topology.FaceIds, request.ElementIds, "face").Order().ToHashSet();
        var thickness = ReadFiniteDouble(request.Parameters, "thickness");
        var depth = ReadFiniteDouble(request.Parameters, "depth", 0);
        var individual = ReadBoolean(request.Parameters, "individual", false);
        var boundary = ReadBoolean(request.Parameters, "boundary", true);
        if (thickness <= 0)
            throw Failure("REKALL_MESH_OPERATION_PARAMETER_INVALID", "Inset thickness must be positive and finite.");
        if (!individual && selectedFaces.Count > 1)
            throw Failure("REKALL_MESH_INSET_REGION_UNSUPPORTED", "Connected multi-face region inset is not yet supported; enable individual mode or select one face.");
        if (!boundary)
            throw Failure("REKALL_MESH_INSET_BOUNDARY_UNSUPPORTED", "The current inset contract requires boundary faces.");

        var positions = topology.Positions.ToList();
        var sourcePointByOutput = Enumerable.Range(0, topology.PointIds.Count).ToList();
        var faces = new List<InsetFace>();
        for (var faceIndex = 0; faceIndex < topology.FaceIds.Count; faceIndex++)
        {
            var sourceCorners = FaceCornerSourceIndices(faceIndex, topology).ToArray();
            if (!selectedFaces.Contains(faceIndex))
            {
                faces.Add(new(sourceCorners.Select(corner => topology.CornerPointIndices[corner]).ToArray(), faceIndex, sourceCorners));
                continue;
            }

            var center = new RekallAgeGeometryVector3(
                sourceCorners.Average(corner => topology.Positions[topology.CornerPointIndices[corner]].X),
                sourceCorners.Average(corner => topology.Positions[topology.CornerPointIndices[corner]].Y),
                sourceCorners.Average(corner => topology.Positions[topology.CornerPointIndices[corner]].Z));
            var normal = InsetNormal(sourceCorners.Select(corner => topology.Positions[topology.CornerPointIndices[corner]]).ToArray());
            var insetPoints = new int[sourceCorners.Length];
            for (var local = 0; local < sourceCorners.Length; local++)
            {
                var corner = sourceCorners[local];
                var sourcePoint = topology.CornerPointIndices[corner];
                var point = topology.Positions[sourcePoint];
                var towardCenter = InsetSubtract(center, point);
                var available = Math.Sqrt(InsetDot(towardCenter, towardCenter));
                if (available <= 1e-9 || thickness >= available * 0.95)
                    throw Failure("REKALL_MESH_INSET_COLLAPSE", $"Inset would collapse face '{topology.FaceIds[faceIndex]}'; reduce thickness.");
                insetPoints[local] = positions.Count;
                positions.Add(InsetAdd(point, InsetAdd(InsetScale(towardCenter, thickness / available), InsetScale(normal, depth))));
                sourcePointByOutput.Add(sourcePoint);
            }
            faces.Add(new(insetPoints, faceIndex, sourceCorners));
            for (var local = 0; local < sourceCorners.Length; local++)
            {
                var next = (local + 1) % sourceCorners.Length;
                faces.Add(new(
                    [topology.CornerPointIndices[sourceCorners[local]], topology.CornerPointIndices[sourceCorners[next]], insetPoints[next], insetPoints[local]],
                    faceIndex,
                    [sourceCorners[local], sourceCorners[next], sourceCorners[next], sourceCorners[local]]));
            }
        }

        var edges = new List<RekallAgeMeshEdgePointIndices>();
        var edgeMap = new Dictionary<(int, int), int>();
        var edgeSources = new List<int?>();
        var faceOffsets = new List<int> { 0 };
        var cornerPoints = new List<int>();
        var cornerEdges = new List<int>();
        var cornerSources = new List<int>();
        foreach (var face in faces)
        {
            for (var local = 0; local < face.Points.Length; local++)
            {
                var first = face.Points[local];
                var second = face.Points[(local + 1) % face.Points.Length];
                var key = first < second ? (first, second) : (second, first);
                if (!edgeMap.TryGetValue(key, out var edgeIndex))
                {
                    edgeIndex = edges.Count;
                    edgeMap[key] = edgeIndex;
                    edges.Add(new(first, second));
                    edgeSources.Add(FindSourceEdge(sourcePointByOutput[first], sourcePointByOutput[second]));
                }
                cornerPoints.Add(first);
                cornerEdges.Add(edgeIndex);
                cornerSources.Add(face.CornerSources[local]);
            }
            faceOffsets.Add(cornerPoints.Count);
        }

        var pointIds = topology.PointIds.Concat(AllocateIds(topology.PointIds, positions.Count - topology.PointIds.Count)).ToArray();
        var edgeIds = AllocateIds(topology.EdgeIds, edges.Count);
        var faceIds = AllocateIds(topology.FaceIds, faces.Count);
        var cornerIds = AllocateIds(topology.CornerIds, cornerPoints.Count);
        var attributes = source.Attributes.Select(attribute => attribute.Domain switch
        {
            RekallAgeGeometryDomain.Point => attribute with { Values = sourcePointByOutput.Select(index => attribute.Values[index]).ToArray() },
            RekallAgeGeometryDomain.Edge => attribute with { Values = edgeSources.Select(index => index.HasValue ? attribute.Values[index.Value] : DefaultValue(attribute)).ToArray() },
            RekallAgeGeometryDomain.Face => attribute with { Values = faces.Select(face => attribute.Values[face.SourceFace]).ToArray() },
            RekallAgeGeometryDomain.Corner => attribute with { Values = cornerSources.Select(index => attribute.Values[index]).ToArray() },
            _ => attribute
        }).ToArray();
        var mesh = source with
        {
            Revision = checked(source.Revision + 1),
            Topology = new(pointIds, positions, edgeIds, edges, faceIds, faceOffsets, cornerIds, cornerPoints, cornerEdges),
            Attributes = attributes,
            SelectionSets = []
        };
        var createdPoints = pointIds.Skip(topology.PointIds.Count).ToArray();
        var provenance = topology.FaceIds.Select((id, sourceFace) => new RekallAgeMeshElementProvenance(
            RekallAgeGeometryDomain.Face,
            id,
            faces.Select((face, outputFace) => (face, outputFace)).Where(item => item.face.SourceFace == sourceFace).Select(item => faceIds[item.outputFace]).ToArray())).ToArray();
        return Result(source, mesh, ChangeSet(
            RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Positions
            | (source.Attributes.Count > 0 ? RekallAgeMeshChangeKind.Attributes : RekallAgeMeshChangeKind.None)
            | (source.SelectionSets.Count > 0 ? RekallAgeMeshChangeKind.Selection : RekallAgeMeshChangeKind.None),
            createdPoints: createdPoints, createdEdges: edgeIds, createdFaces: faceIds, createdCorners: cornerIds,
            deletedEdges: topology.EdgeIds, deletedFaces: topology.FaceIds, deletedCorners: topology.CornerIds,
            changedAttributes: source.Attributes.Select(item => item.Name).Order(StringComparer.Ordinal).ToArray(),
            affectedBounds: Bounds(topology.Positions.Concat(positions))), provenance);

        int? FindSourceEdge(int firstPoint, int secondPoint)
        {
            if (firstPoint == secondPoint) return null;
            var key = firstPoint < secondPoint ? (firstPoint, secondPoint) : (secondPoint, firstPoint);
            for (var index = 0; index < topology.EdgePointIndices.Count; index++)
            {
                var edge = topology.EdgePointIndices[index];
                var candidate = edge.A < edge.B ? (edge.A, edge.B) : (edge.B, edge.A);
                if (candidate == key) return index;
            }
            return null;
        }
    }

    private static RekallAgeGeometryVector3 InsetNormal(IReadOnlyList<RekallAgeGeometryVector3> points)
    {
        var normal = new RekallAgeGeometryVector3(0, 0, 0);
        for (var index = 0; index < points.Count; index++)
        {
            var current = points[index];
            var next = points[(index + 1) % points.Count];
            normal = new(
                normal.X + (current.Y - next.Y) * (current.Z + next.Z),
                normal.Y + (current.Z - next.Z) * (current.X + next.X),
                normal.Z + (current.X - next.X) * (current.Y + next.Y));
        }
        var length = Math.Sqrt(InsetDot(normal, normal));
        if (length <= 1e-12) throw Failure("REKALL_MESH_INSET_FACE_DEGENERATE", "Inset requires a face with a finite non-zero normal.");
        return InsetScale(normal, 1 / length);
    }

    private static RekallAgeGeometryVector3 InsetAdd(RekallAgeGeometryVector3 first, RekallAgeGeometryVector3 second) => new(first.X + second.X, first.Y + second.Y, first.Z + second.Z);
    private static RekallAgeGeometryVector3 InsetSubtract(RekallAgeGeometryVector3 first, RekallAgeGeometryVector3 second) => new(first.X - second.X, first.Y - second.Y, first.Z - second.Z);
    private static RekallAgeGeometryVector3 InsetScale(RekallAgeGeometryVector3 value, double scale) => new(value.X * scale, value.Y * scale, value.Z * scale);
    private static double InsetDot(RekallAgeGeometryVector3 first, RekallAgeGeometryVector3 second) => first.X * second.X + first.Y * second.Y + first.Z * second.Z;
}
