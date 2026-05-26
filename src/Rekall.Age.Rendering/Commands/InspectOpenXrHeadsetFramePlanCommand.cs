using Rekall.Age.Core.Commands;
using Rekall.Age.Runtime;

namespace Rekall.Age.Rendering.Commands;

public sealed record InspectOpenXrHeadsetFramePlanRequest(
    string ProjectRoot,
    string SceneName,
    int Frames = 0,
    int Width = 1920,
    int Height = 1080,
    bool DebugOverlay = false);

public sealed record InspectOpenXrHeadsetFramePlanResult(
    string SceneName,
    int FrameIndex,
    bool HeadsetSessionReady,
    bool HmdSystemAvailable,
    ulong? SystemId,
    string? ActiveCamera,
    bool StereoEnabled,
    string StereoRenderMode,
    string ViewConfiguration,
    int EyeCount,
    bool UsesMultiview,
    bool SharedGeometryBuffers,
    int VertexCount,
    int IndexCount,
    int DrawCount,
    int ColorSwapchainCount,
    int DepthSwapchainCount,
    int SwapchainArraySize,
    int RecommendedEyeWidth,
    int RecommendedEyeHeight,
    string RecommendedColorFormat,
    string RecommendedDepthFormat,
    string NativeVulkanTargetKind,
    bool NativeVulkanSceneTargetReady,
    string NativeVulkanSynchronizationOwner,
    bool NativeVulkanOwnsColorImages,
    bool NativeVulkanOwnsDepthImages,
    bool NativeVulkanOwnsReadbackBuffers,
    int NativeVulkanFramebufferCountPerSwapchainImage,
    int NativeVulkanColorImageViewCountPerSwapchainImage,
    int NativeVulkanRenderPassesPerFrame,
    int NativeVulkanFrameUniformBuffers,
    bool NativeVulkanLeavesColorForCompositor,
    IReadOnlyList<string> NativeVulkanRenderSteps,
    IReadOnlyList<string> RequiredOpenXrCalls,
    IReadOnlyList<string> FrameLoopSteps,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings);

