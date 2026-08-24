using System.Numerics;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Silk.NET.Vulkan;

namespace Rekall.Age.Tests.Rendering;

public sealed class VulkanSceneCommandPlanTests
{
    [Fact]
    public void OffscreenCommandPlanUsesOneRenderPassAndReadbackCopy()
    {
        var prepared = CreatePreparedFrame(RekallAgeVulkanSceneRenderTarget.OffscreenCapture(128, 72));

        var plan = RekallAgeVulkanSceneCommandPlanBuilder.BuildOffscreen(prepared);

        Assert.True(plan.Ready, string.Join(" ", plan.Blockers));
        var pass = Assert.Single(plan.RenderPasses);
        Assert.Null(pass.EyeIndex);
        Assert.Equal(0u, pass.FramebufferIndex);
        Assert.Equal(128, pass.Viewport.Z);
        Assert.Equal(72, pass.Viewport.W);
        Assert.True(plan.CopiesColorToReadback);
        Assert.False(plan.LeavesColorForCompositor);
        Assert.False(plan.RequiresExternalAcquireRelease);
        Assert.Equal(1u, plan.FrameUniformBufferCount);
        Assert.Equal(prepared.DrawCount, pass.Draws.Count);
    }

    [Fact]
    public void OpenXrCommandPlanUsesOneRenderPassPerEyeAndCompositorHandoff()
    {
        var prepared = CreatePreparedFrame(
            RekallAgeVulkanSceneRenderTarget.OpenXrStereoSwapchain(
                128,
                72,
                2,
                Format.R8G8B8A8Srgb,
                Format.D32Sfloat));
        var nativePlan = RekallAgeOpenXrNativeSceneRenderPlanBuilder.Build(
            prepared,
            [
                new RekallAgeOpenXrLocatedEyeView(0, Quaternion.Identity, new Vector3(-0.03f, 0, 0), -0.4f, 0.5f, 0.45f, -0.45f),
                new RekallAgeOpenXrLocatedEyeView(1, Quaternion.Identity, new Vector3(0.03f, 0, 0), -0.5f, 0.4f, 0.45f, -0.45f)
            ]);

        var plan = RekallAgeVulkanSceneCommandPlanBuilder.BuildOpenXr(nativePlan);

        Assert.True(plan.Ready, string.Join(" ", plan.Blockers));
        Assert.Equal(2, plan.RenderPasses.Count);
        Assert.Equal(0, plan.RenderPasses[0].EyeIndex);
        Assert.Equal(1, plan.RenderPasses[1].EyeIndex);
        Assert.Equal(0u, plan.RenderPasses[0].FramebufferIndex);
        Assert.Equal(1u, plan.RenderPasses[1].FramebufferIndex);
        Assert.NotEqual(plan.RenderPasses[0].FrameUniform.ViewProjection, plan.RenderPasses[1].FrameUniform.ViewProjection);
        Assert.False(plan.CopiesColorToReadback);
        Assert.True(plan.LeavesColorForCompositor);
        Assert.True(plan.RequiresExternalAcquireRelease);
        Assert.Equal(2u, plan.FrameUniformBufferCount);
        Assert.Equal(prepared.DrawCount * 2, plan.DrawCount);
    }

    [Fact]
    public void HighFidelityOffscreenPlanCarriesOnlyExecutablePostCommandsFromValidatedGraph()
    {
        var target = RekallAgeVulkanSceneRenderTarget.HighFidelityOffscreenCapture(128, 72);
        var prepared = CreatePreparedFrame(target);
        var quality = new RekallAgeRenderQualityProfileResolver().Resolve(
            new RekallAgeRenderQualityIntent("High"),
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("test"),
            128,
            72);
        var runtimeFrame = new RekallAgeRuntimeViewportFrame(
            "Main", 0, 0, 128, 72, null, [], [], 0,
            new RekallAgeRuntimeViewportOverlay(false, 0), [])
        {
            ResolvedQualityPlan = quality
        };
        var graph = new RekallAgeHighFidelityRenderGraphBuilder().Build(runtimeFrame, quality);

        var plan = RekallAgeVulkanSceneCommandPlanBuilder.BuildOffscreen(prepared, graph);

        Assert.True(plan.Ready, string.Join(" ", plan.Blockers));
        Assert.Equal(["bloom", "tone-map", "ui", "present"], plan.PostPasses.Select(pass => pass.Name));
        Assert.True(plan.CopiesColorToReadback);
        Assert.Equal(Format.R16G16B16A16Sfloat, target.ColorFormat);
        Assert.Equal(Format.R8G8B8A8Unorm, target.EffectiveOutputColorFormat);
    }

