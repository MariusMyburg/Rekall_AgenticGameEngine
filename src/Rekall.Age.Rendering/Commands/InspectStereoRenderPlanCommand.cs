using Rekall.Age.Core.Commands;
using Rekall.Age.Runtime;

namespace Rekall.Age.Rendering.Commands;

public sealed record InspectStereoRenderPlanRequest(
    string ProjectRoot,
    string SceneName,
    int Frames = 0,
    int Width = 1920,
    int Height = 1080,
    bool DebugOverlay = false);

public sealed record InspectStereoRenderPlanResult(
    string SceneName,
    int FrameIndex,
    string? ActiveCamera,
    bool StereoEnabled,
    string RenderMode,
    int EyeCount,
    bool PreferSinglePassMultiview,
    bool SharedGeometryBuffers,
    int VertexCount,
    int IndexCount,
    int DrawCount,
    int EyeUniformCount,
    int CurrentPreviewDrawSubmissions,
    int TargetMultiviewDrawSubmissions,
    IReadOnlyList<StereoRenderEyePlan> Eyes,
    IReadOnlyList<string> Recommendations,
    IReadOnlyList<string> Warnings);

public sealed record StereoRenderEyePlan(
    string Name,
    int Index,
    double OffsetX,
    double ViewportX,
    double ViewportY,
    double ViewportWidth,
    double ViewportHeight);

public sealed class InspectStereoRenderPlanCommand
    : IRekallAgeCommand<InspectStereoRenderPlanRequest, InspectStereoRenderPlanResult>
{
    public string Name => "rekall.render.stereo.inspect_plan";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Inspects the stereo/VR render plan for a scene, including eye uniforms and multiview readiness.",
        typeof(InspectStereoRenderPlanRequest).FullName!,
        typeof(InspectStereoRenderPlanResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<InspectStereoRenderPlanResult>> ExecuteAsync(
        InspectStereoRenderPlanRequest request,
        RekallAgeCommandContext context)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return RekallAgeCommandResult<InspectStereoRenderPlanResult>.Failure(
                Empty(request),
                "Stereo render plan inspection requires non-negative frames and positive dimensions.",
                errors);
        }

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
        var eyeUniformCount = batch.Stereo?.Views.Count ?? 0;
        var stereoEnabled = stereo is { Enabled: true };
        var renderMode = stereo?.RenderMode ?? "mono";
        var eyeCount = stereo?.EyeCount ?? 1;
        var preferSinglePassMultiview = stereo?.PreferSinglePassMultiview ?? false;
        var recommendations = BuildRecommendations(stereoEnabled, renderMode, preferSinglePassMultiview);
        var warnings = BuildWarnings(frame, meshes.Count, eyeUniformCount);
        var result = new InspectStereoRenderPlanResult(
            world.SceneName,
            world.FrameIndex,
            frame.ActiveCamera?.EntityName,
            stereoEnabled,
            renderMode,
            eyeCount,
            preferSinglePassMultiview,
            true,
            batch.Vertices.Count,
            batch.Indices.Count,
            batch.Draws.Count,
            eyeUniformCount,
            stereoEnabled ? batch.Draws.Count * Math.Max(1, eyeCount) : batch.Draws.Count,
            preferSinglePassMultiview ? batch.Draws.Count : batch.Draws.Count * Math.Max(1, eyeCount),
            (stereo?.Eyes ?? [])
                .Select(eye => new StereoRenderEyePlan(
                    eye.Name,
                    eye.Index,
                    eye.OffsetX,
                    eye.ViewportX,
                    eye.ViewportY,
                    eye.ViewportWidth,
                    eye.ViewportHeight))
                .ToArray(),
            recommendations,
            warnings);

        return RekallAgeCommandResult<InspectStereoRenderPlanResult>.Success(
            result,
            stereoEnabled
                ? $"Stereo render plan for '{request.SceneName}' uses {result.EyeCount} eyes and {result.RenderMode}."
                : $"Scene '{request.SceneName}' is currently mono.");
    }

    private static IReadOnlyList<string> BuildRecommendations(
        bool stereoEnabled,
        string renderMode,
        bool preferSinglePassMultiview)
    {
        if (!stereoEnabled)
        {
            return
            [
                "Enable Camera3D.StereoMode=stereo for VR or stereoscopic rendering.",
                "Use single-pass-multiview for headset rendering when the backend supports Vulkan multiview."
            ];
        }

        var recommendations = new List<string>
        {
            "Keep mesh, material, and texture buffers shared across eyes.",
            "Update only per-eye frame uniforms between views."
        };
        if (preferSinglePassMultiview)
        {
            recommendations.Add("Use Vulkan multiview/OpenXR array swapchains for final headset output.");
        }
        else if (renderMode.Equals("side-by-side", StringComparison.Ordinal))
        {
            recommendations.Add("Side-by-side is useful for desktop preview; prefer single-pass-multiview for headset output.");
        }
        else
        {
            recommendations.Add("Dual-pass is the compatibility path; prefer single-pass-multiview when available.");
        }

        return recommendations;
    }

    private static IReadOnlyList<string> BuildWarnings(
        Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportFrame frame,
        int meshCount,
        int eyeUniformCount)
    {
        var warnings = new List<string>();
        if (frame.ActiveCamera is null)
        {
            warnings.Add("No active camera is available.");
        }

        if (frame.Stereo is { Enabled: true } && eyeUniformCount < 2)
        {
            warnings.Add("Stereo is enabled but fewer than two eye uniforms were generated.");
        }

        if (frame.Stereo is { Enabled: true, PreferSinglePassMultiview: true } && meshCount == 0)
        {
            warnings.Add("Stereo multiview is enabled but no meshes were generated for the frame.");
        }

        return warnings;
    }

    private static IReadOnlyList<RekallAgeCommandError> Validate(InspectStereoRenderPlanRequest request)
    {
        var errors = new List<RekallAgeCommandError>();
        if (request.Frames < 0)
        {
            errors.Add(new RekallAgeCommandError(
                "REKALL_STEREO_PLAN_INVALID_REQUEST",
                "Frame count cannot be negative.",
                request.SceneName));
        }

        if (request.Width <= 0 || request.Height <= 0)
        {
            errors.Add(new RekallAgeCommandError(
                "REKALL_STEREO_PLAN_INVALID_REQUEST",
                "Stereo render plan dimensions must be positive.",
                $"{request.Width}x{request.Height}"));
        }

        return errors;
    }

    private static InspectStereoRenderPlanResult Empty(InspectStereoRenderPlanRequest request)
    {
        return new InspectStereoRenderPlanResult(
            request.SceneName,
            Math.Max(0, request.Frames),
            null,
            false,
            "invalid",
            0,
            false,
            false,
            0,
            0,
            0,
            0,
            0,
            0,
            [],
            [],
            []);
    }
}
