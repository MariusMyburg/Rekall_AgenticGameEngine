using System.Text.Json;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class MeshCompilerTests
{
    [Fact]
    public void LegacyAdapterProducesEditableSharedEdgeTopologyAndCompilesAttributes()
    {
        var vertices = new[]
        {
            new RekallAgeLegacyGeometryVertex(new(0, 0, 0), new(0, 0, 1), new(0, 0), new(1, 0, 0, 1)),
            new RekallAgeLegacyGeometryVertex(new(1, 0, 0), new(0, 0, 1), new(1, 0), new(0, 1, 0, 1)),
            new RekallAgeLegacyGeometryVertex(new(1, 1, 0), new(0, 0, 1), new(1, 1), new(0, 0, 1, 1)),
            new RekallAgeLegacyGeometryVertex(new(0, 1, 0), new(0, 0, 1), new(0, 1), new(1, 1, 1, 1))
        };

        var editable = new RekallAgeLegacyGeometryMeshAdapter().Convert(
            "legacy-quad",
            "Legacy Quad",
            vertices,
            [0, 1, 2, 0, 2, 3]);
        var compiled = new RekallAgeMeshCompiler().Compile(editable);

        Assert.Equal(5, editable.Topology.EdgeIds.Count);
        Assert.Equal(2, editable.Topology.FaceIds.Count);
        Assert.True(new RekallAgeMeshValidator().Validate(editable).IsValid);
        Assert.Equal(2, compiled.Triangles.Count);
        Assert.True(compiled.HasVertexColors);
        Assert.Equal(new RekallAgeGeometryVector2(1, 1), compiled.Vertices[2].Uv);
        Assert.Equal(new RekallAgeGeometryVector4(0, 0, 1, 1), compiled.Vertices[2].Color);
    }

    [Fact]
    public void CompileTriangulatesConcaveNgonWithFacePickingProvenance()
    {
        var mesh = Polygon(
            [new(0, 0, 0), new(2, 0, 0), new(2, 2, 0), new(1, 1, 0), new(0, 2, 0)],
            faceId: 41);

        var compiled = new RekallAgeMeshCompiler().Compile(mesh);

        Assert.Equal(3, compiled.Triangles.Count);
        Assert.False(compiled.HasVertexColors);
        Assert.Equal(9, compiled.Indices.Count);
        Assert.All(compiled.Triangles, triangle => Assert.Equal(41UL, triangle.SourceFaceId));
        Assert.All(compiled.Triangles, triangle => Assert.Equal(3, triangle.SourceCornerIds.Count));
        Assert.All(compiled.Indices, index => Assert.True(index < compiled.Vertices.Count));
    }

    [Fact]
    public void CompileSplitsSharedPointsByCornerUvWithoutLosingStableIds()
    {
        var topology = new RekallAgeMeshTopology(
            PointIds: [1, 2, 3, 4],
            Positions: [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0)],
            EdgeIds: [11, 12, 13, 14, 15],
            EdgePointIndices: [new(0, 1), new(1, 2), new(2, 0), new(2, 3), new(3, 0)],
            FaceIds: [21, 22],
            FaceOffsets: [0, 3, 6],
            CornerIds: [31, 32, 33, 34, 35, 36],
            CornerPointIndices: [0, 1, 2, 0, 2, 3],
            CornerEdgeIndices: [0, 1, 2, 2, 3, 4]);
        var mesh = RekallAgeMeshAsset.Create(
            "quad",
            "Quad",
            topology,
            [Attribute("uv", RekallAgeGeometryDomain.Corner, RekallAgeGeometryValueType.Float2,
                [new double[] { 0, 0 }, new double[] { 1, 0 }, new double[] { 1, 1 },
                    new double[] { 0.5, 0 }, new double[] { 0.5, 1 }, new double[] { 0, 1 }], "texcoord-0")]);

        var compiled = new RekallAgeMeshCompiler().Compile(mesh);

        Assert.Equal(6, compiled.Vertices.Count);
        var sharedPointVertices = compiled.Vertices.Where(vertex => vertex.SourcePointId == 1).ToArray();
        Assert.Equal(2, sharedPointVertices.Length);
        Assert.Equal([0d, 0.5d], sharedPointVertices.Select(vertex => vertex.Uv.X).Order().ToArray());
        Assert.Equal([31UL, 32, 33], compiled.Triangles[0].SourceCornerIds);
        Assert.Equal([34UL, 35, 36], compiled.Triangles[1].SourceCornerIds);
    }

    [Fact]
    public void CompilePreservesCornerNormalsAndBuildsFiniteTangents()
    {
        var mesh = Polygon(
            [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
            faceId: 21,
            attributes:
            [
                Attribute("normal", RekallAgeGeometryDomain.Corner, RekallAgeGeometryValueType.Float3,
                    [new double[] { 0, 0, 1 }, new double[] { 0, 0, 1 }, new double[] { 0, 0, 1 }], "normal"),
                Attribute("uv", RekallAgeGeometryDomain.Corner, RekallAgeGeometryValueType.Float2,
                    [new double[] { 0, 0 }, new double[] { 1, 0 }, new double[] { 0, 1 }], "texcoord-0")
            ]);

        var compiled = new RekallAgeMeshCompiler().Compile(mesh);

        Assert.All(compiled.Vertices, vertex =>
        {
            Assert.Equal(new RekallAgeGeometryVector3(0, 0, 1), vertex.Normal);
            Assert.True(double.IsFinite(vertex.Tangent.X));
            Assert.True(double.IsFinite(vertex.Tangent.Y));
            Assert.True(double.IsFinite(vertex.Tangent.Z));
            Assert.True(Math.Abs(vertex.Tangent.X) > 0.9);
        });
    }

    [Fact]
    public void CompileBuildsMaterialSurfacesWithSourceFaceMembership()
    {
        var topology = new RekallAgeMeshTopology(
            PointIds: [1, 2, 3, 4],
            Positions: [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0)],
            EdgeIds: [11, 12, 13, 14, 15],
            EdgePointIndices: [new(0, 1), new(1, 2), new(2, 0), new(2, 3), new(3, 0)],
            FaceIds: [21, 22],
            FaceOffsets: [0, 3, 6],
            CornerIds: [31, 32, 33, 34, 35, 36],
            CornerPointIndices: [0, 1, 2, 0, 2, 3],
            CornerEdgeIndices: [0, 1, 2, 2, 3, 4]);
        var mesh = RekallAgeMeshAsset.Create(
            "materials",
            "Materials",
            topology,
            [Attribute("material.index", RekallAgeGeometryDomain.Face, RekallAgeGeometryValueType.Int32,
                [0, 1], "material-index")],
            [new("stone", "mat.stone"), new("glass", "mat.glass")]);

        var compiled = new RekallAgeMeshCompiler().Compile(mesh);

        Assert.Collection(
            compiled.Surfaces,
            first =>
            {
                Assert.Equal(0, first.MaterialSlotIndex);
                Assert.Equal(3, first.IndexCount);
                Assert.Equal([21UL], first.SourceFaceIds);
            },
            second =>
            {
                Assert.Equal(1, second.MaterialSlotIndex);
                Assert.Equal(3, second.IndexCount);
                Assert.Equal([22UL], second.SourceFaceIds);
            });
    }

    [Fact]
    public void CompiledIndicesAreUInt32AndCanAddressBeyondUInt16()
    {
        const int triangleCount = 21_846;
        var points = new RekallAgeGeometryVector3[triangleCount * 3];
        var pointIds = new ulong[points.Length];
        var edgeIds = new ulong[points.Length];
        var edges = new RekallAgeMeshEdgePointIndices[points.Length];
        var faceIds = new ulong[triangleCount];
        var offsets = new int[triangleCount + 1];
        var cornerIds = new ulong[points.Length];
        var cornerPoints = new int[points.Length];
        var cornerEdges = new int[points.Length];
        for (var face = 0; face < triangleCount; face++)
        {
            var start = face * 3;
            points[start] = new(face * 2, 0, 0);
            points[start + 1] = new(face * 2 + 1, 0, 0);
            points[start + 2] = new(face * 2, 1, 0);
            faceIds[face] = (ulong)(1_000_000 + face);
            offsets[face] = start;
            for (var local = 0; local < 3; local++)
            {
                var index = start + local;
                pointIds[index] = (ulong)(index + 1);
                edgeIds[index] = (ulong)(2_000_000 + index);
                edges[index] = new(start + local, start + ((local + 1) % 3));
                cornerIds[index] = (ulong)(3_000_000 + index);
                cornerPoints[index] = index;
                cornerEdges[index] = index;
            }
        }
        offsets[^1] = points.Length;
        var mesh = RekallAgeMeshAsset.Create("large", "Large", new(
            pointIds, points, edgeIds, edges, faceIds, offsets, cornerIds, cornerPoints, cornerEdges));

        var compiled = new RekallAgeMeshCompiler().Compile(mesh);

        Assert.Equal(65_538, compiled.Vertices.Count);
        Assert.IsType<uint>(compiled.Indices[^1]);
        Assert.True(compiled.Indices.Max() > ushort.MaxValue);
    }

    private static RekallAgeMeshAsset Polygon(
        IReadOnlyList<RekallAgeGeometryVector3> points,
        ulong faceId,
        IReadOnlyList<RekallAgeGeometryAttribute>? attributes = null)
    {
        var count = points.Count;
        return RekallAgeMeshAsset.Create("polygon", "Polygon", new(
            Enumerable.Range(1, count).Select(value => (ulong)value).ToArray(),
            points,
            Enumerable.Range(0, count).Select(value => (ulong)(100 + value)).ToArray(),
            Enumerable.Range(0, count).Select(value => new RekallAgeMeshEdgePointIndices(value, (value + 1) % count)).ToArray(),
            [faceId],
            [0, count],
            Enumerable.Range(0, count).Select(value => (ulong)(200 + value)).ToArray(),
            Enumerable.Range(0, count).ToArray(),
            Enumerable.Range(0, count).ToArray()),
            attributes);
    }

    private static RekallAgeGeometryAttribute Attribute(
        string name,
        RekallAgeGeometryDomain domain,
        RekallAgeGeometryValueType type,
        IReadOnlyList<object> values,
        string semantic)
    {
        return new(name, domain, type, values.Select(value =>
            JsonSerializer.SerializeToElement(value, RekallAgeModelingJson.Options)).ToArray(), semantic);
    }
}
