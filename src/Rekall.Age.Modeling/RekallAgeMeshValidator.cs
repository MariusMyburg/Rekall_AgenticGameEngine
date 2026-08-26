using System.Text.Json;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed class RekallAgeMeshValidator
{
    public RekallAgeMeshValidationReport Validate(RekallAgeMeshAsset mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var diagnostics = new List<RekallAgeMeshDiagnostic>();
        var topology = mesh.Topology;

        ValidateDocument(mesh, diagnostics);
        ValidateIds("POINT", topology.PointIds, diagnostics);
        ValidateIds("EDGE", topology.EdgeIds, diagnostics);
        ValidateIds("FACE", topology.FaceIds, diagnostics);
        ValidateIds("CORNER", topology.CornerIds, diagnostics);

        if (topology.Positions.Count != topology.PointIds.Count)
        {
            Error(diagnostics, "REKALL_MESH_POSITION_LENGTH_INVALID", "Point IDs and positions must have the same length.");
        }

        for (var i = 0; i < Math.Min(topology.PointIds.Count, topology.Positions.Count); i++)
        {
            var position = topology.Positions[i];
            if (!IsFinite(position.X) || !IsFinite(position.Y) || !IsFinite(position.Z))
            {
                Error(diagnostics, "REKALL_MESH_POSITION_NONFINITE", "Mesh positions must contain only finite values.", topology.PointIds[i]);
            }
        }

        var edgeUseCounts = new int[topology.EdgeIds.Count];
        ValidateEdges(topology, diagnostics);
        ValidateFacesAndCorners(topology, edgeUseCounts, diagnostics);
        ValidateAttributes(mesh, diagnostics);
        ValidateMaterialSlots(mesh, diagnostics);
        ValidateSelections(mesh, diagnostics);

        var loose = edgeUseCounts.Count(value => value == 0);
        var boundary = edgeUseCounts.Count(value => value == 1);
        var nonManifold = edgeUseCounts.Count(value => value > 2);
        for (var i = 0; i < edgeUseCounts.Length; i++)
        {
            if (edgeUseCounts[i] > 2)
            {
                Warning(
                    diagnostics,
                    "REKALL_MESH_EDGE_NON_MANIFOLD",
                    $"Edge is used by {edgeUseCounts[i]} faces.",
                    topology.EdgeIds[i]);
            }
        }

        var summary = new RekallAgeMeshValidationSummary(
            topology.PointIds.Count,
            topology.EdgeIds.Count,
            topology.FaceIds.Count,
            topology.CornerIds.Count,
            loose,
            boundary,
            nonManifold,
            CalculateBounds(topology.Positions));
        return new RekallAgeMeshValidationReport(
            diagnostics.All(item => item.Severity != RekallAgeMeshDiagnosticSeverity.Error),
            summary,
            diagnostics);
    }

    private static void ValidateDocument(
        RekallAgeMeshAsset mesh,
        ICollection<RekallAgeMeshDiagnostic> diagnostics)
    {
        if (mesh.SchemaVersion != RekallAgeMeshAsset.CurrentSchemaVersion)
        {
            Error(diagnostics, "REKALL_MESH_SCHEMA_UNSUPPORTED", "Mesh schema version is unsupported.");
        }

        if (string.IsNullOrWhiteSpace(mesh.AssetId) || string.IsNullOrWhiteSpace(mesh.Name) || mesh.Revision < 1)
        {
            Error(diagnostics, "REKALL_MESH_DOCUMENT_INVALID", "Mesh asset ID, name, and positive revision are required.");
        }
    }

    private static void ValidateIds(
        string domain,
        IReadOnlyList<ulong> ids,
        ICollection<RekallAgeMeshDiagnostic> diagnostics)
    {
        var seen = new HashSet<ulong>();
        foreach (var id in ids)
        {
            if (id == 0)
            {
                Error(diagnostics, $"REKALL_MESH_{domain}_ID_INVALID", $"{domain} IDs must be nonzero.", id);
            }
            else if (!seen.Add(id))
            {
                Error(diagnostics, $"REKALL_MESH_{domain}_ID_DUPLICATE", $"{domain} IDs must be unique.", id);
            }
        }
    }

    private static void ValidateEdges(
        RekallAgeMeshTopology topology,
        ICollection<RekallAgeMeshDiagnostic> diagnostics)
    {
        if (topology.EdgePointIndices.Count != topology.EdgeIds.Count)
        {
            Error(diagnostics, "REKALL_MESH_EDGE_LENGTH_INVALID", "Edge IDs and endpoint pairs must have the same length.");
        }

        var uniqueEdges = new Dictionary<(int A, int B), ulong>();
        for (var i = 0; i < Math.Min(topology.EdgeIds.Count, topology.EdgePointIndices.Count); i++)
        {
            var edge = topology.EdgePointIndices[i];
            var edgeId = topology.EdgeIds[i];
            if (!IsIndex(edge.A, topology.PointIds.Count) || !IsIndex(edge.B, topology.PointIds.Count))
            {
                Error(diagnostics, "REKALL_MESH_EDGE_POINT_REFERENCE_INVALID", "Edge endpoints must reference existing points.", edgeId);
                continue;
            }

            if (edge.A == edge.B)
            {
                Error(diagnostics, "REKALL_MESH_EDGE_SELF", "An edge cannot reference the same point twice.", edgeId);
            }

            var key = edge.A < edge.B ? (edge.A, edge.B) : (edge.B, edge.A);
            if (uniqueEdges.TryGetValue(key, out var existingId))
            {
                Error(diagnostics, "REKALL_MESH_EDGE_DUPLICATE", "Duplicate unordered edges are not allowed.", existingId, edgeId);
            }
            else
            {
                uniqueEdges.Add(key, edgeId);
            }
        }
    }

    private static void ValidateFacesAndCorners(
        RekallAgeMeshTopology topology,
        int[] edgeUseCounts,
        ICollection<RekallAgeMeshDiagnostic> diagnostics)
    {
        if (topology.CornerPointIndices.Count != topology.CornerIds.Count
            || topology.CornerEdgeIndices.Count != topology.CornerIds.Count)
        {
            Error(diagnostics, "REKALL_MESH_CORNER_LENGTH_INVALID", "Corner IDs, point references, and edge references must have the same length.");
        }

        if (topology.FaceOffsets.Count != topology.FaceIds.Count + 1
            || topology.FaceOffsets.Count == 0
            || topology.FaceOffsets[0] != 0
            || topology.FaceOffsets[^1] != topology.CornerIds.Count)
        {
            Error(diagnostics, "REKALL_MESH_FACE_OFFSETS_INVALID", "Face offsets must start at zero, end at the corner count, and contain one range per face.");
            return;
        }

        var canonicalFaces = new Dictionary<string, ulong>(StringComparer.Ordinal);
        for (var faceIndex = 0; faceIndex < topology.FaceIds.Count; faceIndex++)
        {
            var start = topology.FaceOffsets[faceIndex];
            var end = topology.FaceOffsets[faceIndex + 1];
            var faceId = topology.FaceIds[faceIndex];
            if (start < 0 || end < start || end > topology.CornerIds.Count)
            {
                Error(diagnostics, "REKALL_MESH_FACE_RANGE_INVALID", "Face corner ranges must be ordered and in bounds.", faceId);
                continue;
            }

            if (end - start < 3)
            {
                Error(diagnostics, "REKALL_MESH_FACE_TOO_SMALL", "Faces require at least three corners.", faceId);
            }

            var facePoints = new HashSet<int>();
            var faceEdges = new HashSet<int>();
            var orderedPoints = new List<int>(Math.Max(0, end - start));
            for (var cornerIndex = start; cornerIndex < end; cornerIndex++)
            {
                if (cornerIndex >= topology.CornerPointIndices.Count || cornerIndex >= topology.CornerEdgeIndices.Count)
                {
                    continue;
                }

                var pointIndex = topology.CornerPointIndices[cornerIndex];
                var edgeIndex = topology.CornerEdgeIndices[cornerIndex];
                var cornerId = topology.CornerIds[cornerIndex];
                if (!IsIndex(pointIndex, topology.PointIds.Count))
                {
                    Error(diagnostics, "REKALL_MESH_CORNER_POINT_REFERENCE_INVALID", "A corner references a missing point.", cornerId);
                }
                else if (!facePoints.Add(pointIndex))
                {
                    Error(diagnostics, "REKALL_MESH_FACE_POINT_REPEATED", "A face cannot repeat a point.", faceId, cornerId);
                }
                orderedPoints.Add(pointIndex);

                if (!IsIndex(edgeIndex, topology.EdgeIds.Count) || edgeIndex >= topology.EdgePointIndices.Count)
                {
                    Error(diagnostics, "REKALL_MESH_CORNER_EDGE_REFERENCE_INVALID", "A corner references a missing edge.", cornerId);
                    continue;
                }

                edgeUseCounts[edgeIndex]++;
                if (!faceEdges.Add(edgeIndex))
                {
                    Error(diagnostics, "REKALL_MESH_FACE_EDGE_REPEATED", "A face cannot repeat an edge.", faceId, topology.EdgeIds[edgeIndex]);
                }

                var nextCorner = cornerIndex + 1 == end ? start : cornerIndex + 1;
                if (nextCorner >= topology.CornerPointIndices.Count || !IsIndex(pointIndex, topology.PointIds.Count))
                {
                    continue;
                }

                var nextPointIndex = topology.CornerPointIndices[nextCorner];
                var edge = topology.EdgePointIndices[edgeIndex];
                if (!Connects(edge, pointIndex, nextPointIndex))
                {
                    Error(
                        diagnostics,
                        "REKALL_MESH_CORNER_EDGE_ENDPOINT_MISMATCH",
                        "A face corner edge must connect this corner point to the next corner point.",
                        cornerId,
                        topology.EdgeIds[edgeIndex]);
                }
            }

            if (orderedPoints.Count >= 3 && orderedPoints.All(point => IsIndex(point, topology.Positions.Count)))
            {
                var canonical = CanonicalFace(orderedPoints);
                if (canonicalFaces.TryGetValue(canonical, out var existingFaceId))
                {
                    Error(diagnostics, "REKALL_MESH_FACE_DUPLICATE", "Duplicate faces are not allowed, regardless of winding.", existingFaceId, faceId);
                }
                else
                {
                    canonicalFaces.Add(canonical, faceId);
                }

                if (IsZeroArea(orderedPoints, topology.Positions))
                {
                    Error(diagnostics, "REKALL_MESH_FACE_ZERO_AREA", "A face must enclose nonzero area.", faceId);
                }
            }
        }
    }

    private static void ValidateAttributes(
        RekallAgeMeshAsset mesh,
        ICollection<RekallAgeMeshDiagnostic> diagnostics)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attribute in mesh.Attributes)
        {
            if (string.IsNullOrWhiteSpace(attribute.Name) || !names.Add(attribute.Name))
            {
                Error(diagnostics, "REKALL_MESH_ATTRIBUTE_NAME_INVALID", "Attribute names must be nonempty and unique.");
            }

            var expected = attribute.Domain switch
            {
                RekallAgeGeometryDomain.Point => mesh.Topology.PointIds.Count,
                RekallAgeGeometryDomain.Edge => mesh.Topology.EdgeIds.Count,
                RekallAgeGeometryDomain.Face => mesh.Topology.FaceIds.Count,
                RekallAgeGeometryDomain.Corner => mesh.Topology.CornerIds.Count,
                RekallAgeGeometryDomain.Instance => 0,
                _ => 0
            };
            if (attribute.Values.Count != expected)
            {
                Error(diagnostics, "REKALL_MESH_ATTRIBUTE_LENGTH_INVALID", $"Attribute '{attribute.Name}' requires {expected} values for its domain.");
            }

            foreach (var value in attribute.Values)
            {
                if (!IsValueCompatible(value, attribute.ValueType))
                {
                    Error(diagnostics, "REKALL_MESH_ATTRIBUTE_VALUE_INVALID", $"Attribute '{attribute.Name}' contains a value incompatible with {attribute.ValueType}.");
                    break;
                }
            }

            if (string.Equals(attribute.Semantic, "material-index", StringComparison.Ordinal)
                && attribute.Domain == RekallAgeGeometryDomain.Face
                && attribute.ValueType == RekallAgeGeometryValueType.Int32)
            {
                foreach (var value in attribute.Values)
                {
                    if (value.TryGetInt32(out var materialIndex)
                        && (materialIndex < 0 || materialIndex >= mesh.MaterialSlots.Count))
                    {
                        Error(diagnostics, "REKALL_MESH_MATERIAL_INDEX_INVALID", $"Attribute '{attribute.Name}' references a missing material slot.");
                        break;
                    }
                }
            }
        }
    }

    private static void ValidateMaterialSlots(
        RekallAgeMeshAsset mesh,
        ICollection<RekallAgeMeshDiagnostic> diagnostics)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slot in mesh.MaterialSlots)
        {
            if (string.IsNullOrWhiteSpace(slot.Name) || !names.Add(slot.Name))
            {
                Error(diagnostics, "REKALL_MESH_MATERIAL_SLOT_INVALID", "Material slot names must be nonempty and unique.");
            }
        }
    }

    private static void ValidateSelections(
        RekallAgeMeshAsset mesh,
        ICollection<RekallAgeMeshDiagnostic> diagnostics)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var selection in mesh.SelectionSets)
        {
            if (string.IsNullOrWhiteSpace(selection.Name) || !names.Add(selection.Name))
            {
                Error(diagnostics, "REKALL_MESH_SELECTION_NAME_INVALID", "Selection names must be nonempty and unique.");
            }

            var domainIds = selection.Domain switch
            {
                RekallAgeGeometryDomain.Point => mesh.Topology.PointIds,
                RekallAgeGeometryDomain.Edge => mesh.Topology.EdgeIds,
                RekallAgeGeometryDomain.Face => mesh.Topology.FaceIds,
                RekallAgeGeometryDomain.Corner => mesh.Topology.CornerIds,
                _ => []
            };
            var validIds = domainIds.ToHashSet();
            foreach (var id in selection.ElementIds
                         .Concat(selection.OrderedHistory ?? [])
                         .Concat(selection.ActiveElementId.HasValue ? [selection.ActiveElementId.Value] : []))
            {
                if (!validIds.Contains(id))
                {
                    Error(diagnostics, "REKALL_MESH_SELECTION_ELEMENT_INVALID", $"Selection '{selection.Name}' references an element outside its domain.", id);
                }
            }
        }
    }

    private static bool IsValueCompatible(JsonElement value, RekallAgeGeometryValueType type)
    {
        return type switch
        {
            RekallAgeGeometryValueType.Bool => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            RekallAgeGeometryValueType.Int32 => value.TryGetInt32(out _),
            RekallAgeGeometryValueType.Int4 => IsIntegerArray(value, 4),
            RekallAgeGeometryValueType.Float => IsFiniteNumber(value),
            RekallAgeGeometryValueType.Float2 => IsFiniteNumberArray(value, 2),
            RekallAgeGeometryValueType.Float3 => IsFiniteNumberArray(value, 3),
            RekallAgeGeometryValueType.Float4 or RekallAgeGeometryValueType.ColorLinear or RekallAgeGeometryValueType.Quaternion => IsFiniteNumberArray(value, 4),
            RekallAgeGeometryValueType.Matrix4x4 => IsFiniteNumberArray(value, 16),
            RekallAgeGeometryValueType.String => value.ValueKind == JsonValueKind.String,
            _ => false
        };
    }

    private static bool IsIntegerArray(JsonElement value, int count) =>
        value.ValueKind == JsonValueKind.Array
        && value.GetArrayLength() == count
        && value.EnumerateArray().All(item => item.TryGetInt32(out _));

    private static bool IsFiniteNumber(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && IsFinite(number);
    }

    private static bool IsFiniteNumberArray(JsonElement value, int length)
    {
        return value.ValueKind == JsonValueKind.Array
               && value.GetArrayLength() == length
               && value.EnumerateArray().All(IsFiniteNumber);
    }

    private static bool Connects(RekallAgeMeshEdgePointIndices edge, int first, int second)
    {
        return edge.A == first && edge.B == second || edge.A == second && edge.B == first;
    }

    private static bool IsIndex(int index, int count) => index >= 0 && index < count;

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static RekallAgeMeshBounds CalculateBounds(IReadOnlyList<RekallAgeGeometryVector3> positions)
    {
        var finite = positions.Where(item => IsFinite(item.X) && IsFinite(item.Y) && IsFinite(item.Z)).ToArray();
        if (finite.Length == 0)
        {
            return new RekallAgeMeshBounds(new(0, 0, 0), new(0, 0, 0));
        }

        return new RekallAgeMeshBounds(
            new(
                finite.Min(item => item.X),
                finite.Min(item => item.Y),
                finite.Min(item => item.Z)),
            new(
                finite.Max(item => item.X),
                finite.Max(item => item.Y),
                finite.Max(item => item.Z)));
    }

    private static string CanonicalFace(IReadOnlyList<int> points)
    {
        var forward = SmallestRotation(points);
        var reversed = SmallestRotation(points.Reverse().ToArray());
        return string.CompareOrdinal(forward, reversed) <= 0 ? forward : reversed;
    }

    private static string SmallestRotation(IReadOnlyList<int> points)
    {
        string? best = null;
        for (var start = 0; start < points.Count; start++)
        {
            var candidate = string.Join(",", Enumerable.Range(0, points.Count).Select(offset => points[(start + offset) % points.Count]));
            if (best is null || string.CompareOrdinal(candidate, best) < 0)
            {
                best = candidate;
            }
        }

        return best ?? string.Empty;
    }

    private static bool IsZeroArea(
        IReadOnlyList<int> pointIndices,
        IReadOnlyList<RekallAgeGeometryVector3> positions)
    {
        double x = 0;
        double y = 0;
        double z = 0;
        for (var i = 0; i < pointIndices.Count; i++)
        {
            var current = positions[pointIndices[i]];
            var next = positions[pointIndices[(i + 1) % pointIndices.Count]];
            x += (current.Y - next.Y) * (current.Z + next.Z);
            y += (current.Z - next.Z) * (current.X + next.X);
            z += (current.X - next.X) * (current.Y + next.Y);
        }

        return x * x + y * y + z * z <= 1e-20;
    }

    private static void Error(
        ICollection<RekallAgeMeshDiagnostic> diagnostics,
        string code,
        string message,
        params ulong[] elementIds) =>
        diagnostics.Add(new RekallAgeMeshDiagnostic(code, RekallAgeMeshDiagnosticSeverity.Error, message, elementIds));

    private static void Warning(
        ICollection<RekallAgeMeshDiagnostic> diagnostics,
        string code,
        string message,
        params ulong[] elementIds) =>
        diagnostics.Add(new RekallAgeMeshDiagnostic(code, RekallAgeMeshDiagnosticSeverity.Warning, message, elementIds));
}
