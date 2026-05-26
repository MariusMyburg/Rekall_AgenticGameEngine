using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public sealed record RekallAgeRuntimeSimulationClockOptions(
    double FixedStepSeconds = 1.0 / 60.0,
    double MaximumAccumulatedSeconds = 1.0,
    int MaximumStepsPerAdvance = 120)
{
    public static RekallAgeRuntimeSimulationClockOptions Default { get; } = new();
}

public sealed record RekallAgeRuntimeSimulationClockAdvanceResult(
    RekallAgeRuntimeWorld World,
    int StepsSimulated,
    double DeltaSeconds,
    double AccumulatedSeconds);

public sealed class RekallAgeRuntimeSimulationClock
{
    private readonly RekallAgeRuntimeExecutionLoop _executionLoop;
    private readonly RekallAgeRuntimeSimulationClockOptions _options;
    private double _lastClockSeconds;
    private double _accumulatorSeconds;

    public RekallAgeRuntimeSimulationClock(
        RekallAgeRuntimeExecutionLoop executionLoop,
        TimeSpan initialClock,
        RekallAgeRuntimeSimulationClockOptions? options = null)
    {
        _executionLoop = executionLoop;
        _options = options ?? RekallAgeRuntimeSimulationClockOptions.Default;
        _lastClockSeconds = Math.Max(0, initialClock.TotalSeconds);
    }

    public void Reset(TimeSpan currentClock)
    {
        _lastClockSeconds = Math.Max(0, currentClock.TotalSeconds);
        _accumulatorSeconds = 0;
    }

    public ValueTask<RekallAgeRuntimeSimulationClockAdvanceResult> AdvanceByAsync(
        RekallAgeRuntimeWorld world,
        TimeSpan elapsedSinceLastFrame,
        CancellationToken cancellationToken,
        Func<int, RekallAgeRuntimeInputState>? inputForStep = null)
    {
        return AdvanceToAsync(
            world,
            TimeSpan.FromSeconds(_lastClockSeconds + Math.Max(0, elapsedSinceLastFrame.TotalSeconds)),
            cancellationToken,
            inputForStep);
    }

    public async ValueTask<RekallAgeRuntimeSimulationClockAdvanceResult> AdvanceToAsync(
        RekallAgeRuntimeWorld world,
        TimeSpan currentClock,
        CancellationToken cancellationToken,
        Func<int, RekallAgeRuntimeInputState>? inputForStep = null)
    {
        var currentClockSeconds = Math.Max(0, currentClock.TotalSeconds);
        var deltaSeconds = Math.Clamp(
            currentClockSeconds - _lastClockSeconds,
            0,
            _options.MaximumAccumulatedSeconds);
        _lastClockSeconds = currentClockSeconds;
        _accumulatorSeconds = Math.Min(
            _options.MaximumAccumulatedSeconds,
            _accumulatorSeconds + deltaSeconds);

        var steps = 0;
        while (_accumulatorSeconds + 0.000001 >= _options.FixedStepSeconds
            && steps < _options.MaximumStepsPerAdvance)
        {
            var input = inputForStep?.Invoke(steps) ?? RekallAgeRuntimeInputState.Empty;
            world = (await _executionLoop.RunAsync(world, 1, cancellationToken, input)
                .ConfigureAwait(false)).World;
            _accumulatorSeconds = Math.Max(0, _accumulatorSeconds - _options.FixedStepSeconds);
            steps++;
        }

        return new RekallAgeRuntimeSimulationClockAdvanceResult(
            world,
            steps,
            deltaSeconds,
            _accumulatorSeconds);
    }
}
