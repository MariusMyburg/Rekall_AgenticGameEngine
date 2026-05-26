using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

public sealed record RekallAgeVulkanScenePreparedFrame(
    RekallAgeRuntimeViewportFrame Frame,
    RekallAgeVulkanSceneRenderTarget Target,
    RekallAgeVulkanSceneBatch Batch,
    RekallAgeVulkanSceneDrawPlan DrawPlan,
    RekallAgeVulkanSceneGeometryUpload GeometryUpload,
    IReadOnlyList<RekallAgeVulkanSceneMesh> Meshes,
    ulong ReadbackByteCount)
{
    public int VertexCount => Batch.Vertices.Count;

    public int IndexCount => Batch.Indices.Count;

    public int DrawCount => Batch.Draws.Count;

    public bool HasDrawableGeometry => GeometryUpload.HasGeometry && DrawCount > 0;
}

public static class RekallAgeVulkanScenePreparedFrameBuilder
{
    public static RekallAgeVulkanScenePreparedFrame Build(
        RekallAgeRuntimeViewportFrame frame,
        IReadOnlyList<RekallAgeVulkanSceneMesh> meshes,
        RekallAgeVulkanSceneRenderTarget target)
    {
        var batch = new RekallAgeVulkanSceneBatchBuilder().Build(frame, meshes);
        var drawPlan = RekallAgeVulkanSceneDrawPlanBuilder.Build(batch);
        var geometryUpload = RekallAgeVulkanSceneGeometryUploadBuilder.Build(batch);
        var readbackBytes = RekallAgeVulkanSceneRenderBackendPlanner.Plan(target).RequiresReadback
            ? checked((ulong)target.Width * target.Height * 4)
            : 0;
        return new RekallAgeVulkanScenePreparedFrame(frame, target, batch, drawPlan, geometryUpload, meshes, readbackBytes);
    }
}
