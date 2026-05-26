using Silk.NET.Vulkan;

namespace Rekall.Age.Rendering;

public sealed record RekallAgeVulkanSceneRenderTarget(
    string Kind,
    uint Width,
    uint Height,
    Format ColorFormat,
    Format DepthFormat,
    uint EyeCount = 1,
    bool ExternallyOwnedColorImages = false,
    bool UsesArrayLayers = false,
    ImageLayout InitialColorLayout = ImageLayout.Undefined,
    ImageLayout FinalColorLayout = ImageLayout.TransferSrcOptimal)
{
    public bool IsOpenXrStereoSwapchain =>
        Kind.Equals(RekallAgeVulkanSceneRenderTargetKinds.OpenXrStereoSwapchain, StringComparison.Ordinal)
        && ExternallyOwnedColorImages
        && UsesArrayLayers
        && EyeCount >= 2;

    public static RekallAgeVulkanSceneRenderTarget OffscreenCapture(uint width, uint height)
    {
        return new RekallAgeVulkanSceneRenderTarget(
            RekallAgeVulkanSceneRenderTargetKinds.OffscreenCapture,
            width,
            height,
            Format.R8G8B8A8Unorm,
            Format.D32Sfloat);
    }

    public static RekallAgeVulkanSceneRenderTarget OpenXrStereoSwapchain(
        uint width,
        uint height,
        uint eyeCount,
        Format colorFormat,
        Format depthFormat)
    {
        return new RekallAgeVulkanSceneRenderTarget(
            RekallAgeVulkanSceneRenderTargetKinds.OpenXrStereoSwapchain,
            width,
            height,
            colorFormat,
            depthFormat,
            Math.Max(2, eyeCount),
            ExternallyOwnedColorImages: true,
            UsesArrayLayers: true,
            InitialColorLayout: ImageLayout.Undefined,
            FinalColorLayout: ImageLayout.ColorAttachmentOptimal);
    }
}

public static class RekallAgeVulkanSceneRenderTargetKinds
{
    public const string OffscreenCapture = "offscreen-capture";
    public const string DesktopSwapchain = "desktop-swapchain";
    public const string OpenXrStereoSwapchain = "openxr-stereo-swapchain";
}

public static class RekallAgeVulkanSceneSwapchainFormatMapper
{
    private const long VkFormatR8G8B8A8Unorm = 37;
    private const long VkFormatR8G8B8A8Srgb = 43;
    private const long VkFormatB8G8R8A8Unorm = 44;
    private const long VkFormatB8G8R8A8Srgb = 50;

    public static bool TryMapColorFormat(long formatCode, out Format format)
    {
        format = formatCode switch
        {
            VkFormatR8G8B8A8Unorm => Format.R8G8B8A8Unorm,
            VkFormatR8G8B8A8Srgb => Format.R8G8B8A8Srgb,
            VkFormatB8G8R8A8Unorm => Format.B8G8R8A8Unorm,
            VkFormatB8G8R8A8Srgb => Format.B8G8R8A8Srgb,
            _ => default
        };
        return format != default;
    }
}

public sealed record RekallAgeVulkanSceneRenderBackendPlan(
    RekallAgeVulkanSceneRenderTarget Target,
    RekallAgeVulkanSceneResourceOwnershipPlan Ownership,
    RekallAgeVulkanSceneFramebufferPlan Framebuffers,
    RekallAgeVulkanSceneCommandSubmissionPlan CommandSubmission,
    bool CanUseNativeScenePipeline,
    bool RequiresOpenXrOwnedVulkanDevice,
    bool RequiresPerEyeProjection,
    bool RequiresArrayLayerFramebuffers,
    bool RequiresReadback,
    IReadOnlyList<string> RequiredSteps,
    IReadOnlyList<string> Blockers);

public sealed record RekallAgeVulkanSceneCommandSubmissionPlan(
    uint RenderPassesPerFrame,
    uint DrawSubmissionsPerRenderPass,
    uint FrameUniformBufferCount,
    bool CopiesColorToReadback,
    bool LeavesColorForCompositor,
    bool RequiresExternalAcquireRelease)
{
    public static RekallAgeVulkanSceneCommandSubmissionPlan ForTarget(RekallAgeVulkanSceneRenderTarget target)
    {
        if (target.IsOpenXrStereoSwapchain)
        {
            return new RekallAgeVulkanSceneCommandSubmissionPlan(
                target.EyeCount,
                1,
                target.EyeCount,
                false,
                true,
                true);
        }

        return new RekallAgeVulkanSceneCommandSubmissionPlan(
            1,
            1,
            1,
            true,
            false,
            false);
    }
}

