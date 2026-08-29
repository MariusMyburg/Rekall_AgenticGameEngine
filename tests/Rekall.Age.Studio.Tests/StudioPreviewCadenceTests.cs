using Rekall.Age.Studio;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioPreviewCadenceTests
{
    [Fact]
    public void LiveSimulationPresentsOneFixedStepAtSixtyFramesPerSecond()
    {
        Assert.Equal(60, RekallAgeStudioPreviewCadence.TargetFramesPerSecond);
        Assert.Equal(
            TimeSpan.FromSeconds(1d / 60d),
            RekallAgeStudioPreviewCadence.PresentationInterval);
        Assert.Equal(1, RekallAgeStudioPreviewCadence.FramesPerPresentation);
    }
}
