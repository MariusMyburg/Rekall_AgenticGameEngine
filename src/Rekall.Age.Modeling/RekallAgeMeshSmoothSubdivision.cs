using System.Text.Json;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed partial class RekallAgeMeshOperationExecutor
{
    private static RekallAgeMeshOperationResult SubdivideSmooth(
        RekallAgeMeshAsset source,
        RekallAgeMeshOperationRequest request)
    {
        RequireDomain(request, RekallAgeGeometryDomain.Face);
        if (!request.ElementIds.ToHashSet().SetEquals(source.Topology.FaceIds))
            throw Failure("REKALL_MESH_OPERATION_SMOOTH_REQUIRES_COMPLETE_SURFACE", "Smooth subdivision currently requires every face so it cannot introduce cracks across an unselected boundary.");

        var topology = source.Topology;
        var creaseAttributeName = ReadBoundedString(request.Parameters, "creaseAttribute", "crease.edge");
        var creaseAttribute = source.Attributes.FirstOrDefault(item => item.Name == creaseAttributeName);
        if (creaseAttribute is not null && (creaseAttribute.Domain != RekallAgeGeometryDomain.Edge || creaseAttribute.ValueType != RekallAgeGeometryValueType.Float))
            throw Failure("REKALL_MESH_OPERATION_ATTRIBUTE_CONFLICT", $"Attribute '{creaseAttributeName}' exists with an incompatible domain or type.");
        var edgeCreases = Enumerable.Range(0, topology.EdgeIds.Count).Select(index =>
        {
            if (creaseAttribute is null) return 0d;
            var value = creaseAttribute.Values[index].GetDouble();
            if (!double.IsFinite(value)) throw Failure("REKALL_MESH_OPERATION_PARAMETER_INVALID", $"Crease attribute '{creaseAttributeName}' contains a non-finite value.");
            return Math.Clamp(value, 0, 1);
        }).ToArray();
        var edgeFaces = Enumerable.Range(0, topology.EdgeIds.Count).Select(_ => new List<int>()).ToArray();
        var pointFaces = Enumerable.Range(0, topology.PointIds.Count).Select(_ => new HashSet<int>()).ToArray();
        var pointEdges = Enumerable.Range(0, topology.PointIds.Count).Select(_ => new List<int>()).ToArray();
        for (var edgeIndex = 0; edgeIndex < topology.EdgeIds.Count; edgeIndex++)
        {
            var edge = topology.EdgePointIndices[edgeIndex];
            pointEdges[edge.A].Add(edgeIndex); pointEdges[edge.B].Add(edgeIndex);
        }
        for (var faceIndex = 0; faceIndex < topology.FaceIds.Count; faceIndex++)
        {
            for (var corner = topology.FaceOffsets[faceIndex]; corner < topology.FaceOffsets[faceIndex + 1]; corner++)
            {
                var edgeIndex = topology.CornerEdgeIndices[corner];
                if (!edgeFaces[edgeIndex].Contains(faceIndex)) edgeFaces[edgeIndex].Add(faceIndex);
                pointFaces[topology.CornerPointIndices[corner]].Add(faceIndex);
            }
        }
        var nonManifold = edgeFaces.Select((faces, index) => (faces, index)).Where(item => item.faces.Count > 2).Select(item => topology.EdgeIds[item.index]).ToArray();
        if (nonManifold.Length > 0)
            throw Failure("REKALL_MESH_OPERATION_SMOOTH_NON_MANIFOLD", $"Smooth subdivision requires at most two incident faces per edge; invalid edge IDs: {string.Join(",", nonManifold)}.");

        var facePoints = Enumerable.Range(0, topology.FaceIds.Count)
            .Select(face => AveragePosition(FaceCornerSourceIndices(face, topology).Select(corner => topology.CornerPointIndices[corner]).ToArray(), topology.Positions))
            .ToArray();
        var edgePoints = Enumerable.Range(0, topology.EdgeIds.Count).Select(edgeIndex =>
        {
            var edge = topology.EdgePointIndices[edgeIndex];
            var a = topology.Positions[edge.A]; var b = topology.Positions[edge.B];
            var midpoint = Divide(Add(a, b), 2);
            if (edgeFaces[edgeIndex].Count != 2) return midpoint;
            var smooth = Divide(Add(Add(a, b), Add(facePoints[edgeFaces[edgeIndex][0]], facePoints[edgeFaces[edgeIndex][1]])), 4);
            return Lerp(smooth, midpoint, edgeCreases[edgeIndex]);
        }).ToArray();
        var smoothedOriginals = Enumerable.Range(0, topology.PointIds.Count).Select(pointIndex =>
        {
            var point = topology.Positions[pointIndex];
            var boundaryNeighbors = pointEdges[pointIndex]
                .Where(edge => edgeFaces[edge].Count == 1)
                .Select(edge => OtherPoint(topology.EdgePointIndices[edge], pointIndex)).Distinct().ToArray();
            if (boundaryNeighbors.Length == 2)
                return Divide(Add(Multiply(point, 6), Add(topology.Positions[boundaryNeighbors[0]], topology.Positions[boundaryNeighbors[1]])), 8);
            var adjacentFaces = pointFaces[pointIndex].Order().ToArray();
            var surfaceEdges = pointEdges[pointIndex].Where(edge => edgeFaces[edge].Count > 0).ToArray();
            if (adjacentFaces.Length == 0 || boundaryNeighbors.Length > 0 || surfaceEdges.Length == 0) return point;
            var faceAverage = AverageVectors(adjacentFaces.Select(face => facePoints[face]));
            var edgeMidpointAverage = AverageVectors(surfaceEdges.Select(edge =>
            {
                var endpoints = topology.EdgePointIndices[edge];
                return Divide(Add(topology.Positions[endpoints.A], topology.Positions[endpoints.B]), 2);
            }));
            var n = adjacentFaces.Length;
            var smooth = Divide(Add(Add(faceAverage, Multiply(edgeMidpointAverage, 2)), Multiply(point, n - 3)), n);
            var creased = pointEdges[pointIndex]
                .Where(edge => edgeCreases[edge] > 0)
                .Select(edge => (Edge: edge, Weight: edgeCreases[edge], Neighbor: OtherPoint(topology.EdgePointIndices[edge], pointIndex)))
                .OrderByDescending(item => item.Weight).ThenBy(item => topology.EdgeIds[item.Edge]).ToArray();
            if (creased.Length >= 3)
                return Lerp(smooth, point, creased.Take(3).Average(item => item.Weight));
            if (creased.Length == 2)
            {
                var creasePoint = Divide(Add(Multiply(point, 6), Add(topology.Positions[creased[0].Neighbor], topology.Positions[creased[1].Neighbor])), 8);
                return Lerp(smooth, creasePoint, (creased[0].Weight + creased[1].Weight) * 0.5);
            }
            return smooth;
        }).ToArray();

        var nextPointId = NextId(topology.PointIds); var pointIds = topology.PointIds.ToList();
        var positions = smoothedOriginals.ToList(); var edgePointIndices = new int[topology.EdgeIds.Count]; var facePointIndices = new int[topology.FaceIds.Count];
        var createdPoints = new List<ulong>();
        for (var edge = 0; edge < topology.EdgeIds.Count; edge++) { edgePointIndices[edge] = positions.Count; positions.Add(edgePoints[edge]); pointIds.Add(nextPointId); createdPoints.Add(nextPointId++); }
        for (var face = 0; face < topology.FaceIds.Count; face++) { facePointIndices[face] = positions.Count; positions.Add(facePoints[face]); pointIds.Add(nextPointId); createdPoints.Add(nextPointId++); }

        var nextEdgeId = NextId(topology.EdgeIds); var edgeIds = new List<ulong>(); var edges = new List<RekallAgeMeshEdgePointIndices>();
        var edgeAttributeSources = new List<int?>(); var segmentByEdgePoint = new Dictionary<(int Edge, int Point), int>(); var createdEdges = new List<ulong>();
        for (var edgeIndex = 0; edgeIndex < topology.EdgeIds.Count; edgeIndex++)
        {
            var sourceEdge = topology.EdgePointIndices[edgeIndex]; var edgePoint = edgePointIndices[edgeIndex];
            var first = edges.Count; edgeIds.Add(topology.EdgeIds[edgeIndex]); edges.Add(new(sourceEdge.A, edgePoint)); edgeAttributeSources.Add(edgeIndex); segmentByEdgePoint[(edgeIndex, sourceEdge.A)] = first;
            var secondId = nextEdgeId++; var second = edges.Count; edgeIds.Add(secondId); createdEdges.Add(secondId); edges.Add(new(edgePoint, sourceEdge.B)); edgeAttributeSources.Add(edgeIndex); segmentByEdgePoint[(edgeIndex, sourceEdge.B)] = second;
        }
        var radialByFaceEdge = new Dictionary<(int Face, int Edge), int>();
        for (var face = 0; face < topology.FaceIds.Count; face++)
        {
            for (var corner = topology.FaceOffsets[face]; corner < topology.FaceOffsets[face + 1]; corner++)
            {
                var sourceEdge = topology.CornerEdgeIndices[corner]; var key = (face, sourceEdge);
                if (radialByFaceEdge.ContainsKey(key)) continue;
                radialByFaceEdge[key] = edges.Count; var id = nextEdgeId++; edgeIds.Add(id); createdEdges.Add(id);
                edges.Add(new(edgePointIndices[sourceEdge], facePointIndices[face])); edgeAttributeSources.Add(null);
            }
        }

        var nextFaceId = NextId(topology.FaceIds); var nextCornerId = NextId(topology.CornerIds);
        var faceIds = new List<ulong>(); var faceOffsets = new List<int> { 0 }; var cornerIds = new List<ulong>();
        var cornerPoints = new List<int>(); var cornerEdges = new List<int>(); var faceSources = new List<int>(); var cornerAttributeSources = new List<int[]>();
        var createdFaces = new List<ulong>(); var createdCorners = new List<ulong>(); var faceProvenance = new List<RekallAgeMeshElementProvenance>();
        for (var face = 0; face < topology.FaceIds.Count; face++)
        {
            var start = topology.FaceOffsets[face]; var count = topology.FaceOffsets[face + 1] - start; var outputs = new List<ulong>();
            var allFaceCorners = Enumerable.Range(start, count).ToArray();
            for (var offset = 0; offset < count; offset++)
            {
                var current = start + offset; var previous = start + ((offset + count - 1) % count); var next = start + ((offset + 1) % count);
                var outgoingEdge = topology.CornerEdgeIndices[current]; var incomingEdge = topology.CornerEdgeIndices[previous]; var vertex = topology.CornerPointIndices[current];
                var faceId = offset == 0 ? topology.FaceIds[face] : nextFaceId++; if (offset > 0) createdFaces.Add(faceId); faceIds.Add(faceId); outputs.Add(faceId); faceSources.Add(face);
                AddCorner(topology.CornerIds[current], vertex, segmentByEdgePoint[(outgoingEdge, vertex)], [current]);
                AddCreatedCorner(edgePointIndices[outgoingEdge], radialByFaceEdge[(face, outgoingEdge)], [current, next]);
                AddCreatedCorner(facePointIndices[face], radialByFaceEdge[(face, incomingEdge)], allFaceCorners);
                AddCreatedCorner(edgePointIndices[incomingEdge], segmentByEdgePoint[(incomingEdge, vertex)], [previous, current]);
                faceOffsets.Add(cornerIds.Count);
            }
            faceProvenance.Add(new(RekallAgeGeometryDomain.Face, topology.FaceIds[face], outputs));
        }

        void AddCorner(ulong id, int point, int edge, int[] sources) { cornerIds.Add(id); cornerPoints.Add(point); cornerEdges.Add(edge); cornerAttributeSources.Add(sources); }
        void AddCreatedCorner(int point, int edge, int[] sources) { var id = nextCornerId++; createdCorners.Add(id); AddCorner(id, point, edge, sources); }

        var pointAttributeSources = Enumerable.Range(0, topology.PointIds.Count).Select(index => new[] { index })
            .Concat(topology.EdgePointIndices.Select(edge => new[] { edge.A, edge.B }))
            .Concat(Enumerable.Range(0, topology.FaceIds.Count).Select(face => FaceCornerSourceIndices(face, topology).Select(corner => topology.CornerPointIndices[corner]).ToArray())).ToArray();
        var attributes = source.Attributes.Select(attribute => attribute.Domain switch
        {
            RekallAgeGeometryDomain.Point => attribute with { Values = pointAttributeSources.Select(indices => Average(attribute, indices)).ToArray() },
            RekallAgeGeometryDomain.Edge => attribute with { Values = edgeAttributeSources.Select(index => index.HasValue ? attribute.Values[index.Value] : DefaultValue(attribute)).ToArray() },
            RekallAgeGeometryDomain.Face => attribute with { Values = faceSources.Select(index => attribute.Values[index]).ToArray() },
            RekallAgeGeometryDomain.Corner => attribute with { Values = cornerAttributeSources.Select(indices => Average(attribute, indices)).ToArray() },
            _ => attribute
        }).ToArray();
        var edgeProvenance = Enumerable.Range(0, topology.EdgeIds.Count).Select(edge => new RekallAgeMeshElementProvenance(
            RekallAgeGeometryDomain.Edge, topology.EdgeIds[edge], [edgeIds[edge * 2], edgeIds[edge * 2 + 1]])).ToArray();
        var selections = PropagateSmoothSelections(source.SelectionSets, faceProvenance, edgeProvenance);
        var mesh = source with
        {
            Revision = checked(source.Revision + 1),
            Topology = topology with { PointIds = pointIds, Positions = positions, EdgeIds = edgeIds, EdgePointIndices = edges, FaceIds = faceIds, FaceOffsets = faceOffsets, CornerIds = cornerIds, CornerPointIndices = cornerPoints, CornerEdgeIndices = cornerEdges },
            Attributes = attributes, SelectionSets = selections
        };
        var provenance = topology.PointIds.Select(id => Preserve(RekallAgeGeometryDomain.Point, id)).Concat(edgeProvenance).Concat(faceProvenance).ToArray();
        return Result(source, mesh, ChangeSet(
            RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Positions | (attributes.Length > 0 ? RekallAgeMeshChangeKind.Attributes : RekallAgeMeshChangeKind.None) | (selections.Count > 0 ? RekallAgeMeshChangeKind.Selection : RekallAgeMeshChangeKind.None),
            createdPoints: createdPoints, createdEdges: createdEdges, createdFaces: createdFaces, createdCorners: createdCorners,
            modifiedPoints: topology.PointIds.Order().ToArray(), modifiedEdges: topology.EdgeIds.Order().ToArray(), modifiedFaces: topology.FaceIds.Order().ToArray(), modifiedCorners: topology.CornerIds.Order().ToArray(),
            changedAttributes: attributes.Select(item => item.Name).Order(StringComparer.Ordinal).ToArray(), affectedBounds: Bounds(topology.Positions.Concat(positions))), provenance);
    }

    private static int OtherPoint(RekallAgeMeshEdgePointIndices edge, int point) => edge.A == point ? edge.B : edge.A;
    private static RekallAgeGeometryVector3 AveragePosition(IReadOnlyList<int> indices, IReadOnlyList<RekallAgeGeometryVector3> positions) => AverageVectors(indices.Select(index => positions[index]));
    private static RekallAgeGeometryVector3 AverageVectors(IEnumerable<RekallAgeGeometryVector3> values)
    {
        var array = values.ToArray();
        return new(array.Average(item => item.X), array.Average(item => item.Y), array.Average(item => item.Z));
    }
    private static RekallAgeGeometryVector3 Add(RekallAgeGeometryVector3 a, RekallAgeGeometryVector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    private static RekallAgeGeometryVector3 Multiply(RekallAgeGeometryVector3 value, double factor) => new(value.X * factor, value.Y * factor, value.Z * factor);
    private static RekallAgeGeometryVector3 Divide(RekallAgeGeometryVector3 value, double divisor) => Multiply(value, 1 / divisor);
    private static RekallAgeGeometryVector3 Lerp(RekallAgeGeometryVector3 a, RekallAgeGeometryVector3 b, double weight) =>
        Add(Multiply(a, 1 - weight), Multiply(b, weight));

    private static RekallAgeMeshOperationResult SetEdgeCrease(RekallAgeMeshAsset source, RekallAgeMeshOperationRequest request)
    {
        RequireDomain(request, RekallAgeGeometryDomain.Edge);
        var edgeIndices = ResolveIndices(source.Topology.EdgeIds, request.ElementIds, "edge");
        var weight = ReadFiniteDouble(request.Parameters, "weight");
        if (weight < 0 || weight > 1)
            throw Failure("REKALL_MESH_OPERATION_PARAMETER_INVALID", "Edge crease weight must be in [0, 1].");
        var attributeName = ReadBoundedString(request.Parameters, "attribute", "crease.edge");
        var existing = source.Attributes.FirstOrDefault(item => item.Name == attributeName);
        if (existing is not null && (existing.Domain != RekallAgeGeometryDomain.Edge || existing.ValueType != RekallAgeGeometryValueType.Float))
            throw Failure("REKALL_MESH_OPERATION_ATTRIBUTE_CONFLICT", $"Attribute '{attributeName}' exists with an incompatible domain or type.");
        var values = existing?.Values.ToArray() ?? Enumerable.Repeat(JsonSerializer.SerializeToElement(0d), source.Topology.EdgeIds.Count).ToArray();
        foreach (var index in edgeIndices) values[index] = JsonSerializer.SerializeToElement(weight);
        var attribute = new RekallAgeGeometryAttribute(attributeName, RekallAgeGeometryDomain.Edge, RekallAgeGeometryValueType.Float,
            values, "subdivision-crease", RekallAgeGeometryInterpolation.Linear, JsonSerializer.SerializeToElement(0d));
        var mesh = source with
        {
            Revision = checked(source.Revision + 1),
            Attributes = source.Attributes.Where(item => item.Name != attributeName).Append(attribute).OrderBy(item => item.Name, StringComparer.Ordinal).ToArray()
        };
        var ids = request.ElementIds.Order().ToArray();
        return Result(source, mesh, ChangeSet(RekallAgeMeshChangeKind.Attributes, modifiedEdges: ids, changedAttributes: [attributeName]),
            ids.Select(id => Preserve(RekallAgeGeometryDomain.Edge, id)).ToArray());
    }

    private static IReadOnlyList<RekallAgeMeshSelection> PropagateSmoothSelections(
        IReadOnlyList<RekallAgeMeshSelection> selections,
        IReadOnlyList<RekallAgeMeshElementProvenance> faceProvenance,
        IReadOnlyList<RekallAgeMeshElementProvenance> edgeProvenance)
    {
        var faceMap = faceProvenance.ToDictionary(item => item.InputElementId, item => item.OutputElementIds);
        var edgeMap = edgeProvenance.ToDictionary(item => item.InputElementId, item => item.OutputElementIds);
        return selections.Select(selection =>
        {
            var map = selection.Domain switch { RekallAgeGeometryDomain.Face => faceMap, RekallAgeGeometryDomain.Edge => edgeMap, _ => null };
            if (map is null) return selection;
            return selection with
            {
                ElementIds = Expand(selection.ElementIds, map),
                OrderedHistory = selection.OrderedHistory is null ? null : Expand(selection.OrderedHistory, map),
                ActiveElementId = selection.ActiveElementId.HasValue && map.TryGetValue(selection.ActiveElementId.Value, out var outputs) ? outputs[0] : selection.ActiveElementId
            };
        }).ToArray();
    }
}