public sealed record RekallAgeVulkanSceneFramebufferPlan(
    uint ColorImageLayerCount,
    uint DepthImageLayerCount,
    uint ColorImageViewCountPerSwapchainImage,
    uint DepthImageViewCountPerSwapchainImage,
    uint FramebufferCountPerSwapchainImage,
    uint FramebufferLayers,
    bool UsesPerEyeLayerViews)
{
    public static RekallAgeVulkanSceneFramebufferPlan ForTarget(RekallAgeVulkanSceneRenderTarget target)
    {
        if (target.IsOpenXrStereoSwapchain)
        {
            return new RekallAgeVulkanSceneFramebufferPlan(
                target.EyeCount,
                target.EyeCount,
                target.EyeCount,
                target.EyeCount,
                target.EyeCount,
                1,
                true);
        }

        return new RekallAgeVulkanSceneFramebufferPlan(
            1,
            1,
            1,
            1,
            1,
            1,
            false);
    }
}

public sealed record RekallAgeVulkanSceneResourceOwnershipPlan(
    bool OwnsVulkanInstance,
    bool OwnsVulkanDevice,
    bool OwnsColorImages,
    bool OwnsDepthImages,
    bool OwnsImageViews,
    bool OwnsFramebuffers,
    bool OwnsReadbackBuffers,
    string SynchronizationOwner)
{
    public static RekallAgeVulkanSceneResourceOwnershipPlan ForTarget(RekallAgeVulkanSceneRenderTarget target)
    {
        if (target.IsOpenXrStereoSwapchain)
        {
            return new RekallAgeVulkanSceneResourceOwnershipPlan(
                OwnsVulkanInstance: false,
                OwnsVulkanDevice: false,
                OwnsColorImages: false,
                OwnsDepthImages: true,
                OwnsImageViews: true,
                OwnsFramebuffers: true,
                OwnsReadbackBuffers: false,
                SynchronizationOwner: "openxr-frame-loop");
        }

        return new RekallAgeVulkanSceneResourceOwnershipPlan(
            OwnsVulkanInstance: true,
            OwnsVulkanDevice: true,
            OwnsColorImages: true,
            OwnsDepthImages: true,
            OwnsImageViews: true,
            OwnsFramebuffers: true,
            OwnsReadbackBuffers: true,
            SynchronizationOwner: "renderer-submit-and-wait");
    }
}

public static class RekallAgeVulkanSceneRenderBackendPlanner
{
    public static RekallAgeVulkanSceneRenderBackendPlan Plan(RekallAgeVulkanSceneRenderTarget target)
    {
        var blockers = new List<string>();
        if (target.Width == 0 || target.Height == 0)
        {
            blockers.Add("Render target dimensions must be non-zero.");
        }

        if (target.IsOpenXrStereoSwapchain && target.FinalColorLayout != ImageLayout.ColorAttachmentOptimal)
        {
            blockers.Add("OpenXR compositor images must remain in color-attachment layout after scene rendering.");
        }

        if (target.Kind.Equals(RekallAgeVulkanSceneRenderTargetKinds.OpenXrStereoSwapchain, StringComparison.Ordinal)
            && !target.IsOpenXrStereoSwapchain)
        {
            blockers.Add("OpenXR stereo rendering requires externally owned array-layer color images with at least two eyes.");
        }

        var isOpenXr = target.IsOpenXrStereoSwapchain;
        return new RekallAgeVulkanSceneRenderBackendPlan(
            target,
            RekallAgeVulkanSceneResourceOwnershipPlan.ForTarget(target),
            RekallAgeVulkanSceneFramebufferPlan.ForTarget(target),
            RekallAgeVulkanSceneCommandSubmissionPlan.ForTarget(target),
            blockers.Count == 0,
            isOpenXr,
            isOpenXr,
            isOpenXr,
            target.Kind.Equals(RekallAgeVulkanSceneRenderTargetKinds.OffscreenCapture, StringComparison.Ordinal),
            BuildSteps(target, isOpenXr),
            blockers);
    }

    private static IReadOnlyList<string> BuildSteps(RekallAgeVulkanSceneRenderTarget target, bool isOpenXr)
    {
        if (isOpenXr)
        {
            return
            [
                "Use the OpenXR-created Vulkan instance, physical device, logical device, and graphics queue.",
                "Create per-swapchain-image, per-eye image views over the OpenXR color array layers.",
                "Create matching depth images and framebuffers for each eye layer.",
                "Update the scene frame uniform from xrLocateViews FOV and pose for each eye.",
                "Record the shared Rekall scene pipeline against the acquired OpenXR swapchain image.",
                "Leave the color image in VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL for xrEndFrame."
            ];
        }

        return
        [
            $"Create an owned {target.Width}x{target.Height} color image and depth image.",
            "Render the Rekall scene pipeline into the owned framebuffer.",
            "Copy the color image into the readback buffer for capture."
        ];
    }
}
