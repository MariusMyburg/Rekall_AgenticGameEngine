using System.Numerics;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Silk.NET.Vulkan;

namespace Rekall.Age.Tests.Rendering;

public sealed class OpenXrNativeSceneRenderPlanTests
{
    [Fact]
    public void BuilderCreatesPerEyeFrameUniformsFromLocatedOpenXrViews()
    {
        var camera = new RekallAgeRuntimeViewportCamera(
            "camera",
            "Head Camera",
            "Rekall.Camera3D",
            true,
            0,
            0,
            -4,
            0,
            0,
            0,
            FieldOfViewDegrees: 70,
            StereoMode: "stereo",
            StereoRenderMode: "single-pass-multiview");
        var frame = new RekallAgeRuntimeViewportFrame(
            "Main",
            0,
            0,
            256,
            256,
            camera,
            [camera],
            [
                new RekallAgeRuntimeViewportRenderable(
                    "cube",
                    "Cube",
                    "mesh",
                    "rekall.primitive.cube",
                    0,
                    0,
                    0,
                    0,
                    Variant: "rekall.geometry.cube")
            ],
            0,
            new RekallAgeRuntimeViewportOverlay(false, 0),
            []);
        var meshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame);
        var prepared = RekallAgeVulkanScenePreparedFrameBuilder.Build(
            frame,
            meshes,
            RekallAgeVulkanSceneRenderTarget.OpenXrStereoSwapchain(
                256,
                256,
                2,
                Format.R8G8B8A8Srgb,
                Format.D32Sfloat));
        var locatedEyes = new[]
        {
            new RekallAgeOpenXrLocatedEyeView(0, Quaternion.Identity, new Vector3(-0.032f, 0, 0), -0.45f, 0.55f, 0.50f, -0.50f),
            new RekallAgeOpenXrLocatedEyeView(1, Quaternion.Identity, new Vector3(0.032f, 0, 0), -0.55f, 0.45f, 0.50f, -0.50f)
        };

        var plan = RekallAgeOpenXrNativeSceneRenderPlanBuilder.Build(prepared, locatedEyes);

        Assert.True(plan.Ready, string.Join(" ", plan.Blockers));
        Assert.Equal(2, plan.Eyes.Count);
        Assert.Empty(plan.Blockers);
        Assert.Equal(0u, plan.Eyes[0].FramebufferIndex);
        Assert.Equal(1u, plan.Eyes[1].FramebufferIndex);
        Assert.NotEqual(plan.Eyes[0].ViewProjection, plan.Eyes[1].ViewProjection);
        Assert.Equal(plan.Eyes[0].ViewProjection.M11, plan.Eyes[0].FrameUniform.ViewProjection.M11);
        Assert.Equal(256, plan.Eyes[0].Viewport.Z);
        Assert.Equal(256, plan.Eyes[1].Viewport.W);
    }

    [Fact]
    public void BuilderBlocksNonOpenXrTargets()
    {
        var frame = new RekallAgeRuntimeViewportFrame(
            "Main",
            0,
            0,
            64,
            64,
            null,
            [],
            [],
            0,
            new RekallAgeRuntimeViewportOverlay(false, 0),
            []);
        var prepared = RekallAgeVulkanScenePreparedFrameBuilder.Build(
            frame,
            [],
            RekallAgeVulkanSceneRenderTarget.OffscreenCapture(64, 64));

        var plan = RekallAgeOpenXrNativeSceneRenderPlanBuilder.Build(prepared, []);

        Assert.False(plan.Ready);
        Assert.Contains(plan.Blockers, blocker => blocker.Contains("OpenXR stereo swapchain", StringComparison.Ordinal));
    }
}
