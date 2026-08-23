using System.Text.Json;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed class RekallAgeMeshCompileException : InvalidOperationException
{
    public RekallAgeMeshCompileException(string code, string message, ulong? faceId = null)
        : base(message)
    {
        Code = code;
        FaceId = faceId;
    }

    public string Code { get; }

    public ulong? FaceId { get; }
}

public sealed class RekallAgeMeshCompiler
{
    private const double Epsilon = 1e-12;
    private readonly RekallAgeMeshValidator _validator = new();

    public RekallAgeCompiledMeshSnapshot Compile(RekallAgeMeshAsset mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var validation = _validator.Validate(mesh);
        if (!validation.IsValid)
        {
            throw new RekallAgeMeshCompileException(
                "REKALL_MESH_COMPILE_SOURCE_INVALID",
                "Editable mesh must pass strict validation before compilation.");
        }

        var topology = mesh.Topology;
        var normalAttribute = FindAttribute(mesh, "normal", RekallAgeGeometryValueType.Float3);
        var uvAttribute = FindAttribute(mesh, "texcoord-0", RekallAgeGeometryValueType.Float2)
            ?? mesh.Attributes.FirstOrDefault(attribute =>
                attribute.Name.Equals("uv", StringComparison.OrdinalIgnoreCase)
                && attribute.ValueType == RekallAgeGeometryValueType.Float2);
        var colorAttribute = FindAttribute(mesh, "color", RekallAgeGeometryValueType.ColorLinear)
            ?? FindAttribute(mesh, "color", RekallAgeGeometryValueType.Float4);
        var materialAttribute = mesh.Attributes.FirstOrDefault(attribute =>
            string.Equals(attribute.Semantic, "material-index", StringComparison.OrdinalIgnoreCase)
            && attribute.ValueType == RekallAgeGeometryValueType.Int32
            && attribute.Domain == RekallAgeGeometryDomain.Face);

        var positions = new RekallAgeGeometryVector3[topology.CornerIds.Count];
        var normals = new RekallAgeGeometryVector3[topology.CornerIds.Count];
        var uvs = new RekallAgeGeometryVector2[topology.CornerIds.Count];
        var colors = new RekallAgeGeometryVector4[topology.CornerIds.Count];
        var pointIds = new ulong[topology.CornerIds.Count];
        var tangentSums = new RekallAgeGeometryVector3[topology.CornerIds.Count];
        var bitangentSums = new RekallAgeGeometryVector3[topology.CornerIds.Count];
        var indices = new List<uint>();
        var triangles = new List<RekallAgeCompiledMeshTriangle>();
        var surfaces = new List<SurfaceBuilder>();

        for (var faceIndex = 0; faceIndex < topology.FaceIds.Count; faceIndex++)
        {
            var start = topology.FaceOffsets[faceIndex];
            var end = topology.FaceOffsets[faceIndex + 1];
            var faceNormal = FaceNormal(topology, start, end);
            for (var cornerIndex = start; cornerIndex < end; cornerIndex++)
            {
                var pointIndex = topology.CornerPointIndices[cornerIndex];
                positions[cornerIndex] = topology.Positions[pointIndex];
                pointIds[cornerIndex] = topology.PointIds[pointIndex];
                normals[cornerIndex] = Normalize(ReadVector3(normalAttribute, cornerIndex, pointIndex) ?? faceNormal, faceNormal);
                uvs[cornerIndex] = ReadVector2(uvAttribute, cornerIndex, pointIndex) ?? new(0, 0);
                colors[cornerIndex] = ReadVector4(colorAttribute, cornerIndex, pointIndex) ?? new(1, 1, 1, 1);
            }

            var materialIndex = ReadMaterialIndex(materialAttribute, faceIndex);
            var surface = GetSurface(surfaces, mesh.MaterialSlots, materialIndex, indices.Count);
            var localTriangles = TriangulateFace(topology, faceIndex, start, end);
            foreach (var localTriangle in localTriangles)
            {
                var a = start + localTriangle.A;
                var b = start + localTriangle.B;
                var c = start + localTriangle.C;
                var firstIndex = indices.Count;
                indices.Add(checked((uint)a));
                indices.Add(checked((uint)b));
                indices.Add(checked((uint)c));
                AccumulateTangent(a, b, c, positions, normals, uvs, tangentSums, bitangentSums);
                triangles.Add(new(
                    triangles.Count,
                    topology.FaceIds[faceIndex],
                    [topology.CornerIds[a], topology.CornerIds[b], topology.CornerIds[c]],
                    [pointIds[a], pointIds[b], pointIds[c]],
                    surface.SurfaceIndex));
                surface.IndexCount += indices.Count - firstIndex;
            }
            surface.SourceFaceIds.Add(topology.FaceIds[faceIndex]);
        }

        var vertices = new RekallAgeCompiledMeshVertex[topology.CornerIds.Count];
        for (var index = 0; index < vertices.Length; index++)
        {
            var tangent = OrthonormalTangent(normals[index], tangentSums[index]);
            var handedness = Dot(Cross(normals[index], tangent), bitangentSums[index]) < 0 ? -1d : 1d;
            vertices[index] = new(
                pointIds[index],
                topology.CornerIds[index],
                positions[index],
                normals[index],
                new(tangent.X, tangent.Y, tangent.Z, handedness),
                uvs[index],
                colors[index]);
        }

        return new RekallAgeCompiledMeshSnapshot(
            mesh.AssetId,
            mesh.Revision,
            vertices,
            indices,
            triangles,
            surfaces.Select(surface => surface.Build()).ToArray(),
            validation.Summary.Bounds);
    }

