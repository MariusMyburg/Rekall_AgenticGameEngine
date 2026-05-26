using System.Runtime.InteropServices;

namespace Rekall.Age.Rendering;

public sealed record RekallAgeVulkanSceneGeometryUpload(
    byte[] VertexBytes,
    byte[] IndexBytes,
    int VertexCount,
    int IndexCount)
{
    public static RekallAgeVulkanSceneGeometryUpload Empty { get; } = new([], [], 0, 0);

    public bool HasGeometry => VertexCount > 0 && IndexCount > 0;
}

public static class RekallAgeVulkanSceneGeometryUploadBuilder
{
    public static RekallAgeVulkanSceneGeometryUpload Build(RekallAgeVulkanSceneBatch batch)
    {
        var vertices = batch.Vertices
            .Select(vertex => new RekallAgeVulkanSceneGpuVertex(
                vertex.X,
                vertex.Y,
                vertex.Z,
                vertex.NormalX,
                vertex.NormalY,
                vertex.NormalZ,
                vertex.R,
                vertex.G,
                vertex.B,
                vertex.A,
                vertex.U,
                vertex.V))
            .ToArray();
        var indices = batch.Indices.ToArray();
        return new RekallAgeVulkanSceneGeometryUpload(
            MemoryMarshal.AsBytes(vertices.AsSpan()).ToArray(),
            MemoryMarshal.AsBytes(indices.AsSpan()).ToArray(),
            vertices.Length,
            indices.Length);
    }
}

[StructLayout(LayoutKind.Sequential)]
public readonly record struct RekallAgeVulkanSceneGpuVertex(
    float X,
    float Y,
    float Z,
    float NormalX,
    float NormalY,
    float NormalZ,
    float R,
    float G,
    float B,
    float A,
    float U,
    float V);
