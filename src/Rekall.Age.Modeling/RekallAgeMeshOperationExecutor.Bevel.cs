using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed partial class RekallAgeMeshOperationExecutor
{
    private sealed record BevelFace(int[] Points, int SourceFace, int?[] CornerSources);

    private static RekallAgeMeshOperationResult BevelEdges(RekallAgeMeshAsset source, RekallAgeMeshOperationRequest request)
    {
        RequireDomain(request, RekallAgeGeometryDomain.Edge);
        var topology = source.Topology;
        var selected = request.ElementIds.Order().ToArray();
        if (!selected.SequenceEqual(topology.EdgeIds.Order()))
            throw Failure("REKALL_MESH_BEVEL_PARTIAL_UNSUPPORTED", "The current deterministic bevel requires every mesh edge; use a complete edge selection.");

        var width = ReadFiniteDouble(request.Parameters, "width");
        var segments = ReadBoundedInt(request.Parameters, "segments", 1, 1, 64);
        var profile = ReadFiniteDouble(request.Parameters, "profile", 0.5);
        var clampOverlap = ReadBoolean(request.Parameters, "clampOverlap", true);
        _ = ReadBoolean(request.Parameters, "hardenNormals", false);
        if (width <= 0 || profile is < 0.01 or > 0.99)
            throw Failure("REKALL_MESH_OPERATION_PARAMETER_INVALID", "Bevel width must be positive and profile must be between 0.01 and 0.99.");

        var positions = new List<RekallAgeGeometryVector3>();
        var sourcePointByOutput = new List<int>();
        var insetByCorner = new Dictionary<int, int>();
        var faces = new List<BevelFace>();
        for (var faceIndex = 0; faceIndex < topology.FaceIds.Count; faceIndex++)
        {
            var start = topology.FaceOffsets[faceIndex];
            var end = topology.FaceOffsets[faceIndex + 1];
            var count = end - start;
            var center = new RekallAgeGeometryVector3(
                Enumerable.Range(start, count).Average(corner => topology.Positions[topology.CornerPointIndices[corner]].X),
                Enumerable.Range(start, count).Average(corner => topology.Positions[topology.CornerPointIndices[corner]].Y),
                Enumerable.Range(start, count).Average(corner => topology.Positions[topology.CornerPointIndices[corner]].Z));
            var inset = new int[count];
            var insetCornerSources = new int?[count];
            for (var local = 0; local < count; local++)
            {
                var corner = start + local;
                var sourcePoint = topology.CornerPointIndices[corner];
                var point = topology.Positions[sourcePoint];
                var towardCenter = Subtract(center, point);
                var available = Math.Sqrt(Dot(towardCenter, towardCenter));
                if (available <= 1e-9)
                    throw Failure("REKALL_MESH_BEVEL_FACE_DEGENERATE", "Bevel cannot inset a face corner at its centroid.");
                var safeWidth = available * 0.45;
                if (!clampOverlap && width > safeWidth)
                    throw Failure("REKALL_MESH_BEVEL_OVERLAP", "Bevel width overlaps an inset face; reduce width or enable clampOverlap.");
                var insetWidth = Math.Min(width, safeWidth);
                var scale = insetWidth / available;
                inset[local] = positions.Count;
                positions.Add(BevelAdd(point, BevelScale(towardCenter, scale)));
                sourcePointByOutput.Add(sourcePoint);
                insetByCorner[corner] = inset[local];
                insetCornerSources[local] = corner;
            }
            faces.Add(new(inset, faceIndex, insetCornerSources));
        }

        var edgeCorners = new Dictionary<int, List<(int Corner, int Face)>>();
        for (var face = 0; face < topology.FaceIds.Count; face++)
            for (var corner = topology.FaceOffsets[face]; corner < topology.FaceOffsets[face + 1]; corner++)
            {
                var edge = topology.CornerEdgeIndices[corner];
                if (!edgeCorners.TryGetValue(edge, out var uses)) edgeCorners[edge] = uses = [];
                uses.Add((corner, face));
            }

        foreach (var (edgeIndex, uses) in edgeCorners.OrderBy(item => item.Key))
        {
            if (uses.Count != 2)
                throw Failure("REKALL_MESH_BEVEL_NON_MANIFOLD", "Bevel requires a closed two-face manifold edge set.");
            var edge = topology.EdgePointIndices[edgeIndex];
            var first = uses[0];
            var second = uses[1];
            var firstNext = NextCorner(topology, first.Corner, first.Face);
            var secondNext = NextCorner(topology, second.Corner, second.Face);
            var firstA = topology.CornerPointIndices[first.Corner] == edge.A ? first.Corner : firstNext;
            var firstB = firstA == first.Corner ? firstNext : first.Corner;
            var secondA = topology.CornerPointIndices[second.Corner] == edge.A ? second.Corner : secondNext;
            var secondB = secondA == second.Corner ? secondNext : second.Corner;
            var chainA = BuildProfileChain(edge.A, insetByCorner[firstA], insetByCorner[secondA]);
            var chainB = BuildProfileChain(edge.B, insetByCorner[firstB], insetByCorner[secondB]);
            for (var segment = 0; segment < segments; segment++)
                faces.Add(new([chainA[segment], chainB[segment], chainB[segment + 1], chainA[segment + 1]], first.Face, [firstA, firstB, secondB, secondA]));
        }

        for (var pointIndex = 0; pointIndex < topology.PointIds.Count; pointIndex++)
        {
            var incident = sourcePointByOutput.Select((sourcePoint, outputPoint) => (sourcePoint, outputPoint))
                .Where(item => item.sourcePoint == pointIndex).Select(item => item.outputPoint).Distinct().ToArray();
            if (incident.Length < 3) continue;
            var sourcePosition = topology.Positions[pointIndex];
            var axis = Normalize(new(
                incident.Sum(index => positions[index].X - sourcePosition.X),
                incident.Sum(index => positions[index].Y - sourcePosition.Y),
                incident.Sum(index => positions[index].Z - sourcePosition.Z)));
            var reference = Normalize(Subtract(positions[incident[0]], sourcePosition));
            var tangent = Normalize(Cross(axis, reference));
            var ordered = incident.OrderBy(index => Math.Atan2(Dot(Subtract(positions[index], sourcePosition), tangent), Dot(Subtract(positions[index], sourcePosition), reference))).ToArray();
            var capFace = Enumerable.Range(0, topology.FaceIds.Count)
                .First(face => FaceCornerSourceIndices(face, topology).Any(corner => topology.CornerPointIndices[corner] == pointIndex));
            if (segments == 1)
            {
                faces.Add(new(ordered, capFace, ordered.Select(index => (int?)FindCorner(sourcePointByOutput[index], capFace)).ToArray()));
            }
            else
            {
                // A segmented bevel cap is intentionally emitted as a triangle fan.
                // Large ngons at poles or irregular valence can be non-planar and
                // self-intersect under projection even when their boundary is valid.
                var capCenter = positions.Count;
                positions.Add(new(
                    ordered.Average(index => positions[index].X),
                    ordered.Average(index => positions[index].Y),
                    ordered.Average(index => positions[index].Z)));
                sourcePointByOutput.Add(pointIndex);
                for (var index = 0; index < ordered.Length; index++)
                {
                    var next = (index + 1) % ordered.Length;
                    faces.Add(new(
                        [capCenter, ordered[index], ordered[next]],
                        capFace,
                        [FindCorner(pointIndex, capFace), FindCorner(sourcePointByOutput[ordered[index]], capFace), FindCorner(sourcePointByOutput[ordered[next]], capFace)]));
                }
            }
        }

        var edgeMap = new Dictionary<(int, int), int>();
        var edges = new List<RekallAgeMeshEdgePointIndices>();
        var outputEdgeSources = new List<int?>();
        var faceOffsets = new List<int> { 0 };
        var cornerPoints = new List<int>();
        var cornerEdges = new List<int>();
        var cornerSources = new List<int?>();
        foreach (var face in faces)
        {
            for (var index = 0; index < face.Points.Length; index++)
            {
                var a = face.Points[index];
                var b = face.Points[(index + 1) % face.Points.Length];
                var key = a < b ? (a, b) : (b, a);
                if (!edgeMap.TryGetValue(key, out var outputEdge))
                {
                    outputEdge = edges.Count;
                    edgeMap[key] = outputEdge;
                    edges.Add(new(a, b));
                    outputEdgeSources.Add(FindSourceEdge(sourcePointByOutput[a], sourcePointByOutput[b]));
                }
                cornerPoints.Add(a);
                cornerEdges.Add(outputEdge);
                cornerSources.Add(face.CornerSources[index] ?? FindCorner(sourcePointByOutput[a], face.SourceFace));
            }
            faceOffsets.Add(cornerPoints.Count);
        }

        var pointIds = AllocateIds(topology.PointIds, positions.Count);
        var edgeIds = AllocateIds(topology.EdgeIds, edges.Count);
        var faceIds = AllocateIds(topology.FaceIds, faces.Count);
        var cornerIds = AllocateIds(topology.CornerIds, cornerPoints.Count);
        var attributes = source.Attributes.Select(attribute => attribute.Domain switch
        {
            RekallAgeGeometryDomain.Point => attribute with { Values = sourcePointByOutput.Select(index => attribute.Values[index]).ToArray() },
            RekallAgeGeometryDomain.Edge => attribute with { Values = outputEdgeSources.Select(index => index.HasValue ? attribute.Values[index.Value] : DefaultValue(attribute)).ToArray() },
            RekallAgeGeometryDomain.Face => attribute with { Values = faces.Select(face => attribute.Values[face.SourceFace]).ToArray() },
            RekallAgeGeometryDomain.Corner => attribute with { Values = cornerSources.Select(index => index.HasValue ? attribute.Values[index.Value] : DefaultValue(attribute)).ToArray() },
            _ => attribute
        }).ToArray();
        var mesh = source with
        {
            Revision = checked(source.Revision + 1),
            Topology = new(pointIds, positions, edgeIds, edges, faceIds, faceOffsets, cornerIds, cornerPoints, cornerEdges),
            Attributes = attributes,
            SelectionSets = []
        };

        var provenance = new List<RekallAgeMeshElementProvenance>();
        provenance.AddRange(topology.PointIds.Select((id, index) => new RekallAgeMeshElementProvenance(RekallAgeGeometryDomain.Point, id,
            sourcePointByOutput.Select((sourceIndex, outputIndex) => (sourceIndex, outputIndex)).Where(item => item.sourceIndex == index).Select(item => pointIds[item.outputIndex]).ToArray())));
        provenance.AddRange(topology.EdgeIds.Select((id, index) => new RekallAgeMeshElementProvenance(RekallAgeGeometryDomain.Edge, id,
            outputEdgeSources.Select((sourceIndex, outputIndex) => (sourceIndex, outputIndex)).Where(item => item.sourceIndex == index).Select(item => edgeIds[item.outputIndex]).ToArray())));
        provenance.AddRange(topology.FaceIds.Select((id, index) => new RekallAgeMeshElementProvenance(RekallAgeGeometryDomain.Face, id,
            faces.Select((face, outputIndex) => (face, outputIndex)).Where(item => item.face.SourceFace == index).Select(item => faceIds[item.outputIndex]).ToArray())));
        provenance.AddRange(topology.CornerIds.Select((id, index) => new RekallAgeMeshElementProvenance(RekallAgeGeometryDomain.Corner, id,
            cornerSources.Select((sourceIndex, outputIndex) => (sourceIndex, outputIndex)).Where(item => item.sourceIndex == index).Select(item => cornerIds[item.outputIndex]).ToArray())));
        return Result(source, mesh, ChangeSet(
            RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Positions
            | (source.Attributes.Count > 0 ? RekallAgeMeshChangeKind.Attributes : RekallAgeMeshChangeKind.None)
            | (source.SelectionSets.Count > 0 ? RekallAgeMeshChangeKind.Selection : RekallAgeMeshChangeKind.None),
            createdPoints: pointIds, createdEdges: edgeIds, createdFaces: faceIds, createdCorners: cornerIds,
            deletedPoints: topology.PointIds, deletedEdges: topology.EdgeIds, deletedFaces: topology.FaceIds, deletedCorners: topology.CornerIds,
            changedAttributes: source.Attributes.Select(item => item.Name).Order(StringComparer.Ordinal).ToArray(),
            affectedBounds: Bounds(topology.Positions.Concat(positions))), provenance);

        int[] BuildProfileChain(int sourcePoint, int firstOutput, int secondOutput)
        {
            var chain = new int[segments + 1];
            chain[0] = firstOutput;
            chain[^1] = secondOutput;
            var origin = topology.Positions[sourcePoint];
            for (var segment = 1; segment < segments; segment++)
            {
                chain[segment] = positions.Count;
                positions.Add(SphericalInterpolate(origin, positions[firstOutput], positions[secondOutput], ProfileParameter((double)segment / segments, profile)));
                sourcePointByOutput.Add(sourcePoint);
            }
            return chain;
        }

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

        int FindCorner(int sourcePoint, int preferredFace)
        {
            foreach (var corner in FaceCornerSourceIndices(preferredFace, topology))
                if (topology.CornerPointIndices[corner] == sourcePoint) return corner;
            for (var corner = 0; corner < topology.CornerIds.Count; corner++)
                if (topology.CornerPointIndices[corner] == sourcePoint) return corner;
            return 0;
        }
    }

    private static int NextCorner(RekallAgeMeshTopology topology, int corner, int face) =>
        corner + 1 == topology.FaceOffsets[face + 1] ? topology.FaceOffsets[face] : corner + 1;

    private static IReadOnlyList<ulong> AllocateIds(IReadOnlyCollection<ulong> sourceIds, int count)
    {
        var first = NextId(sourceIds);
        return Enumerable.Range(0, count).Select(index => checked(first + (ulong)index)).ToArray();
    }

    private static double ProfileParameter(double t, double profile) => Math.Pow(t, Math.Log(0.5) / Math.Log(profile));

    private static RekallAgeGeometryVector3 SphericalInterpolate(RekallAgeGeometryVector3 origin, RekallAgeGeometryVector3 first, RekallAgeGeometryVector3 second, double t)
    {
        var firstVector = Subtract(first, origin);
        var secondVector = Subtract(second, origin);
        var firstLength = Math.Sqrt(Dot(firstVector, firstVector));
        var secondLength = Math.Sqrt(Dot(secondVector, secondVector));
        var firstDirection = Normalize(firstVector);
        var secondDirection = Normalize(secondVector);
        var cosine = Math.Clamp(Dot(firstDirection, secondDirection), -1, 1);
        var angle = Math.Acos(cosine);
        var direction = angle <= 1e-7
            ? Normalize(BevelAdd(BevelScale(firstDirection, 1 - t), BevelScale(secondDirection, t)))
            : BevelAdd(BevelScale(firstDirection, Math.Sin((1 - t) * angle) / Math.Sin(angle)), BevelScale(secondDirection, Math.Sin(t * angle) / Math.Sin(angle)));
        return BevelAdd(origin, BevelScale(direction, firstLength + (secondLength - firstLength) * t));
    }

    private static RekallAgeGeometryVector3 BevelAdd(RekallAgeGeometryVector3 a, RekallAgeGeometryVector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    private static RekallAgeGeometryVector3 BevelScale(RekallAgeGeometryVector3 value, double scale) => new(value.X * scale, value.Y * scale, value.Z * scale);
    private static RekallAgeGeometryVector3 Subtract(RekallAgeGeometryVector3 a, RekallAgeGeometryVector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    private static double Dot(RekallAgeGeometryVector3 a, RekallAgeGeometryVector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static RekallAgeGeometryVector3 Cross(RekallAgeGeometryVector3 a, RekallAgeGeometryVector3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    private static RekallAgeGeometryVector3 Normalize(RekallAgeGeometryVector3 value)
    {
        var length = Math.Sqrt(Dot(value, value));
        return length <= 1e-12 ? new(1, 0, 0) : new(value.X / length, value.Y / length, value.Z / length);
    }
}
