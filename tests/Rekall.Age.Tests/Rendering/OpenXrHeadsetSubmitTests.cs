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
}