    private static RekallAgeGeometryAttribute? FindAttribute(
        RekallAgeMeshAsset mesh,
        string semantic,
        RekallAgeGeometryValueType type) =>
        mesh.Attributes.FirstOrDefault(attribute =>
            string.Equals(attribute.Semantic, semantic, StringComparison.OrdinalIgnoreCase)
            && attribute.ValueType == type
            && attribute.Domain is RekallAgeGeometryDomain.Point or RekallAgeGeometryDomain.Corner);

    private static int ReadMaterialIndex(RekallAgeGeometryAttribute? attribute, int faceIndex)
    {
        if (attribute is null || attribute.Domain != RekallAgeGeometryDomain.Face)
        {
            return 0;
        }
        return attribute.Values[faceIndex].TryGetInt32(out var value) ? value : 0;
    }

    private static SurfaceBuilder GetSurface(
        List<SurfaceBuilder> surfaces,
        IReadOnlyList<RekallAgeMaterialSlot> materialSlots,
        int materialIndex,
        int firstIndex)
    {
        if (surfaces.Count > 0 && surfaces[^1].MaterialSlotIndex == materialIndex)
        {
            return surfaces[^1];
        }
        var materialAssetId = materialIndex >= 0 && materialIndex < materialSlots.Count
            ? materialSlots[materialIndex].MaterialAssetId
            : null;
        var result = new SurfaceBuilder(surfaces.Count, materialIndex, materialAssetId, firstIndex);
        surfaces.Add(result);
        return result;
    }

    private static IReadOnlyList<LocalTriangle> TriangulateFace(
        RekallAgeMeshTopology topology,
        int faceIndex,
        int start,
        int end)
    {
        var count = end - start;
        if (count == 3)
        {
            return [new(0, 1, 2)];
        }

        var normal = FaceNormal(topology, start, end);
        var projected = new RekallAgeGeometryVector2[count];
        var axis = DominantAxis(normal);
        for (var local = 0; local < count; local++)
        {
            projected[local] = Project(topology.Positions[topology.CornerPointIndices[start + local]], axis);
        }
        var orientation = SignedArea(projected) >= 0 ? 1d : -1d;
        var remaining = Enumerable.Range(0, count).ToList();
        var output = new List<LocalTriangle>(count - 2);
        while (remaining.Count > 3)
        {
            var clipped = false;
            for (var cursor = 0; cursor < remaining.Count; cursor++)
            {
                var previous = remaining[(cursor + remaining.Count - 1) % remaining.Count];
                var current = remaining[cursor];
                var next = remaining[(cursor + 1) % remaining.Count];
                if (Cross2(projected[previous], projected[current], projected[next]) * orientation <= Epsilon)
                {
                    continue;
                }
                if (remaining.Any(candidate =>
                    candidate != previous && candidate != current && candidate != next
                    && IsInsideTriangle(projected[candidate], projected[previous], projected[current], projected[next], orientation)))
                {
                    continue;
                }
                output.Add(new(previous, current, next));
                remaining.RemoveAt(cursor);
                clipped = true;
                break;
            }
            if (!clipped)
            {
                throw new RekallAgeMeshCompileException(
                    "REKALL_MESH_COMPILE_TRIANGULATION_FAILED",
                    $"Face {topology.FaceIds[faceIndex]} could not be deterministically triangulated; repair self-intersection or degeneracy.",
                    topology.FaceIds[faceIndex]);
            }
        }
        output.Add(new(remaining[0], remaining[1], remaining[2]));
        return output;
    }

