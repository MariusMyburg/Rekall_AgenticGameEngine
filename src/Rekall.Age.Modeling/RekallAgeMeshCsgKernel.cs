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
}
