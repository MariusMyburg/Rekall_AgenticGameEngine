using System.Text.Json;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed partial class RekallAgeMeshOperationExecutor
{
    private sealed record LoopCutVertex(int Point, int CornerA, int CornerB, double T);
    private sealed record LoopCutFace(IReadOnlyList<LoopCutVertex> Vertices, int SourceFace);

    private static RekallAgeMeshOperationResult LoopCutEdges(
        RekallAgeMeshAsset source,
        RekallAgeMeshOperationRequest request)
    {
        RequireDomain(request, RekallAgeGeometryDomain.Edge);
        if (request.ElementIds.Count != 1)
            throw Failure("REKALL_MESH_LOOP_CUT_SELECTION_INVALID", "Loop Cut requires exactly one seed edge.");
        var factor = ReadFiniteDouble(request.Parameters, "factor", 0.5);
        if (factor <= 0 || factor >= 1)
            throw Failure("REKALL_MESH_LOOP_CUT_FACTOR_INVALID", "Loop Cut factor must be greater than zero and less than one.");

        var topology = source.Topology;
        var ringIds = RekallAgeMeshEdgeSplitKernel.ResolveQuadRing(source, request.ElementIds[0]);
        var availableEdgeIds = topology.EdgeIds.ToList();
        var ringIndices = ringIds.Select(id => availableEdgeIds.IndexOf(id)).ToHashSet();
        var splitFactorByEdge = ResolveSplitFactors(availableEdgeIds.IndexOf(request.ElementIds[0]), factor);
        var positions = topology.Positions.ToList();
        var pointSources = Enumerable.Range(0, topology.PointIds.Count).Select(index => (A: index, B: index, T: 0d)).ToList();
        var splitPointByEdge = new Dictionary<int, int>();
        foreach (var edgeIndex in ringIndices.Order())
        {
            var edge = topology.EdgePointIndices[edgeIndex];
            splitPointByEdge[edgeIndex] = positions.Count;
            var edgeFactor = splitFactorByEdge[edgeIndex];
            positions.Add(RekallAgeMeshEdgeSplitKernel.Interpolate(topology.Positions[edge.A], topology.Positions[edge.B], edgeFactor));
            pointSources.Add((edge.A, edge.B, edgeFactor));
        }

        var faces = new List<LoopCutFace>();
        for (var face = 0; face < topology.FaceIds.Count; face++)
        {
            var start = topology.FaceOffsets[face];
            var end = topology.FaceOffsets[face + 1];
            var vertices = new List<LoopCutVertex>();
            for (var corner = start; corner < end; corner++)
            {
                var point = topology.CornerPointIndices[corner];
                vertices.Add(new(point, corner, corner, 0));
                var edgeIndex = topology.CornerEdgeIndices[corner];
                if (!splitPointByEdge.TryGetValue(edgeIndex, out var splitPoint)) continue;
                var nextCorner = corner + 1 == end ? start : corner + 1;
                var edge = topology.EdgePointIndices[edgeIndex];
                var edgeFactor = splitFactorByEdge[edgeIndex];
                var localFactor = point == edge.A ? edgeFactor : 1 - edgeFactor;
                vertices.Add(new(splitPoint, corner, nextCorner, localFactor));
            }

            var cuts = vertices.Select((vertex, index) => (vertex, index))
                .Where(item => item.vertex.CornerA != item.vertex.CornerB).Select(item => item.index).ToArray();
            if (cuts.Length == 0)
            {
                faces.Add(new(vertices, face));
                continue;
            }
            if (end - start != 4 || cuts.Length != 2)
                throw Failure("REKALL_MESH_LOOP_CUT_TOPOLOGY_UNSUPPORTED", "Loop Cut currently requires a continuous ring of quad faces.");
            faces.Add(new(Path(vertices, cuts[0], cuts[1]), face));
            faces.Add(new(Path(vertices, cuts[1], cuts[0]), face));
        }

        var edges = new List<RekallAgeMeshEdgePointIndices>();
        var edgeMap = new Dictionary<(int, int), int>();
        var edgeSources = new List<int?>();
        var offsets = new List<int> { 0 };
        var cornerPoints = new List<int>();
        var cornerEdges = new List<int>();
        var cornerRefs = new List<LoopCutVertex>();
        foreach (var face in faces)
        {
            for (var local = 0; local < face.Vertices.Count; local++)
            {
                var vertex = face.Vertices[local];
                var next = face.Vertices[(local + 1) % face.Vertices.Count];
                var key = EdgeKey(vertex.Point, next.Point);
                if (!edgeMap.TryGetValue(key, out var edgeIndex))
                {
                    edgeIndex = edges.Count;
                    edgeMap.Add(key, edgeIndex);
                    edges.Add(new(vertex.Point, next.Point));
                    edgeSources.Add(FindExactSourceEdge(vertex.Point, next.Point));
                }
                cornerPoints.Add(vertex.Point);
                cornerEdges.Add(edgeIndex);
                cornerRefs.Add(vertex);
            }
            offsets.Add(cornerPoints.Count);
        }

        var createdPointIds = AllocateIds(topology.PointIds, positions.Count - topology.PointIds.Count);
        var pointIds = topology.PointIds.Concat(createdPointIds).ToArray();
        var edgeIds = AllocateIds(topology.EdgeIds, edges.Count);
        var faceIds = AllocateIds(topology.FaceIds, faces.Count);
        var cornerIds = AllocateIds(topology.CornerIds, cornerPoints.Count);
        var attributes = source.Attributes.Select(attribute => attribute.Domain switch
        {
            RekallAgeGeometryDomain.Point => attribute with
            {
                Values = pointSources.Select(item => Interpolate(attribute, item.A, item.B, item.T)).ToArray()
            },
            RekallAgeGeometryDomain.Edge => attribute with
            {
                Values = edgeSources.Select(index => index.HasValue ? attribute.Values[index.Value] : DefaultValue(attribute)).ToArray()
            },
            RekallAgeGeometryDomain.Face => attribute with
            {
                Values = faces.Select(face => attribute.Values[face.SourceFace]).ToArray()
            },
            RekallAgeGeometryDomain.Corner => attribute with
            {
                Values = cornerRefs.Select(item => Interpolate(attribute, item.CornerA, item.CornerB, item.T)).ToArray()
            },
            _ => attribute
        }).ToArray();
        var mesh = source with
        {
            Revision = checked(source.Revision + 1),
            Topology = new(pointIds, positions, edgeIds, edges, faceIds, offsets, cornerIds, cornerPoints, cornerEdges),
            Attributes = attributes,
            SelectionSets = []
        };
        var provenance = topology.FaceIds.Select((id, face) => new RekallAgeMeshElementProvenance(
            RekallAgeGeometryDomain.Face,
            id,
            faces.Select((candidate, index) => (candidate, index)).Where(item => item.candidate.SourceFace == face)
                .Select(item => faceIds[item.index]).ToArray())).ToArray();
        return Result(source, mesh, ChangeSet(
            RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Positions
            | (source.Attributes.Count > 0 ? RekallAgeMeshChangeKind.Attributes : RekallAgeMeshChangeKind.None)
            | (source.SelectionSets.Count > 0 ? RekallAgeMeshChangeKind.Selection : RekallAgeMeshChangeKind.None),
            createdPoints: createdPointIds,
            createdEdges: edgeIds,
            createdFaces: faceIds,
            createdCorners: cornerIds,
            deletedEdges: topology.EdgeIds,
            deletedFaces: topology.FaceIds,
            deletedCorners: topology.CornerIds,
            changedAttributes: source.Attributes.Select(item => item.Name).Order(StringComparer.Ordinal).ToArray(),
            affectedBounds: Bounds(positions)), provenance);

        int? FindExactSourceEdge(int a, int b)
        {
            if (a >= topology.PointIds.Count || b >= topology.PointIds.Count) return null;
            var key = EdgeKey(a, b);
            for (var index = 0; index < topology.EdgePointIndices.Count; index++)
                if (EdgeKey(topology.EdgePointIndices[index].A, topology.EdgePointIndices[index].B) == key) return index;
            return null;
        }

        Dictionary<int, double> ResolveSplitFactors(int seedEdge, double seedFactor)
        {
            var resolved = new Dictionary<int, double> { [seedEdge] = seedFactor };
            var pending = new Queue<int>();
            pending.Enqueue(seedEdge);
            while (pending.Count > 0)
            {
                var current = pending.Dequeue();
                for (var face = 0; face < topology.FaceIds.Count; face++)
                {
                    var start = topology.FaceOffsets[face];
                    var count = topology.FaceOffsets[face + 1] - start;
                    if (count != 4) continue;
                    var local = Enumerable.Range(0, 4).FirstOrDefault(index => topology.CornerEdgeIndices[start + index] == current, -1);
                    if (local < 0) continue;
                    var opposite = topology.CornerEdgeIndices[start + ((local + 2) % 4)];
                    if (!ringIndices.Contains(opposite) || resolved.ContainsKey(opposite)) continue;
                    var currentStartPoint = topology.CornerPointIndices[start + local];
                    var currentStored = topology.EdgePointIndices[current];
                    var currentBoundaryFactor = currentStartPoint == currentStored.A ? resolved[current] : 1 - resolved[current];
                    var oppositeLocal = (local + 2) % 4;
                    var oppositeStartPoint = topology.CornerPointIndices[start + oppositeLocal];
                    var oppositeStored = topology.EdgePointIndices[opposite];
                    var oppositeBoundaryFactor = 1 - currentBoundaryFactor;
                    resolved[opposite] = oppositeStartPoint == oppositeStored.A
                        ? oppositeBoundaryFactor
                        : 1 - oppositeBoundaryFactor;
                    pending.Enqueue(opposite);
                }
            }
            return resolved;
        }
    }

    private static IReadOnlyList<LoopCutVertex> Path(IReadOnlyList<LoopCutVertex> vertices, int start, int end)
    {
        var result = new List<LoopCutVertex>();
        for (var index = start;; index = (index + 1) % vertices.Count)
        {
            result.Add(vertices[index]);
            if (index == end) return result;
        }
    }
}
