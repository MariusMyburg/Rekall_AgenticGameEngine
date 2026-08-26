using Csg = CSG.Sharp;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

/// <summary>Which polygon-set a CSG polygon originated from, and enough of its source triangle to
/// interpolate attributes back onto CSG output later (barycentric position/corner ids).</summary>
internal sealed record RekallAgeMeshCsgFaceSource(
    string Operand,
    ulong FaceId,
    IReadOnlyList<ulong> SourceCornerIds,
    IReadOnlyList<RekallAgeGeometryVector3> TrianglePositions);

/// <summary>
/// The mesh-to-CSG-polygon conversion and small vector-math helpers shared by every consumer of
/// the <c>CSG.Sharp</c> kernel (currently the <c>rekall.modeling.boolean</c> node, and mesh
/// fracture). Extracted from <c>RekallAgeModelingBoolean.cs</c> so both reuse the same tested
/// conversion instead of each maintaining its own; CSG-polygons-back-to-mesh-topology
/// (welding/attribute interpolation) stays with each consumer because the boolean node's version
/// is tightly coupled to its two-operand attribute-blend plan, which a single-operand consumer
/// like fracture does not need.
/// </summary>
internal static class RekallAgeMeshCsgKernel
{
    public static Csg.CSG ToCsg(RekallAgeMeshAsset source, string operand)
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
                new RekallAgeMeshCsgFaceSource(
                    operand,
                    compiled.Triangles[triangle].SourceFaceId,
                    compiled.Triangles[triangle].SourceCornerIds.ToArray(),
                    [p0, p1, p2])));
        }
        return Csg.CSG.FromPolygons(polygons.ToArray());
    }

    public static Csg.Vector ToCsgVector(RekallAgeGeometryVector3 value) => new(value.X, value.Y, value.Z);
    public static RekallAgeGeometryVector3 Subtract(RekallAgeGeometryVector3 a, RekallAgeGeometryVector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static RekallAgeGeometryVector3 Cross(RekallAgeGeometryVector3 a, RekallAgeGeometryVector3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    public static double Dot(RekallAgeGeometryVector3 a, RekallAgeGeometryVector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static RekallAgeGeometryVector3 Unit(RekallAgeGeometryVector3 value)
    {
        var length = Math.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);
        return new(value.X / length, value.Y / length, value.Z / length);
    }

    /// <summary>
    /// Converts CSG output polygons into a valid, closed-manifold mesh topology with no
    /// attributes: welds coincident vertices by tolerance and normalizes boundary edges exactly
    /// like the boolean node's <c>FromCsg</c>, but without that method's two-operand attribute-
    /// interpolation plan, which a single-operand consumer (mesh fracture) has no use for. A
    /// caller that needs attributes on the result can copy them on afterward from the known
    /// single source mesh.
    /// </summary>
    public static RekallAgeMeshAsset FromPolygonsToMesh(Csg.CSG csg, string assetId, string name)
    {
        var polygons = csg.ToPolygons();
        if (polygons.Sum(polygon => polygon.Vertices.Length) > 8_000_000)
            throw new InvalidOperationException("CSG result exceeds the hard corner ceiling.");
        var all = polygons.SelectMany(polygon => polygon.Vertices).Select(vertex => vertex.Pos).ToArray();
        var span = all.Length == 0 ? 1 : Math.Max(
            all.Max(value => value.x) - all.Min(value => value.x),
            Math.Max(all.Max(value => value.y) - all.Min(value => value.y), all.Max(value => value.z) - all.Min(value => value.z)));
        var tolerance = Math.Max(span * 1e-8, 1e-9);
        var pointMap = new Dictionary<(long X, long Y, long Z), int>();
        var positions = new List<RekallAgeGeometryVector3>();
        var faces = new List<int[]>();
        foreach (var polygon in polygons)
        {
            var face = polygon.Vertices.Select(vertex => Point(vertex.Pos)).ToList();
            for (var index = face.Count - 1; index > 0; index--)
                if (face[index] == face[index - 1]) face.RemoveAt(index);
            if (face.Count > 1 && face[0] == face[^1]) face.RemoveAt(face.Count - 1);
            if (face.Count >= 3) faces.Add(face.ToArray());
        }

        var boundarySegmentCount = faces.Sum(face => face.Length);
        if (checked((long)positions.Count * boundarySegmentCount) > 50_000_000)
            throw new InvalidOperationException("CSG boundary normalization exceeds the hard deterministic work ceiling.");
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
        return RekallAgeMeshAsset.Create(assetId, name, topology);

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
}