    private static bool IsInsideTriangle(
        RekallAgeGeometryVector2 point,
        RekallAgeGeometryVector2 a,
        RekallAgeGeometryVector2 b,
        RekallAgeGeometryVector2 c,
        double orientation)
    {
        var ab = Cross2(a, b, point) * orientation;
        var bc = Cross2(b, c, point) * orientation;
        var ca = Cross2(c, a, point) * orientation;
        return ab >= -Epsilon && bc >= -Epsilon && ca >= -Epsilon;
    }

    private static RekallAgeGeometryVector3 FaceNormal(RekallAgeMeshTopology topology, int start, int end)
    {
        var normal = new RekallAgeGeometryVector3(0, 0, 0);
        for (var index = start; index < end; index++)
        {
            var next = index + 1 == end ? start : index + 1;
            var a = topology.Positions[topology.CornerPointIndices[index]];
            var b = topology.Positions[topology.CornerPointIndices[next]];
            normal = new(
                normal.X + (a.Y - b.Y) * (a.Z + b.Z),
                normal.Y + (a.Z - b.Z) * (a.X + b.X),
                normal.Z + (a.X - b.X) * (a.Y + b.Y));
        }
        return Normalize(normal, new(0, 1, 0));
    }

    private static void AccumulateTangent(
        int a,
        int b,
        int c,
        IReadOnlyList<RekallAgeGeometryVector3> positions,
        IReadOnlyList<RekallAgeGeometryVector3> normals,
        IReadOnlyList<RekallAgeGeometryVector2> uvs,
        RekallAgeGeometryVector3[] tangents,
        RekallAgeGeometryVector3[] bitangents)
    {
        var edge1 = Subtract(positions[b], positions[a]);
        var edge2 = Subtract(positions[c], positions[a]);
        var du1 = uvs[b].X - uvs[a].X;
        var dv1 = uvs[b].Y - uvs[a].Y;
        var du2 = uvs[c].X - uvs[a].X;
        var dv2 = uvs[c].Y - uvs[a].Y;
        var denominator = du1 * dv2 - du2 * dv1;
        RekallAgeGeometryVector3 tangent;
        RekallAgeGeometryVector3 bitangent;
        if (Math.Abs(denominator) <= Epsilon)
        {
            tangent = OrthonormalTangent(normals[a], edge1);
            bitangent = Cross(normals[a], tangent);
        }
        else
        {
            var inverse = 1d / denominator;
            tangent = new(
                (edge1.X * dv2 - edge2.X * dv1) * inverse,
                (edge1.Y * dv2 - edge2.Y * dv1) * inverse,
                (edge1.Z * dv2 - edge2.Z * dv1) * inverse);
            bitangent = new(
                (edge2.X * du1 - edge1.X * du2) * inverse,
                (edge2.Y * du1 - edge1.Y * du2) * inverse,
                (edge2.Z * du1 - edge1.Z * du2) * inverse);
        }
        foreach (var index in new[] { a, b, c })
        {
            tangents[index] = Add(tangents[index], tangent);
            bitangents[index] = Add(bitangents[index], bitangent);
        }
    }

    private static RekallAgeGeometryVector3 OrthonormalTangent(
        RekallAgeGeometryVector3 normal,
        RekallAgeGeometryVector3 candidate)
    {
        var projected = Subtract(candidate, Scale(normal, Dot(normal, candidate)));
        if (LengthSquared(projected) <= Epsilon)
        {
            var axis = Math.Abs(normal.X) < 0.9 ? new RekallAgeGeometryVector3(1, 0, 0) : new(0, 1, 0);
            projected = Cross(axis, normal);
        }
        return Normalize(projected, new(1, 0, 0));
    }

    private static RekallAgeGeometryVector2? ReadVector2(RekallAgeGeometryAttribute? attribute, int cornerIndex, int pointIndex) =>
        attribute is null ? null : TryVector2(attribute.Values[AttributeIndex(attribute, cornerIndex, pointIndex)]);

    private static RekallAgeGeometryVector3? ReadVector3(RekallAgeGeometryAttribute? attribute, int cornerIndex, int pointIndex) =>
        attribute is null ? null : TryVector3(attribute.Values[AttributeIndex(attribute, cornerIndex, pointIndex)]);

