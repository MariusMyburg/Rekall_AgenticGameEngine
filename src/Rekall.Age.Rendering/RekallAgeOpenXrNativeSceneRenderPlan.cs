using System.Numerics;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

public sealed record RekallAgeOpenXrLocatedEyeView(
    int EyeIndex,
    Quaternion Orientation,
    Vector3 Position,
    float AngleLeft,
    float AngleRight,
    float AngleUp,
    float AngleDown);

public sealed record RekallAgeOpenXrNativeSceneEyeRenderPlan(
    int EyeIndex,
    uint FramebufferIndex,
    Matrix4x4 ViewProjection,
    Vector4 Viewport,
    RekallAgeVulkanSceneGpuFrameUniform FrameUniform);

public sealed record RekallAgeOpenXrNativeSceneRenderPlan(
    RekallAgeVulkanScenePreparedFrame PreparedFrame,
    IReadOnlyList<RekallAgeOpenXrNativeSceneEyeRenderPlan> Eyes,
    bool Ready,
    IReadOnlyList<string> Blockers);

public static class RekallAgeOpenXrNativeSceneRenderPlanBuilder
{
    public static RekallAgeOpenXrNativeSceneRenderPlan Build(
        RekallAgeVulkanScenePreparedFrame preparedFrame,
        IReadOnlyList<RekallAgeOpenXrLocatedEyeView> locatedEyes)
    {
        var blockers = new List<string>();
        if (!preparedFrame.Target.IsOpenXrStereoSwapchain)
        {
            blockers.Add("Native OpenXR scene rendering requires an OpenXR stereo swapchain render target.");
        }

        if (preparedFrame.Frame.ActiveCamera is null)
        {
            blockers.Add("Native OpenXR scene rendering requires an active camera.");
        }

        if (locatedEyes.Count < preparedFrame.Target.EyeCount)
        {
            blockers.Add($"OpenXR located {locatedEyes.Count} eye view(s), but the target requires {preparedFrame.Target.EyeCount}.");
        }

        if (!preparedFrame.HasDrawableGeometry)
        {
            blockers.Add("Native OpenXR scene rendering requires drawable scene geometry.");
        }

        if (blockers.Count > 0)
        {
            return new RekallAgeOpenXrNativeSceneRenderPlan(
                preparedFrame,
                Array.Empty<RekallAgeOpenXrNativeSceneEyeRenderPlan>(),
                false,
                blockers);
        }

        var camera = preparedFrame.Frame.ActiveCamera!;
        var renderer = new RekallAgePerspectiveSoftwareSceneRenderer();
        var eyes = locatedEyes
            .OrderBy(eye => eye.EyeIndex)
            .Take(checked((int)preparedFrame.Target.EyeCount))
            .Select(eye =>
            {
                var viewProjection = renderer.CreateCameraViewProjection(
                    camera,
                    checked((int)preparedFrame.Target.Width),
                    checked((int)preparedFrame.Target.Height),
                    eye.Orientation,
                    eye.Position,
                    eye.AngleLeft,
                    eye.AngleRight,
                    eye.AngleUp,
                    eye.AngleDown);
                return new RekallAgeOpenXrNativeSceneEyeRenderPlan(
                    eye.EyeIndex,
                    checked((uint)eye.EyeIndex),
                    viewProjection,
                    new Vector4(0, 0, preparedFrame.Target.Width, preparedFrame.Target.Height),
                    RekallAgeVulkanSceneUniformUploadBuilder.BuildFrameUniform(
                        preparedFrame.Batch.Frame with { ViewProjection = viewProjection }));
            })
            .ToArray();

        return new RekallAgeOpenXrNativeSceneRenderPlan(
            preparedFrame,
            eyes,
            true,
            Array.Empty<string>());
    }
}
