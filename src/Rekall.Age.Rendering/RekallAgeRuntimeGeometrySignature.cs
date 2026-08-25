using System.Runtime.CompilerServices;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

public static class RekallAgeRuntimeGeometrySignature
{
    private static readonly ConditionalWeakTable<RekallAgeRuntimeViewportGeometryMesh, SignatureBox> MeshSignatures = new();
    private static readonly ConditionalWeakTable<RekallAgeRuntimeViewportLineSegments, SignatureBox> LineSignatures = new();

    public static int For(RekallAgeRuntimeViewportGeometryMesh mesh) =>
        MeshSignatures.GetValue(mesh, static value => new SignatureBox(Hash(value))).Value;

    public static int For(RekallAgeRuntimeViewportLineSegments lines) =>
        LineSignatures.GetValue(lines, static value => new SignatureBox(Hash(value))).Value;

    private static int Hash(RekallAgeRuntimeViewportGeometryMesh mesh)
    {
        var hash = new HashCode();
        hash.Add(mesh.Vertices.Count);
        foreach (var vertex in mesh.Vertices)
        {
            hash.Add(vertex.X);
            hash.Add(vertex.Y);
            hash.Add(vertex.Z);
            hash.Add(vertex.NormalX);
            hash.Add(vertex.NormalY);
            hash.Add(vertex.NormalZ);
            hash.Add(vertex.R);
            hash.Add(vertex.G);
            hash.Add(vertex.B);
            hash.Add(vertex.A);
            hash.Add(vertex.U);
            hash.Add(vertex.V);
        }

        hash.Add(mesh.Indices.Count);
        foreach (var index in mesh.Indices)
        {
            hash.Add(index);
        }

        foreach (var surface in mesh.Surfaces ?? [])
        {
            hash.Add(surface.SurfaceIndex);
            hash.Add(surface.MaterialSlotIndex);
            hash.Add(surface.MaterialAssetId, StringComparer.Ordinal);
            hash.Add(surface.FirstIndex);
            hash.Add(surface.IndexCount);
        }

        return hash.ToHashCode();
    }

    private static int Hash(RekallAgeRuntimeViewportLineSegments lines)
    {
        var hash = new HashCode();
        hash.Add(lines.Thickness);
        hash.Add(lines.Segments.Count);
        foreach (var segment in lines.Segments)
        {
            hash.Add(segment.FromX);
            hash.Add(segment.FromY);
            hash.Add(segment.FromZ);
            hash.Add(segment.ToX);
            hash.Add(segment.ToY);
            hash.Add(segment.ToZ);
        }

        return hash.ToHashCode();
    }

    private sealed record SignatureBox(int Value);
}
