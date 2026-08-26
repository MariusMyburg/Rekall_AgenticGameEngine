using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed partial class RekallAgeMeshOperationExecutor
{
    private sealed record CornerBlend(int A, int B, double T);
    private sealed record ConstructionFace(
        ulong? Id,
        IReadOnlyList<int> Points,
        int? SourceFace,
        IReadOnlyList<int?> CornerSources,
        IReadOnlyList<CornerBlend?>? CornerBlends = null);

    private static RekallAgeMeshOperationResult FillHoles(RekallAgeMeshAsset source, RekallAgeMeshOperationRequest request)
    {
        RequireDomain(request, RekallAgeGeometryDomain.Edge);
        var selected = ResolveIndices(source.Topology.EdgeIds, request.ElementIds, "edge").ToArray();
        var use = EdgeUseCounts(source.Topology);
        if (selected.Any(index => use[index] != 1))
            throw Failure("REKALL_MESH_FILL_HOLES_SELECTION_INVALID", "Fill Holes requires selected boundary edges used by exactly one face.");
        var loops = ExtractSimpleLoops(source.Topology, selected, "REKALL_MESH_FILL_HOLES_SELECTION_INVALID");
        var material = ReadMaterialIndex(source, request.Parameters);
        var faces = ExistingFaces(source.Topology);
        foreach (var rawLoop in loops)
        {
            var loop = OrientFillLoop(source.Topology, rawLoop);
            faces.Add(new(null, loop, null, Enumerable.Repeat<int?>(null, loop.Count).ToArray()));
        }
        var result = Rebuild(source, source.Topology.PointIds, source.Topology.Positions,
            Enumerable.Range(0, source.Topology.PointIds.Count).Select(index => new[] { index }).ToArray(), faces, material);
        var createdFaces = result.Mesh.Topology.FaceIds.Except(source.Topology.FaceIds).Order().ToArray();
        return result with
        {
            Provenance = request.ElementIds.Order().Select(id => Preserve(RekallAgeGeometryDomain.Edge, id)).ToArray(),
            Changes = result.Changes with { CreatedFaceIds = createdFaces, ModifiedEdgeIds = request.ElementIds.Order().ToArray() }
        };
    }

    private static RekallAgeMeshOperationResult BridgeEdgeLoops(RekallAgeMeshAsset source, RekallAgeMeshOperationRequest request)
    {
        RequireDomain(request, RekallAgeGeometryDomain.Edge);
        var selected = ResolveIndices(source.Topology.EdgeIds, request.ElementIds, "edge").ToArray();
        var use = EdgeUseCounts(source.Topology);
        if (selected.Any(index => use[index] != 1))
            throw Failure("REKALL_MESH_BRIDGE_LOOPS_SELECTION_INVALID", "Bridge Edge Loops requires boundary-edge selections.");
        var loops = ExtractSimpleLoops(source.Topology, selected, "REKALL_MESH_BRIDGE_LOOPS_SELECTION_INVALID");
        if (loops.Count != 2 || loops[0].Count != loops[1].Count)
            throw Failure("REKALL_MESH_BRIDGE_LOOPS_SELECTION_INVALID", "Bridge Edge Loops requires exactly two disjoint loops with equal point counts.");
        var first = loops.OrderBy(loop => source.Topology.PointIds[loop[0]]).First();
        var secondRaw = loops.Single(loop => !ReferenceEquals(loop, first));
        var second = AlignLoop(source.Topology.Positions, first, secondRaw);
        var material = ReadMaterialIndex(source, request.Parameters);
        var faces = ExistingFaces(source.Topology);
        var createdSourceEdges = new List<ulong>();
        for (var index = 0; index < first.Count; index++)
        {
            var next = (index + 1) % first.Count;
            faces.Add(new(null, [first[index], first[next], second[next], second[index]], null, [null, null, null, null]));
            createdSourceEdges.Add(source.Topology.EdgeIds[selected[index % selected.Length]]);
        }
        var result = Rebuild(source, source.Topology.PointIds, source.Topology.Positions,
            Enumerable.Range(0, source.Topology.PointIds.Count).Select(index => new[] { index }).ToArray(), faces, material);
        var createdFaces = result.Mesh.Topology.FaceIds.Except(source.Topology.FaceIds).Order().ToArray();
        return result with
        {
            Provenance = request.ElementIds.Order().Select(id => Preserve(RekallAgeGeometryDomain.Edge, id)).ToArray(),
            Changes = result.Changes with { CreatedFaceIds = createdFaces, ModifiedEdgeIds = request.ElementIds.Order().ToArray() }
        };
    }

    private static RekallAgeMeshOperationResult DissolveEdges(RekallAgeMeshAsset source, RekallAgeMeshOperationRequest request)
    {
        RequireDomain(request, RekallAgeGeometryDomain.Edge);
        var selected = ResolveIndices(source.Topology.EdgeIds, request.ElementIds, "edge").ToArray();
        if (selected.Length != 1)
            throw Failure("REKALL_MESH_DISSOLVE_SELECTION_INVALID", "Dissolve Edges currently requires exactly one selected edge.");
        var edge = selected[0];
        var adjacentFaces = FacesUsingEdge(source.Topology, edge);
        if (adjacentFaces.Count != 2)
            throw Failure("REKALL_MESH_DISSOLVE_SELECTION_INVALID", "The selected dissolve edge must be a two-face manifold edge.");
        var materialAttribute = source.Attributes.FirstOrDefault(attribute =>
            attribute.Domain == RekallAgeGeometryDomain.Face && string.Equals(attribute.Semantic, "material-index", StringComparison.OrdinalIgnoreCase));
        if (materialAttribute is not null && materialAttribute.Values[adjacentFaces[0]].GetInt32() != materialAttribute.Values[adjacentFaces[1]].GetInt32())
            throw Failure("REKALL_MESH_DISSOLVE_MATERIAL_CONFLICT", "Adjacent faces have different material indices; assign a common material before dissolving.");

        var boundaryEdges = adjacentFaces.SelectMany(face => FaceEdges(source.Topology, face)).Where(index => index != edge).ToArray();
        var loop = ExtractSimpleLoops(source.Topology, boundaryEdges, "REKALL_MESH_DISSOLVE_SELECTION_INVALID").SingleOrDefault();
        if (loop is null)
            throw Failure("REKALL_MESH_DISSOLVE_SELECTION_INVALID", "Dissolving the selected edge does not produce one simple polygon boundary.");
        var cornerSources = loop.Select(point => FindCornerAtPoint(source.Topology, adjacentFaces, point)).ToArray();
        var keptFace = adjacentFaces.OrderBy(index => source.Topology.FaceIds[index]).First();
        var removedFace = adjacentFaces.Single(index => index != keptFace);
        var faces = ExistingFaces(source.Topology).Where(face => face.SourceFace is null || !adjacentFaces.Contains(face.SourceFace.Value)).ToList();
        faces.Insert(keptFace, new(source.Topology.FaceIds[keptFace], loop, keptFace, cornerSources));
        var result = Rebuild(source, source.Topology.PointIds, source.Topology.Positions,
            Enumerable.Range(0, source.Topology.PointIds.Count).Select(index => new[] { index }).ToArray(), faces, null);
        var outputFace = source.Topology.FaceIds[keptFace];
        return result with
        {
            Provenance = adjacentFaces.OrderBy(index => source.Topology.FaceIds[index])
                .Select(index => new RekallAgeMeshElementProvenance(RekallAgeGeometryDomain.Face, source.Topology.FaceIds[index], [outputFace])).ToArray(),
            Changes = result.Changes with
            {
                DeletedEdgeIds = [source.Topology.EdgeIds[edge]], DeletedFaceIds = [source.Topology.FaceIds[removedFace]],
                ModifiedFaceIds = [outputFace]
            }
        };
    }

    private static RekallAgeMeshOperationResult BisectPlane(RekallAgeMeshAsset source, RekallAgeMeshOperationRequest request)
    {
        RequireDomain(request, RekallAgeGeometryDomain.Face);
        _ = ResolveIndices(source.Topology.FaceIds, request.ElementIds, "face");
        if (request.ElementIds.Count != source.Topology.FaceIds.Count)
            throw Failure("REKALL_MESH_BISECT_PARTIAL_SELECTION_UNSUPPORTED", "Plane bisection currently requires the complete face set so intersections remain coherent.");
        if (ReadBoolean(request.Parameters, "fill", false))
            throw Failure("REKALL_MESH_BISECT_FILL_UNSUPPORTED", "Plane-bisect cut filling is not implemented; use Fill Holes on the resulting boundary.");
        var clearPositive = ReadBoolean(request.Parameters, "clearPositive", true);
        var clearNegative = ReadBoolean(request.Parameters, "clearNegative", false);
        if (clearPositive == clearNegative)
            throw Failure("REKALL_MESH_BISECT_MODE_UNSUPPORTED", "Exactly one of clearPositive or clearNegative must be enabled in this bounded implementation.");
        var plane = new RekallAgeGeometryVector3(ReadFiniteDouble(request.Parameters, "planeX"), ReadFiniteDouble(request.Parameters, "planeY"), ReadFiniteDouble(request.Parameters, "planeZ"));
        var normal = new RekallAgeGeometryVector3(ReadFiniteDouble(request.Parameters, "normalX", 1), ReadFiniteDouble(request.Parameters, "normalY"), ReadFiniteDouble(request.Parameters, "normalZ"));
        var length = Math.Sqrt(normal.X * normal.X + normal.Y * normal.Y + normal.Z * normal.Z);
        if (!double.IsFinite(length) || length <= 1e-12)
            throw Failure("REKALL_MESH_OPERATION_PARAMETER_INVALID", "Bisect plane normal must be finite and nonzero.");
        normal = new(normal.X / length, normal.Y / length, normal.Z / length);
        var keepSign = clearPositive ? -1d : 1d;
        double Distance(RekallAgeGeometryVector3 point) => ((point.X - plane.X) * normal.X + (point.Y - plane.Y) * normal.Y + (point.Z - plane.Z) * normal.Z) * keepSign;

        var pointIds = source.Topology.PointIds.ToList();
        var positions = source.Topology.Positions.ToList();
        var pointSources = Enumerable.Range(0, pointIds.Count).Select(index => new[] { index }).ToList();
        var intersections = new Dictionary<(int, int), int>();
        var interpolationByPointId = new Dictionary<ulong, (int A, int B, double T)>();
        var nextPointId = NextId(pointIds);
        int Intersection(int a, int b)
        {
            var key = EdgeKey(a, b);
            if (intersections.TryGetValue(key, out var existing)) return existing;
            var da = Distance(positions[a]); var db = Distance(positions[b]);
            var t = da / (da - db);
            var pa = positions[a]; var pb = positions[b];
            var index = positions.Count;
            positions.Add(new(pa.X + (pb.X - pa.X) * t, pa.Y + (pb.Y - pa.Y) * t, pa.Z + (pb.Z - pa.Z) * t));
            var pointId = nextPointId++;
            pointIds.Add(pointId); pointSources.Add([a, b]); intersections[key] = index;
            interpolationByPointId[pointId] = (a, b, t);
            return index;
        }

        var faces = new List<ConstructionFace>();
        for (var face = 0; face < source.Topology.FaceIds.Count; face++)
        {
            var input = FacePoints(source.Topology, face);
            var output = new List<int>();
            for (var index = 0; index < input.Count; index++)
            {
                var current = input[index]; var next = input[(index + 1) % input.Count];
                var currentInside = Distance(positions[current]) >= -1e-9;
                var nextInside = Distance(positions[next]) >= -1e-9;
                if (currentInside) output.Add(current);
                if (currentInside != nextInside) output.Add(Intersection(current, next));
            }
            output = output.Distinct().ToList();
            if (output.Count >= 3)
            {
                var cornerSources = output.Select(point => point < source.Topology.PointIds.Count ? FindCornerAtPoint(source.Topology, [face], point) : null).ToArray();
                var cornerBlends = output.Select(point =>
                {
                    if (!interpolationByPointId.TryGetValue(pointIds[point], out var interpolation)) return null;
                    var a = FindCornerAtPoint(source.Topology, [face], interpolation.A);
                    var b = FindCornerAtPoint(source.Topology, [face], interpolation.B);
                    return a.HasValue && b.HasValue ? new CornerBlend(a.Value, b.Value, interpolation.T) : null;
                }).ToArray();
                faces.Add(new(source.Topology.FaceIds[face], output, face, cornerSources, cornerBlends));
            }
        }
        if (faces.Count == 0) throw Failure("REKALL_MESH_BISECT_EMPTY", "Bisect plane removed the complete mesh.");

        var used = faces.SelectMany(face => face.Points).Distinct().OrderBy(index => pointIds[index]).ToArray();
        var remap = used.Select((old, index) => (old, index)).ToDictionary(item => item.old, item => item.index);
        var compactFaces = faces.Select(face => face with { Points = face.Points.Select(index => remap[index]).ToArray() }).ToArray();
        var result = Rebuild(source, used.Select(index => pointIds[index]).ToArray(), used.Select(index => positions[index]).ToArray(),
            used.Select(index => pointSources[index]).ToArray(), compactFaces, null);
        result = ApplyWeightedPointAttributes(source, result, interpolationByPointId);
        return result with
        {
            Provenance = source.Topology.FaceIds.Select(id => new RekallAgeMeshElementProvenance(RekallAgeGeometryDomain.Face, id,
                result.Mesh.Topology.FaceIds.Contains(id) ? [id] : [])).ToArray()
        };
    }

    private static RekallAgeMeshOperationResult Rebuild(
        RekallAgeMeshAsset source,
        IReadOnlyList<ulong> pointIds,
        IReadOnlyList<RekallAgeGeometryVector3> positions,
        IReadOnlyList<int[]> pointSources,
        IReadOnlyList<ConstructionFace> faces,
        int? newFaceMaterial)
    {
        var oldEdgeByKey = source.Topology.EdgePointIndices.Select((edge, index) => (Key: EdgeKey(edge.A, edge.B), index))
            .ToDictionary(item => item.Key, item => item.index);
        var edgeIds = new List<ulong>(); var edgePoints = new List<RekallAgeMeshEdgePointIndices>(); var edgeSources = new List<int?>();
        var edgeMap = new Dictionary<(int, int), int>(); var nextEdgeId = NextId(source.Topology.EdgeIds);
        var faceIds = new List<ulong>(); var faceOffsets = new List<int> { 0 }; var faceSources = new List<int?>();
        var cornerIds = new List<ulong>(); var cornerPoints = new List<int>(); var cornerEdges = new List<int>(); var cornerSources = new List<int?>(); var cornerBlends = new List<CornerBlend?>();
        var nextFaceId = NextId(source.Topology.FaceIds); var nextCornerId = NextId(source.Topology.CornerIds);
        foreach (var face in faces)
        {
            faceIds.Add(face.Id ?? nextFaceId++); faceSources.Add(face.SourceFace);
            for (var local = 0; local < face.Points.Count; local++)
            {
                var a = face.Points[local]; var b = face.Points[(local + 1) % face.Points.Count]; var key = EdgeKey(a, b);
                if (!edgeMap.TryGetValue(key, out var edgeIndex))
                {
                    edgeIndex = edgeIds.Count; edgeMap[key] = edgeIndex; edgePoints.Add(new(a, b));
                    var sourceKey = pointSources[a].Length == 1 && pointSources[b].Length == 1
                        ? EdgeKey(pointSources[a][0], pointSources[b][0])
                        : ((int A, int B)?)null;
                    if (sourceKey.HasValue && oldEdgeByKey.TryGetValue(sourceKey.Value, out var oldEdge)) { edgeIds.Add(source.Topology.EdgeIds[oldEdge]); edgeSources.Add(oldEdge); }
                    else { edgeIds.Add(nextEdgeId++); edgeSources.Add(null); }
                }
                var sourceCorner = face.CornerSources[local];
                cornerIds.Add(sourceCorner.HasValue ? source.Topology.CornerIds[sourceCorner.Value] : nextCornerId++);
                cornerPoints.Add(a); cornerEdges.Add(edgeIndex); cornerSources.Add(sourceCorner); cornerBlends.Add(face.CornerBlends?[local]);
            }
            faceOffsets.Add(cornerIds.Count);
        }
        var attributes = source.Attributes.Select(attribute => attribute.Domain switch
        {
            RekallAgeGeometryDomain.Point => attribute with { Values = pointSources.Select(indices => Average(attribute, indices)).ToArray() },
            RekallAgeGeometryDomain.Edge => attribute with { Values = edgeSources.Select(index => index.HasValue ? attribute.Values[index.Value] : DefaultValue(attribute)).ToArray() },
            RekallAgeGeometryDomain.Face => attribute with { Values = faceSources.Select(index => index.HasValue ? attribute.Values[index.Value] :
                newFaceMaterial.HasValue && string.Equals(attribute.Semantic, "material-index", StringComparison.OrdinalIgnoreCase)
                    ? JsonSerializer.SerializeToElement(newFaceMaterial.Value) : DefaultValue(attribute)).ToArray() },
            RekallAgeGeometryDomain.Corner => attribute with { Values = cornerSources.Select((index, output) => index.HasValue ? attribute.Values[index.Value]
                : cornerBlends[output] is { } blend ? Interpolate(attribute, blend.A, blend.B, blend.T) : DefaultValue(attribute)).ToArray() },
            _ => attribute
        }).ToArray();
        var topology = new RekallAgeMeshTopology(pointIds, positions, edgeIds, edgePoints, faceIds, faceOffsets, cornerIds, cornerPoints, cornerEdges);
        var validIds = new Dictionary<RekallAgeGeometryDomain, HashSet<ulong>>
        {
            [RekallAgeGeometryDomain.Point] = pointIds.ToHashSet(), [RekallAgeGeometryDomain.Edge] = edgeIds.ToHashSet(),
            [RekallAgeGeometryDomain.Face] = faceIds.ToHashSet(), [RekallAgeGeometryDomain.Corner] = cornerIds.ToHashSet()
        };
        var selections = source.SelectionSets.Select(selection => !validIds.TryGetValue(selection.Domain, out var ids) ? selection : selection with
        {
            ElementIds = selection.ElementIds.Where(ids.Contains).ToArray(),
            ActiveElementId = selection.ActiveElementId.HasValue && ids.Contains(selection.ActiveElementId.Value) ? selection.ActiveElementId : null,
            OrderedHistory = selection.OrderedHistory?.Where(ids.Contains).ToArray()
        }).ToArray();
        var mesh = source with { Revision = checked(source.Revision + 1), Topology = topology, Attributes = attributes, SelectionSets = selections };
        var createdPoints = pointIds.Except(source.Topology.PointIds).Order().ToArray();
        var createdEdges = edgeIds.Except(source.Topology.EdgeIds).Order().ToArray();
        var createdFaces = faceIds.Except(source.Topology.FaceIds).Order().ToArray();
        var createdCorners = cornerIds.Except(source.Topology.CornerIds).Order().ToArray();
        var positionsChanged = pointIds.Count != source.Topology.PointIds.Count
            || !pointIds.SequenceEqual(source.Topology.PointIds)
            || !positions.SequenceEqual(source.Topology.Positions);
        return Result(source, mesh, ChangeSet(RekallAgeMeshChangeKind.Topology | (positionsChanged ? RekallAgeMeshChangeKind.Positions : RekallAgeMeshChangeKind.None) |
            (attributes.Length > 0 ? RekallAgeMeshChangeKind.Attributes : 0) | (selections.Length > 0 ? RekallAgeMeshChangeKind.Selection : 0),
            createdPoints, createdEdges, createdFaces, createdCorners,
            deletedPoints: source.Topology.PointIds.Except(pointIds).Order().ToArray(), deletedEdges: source.Topology.EdgeIds.Except(edgeIds).Order().ToArray(),
            deletedFaces: source.Topology.FaceIds.Except(faceIds).Order().ToArray(), deletedCorners: source.Topology.CornerIds.Except(cornerIds).Order().ToArray(),
            changedAttributes: attributes.Select(attribute => attribute.Name).Order(StringComparer.Ordinal).ToArray(), affectedBounds: Bounds(positions)), []);
    }

    private static List<ConstructionFace> ExistingFaces(RekallAgeMeshTopology topology) =>
        Enumerable.Range(0, topology.FaceIds.Count).Select(face => new ConstructionFace(topology.FaceIds[face], FacePoints(topology, face), face,
            FaceCornerSourceIndices(face, topology).Select(index => (int?)index).ToArray())).ToList();

    private static IReadOnlyList<int> FacePoints(RekallAgeMeshTopology topology, int face) =>
        FaceCornerSourceIndices(face, topology).Select(index => topology.CornerPointIndices[index]).ToArray();

    private static IReadOnlyList<int> FaceEdges(RekallAgeMeshTopology topology, int face) =>
        FaceCornerSourceIndices(face, topology).Select(index => topology.CornerEdgeIndices[index]).ToArray();

    private static IReadOnlyList<int> FacesUsingEdge(RekallAgeMeshTopology topology, int edge) =>
        Enumerable.Range(0, topology.FaceIds.Count).Where(face => FaceEdges(topology, face).Contains(edge)).ToArray();

    private static int[] EdgeUseCounts(RekallAgeMeshTopology topology)
    {
        var result = new int[topology.EdgeIds.Count]; foreach (var edge in topology.CornerEdgeIndices) result[edge]++; return result;
    }

    private static IReadOnlyList<IReadOnlyList<int>> ExtractSimpleLoops(RekallAgeMeshTopology topology, IReadOnlyList<int> edgeIndices, string errorCode)
    {
        var adjacency = new Dictionary<int, List<int>>();
        foreach (var edgeIndex in edgeIndices.Distinct())
        {
            var edge = topology.EdgePointIndices[edgeIndex];
            if (!adjacency.TryGetValue(edge.A, out var first)) adjacency[edge.A] = first = []; first.Add(edge.B);
            if (!adjacency.TryGetValue(edge.B, out var second)) adjacency[edge.B] = second = []; second.Add(edge.A);
        }
        if (adjacency.Count < 3 || adjacency.Any(item => item.Value.Count != 2))
            throw Failure(errorCode, "Selection must form one or more disjoint, simple closed loops.");
        var remaining = adjacency.Keys.ToHashSet(); var loops = new List<IReadOnlyList<int>>();
        while (remaining.Count > 0)
        {
            var start = remaining.OrderBy(index => topology.PointIds[index]).First();
            var loop = new List<int>(); var previous = -1; var current = start;
            do
            {
                loop.Add(current); remaining.Remove(current);
                var next = adjacency[current].Where(candidate => candidate != previous)
                    .OrderBy(index => topology.PointIds[index]).First();
                previous = current; current = next;
                if (loop.Count > adjacency.Count) throw Failure(errorCode, "Selection loop traversal did not close deterministically.");
            } while (current != start);
            loops.Add(loop);
        }
        return loops.OrderBy(loop => topology.PointIds[loop[0]]).ToArray();
    }

    private static IReadOnlyList<int> OrientFillLoop(RekallAgeMeshTopology topology, IReadOnlyList<int> loop)
    {
        var a = loop[0]; var b = loop[1];
        foreach (var face in Enumerable.Range(0, topology.FaceIds.Count))
        {
            var points = FacePoints(topology, face);
            for (var i = 0; i < points.Count; i++)
                if (points[i] == a && points[(i + 1) % points.Count] == b) return loop.Reverse().ToArray();
        }
        return loop.ToArray();
    }

    private static IReadOnlyList<int> AlignLoop(IReadOnlyList<RekallAgeGeometryVector3> positions, IReadOnlyList<int> first, IReadOnlyList<int> second)
    {
        IReadOnlyList<int>? best = null; var bestScore = double.PositiveInfinity; var bestKey = string.Empty;
        foreach (var orientation in new[] { second.ToArray(), second.Reverse().ToArray() })
        for (var offset = 0; offset < orientation.Length; offset++)
        {
            var candidate = Enumerable.Range(0, orientation.Length).Select(i => orientation[(i + offset) % orientation.Length]).ToArray();
            var score = first.Select((point, i) => SquaredDistance(positions[point], positions[candidate[i]])).Sum();
            var key = string.Join(",", candidate);
            if (score < bestScore - 1e-12 || Math.Abs(score - bestScore) <= 1e-12 && string.CompareOrdinal(key, bestKey) < 0)
            { best = candidate; bestScore = score; bestKey = key; }
        }
        return best!;
    }

    private static double SquaredDistance(RekallAgeGeometryVector3 a, RekallAgeGeometryVector3 b)
    { var x = a.X - b.X; var y = a.Y - b.Y; var z = a.Z - b.Z; return x * x + y * y + z * z; }

    private static int? FindCornerAtPoint(RekallAgeMeshTopology topology, IReadOnlyList<int> faces, int point) =>
        faces.SelectMany(face => FaceCornerSourceIndices(face, topology)).Cast<int?>().FirstOrDefault(corner => corner.HasValue && topology.CornerPointIndices[corner.Value] == point);

    private static int ReadMaterialIndex(RekallAgeMeshAsset source, JsonObject parameters)
    {
        var index = parameters.TryGetPropertyValue("materialIndex", out var node) && node is not null ? node.GetValue<int>() : 0;
        if (source.MaterialSlots.Count == 0 && !source.Attributes.Any(attribute =>
                attribute.Domain == RekallAgeGeometryDomain.Face && string.Equals(attribute.Semantic, "material-index", StringComparison.OrdinalIgnoreCase)))
            return 0;
        if (index < 0 || index >= source.MaterialSlots.Count)
            throw Failure("REKALL_MESH_OPERATION_PARAMETER_INVALID", "Material index must reference an existing material slot.");
        return index;
    }

    private static RekallAgeMeshOperationResult ApplyWeightedPointAttributes(
        RekallAgeMeshAsset source,
        RekallAgeMeshOperationResult result,
        IReadOnlyDictionary<ulong, (int A, int B, double T)> interpolationByPointId)
    {
        if (interpolationByPointId.Count == 0) return result;
        var pointIds = result.Mesh.Topology.PointIds;
        var attributes = result.Mesh.Attributes.Select(attribute =>
        {
            if (attribute.Domain != RekallAgeGeometryDomain.Point) return attribute;
            var values = attribute.Values.ToArray();
            for (var index = 0; index < pointIds.Count; index++)
                if (interpolationByPointId.TryGetValue(pointIds[index], out var sourcePair))
                    values[index] = Interpolate(attribute, sourcePair.A, sourcePair.B, sourcePair.T);
            return attribute with { Values = values };
        }).ToArray();
        return result with { Mesh = result.Mesh with { Attributes = attributes } };
    }

    private static JsonElement Interpolate(RekallAgeGeometryAttribute attribute, int a, int b, double t)
    {
        if (attribute.Interpolation is RekallAgeGeometryInterpolation.Constant or RekallAgeGeometryInterpolation.Nearest
            || attribute.ValueType is RekallAgeGeometryValueType.Bool or RekallAgeGeometryValueType.Int4 or RekallAgeGeometryValueType.String
                or RekallAgeGeometryValueType.Quaternion or RekallAgeGeometryValueType.Matrix4x4)
            return attribute.Values[t < 0.5 ? a : b];
        if (attribute.ValueType == RekallAgeGeometryValueType.Int32)
            return JsonSerializer.SerializeToElement((int)Math.Round(attribute.Values[a].GetInt32() * (1 - t) + attribute.Values[b].GetInt32() * t, MidpointRounding.AwayFromZero));
        if (attribute.ValueType == RekallAgeGeometryValueType.Float)
            return JsonSerializer.SerializeToElement(attribute.Values[a].GetDouble() * (1 - t) + attribute.Values[b].GetDouble() * t);
        var count = attribute.ValueType switch
        {
            RekallAgeGeometryValueType.Float2 => 2,
            RekallAgeGeometryValueType.Float3 => 3,
            RekallAgeGeometryValueType.Float4 or RekallAgeGeometryValueType.ColorLinear => 4,
            _ => 0
        };
        if (count == 0) return attribute.Values[t < 0.5 ? a : b];
        var first = attribute.Values[a].EnumerateArray().Select(value => value.GetDouble()).ToArray();
        var second = attribute.Values[b].EnumerateArray().Select(value => value.GetDouble()).ToArray();
        var values = Enumerable.Range(0, count).Select(index => first[index] * (1 - t) + second[index] * t).ToArray();
        if (attribute.Interpolation == RekallAgeGeometryInterpolation.NormalizedLinear)
        {
            var length = Math.Sqrt(values.Sum(value => value * value));
            if (length > 1e-12) for (var index = 0; index < values.Length; index++) values[index] /= length;
        }
        return JsonSerializer.SerializeToElement(values);
    }
}
