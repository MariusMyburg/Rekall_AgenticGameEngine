using System.Text.Json.Nodes;
using Rekall.Age.Rendering;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Rendering;

public sealed class OpenXrSoftwareSceneFrameSourceTests
{
    [Fact]
    public async Task FrameSourceAdvancesRuntimeWorldBetweenHeadsetFrames()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d", "vr"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject { ["z"] = -6 }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["stereoMode"] = "stereo"
                })))
            .AddEntity(RekallAgeEntityDocument.Create("Cube", ["prop"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject()))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.TransformAnimation",
                    new JsonObject { ["yawDegreesPerSecond"] = 90 })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);
        var plan = new RekallAgeOpenXrHeadsetSoftwareSceneSubmitPlan(
            root,
            "Main",
            2,
            0,
            128,
            72,
            false);
        var source = await new RekallAgeOpenXrSoftwareSceneFrameRenderer()
            .CreateFrameSourceAsync(plan, CancellationToken.None);

        var first = source.BuildCurrentFrame();
        var second = await source.AdvanceByAsync(TimeSpan.FromSeconds(1.0 / 60.0), CancellationToken.None);

        Assert.Equal(0, first.Frame.FrameIndex);
        Assert.Equal(1, second.Frame.FrameIndex);
        Assert.NotEqual(first.Batch.Draws.Single().Model, second.Batch.Draws.Single().Model);
        Assert.True(second.PreparedFrame.Target.IsOpenXrStereoSwapchain);
        Assert.Equal(second.Batch, second.PreparedFrame.Batch);
    }

    [Fact]
    public async Task FrameSourceCatchesSimulationUpToWallClockWhenHeadsetRenderingIsSlowerThanSimulation()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d", "vr"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["stereoMode"] = "stereo"
                })))
            .AddEntity(RekallAgeEntityDocument.Create("Cube", ["prop"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject()))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.TransformAnimation",
                    new JsonObject { ["yawDegreesPerSecond"] = 90 })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);
        var plan = new RekallAgeOpenXrHeadsetSoftwareSceneSubmitPlan(root, "Main", 2, 0, 128, 72, false);
        var source = await new RekallAgeOpenXrSoftwareSceneFrameRenderer()
            .CreateFrameSourceAsync(plan, CancellationToken.None);

        var frame = await source.AdvanceByAsync(TimeSpan.FromSeconds(0.25), CancellationToken.None);

        Assert.Equal(15, frame.Frame.FrameIndex);
        Assert.Equal(0.25, frame.Frame.ElapsedSeconds, precision: 5);
    }

    [Fact]
    public async Task FrameSourceCarriesSelectedOpenXrSwapchainColorFormat()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d", "vr"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["stereoMode"] = "stereo"
                })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);
        var plan = new RekallAgeOpenXrHeadsetSoftwareSceneSubmitPlan(root, "Main", 1, 0, 128, 72, false);

        var source = await new RekallAgeOpenXrSoftwareSceneFrameRenderer()
            .CreateFrameSourceAsync(plan, CancellationToken.None, Silk.NET.Vulkan.Format.B8G8R8A8Srgb);
        var frame = source.BuildCurrentFrame();

        Assert.Equal(Silk.NET.Vulkan.Format.B8G8R8A8Srgb, frame.PreparedFrame.Target.ColorFormat);
    }
}
