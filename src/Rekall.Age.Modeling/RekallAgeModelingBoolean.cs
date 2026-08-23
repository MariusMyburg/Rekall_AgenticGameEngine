using System.Text.Json;
using Csg = CSG.Sharp;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed partial class RekallAgeModelingGraphEvaluator
{
    private static NodeValue BooleanGeometry(
        RekallAgeModelingGraphAsset graph,
        RekallAgeModelingGraphNode node,
        NodeValue a,
        NodeValue b)
    {
        var meshA = a.Mesh ?? throw new EvaluationException("REKALL_MODELING_EVALUATION_INPUT_TYPE_INVALID", "Boolean input A must be geometry.", node.NodeId);
        var meshB = b.Mesh ?? throw new EvaluationException("REKALL_MODELING_EVALUATION_INPUT_TYPE_INVALID", "Boolean input B must be geometry.", node.NodeId);
        RequireBooleanInput(meshA, "A", node.NodeId);
        RequireBooleanInput(meshB, "B", node.NodeId);
        var attributePlan = PrepareBooleanAttributes(meshA, meshB, node.NodeId);
        var operation = ReadString(node, "operation", "union");
        if (operation is not ("union" or "intersect" or "difference"))
            throw new EvaluationException("REKALL_MODELING_EVALUATION_PARAMETER_INVALID", "Boolean operation must be union, intersect, or difference.", node.NodeId);

        try
        {
            var csgA = ToCsg(meshA, "a");
            var csgB = ToCsg(meshB, "b");
            var result = operation switch
            {
                "union" => csgA.Union(csgB),
                "intersect" => csgA.Intersect(csgB),
                "difference" => csgA.Subtract(csgB),
                _ => throw new InvalidOperationException()
            };
            return new(FromCsg(graph, node, result, meshA, meshB, attributePlan));
        }
        catch (EvaluationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or ArithmeticException)
        {
            throw new EvaluationException("REKALL_MODELING_BOOLEAN_KERNEL_FAILED", $"Boolean kernel failed safely: {exception.Message}", node.NodeId);
        }
    }

    private static void RequireBooleanInput(RekallAgeMeshAsset mesh, string label, string nodeId)
    {
        var validation = new RekallAgeMeshValidator().Validate(mesh);
        if (!validation.IsValid || validation.Summary.BoundaryEdgeCount != 0 || validation.Summary.NonManifoldEdgeCount != 0 || validation.Summary.FaceCount == 0)
            throw new EvaluationException("REKALL_MODELING_BOOLEAN_INPUT_NOT_CLOSED_MANIFOLD", $"Boolean input {label} must be a non-empty, closed manifold surface.", nodeId);
        if (mesh.Attributes.Any(attribute => !IsBooleanProvenance(attribute) && attribute.Domain is not (RekallAgeGeometryDomain.Face or RekallAgeGeometryDomain.Corner)))
            throw new EvaluationException("REKALL_MODELING_BOOLEAN_ATTRIBUTES_UNSUPPORTED", $"Boolean input {label} contains point or edge attributes that cannot yet be interpolated without data loss; use face/corner attributes or apply them after the Boolean node.", nodeId);
    }

    private static BooleanAttributePlan PrepareBooleanAttributes(RekallAgeMeshAsset a, RekallAgeMeshAsset b, string nodeId)
    {
        var inputs = new[] { a, b };
        var schemas = inputs.SelectMany(mesh => mesh.Attributes.Where(attribute => !IsBooleanProvenance(attribute)))
            .GroupBy(attribute => attribute.Name, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(attribute => attribute.Name, StringComparer.Ordinal)
            .ToArray();
        foreach (var schema in schemas)
        {
            var matches = inputs.Select(mesh => mesh.Attributes.FirstOrDefault(attribute => attribute.Name == schema.Name)).ToArray();
            if (matches.Any(attribute => attribute is not null &&
                (attribute.Domain != schema.Domain || attribute.ValueType != schema.ValueType || attribute.Semantic != schema.Semantic || attribute.Interpolation != schema.Interpolation)))
                throw new EvaluationException("REKALL_MODELING_BOOLEAN_ATTRIBUTE_SCHEMA_CONFLICT", $"Boolean attribute '{schema.Name}' has incompatible operand schemas.", nodeId);
            if (schema.Semantic?.Equals("material-index", StringComparison.OrdinalIgnoreCase) == true &&
                (matches.Any(attribute => attribute is null) || inputs.Any(mesh => mesh.MaterialSlots.Count == 0)))
                throw new EvaluationException("REKALL_MODELING_BOOLEAN_MATERIAL_SCHEMA_MISMATCH", "Both Boolean operands must carry a material-index face attribute and material slots when either operand is material-assigned.", nodeId);
        }
        var hasMaterialSchema = schemas.Any(schema => schema.Semantic?.Equals("material-index", StringComparison.OrdinalIgnoreCase) == true);
        if (!hasMaterialSchema && inputs.Any(mesh => mesh.MaterialSlots.Count > 0))
            throw new EvaluationException("REKALL_MODELING_BOOLEAN_MATERIAL_SCHEMA_MISMATCH", "Boolean material slots require a compatible material-index face attribute on both operands.", nodeId);
        var (slots, maps) = MergeMaterialSlots(inputs);
        return new(schemas, slots, maps);
    }

    private static bool IsBooleanProvenance(RekallAgeGeometryAttribute attribute) =>
        attribute.Name is "boolean.sourceOperand" or "boolean.sourceFaceId";

    private static Csg.CSG ToCsg(RekallAgeMeshAsset source, string operand)
    {
        var compiled = new RekallAgeMeshCompiler().Compile(source);
        var polygons = new List<Csg.Polygon>(compiled.Triangles.Count);
        for (var triangle = 0; triangle < compiled.Triangles.Count; triangle++)
        {
            var indices = compiled.Indices.Skip(triangle * 3).Take(3).Select(index => checked((int)index)).ToArray();
            var p0 = compiled.Vertices[indices[0]].Position;
            var p1 = compiled.Vertices[indices[1]].Position;
            var p2 = compiled.Vertices[indices[2]].Position;
            var normal = Unit(Cross(Subtract(p1, p0), Subtract(p2, p0)));
            polygons.Add(new(
                indices.Select(index => new Csg.Vertex(ToCsgVector(compiled.Vertices[index].Position), ToCsgVector(normal))).ToArray(),
                new BooleanFaceSource(
                    operand,
                    compiled.Triangles[triangle].SourceFaceId,
                    compiled.Triangles[triangle].SourceCornerIds.ToArray(),
                    [p0, p1, p2])));
        }
        return Csg.CSG.FromPolygons(polygons.ToArray());
    }

    private static RekallAgeMeshAsset FromCsg(
        RekallAgeModelingGraphAsset graph,
        RekallAgeModelingGraphNode node,
        Csg.CSG csg,
        RekallAgeMeshAsset meshA,
        RekallAgeMeshAsset meshB,
        BooleanAttributePlan attributePlan)
    {
        var polygons = csg.ToPolygons();
        if (polygons.Sum(polygon => polygon.Vertices.Length) > 8_000_000)
            throw new EvaluationException("REKALL_MODELING_EVALUATION_ELEMENT_BUDGET_EXCEEDED", "Boolean result exceeds the hard corner ceiling.", node.NodeId);
        var all = polygons.SelectMany(polygon => polygon.Vertices).Select(vertex => vertex.Pos).ToArray();
        var span = all.Length == 0 ? 1 : Math.Max(
            all.Max(value => value.x) - all.Min(value => value.x),
            Math.Max(all.Max(value => value.y) - all.Min(value => value.y), all.Max(value => value.z) - all.Min(value => value.z)));
        var tolerance = Math.Max(span * 1e-8, 1e-9);
        var pointMap = new Dictionary<(long X, long Y, long Z), int>();
        var positions = new List<RekallAgeGeometryVector3>();
        var faces = new List<int[]>();
        var sources = new List<BooleanFaceSource>();
        foreach (var polygon in polygons)
        {
            var face = polygon.Vertices.Select(vertex => Point(vertex.Pos)).ToList();
            for (var index = face.Count - 1; index > 0; index--)
                if (face[index] == face[index - 1]) face.RemoveAt(index);
            if (face.Count > 1 && face[0] == face[^1]) face.RemoveAt(face.Count - 1);
            if (face.Count >= 3)
            {
                faces.Add(face.ToArray());
                sources.Add(polygon.Shared as BooleanFaceSource
                    ?? throw new EvaluationException("REKALL_MODELING_BOOLEAN_PROVENANCE_MISSING", "Boolean kernel output lost source-face provenance.", node.NodeId));
            }
        }

        var boundarySegmentCount = faces.Sum(face => face.Length);
        if (checked((long)positions.Count * boundarySegmentCount) > 50_000_000)
            throw new EvaluationException("REKALL_MODELING_EVALUATION_ELEMENT_BUDGET_EXCEEDED", "Boolean boundary normalization exceeds the hard deterministic work ceiling.", node.NodeId);
        faces = faces.Select(NormalizeBoundary).ToList();

        var edgeMap = new Dictionary<(int A, int B), int>();
        var edges = new List<RekallAgeMeshEdgePointIndices>();
        var cornerPoints = new List<int>();
        var cornerEdges = new List<int>();
        var faceOffsets = new List<int> { 0 };
        foreach (var face in faces)
        {
            for (var corner = 0; corner < face.Length; corner++)
            {
                var a = face[corner]; var b = face[(corner + 1) % face.Length];
                var key = a < b ? (a, b) : (b, a);
                if (!edgeMap.TryGetValue(key, out var edge))
                {
                    edge = edges.Count; edgeMap[key] = edge; edges.Add(new(a, b));
                }
                cornerPoints.Add(a); cornerEdges.Add(edge);
            }
            faceOffsets.Add(cornerPoints.Count);
        }
        var topology = new RekallAgeMeshTopology(
            Enumerable.Range(1, positions.Count).Select(value => (ulong)value).ToArray(), positions,
            Enumerable.Range(1, edges.Count).Select(value => (ulong)(10_000 + value)).ToArray(), edges,
            Enumerable.Range(1, faces.Count).Select(value => (ulong)(20_000 + value)).ToArray(), faceOffsets,
            Enumerable.Range(1, cornerPoints.Count).Select(value => (ulong)(30_000 + value)).ToArray(), cornerPoints, cornerEdges);
        var faceIndices = new[]
        {
            meshA.Topology.FaceIds.Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index),
            meshB.Topology.FaceIds.Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index)
        };
        var cornerIndices = new[]
        {
            meshA.Topology.CornerIds.Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index),
            meshB.Topology.CornerIds.Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index)
        };
        var inputs = new[] { meshA, meshB };
        var attributes = new List<RekallAgeGeometryAttribute>();
        foreach (var schema in attributePlan.Schemas)
        {
            var values = new List<JsonElement>(schema.Domain == RekallAgeGeometryDomain.Face ? sources.Count : cornerPoints.Count);
            for (var faceIndex = 0; faceIndex < sources.Count; faceIndex++)
            {
                var source = sources[faceIndex];
                var inputIndex = source.Operand == "a" ? 0 : 1;
                var sourceAttribute = inputs[inputIndex].Attributes.FirstOrDefault(attribute => attribute.Name == schema.Name);
                if (schema.Domain == RekallAgeGeometryDomain.Face)
                {
                    var value = sourceAttribute is null ? DefaultValue(schema) : sourceAttribute.Values[faceIndices[inputIndex][source.FaceId]];
                    if (schema.Semantic?.Equals("material-index", StringComparison.OrdinalIgnoreCase) == true &&
                        value.TryGetInt32(out var materialIndex) && materialIndex >= 0 && materialIndex < attributePlan.SlotMaps[inputIndex].Length)
                        value = JsonSerializer.SerializeToElement(attributePlan.SlotMaps[inputIndex][materialIndex]);
                    values.Add(value.Clone());
                }
                else
                {
                    foreach (var pointIndex in faces[faceIndex])
                    {
                        if (sourceAttribute is null) values.Add(DefaultValue(schema));
                        else values.Add(InterpolateCorner(
                            schema,
                            sourceAttribute,
                            source,
                            positions[pointIndex],
                            cornerIndices[inputIndex]));
                    }
                }
            }
            attributes.Add(schema with { Values = values });
        }
        attributes.AddRange([
            new RekallAgeGeometryAttribute(
                "boolean.sourceOperand", RekallAgeGeometryDomain.Face, RekallAgeGeometryValueType.String,
                sources.Select(source => JsonSerializer.SerializeToElement(source.Operand)).ToArray(),
                "boolean-source-operand", RekallAgeGeometryInterpolation.Nearest),
            new RekallAgeGeometryAttribute(
                "boolean.sourceFaceId", RekallAgeGeometryDomain.Face, RekallAgeGeometryValueType.String,
                sources.Select(source => JsonSerializer.SerializeToElement(source.FaceId.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray(),
                "boolean-source-face-id", RekallAgeGeometryInterpolation.Nearest)
        ]);
        var mesh = RekallAgeMeshAsset.Create($"{graph.AssetId}.{node.NodeId}", node.NodeId, topology, attributes, attributePlan.MaterialSlots) with { Revision = graph.Revision };
        var validation = new RekallAgeMeshValidator().Validate(mesh);
        if (!validation.IsValid || validation.Summary.BoundaryEdgeCount != 0 || validation.Summary.NonManifoldEdgeCount != 0)
            throw new EvaluationException(
                "REKALL_MODELING_BOOLEAN_OUTPUT_INVALID",
                $"Boolean kernel output did not satisfy AGE's strict closed-manifold topology contract (boundary={validation.Summary.BoundaryEdgeCount}, nonManifold={validation.Summary.NonManifoldEdgeCount}, diagnostics={string.Join(',', validation.Diagnostics.Select(item => item.Code).Distinct())}).",
                node.NodeId);
        return mesh;

        int Point(Csg.Vector value)
        {
            var key = (
                checked((long)Math.Round(value.x / tolerance)),
                checked((long)Math.Round(value.y / tolerance)),
                checked((long)Math.Round(value.z / tolerance)));
            if (pointMap.TryGetValue(key, out var index)) return index;
            index = positions.Count; pointMap[key] = index; positions.Add(new(value.x, value.y, value.z)); return index;
        }

        int[] NormalizeBoundary(int[] face)
        {
            var normalized = new List<int>(face.Length);
            for (var corner = 0; corner < face.Length; corner++)
            {
                var a = face[corner]; var b = face[(corner + 1) % face.Length];
                normalized.Add(a);
                var start = positions[a]; var end = positions[b];
                var dx = end.X - start.X; var dy = end.Y - start.Y; var dz = end.Z - start.Z;
                var lengthSquared = dx * dx + dy * dy + dz * dz;
                var intermediates = new List<(double T, int Index)>();
                for (var candidate = 0; candidate < positions.Count; candidate++)
                {
                    if (candidate == a || candidate == b) continue;
                    var value = positions[candidate];
                    var t = ((value.X - start.X) * dx + (value.Y - start.Y) * dy + (value.Z - start.Z) * dz) / lengthSquared;
                    if (t <= 1e-10 || t >= 1 - 1e-10) continue;
                    var ex = value.X - (start.X + t * dx);
                    var ey = value.Y - (start.Y + t * dy);
                    var ez = value.Z - (start.Z + t * dz);
                    if (ex * ex + ey * ey + ez * ez <= tolerance * tolerance)
                        intermediates.Add((t, candidate));
                }
                normalized.AddRange(intermediates.OrderBy(item => item.T).ThenBy(item => item.Index).Select(item => item.Index));
            }
            return normalized.ToArray();
        }
    }

    private static Csg.Vector ToCsgVector(RekallAgeGeometryVector3 value) => new(value.X, value.Y, value.Z);
    private static RekallAgeGeometryVector3 Subtract(RekallAgeGeometryVector3 a, RekallAgeGeometryVector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    private static RekallAgeGeometryVector3 Cross(RekallAgeGeometryVector3 a, RekallAgeGeometryVector3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    private static RekallAgeGeometryVector3 Unit(RekallAgeGeometryVector3 value)
    {
        var length = Math.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);
        return new(value.X / length, value.Y / length, value.Z / length);
    }

    private static JsonElement InterpolateCorner(
        RekallAgeGeometryAttribute schema,
        RekallAgeGeometryAttribute sourceAttribute,
        BooleanFaceSource source,
        RekallAgeGeometryVector3 position,
        IReadOnlyDictionary<ulong, int> cornerIndices)
    {
        var weights = Barycentric(position, source.TrianglePositions[0], source.TrianglePositions[1], source.TrianglePositions[2]);
        var sourceValues = source.SourceCornerIds.Select(id => sourceAttribute.Values[cornerIndices[id]]).ToArray();
        var nearest = weights[1] > weights[0] ? 1 : 0;
        if (weights[2] > weights[nearest]) nearest = 2;
        if (schema.Interpolation == RekallAgeGeometryInterpolation.Nearest || schema.ValueType is
            RekallAgeGeometryValueType.Bool or RekallAgeGeometryValueType.Int32 or RekallAgeGeometryValueType.String)
            return sourceValues[nearest].Clone();
        if (schema.ValueType == RekallAgeGeometryValueType.Float)
            return JsonSerializer.SerializeToElement(sourceValues.Select((value, index) => value.GetDouble() * weights[index]).Sum());
        var componentCount = schema.ValueType switch
        {
            RekallAgeGeometryValueType.Float2 => 2,
            RekallAgeGeometryValueType.Float3 => 3,
            RekallAgeGeometryValueType.Float4 or RekallAgeGeometryValueType.ColorLinear or RekallAgeGeometryValueType.Quaternion => 4,
            RekallAgeGeometryValueType.Matrix4x4 => 16,
            _ => throw new EvaluationException("REKALL_MODELING_BOOLEAN_ATTRIBUTE_TYPE_UNSUPPORTED", $"Boolean corner interpolation does not support {schema.ValueType} values.")
        };
        var vectors = sourceValues.Select(value => value.EnumerateArray().Select(component => component.GetDouble()).ToArray()).ToArray();
        var result = Enumerable.Range(0, componentCount)
            .Select(component => Enumerable.Range(0, 3).Sum(index => vectors[index][component] * weights[index]))
            .ToArray();
        if (schema.Interpolation == RekallAgeGeometryInterpolation.NormalizedLinear)
        {
            var length = Math.Sqrt(result.Sum(component => component * component));
            if (length > 1e-15)
                for (var component = 0; component < result.Length; component++) result[component] /= length;
        }
        return JsonSerializer.SerializeToElement(result);
    }

    private static double[] Barycentric(
        RekallAgeGeometryVector3 point,
        RekallAgeGeometryVector3 a,
        RekallAgeGeometryVector3 b,
        RekallAgeGeometryVector3 c)
    {
        var v0 = Subtract(b, a); var v1 = Subtract(c, a); var v2 = Subtract(point, a);
        var d00 = Dot(v0, v0); var d01 = Dot(v0, v1); var d11 = Dot(v1, v1);
        var d20 = Dot(v2, v0); var d21 = Dot(v2, v1);
        var denominator = d00 * d11 - d01 * d01;
        if (Math.Abs(denominator) <= 1e-20)
            throw new EvaluationException("REKALL_MODELING_BOOLEAN_PROVENANCE_DEGENERATE", "Boolean source triangle is degenerate during corner interpolation.");
        var v = (d11 * d20 - d01 * d21) / denominator;
        var w = (d00 * d21 - d01 * d20) / denominator;
        return [1 - v - w, v, w];
    }

    private static double Dot(RekallAgeGeometryVector3 a, RekallAgeGeometryVector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private sealed record BooleanFaceSource(
        string Operand,
        ulong FaceId,
        IReadOnlyList<ulong> SourceCornerIds,
        IReadOnlyList<RekallAgeGeometryVector3> TrianglePositions);
    private sealed record BooleanAttributePlan(
        IReadOnlyList<RekallAgeGeometryAttribute> Schemas,
        IReadOnlyList<RekallAgeMaterialSlot> MaterialSlots,
        IReadOnlyList<int[]> SlotMaps);
}
