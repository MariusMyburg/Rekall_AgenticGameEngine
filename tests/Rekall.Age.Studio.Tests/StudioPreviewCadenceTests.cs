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

    [Fact]
    public void SimulationCadenceAdvancesOneFixedFrameForAnOnTimePresentation()
    {
        var clock = new ManualMonotonicClock();
        var cadence = new RekallAgeStudioSimulationCadence(clock);

        cadence.Reset();
        clock.Advance(RekallAgeStudioPreviewCadence.PresentationInterval);

        Assert.Equal(1, cadence.ConsumeSimulationFrames());
    }

    [Fact]
    public void SimulationCadenceCapsMissedDeadlineCatchUp()
    {
        var clock = new ManualMonotonicClock();
        var cadence = new RekallAgeStudioSimulationCadence(clock);

        cadence.Reset();
        clock.Advance(TimeSpan.FromMilliseconds(100));

        Assert.Equal(6, cadence.ConsumeSimulationFrames());

        clock.Advance(TimeSpan.FromMilliseconds(17));
        Assert.Equal(1, cadence.ConsumeSimulationFrames());
    }

    [Fact]
    public void SimulationCadenceCarriesFractionalRemainderForward()
    {
        var clock = new ManualMonotonicClock();
        var cadence = new RekallAgeStudioSimulationCadence(clock);

        cadence.Reset();
        clock.Advance(TimeSpan.FromMilliseconds(25));
        Assert.Equal(1, cadence.ConsumeSimulationFrames());

        clock.Advance(TimeSpan.FromMilliseconds(8));
        Assert.Equal(0, cadence.ConsumeSimulationFrames());

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, cadence.ConsumeSimulationFrames());
    }

    private sealed class ManualMonotonicClock : IRekallAgeStudioMonotonicClock
    {
        private TimeSpan _timestamp;

        public TimeSpan GetTimestamp() => _timestamp;

        public void Advance(TimeSpan elapsed) => _timestamp += elapsed;
    }
}