    private static RekallAgeGeometryVector4? ReadVector4(RekallAgeGeometryAttribute? attribute, int cornerIndex, int pointIndex) =>
        attribute is null ? null : TryVector4(attribute.Values[AttributeIndex(attribute, cornerIndex, pointIndex)]);

    private static int AttributeIndex(RekallAgeGeometryAttribute attribute, int cornerIndex, int pointIndex) =>
        attribute.Domain == RekallAgeGeometryDomain.Corner ? cornerIndex : pointIndex;

    private static RekallAgeGeometryVector2? TryVector2(JsonElement value)
    {
        var items = Components(value, 2);
        return items is null ? null : new(items[0], items[1]);
    }

    private static RekallAgeGeometryVector3? TryVector3(JsonElement value)
    {
        var items = Components(value, 3);
        return items is null ? null : new(items[0], items[1], items[2]);
    }

    private static RekallAgeGeometryVector4? TryVector4(JsonElement value)
    {
        var items = Components(value, 4);
        return items is null ? null : new(items[0], items[1], items[2], items[3]);
    }

    private static double[]? Components(JsonElement value, int count)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != count)
        {
            return null;
        }
        var result = new double[count];
        var index = 0;
        foreach (var component in value.EnumerateArray())
        {
            if (!component.TryGetDouble(out result[index]) || !double.IsFinite(result[index]))
            {
                return null;
            }
            index++;
        }
        return result;
    }

    private static int DominantAxis(RekallAgeGeometryVector3 normal)
    {
        var x = Math.Abs(normal.X);
        var y = Math.Abs(normal.Y);
        var z = Math.Abs(normal.Z);
        return x >= y && x >= z ? 0 : y >= z ? 1 : 2;
    }

    private static RekallAgeGeometryVector2 Project(RekallAgeGeometryVector3 point, int axis) => axis switch
    {
        0 => new(point.Y, point.Z),
        1 => new(point.X, point.Z),
        _ => new(point.X, point.Y)
    };

    private static double SignedArea(IReadOnlyList<RekallAgeGeometryVector2> points)
    {
        var area = 0d;
        for (var index = 0; index < points.Count; index++)
        {
            var next = (index + 1) % points.Count;
            area += points[index].X * points[next].Y - points[next].X * points[index].Y;
        }
        return area * 0.5;
    }

    private static double Cross2(RekallAgeGeometryVector2 a, RekallAgeGeometryVector2 b, RekallAgeGeometryVector2 c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static RekallAgeGeometryVector3 Add(RekallAgeGeometryVector3 a, RekallAgeGeometryVector3 b) =>
        new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    private static RekallAgeGeometryVector3 Subtract(RekallAgeGeometryVector3 a, RekallAgeGeometryVector3 b) =>
        new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private static RekallAgeGeometryVector3 Scale(RekallAgeGeometryVector3 value, double scale) =>
        new(value.X * scale, value.Y * scale, value.Z * scale);

    private static double Dot(RekallAgeGeometryVector3 a, RekallAgeGeometryVector3 b) =>
        a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static RekallAgeGeometryVector3 Cross(RekallAgeGeometryVector3 a, RekallAgeGeometryVector3 b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    private static double LengthSquared(RekallAgeGeometryVector3 value) => Dot(value, value);

    private static RekallAgeGeometryVector3 Normalize(RekallAgeGeometryVector3 value, RekallAgeGeometryVector3 fallback)
    {
        var lengthSquared = LengthSquared(value);
        if (!double.IsFinite(lengthSquared) || lengthSquared <= Epsilon)
        {
            return fallback;
        }
        return Scale(value, 1d / Math.Sqrt(lengthSquared));
    }

    private readonly record struct LocalTriangle(int A, int B, int C);

    private sealed class SurfaceBuilder(
        int surfaceIndex,
        int materialSlotIndex,
        string? materialAssetId,
        int firstIndex)
    {
        public int SurfaceIndex { get; } = surfaceIndex;
        public int MaterialSlotIndex { get; } = materialSlotIndex;
        public string? MaterialAssetId { get; } = materialAssetId;
        public int FirstIndex { get; } = firstIndex;
        public int IndexCount { get; set; }
        public List<ulong> SourceFaceIds { get; } = [];

        public RekallAgeCompiledMeshSurface Build() =>
            new(SurfaceIndex, MaterialSlotIndex, MaterialAssetId, FirstIndex, IndexCount, SourceFaceIds.ToArray());
    }
}
