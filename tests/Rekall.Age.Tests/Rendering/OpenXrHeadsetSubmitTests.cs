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
    public void SoftwareSceneSubmitRequestClampsToBoundedInteractiveTexture()
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
        Assert.Equal(600, plan.FrameCount);
        Assert.Equal(0, plan.SimulationStartFrame);
        Assert.Equal(4096, plan.RenderWidth);
        Assert.Equal(64, plan.RenderHeight);
    }
}
