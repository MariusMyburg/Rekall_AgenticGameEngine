using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

public static class RekallAgeVirtualGeometryReducer
{
    public static RekallAgeVulkanSceneMesh Reduce(
        RekallAgeVulkanSceneMesh mesh,
        RekallAgeRuntimeViewportRenderable renderable,
        RekallAgeRuntimeViewportCamera? camera)
    {
        var settings = renderable.VirtualGeometry;
        var sourceTriangles = mesh.Indices.Count / 3;
        if (settings is not { Enabled: true }
            || sourceTriangles <= 1
            || settings.MaxLodLevel <= 0
            || (settings.MaxSelectedTriangles <= 0 && camera is null))
        {
            return mesh;
        }

        var targetLevel = Math.Max(
            ResolveBudgetLevel(sourceTriangles, settings),
            ResolveDistanceLevel(renderable, camera, settings));
        targetLevel = Math.Clamp(targetLevel, 0, settings.MaxLodLevel);
        if (targetLevel <= 0)
        {
            return mesh;
        }

        var stride = 1 << Math.Min(targetLevel, 20);
        var selectedTriangleCapacity = Math.Max(1, (sourceTriangles + stride - 1) / stride);
        var vertices = new List<RekallAgeVulkanSceneVertex>(Math.Min(mesh.Vertices.Count, selectedTriangleCapacity * 3));
        var indices = new List<uint>(selectedTriangleCapacity * 3);
        var remap = new Dictionary<uint, uint>();
        var selectedTriangles = 0;
        var clusterTriangleCount = Math.Clamp(settings.ClusterTriangleCount, 1, sourceTriangles);
        for (var clusterStart = 0; clusterStart < sourceTriangles; clusterStart += clusterTriangleCount)
        {
            var clusterEnd = Math.Min(sourceTriangles, clusterStart + clusterTriangleCount);
            for (var triangle = clusterStart; triangle < clusterEnd; triangle += stride)
            {
                if (settings.MaxSelectedTriangles > 0 && selectedTriangles >= settings.MaxSelectedTriangles)
                {
                    break;
                }

                var sourceIndex = triangle * 3;
                indices.Add(GetOrAdd(mesh.Indices[sourceIndex]));
                indices.Add(GetOrAdd(mesh.Indices[sourceIndex + 1]));
                indices.Add(GetOrAdd(mesh.Indices[sourceIndex + 2]));
                selectedTriangles++;
            }

            if (settings.MaxSelectedTriangles > 0 && selectedTriangles >= settings.MaxSelectedTriangles)
            {
                break;
            }
        }

        if (indices.Count == 0 || indices.Count == mesh.Indices.Count)
        {
            return mesh;
        }

        return mesh with
        {
            Vertices = vertices,
            Indices = indices,
            VirtualGeometrySourceTriangleCount = sourceTriangles,
            VirtualGeometryLodLevel = targetLevel
        };

        uint GetOrAdd(uint sourceVertexIndex)
        {
            if (remap.TryGetValue(sourceVertexIndex, out var existing))
            {
                return existing;
            }

            if (sourceVertexIndex >= mesh.Vertices.Count)
            {
                return 0;
            }

            var mapped = checked((uint)vertices.Count);
            vertices.Add(mesh.Vertices[(int)sourceVertexIndex]);
            remap[sourceVertexIndex] = mapped;
            return mapped;
        }
    }

    private static int ResolveBudgetLevel(
        int sourceTriangles,
        RekallAgeRuntimeViewportVirtualGeometry settings)
    {
        if (settings.MaxSelectedTriangles <= 0 || sourceTriangles <= settings.MaxSelectedTriangles)
        {
            return 0;
        }

        var level = 0;
        var stride = 1;
        while (level < settings.MaxLodLevel
            && (sourceTriangles + stride - 1) / stride > settings.MaxSelectedTriangles)
        {
            level++;
            stride <<= 1;
        }

        return level;
    }

    private static int ResolveDistanceLevel(
        RekallAgeRuntimeViewportRenderable renderable,
        RekallAgeRuntimeViewportCamera? camera,
        RekallAgeRuntimeViewportVirtualGeometry settings)
    {
        if (camera is null || settings.TargetPixelError <= 0)
        {
            return 0;
        }

        var dx = renderable.X - camera.X;
        var dy = renderable.Y - camera.Y;
        var dz = renderable.Z - camera.Z;
        var distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        var distancePerLevel = Math.Max(16, settings.TargetPixelError * 32);
        return Math.Clamp((int)Math.Floor(distance / distancePerLevel), 0, settings.MaxLodLevel);
    }
}
