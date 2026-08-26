using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed partial class RekallAgeMeshOperationExecutor
{
    private sealed record BevelFace(
        int[] Points,
        int SourceFace,
        int?[] CornerSources,
        int? MaterialIndex = null,
        bool Generated = false);

    private static RekallAgeMeshOperationResult BevelEdges(RekallAgeMeshAsset source, RekallAgeMeshOperationRequest request)
    {
        RequireDomain(request, RekallAgeGeometryDomain.Edge);
        var topology = source.Topology;
        var requestedEdgeIndices = ResolveIndices(topology.EdgeIds, request.ElementIds, "edge");
        var sourceEdgeByPoints = topology.EdgePointIndices
            .Select((edge, index) => (Key: edge.A < edge.B ? (edge.A, edge.B) : (edge.B, edge.A), Index: index))
            .ToDictionary(item => item.Key, item => item.Index);

        var width = ReadFiniteDouble(request.Parameters, "width");
        var segments = ReadBoundedInt(request.Parameters, "segments", 1, 1, 64);
        var profile = ReadFiniteDouble(request.Parameters, "profile", 0.5);
        var clampOverlap = ReadBoolean(request.Parameters, "clampOverlap", true);
        var hardenNormals = ReadBoolean(request.Parameters, "hardenNormals", false);
        if (width <= 0 || profile is < 0.01 or > 0.99)
            throw Failure("REKALL_MESH_OPERATION_PARAMETER_INVALID", "Bevel width must be positive and profile must be between 0.01 and 0.99.");
        var weightAttributeName = ReadOptionalString(request.Parameters, "weightAttribute");
        var weightAttribute = weightAttributeName is null
            ? null
            : source.Attributes.SingleOrDefault(attribute =>
                attribute.Name.Equals(weightAttributeName, StringComparison.Ordinal)
                && attribute.Domain == RekallAgeGeometryDomain.Edge
                && attribute.ValueType == RekallAgeGeometryValueType.Float)
                ?? throw Failure(
                    "REKALL_MESH_BEVEL_WEIGHT_ATTRIBUTE_INVALID",
                    $"Bevel weight attribute '{weightAttributeName}' must be an edge-domain Float attribute.");
        var edgeWeights = requestedEdgeIndices.ToDictionary(
            index => index,
            index => weightAttribute is null ? 1d : Math.Clamp(weightAttribute.Values[index].GetDouble(), 0, 1));
        var selectedEdgeIndices = edgeWeights
            .Where(item => item.Value > 1e-9)
            .Select(item => item.Key)
            .ToHashSet();
        if (selectedEdgeIndices.Count == 0)
            throw Failure("REKALL_MESH_BEVEL_SELECTION_EMPTY", "Bevel selection has no edges with a positive effective weight.");
        var materialIndex = ReadBoundedInt(request.Parameters, "materialIndex", -1, -1, 65_535);
        if (materialIndex >= source.MaterialSlots.Count)
            throw Failure("REKALL_MESH_BEVEL_MATERIAL_INVALID", "Bevel materialIndex must reference an existing material slot or be -1 to inherit source faces.");
        int? generatedMaterialIndex = materialIndex >= 0 ? materialIndex : null;
        var affectedPoints = selectedEdgeIndices
            .SelectMany(index => new[] { topology.EdgePointIndices[index].A, topology.EdgePointIndices[index].B })
            .ToHashSet();

        var positions = new List<RekallAgeGeometryVector3>();
        var sourcePointByOutput = new List<int>();
        var insetByCorner = new Dictionary<int, int>();
        var originalOutputBySourcePoint = new Dictionary<int, int>();
        var firstFaceByPoint = new Dictionary<int, int>();
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
                firstFaceByPoint.TryAdd(sourcePoint, faceIndex);
                var point = topology.Positions[sourcePoint];
                var previousCorner = local == 0 ? end - 1 : corner - 1;
                var currentEdge = topology.CornerEdgeIndices[corner];
                var previousEdge = topology.CornerEdgeIndices[previousCorner];
                var cornerWeight = Math.Max(
                    selectedEdgeIndices.Contains(currentEdge) ? edgeWeights[currentEdge] : 0,
                    selectedEdgeIndices.Contains(previousEdge) ? edgeWeights[previousEdge] : 0);
                if (cornerWeight <= 1e-9)
                {
                    inset[local] = OriginalOutput(sourcePoint);
                    insetByCorner[corner] = inset[local];
                    insetCornerSources[local] = corner;
                    continue;
                }
                var towardCenter = Subtract(center, point);
                var available = Math.Sqrt(Dot(towardCenter, towardCenter));
                if (available <= 1e-9)
                    throw Failure("REKALL_MESH_BEVEL_FACE_DEGENERATE", "Bevel cannot inset a face corner at its centroid.");
                var requestedWidth = width * cornerWeight;
                var safeWidth = available * 0.45;
                if (!clampOverlap && requestedWidth > safeWidth)
                    throw Failure("REKALL_MESH_BEVEL_OVERLAP", "Bevel width overlaps an inset face; reduce width or enable clampOverlap.");
                var insetWidth = Math.Min(requestedWidth, safeWidth);
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
            {
                if (selectedEdgeIndices.Contains(edgeIndex))
                    throw Failure("REKALL_MESH_BEVEL_NON_MANIFOLD", "Selected bevel edges must each have exactly two incident faces.");
                continue;
            }
            var edge = topology.EdgePointIndices[edgeIndex];
            var first = uses[0];
            var second = uses[1];
            var firstNext = NextCorner(topology, first.Corner, first.Face);
            var secondNext = NextCorner(topology, second.Corner, second.Face);
            var firstA = topology.CornerPointIndices[first.Corner] == edge.A ? first.Corner : firstNext;
            var firstB = firstA == first.Corner ? firstNext : first.Corner;
            var secondA = topology.CornerPointIndices[second.Corner] == edge.A ? second.Corner : secondNext;
            var secondB = secondA == second.Corner ? secondNext : second.Corner;
            var firstTraversesAToB = firstA == first.Corner;
            if (selectedEdgeIndices.Contains(edgeIndex))
            {
                var chainA = BuildProfileChain(edge.A, insetByCorner[firstA], insetByCorner[secondA]);
                var chainB = BuildProfileChain(edge.B, insetByCorner[firstB], insetByCorner[secondB]);
                for (var segment = 0; segment < segments; segment++)
                {
                    var points = firstTraversesAToB
                        ? new[] { chainB[segment], chainA[segment], chainA[segment + 1], chainB[segment + 1] }
                        : new[] { chainA[segment], chainB[segment], chainB[segment + 1], chainA[segment + 1] };
                    var profileCornerSources = firstTraversesAToB
                        ? new int?[] { firstB, firstA, secondA, secondB }
                        : [firstA, firstB, secondB, secondA];
                    faces.Add(new(points, first.Face, profileCornerSources, generatedMaterialIndex, true));
                }
            }
            else
            {
                var transitionPoints = firstTraversesAToB
                    ? new[] { insetByCorner[firstB], insetByCorner[firstA], insetByCorner[secondA], insetByCorner[secondB] }
                    : [insetByCorner[firstA], insetByCorner[firstB], insetByCorner[secondB], insetByCorner[secondA]];
                var transitionSources = firstTraversesAToB
                    ? new int?[] { firstB, firstA, secondA, secondB }
                    : [firstA, firstB, secondB, secondA];
                AddTransitionFace(
                    transitionPoints,
                    first.Face,
                    transitionSources);
            }
        }

        var capBoundaryUses = new Dictionary<int, Dictionary<(int A, int B), (int Count, int A, int B)>>();
        foreach (var face in faces)
        {
            for (var index = 0; index < face.Points.Length; index++)
            {
                var a = face.Points[index];
                var b = face.Points[(index + 1) % face.Points.Length];
                var sourcePoint = sourcePointByOutput[a];
                if (sourcePoint != sourcePointByOutput[b] || !affectedPoints.Contains(sourcePoint))
                    continue;
                if (!capBoundaryUses.TryGetValue(sourcePoint, out var uses))
                    capBoundaryUses[sourcePoint] = uses = [];
                var key = a < b ? (a, b) : (b, a);
                uses[key] = uses.TryGetValue(key, out var existing)
                    ? (existing.Count + 1, existing.A, existing.B)
                    : (1, a, b);
            }
        }

        foreach (var pointIndex in affectedPoints.Order())
        {
            var ordered = BuildCapBoundary(pointIndex);
            if (ordered.Length < 3) continue;
            var capFace = firstFaceByPoint[pointIndex];
            if (segments == 1)
            {
                faces.Add(new(
                    ordered,
                    capFace,
                    ordered.Select(index => (int?)FindCorner(sourcePointByOutput[index], capFace)).ToArray(),
                    generatedMaterialIndex,
                    true));
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
                    var capTriangle = new[] { capCenter, ordered[index], ordered[next] };
                    if (HasArea(capTriangle))
                        faces.Add(new(
                            capTriangle,
                            capFace,
                            [FindCorner(pointIndex, capFace), FindCorner(sourcePointByOutput[ordered[index]], capFace), FindCorner(sourcePointByOutput[ordered[next]], capFace)],
                            generatedMaterialIndex,
                            true));
                }
            }
        }

        int[] BuildCapBoundary(int sourcePoint)
        {
            var directedUses = capBoundaryUses.GetValueOrDefault(sourcePoint)?.Values
                .Where(use => use.Count == 1)
                .Select(use => (A: use.A, B: use.B))
                .ToArray() ?? [];
            if (directedUses.Length < 3)
                return [];

            // Every existing boundary A->B must be met by the cap as B->A.
            var next = new Dictionary<int, int>();
            foreach (var edge in directedUses)
                if (!next.TryAdd(edge.B, edge.A))
                    return [];
            var start = next.Keys.Min();
            var ordered = new List<int> { start };
            var current = start;
            while (next.TryGetValue(current, out var following) && following != start)
            {
                if (ordered.Contains(following))
                    return [];
                ordered.Add(following);
                current = following;
            }
            return next.TryGetValue(current, out var close) && close == start && ordered.Count == directedUses.Length
                ? ordered.ToArray()
                : [];
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
            RekallAgeGeometryDomain.Face => attribute with
            {
                Values = faces.Select(face =>
                    face.MaterialIndex.HasValue && IsMaterialIndex(attribute)
                        ? JsonSerializer.SerializeToElement(face.MaterialIndex.Value)
                        : attribute.Values[face.SourceFace]).ToArray()
            },
            RekallAgeGeometryDomain.Corner => attribute with { Values = cornerSources.Select(index => index.HasValue ? attribute.Values[index.Value] : DefaultValue(attribute)).ToArray() },
            _ => attribute
        }).ToList();
        if (generatedMaterialIndex.HasValue && !attributes.Any(IsMaterialIndex))
        {
            attributes.Add(new RekallAgeGeometryAttribute(
                "material.index",
                RekallAgeGeometryDomain.Face,
                RekallAgeGeometryValueType.Int32,
                faces.Select(face => JsonSerializer.SerializeToElement(
                    face.Generated ? generatedMaterialIndex.Value : 0)).ToArray(),
                "material-index",
                RekallAgeGeometryInterpolation.Nearest,
                JsonSerializer.SerializeToElement(0)));
        }
        if (hardenNormals)
        {
            var namedSmooth = source.Attributes.FirstOrDefault(attribute =>
                attribute.Name.Equals("normal.smooth", StringComparison.Ordinal));
            if (namedSmooth is not null
                && (namedSmooth.Domain != RekallAgeGeometryDomain.Face
                    || namedSmooth.ValueType != RekallAgeGeometryValueType.Bool))
                throw Failure(
                    "REKALL_MESH_OPERATION_ATTRIBUTE_CONFLICT",
                    "Attribute 'normal.smooth' exists with an incompatible domain or type.");
            var existingSmooth = namedSmooth;
            var smoothValues = faces.Select(face => JsonSerializer.SerializeToElement(
                face.Generated
                || (existingSmooth is not null && existingSmooth.Values[face.SourceFace].GetBoolean()))).ToArray();
            var authoredSmooth = existingSmooth is null
                ? new RekallAgeGeometryAttribute(
                    "normal.smooth",
                    RekallAgeGeometryDomain.Face,
                    RekallAgeGeometryValueType.Bool,
                    smoothValues,
                    "normal-smooth",
                    RekallAgeGeometryInterpolation.Nearest,
                    JsonSerializer.SerializeToElement(true))
                : existingSmooth with { Values = smoothValues };
            var smoothIndex = attributes.FindIndex(attribute =>
                attribute.Name.Equals("normal.smooth", StringComparison.Ordinal));
            if (smoothIndex >= 0)
                attributes[smoothIndex] = authoredSmooth;
            else
                attributes.Add(authoredSmooth);
        }
        var mesh = source with
        {
            Revision = checked(source.Revision + 1),
            Topology = new(pointIds, positions, edgeIds, edges, faceIds, faceOffsets, cornerIds, cornerPoints, cornerEdges),
            Attributes = attributes,
            SelectionSets = []
        };

        var pointOutputs = GroupOutputIds(topology.PointIds.Count, pointIds.Count, output => sourcePointByOutput[output], pointIds);
        var edgeOutputs = GroupOutputIds(topology.EdgeIds.Count, edgeIds.Count, output => outputEdgeSources[output], edgeIds);
        var faceOutputs = GroupOutputIds(topology.FaceIds.Count, faceIds.Count, output => faces[output].SourceFace, faceIds);
        var cornerOutputs = GroupOutputIds(topology.CornerIds.Count, cornerIds.Count, output => cornerSources[output], cornerIds);
        var provenance = new List<RekallAgeMeshElementProvenance>();
        provenance.AddRange(topology.PointIds.Select((id, index) => new RekallAgeMeshElementProvenance(
            RekallAgeGeometryDomain.Point, id, pointOutputs[index])));
        provenance.AddRange(topology.EdgeIds.Select((id, index) => new RekallAgeMeshElementProvenance(
            RekallAgeGeometryDomain.Edge, id, edgeOutputs[index])));
        provenance.AddRange(topology.FaceIds.Select((id, index) => new RekallAgeMeshElementProvenance(
            RekallAgeGeometryDomain.Face, id, faceOutputs[index])));
        provenance.AddRange(topology.CornerIds.Select((id, index) => new RekallAgeMeshElementProvenance(
            RekallAgeGeometryDomain.Corner, id, cornerOutputs[index])));
        return Result(source, mesh, ChangeSet(
            RekallAgeMeshChangeKind.Topology | RekallAgeMeshChangeKind.Positions
            | (attributes.Count > 0 ? RekallAgeMeshChangeKind.Attributes : RekallAgeMeshChangeKind.None)
            | (source.SelectionSets.Count > 0 ? RekallAgeMeshChangeKind.Selection : RekallAgeMeshChangeKind.None),
            createdPoints: pointIds, createdEdges: edgeIds, createdFaces: faceIds, createdCorners: cornerIds,
            deletedPoints: topology.PointIds, deletedEdges: topology.EdgeIds, deletedFaces: topology.FaceIds, deletedCorners: topology.CornerIds,
            changedAttributes: attributes.Select(item => item.Name).Order(StringComparer.Ordinal).ToArray(),
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

        int OriginalOutput(int sourcePoint)
        {
            if (originalOutputBySourcePoint.TryGetValue(sourcePoint, out var output))
                return output;
            output = positions.Count;
            originalOutputBySourcePoint[sourcePoint] = output;
            positions.Add(topology.Positions[sourcePoint]);
            sourcePointByOutput.Add(sourcePoint);
            return output;
        }

        void AddTransitionFace(int[] candidatePoints, int sourceFace, int?[] cornerSourceCandidates)
        {
            var points = new List<int>(candidatePoints.Length);
            var cornerSources = new List<int?>(candidatePoints.Length);
            for (var index = 0; index < candidatePoints.Length; index++)
            {
                if (points.Count > 0 && points[^1] == candidatePoints[index])
                    continue;
                points.Add(candidatePoints[index]);
                cornerSources.Add(cornerSourceCandidates[index]);
            }
            if (points.Count > 1 && points[0] == points[^1])
            {
                points.RemoveAt(points.Count - 1);
                cornerSources.RemoveAt(cornerSources.Count - 1);
            }
            if (points.Distinct().Count() >= 3 && HasArea(points))
                faces.Add(new(points.ToArray(), sourceFace, cornerSources.ToArray(), generatedMaterialIndex, true));
        }

        bool HasArea(IReadOnlyList<int> pointIndices)
        {
            if (pointIndices.Count < 3)
                return false;
            var origin = positions[pointIndices[0]];
            var doubledArea = 0d;
            for (var index = 1; index + 1 < pointIndices.Count; index++)
            {
                var first = Subtract(positions[pointIndices[index]], origin);
                var second = Subtract(positions[pointIndices[index + 1]], origin);
                var cross = Cross(first, second);
                doubledArea += Math.Sqrt(Dot(cross, cross));
            }
            return doubledArea > 1e-10;
        }

        int? FindSourceEdge(int firstPoint, int secondPoint)
        {
            if (firstPoint == secondPoint) return null;
            var key = firstPoint < secondPoint ? (firstPoint, secondPoint) : (secondPoint, firstPoint);
            return sourceEdgeByPoints.TryGetValue(key, out var index) ? index : null;
        }

        static IReadOnlyList<ulong>[] GroupOutputIds(
            int sourceCount,
            int outputCount,
            Func<int, int?> sourceIndexAt,
            IReadOnlyList<ulong> outputIds)
        {
            var result = Enumerable.Range(0, sourceCount).Select(_ => new List<ulong>()).ToArray();
            for (var output = 0; output < outputCount; output++)
            {
                var sourceIndex = sourceIndexAt(output);
                if (sourceIndex.HasValue)
                    result[sourceIndex.Value].Add(outputIds[output]);
            }
            return result;
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

    private static string? ReadOptionalString(JsonObject parameters, string name)
    {
        if (!parameters.TryGetPropertyValue(name, out var node) || node is null)
            return null;
        if (node is not JsonValue value || !value.TryGetValue<string>(out var text) || text.Length > 128)
            throw Failure("REKALL_MESH_OPERATION_PARAMETER_INVALID", $"Parameter '{name}' must be a string of at most 128 characters.");
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static bool IsMaterialIndex(RekallAgeGeometryAttribute attribute) =>
        attribute.Domain == RekallAgeGeometryDomain.Face
        && attribute.ValueType == RekallAgeGeometryValueType.Int32
        && (attribute.Name.Equals("material.index", StringComparison.Ordinal)
            || string.Equals(attribute.Semantic, "material-index", StringComparison.Ordinal));

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
