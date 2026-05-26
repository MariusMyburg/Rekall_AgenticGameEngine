using System.Numerics;

namespace Rekall.Age.Rendering;

public sealed record RekallAgeVulkanSceneCommandDraw(
    uint FirstIndex,
    uint IndexCount,
    int VertexOffset,
    RekallAgeVulkanSceneMaterialKey MaterialKey,
    RekallAgeVulkanSceneGpuDrawPushConstants PushConstants);

public sealed record RekallAgeVulkanSceneRenderPassCommand(
    uint FramebufferIndex,
    int? EyeIndex,
    Vector4 Viewport,
    RekallAgeVulkanSceneGpuFrameUniform FrameUniform,
    IReadOnlyList<RekallAgeVulkanSceneCommandDraw> Draws);

public sealed record RekallAgeVulkanSceneCommandPlan(
    RekallAgeVulkanScenePreparedFrame PreparedFrame,
    IReadOnlyList<RekallAgeVulkanSceneRenderPassCommand> RenderPasses,
    uint FrameUniformBufferCount,
    bool CopiesColorToReadback,
    bool LeavesColorForCompositor,
    bool RequiresExternalAcquireRelease,
    bool Ready,
    IReadOnlyList<string> Blockers)
{
    public int DrawCount => RenderPasses.Sum(pass => pass.Draws.Count);
}

public static class RekallAgeVulkanSceneCommandPlanBuilder
{
    public static RekallAgeVulkanSceneCommandPlan BuildOffscreen(RekallAgeVulkanScenePreparedFrame preparedFrame)
    {
        var backend = RekallAgeVulkanSceneRenderBackendPlanner.Plan(preparedFrame.Target);
        var blockers = new List<string>(backend.Blockers);
        if (preparedFrame.Target.IsOpenXrStereoSwapchain)
        {
            blockers.Add("Offscreen command plans require a non-OpenXR render target.");
        }

        if (!preparedFrame.HasDrawableGeometry)
        {
            blockers.Add("Offscreen command plans require drawable geometry.");
        }

        if (blockers.Count > 0)
        {
            return Blocked(preparedFrame, backend, blockers);
        }

        return new RekallAgeVulkanSceneCommandPlan(
            preparedFrame,
            [
                new RekallAgeVulkanSceneRenderPassCommand(
                    0,
                    null,
                    new Vector4(0, 0, preparedFrame.Target.Width, preparedFrame.Target.Height),
                    RekallAgeVulkanSceneUniformUploadBuilder.BuildFrameUniform(preparedFrame.Batch.Frame),
                    BuildDrawCommands(preparedFrame.DrawPlan.Draws))
            ],
            backend.CommandSubmission.FrameUniformBufferCount,
            backend.CommandSubmission.CopiesColorToReadback,
            backend.CommandSubmission.LeavesColorForCompositor,
            backend.CommandSubmission.RequiresExternalAcquireRelease,
            true,
            Array.Empty<string>());
    }

    public static RekallAgeVulkanSceneCommandPlan BuildOpenXr(RekallAgeOpenXrNativeSceneRenderPlan nativePlan)
    {
        var preparedFrame = nativePlan.PreparedFrame;
        var backend = RekallAgeVulkanSceneRenderBackendPlanner.Plan(preparedFrame.Target);
        var blockers = new List<string>(backend.Blockers);
        if (!nativePlan.Ready)
        {
            blockers.AddRange(nativePlan.Blockers);
        }

        if (!preparedFrame.Target.IsOpenXrStereoSwapchain)
        {
            blockers.Add("OpenXR command plans require an OpenXR stereo swapchain render target.");
        }

        if (blockers.Count > 0)
        {
            return Blocked(preparedFrame, backend, blockers);
        }

        var draws = BuildDrawCommands(preparedFrame.DrawPlan.Draws);
        return new RekallAgeVulkanSceneCommandPlan(
            preparedFrame,
            nativePlan.Eyes
                .OrderBy(eye => eye.FramebufferIndex)
                .Select(eye => new RekallAgeVulkanSceneRenderPassCommand(
                    eye.FramebufferIndex,
                    eye.EyeIndex,
                    eye.Viewport,
                    eye.FrameUniform,
                    draws))
                .ToArray(),
            backend.CommandSubmission.FrameUniformBufferCount,
            backend.CommandSubmission.CopiesColorToReadback,
            backend.CommandSubmission.LeavesColorForCompositor,
            backend.CommandSubmission.RequiresExternalAcquireRelease,
            true,
            Array.Empty<string>());
    }

    private static RekallAgeVulkanSceneCommandPlan Blocked(
        RekallAgeVulkanScenePreparedFrame preparedFrame,
        RekallAgeVulkanSceneRenderBackendPlan backend,
        IReadOnlyList<string> blockers)
    {
        return new RekallAgeVulkanSceneCommandPlan(
            preparedFrame,
            Array.Empty<RekallAgeVulkanSceneRenderPassCommand>(),
            backend.CommandSubmission.FrameUniformBufferCount,
            backend.CommandSubmission.CopiesColorToReadback,
            backend.CommandSubmission.LeavesColorForCompositor,
            backend.CommandSubmission.RequiresExternalAcquireRelease,
            false,
            blockers.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static IReadOnlyList<RekallAgeVulkanSceneCommandDraw> BuildDrawCommands(
        IReadOnlyList<RekallAgeVulkanScenePreparedDraw> draws)
    {
        return draws
            .Select(draw => new RekallAgeVulkanSceneCommandDraw(
                draw.FirstIndex,
                draw.IndexCount,
                draw.VertexOffset,
                draw.MaterialKey,
                RekallAgeVulkanSceneUniformUploadBuilder.BuildDrawPushConstants(
                    draw.Model,
                    draw.MaterialFactors,
                    draw.EmissiveFactors)))
            .ToArray();
    }
}
