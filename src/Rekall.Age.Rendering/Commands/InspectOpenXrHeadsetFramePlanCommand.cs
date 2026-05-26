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
        var assets = await new RekallAgeRuntimeViewportAssetResolver().ResolveAsync(
            request.ProjectRoot,
            frame,
            context.CancellationToken).ConfigureAwait(false);
        var meshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame, assets);
        var batch = new RekallAgeVulkanSceneBatchBuilder().Build(frame, meshes);
        var stereo = frame.Stereo;
        var stereoEnabled = stereo is { Enabled: true };
        var eyeCount = stereo?.EyeCount ?? 1;
        var usesMultiview = stereoEnabled && (stereo?.PreferSinglePassMultiview ?? false);
        var recommendedEyeWidth = usesMultiview ? request.Width / Math.Max(1, eyeCount) : request.Width;
        var recommendedEyeHeight = request.Height;
        var blockers = BuildBlockers(session, stereoEnabled, eyeCount, usesMultiview);
        var warnings = BuildWarnings(meshes.Count, batch.Draws.Count, usesMultiview);
        var result = new InspectOpenXrHeadsetFramePlanResult(
            world.SceneName,
            world.FrameIndex,
            session.HeadsetSessionReady,
            session.HmdSystemAvailable,
            session.SystemId,
            frame.ActiveCamera?.EntityName,
            stereoEnabled,
            stereo?.RenderMode ?? "mono",
            frame.ActiveCamera?.XrViewConfiguration ?? "primary-stereo",
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
            RequiredOpenXrCalls,
            BuildFrameLoopSteps(usesMultiview, eyeCount),
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

    private static IReadOnlyList<string> BuildWarnings(int meshCount, int drawCount, bool usesMultiview)
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

        return warnings;
    }

    private static IReadOnlyList<string> BuildFrameLoopSteps(bool usesMultiview, int eyeCount)
    {
        var swapchainDescription = usesMultiview
            ? $"Acquire one color/depth array swapchain image with array size {Math.Max(1, eyeCount)}."
            : $"Acquire {Math.Max(1, eyeCount)} color/depth swapchain image pairs.";
        return
        [
            "Poll OpenXR events and react to session state changes.",
            "Call xrWaitFrame and xrBeginFrame.",
            "Call xrLocateViews for XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO.",
            swapchainDescription,
            "Render scene geometry once with multiview eye uniforms when available.",
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
            request.SceneName,
            Math.Max(0, request.Frames),
            false,
            false,
            null,
            null,
            false,
            "invalid",
            "primary-stereo",
            0,
            false,
            false,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            "unknown",
            "unknown",
            [],
            [],
            [],
            []);
    }
}
