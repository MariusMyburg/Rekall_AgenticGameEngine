using System.Text.Json;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed class RekallAgeLegacyGeometryMeshAdapter
{
    public RekallAgeMeshAsset Convert(
        string assetId,
        string name,
        IReadOnlyList<RekallAgeLegacyGeometryVertex> vertices,
        IReadOnlyList<uint> indices)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);
        if (vertices.Count < 3 || indices.Count < 3 || indices.Count % 3 != 0)
        {
            throw Error("REKALL_MESH_LEGACY_TRIANGLES_INVALID", "Legacy geometry requires vertices and triangle-list indices.");
        }
        if (vertices.Any(vertex => !Finite(vertex.Position)
            || vertex.Normal is { } normal && !Finite(normal)
            || vertex.Uv is { } uv && !Finite(uv)
            || vertex.Color is { } color && !Finite(color)))
        {
            throw Error("REKALL_MESH_LEGACY_VERTEX_INVALID", "Legacy geometry vertices and attributes must be finite.");
        }

        var edgeIds = new List<ulong>();
        var edgePoints = new List<RekallAgeMeshEdgePointIndices>();
        var edgeByPoints = new Dictionary<(int A, int B), int>();
        var faceIds = new List<ulong>();
        var faceOffsets = new List<int> { 0 };
        var cornerIds = new List<ulong>();
        var cornerPoints = new List<int>();
        var cornerEdges = new List<int>();
        for (var offset = 0; offset < indices.Count; offset += 3)
        {
            var triangle = new[]
            {
                CheckedIndex(indices[offset], vertices.Count),
                CheckedIndex(indices[offset + 1], vertices.Count),
                CheckedIndex(indices[offset + 2], vertices.Count)
            };
            if (triangle.Distinct().Count() != 3)
            {
                throw Error("REKALL_MESH_LEGACY_TRIANGLE_DEGENERATE", $"Legacy triangle {offset / 3} repeats a vertex index.");
            }
            faceIds.Add((ulong)faceIds.Count + 1);
            for (var corner = 0; corner < 3; corner++)
            {
                var point = triangle[corner];
                var next = triangle[(corner + 1) % 3];
                var key = point < next ? (point, next) : (next, point);
                if (!edgeByPoints.TryGetValue(key, out var edgeIndex))
                {
                    edgeIndex = edgeIds.Count;
                    edgeByPoints.Add(key, edgeIndex);
                    edgeIds.Add((ulong)edgeIds.Count + 1);
                    edgePoints.Add(new(key.Item1, key.Item2));
                }
                cornerIds.Add((ulong)cornerIds.Count + 1);
                cornerPoints.Add(point);
                cornerEdges.Add(edgeIndex);
            }
            faceOffsets.Add(cornerIds.Count);
        }

        var attributes = new List<RekallAgeGeometryAttribute>();
        AddAttribute(attributes, "normal", "normal", RekallAgeGeometryValueType.Float3,
            vertices.Select(vertex => vertex.Normal is { } value ? Element(value.X, value.Y, value.Z) : Element(0, 0, 0)).ToArray(),
            vertices.All(vertex => vertex.Normal is not null));
        AddAttribute(attributes, "uv", "texcoord-0", RekallAgeGeometryValueType.Float2,
            vertices.Select(vertex => vertex.Uv is { } value ? Element(value.X, value.Y) : Element(0, 0)).ToArray(),
            vertices.All(vertex => vertex.Uv is not null));
        AddAttribute(attributes, "color", "color", RekallAgeGeometryValueType.ColorLinear,
            vertices.Select(vertex => vertex.Color is { } value ? Element(value.X, value.Y, value.Z, value.W) : Element(1, 1, 1, 1)).ToArray(),
            vertices.All(vertex => vertex.Color is not null));

        var mesh = RekallAgeMeshAsset.Create(
            assetId.Trim(),
            name.Trim(),
            new(
                Enumerable.Range(1, vertices.Count).Select(value => (ulong)value).ToArray(),
                vertices.Select(vertex => vertex.Position).ToArray(),
                edgeIds,
                edgePoints,
                faceIds,
                faceOffsets,
                cornerIds,
                cornerPoints,
                cornerEdges),
            attributes);
        if (!new RekallAgeMeshValidator().Validate(mesh).IsValid)
        {
            throw Error("REKALL_MESH_LEGACY_RESULT_INVALID", "Legacy geometry could not be converted into valid editable topology.");
        }
        return mesh;
    }

    private static int CheckedIndex(uint index, int vertexCount)
    {
        if (index >= vertexCount)
        {
            throw Error("REKALL_MESH_LEGACY_INDEX_OUT_OF_RANGE", $"Legacy index {index} exceeds vertex count {vertexCount}.");
        }
        return checked((int)index);
    }

    private static void AddAttribute(
        ICollection<RekallAgeGeometryAttribute> attributes,
        string name,
        string semantic,
        RekallAgeGeometryValueType type,
        IReadOnlyList<JsonElement> values,
        bool include)
    {
        if (include)
        {
            attributes.Add(new(name, RekallAgeGeometryDomain.Point, type, values, semantic));
        }
    }

    private static JsonElement Element(params double[] values) => JsonSerializer.SerializeToElement(values);

    private static RekallAgeMeshCompileException Error(string code, string message) => new(code, message);

    private static bool Finite(RekallAgeGeometryVector2 value) => double.IsFinite(value.X) && double.IsFinite(value.Y);
    private static bool Finite(RekallAgeGeometryVector3 value) => double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);
    private static bool Finite(RekallAgeGeometryVector4 value) => double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z) && double.IsFinite(value.W);
}
