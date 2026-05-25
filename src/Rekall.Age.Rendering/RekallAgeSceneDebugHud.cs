using System.Globalization;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

public sealed record RekallAgeSceneDebugHudStats(
    string SceneName,
    int EntityCount,
    int RenderableCount,
    int ColliderDebugCount,
    int MeshCount,
    int TriangleCount,
    int TextureCount,
    int DrawCallCount,
    int SubmittedVertexCount,
    int Fps,
    string Backend);

public static class RekallAgeSceneDebugHud
{
    public static RekallAgeSceneDebugHudStats CreateStats(
        RekallAgeRuntimeViewportFrame frame,
        IReadOnlyList<RekallAgeVulkanSceneMesh> meshes,
        int entityCount,
        int fps,
        string backend,
        int submittedVertexCount = 0,
        int drawCallCount = 0)
    {
        var textureCount = meshes
            .SelectMany(mesh => new[]
            {
                mesh.BaseColorTexture?.Id,
                mesh.MetallicRoughnessTexture?.Id,
                mesh.NormalTexture?.Id,
                mesh.OcclusionTexture?.Id,
                mesh.EmissiveTexture?.Id
            })
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Count();

        return new RekallAgeSceneDebugHudStats(
            frame.SceneName,
            entityCount,
            frame.Renderables.Count,
            CountColliderDebugRenderables(frame),
            meshes.Count,
            meshes.Sum(mesh => mesh.Indices.Count / 3),
            textureCount,
            drawCallCount,
            submittedVertexCount,
            fps,
            backend);
    }

    public static IReadOnlyList<string> FormatLines(RekallAgeSceneDebugHudStats stats)
    {
        return
        [
            stats.SceneName,
            $"FPS {stats.Fps.ToString(CultureInfo.InvariantCulture)}",
            $"ENTITIES {stats.EntityCount.ToString(CultureInfo.InvariantCulture)}",
            $"RENDERABLES {stats.RenderableCount.ToString(CultureInfo.InvariantCulture)}",
            $"COLLIDERS {stats.ColliderDebugCount.ToString(CultureInfo.InvariantCulture)}",
            $"MESHES {stats.MeshCount.ToString(CultureInfo.InvariantCulture)}",
            $"TRIANGLES {FormatCount(stats.TriangleCount)}",
            $"VERTICES {FormatCount(stats.SubmittedVertexCount)}",
            $"TEXTURES {stats.TextureCount.ToString(CultureInfo.InvariantCulture)}",
            $"DRAWS {stats.DrawCallCount.ToString(CultureInfo.InvariantCulture)}",
            stats.Backend.ToUpperInvariant()
        ];
    }

    private static int CountColliderDebugRenderables(RekallAgeRuntimeViewportFrame frame)
    {
        return frame.Renderables.Count(renderable =>
            renderable.EntityId.EndsWith(":collider", StringComparison.Ordinal)
            || renderable.EntityName.EndsWith(" Collider", StringComparison.Ordinal));
    }

    private static string FormatCount(int count)
    {
        return count >= 1_000_000
            ? $"{count / 1_000_000d:0.0}M"
            : count >= 1_000
            ? $"{count / 1_000d:0.0}K"
            : count.ToString(CultureInfo.InvariantCulture);
    }
}
