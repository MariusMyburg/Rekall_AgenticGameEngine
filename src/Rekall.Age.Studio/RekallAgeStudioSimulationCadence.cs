using System.Diagnostics;

namespace Rekall.Age.Studio;

internal interface IRekallAgeStudioMonotonicClock
{
    TimeSpan GetTimestamp();
}

internal sealed class RekallAgeStudioStopwatchClock : IRekallAgeStudioMonotonicClock
{
    public TimeSpan GetTimestamp() => Stopwatch.GetElapsedTime(0, Stopwatch.GetTimestamp());
}

internal sealed class RekallAgeStudioSimulationCadence(IRekallAgeStudioMonotonicClock clock)
{
    private readonly IRekallAgeStudioMonotonicClock _clock = clock
        ?? throw new ArgumentNullException(nameof(clock));
    private TimeSpan _accumulated;
    private TimeSpan _lastTimestamp;

    internal void Reset()
    {
        _accumulated = TimeSpan.Zero;
        _lastTimestamp = _clock.GetTimestamp();
    }

    internal int ConsumeSimulationFrames()
    {
        var now = _clock.GetTimestamp();
        var elapsed = now - _lastTimestamp;
        _lastTimestamp = now;
        if (elapsed > TimeSpan.Zero) _accumulated += elapsed;

        var frames = Math.Min(
            RekallAgeStudioPreviewCadence.MaximumSimulationFramesPerPresentation,
            (int)(_accumulated.Ticks / RekallAgeStudioPreviewCadence.PresentationInterval.Ticks));
        _accumulated -= TimeSpan.FromTicks(
            (long)frames * RekallAgeStudioPreviewCadence.PresentationInterval.Ticks);
        return frames;
    }
}
