using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Rendering;

public sealed class OpenXrHeadsetFramePlanCommandTests
{
    [Fact]
    public async Task InspectHeadsetFramePlanReportsPrimaryStereoSwapchainPlan()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d", "vr"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject { ["z"] = 8 }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["stereoMode"] = "stereo",
                    ["stereoRenderMode"] = "single-pass-multiview",
                    ["xrViewConfiguration"] = "primary-stereo"
                })))
            .AddEntity(RekallAgeEntityDocument.Create("Cube", ["prop"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);
        var context = new RekallAgeCommandContext(
            "test",
            RekallAgeTransaction.Begin("inspect openxr frame plan"),
            CancellationToken.None);

        var result = await new InspectOpenXrHeadsetFramePlanCommand(ReadyBootstrap()).ExecuteAsync(
            new InspectOpenXrHeadsetFramePlanRequest(root, "Main", Width: 2048, Height: 1024),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.HeadsetSessionReady);
        Assert.True(result.Value.StereoEnabled);
        Assert.True(result.Value.UsesMultiview);
        Assert.Equal("primary-stereo", result.Value.ViewConfiguration);
        Assert.Equal(2, result.Value.EyeCount);
        Assert.Equal(1, result.Value.ColorSwapchainCount);
        Assert.Equal(1, result.Value.DepthSwapchainCount);
        Assert.Equal(2, result.Value.SwapchainArraySize);
        Assert.Equal(1024, result.Value.RecommendedEyeWidth);
        Assert.Equal(1024, result.Value.RecommendedEyeHeight);
        Assert.Contains(result.Value.RequiredOpenXrCalls, call => call == "xrWaitFrame");
        Assert.Contains(result.Value.RequiredOpenXrCalls, call => call == "xrEndFrame");
        Assert.Contains(result.Value.FrameLoopSteps, step => step.Contains("xrLocateViews", StringComparison.Ordinal));
        Assert.Empty(result.Value.Blockers);
    }

    [Fact]
    public async Task InspectHeadsetFramePlanBlocksMonoSceneUntilStereoCameraExists()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Cube", ["prop"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);
        var context = new RekallAgeCommandContext(
            "test",
            RekallAgeTransaction.Begin("inspect mono openxr frame plan"),
            CancellationToken.None);

        var result = await new InspectOpenXrHeadsetFramePlanCommand(ReadyBootstrap()).ExecuteAsync(
            new InspectOpenXrHeadsetFramePlanRequest(root, "Main"),
            context);

        Assert.False(result.Ok);
        Assert.True(result.Value.HeadsetSessionReady);
        Assert.False(result.Value.StereoEnabled);
        Assert.Contains(result.Value.Blockers, blocker => blocker.Contains("StereoMode=stereo", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Code == "REKALL_OPENXR_FRAME_PLAN_NOT_READY");
    }

    private static IRekallAgeOpenXrSessionBootstrap ReadyBootstrap()
    {
        return new FakeOpenXrSessionBootstrap(new RekallAgeOpenXrSessionBootstrapResult(
            true,
            true,
            true,
            true,
            42,
            true,
            true,
            true,
            ["XR_KHR_vulkan_enable2"],
            ["XR_KHR_vulkan_enable2"],
            [],
            [],
            []));
    }

    private sealed class FakeOpenXrSessionBootstrap : IRekallAgeOpenXrSessionBootstrap
    {
        private readonly RekallAgeOpenXrSessionBootstrapResult _result;

        public FakeOpenXrSessionBootstrap(RekallAgeOpenXrSessionBootstrapResult result)
        {
            _result = result;
        }

        public ValueTask<RekallAgeOpenXrSessionBootstrapResult> BootstrapAsync(CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(_result);
        }
    }
}