public sealed class InspectOpenXrHeadsetFramePlanCommand
    : IRekallAgeCommand<InspectOpenXrHeadsetFramePlanRequest, InspectOpenXrHeadsetFramePlanResult>
{
    private static readonly string[] RequiredOpenXrCalls =
    [
        "xrCreateInstance",
        "xrGetSystem",
        "xrCreateSession",
        "xrEnumerateViewConfigurationViews",
        "xrCreateSwapchain",
        "xrWaitFrame",
        "xrBeginFrame",
        "xrLocateViews",
        "xrAcquireSwapchainImage",
        "xrWaitSwapchainImage",
        "xrReleaseSwapchainImage",
        "xrEndFrame"
    ];

    private readonly IRekallAgeOpenXrSessionBootstrap _sessionBootstrap;

    public InspectOpenXrHeadsetFramePlanCommand()
        : this(new RekallAgeNativeOpenXrSessionBootstrap())
    {
    }

    public InspectOpenXrHeadsetFramePlanCommand(IRekallAgeOpenXrSessionBootstrap sessionBootstrap)
    {
        _sessionBootstrap = sessionBootstrap;
    }

    public string Name => "rekall.render.openxr.inspect_headset_frame_plan";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Inspects the OpenXR headset frame plan for a scene, including stereo swapchains and compositor frame-loop calls.",
        typeof(InspectOpenXrHeadsetFramePlanRequest).FullName!,
        typeof(InspectOpenXrHeadsetFramePlanResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<InspectOpenXrHeadsetFramePlanResult>> ExecuteAsync(
        InspectOpenXrHeadsetFramePlanRequest request,
        RekallAgeCommandContext context)
    {
        var validationErrors = Validate(request);
        if (validationErrors.Count > 0)
        {
            return RekallAgeCommandResult<InspectOpenXrHeadsetFramePlanResult>.Failure(
                Empty(request),
                "OpenXR headset frame plan inspection requires non-negative frames and positive dimensions.",
                validationErrors);
        }

        var session = await _sessionBootstrap.BootstrapAsync(context.CancellationToken).ConfigureAwait(false);
        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            request.ProjectRoot,
            request.SceneName,
            request.Frames,
            context.CancellationToken).ConfigureAwait(false);
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(
            world,
            request.Width,
            request.Height,
            request.DebugOverlay);
        var renderFrame = frame.ForHeadsetOutput();
        var assets = await new RekallAgeRuntimeViewportAssetResolver().ResolveAsync(
            request.ProjectRoot,
            renderFrame,
            context.CancellationToken).ConfigureAwait(false);
        var meshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(renderFrame, assets);
        var batch = new RekallAgeVulkanSceneBatchBuilder().Build(renderFrame, meshes);
        var stereo = renderFrame.Stereo;
        var stereoEnabled = stereo is { Enabled: true };
        var eyeCount = stereo?.EyeCount ?? 1;
        var usesMultiview = stereoEnabled && (stereo?.PreferSinglePassMultiview ?? false);
        var firstRuntimeEye = session.PrimaryStereoViews.FirstOrDefault();
        var recommendedEyeWidth = firstRuntimeEye is not null
            ? checked((int)firstRuntimeEye.RecommendedImageRectWidth)
            : usesMultiview ? request.Width / Math.Max(1, eyeCount) : request.Width;
        var recommendedEyeHeight = firstRuntimeEye is not null
            ? checked((int)firstRuntimeEye.RecommendedImageRectHeight)
            : request.Height;
        var blockers = BuildBlockers(session, stereoEnabled, eyeCount, usesMultiview).ToList();
        var warnings = BuildWarnings(frame, meshes.Count, batch.Draws.Count, usesMultiview);
        var nativeTargetPlan = RekallAgeVulkanSceneRenderBackendPlanner.Plan(
            RekallAgeVulkanSceneRenderTarget.OpenXrStereoSwapchain(
                checked((uint)Math.Max(1, recommendedEyeWidth)),
                checked((uint)Math.Max(1, recommendedEyeHeight)),
                checked((uint)Math.Max(2, eyeCount)),
                Silk.NET.Vulkan.Format.R8G8B8A8Srgb,
                Silk.NET.Vulkan.Format.D32Sfloat));
        blockers.AddRange(nativeTargetPlan.Blockers);
        var result = new InspectOpenXrHeadsetFramePlanResult(
            world.SceneName,
            world.FrameIndex,
            session.HeadsetSessionReady,
            session.HmdSystemAvailable,
            session.SystemId,
            renderFrame.ActiveCamera?.EntityName,
            stereoEnabled,
            stereo?.RenderMode ?? "mono",
            renderFrame.ActiveCamera?.XrViewConfiguration ?? "primary-stereo",
            eyeCount,
            usesMultiview,
            true,
            batch.Vertices.Count,
            batch.Indices.Count,
            batch.Draws.Count,
            usesMultiview ? 1 : Math.Max(1, eyeCount),
            usesMultiview ? 1 : Math.Max(1, eyeCount),
            usesMultiview ? Math.Max(1, eyeCount) : 1,
            recommendedEyeWidth,
            recommendedEyeHeight,
            "R8G8B8A8_SRGB",
            "D32_SFLOAT",
            nativeTargetPlan.Target.Kind,
            nativeTargetPlan.CanUseNativeScenePipeline,
            nativeTargetPlan.Ownership.SynchronizationOwner,
            nativeTargetPlan.Ownership.OwnsColorImages,
            nativeTargetPlan.Ownership.OwnsDepthImages,
            nativeTargetPlan.Ownership.OwnsReadbackBuffers,
            checked((int)nativeTargetPlan.Framebuffers.FramebufferCountPerSwapchainImage),
            checked((int)nativeTargetPlan.Framebuffers.ColorImageViewCountPerSwapchainImage),
            checked((int)nativeTargetPlan.CommandSubmission.RenderPassesPerFrame),
            checked((int)nativeTargetPlan.CommandSubmission.FrameUniformBufferCount),
            nativeTargetPlan.CommandSubmission.LeavesColorForCompositor,
            nativeTargetPlan.RequiredSteps,
            RequiredOpenXrCalls,
            BuildFrameLoopSteps(usesMultiview, eyeCount, nativeTargetPlan.CommandSubmission.RenderPassesPerFrame),
            blockers,
            warnings);

        if (blockers.Count == 0)
        {
            return RekallAgeCommandResult<InspectOpenXrHeadsetFramePlanResult>.Success(
                result,
                $"Scene '{request.SceneName}' is ready for OpenXR primary-stereo frame submission planning.");
        }

        return RekallAgeCommandResult<InspectOpenXrHeadsetFramePlanResult>.Failure(
            result,
            "OpenXR headset frame plan is not ready.",
            [
                new RekallAgeCommandError(
                    "REKALL_OPENXR_FRAME_PLAN_NOT_READY",
                    string.Join(" ", blockers),
                    request.SceneName)
            ]);
    }

    private static IReadOnlyList<string> BuildBlockers(
        RekallAgeOpenXrSessionBootstrapResult session,
        bool stereoEnabled,
        int eyeCount,
        bool usesMultiview)
    {
        var blockers = new List<string>();
        if (!session.HeadsetSessionReady)
        {
            blockers.AddRange(session.Errors.Count > 0 ? session.Errors : session.NextRenderSteps);
        }

        if (!stereoEnabled)
        {
            blockers.Add("Enable Camera3D.StereoMode=stereo on the active camera.");
        }
        else if (eyeCount < 2)
        {
            blockers.Add("OpenXR primary-stereo rendering requires two eye views.");
        }

        if (stereoEnabled && !usesMultiview)
        {
            blockers.Add("Use Camera3D.StereoRenderMode=single-pass-multiview for performant OpenXR headset output.");
        }

        return blockers;
    }

    private static IReadOnlyList<string> BuildWarnings(
        Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportFrame frame,
        int meshCount,
        int drawCount,
        bool usesMultiview)
    {
        var warnings = new List<string>();
        if (meshCount == 0 || drawCount == 0)
        {
            warnings.Add("The frame plan has no drawable meshes.");
        }

        if (!usesMultiview)
        {
            warnings.Add("Non-multiview headset rendering duplicates draw submission work per eye.");
        }

        var activeStereoCameras = frame.Cameras
            .Where(camera => camera.Active && IsStereoMode(camera.StereoMode))
            .OrderBy(camera => camera.RenderOrder)
            .ThenBy(camera => camera.EntityName, StringComparer.Ordinal)
            .ToArray();
        if (activeStereoCameras.Length > 1)
        {
            warnings.Add(
                $"Scene has multiple active stereo cameras ({string.Join(", ", activeStereoCameras.Select(camera => camera.EntityName))}); OpenXR headset output uses '{frame.HeadsetCamera?.EntityName ?? "none"}'. Disable extra stereo cameras or make spectator cameras mono.");
        }

        return warnings;
    }

    private static bool IsStereoMode(string? stereoMode)
    {
        return string.Equals(stereoMode, "stereo", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(stereoMode, "vr", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(stereoMode, "xr", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> BuildFrameLoopSteps(bool usesMultiview, int eyeCount, uint renderPassesPerFrame)
    {
        var swapchainDescription = usesMultiview
            ? $"Acquire one color/depth array swapchain image with array size {Math.Max(1, eyeCount)}."
            : $"Acquire {Math.Max(1, eyeCount)} color/depth swapchain image pairs.";
        var renderDescription = renderPassesPerFrame <= 1
            ? "Render scene geometry with a single native Vulkan pass."
            : $"Render scene geometry into {renderPassesPerFrame} eye layers with shared geometry buffers and per-eye frame uniforms.";
        return
        [
            "Poll OpenXR events and react to session state changes.",
            "Call xrWaitFrame and xrBeginFrame.",
            "Call xrLocateViews for XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO.",
            swapchainDescription,
            renderDescription,
            "Release swapchain images.",
            "Submit an XrCompositionLayerProjection with one projection view per eye via xrEndFrame."
        ];
    }

    private static IReadOnlyList<RekallAgeCommandError> Validate(InspectOpenXrHeadsetFramePlanRequest request)
    {
        var errors = new List<RekallAgeCommandError>();
        if (request.Frames < 0)
        {
            errors.Add(new RekallAgeCommandError(
                "REKALL_OPENXR_FRAME_PLAN_INVALID_REQUEST",
                "Frame count cannot be negative.",
                request.SceneName));
        }

        if (request.Width <= 0 || request.Height <= 0)
        {
            errors.Add(new RekallAgeCommandError(
                "REKALL_OPENXR_FRAME_PLAN_INVALID_REQUEST",
                "OpenXR headset frame plan dimensions must be positive.",
                $"{request.Width}x{request.Height}"));
        }

        return errors;
    }

    private static InspectOpenXrHeadsetFramePlanResult Empty(InspectOpenXrHeadsetFramePlanRequest request)
    {
        return new InspectOpenXrHeadsetFramePlanResult(
            SceneName: request.SceneName,
            FrameIndex: Math.Max(0, request.Frames),
            HeadsetSessionReady: false,
            HmdSystemAvailable: false,
            SystemId: null,
            ActiveCamera: null,
            StereoEnabled: false,
            StereoRenderMode: "invalid",
            ViewConfiguration: "primary-stereo",
            EyeCount: 0,
            UsesMultiview: false,
            SharedGeometryBuffers: false,
            VertexCount: 0,
            IndexCount: 0,
            DrawCount: 0,
            ColorSwapchainCount: 0,
            DepthSwapchainCount: 0,
            SwapchainArraySize: 0,
            RecommendedEyeWidth: 0,
            RecommendedEyeHeight: 0,
            RecommendedColorFormat: "unknown",
            RecommendedDepthFormat: "unknown",
            NativeVulkanTargetKind: "invalid",
            NativeVulkanSceneTargetReady: false,
            NativeVulkanSynchronizationOwner: "unknown",
            NativeVulkanOwnsColorImages: false,
            NativeVulkanOwnsDepthImages: false,
            NativeVulkanOwnsReadbackBuffers: false,
            NativeVulkanFramebufferCountPerSwapchainImage: 0,
            NativeVulkanColorImageViewCountPerSwapchainImage: 0,
            NativeVulkanRenderPassesPerFrame: 0,
            NativeVulkanFrameUniformBuffers: 0,
            NativeVulkanLeavesColorForCompositor: false,
            NativeVulkanRenderSteps: [],
            RequiredOpenXrCalls: [],
            FrameLoopSteps: [],
            Blockers: [],
            Warnings: []);
    }
}
