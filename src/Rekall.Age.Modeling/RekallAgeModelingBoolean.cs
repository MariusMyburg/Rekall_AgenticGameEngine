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
            return new(FromCsg(graph, node, result));
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
        if (mesh.MaterialSlots.Count != 0 || mesh.Attributes.Any(attribute => attribute.Name is not ("boolean.sourceOperand" or "boolean.sourceFaceId")))
            throw new EvaluationException("REKALL_MODELING_BOOLEAN_ATTRIBUTES_UNSUPPORTED", $"Boolean input {label} contains attributes or materials that cannot yet be interpolated without data loss; apply them after the Boolean node.", nodeId);
    }

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
                new BooleanFaceSource(operand, compiled.Triangles[triangle].SourceFaceId)));
        }
        return Csg.CSG.FromPolygons(polygons.ToArray());
    }

    private static RekallAgeMeshAsset FromCsg(
        RekallAgeModelingGraphAsset graph,
        RekallAgeModelingGraphNode node,
        Csg.CSG csg)
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
        var attributes = new[]
        {
            new RekallAgeGeometryAttribute(
                "boolean.sourceOperand", RekallAgeGeometryDomain.Face, RekallAgeGeometryValueType.String,
                sources.Select(source => JsonSerializer.SerializeToElement(source.Operand)).ToArray(),
                "boolean-source-operand", RekallAgeGeometryInterpolation.Nearest),
            new RekallAgeGeometryAttribute(
                "boolean.sourceFaceId", RekallAgeGeometryDomain.Face, RekallAgeGeometryValueType.String,
                sources.Select(source => JsonSerializer.SerializeToElement(source.FaceId.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray(),
                "boolean-source-face-id", RekallAgeGeometryInterpolation.Nearest)
        };
        var mesh = RekallAgeMeshAsset.Create($"{graph.AssetId}.{node.NodeId}", node.NodeId, topology, attributes) with { Revision = graph.Revision };
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

    private sealed record BooleanFaceSource(string Operand, ulong FaceId);
}
