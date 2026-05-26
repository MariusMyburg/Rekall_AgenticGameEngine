using Rekall.Age.Rendering;
using Silk.NET.Vulkan;

namespace Rekall.Age.Tests.Rendering;

public sealed class VulkanSceneRenderBackendPlannerTests
{
    [Fact]
    public void PlannerTreatsOpenXrStereoSwapchainAsExternallyOwnedLayeredRenderTarget()
    {
        var target = RekallAgeVulkanSceneRenderTarget.OpenXrStereoSwapchain(
            1832,
            1920,
            2,
            Format.B8G8R8A8Srgb,
            Format.D32Sfloat);

        var plan = RekallAgeVulkanSceneRenderBackendPlanner.Plan(target);

        Assert.True(plan.CanUseNativeScenePipeline);
        Assert.True(plan.RequiresOpenXrOwnedVulkanDevice);
        Assert.True(plan.RequiresPerEyeProjection);
        Assert.True(plan.RequiresArrayLayerFramebuffers);
        Assert.False(plan.RequiresReadback);
        Assert.False(plan.Ownership.OwnsVulkanInstance);
        Assert.False(plan.Ownership.OwnsVulkanDevice);
        Assert.False(plan.Ownership.OwnsColorImages);
        Assert.True(plan.Ownership.OwnsDepthImages);
        Assert.True(plan.Ownership.OwnsImageViews);
        Assert.Equal("openxr-frame-loop", plan.Ownership.SynchronizationOwner);
        Assert.Equal(2u, plan.Framebuffers.ColorImageLayerCount);
        Assert.Equal(2u, plan.Framebuffers.ColorImageViewCountPerSwapchainImage);
        Assert.Equal(2u, plan.Framebuffers.FramebufferCountPerSwapchainImage);
        Assert.Equal(1u, plan.Framebuffers.FramebufferLayers);
        Assert.True(plan.Framebuffers.UsesPerEyeLayerViews);
        Assert.Equal(2u, plan.CommandSubmission.RenderPassesPerFrame);
        Assert.Equal(2u, plan.CommandSubmission.FrameUniformBufferCount);
        Assert.False(plan.CommandSubmission.CopiesColorToReadback);
        Assert.True(plan.CommandSubmission.LeavesColorForCompositor);
        Assert.True(plan.CommandSubmission.RequiresExternalAcquireRelease);
        Assert.Equal(ImageLayout.ColorAttachmentOptimal, plan.Target.FinalColorLayout);
        Assert.Contains(plan.RequiredSteps, step => step.Contains("xrLocateViews", StringComparison.Ordinal));
        Assert.Empty(plan.Blockers);
    }

    [Fact]
    public void PlannerKeepsOffscreenCaptureAsReadbackTarget()
    {
        var target = RekallAgeVulkanSceneRenderTarget.OffscreenCapture(320, 180);

        var plan = RekallAgeVulkanSceneRenderBackendPlanner.Plan(target);

        Assert.True(plan.CanUseNativeScenePipeline);
        Assert.False(plan.RequiresOpenXrOwnedVulkanDevice);
        Assert.False(plan.RequiresPerEyeProjection);
        Assert.False(plan.RequiresArrayLayerFramebuffers);
        Assert.True(plan.RequiresReadback);
        Assert.True(plan.Ownership.OwnsVulkanInstance);
        Assert.True(plan.Ownership.OwnsVulkanDevice);
        Assert.True(plan.Ownership.OwnsColorImages);
        Assert.True(plan.Ownership.OwnsReadbackBuffers);
        Assert.Equal("renderer-submit-and-wait", plan.Ownership.SynchronizationOwner);
        Assert.Equal(1u, plan.Framebuffers.ColorImageLayerCount);
        Assert.Equal(1u, plan.Framebuffers.ColorImageViewCountPerSwapchainImage);
        Assert.Equal(1u, plan.Framebuffers.FramebufferCountPerSwapchainImage);
        Assert.False(plan.Framebuffers.UsesPerEyeLayerViews);
        Assert.Equal(1u, plan.CommandSubmission.RenderPassesPerFrame);
        Assert.Equal(1u, plan.CommandSubmission.FrameUniformBufferCount);
        Assert.True(plan.CommandSubmission.CopiesColorToReadback);
        Assert.False(plan.CommandSubmission.LeavesColorForCompositor);
        Assert.False(plan.CommandSubmission.RequiresExternalAcquireRelease);
        Assert.Equal(ImageLayout.TransferSrcOptimal, plan.Target.FinalColorLayout);
        Assert.Empty(plan.Blockers);
    }

    [Theory]
    [InlineData(43, Format.R8G8B8A8Srgb)]
    [InlineData(50, Format.B8G8R8A8Srgb)]
    public void SwapchainFormatMapperResolvesCommonOpenXrColorFormats(long formatCode, Format expected)
    {
        var mapped = RekallAgeVulkanSceneSwapchainFormatMapper.TryMapColorFormat(formatCode, out var format);

        Assert.True(mapped);
        Assert.Equal(expected, format);
    }
}