    [Fact]
    public void HighFidelityPlanReportsEveryUnsupportedOrClampedAuthoredPostSetting()
    {
        var quality = new RekallAgeRenderQualityProfileResolver().Resolve(
            new RekallAgeRenderQualityIntent("High", Bloom: true),
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("test"),
            128,
            72);
        var frame = new RekallAgeRuntimeViewportFrame(
            "Main", 0, 0, 128, 72, null, [], [], 0,
            new RekallAgeRuntimeViewportOverlay(false, 0), [])
        {
            ResolvedQualityPlan = quality,
            PostProcessStack = new RekallAgeRuntimeViewportPostProcessStack(
                "post",
                "Authored Post",
                true,
                [
                    new RekallAgeRuntimeViewportPostProcessPass(
                        "Bloom",
                        "bloom",
                        Scale: 0.5,
                        Iterations: 3,
                        Threshold: -0.25,
                        Intensity: -2,
                        Radius: 99,
                        BlendMode: "screen"),
                    new RekallAgeRuntimeViewportPostProcessPass("Lens", "vignette"),
                    new RekallAgeRuntimeViewportPostProcessPass("Tone Map", "tone-map")
                ])
        };

        var plan = Assert.IsType<RekallAgeVulkanHighFidelityFramePlan>(
            new RekallAgeVulkanHighFidelityFrameRenderer().Plan(frame));

        AssertPostDiagnostic(plan, "REKALL_RENDER_POST_SETTING_UNSUPPORTED", "post.pass[0].scale", "0.5", "0.25");
        AssertPostDiagnostic(plan, "REKALL_RENDER_POST_SETTING_UNSUPPORTED", "post.pass[0].iterations", "3", "1");
        AssertPostDiagnostic(plan, "REKALL_RENDER_POST_SETTING_CLAMPED", "post.pass[0].threshold", "-0.25", "0");
        AssertPostDiagnostic(plan, "REKALL_RENDER_POST_SETTING_CLAMPED", "post.pass[0].intensity", "-2", "0");
        AssertPostDiagnostic(plan, "REKALL_RENDER_POST_SETTING_CLAMPED", "post.pass[0].radius", "99", "32");
        AssertPostDiagnostic(plan, "REKALL_RENDER_POST_SETTING_UNSUPPORTED", "post.pass[0].blendMode", "screen", "add");
        AssertPostDiagnostic(plan, "REKALL_RENDER_POST_PASS_UNSUPPORTED", "post.pass[1].type", "vignette", "ignored");
    }

    private static RekallAgeVulkanScenePreparedFrame CreatePreparedFrame(RekallAgeVulkanSceneRenderTarget target)
    {
        var camera = new RekallAgeRuntimeViewportCamera(
            "camera",
            "Camera",
            "Rekall.Camera3D",
            true,
            0,
            0,
            -4,
            FieldOfViewDegrees: 70,
            StereoMode: "stereo",
            StereoRenderMode: "single-pass-multiview");
        var frame = new RekallAgeRuntimeViewportFrame(
            "Main",
            0,
            0,
            checked((int)target.Width),
            checked((int)target.Height),
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
        return RekallAgeVulkanScenePreparedFrameBuilder.Build(frame, meshes, target);
    }

    private static void AssertPostDiagnostic(
        RekallAgeVulkanHighFidelityFramePlan plan,
        string code,
        string setting,
        string requested,
        string resolved)
    {
        Assert.Contains(
            plan.Graph.Diagnostics,
            diagnostic => diagnostic.Code == code
                && diagnostic.Target == setting
                && diagnostic.Message.Contains($"requested='{requested}'", StringComparison.Ordinal)
                && diagnostic.Message.Contains($"resolved='{resolved}'", StringComparison.Ordinal));
    }
}
