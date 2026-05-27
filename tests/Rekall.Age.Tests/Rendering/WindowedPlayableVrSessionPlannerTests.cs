using Rekall.Age.Rendering;

namespace Rekall.Age.Tests.Rendering;

public sealed class WindowedPlayableVrSessionPlannerTests
{
    [Fact]
    public void PlayableVrStartsHeadsetSubmitWhenHeadsetSessionIsReady()
    {
        var plan = RekallAgeWindowedPlayableVrSessionPlanner.Plan(
            openXrRequested: true,
            headsetSessionReady: true,
            playableMode: true);

        Assert.True(plan.ShouldStartHeadsetSubmit);
        Assert.True(plan.UsesWindowedInputBridge);
        Assert.Contains("playable", plan.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonPlayableVrStillStartsHeadsetSubmitWhenHeadsetSessionIsReady()
    {
        var plan = RekallAgeWindowedPlayableVrSessionPlanner.Plan(
            openXrRequested: true,
            headsetSessionReady: true,
            playableMode: false);

        Assert.True(plan.ShouldStartHeadsetSubmit);
        Assert.True(plan.UsesWindowedInputBridge);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void HeadsetSubmitRequiresOpenXrRequestAndReadySession(bool openXrRequested, bool headsetSessionReady)
    {
        var plan = RekallAgeWindowedPlayableVrSessionPlanner.Plan(
            openXrRequested,
            headsetSessionReady,
            playableMode: true);

        Assert.False(plan.ShouldStartHeadsetSubmit);
    }
}
