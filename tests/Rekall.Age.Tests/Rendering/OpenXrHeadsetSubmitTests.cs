using Rekall.Age.Rendering;

namespace Rekall.Age.Tests.Rendering;

public sealed class OpenXrHeadsetSubmitTests
{
    [Fact]
    public void HeadsetClearSubmitRequestClampsFrameCountToInteractiveSmokeRange()
    {
        var plan = RekallAgeOpenXrHeadsetSubmitPlanner.Plan(new RekallAgeOpenXrHeadsetClearSubmitRequest(FrameCount: 10_000));

        Assert.Equal(600, plan.FrameCount);
    }

    [Fact]
    public void HeadsetClearSubmitRequestUsesBrightVisibleDefaultColor()
    {
        var plan = RekallAgeOpenXrHeadsetSubmitPlanner.Plan(new RekallAgeOpenXrHeadsetClearSubmitRequest());

        Assert.True(plan.Blue > plan.Red);
        Assert.True(plan.Green > plan.Red);
        Assert.Equal(1, plan.Alpha);
    }

    [Fact]
    public void SoftwareSceneSubmitRequestClampsToBoundedInteractiveEyeTexture()
    {
        var plan = RekallAgeOpenXrHeadsetSubmitPlanner.Plan(new RekallAgeOpenXrHeadsetSoftwareSceneSubmitRequest(
            " F:/Dev/Rekall_AGE/.age-ksa-solar ",
            " Main ",
            FrameCount: 10_000,
            SimulationStartFrame: -20,
            RenderWidth: 16_384,
            RenderHeight: 32));

        Assert.Equal("F:/Dev/Rekall_AGE/.age-ksa-solar", plan.ProjectRoot);
        Assert.Equal("Main", plan.SceneName);
        Assert.Equal(10_000, plan.FrameCount);
        Assert.Equal(0, plan.SimulationStartFrame);
        Assert.Equal(8192, plan.RenderWidth);
        Assert.Equal(64, plan.RenderHeight);
    }

    [Fact]
    public void SoftwareSceneSubmitRequestCanUseRuntimeRecommendedEyeSize()
    {
        var plan = RekallAgeOpenXrHeadsetSubmitPlanner.Plan(new RekallAgeOpenXrHeadsetSoftwareSceneSubmitRequest(
            ".age-ksa-solar",
            "Main",
            RenderWidth: 0,
            RenderHeight: 0));

        Assert.Equal(RekallAgeOpenXrHeadsetSubmitPlanner.RecommendedRuntimeExtent, plan.RenderWidth);
        Assert.Equal(RekallAgeOpenXrHeadsetSubmitPlanner.RecommendedRuntimeExtent, plan.RenderHeight);
    }

    [Fact]
    public void SoftwareSceneSubmitRequestCanRunContinuouslyUntilCancelled()
    {
        var plan = RekallAgeOpenXrHeadsetSubmitPlanner.Plan(new RekallAgeOpenXrHeadsetSoftwareSceneSubmitRequest(
            ".age-ksa-solar",
            "Main",
            FrameCount: 0));

        Assert.Equal(0, plan.FrameCount);
    }
}
